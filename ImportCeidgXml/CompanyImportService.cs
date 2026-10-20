namespace ImportCeidgXml;

public sealed class CompanyImportService(CeidgXmlReader reader, CompanyRepository repository, ImportLog log)
{
    public async Task ImportAsync(ImportOptions options, CancellationToken token)
    {
        var files = Directory.GetFiles(options.Folder, "*.xml").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("Brak plikow XML w podanym folderze.");
        log.Write(options.Apply ? "APPLY: zapis firm i przypisan PKD." : "DRY RUN: walidacja XML i PKD, bez zapisu.");
        await repository.OpenAsync(token);
        var read = 0;
        var inserted = 0;
        var existing = 0;
        var inactive = 0;
        var noNip = 0;
        foreach (var file in files)
        {
            var wojewodztwo = Path.GetFileNameWithoutExtension(file).EndsWith("brak województwa", StringComparison.OrdinalIgnoreCase)
                ? "Brak województwa" : CeidgXmlReader.ProvinceFromFile(file)
                ?? throw new InvalidDataException($"Brak wojewodztwa w nazwie pliku: {Path.GetFileName(file)}");
            int? wojewodztwoId = repository.FindProvinceId(wojewodztwo);
            log.Write($"Plik: {Path.GetFileName(file)}; wojewodztwo={wojewodztwo}; Id={wojewodztwoId?.ToString() ?? "dry-run"}");
            await foreach (var company in reader.ReadAsync(file, token))
            {
                read++;
                var companyFromFile = company with
                {
                    Province = wojewodztwo,
                    WojewodztwoId = wojewodztwoId
                };

                if (string.IsNullOrWhiteSpace(company.Nip))
                {
                    noNip++;
                    var matchingPersonExists = await repository.ExistsWithoutNipAsync(
                        company.FirstName, company.LastName, token);
                    if (matchingPersonExists)
                        log.Write($"Firma bez NIP o tym imieniu i nazwisku juz istnieje; dodaje mimo dopasowania. Rekord={read}");
                }
                if (options.Apply)
                {
                    if (await repository.InsertIfMissingAsync(companyFromFile, token)) inserted++;
                    else existing++;
                }
                else
                {
                    await repository.ValidatePkdsAsync(companyFromFile, token);
                }

                if (read % 500 == 0)
                {
                    if (options.Apply) await repository.CommitBatchAsync(token);
                    log.Write($"Odczytano={read}; dodano={inserted}");
                }
                if (options.Limit is int limit && read >= limit) break;
            }
            if (options.Limit is int maximum && read >= maximum) break;
        }
        if (options.Apply) await repository.CommitBatchAsync(token);
        log.Write($"KONIEC: odczytano={read}; dodano={inserted}; istnieje={existing}; nieaktywne={inactive}; bez NIP={noNip}");
    }
}
