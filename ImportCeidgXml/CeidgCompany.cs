namespace ImportCeidgXml;

public sealed record CeidgCompany(
    string Name, string? Nip, string? Regon, string? FirstName, string? LastName,
    string? Province, string? County, string? Commune, string? City,
    string? PostalCode, string? Street, string? BuildingNumber, string? ApartmentNumber,
    string? Phone, string? Email, string? GoogleEmail, string? Website, DateTime? StartedAt,
    string? Status);
