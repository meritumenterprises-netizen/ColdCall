using System.Security.Cryptography;
using System.Text;
using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/account")]
[EnableRateLimiting("local-auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccountController(ColdCallDbContext db, LocalAuthDbContext credentials, UserManager<LocalAccount> manager,
    IAuthEmailSender emailSender, TrialPlanService trials, IConfiguration configuration) : ControllerBase
{
    private const string MailMessage = "Sprawdz skrzynke email, aby kontynuowac. Jesli konto istnieje, otrzymasz odpowiednia wiadomosc.";
    private static readonly LocalAccount DummyAccount = new();
    private static readonly string DummyHash = new PasswordHasher<LocalAccount>().HashPassword(DummyAccount, Guid.NewGuid().ToString("N"));
    private IActionResult GoogleRequired() => Conflict(new { code = "GoogleSignInRequired", message = "zaloguj sie przez Google" });
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
    private static Task<bool> RequiresGoogle(string email) => Task.FromResult(
        email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
        || email.EndsWith("@googlemail.com", StringComparison.OrdinalIgnoreCase));

    [AllowAnonymous, HttpGet("csrf"), DisableRateLimiting]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    [AllowAnonymous, HttpPost("register")]
    public async Task<IActionResult> Register(RegistrationRequest request)
    {
        var email = Normalize(request.Email);
        if (await RequiresGoogle(email)) return GoogleRequired();
        if (string.IsNullOrWhiteSpace(request.DisplayName)) return BadRequest(new { message = "Podaj imie i nazwisko." });
        await using var transaction = await db.Database.BeginTransactionAsync();
        await credentials.Database.UseTransactionAsync(transaction.GetDbTransaction());
        var resource = "RegisterEmail:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email)));
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 50001, 'Registration lock unavailable', 1;");
        if (await db.Users.AnyAsync(u => u.GoogleEmail == email)) return Ok(new { message = MailMessage });
        var user = new AppUser
        {
            GoogleEmail = email, GoogleSubject = "local:" + Guid.NewGuid().ToString("N"),
            DisplayName = request.DisplayName.Trim(), IsAdmin = false, IsEnabled = true, UsesGmail = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var account = new LocalAccount { Id = user.Id, UserName = email, Email = email, LockoutEnabled = true };
        var result = await manager.CreateAsync(account, request.Password);
        if (!result.Succeeded) return BadRequest(new { message = "Haslo musi miec co najmniej 12 znakow. " + string.Join(" ", result.Errors.Select(e => e.Description)) });
        await transaction.CommitAsync();
        await credentials.Database.UseTransactionAsync(null);
        await SendConfirmation(account);
        return Ok(new { message = MailMessage });
    }

    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login(PasswordLoginRequest request)
    {
        var email = Normalize(request.Email);
        if (await RequiresGoogle(email)) return GoogleRequired();
        var account = await manager.FindByEmailAsync(email);
        var user = account == null ? null : await db.Users.SingleOrDefaultAsync(u => u.Id == account.Id);
        if (account == null || user == null || !user.IsEnabled || await manager.IsLockedOutAsync(account))
        {
            manager.PasswordHasher.VerifyHashedPassword(DummyAccount, DummyHash, request.Password);
            return Unauthorized(new { message = "Niepoprawny email lub haslo albo konto jest zablokowane." });
        }
        if (!await manager.CheckPasswordAsync(account, request.Password))
        {
            await manager.AccessFailedAsync(account);
            return Unauthorized(new { message = "Niepoprawny email lub haslo albo konto jest zablokowane." });
        }
        if (!account.EmailConfirmed) return BadRequest(new { code = "EmailNotConfirmed", message = "Potwierdz adres email przed zalogowaniem." });
        await manager.ResetAccessFailedCountAsync(account);
        TrialOffer? offer = null;
        if (!account.TrialOffered && !await db.UserPlans.AnyAsync(p => p.AppUserId == user.Id))
        {
            offer = await trials.OfferAsync(user.Id);
            account.TrialOffered = true;
            await manager.UpdateAsync(account);
        }
        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await HttpContext.SignInAsync(LocalAuthentication.Scheme, LocalAuthentication.Principal(account),
            new AuthenticationProperties { IsPersistent = false });
        return Ok(new { trialOffer = offer });
    }

    [AllowAnonymous, HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(EmailTokenRequest request)
    {
        var account = await manager.FindByEmailAsync(Normalize(request.Email));
        var token = DecodeToken(request.Token);
        if (account == null || token == null || !(await manager.ConfirmEmailAsync(account, token)).Succeeded)
            return BadRequest(new { message = "Link potwierdzajacy jest nieprawidlowy lub wygasl." });
        return Ok(new { message = "Email potwierdzony. Mozesz sie zalogowac." });
    }

    [AllowAnonymous, HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(EmailRequest request)
    {
        if (await RequiresGoogle(Normalize(request.Email))) return GoogleRequired();
        var account = await manager.FindByEmailAsync(Normalize(request.Email));
        if (account != null && !account.EmailConfirmed) await SendConfirmation(account);
        return Ok(new { message = MailMessage });
    }

    [AllowAnonymous, HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailRequest request)
    {
        var email = Normalize(request.Email);
        if (await RequiresGoogle(email)) return GoogleRequired();
        var account = await manager.FindByEmailAsync(email);
        if (account != null && account.EmailConfirmed && await db.Users.AnyAsync(u => u.Id == account.Id && u.IsEnabled))
        {
            var token = await manager.GeneratePasswordResetTokenAsync(account);
            await emailSender.SendAsync(email, "FindMeNow - reset hasla", "Aby ustawic nowe haslo, otworz:\n" + Link("reset-password", email, token), HttpContext.RequestAborted);
        }
        return Ok(new { message = MailMessage });
    }

    [AllowAnonymous, HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        if (await RequiresGoogle(Normalize(request.Email))) return GoogleRequired();
        var account = await manager.FindByEmailAsync(Normalize(request.Email));
        var token = DecodeToken(request.Token);
        if (account == null || token == null || !account.EmailConfirmed || !(await manager.ResetPasswordAsync(account, token, request.Password)).Succeeded)
            return BadRequest(new { message = "Link jest nieprawidlowy lub wygasl albo haslo ma mniej niz 12 znakow." });
        return Ok(new { message = "Haslo zmienione. Zaloguj sie nowym haslem." });
    }

    [AllowAnonymous, HttpPost("logout"), DisableRateLimiting]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(LocalAuthentication.Scheme);
        return Ok();
    }

    private async Task SendConfirmation(LocalAccount account)
    {
        var token = await manager.GenerateEmailConfirmationTokenAsync(account);
        await emailSender.SendAsync(account.Email!, "FindMeNow - potwierdz email", "Aby potwierdzic adres email, otworz:\n"
            + Link("confirm-email", account.Email!, token), HttpContext.RequestAborted);
    }

    private string Link(string mode, string email, string token)
    {
        var frontend = configuration["LocalAuth:FrontendUrl"];
        if (!Uri.TryCreate(frontend, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("Ustaw LocalAuth:FrontendUrl na adres HTTPS witryny Angular.");
        return QueryHelpers.AddQueryString(uri.ToString(), new Dictionary<string, string?>
        {
            ["mode"] = mode, ["email"] = email, ["token"] = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))
        });
    }

    private static string? DecodeToken(string value)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value)); }
        catch (FormatException) { return null; }
    }
}
