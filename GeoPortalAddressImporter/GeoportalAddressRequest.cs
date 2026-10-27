namespace GeoPortalAddressImporter;

public sealed class GeoportalAddressRequest
{
    public int CompanyId { get; init; }
    public int ProvinceId { get; init; }
    public int CountyId { get; init; }
    public int MunicipalityId { get; init; }
    public int CityId { get; init; }
    public string Province { get; init; } = "";
    public string County { get; init; } = "";
    public string Municipality { get; init; } = "";
    public string City { get; init; } = "";
    public string? Street { get; init; }
    public string? BuildingNumber { get; init; }
    public string? PostalCode { get; init; }
}
