using AdjustCeidgAddress.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using AdjustCeidgAddress.Data.FirmyTemp;
using System.Text;
using System.Collections.Concurrent;

namespace AdjustCeidgAddress;

public sealed class AddressAdjuster
{
    public async Task<int> RunAsync(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<AddressAdjuster>(optional: true)
            .Build();

        var firmyTempConnection = configuration.GetConnectionString("FirmyTemp");
        var firmyImportConnection = configuration.GetConnectionString("FirmyImport");
        if (string.IsNullOrWhiteSpace(firmyTempConnection) || string.IsNullOrWhiteSpace(firmyImportConnection))
        {
            Console.Error.WriteLine("Ustaw ConnectionStrings:FirmyTemp i ConnectionStrings:FirmyImport w User Secrets ColdCall-ImportCEIDGByDate.");
            return 1;
        }

        var contextOptions = new DbContextOptionsBuilder<FirmyTempDbContext>()
            .UseSqlServer(firmyTempConnection, sql => sql.UseNetTopologySuite()).Options;
        using var firmyTemp = new FirmyTempDbContext(contextOptions);
        using var firmyImport = new FirmyImportDbContext(
            new DbContextOptionsBuilder<FirmyImportDbContext>()
                .UseSqlServer(firmyImportConnection).Options);

        var importFolder = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "Import");
        if (!Directory.Exists(importFolder))
        {
            Console.Error.WriteLine($"Nie znaleziono folderu XML: {importFolder}");
            return 1;
        }

        Console.WriteLine("Ładowanie Companies z WojewodztwoId = 1...");
        var companies = await firmyTemp.Companies.AsNoTracking()
            .Where(company => company.WojewodztwoId == 1).ToListAsync();
        var nips = companies.Where(company => !string.IsNullOrWhiteSpace(company.Nip))
            .Select(company => company.Nip!.Trim()).ToHashSet(StringComparer.Ordinal);
        Console.WriteLine($"Zaladowano {companies.Count} firm, {nips.Count} unikalnych NIP-ow.");
        var wojewodztwa = await firmyTemp.Wojewodztwas.AsNoTracking().ToListAsync();
        using var geoHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        geoHttp.DefaultRequestHeaders.UserAgent.ParseAdd("AdjustCeidgAddress/1.0");
        using var geoportal = new ImportCEIDGByDate.Geoportal.GeoportalAddressService(
            geoHttp, Path.Combine(AppContext.BaseDirectory, "geoportal"));
        using var locationResolver = new LocationResolver(() => new FirmyTempDbContext(contextOptions));
        var companyIdsByNip = companies.Where(company => !string.IsNullOrWhiteSpace(company.Nip))
            .GroupBy(company => company.Nip!.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);

        long recordCount = 0;
        long matchCount = 0;
        long changedCount = 0;

        var matchedNips = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var fileCount = 0;
        var failedFiles = 0;
        foreach (var file in Directory.EnumerateFiles(importFolder, "Zarejestrowane działalności*.xml", SearchOption.TopDirectoryOnly))
        {
            fileCount++;
            Console.WriteLine($"Plik: {Path.GetFileName(file)}");
            try
            {
                var wojewodztwoId = ProvinceFileName.ResolveId(file, wojewodztwa);
                if (wojewodztwoId == null)
                {
                    failedFiles++;
                    Console.Error.WriteLine($"Nie znaleziono wojewodztwa w tabeli Wojewodztwa dla pliku: {Path.GetFileName(file)}");
                    continue;
                }
                Console.WriteLine($"WojewodztwoId={wojewodztwoId.Value}");
                var records = CompanyXmlReader.Read(file).ToList();
                recordCount += records.Count;
                var groups = records.Select(detail => new
                {
                    Detail = detail,
                    Nip = detail.Elements().FirstOrDefault(element => element.Name.LocalName == "Nip")?.Value.Trim()
                })
                    .Where(record => !string.IsNullOrWhiteSpace(record.Nip) && nips.Contains(record.Nip))
                    .GroupBy(record => record.Nip!, StringComparer.Ordinal).ToList();
                // Preserve XML order within each NIP; only independent companies run in parallel.
                await Parallel.ForEachAsync(groups, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (group, token) =>
                {
                    await using var workerContext = new FirmyTempDbContext(contextOptions);
                    var company = await workerContext.Companies
                        .Include(row => row.Miejscowosc).Include(row => row.Powiat).Include(row => row.Gmina)
                        .SingleOrDefaultAsync(row => row.Id == companyIdsByNip[group.Key], token);
                    if (company == null)
                    {
                        Console.Error.WriteLine($"Pominieto usunieta firme NIP={group.Key}.");
                        return;
                    }
                    var addressUpdater = new CompanyGeoAddressUpdater(workerContext, geoportal);
                    foreach (var record in group)
                    {
                        var xmlCompanyDetail = record.Detail;
                        var nip = group.Key;

                        Interlocked.Increment(ref matchCount);

                        matchedNips.TryAdd(nip, 0);
                        bool changedAddressData = false;

                        var name = xmlCompanyDetail.Elements().FirstOrDefault(element => element.Name.LocalName == "NazwaPodmiotu")?.Value;
                        Console.WriteLine($"Znaleziono NIP={nip}, firma={name}");

                        var message = new StringBuilder();
                        bool changedProvince = company.WojewodztwoId != wojewodztwoId;
                        company.WojewodztwoId = wojewodztwoId;

                        message.Append($"Zmieniono województwo: {wojewodztwa.FirstOrDefault((w) => w.Id == wojewodztwoId)?.Name}.\r\n");

                        var miejscowosc = xmlCompanyDetail.Elements().FirstOrDefault(element => element.Name.LocalName == "Miejscowosc")?.Value.Trim();
                        if (!string.IsNullOrWhiteSpace(miejscowosc) && miejscowosc != company.Miejscowosc?.Name)
                        {
                            company.MiejscowoscId = await locationResolver.ResolveAsync<Miejscowosci>(miejscowosc, token);
                            message.Append($"Zmieniono miejscowość: {miejscowosc}.\r\n");
                            changedAddressData = true;
                        }

                        var postalCode = xmlCompanyDetail.Elements().FirstOrDefault(element => element.Name.LocalName == "KodPocztowy")?.Value.Trim();
                        if (!string.IsNullOrWhiteSpace(postalCode) && postalCode != company.PostalCode)
                        {
                            company.PostalCode = postalCode;
                            message.Append($"Zmieniono kod pocztowy: {postalCode}.\r\n");
                            changedAddressData = true;
                        }

                        var powiat = xmlCompanyDetail.Elements().FirstOrDefault(element => element.Name.LocalName == "Powiat")?.Value.Trim();
                        if (!string.IsNullOrWhiteSpace(powiat) && powiat != company.Powiat?.Name)
                        {
                            company.PowiatId = await locationResolver.ResolveAsync<Powiaty>(powiat, token);
                            message.Append($"Zmieniono powiat: {powiat}.\r\n");
                            changedAddressData = true;
                        }

                        var gmina = xmlCompanyDetail.Elements().FirstOrDefault(element => element.Name.LocalName == "Gmina")?.Value.Trim();
                        if (!string.IsNullOrWhiteSpace(gmina) && gmina != company.Gmina?.Name)
                        {
                            company.GminaId = await locationResolver.ResolveAsync<Gminy>(gmina, token);
                            message.Append($"Zmieniono gminę: {gmina}.\r\n");
                            changedAddressData = true;
                        }

                        var street = xmlCompanyDetail.Elements().FirstOrDefault(element => element.Name.LocalName == "Ulica")?.Value.Trim();
                        if (!string.IsNullOrWhiteSpace(street) && street != company.Street)
                        {
                            company.Street = street;
                            message.Append($"Zmieniono ulicę: {street}.\r\n");
                            changedAddressData = true;
                        }

                        var buildingNumber = xmlCompanyDetail.Elements().FirstOrDefault(element => element.Name.LocalName == "NrBudynku")?.Value.Trim();
                        if (!string.IsNullOrWhiteSpace(buildingNumber) && buildingNumber != company.BuildingNumber)
                        {
                            company.BuildingNumber = buildingNumber;
                            message.Append($"Zmieniono numer budynku: {buildingNumber}.\r\n");
                            changedAddressData = true;
                        }

                        var apartmentNumber = xmlCompanyDetail.Elements().FirstOrDefault(element => element.Name.LocalName == "NrLokalu")?.Value.Trim();
                        if (!string.IsNullOrWhiteSpace(apartmentNumber) && apartmentNumber != company.ApartmentNumber)
                        {
                            company.ApartmentNumber = apartmentNumber;
                            message.Append($"Zmieniono numer lokalu: {apartmentNumber}.\r\n");
                            changedAddressData = true;
                        }

                        if (changedAddressData || changedProvince)
                        {
                            await addressUpdater.UpdateAsync(company, token);
                            var cityName = await workerContext.Miejscowoscis.AsNoTracking()
                                .Where(row => row.Id == company.MiejscowoscId)
                                .Select(row => row.Name).FirstOrDefaultAsync(token);
                            company.AddressSort = CompanyAddressSort.Build(company, cityName ?? "");
                            Console.WriteLine($"NIP: {company.Nip}\r\n" + message.ToString());
                            await workerContext.SaveChangesAsync(token);
                            Interlocked.Increment(ref changedCount);
                        }

                    }
                });
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
            {
                failedFiles++;
                Console.Error.WriteLine($"Blad odczytu {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        Console.WriteLine($"Pliki: {fileCount}, bledne: {failedFiles}, rekordy XML: {recordCount}, dopasowania: {matchCount}, zapisane zmiany: {changedCount}, unikalne dopasowane NIP-y: {matchedNips.Count}.");
        Console.WriteLine("Przetwarzanie adresow zakonczone.");
        if (fileCount == 0)
            Console.WriteLine($"Brak plikow .xml w {importFolder}");
        if (failedFiles > 0)
            return 1;
        return 0;
    }
}
