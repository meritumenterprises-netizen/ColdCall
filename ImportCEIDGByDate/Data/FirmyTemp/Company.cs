using System;
using System.Collections.Generic;

namespace ImportCEIDGByDate.Data.FirmyTemp;

public partial class Company
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int BusinessType { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Krs { get; set; }

    public string? Nip { get; set; }

    public int? WojewodztwoId { get; set; }

    public int? MiejscowoscId { get; set; }

    public int? GminaId { get; set; }

    public int? PowiatId { get; set; }

    public string? PostalCode { get; set; }
    public string? AddressSort { get; set; }

    public string? Street { get; set; }

    public string? BuildingNumber { get; set; }

    public string? ApartmentNumber { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? GoogleEmail { get; set; }

    public string? Website { get; set; }

    public string? InstagramAccount { get; set; }

    public string? FacebookAccount { get; set; }

    public DateTime? StartedAt { get; set; }

    public string? Regon { get; set; }

    public DateTime CreatedOn { get; set; }

    public DateTime? DataModifiedOn { get; set; }

    public byte StatusDzialalnosciId { get; set; }

    public bool? AddressIsMissing { get; set; }

    public bool BlockedByRodo { get; set; }

    public bool SkipAddressVerification { get; set; }

    public DateTime? ImportedOn { get; set; }

    public bool BatchImported { get; set; }

    public DateTime? SuspendedOn { get; set; }

    public DateTime? ResumedOn { get; set; }

    public virtual BusinessType BusinessTypeNavigation { get; set; } = null!;

    public virtual ICollection<CompanyAddress> CompanyAddresses { get; set; } = new List<CompanyAddress>();

    public virtual ICollection<PkdCode> PkdCodes { get; set; } = new List<PkdCode>();
    public virtual ICollection<CompanyPkd> CompanyPkds { get; set; } = new List<CompanyPkd>();

    public virtual Gminy? Gmina { get; set; }

    public virtual Miejscowosci? Miejscowosc { get; set; }

    public virtual Powiaty? Powiat { get; set; }

    public virtual CompanyStatus StatusDzialalnosci { get; set; } = null!;

    public virtual Wojewodztwa? Wojewodztwo { get; set; }
}
