namespace ImportCeidgXml;

public sealed record CeidgCompanyPkd(
    int Id,
    int CompanyId,
    int PkdCodeId,
    string Code,
    string Name,
    bool Prevailing);
