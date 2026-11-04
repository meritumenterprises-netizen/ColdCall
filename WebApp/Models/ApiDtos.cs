namespace ColdCall.Models;

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<SortableColumnDto>? SortableColumns = null,
    int? TotalWithoutLimit = null,
    bool? HasNotes = null);

public record SortableColumnDto(string Key, string Label);

public record PkdDto(int Id, string Code, string Name);

public record CompanyPkdDto(int Id, string Code, string Name, bool Prevailing);

public record CompanyDetailsDto(CompanyDto Company, IReadOnlyList<CompanyPkdDto> PkdCodes, IReadOnlyList<CompanyAddressDto> Addresses);

public record CompanyAddressDto(
    int CompanyAdresId, int CompanyId, int AddressId,
    int WojewodztwoId, int PowiatId, int GminaId, int MiejscowoscId,
    string Wojewodztwo, string Powiat, string Gmina, string Miejscowosc,
    string? Ulica, string? NrUlicy, string? Rodzaj,
    decimal? Latitude, decimal? Longitude, string? PrngId);

public record CompanyDto(
    int Id,
    string Name,
    int BusinessTypeId,
    string? BusinessTypeName,
    string? FirstName,
    string? LastName,
    string? Krs,
    string? Nip,
    string? Wojewodztwo,
    string? City,
    string? Gmina,
    string? Powiat,
    string? PostalCode,
    string? Street,
    string? BuildingNumber,
    string? ApartmentNumber,
    string? Phone,
    string? Email,
    string? GoogleEmail,
    string? Website,
    string? InstagramAccount,
    string? FacebookAccount,
    DateTime? StartedAt,
    DateTime CreatedOn,
    string? Regon,
    byte StatusDzialalnosciId,
    string? StatusDzialalnosciName,
    bool? AddressIsMissing,
    bool BlockedByRODO,
    int? WojewodztwoId,
    int? MiejscowoscId,
    int? GminaId,
    int? PowiatId,
    DateTime? DataModifiedOn = null,
    DateTime? ResumedOn = null,
    string? UserNotes = null,
    DateTime? SuspendedOn = null,
    string? AddressSort = null);

public record CompanyUpdateDto(
    [System.ComponentModel.DataAnnotations.StringLength(1000)] string Name,
    int BusinessTypeId,
    string? FirstName,
    string? LastName,
    string? Krs,
    string? Nip,
    int? WojewodztwoId,
    int? MiejscowoscId,
    int? GminaId,
    int? PowiatId,
    string? PostalCode,
    string? Street,
    string? BuildingNumber,
    string? ApartmentNumber,
    string? Phone,
    string? Email,
    string? GoogleEmail,
    string? Website,
    string? InstagramAccount,
    string? FacebookAccount,
    DateTime? StartedAt);

public record SearchProfileDto(int Id, string Name, IReadOnlyList<PkdDto> PkdCodes, SearchProfileCriteriaDto? Criteria = null, bool HasCsvExports = false, int? RecordCount = null);

public record SaveSearchProfileDto(string Name, IReadOnlyList<int> PkdCodeIds, SearchProfileCriteriaDto? Criteria = null, bool ConfirmDuplicateCriteria = false);

public record PlanDto(string Code, int SearchLimit, string DescriptionHtml, decimal Price, string Currency, DateTime? ActiveUntil,
    int Order, bool CanPurchase);

public record UserPlanDto(string PlanCode, DateTime PurchasedAtUtc, DateTime ExpiresAtUtc, string? Source, string DescriptionHtml = "", DateTime? PaidAtUtc = null, decimal? AmountPaid = null);

public record UserDto(int Id, string GoogleEmail, string? DisplayName, IReadOnlyList<UserPlanDto> Plans, DateTime CreatedAtUtc = default, bool DisablePlanPurchase = false, bool IsEnabled = true,
    string? FirstName = null, string? LastName = null, string? CompanyName = null, string? PostalCode = null,
    string? City = null, string? Street = null, string? StreetNumber = null, bool IsAdmin = false, string? Nip = null, string? Phone = null, bool IsBetaUser = false);

public record GrantPlanDto(int UserId, string PlanCode);

public record DictionaryDto(int Id, string Name);

public record BusinessTypeDto(int TypDzialalnosciId, string TypDzialalnosci);

public record CheckoutRequest(string PlanCode);
public record ConfirmCheckoutRequest(string SessionId);
