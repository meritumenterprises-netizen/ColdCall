namespace ImportCeidgXml;

public sealed class CompanyImportService(CeidgXmlReader reader, CompanyRepository repository, ImportLog log)
{
    public async Task ImportAsync(ImportOptions options, CancellationToken token)
    {
        var files = Directory.GetFiles(options.Folder, "*.xml").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("Brak plikow XML w podanym folderze.");
        log.Write(options.Apply ? "APPLY: zapis firm i przypisan PKD." : "DRY RUN: walidacja XML i PKD, bez zapisu.");
        await repository.OpenAsync(token);
        var checkpointStore = options.Apply ? new ImportCheckpointStore(options.Folder, options.ConnectionString) : null;
        var checkpoint = checkpointStore == null ? null
            : await checkpointStore.LoadOrCreateAsync(files, options.Restart, token);
        var startFile = 0;
        if (checkpoint != null)
        {
            log.Write($"Checkpoint: {checkpointStore!.FilePath}");
            if (checkpoint.Completed)
            {
                log.Write("Import tych plikow zostal juz zakonczony. Uzyj --restart, aby przetworzyc je ponownie.");
                return;
            }
            startFile = Array.FindIndex(files, file => string.Equals(file, checkpoint.FilePath, StringComparison.OrdinalIgnoreCase));
            if (checkpoint.FileCompleted) startFile++;
            // Persist the starting position before performing any writes (also resets --restart).
            await checkpointStore.SaveAsync(checkpoint, token);
        }

        async Task SaveProgressAsync(string file, long position, bool fileCompleted, bool completed)
        {
            if (checkpoint == null) return;
            await repository.CommitBatchAsync(token);
            checkpoint.FilePath = file;
            checkpoint.RecordsCommitted = position;
            checkpoint.FileCompleted = fileCompleted;
            checkpoint.Completed = completed;
            // Once SQL has committed, Ctrl+C must not cancel checkpoint persistence.
            await checkpointStore!.SaveAsync(checkpoint, CancellationToken.None);
        }

        long read = 0;
        var inserted = 0;
        var updated = 0;
        var inactive = 0;
        var noNip = 0;
        for (var fileIndex = startFile; fileIndex < files.Length; fileIndex++)
        {
            var file = files[fileIndex];
            var position = checkpoint != null && string.Equals(file, checkpoint.FilePath, StringComparison.OrdinalIgnoreCase)
                ? checkpoint.RecordsCommitted : 0;
            await SaveProgressAsync(file, position, false, false);
            var wojewodztwo = Path.GetFileNameWithoutExtension(file).EndsWith("brak województwa", StringComparison.OrdinalIgnoreCase)
                ? "Brak województwa" : CeidgXmlReader.ProvinceFromFile(file)
                ?? throw new InvalidDataException($"Brak wojewodztwa w nazwie pliku: {Path.GetFileName(file)}");
            int? wojewodztwoId = repository.FindProvinceId(wojewodztwo);
            log.Write($"Plik: {Path.GetFileName(file)}; wojewodztwo={wojewodztwo}; Id={wojewodztwoId?.ToString() ?? "dry-run"}");
            if (position > 0) log.Write($"Wznawiam od rekordu {position + 1}; zatwierdzono juz {position} rekordow tego pliku.");
            var limitReached = false;
            await foreach (var company in reader.ReadAsync(file, position, token))
            {
                read++;
                position++;
                var companyFromFile = company with
                {
                    Province = wojewodztwo,
                    WojewodztwoId = wojewodztwoId
                };

                if (!string.IsNullOrWhiteSpace(company.Nip) && company.PhoneWasRejected)
                    log.Write($"NIP={company.Nip}: nie rozpoznano numeru w za dlugim polu Telefon; zachowano istniejacy telefon (NULL dla nowej firmy). Pelny tekst pozostaje w XML zrodlowym (CompanyXml w --apply).");
                else if (!string.IsNullOrWhiteSpace(company.Nip) && company.PhoneWasNormalized)
                    log.Write($"NIP={company.Nip}: oczyszczono pole Telefon; pelna wartosc pozostaje w XML zrodlowym (CompanyXml w --apply).");

                if (string.IsNullOrWhiteSpace(company.Nip))
                {
                    noNip++;
                }
                else if (options.Apply)
                {
                    if (await repository.InsertIfMissingAsync(companyFromFile, token)) inserted++;
                    else updated++;
                    await repository.UpdateCompanyXmlAsync(companyFromFile, token);
                }
                else
                {
                    await repository.ValidatePkdsAsync(companyFromFile, token);
                }

                if (read % 500 == 0)
                {
                    await SaveProgressAsync(file, position, false, false);
                    log.Write($"Odczytano={read}; pozycja w pliku={position}; dodano={inserted}; zaktualizowano={updated}");
                }
                if (options.Limit is int limit && read >= limit)
                {
                    limitReached = true;
                    break;
                }
            }
            await SaveProgressAsync(file, position, !limitReached, !limitReached && fileIndex == files.Length - 1);
            if (limitReached) break;
        }
        log.Write($"KONIEC: odczytano={read}; dodano={inserted}; zaktualizowano={updated}; nieaktywne={inactive}; bez NIP={noNip}");
    }
}
