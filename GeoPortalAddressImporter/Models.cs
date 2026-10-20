using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace GeoPortalAddressImporter;

internal sealed record Company(
    int Id, int ProvinceId, int CityId, string Province, string City,
    string? Street, string? Number, string? PostalCode, int? CountyId = null, int? MunicipalityId = null,
    string? County = null, string? Municipality = null)
{
    public string LocationDescription => $"Wojew\u00f3dztwo: {Province}; powiat: {County}; gmina: {Municipality}; miejscowo\u015b\u0107: {City}";
}

internal sealed record Address(
    string Province, string County, string Municipality, string City,
    string? Street, string Number, string PostalCode, string Teryt,
    string Simc, string Ulic, double X, double Y, bool IsCity = false)
{
    public string SourceKey => IsCity
        ? "UUG:C:" + Text.Hash(Teryt + "|" + Simc)[..44]
        : "UUG:" + Text.Hash(Teryt + "|" + Simc + "|" + Ulic + "|" + Text.Number(Number))[..46];

    public string SourceKeyFor(Company company) => "UUG:CO:" + Text.Hash(
        SourceKey + "|" + company.ProvinceId + "|" + company.CountyId + "|"
        + company.MunicipalityId + "|" + company.CityId)[..43];
}

internal sealed record MatchResult(Address? Address, string Reason);
internal sealed record ApiResult(string Type, int Found, int Returned, IReadOnlyList<ApiPoint> Points);
internal sealed record ApiPoint(Address Address, string Geometry, double? Accuracy, double? CityAccuracy, double? StreetAccuracy);

internal static partial class Text
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Normalize(string? value) =>
        Spaces().Replace((value ?? "").Normalize(NormalizationForm.FormKC).Trim(), " ").ToUpperInvariant();
    public static string Street(string? value)
    {
        var result = Normalize(value);
        while (true)
        {
            var next = Prefix().Replace(result, "");
            if (next == result) return result;
            result = next;
        }
    }
    public static string Number(string? value) => Spaces().Replace(Normalize(value), "");
    public static string Province(string value) => Normalize(Normalize(value)
        .Replace("WOJEWÓDZTWO ", "", StringComparison.Ordinal).Replace('-', ' '));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"^(?:ULICA|UL|ALEJA|ALEJE|AL|PLAC|PL|OSIEDLE|OS)(?:[.:]\s*|\s+)")]
    private static partial Regex Prefix();
    [GeneratedRegex(@"^\d{2}-\d{3}$")]
    public static partial Regex Postal();
}

internal static class ApiParser
{
    public static ApiResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Odpowiedz UUG nie jest obiektem JSON.");
        var type = Get(root, "type");
        if (!string.IsNullOrWhiteSpace(Get(root, "error")) || !string.IsNullOrWhiteSpace(Get(root, "errors")))
            throw new InvalidDataException("UUG zwrocil blad zamiast wyniku wyszukiwania.");
        // GetAddress may fall back to a street/city response without returned objects.
        if (type == "street") return new ApiResult(type, 0, 0, Array.Empty<ApiPoint>());
        if (type is not ("address" or "city")) throw new InvalidDataException($"Nieznany typ odpowiedzi UUG: '{type}'.");
        if (!root.TryGetProperty("results", out var results))
            throw new InvalidDataException("Brak pola results w odpowiedzi UUG.");
        var empty = results.ValueKind == JsonValueKind.Null ||
            (results.ValueKind == JsonValueKind.Object && !results.EnumerateObject().Any());
        // UUG reports found objects=null for some searches with an explicit zero returned count.
        var confirmedEmpty = empty && int.TryParse(Get(root, "returned objects"), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var explicitReturned) && explicitReturned == 0;
        var found = confirmedEmpty && string.IsNullOrWhiteSpace(Get(root, "found objects"))
            ? 0 : Count(root, "found objects");
        var returned = !root.TryGetProperty("returned objects", out _) && (type == "city" || found == 0 && empty)
            ? found : Count(root, "returned objects");
        var points = new List<ApiPoint>();
        if (results.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in results.EnumerateObject())
            {
                var p = entry.Value;
                var units = ParseUnits(Get(p, "jednostka"));
                if (units.Count != 4 || Text.Normalize(units[0]) != "POLSKA")
                    throw new InvalidDataException("Nieznany podzial administracyjny UUG.");
                var address = new Address(
                    units[1], units[2], units[3], Get(p, "city"), Null(p, "street"),
                    Get(p, "number"), Get(p, "code"), Get(p, "teryt"), Get(p, "simc"),
                    Get(p, "ulic"), RequiredDouble(p, "x"), RequiredDouble(p, "y"), type == "city");
                points.Add(new ApiPoint(address, Get(p, "geometry_wkt"),
                    Double(p, "accuracy"), Double(p, "city_accuracy"), Double(p, "street_accuracy")));
            }
        }
        else if (results.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Nieznany format wynikow UUG.");
        if (returned != points.Count || found < returned)
            throw new InvalidDataException("Niespojna liczba wynikow UUG.");
        return new ApiResult(type, found, returned, points);
    }

    private static int Count(JsonElement root, string name)
    {
        if (!int.TryParse(Get(root, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidDataException($"Brak poprawnej liczby '{name}' w odpowiedzi UUG.");
        return value;
    }

    private static double RequiredDouble(JsonElement point, string name) => Double(point, name) ??
        throw new InvalidDataException($"Brak poprawnej wspolrzednej '{name}' w odpowiedzi UUG.");

    private static string? Null(JsonElement p, string name) =>
        p.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
    private static string Get(JsonElement p, string name) => Null(p, name) ?? "";
    private static double? Double(JsonElement p, string name) =>
        double.TryParse(Get(p, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) ? value : null;

    // UUG returns a PostgreSQL array literal, including quoted commas and backslash escapes.
    public static IReadOnlyList<string> ParseUnits(string value)
    {
        if (value.Length < 2 || value[0] != '{' || value[^1] != '}')
            throw new InvalidDataException("Nieprawidlowy format jednostka.");
        var items = new List<string>();
        var item = new StringBuilder();
        var quoted = false;
        for (var i = 1; i < value.Length - 1; i++)
        {
            var c = value[i];
            if (c == '\\')
            {
                if (++i >= value.Length - 1) throw new InvalidDataException("Urwana sekwencja jednostka.");
                item.Append(value[i]);
            }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted)
            {
                items.Add(item.ToString().Trim());
                item.Clear();
            }
            else item.Append(c);
        }
        if (quoted) throw new InvalidDataException("Niezamkniety cytat jednostka.");
        items.Add(item.ToString().Trim());
        return items;
    }
}

internal static class Matching
{
    public static string? Missing(Company c)
    {
        if (string.IsNullOrWhiteSpace(c.City) || string.IsNullOrWhiteSpace(c.Number))
            return "Brak miejscowosci lub numeru budynku.";
        if (Text.Number(c.Number) is "-" or "--" or "N/A" or "B/N" or "BRAK")
            return "Brak rzeczywistego numeru budynku (wartosc zastepcza).";
        if (!Text.Postal().IsMatch(c.PostalCode?.Trim() ?? ""))
            return "Brak poprawnego kodu pocztowego; brak bezpiecznej identyfikacji miejscowosci.";
        return null;
    }

    public static bool Same(Company c, Address a) =>
        Text.Province(c.Province) == Text.Province(a.Province) &&
        Text.Normalize(c.City) == Text.Normalize(a.City) &&
        Text.Street(c.Street) == Text.Street(a.Street) &&
        Text.Number(c.Number) == Text.Number(a.Number) &&
        c.PostalCode!.Trim() == a.PostalCode.Trim();

    public static MatchResult Candidate(Company c, ApiResult response)
    {
        if (response.Type != "address" || response.Points.Count == 0) return new(null, "Brak punktu adresowego.");
        var p = response.Points[0];
        if (p.Accuracy != 1 || p.CityAccuracy != 1 ||
            (!string.IsNullOrWhiteSpace(c.Street) && p.StreetAccuracy != 1) || !Same(c, p.Address))
            return new(null, "Adres przyblizony lub rozbieznosc miasta, ulicy, numeru, wojewodztwa lub kodu pocztowego.");
        ValidatePoint(p);
        return new(p.Address, "Kandydat dokladny.");
    }

    public static MatchResult Verify(Company c, Address candidate, ApiResult response)
    {
        if (response.Type != "address" || response.Found != response.Returned)
            return new(null, "Niepelna lista punktow adresowych.");
        var matches = response.Points.Where(p =>
            p.Address.Simc == candidate.Simc && p.Address.Ulic == candidate.Ulic &&
            p.Address.Teryt == candidate.Teryt && Same(c, p.Address)).ToArray();
        if (matches.Length != 1) return new(null, $"Liczba dokladnych punktow adresowych: {matches.Length}.");
        ValidatePoint(matches[0]);
        var address = matches[0].Address;
        if (Math.Abs(address.X - candidate.X) > 0.1 || Math.Abs(address.Y - candidate.Y) > 0.1)
            return new(null, "Rozne wspolrzedne wyszukiwania i listy punktow.");
        if (new[] { address.Province, address.County, address.Municipality, address.City }.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 250) ||
            address.Street?.Length > 200 || address.Number.Length > 50 ||
            !Regex.IsMatch(address.Teryt, @"^\d{6}$") || !Regex.IsMatch(address.Simc, @"^\d{7}$") ||
            (!string.IsNullOrEmpty(address.Street) && !Regex.IsMatch(address.Ulic, @"^\d{5}$")))
            return new(null, "Brak identyfikatorow lub przekroczone limity tabeli Adresy.");
        return new(address, "Jednoznaczny punkt adresowy.");
    }

    internal static void ValidatePoint(ApiPoint p)
    {
        var match = Regex.Match(p.Geometry, @"^POINT\s*\(\s*([-+\d.eE]+)\s+([-+\d.eE]+)\s*\)$", RegexOptions.CultureInvariant);
        if (!match.Success ||
            !double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var y) ||
            Math.Abs(x - p.Address.X) > 0.01 || Math.Abs(y - p.Address.Y) > 0.01)
            throw new InvalidDataException("Geometria nie jest zgodnym punktem adresowym.");
        _ = Coordinates.Convert(p.Address.X, p.Address.Y);
    }
}

internal static class Coordinates
{
    private const string Wkt = "PROJCS[\"Poland\",GEOGCS[\"ETRF\",DATUM[\"ETRF\",SPHEROID[\"GRS 1980\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",19],PARAMETER[\"scale_factor\",0.9993],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",-5300000],UNIT[\"metre\",1]]";
    private static readonly ICoordinateTransformation Transform = new CoordinateTransformationFactory()
        .CreateFromCoordinateSystems(new CoordinateSystemFactory().CreateFromWkt(Wkt), GeographicCoordinateSystem.WGS84);

    public static (decimal Latitude, decimal Longitude) Convert(double x, double y)
    {
        // UUG WKT uses easting, northing (unlike the northing, easting axes in PRG GML).
        double[] result;
        lock (Transform) result = Transform.MathTransform.Transform(new[] { x, y });
        var longitude = result[0];
        var latitude = result[1];
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < 48 or > 56 || longitude is < 13 or > 25)
            throw new InvalidDataException("Wspolrzedne poza zakresem Polski.");
        return (decimal.Round((decimal)latitude, 6), decimal.Round((decimal)longitude, 6));
    }
}
