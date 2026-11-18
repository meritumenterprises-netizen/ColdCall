using System.Net.Http.Headers;
using System.Text.Json;
using System.Globalization;
using System.Text;

namespace ColdCall.Services;

public sealed class CeidgService(HttpClient http, IConfiguration configuration, ILogger<CeidgService> logger)
{
    public async Task<string> GetCompanyId(string? nip, string? status = "AKTYWNY", CancellationToken cancellationToken = default)
    {
        nip = nip?.Trim();
        if (nip == null || nip.Length != 10 || !nip.All(char.IsAsciiDigit))
            return string.Empty;
        var expectedStatus = NormalizeStatus(status);
        if (expectedStatus != "AKTYWNY" && expectedStatus != "WYKRESLONY"
            && expectedStatus.IndexOf("CYWILNEJ") == 0)
            return string.Empty;

        var token = configuration["CEIDG_API_TOKEN"] ?? configuration["Ceidg:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("CEIDG token is not configured.");
            return string.Empty;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://dane.biznes.gov.pl/api/ceidg/v3/firma?nip=" + Uri.EscapeDataString(nip));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CEIDG company lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                return string.Empty;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("firma", out var companies)
                || companies.ValueKind != JsonValueKind.Array)
                return string.Empty;

            foreach (var company in companies.EnumerateArray())
            {
                if (NormalizeStatus(Text(company, "status")) != expectedStatus)
                    continue;
                var companyNip = Text(company, "nip");
                if (company.ValueKind == JsonValueKind.Object && company.TryGetProperty("wlasciciel", out var owner))
                    companyNip = Text(owner, "nip") ?? companyNip;
                if (!string.Equals(companyNip, nip, StringComparison.Ordinal))
                    continue;
                var id = Text(company, "id");
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            // Do not log credentials or upstream response bodies.
            logger.LogWarning("CEIDG company lookup failed ({ErrorType}).", ex.GetType().Name);
        }
        return string.Empty;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static string NormalizeStatus(string? status) => new string((status ?? "")
        .ToUpperInvariant().Replace('\u0141', 'L').Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
            && char.IsLetterOrDigit(character)).ToArray());
}
