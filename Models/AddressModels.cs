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
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public Geometry? Location { get; set; }
    public string? PrngId { get; set; }
    public ICollection<CompanyAddress> CompanyAddresses { get; set; } = new List<CompanyAddress>();
}

public class CompanyAddress
{
    public int CompanyAdresId { get; set; }
    public int CompanyId { get; set; }
    public int AddressId { get; set; }
    public Company Company { get; set; } = null!;
    public Adres Address { get; set; } = null!;
}
