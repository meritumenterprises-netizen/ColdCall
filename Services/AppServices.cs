using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using ColdCall.Data;
using ColdCall.Models;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public interface ICurrentUserService
{
    Task<AppUser> GetOrCreateAsync(ClaimsPrincipal principal);
}

public class CurrentUserService : ICurrentUserService
{
    private readonly ColdCallDbContext _db;

    public CurrentUserService(ColdCallDbContext db)
    {
        _db = db;
    }

    public async Task<AppUser> GetOrCreateAsync(ClaimsPrincipal principal)
    {
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
        }

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
        var isAdmin = await _db.AdminEmails.AnyAsync(x => x.Email == user.GoogleEmail);
        return isAdmin ? user : null;
    }

    public async Task<AppUser?> RequireOwnerAsync(ClaimsPrincipal principal)
    {
        var user = await _currentUserService.GetOrCreateAsync(principal);
        return string.Equals(user.GoogleEmail, "pluczak99@gmail.com", StringComparison.OrdinalIgnoreCase)
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
    public static IQueryable<Company> ApplyCompanySort(this IQueryable<Company> query, string? sortBy, string? direction)
    {
        var desc = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        return (sortBy ?? "name").ToLowerInvariant() switch
        {
            "nip" => desc ? query.OrderByDescending(x => x.Nip) : query.OrderBy(x => x.Nip),
            "krs" => desc ? query.OrderByDescending(x => x.Krs) : query.OrderBy(x => x.Krs),
            "city" => desc ? query.OrderByDescending(x => x.Miejscowosc!.Name) : query.OrderBy(x => x.Miejscowosc!.Name),
            "postalcode" => desc ? query.OrderByDescending(x => x.PostalCode) : query.OrderBy(x => x.PostalCode),
            "startedat" => desc ? query.OrderByDescending(x => x.StartedAt) : query.OrderBy(x => x.StartedAt),
            _ => desc ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name)
        };
    }
}
