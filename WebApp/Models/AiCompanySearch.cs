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
    public DateOnly? StartedFrom { get; set; }
    public DateOnly? StartedTo { get; set; }
    public int Page { get; set; } = 1;
}
public sealed record AiCompanySearchResult(SearchProfileCriteriaDto Criteria,
    IReadOnlyList<PkdDto> PkdCodes, int Total, int RecordLimit, int Page,
    IReadOnlyList<CompanyShortDto> Items, string Summary);
