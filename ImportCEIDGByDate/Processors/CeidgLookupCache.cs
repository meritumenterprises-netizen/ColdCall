using System.Xml;
using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate;

public sealed class CeidgLookupCache(FirmyTempDbContext context)
{
    private bool loaded;
    private Dictionary<string, Powiaty> counties = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Gminy> municipalities = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Miejscowosci> cities = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, string> provinces = new();
    private Dictionary<string, byte> statuses = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int[]> pkds = new(StringComparer.OrdinalIgnoreCase);

    public async Task PrepareBatchAsync(IEnumerable<string> xmls)
    {
        if (!loaded)
        {
            provinces = await context.Wojewodztwas.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
            counties = (await context.Powiaties.AsNoTracking().OrderBy(x => x.Id).ToListAsync())
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            municipalities = (await context.Gminies.AsNoTracking().OrderBy(x => x.Id).ToListAsync())
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            cities = (await context.Miejscowoscis.AsNoTracking().OrderBy(x => x.Id).ToListAsync())
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            statuses = (await context.CompanyStatuses.AsNoTracking().ToListAsync())
                .ToDictionary(x => x.Nazwa, x => x.StatusId, StringComparer.OrdinalIgnoreCase);
            pkds = (await context.PkdCodes.AsNoTracking().Select(x => new { x.Id, x.CodeWithoutDots }).ToListAsync())
                .Where(x => !string.IsNullOrWhiteSpace(x.CodeWithoutDots))
                .GroupBy(x => NormalizePkd(x.CodeWithoutDots!), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToArray(), StringComparer.OrdinalIgnoreCase);
            loaded = true;
        }
        var changed = false;
        foreach (var xml in xmls)
        {
            var document = new XmlDocument(); document.LoadXml(xml);
            var county = document.GetInnerTextOrEmpty("Powiat");
            if (document.HasElement("Powiat") && !counties.ContainsKey(county))
            {
                var row = new Powiaty { Name = county }; counties.Add(county, row); context.Powiaties.Add(row); changed = true;
            }
            var municipality = document.GetInnerTextOrEmpty("Gmina");
            if (document.HasElement("Gmina") && !municipalities.ContainsKey(municipality))
            {
                var row = new Gminy { Name = municipality }; municipalities.Add(municipality, row); context.Gminies.Add(row); changed = true;
            }
            var city = document.GetInnerTextOrEmpty("Miejscowosc");
            if (document.HasElement("Miejscowosc") && !cities.ContainsKey(city))
            {
                var row = new Miejscowosci { Name = city }; cities.Add(city, row); context.Miejscowoscis.Add(row); changed = true;
            }
        }
        if (changed) await context.SaveChangesAsync();
    }

    public int CountyId(string name) => counties[name].Id;
    public int MunicipalityId(string name) => municipalities[name].Id;
    public int CityId(string name) => cities[name].Id;
    public byte StatusId(string name) => statuses.TryGetValue(name, out var id) ? id
        : throw new InvalidDataException($"Unknown company status: {name}");
    public int PkdId(string code) => pkds.TryGetValue(NormalizePkd(code), out var ids) && ids.Length == 1 ? ids[0]
        : throw new InvalidDataException($"Unknown or ambiguous PKD code: {code}");
    public string ProvinceName(int? id) => id.HasValue && provinces.TryGetValue(id.Value, out var name) ? name : "";
    public string CountyName(int? id) => counties.Values.FirstOrDefault(x => x.Id == id)?.Name ?? "";
    public string MunicipalityName(int? id) => municipalities.Values.FirstOrDefault(x => x.Id == id)?.Name ?? "";
    public string CityName(int? id) => cities.Values.FirstOrDefault(x => x.Id == id)?.Name ?? "";
    public static string NormalizePkd(string code) => code.Trim().Replace(".", "").ToUpperInvariant();
}
