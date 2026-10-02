namespace ColdCall.Models;

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<SortableColumnDto>? SortableColumns = null);

public record SortableColumnDto(string Key, string Label);

public record PkdDto(int Id, string Code, string Name);

public record CompanyDetailsDto(CompanyDto Company, IReadOnlyList<PkdDto> PkdCodes);

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
    DateTime CreatedOn);

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

public record SearchProfileDto(int Id, string Name, IReadOnlyList<PkdDto> PkdCodes, SearchProfileCriteriaDto? Criteria = null);

public record SaveSearchProfileDto(string Name, IReadOnlyList<int> PkdCodeIds, SearchProfileCriteriaDto? Criteria = null);

public record PlanDto(string Code, int SearchLimit, string DescriptionHtml, decimal Price, string Currency, DateTime? ActiveUntil);

public record UserPlanDto(string PlanCode, DateTime PurchasedAtUtc, DateTime ExpiresAtUtc, string Source);

public record UserDto(int Id, string GoogleEmail, string? DisplayName, IReadOnlyList<UserPlanDto> Plans);

public record GrantPlanDto(int UserId, string PlanCode);

public record DictionaryDto(int Id, string Name);

public record BusinessTypeDto(int TypDzialalnosciId, string TypDzialalnosci);

public record CheckoutRequest(string PlanCode, string SuccessUrl, string CancelUrl);
