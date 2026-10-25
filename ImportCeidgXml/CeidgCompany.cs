namespace ImportCeidgXml;

// Column names and nullability follow ColdCall.Companies.
public sealed record CeidgCompany
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int BusinessType { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Krs { get; init; }
    public string? Nip { get; init; }
    public string? Regon { get; init; }
    public int? WojewodztwoId { get; init; }
    public int? MiejscowoscId { get; init; }
    public int? GminaId { get; init; }
    public int? PowiatId { get; init; }
    public string? PostalCode { get; init; }
    public string? Street { get; init; }
    public string? BuildingNumber { get; init; }
    public string? ApartmentNumber { get; init; }
    public string? Phone { get; init; }
    public bool PhoneWasNormalized { get; init; }
    public bool PhoneWasRejected { get; init; }
    public string? Email { get; init; }
    public string? GoogleEmail { get; init; }
    public string? Website { get; init; }
    public string? InstagramAccount { get; init; }
    public string? FacebookAccount { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime CreatedOn { get; init; }
    public DateTime? DataModifiedOn { get; init; }
    public byte StatusDzialalnosciId { get; init; }
    public bool? AddressIsMissing { get; init; }
    public bool BlockedByRODO { get; init; }
    public bool SkipAddressVerification { get; init; }
    public DateTime? ImportedOn { get; init; }
    public bool BatchImported { get; init; }
    public DateTime? SuspendedOn { get; init; }
    public DateTime? ResumedOn { get; init; }

    // XML source values used to resolve dictionary IDs and filter active entries.
    public string? Province { get; init; }
    public string? County { get; init; }
    public string? Commune { get; init; }
    public string? City { get; init; }
    public string? Status { get; init; }
    public string SourceXml { get; init; } = "";

    public List<string> PKDs { get; init; } = new();
    public string PrevailingPKD { get; init; } = "";
    public IReadOnlyList<CeidgCompanyPkd> CompanyPkds { get; init; } = Array.Empty<CeidgCompanyPkd>();
}
