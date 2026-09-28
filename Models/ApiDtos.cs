namespace ColdCall.Models;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record PkdDto(int Id, string Code, string Name);

public record CompanyDto(
    int Id,
    string Name,
    string BusinessType,
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
    DateTime? StartedAt);

public record CompanyUpdateDto(
    string Name,
    BusinessType BusinessType,
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

public record SearchProfileDto(int Id, string Name, IReadOnlyList<PkdDto> PkdCodes);

public record SaveSearchProfileDto(string Name, IReadOnlyList<int> PkdCodeIds);

public record PlanDto(string Code, int SearchLimit, string DescriptionHtml, decimal Price, string Currency, DateTime? ActiveUntil);

public record UserPlanDto(string PlanCode, DateTime PurchasedAtUtc, DateTime ExpiresAtUtc, string Source);

public record UserDto(int Id, string GoogleEmail, string? DisplayName, IReadOnlyList<UserPlanDto> Plans);

public record GrantPlanDto(int UserId, string PlanCode);

public record DictionaryDto(int Id, string Name);

public record CheckoutRequest(string PlanCode, string SuccessUrl, string CancelUrl);
