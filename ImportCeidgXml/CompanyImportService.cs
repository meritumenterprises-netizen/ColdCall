namespace ImportCeidgXml;

public sealed class CompanyImportService(CeidgXmlReader reader, CompanyRepository repository, ImportLog log)
{
    public async Task ImportAsync(ImportOptions options, CancellationToken token)
    {
        var files = Directory.GetFiles(options.Folder, "*.xml").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("Brak plikow XML w podanym folderze.");
        log.Write(options.Apply ? "APPLY: zapis nowych firm do Companies." : "DRY RUN: walidacja XML, bez zapisu SQL.");
        if (options.Apply) await repository.OpenAsync(token);
        var read = 0;
        var inserted = 0;
        var existing = 0;
        var inactive = 0;
        var noNip = 0;
        foreach (var file in files)
        {
            log.Write($"Plik: {Path.GetFileName(file)}");
            await foreach (var company in reader.ReadAsync(file, token))
            {
                read++;
                if (company.Status != "Aktywny") inactive++;
                else if (company.Nip is null)
                {
                    noNip++;
                    log.Write($"SKIP bez NIP: plik={Path.GetFileName(file)}, rekord={read}, nazwa={company.Name}");
                }
                else if (options.Apply)
                {
                    if (await repository.InsertIfMissingAsync(company, token)) inserted++;
                    else existing++;
                }
                if (read % 500 == 0)
                {
                    if (options.Apply) await repository.CommitBatchAsync(token);
                    log.Write($"Odczytano={read}; dodano={inserted}; istnieje={existing}; nieaktywne={inactive}; bez NIP={noNip}");
                }
                if (options.Limit is int limit && read >= limit) break;
            }
            if (options.Limit is int maximum && read >= maximum) break;
        }
        if (options.Apply) await repository.CommitBatchAsync(token);
        log.Write($"KONIEC: odczytano={read}; dodano={inserted}; istnieje={existing}; nieaktywne={inactive}; bez NIP={noNip}");
    }
}
