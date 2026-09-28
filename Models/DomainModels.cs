namespace ColdCall.Models;

public enum BusinessType
{
    Jdg = 1,
    Company = 2
}

public class AppUser
{
    public int Id { get; set; }
    public string GoogleSubject { get; set; } = "";
    public string GoogleEmail { get; set; } = "";
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
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public BusinessType BusinessType { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Krs { get; set; }
    public string? Nip { get; set; }
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
    public ICollection<CompanyPkd> CompanyPkds { get; set; } = new List<CompanyPkd>();
}

public class PkdCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}

public class CompanyPkd
{
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int PkdCodeId { get; set; }
    public PkdCode PkdCode { get; set; } = null!;
}

public class SearchProfile
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;
    public string Name { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<SearchProfilePkd> PkdCodes { get; set; } = new List<SearchProfilePkd>();
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
    public PlanDefinition()
    {
    }

    public PlanDefinition(string code, int searchLimit, decimal price)
    {
        Code = code;
        SearchLimit = searchLimit;
        Price = price;
    }

    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int SearchLimit { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";
    public string? StripePriceId { get; set; }
}

public class UserPlan
{
    public int Id { get; set; }
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
    public string Name { get; set; } = "";
}
