using NetTopologySuite.Geometries;

namespace ColdCall.Models;

public class Adres
{
    public int Id { get; set; }
    public int WojewodztwoId { get; set; }
    public Wojewodztwo Wojewodztwo { get; set; } = null!;
    public int PowiatId { get; set; }
    public Powiat Powiat { get; set; } = null!;
    public int GminaId { get; set; }
    public Gmina Gmina { get; set; } = null!;
    public int MiejscowoscId { get; set; }
    public Miejscowosc Miejscowosc { get; set; } = null!;
    public string? Ulica { get; set; }
    public string? NrUlicy { get; set; }
    public string? Rodzaj { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public Geometry Location { get; set; } = null!;
    public string? PrngId { get; set; }
}

// The import staging table has no primary key in SQL Server.
public class AdresImportStage
{
    public string? PrngId { get; set; }
    public string Nazwa { get; set; } = "";
    public string? Rodzaj { get; set; }
    public string Wojewodztwo { get; set; } = "";
    public string Powiat { get; set; } = "";
    public string Gmina { get; set; } = "";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}

// SQL Server currently defines no foreign keys on this linking table.
public class CompanyAddress
{
    public int CompanyAdresId { get; set; }
    public int CompanyId { get; set; }
    public int AddressId { get; set; }
}
