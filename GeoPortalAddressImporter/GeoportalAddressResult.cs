namespace GeoPortalAddressImporter;

public sealed class GeoportalAddressResult
{
    public bool Found { get; init; }
    public GeoportalAddressErrorCode ErrorCode { get; init; }
    public string Reason { get; init; } = "";
    public string? Street { get; init; }
    public string? BuildingNumber { get; init; }
    public string? SourceKey { get; init; }
    public bool IsCity { get; init; }
    public decimal Latitude { get; init; }
    public decimal Longitude { get; init; }
}
