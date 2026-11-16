using AdjustCeidgAddress.Data;
using AdjustCeidgAddress.Data.FirmyTemp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AdjustCeidgAddress;

public sealed class CompanyNameAdjuster
{
    public async Task<int> RunAsync(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);
        var configuration = new ConfigurationBuilder().AddUserSecrets<CompanyNameAdjuster>(optional: true).Build();
        var connection = configuration.GetConnectionString("FirmyTemp");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.Error.WriteLine("Ustaw ConnectionStrings:FirmyTemp w User Secrets ColdCall-ImportCEIDGByDate.");
            return 1;
        }

        var folder = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "Import");
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"Nie znaleziono folderu XML: {folder}");
            return 1;
        }

        await using var context = new FirmyTempDbContext(new DbContextOptionsBuilder<FirmyTempDbContext>()
            .UseSqlServer(connection, sql => sql.UseNetTopologySuite()).Options);
        await ImportAsync(context, folder);
        await ChangeCompanyNames(context);

        Console.WriteLine("Processed all.");
        Console.ReadLine();

        return 0;
    }

    public async Task<int> ChangeCompanyNames(FirmyTempDbContext context)
    {
        var query = context.AdjustNames.Where(row => !row.Processed
            && context.Companies.Any(company => company.Nip == row.Nip));
        var total = await query.CountAsync();
        var processed = 0;
        var changed = 0;
        var lastId = 0;
        Console.WriteLine($"Zmiana nazw firm: dostepne rekordy {total}.");

        while (true)
        {
            var batch = await query.Where(row => row.AdjustId > lastId)
                .OrderBy(row => row.AdjustId).Take(100).ToListAsync();
            if (batch.Count == 0)
                break;

            var ids = batch.Select(row => row.AdjustId).ToArray();
            // Join in SQL so NIP matching follows the database collation.
            var matches = await (from company in context.Companies
                                 join row in context.AdjustNames.Where(row => ids.Contains(row.AdjustId))
                                     on company.Nip equals row.Nip
                                 select new { Company = company, row.AdjustId }).ToListAsync();
            var companiesByAdjustment = matches.ToLookup(match => match.AdjustId, match => match.Company);
            var batchChanged = 0;
            foreach (var row in batch)
            {
                var companies = companiesByAdjustment[row.AdjustId].ToList();
                if (companies.Count == 0)
                    continue;

                foreach (var company in companies)
                {
                    if (!string.Equals(company.Name, row.Nazwa, StringComparison.Ordinal))
                    {
                        company.Name = row.Nazwa;
                        batchChanged++;
                    }
                }
                row.Processed = true;
            }

            // Commit company names and processing flags together before reporting progress.
            await context.SaveChangesAsync();
            processed += batch.Count(row => row.Processed);
            changed += batchChanged;
            lastId = batch[^1].AdjustId;
            var percent = total == 0 ? 100 : 100d * processed / total;
            Console.WriteLine($"Przetworzono {processed}/{total} ({percent:F2}%), zmienione nazwy: {changed}.");
            foreach (var row in batch)
                context.Entry(row).State = EntityState.Detached;
            foreach (var company in matches.Select(match => match.Company).Distinct())
                context.Entry(company).State = EntityState.Detached;
        }

        if (total == 0)
            Console.WriteLine("Przetworzono 0/0 (100,00%). Brak rekordow do przetworzenia.");
        return 0;
    }

    public async Task<int> ImportAsync(FirmyTempDbContext context, string folder)
    {
        Console.WriteLine("Czyszczenie tabeli ColdCall.AdjustName...");
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE [ColdCall].[AdjustName];");
        foreach (var entry in context.ChangeTracker.Entries<AdjustName>().ToList())
            entry.State = EntityState.Detached;
        Console.WriteLine("Importowanie rekordów NIP, Nazwa i XML do AdjustName");
        long records = 0, inserted = 0, updated = 0, skipped = 0;
        int files = 0, failedFiles = 0;
        var provinces = await context.Wojewodztwas.AsNoTracking().ToListAsync();
        foreach (var file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            files++;
            Console.WriteLine($"Plik: {Path.GetFileName(file)}");
            var wojewodztwoId = ProvinceFileName.ResolveId(file, provinces);
            if (wojewodztwoId == null)
            {
                failedFiles++;
                Console.Error.WriteLine($"Nie znaleziono wojewodztwa dla pliku: {Path.GetFileName(file)}; pominieto plik.");
                continue;
            }
            var batch = new Dictionary<string, (string Name, string Xml, int ProvinceId)>(StringComparer.Ordinal);
            try
            {
                foreach (var detail in CompanyXmlReader.Read(file))
                {
                    records++;
                    var nip = detail.Elements().FirstOrDefault(e => e.Name.LocalName == "Nip")?.Value.Trim();
                    var name = detail.Elements().FirstOrDefault(e => e.Name.LocalName == "NazwaPodmiotu")?.Value.Trim();
                    if (string.IsNullOrWhiteSpace(nip) || string.IsNullOrWhiteSpace(name)
                        || nip.Length > 50 || name.Length > 1000)
                    {
                        skipped++;
                        Console.Error.WriteLine($"Pominieto rekord {records}: brak lub niepoprawna dlugosc NIP/NazwaPodmiotu (NIP={nip}).");
                        continue;
                    }
                    // NIP is unique; the last occurrence in input order wins.
                    batch[nip] = (name, detail.ToString(SaveOptions.DisableFormatting), wojewodztwoId.Value);
                    if (batch.Count >= 1000)
                        await FlushAsync(batch);
                }
                await FlushAsync(batch);
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                failedFiles++;
                Console.Error.WriteLine($"Blad pliku {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        Console.WriteLine($"Pliki: {files}, bledne: {failedFiles}, XML: {records}, dodane: {inserted}, zaktualizowane: {updated}, pominiete: {skipped}.");
        if (files == 0) Console.WriteLine($"Brak plikow .xml w {folder}");
        return failedFiles > 0 ? 1 : 0;

        async Task FlushAsync(Dictionary<string, (string Name, string Xml, int ProvinceId)> batch)
        {
            if (batch.Count == 0) return;
            var nips = batch.Keys.ToArray();
            var existing = await context.AdjustNames.Where(row => nips.Contains(row.Nip))
                .ToDictionaryAsync(row => row.Nip, StringComparer.Ordinal);
            long added = 0, changed = 0;
            foreach (var (nip, record) in batch)
            {
                if (!existing.TryGetValue(nip, out var row))
                {
                    row = new AdjustName { Nip = nip };
                    context.AdjustNames.Add(row);
                    added++;
                }
                else changed++;
                row.Nazwa = record.Name;
                row.CompanyXml = record.Xml;
                row.WojewodztwoId = record.ProvinceId;
                row.Processed = false;
                row.Adjusted = false;
                row.DateModified = DateTime.Now;
            }
            await context.SaveChangesAsync();
            inserted += added;
            updated += changed;
            context.ChangeTracker.Clear();
            batch.Clear();
            Console.WriteLine($"AdjustName: dodane {inserted}, zaktualizowane {updated}.");
        }
    }
}
