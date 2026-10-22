using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;

namespace ImportCEIDGByDate.Data.FirmyTemp;

public partial class Adresy
{
    public int Id { get; set; }

    public int WojewodztwoId { get; set; }

    public int PowiatId { get; set; }

    public int GminaId { get; set; }

    public int MiejscowoscId { get; set; }

    public string? Ulica { get; set; }

    public string? NrUlicy { get; set; }

    public string? Rodzaj { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public Geometry? Location { get; set; }

    public string? PrngId { get; set; }

    public virtual ICollection<CompanyAddress> CompanyAddresses { get; set; } = new List<CompanyAddress>();

    public virtual Gminy Gmina { get; set; } = null!;

    public virtual Miejscowosci Miejscowosc { get; set; } = null!;

    public virtual Powiaty Powiat { get; set; } = null!;

    public virtual Wojewodztwa Wojewodztwo { get; set; } = null!;
}
