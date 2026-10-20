using System.Text.RegularExpressions;

namespace GeoPortalAddressImporter;

internal static class CityMatching
{
    public static MatchResult Match(Company company, ApiResult response)
    {
        if (response.Type != "city" || response.Found != response.Returned)
            return new(null, "Brak kompletnego wyniku dla miejscowosci.");
        var matches = response.Points.Where(p =>
            Text.Normalize(p.Address.City) == Text.Normalize(company.City)
            && Text.Province(p.Address.Province) == Text.Province(company.Province)).ToArray();
        if (matches.Length != 1)
            return new(null, $"Liczba pasujacych miejscowosci: {matches.Length}; bez zgadywania lokalizacji.");
        var point = matches[0];
        var address = point.Address;
        if (point.Accuracy != 1 || !address.IsCity
            || !Regex.IsMatch(address.Simc, @"^\d{7}$")
            || !Regex.IsMatch(address.Teryt, @"^\d{6}$")
            || new[] { address.Province, address.County, address.Municipality, address.City }
                .Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 250))
            return new(null, "Niepelna lub przyblizona identyfikacja miejscowosci.");
        Matching.ValidatePoint(point);
        return new(address with { Street = null, Number = "", PostalCode = "", Ulic = "" },
            "GPS miejscowosci (nie budynku).");
    }
}
