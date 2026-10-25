using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using ColdCall.Data;
using ColdCall.Models;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public interface ICurrentUserService
{
    bool WasCreated => false;
    Task<AppUser> GetOrCreateAsync(ClaimsPrincipal principal);
}

public class CurrentUserService : ICurrentUserService
{
    public bool WasCreated { get; private set; }
    private readonly ColdCallDbContext _db;

    public CurrentUserService(ColdCallDbContext db)
    {
        _db = db;
    }

    public async Task<AppUser> GetOrCreateAsync(ClaimsPrincipal principal)
    {
        var localId = principal.FindFirstValue(LocalAuthentication.UserIdClaim);
        if (int.TryParse(localId, out var userId))
        {
            var localUser = await _db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsEnabled)
                ?? throw new UnauthorizedAccessException("Konto jest niedostepne.");
            return localUser;
        }
        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email")
            ?? throw new InvalidOperationException("Token Google nie zawiera adresu email.");
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? email;
        var user = await _db.Users.FirstOrDefaultAsync(x => x.GoogleEmail == email);
        if (user == null)
        {
            user = new AppUser
            {
                GoogleEmail = email,
                GoogleSubject = subject,
                DisplayName = principal.FindFirstValue("name")
            };
            _db.Users.Add(user);
            WasCreated = true;
        }
        else if (user.GoogleSubject.StartsWith("local:", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("To konto korzysta z logowania haslem.");
        if (!user.IsEnabled) throw new UnauthorizedAccessException("Konto jest zablokowane.");

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return user;
    }
}

public class AdminAccessService
{
    private readonly ColdCallDbContext _db;
    private readonly ICurrentUserService _currentUserService;

    public AdminAccessService(ColdCallDbContext db, ICurrentUserService currentUserService)
    {
        _db = db;
        _currentUserService = currentUserService;
    }

    public async Task<AppUser?> RequireAdminAsync(ClaimsPrincipal principal)
    {
        var user = await _currentUserService.GetOrCreateAsync(principal);
        return user.IsAdmin ? user : null;
    }

    public async Task<AppUser?> RequireOwnerAsync(ClaimsPrincipal principal)
    {
        var user = await _currentUserService.GetOrCreateAsync(principal);
        return user.IsAdmin && string.Equals(user.GoogleEmail, "pluczak99@gmail.com", StringComparison.OrdinalIgnoreCase)
            ? user
            : null;
    }
}

public interface IEmailSender
{
    Task SendPlanGrantedAsync(AppUser user, UserPlan userPlan);
}

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendPlanGrantedAsync(AppUser user, UserPlan userPlan)
    {
        var host = _configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogInformation(
                "Email do {Email}: przyznano plan {Plan} do {ExpiresAt}.",
                user.GoogleEmail,
                userPlan.PlanDefinition.Code,
                userPlan.ExpiresAtUtc);
            return;
        }

        using var client = new SmtpClient(host, int.Parse(_configuration["Smtp:Port"] ?? "587"))
        {
            EnableSsl = bool.Parse(_configuration["Smtp:EnableSsl"] ?? "true"),
            Credentials = new NetworkCredential(_configuration["Smtp:User"], _configuration["Smtp:Password"])
        };

        var from = _configuration["Smtp:From"] ?? "noreply@coldcall.local";
        var message = new MailMessage(from, user.GoogleEmail)
        {
            Subject = "Przyznano plan ColdCall",
            Body = $"Przyznano plan {userPlan.PlanDefinition.Code}. Wazny do {userPlan.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC."
        };
        await client.SendMailAsync(message);
    }
}

public static class QueryableExtensions
{
    public static readonly IReadOnlyList<SortableColumnDto> CompanyShortSortableColumns = new[]
    {
        new SortableColumnDto("id", "Id"),
        new SortableColumnDto("name", "Nazwa"),
        new SortableColumnDto("nip", "NIP"),
        new SortableColumnDto("fullAddress", "Adres"),
        new SortableColumnDto("phone", "Telefon"),
        new SortableColumnDto("email", "Email"),
        new SortableColumnDto("www", "Witryna WWW"),
        new SortableColumnDto("dataAktywnosci", "Data aktywno\u015bci"),
        new SortableColumnDto("dataModifiedOn", "Zmodyfikowano"),
        new SortableColumnDto("typyDzialalnosci", "Typ Dzia\u0142alno\u015bci")
    };

    public static IQueryable<CompanyShortDto> ApplyCompanyShortSort(
        this IQueryable<CompanyShortDto> query, string? sortBy, string? direction)
    {
        System.Linq.Expressions.Expression<Func<CompanyShortDto, object?>> key =
            (sortBy ?? "name").ToLowerInvariant() switch
            {
                "id" => x => x.Id,
                "nip" => x => x.Nip,
                "fulladdress" => x => x.FullAddress,
                "phone" => x => x.Phone,
                "email" => x => x.Email,
                "www" => x => x.Www,
                "dataaktywnosci" => x => x.DataAktywnosci,
                "datamodifiedon" => x => x.DataModifiedOn,
                "typydzialalnosci" => x => x.TypyDzialalnosci,
                _ => x => x.Name
            };
        var ordered = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderByDescending(key)
            : query.OrderBy(key);
        return ordered.ThenBy(x => x.Id);
    }

    public static readonly IReadOnlyList<SortableColumnDto> CompanySortableColumns = new[]
    {
        new SortableColumnDto("name", "Nazwa firmy"),
        new SortableColumnDto("businessTypeName", "Typ dzialalnosci"),
        new SortableColumnDto("firstName", "Imie"),
        new SortableColumnDto("lastName", "Nazwisko"),
        new SortableColumnDto("krs", "KRS"),
        new SortableColumnDto("nip", "NIP"),
        new SortableColumnDto("wojewodztwo", "Wojewodztwo"),
        new SortableColumnDto("city", "Miejscowosc"),
        new SortableColumnDto("gmina", "Gmina"),
        new SortableColumnDto("powiat", "Powiat"),
        new SortableColumnDto("postalCode", "Kod"),
        new SortableColumnDto("street", "Ulica"),
        new SortableColumnDto("buildingNumber", "Budynek"),
        new SortableColumnDto("apartmentNumber", "Lokal"),
        new SortableColumnDto("phone", "Telefon"),
        new SortableColumnDto("email", "Email"),
        new SortableColumnDto("googleEmail", "Google Email"),
        new SortableColumnDto("website", "WWW"),
        new SortableColumnDto("instagramAccount", "Instagram"),
        new SortableColumnDto("facebookAccount", "Facebook"),
        new SortableColumnDto("startedAt", "Start"),
        new SortableColumnDto("dataModifiedOn", "Zmodyfikowano")
    };

    public static IOrderedQueryable<Company> ApplyCompanySort(this IQueryable<Company> query, string? sortBy, string? direction)
    {
        var desc = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        return (sortBy ?? "name").ToLowerInvariant() switch
        {
            "firstname" => desc ? query.OrderByDescending(x => x.FirstName) : query.OrderBy(x => x.FirstName),
            "lastname" => desc ? query.OrderByDescending(x => x.LastName) : query.OrderBy(x => x.LastName),
            "nip" => desc ? query.OrderByDescending(x => x.Nip) : query.OrderBy(x => x.Nip),
            "krs" => desc ? query.OrderByDescending(x => x.Krs) : query.OrderBy(x => x.Krs),
            "businesstype" or "businesstypeid" or "businesstypename" => desc ? query.OrderByDescending(x => x.BusinessType!.TypDzialalnosci) : query.OrderBy(x => x.BusinessType!.TypDzialalnosci),
            "wojewodztwo" => desc ? query.OrderByDescending(x => x.Wojewodztwo!.Name) : query.OrderBy(x => x.Wojewodztwo!.Name),
            "city" => desc ? query.OrderByDescending(x => x.Miejscowosc!.Name) : query.OrderBy(x => x.Miejscowosc!.Name),
            "fulladdress" => desc
                ? query.OrderByDescending(x => (x.Miejscowosc!.Name ?? "") + " " + (x.PostalCode ?? "") + " " + (x.Street ?? "") + " " + (x.BuildingNumber ?? "") + (x.ApartmentNumber == null ? "" : "/" + x.ApartmentNumber))
                : query.OrderBy(x => (x.Miejscowosc!.Name ?? "") + " " + (x.PostalCode ?? "") + " " + (x.Street ?? "") + " " + (x.BuildingNumber ?? "") + (x.ApartmentNumber == null ? "" : "/" + x.ApartmentNumber)),
            "gmina" => desc ? query.OrderByDescending(x => x.Gmina!.Name) : query.OrderBy(x => x.Gmina!.Name),
            "powiat" => desc ? query.OrderByDescending(x => x.Powiat!.Name) : query.OrderBy(x => x.Powiat!.Name),
            "postalcode" => desc ? query.OrderByDescending(x => x.PostalCode) : query.OrderBy(x => x.PostalCode),
            "street" => desc ? query.OrderByDescending(x => x.Street) : query.OrderBy(x => x.Street),
            "buildingnumber" => desc ? query.OrderByDescending(x => x.BuildingNumber) : query.OrderBy(x => x.BuildingNumber),
            "apartmentnumber" => desc ? query.OrderByDescending(x => x.ApartmentNumber) : query.OrderBy(x => x.ApartmentNumber),
            "phone" => desc ? query.OrderByDescending(x => x.Phone) : query.OrderBy(x => x.Phone),
            "email" => desc ? query.OrderByDescending(x => x.Email) : query.OrderBy(x => x.Email),
            "googleemail" => desc ? query.OrderByDescending(x => x.GoogleEmail) : query.OrderBy(x => x.GoogleEmail),
            "website" => desc ? query.OrderByDescending(x => x.Website) : query.OrderBy(x => x.Website),
            "instagramaccount" => desc ? query.OrderByDescending(x => x.InstagramAccount) : query.OrderBy(x => x.InstagramAccount),
            "facebookaccount" => desc ? query.OrderByDescending(x => x.FacebookAccount) : query.OrderBy(x => x.FacebookAccount),
            "startedat" => desc ? query.OrderByDescending(x => x.StartedAt) : query.OrderBy(x => x.StartedAt),
            "datamodifiedon" => desc ? query.OrderByDescending(x => x.DataModifiedOn) : query.OrderBy(x => x.DataModifiedOn),
            _ => desc ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name)
        };
    }
}
