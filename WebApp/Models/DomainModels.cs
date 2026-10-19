namespace ColdCall.Models;

public class AppUser
{
    public int Id { get; set; }
    public string GoogleSubject { get; set; } = "";
    public string GoogleEmail { get; set; } = "";
    public bool UsesGmail { get; set; } = true;
    public bool IsAdmin { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? DisplayName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<UserPlan> Plans { get; set; } = new List<UserPlan>();
    public ICollection<SearchProfile> SearchProfiles { get; set; } = new List<SearchProfile>();
}

public class AdminEmail
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string AddedByEmail { get; set; } = "";
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Company
{
    public byte StatusDzialalnosciId { get; set; } = 1;
    public CompanyStatus? StatusDzialalnosci { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int BusinessTypeId { get; set; }
    public BusinessType? BusinessType { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Krs { get; set; }
    public string? Nip { get; set; }
    public string? Regon { get; set; }
    public bool? AddressIsMissing { get; set; }
    public bool BlockedByRODO { get; set; }
    public bool SkipAddressVerification { get; set; }
    public int? WojewodztwoId { get; set; }
    public Wojewodztwo? Wojewodztwo { get; set; }
    public int? MiejscowoscId { get; set; }
    public Miejscowosc? Miejscowosc { get; set; }
    public int? GminaId { get; set; }
    public Gmina? Gmina { get; set; }
    public int? PowiatId { get; set; }
    public Powiat? Powiat { get; set; }
    public string? PostalCode { get; set; }
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
    public DateTime CreatedOn { get; set; }
    public DateTime? DataModifiedOn { get; set; }
    public DateTime? ImportedOn { get; set; }
    public ICollection<CompanyPkd> CompanyPkds { get; set; } = new List<CompanyPkd>();
    public ICollection<CompanyAddress> CompanyAddresses { get; set; } = new List<CompanyAddress>();
}

public class CompanyStatus
{
    public byte StatusId { get; set; }
    public string Nazwa { get; set; } = "";
}

public class BusinessType
{
    public int TypDzialalnosciId { get; set; }
    public string TypDzialalnosci { get; set; } = "";
}

public class PkdCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string? CodeWithoutDots { get; private set; }
    public string Name { get; set; } = "";
}

public class CompanyPkd
{
    public bool Prevailing { get; set; }
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int PkdCodeId { get; set; }
    public PkdCode PkdCode { get; set; } = null!;
}

public class SearchProfile
{
    public DateTime? LastSavedAtUtc { get; set; }
    public string? SearchProfileHash { get; set; }
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;
    public string Name { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<SearchProfilePkd> PkdCodes { get; set; } = new List<SearchProfilePkd>();
    public SearchProfileCriteria? Criteria { get; set; }
}

public class SearchProfileCriteria
{
    public string Name { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public int Id { get; set; }
    public int SearchProfileId { get; set; }
    public SearchProfile SearchProfile { get; set; } = null!;
    public string SortColumn { get; set; } = "dataAktywnosci";
    public string SortDirection { get; set; } = "desc";
    public string Address { get; set; } = "";
    public bool? HasPhone { get; set; }
    public bool? HasEmail { get; set; }
    public bool? HasWebsite { get; set; }
    public bool DateRangeEnabled { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public int RecordLimit { get; set; } = 1000;
    public int? WojewodztwoId { get; set; }
    public Wojewodztwo? Wojewodztwo { get; set; }
    public int? PowiatId { get; set; }
    public Powiat? Powiat { get; set; }
    public int? GminaId { get; set; }
    public Gmina? Gmina { get; set; }
    public int? MiejscowoscId { get; set; }
    public Miejscowosc? Miejscowosc { get; set; }
    public bool? HasFirmyWPromieniu { get; set; }
    public int? FirmyWPromieniu { get; set; }
}

public class SearchProfilePkd
{
    public int SearchProfileId { get; set; }
    public SearchProfile SearchProfile { get; set; } = null!;
    public int PkdCodeId { get; set; }
    public PkdCode PkdCode { get; set; } = null!;
}

public class PlanDefinition
{
    public int Order { get; set; }
    public PlanDefinition()
    {
    }

    public PlanDefinition(string code, int searchLimit, decimal price)
    {
        Code = code;
        MaxNumberOfSearches = searchLimit;
        Price = price;
    }

    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int MaxNumberOfSearches { get; set; }
    public int? MaxNumberOfCSVExportsPerSearch { get; set; }
    public int? MaxNumberOfExportedRecords { get; set; }
    public int? ExpiresAfterDays { get; set; }
    public bool IsTrial { get; set; }
    public bool IsUpgrade { get; set; }
    public int? UpgradeFrom { get; set; }
    public PlanDefinition? UpgradeFromPlan { get; set; }
    public int? UpgradeTo { get; set; }
    public PlanDefinition? UpgradeToPlan { get; set; }
    public bool ShowInOffer { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string DescriptionHtml { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";
    public string? StripePriceId { get; set; }
}

public class UserPlan
{
    public int Id { get; set; }
    public bool? IsActive { get; set; }
    public int AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;
    public int PlanDefinitionId { get; set; }
    public PlanDefinition PlanDefinition { get; set; } = null!;
    public DateTime PurchasedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public decimal PricePaid { get; set; }
    public string Source { get; set; } = "Manual";
    public string? StripeSessionId { get; set; }
}

public class Wojewodztwo : DictionaryEntity
{
}

public class Miejscowosc : DictionaryEntity
{
}

public class Gmina : DictionaryEntity
{
}

public class Powiat : DictionaryEntity
{
}

public abstract class DictionaryEntity
{
    public int Id { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(250)]
    public string Name { get; set; } = "";
}
