namespace ColdCall.Models;

public sealed class AiCompanySearchRequest
{
    public string? City { get; set; }
    public string? Voivodeship { get; set; }
    public string? County { get; set; }
    public string? Municipality { get; set; }
    public int? RadiusKm { get; set; }
    public int[] StatusIds { get; set; } = [1];
    public string[] PkdCodes { get; set; } = [];
    public string? CompanyName { get; set; }
    public bool? HasPhone { get; set; }
    public bool? HasEmail { get; set; }
    public bool? HasWebsite { get; set; }
    public bool? HasAddress { get; set; }
    public DateOnly? StartedFrom { get; set; }
    public DateOnly? StartedTo { get; set; }
    public int Page { get; set; } = 1;
}
public sealed record AiCompanySearchResult(SearchProfileCriteriaDto Criteria,
    IReadOnlyList<PkdDto> PkdCodes, int Total, int RecordLimit, int Page,
    IReadOnlyList<AiCompanySearchItemDto> Items, string Summary);

public sealed class AiCompanySearchItemDto : CompanyShortDto
{
    [System.Text.Json.Serialization.JsonPropertyName("Wojew\u00f3dztwo")]
    public string? Wojewodztwo { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Gmina")]
    public string? Gmina { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Powiat")]
    public string? Powiat { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Miejscowo\u015b\u0107")]
    public string? Miejscowosc { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Ulica")]
    public string? Ulica { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Nr Budynku")]
    public string? BuildingNumber { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("Nr apartamentu")]
    public string? ApartmentNumber { get; set; }
}
