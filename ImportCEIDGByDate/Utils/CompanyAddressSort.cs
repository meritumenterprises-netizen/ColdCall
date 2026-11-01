using ImportCEIDGByDate.Data.FirmyTemp;

namespace ImportCEIDGByDate;

public static class CompanyAddressSort
{
    public static string Build(Company company, string cityName) =>
        cityName + " " + (company.PostalCode ?? "") + " " + (company.Street ?? "")
        + " " + (company.BuildingNumber ?? "")
        + (company.ApartmentNumber == null ? "" : "/" + company.ApartmentNumber);
}
