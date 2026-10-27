namespace GeoPortalAddressImporter;

internal sealed class AddressSaveItem
{
    public required int CompanyId { get; init; }
    public required int ProvinceId { get; init; }
    public required int CityId { get; init; }
    public required int CountyId { get; init; }
    public required int MunicipalityId { get; init; }
    public string? OriginalStreet { get; init; }
    public string? OriginalNumber { get; init; }
    public string? PostalCode { get; init; }
    public string? Street { get; init; }
    public string? Number { get; init; }
    public required string Kind { get; init; }
    public required string SourceKey { get; init; }
    public required bool IsCity { get; init; }
    public required decimal Latitude { get; init; }
    public required decimal Longitude { get; init; }
}
