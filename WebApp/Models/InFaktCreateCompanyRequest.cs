using System.Text.Json.Serialization;

namespace ColdCall.Models;

public sealed class InFaktCreateCompanyRequest
{
    [JsonPropertyName("company_name")] public required string CompanyName { get; init; }
    [JsonPropertyName("street")] public required string Street { get; init; }
    [JsonPropertyName("street_number")] public required string StreetNumber { get; init; }
    [JsonPropertyName("city")] public required string City { get; init; }
    [JsonPropertyName("nip")] public required string Nip { get; init; }
    [JsonPropertyName("post_code")] public required string PostCode { get; init; }
    [JsonPropertyName("country")] public string Country { get; init; } = "PL";
}
