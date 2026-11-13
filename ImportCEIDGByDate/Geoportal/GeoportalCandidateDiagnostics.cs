using System.Text;

namespace ImportCEIDGByDate.Geoportal;

internal static class GeoportalCandidateDiagnostics
{
    public static void Write(Company company, ApiResult response)
    {
        var prefix = $"GeoPortal: firma {company.Id}";
        var text = new StringBuilder();
        text.AppendLine($"{prefix}: kandydaci odpowiedzi: {response.Returned} z {response.Found} punktow.");
        text.AppendLine($"{prefix}: dane firmy: wojewodztwo={company.Province}; powiat={company.County}; gmina={company.Municipality}; miejscowosc={company.City}; ulica={company.Street}; numer budynku={company.Number}; kod pocztowy={company.PostalCode}.");
        for (var i = 0; i < response.Points.Count; i++)
        {
            var point = response.Points[i];
            var address = point.Address;
            text.AppendLine($"{prefix}: punkt {i + 1}/{response.Points.Count}: wojewodztwo={address.Province}; powiat={address.County}; gmina={address.Municipality}; miejscowosc={address.City}; ulica={address.Street}; numer budynku={address.Number}; kod pocztowy={address.PostalCode}; accuracy={point.Accuracy}; city_accuracy={point.CityAccuracy}; street_accuracy={point.StreetAccuracy}; X={address.X}; Y={address.Y}.");
        }
        // Keep one company's diagnostic block together during parallel lookups.
        Console.Write(text.ToString());
    }
}
