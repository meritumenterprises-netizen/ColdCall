using AdjustCeidgAddress.Data.FirmyTemp;

namespace AdjustCeidgAddress;

public static class CompanyAddressSort
{
    public static string Build(Company company, string cityName) =>
        cityName + " " + (company.PostalCode ?? "") + " " + (company.Street ?? "")
        + " " + (company.BuildingNumber ?? "")
        + (string.IsNullOrWhiteSpace(company.ApartmentNumber) ? "" : "/" + company.ApartmentNumber.Trim());
}
