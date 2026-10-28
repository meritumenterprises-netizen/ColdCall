using GeoPortalAddressImporter;
using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.HPRtree;
using System.Data;

namespace ImportCEIDGByDate;

public sealed class CeidgCompanyAddressUpdater : IDisposable
{
    private readonly FirmyTempDbContext context;
    private readonly HttpClient http;
    private readonly GeoportalAddressService geoportal;
    private Statistics Statistics { get; set; }
    private Dictionary<int, string> provinces = new();
    private Dictionary<int, string> counties = new();
    private Dictionary<int, string> municipalities = new();
    private Dictionary<int, string> cities = new();

    public CeidgCompanyAddressUpdater(FirmyTempDbContext context, Statistics statistics)
        : this(context, new HttpClient { Timeout = TimeSpan.FromSeconds(45) },
            Path.Combine(AppContext.BaseDirectory, "geoportal"), statistics)
    {
        this.Statistics = statistics;
    }

    public CeidgCompanyAddressUpdater(FirmyTempDbContext context, HttpClient httpClient, string dataDirectory, Statistics statistics)
    {
        this.context = context;
        http = httpClient;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ImportCEIDGByDate/1.0");
        geoportal = new GeoportalAddressService(http, dataDirectory);
        this.Statistics = statistics;
    }

    public async Task<IReadOnlyList<bool>> UpdateBatchAsync(IReadOnlyList<Company> companies, CancellationToken token = default,
        AddressImportProgress? progress = null)
    {
        if (companies.Count > 100) throw new ArgumentOutOfRangeException(nameof(companies));
        if (companies.Count == 0) return Array.Empty<bool>();
        var provinceIds = companies.Select(company => company.WojewodztwoId).ToArray();
        var countyIds = companies.Select(company => company.PowiatId).ToArray();
        var municipalityIds = companies.Select(company => company.GminaId).ToArray();
        var cityIds = companies.Select(company => company.MiejscowoscId).ToArray();
        provinces = await context.Wojewodztwas.AsNoTracking().Where(row => provinceIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Name, token);
        counties = await context.Powiaties.AsNoTracking().Where(row => countyIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Name, token);
        municipalities = await context.Gminies.AsNoTracking().Where(row => municipalityIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Name, token);
        cities = await context.Miejscowoscis.AsNoTracking().Where(row => cityIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, row => row.Name, token);
        var results = new bool[companies.Count];
        var preparedByIndex = new PreparedCompanyAddress?[companies.Count];
        var skippedMessages = new string?[companies.Count];
        var addressesByIndex = new Adresy?[companies.Count];
        var total = progress?.Total ?? companies.Count;
        var processedBeforeBatch = progress?.Processed ?? 0;
        var resolvedCount = processedBeforeBatch;
        var progressLock = new object();
        // Only HTTP preparation is parallel; the shared DbContext stays on the serial path.
        await Parallel.ForEachAsync(Enumerable.Range(0, companies.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = token }, async (i, cancellationToken) =>
        {
            var item = await PrepareAsync(companies[i], cancellationToken, message => skippedMessages[i] = message);
            if (item != null) { item.Index = i; preparedByIndex[i] = item; }
            // Serialize increment and output so concurrent completions print increasing percentages.
            lock (progressLock)
            {
                var completed = Interlocked.Increment(ref resolvedCount);
                var percent = total == 0 ? 100 : 100d * completed / total;
                Console.WriteLine($"GeoPortal: sprawdzono {percent:F2}% ({completed}/{total}); firma {companies[i].Id}: " +
                    (item != null ? item.Result.Reason : skippedMessages[i]));
            }
        });
        var prepared = preparedByIndex.OfType<PreparedCompanyAddress>().ToList();
        if (prepared.Count > 0)
        {
            // Resolve all network calls before opening the batch transaction.
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var companyIds = prepared.Select(item => item.Company.Id).ToArray();
            var links = await context.CompanyAddresses.Include(link => link.Address)
                .Where(link => companyIds.Contains(link.CompanyId)).ToListAsync(token);
            var linksByCompany = links.ToLookup(link => link.CompanyId);
            var sourceKeys = prepared.Select(item => item.Result.SourceKey).Distinct().ToArray();
            var knownAddresses = await context.Adresies
                .Where(address => sourceKeys.Contains(address.PrngId)).ToListAsync(token);
            foreach (var item in prepared)
                addressesByIndex[item.Index] = Apply(item, linksByCompany[item.Company.Id].ToList(), knownAddresses);
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            foreach (var item in prepared) results[item.Index] = true;
        }
        // Log in input order after commit so skips and parallel completion cannot move progress backwards.
        for (var i = 0; i < companies.Count; i++)
        {
            var completed = processedBeforeBatch + i + 1;
            var percent = total == 0 ? 100 : 100d * completed / total;
            if (results[i])
            {
                var reason = preparedByIndex[i]!.Result.Reason;
                if (reason.Length > 0) reason = char.ToLowerInvariant(reason[0]) + reason[1..];
                Console.WriteLine($"GeoPortal: firma {companies[i].Id} {percent:F2}%: zapisano adres {addressesByIndex[i]!.Id}: {reason}");
            }
            else
            {
                Console.WriteLine($"GeoPortal: firma {companies[i].Id} {percent:F2}%: {skippedMessages[i]}");
            }
        }
        return results;
    }

    private string StreetExpander ( string? street )
    {
        if (string.IsNullOrWhiteSpace(street)) return "";
        if (street.StartsWith("ul."))
        {
            return "Ulica " + street.Substring(4).Trim();
        }
        if (street.StartsWith("al."))
        {
            return "Aleja " + street.Substring(4).Trim();
        }
        return street;
    }

    private async Task<PreparedCompanyAddress?> PrepareAsync(Company company, CancellationToken token, Action<string> reportSkipped)
    {
        if (company.BlockedByRodo || company.SkipAddressVerification)
        {
            reportSkipped("RODO lub wylaczona weryfikacja adresu; pominieto adres.");
            return null;
        }
        var request = new GeoportalAddressRequest
        {
            CompanyId = company.Id,
            ProvinceId = company.WojewodztwoId ?? 0,
            CountyId = company.PowiatId ?? 0,
            MunicipalityId = company.GminaId ?? 0,
            CityId = company.MiejscowoscId ?? 0,
            Province = provinces.GetValueOrDefault(company.WojewodztwoId ?? 0, ""),
            County = counties.GetValueOrDefault(company.PowiatId ?? 0, ""),
            Municipality = municipalities.GetValueOrDefault(company.GminaId ?? 0, ""),
            City = cities.GetValueOrDefault(company.MiejscowoscId ?? 0, ""),
            Street = StreetExpander(company.Street), 
            BuildingNumber = company.BuildingNumber,
            PostalCode = company.PostalCode
        };

        var result = await geoportal.FindAsync(request, token);
        if (result.ErrorCode != GeoportalAddressErrorCode.None)
        {
            reportSkipped($"{result.ErrorCode}: {result.Reason}; dotychczasowy adres bez zmian.");
            return null;
        }
        if (!result.Found)
        {
            reportSkipped($"{result.Reason}; pominieto adres.");
            return null;
        }

        return new PreparedCompanyAddress { Company = company, Request = request, Result = result };
    }

    private Adresy Apply(PreparedCompanyAddress item, List<CompanyAddress> links, List<Adresy> knownAddresses)
    {
        var company = item.Company;
        var request = item.Request;
        var result = item.Result;
        var address = links.FirstOrDefault()?.Address
            ?? context.Adresies.Local.FirstOrDefault(x => x.PrngId == result.SourceKey)
            ?? knownAddresses.FirstOrDefault(x => x.PrngId == result.SourceKey);
        if (address == null)
        {
            address = new Adresy();
            context.Adresies.Add(address);
        }

        address.WojewodztwoId = request.ProvinceId;
        address.PowiatId = request.CountyId;
        address.GminaId = request.MunicipalityId;
        address.MiejscowoscId = request.CityId;
        address.Ulica = result.Street;
        address.NrUlicy = result.BuildingNumber;
        address.Rodzaj = result.IsCity ? "miejscowosc UUG (GPS przyblizony)" : "punkt adresowy UUG";
        address.Latitude = result.Latitude;
        address.Longitude = result.Longitude;
        address.Location = new Point((double)result.Longitude, (double)result.Latitude) { SRID = 4326 };
        address.PrngId = result.SourceKey;

        if (links.Count == 0)
            context.CompanyAddresses.Add(new CompanyAddress { Company = company, Address = address });
        else
        {
            links[0].Address = address;
        }
        company.AddressIsMissing = result.IsCity;
        return address;
    }

    public void Dispose()
    {
        geoportal.Dispose();
        http.Dispose();
    }
}
