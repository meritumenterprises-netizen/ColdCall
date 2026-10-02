using NetTopologySuite.Geometries;

namespace ColdCall.Models;

public class AdresView
{
    public Geometry Location { get; set; } = null!;
    public int WojewodztwoId { get; set; }
    public int PowiatId { get; set; }
    public int GminaId { get; set; }
    public int MiejscowoscId { get; set; }
    public string Wojewodztwo { get; set; } = "";
    public string Powiat { get; set; } = "";
    public string Gmina { get; set; } = "";
    public string Miejscowosc { get; set; } = "";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}

public class CompanyAddressView
{
    public int CompanyId { get; set; }
    public string Company { get; set; } = "";
    public int WojewodztwoId { get; set; }
    public int PowiatId { get; set; }
    public int GminaId { get; set; }
    public int MiejscowoscId { get; set; }
    public string Wojewodztwo { get; set; } = "";
    public string Powiat { get; set; } = "";
    public string Gmina { get; set; } = "";
    public string Miejscowosc { get; set; } = "";
    public bool? AddressIsMissing { get; set; }
}

public class CompanyView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TypDzialalnosci { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Krs { get; set; }
    public string? Nip { get; set; }
    public string Wojewodztwo { get; set; } = "";
    public string Miejscowosc { get; set; } = "";
    public string Gmina { get; set; } = "";
    public string Powiat { get; set; } = "";
    public string? PostalCode { get; set; }
    public string? Street { get; set; }
    public string? BuildingNumber { get; set; }
    public string? ApartmentNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GoogleEmail { get; set; }
    public string? Website { get; set; }
    public string? InstagramAccount { get; set; }
    public DateTime? StartedAt { get; set; }
    public string? Regon { get; set; }
    public string? StatusDzialalnosci { get; set; }
    public string? FacebookAccount { get; set; }
}

public class CompanyWithPkdsView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string PkdCode { get; set; } = "";
    public string PkdOpis { get; set; } = "";
    public string? Phone { get; set; }
    public int BusinessType { get; set; }
    public string? Email { get; set; }
    public string TypDzialalnosci { get; set; } = "";
    public string Gmina { get; set; } = "";
    public string Miejscowosc { get; set; } = "";
    public string Powiat { get; set; } = "";
    public string? PostalCode { get; set; }
    public string? Street { get; set; }
    public string? BuildingNumber { get; set; }
    public string? ApartmentNumber { get; set; }
    public string? Website { get; set; }
    public string? Regon { get; set; }
    public string? StatusDzialalnosci { get; set; }
    public string Wojewodztwo { get; set; } = "";
    public string? Nip { get; set; }
}
