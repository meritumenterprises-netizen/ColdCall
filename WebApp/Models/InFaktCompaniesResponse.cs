using System.Text.Json;
using System.Text.Json.Serialization;

namespace ColdCall.Models;

public sealed class InFaktCompaniesResponse
{
    [JsonPropertyName("metainfo")]
    public required InFaktPagination Metainfo { get; init; }

    [JsonPropertyName("entities")]
    public required List<InFaktClient> Entities { get; init; }
}

public sealed class InFaktPagination
{
    [JsonPropertyName("count")] public int Count { get; init; }
    [JsonPropertyName("total_count")] public int TotalCount { get; init; }
    [JsonPropertyName("next")] public string? Next { get; init; }
    [JsonPropertyName("previous")] public string? Previous { get; init; }
}

public sealed class InFaktClient
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("uuid")] public Guid? Uuid { get; init; }
    [JsonPropertyName("company_name")] public string? CompanyName { get; init; }
    [JsonPropertyName("street")] public string? Street { get; init; }
    [JsonPropertyName("street_number")] public string? StreetNumber { get; init; }
    [JsonPropertyName("flat_number")] public string? FlatNumber { get; init; }
    [JsonPropertyName("city")] public string? City { get; init; }
    [JsonPropertyName("country")] public string? Country { get; init; }
    [JsonPropertyName("country_full_name")] public string? CountryFullName { get; init; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; init; }
    [JsonPropertyName("nip")] public string? Nip { get; init; }
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; init; }
    [JsonPropertyName("web_site")] public string? WebSite { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("note")] public string? Note { get; init; }
    [JsonPropertyName("receiver")] public string? Receiver { get; init; }
    [JsonPropertyName("mailing_company_name")] public string? MailingCompanyName { get; init; }
    [JsonPropertyName("mailing_street")] public string? MailingStreet { get; init; }
    [JsonPropertyName("mailing_city")] public string? MailingCity { get; init; }
    [JsonPropertyName("mailing_postal_code")] public string? MailingPostalCode { get; init; }
    [JsonPropertyName("days_to_payment")] public string? DaysToPayment { get; init; }
    [JsonPropertyName("payment_method")] public string? PaymentMethod { get; init; }
    [JsonPropertyName("invoice_note")] public string? InvoiceNote { get; init; }
    [JsonPropertyName("same_forward_address")] public bool SameForwardAddress { get; init; }
    [JsonPropertyName("first_name")] public string? FirstName { get; init; }
    [JsonPropertyName("last_name")] public string? LastName { get; init; }
    [JsonPropertyName("business_activity_kind")] public JsonElement? BusinessActivityKind { get; init; }
    [JsonPropertyName("is_vindicable")] public bool IsVindicable { get; init; }
    [JsonPropertyName("vindicable_invoices_count")] public int VindicableInvoicesCount { get; init; }
}
