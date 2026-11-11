using System.Net.Http.Json;
using System.Text.Json;
using ColdCall.Models;

namespace ColdCall.Services;

public class InFaktService(HttpClient client, IConfiguration configuration)
{
    private const string InvoicesUrl = "https://api.sandbox-infakt.pl/api/v3/invoices.json";
    private const string ClientsUrl = "https://api.sandbox-infakt.pl/api/v3/clients.json";

    public async Task<InFaktClient> CreateCompany(InFaktCreateCompanyRequest company,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);
        using var request = CreateRequest(HttpMethod.Post);
        request.Content = JsonContent.Create(new { client = company });
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            if (details.Length > 8192) details = details[..8192];
            throw new HttpRequestException(
                $"inFakt CreateCompany failed (HTTP {(int)response.StatusCode}): {details}",
                null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<InFaktClient>(cancellationToken)
            ?? throw new InvalidDataException("inFakt returned an empty response.");
    }

    public Task<JsonElement> CreateInvoice(string clientCompanyName, int invoiceNumber, string invoiceDate, string planName, decimal price,
        CancellationToken cancellationToken = default, InFaktInvoiceBuyer? buyer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(invoiceNumber);
        return CreateInvoice(clientCompanyName, invoiceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), invoiceDate, planName, price, cancellationToken, buyer);
    }

    public async Task<JsonElement> CreateInvoice(string clientCompanyName, string invoiceNumber, string invoiceDate, string planName, decimal price,
        CancellationToken cancellationToken = default, InFaktInvoiceBuyer? buyer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientCompanyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(planName);
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);
        var priceInGrosze = checked((long)(price * 100m));
        if (price * 100m != priceInGrosze)
            throw new ArgumentException("The invoice price must have at most two decimal places.", nameof(price));
        using var request = CreateRequest(HttpMethod.Post, InvoicesUrl);
        var invoiceFields = new Dictionary<string, object?>
        {
                ["kind"] = "vat",
                ["number"] = invoiceNumber,
                ["invoice_date"] = invoiceDate,
                ["sell_date"] = invoiceDate,
                ["payment_to"] = invoiceDate,
                ["services"] = new[]
                {
                    new
                    {
                        name = "Plan " + planName, quantity = 1,
                        unit_net_price = priceInGrosze, net_price = priceInGrosze, tax_symbol = "23"
                    }
                }
        };
        invoiceFields["client_company_name"] = clientCompanyName;
        if (buyer != null)
        {
            if (string.IsNullOrWhiteSpace(buyer.CompanyName)
                && !string.IsNullOrWhiteSpace(buyer.FirstName) && !string.IsNullOrWhiteSpace(buyer.LastName))
            {
                invoiceFields["client_first_name"] = buyer.FirstName;
                invoiceFields["client_last_name"] = buyer.LastName;
            }
            if (!string.IsNullOrWhiteSpace(buyer.Nip)) invoiceFields["client_tax_code"] = buyer.Nip;
            ArgumentException.ThrowIfNullOrWhiteSpace(buyer.City);
            invoiceFields["client_street"] = string.Join(" ", new[] { buyer.Street, buyer.StreetNumber }.Where(s => !string.IsNullOrWhiteSpace(s)));
            invoiceFields["client_city"] = buyer.City;
            invoiceFields["client_post_code"] = buyer.PostalCode;
            invoiceFields["client_country"] = "PL";
        }
        request.Content = JsonContent.Create(new { invoice = invoiceFields });
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            if (details.Length > 8192) details = details[..8192];
            throw new HttpRequestException(
                $"inFakt CreateInvoice failed (HTTP {(int)response.StatusCode}): {details}",
                null, response.StatusCode);
        }
        var invoice = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (invoice.ValueKind == JsonValueKind.Null)
            throw new InvalidDataException("inFakt returned an empty invoice response.");
        return invoice;
    }

    public async Task<byte[]> GetInvoice(string invoiceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);
        var url = "https://api.sandbox-infakt.pl/api/v3/invoices/"
            + Uri.EscapeDataString(invoiceId) + "/pdf.json?document_type=original";
        using var request = CreateRequest(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/pdf");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string? url = null)
    {
        var apiKey = configuration["InFakt:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Configure InFakt:ApiKey in user secrets.");
        var request = new HttpRequestMessage(method, url ?? ClientsUrl);
        request.Headers.Add("X-inFakt-ApiKey", apiKey);
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    public async Task<InFaktCompaniesResponse> RetrieveCompanies(CancellationToken cancellationToken = default, string? pageUrl = null)
    {
        if (pageUrl != null && (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)
            || page.Scheme != Uri.UriSchemeHttps || page.Host != "api.sandbox-infakt.pl"
            || page.Port != 443 || page.AbsolutePath != "/api/v3/clients.json" || !string.IsNullOrEmpty(page.UserInfo)))
            throw new InvalidDataException("inFakt returned an invalid client pagination URL.");
        using var request = CreateRequest(HttpMethod.Get, pageUrl);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InFaktCompaniesResponse>(cancellationToken)
            ?? throw new InvalidDataException("inFakt returned an empty response.");
    }
}
