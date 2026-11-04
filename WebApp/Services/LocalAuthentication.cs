using System.Security.Claims;
using ColdCall.Data;
using ColdCall.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public static class LocalAuthentication
{
    public const string Scheme = "ColdCallSession";
    public const string UserIdClaim = "coldcall:user_id";
    public const string StampClaim = "coldcall:security_stamp";
    public const string GoogleSubjectClaim = "coldcall:google_subject";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public static AuthenticationProperties SessionProperties() => new()
    {
        IsPersistent = true,
        AllowRefresh = false,
        IssuedUtc = DateTimeOffset.UtcNow,
        ExpiresUtc = DateTimeOffset.UtcNow.Add(SessionLifetime)
    };

    public static ClaimsPrincipal Principal(LocalAccount account) => new(new ClaimsIdentity(new[]
    {
        new Claim(UserIdClaim, account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Claim(StampClaim, account.SecurityStamp ?? ""),
        new Claim(ClaimTypes.Email, account.Email ?? "")
    }, Scheme));

    public static async Task ValidateCookie(CookieValidatePrincipalContext context)
    {
        if (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            context.RejectPrincipal();
            return;
        }
        try { await ValidateCookieCore(context); }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            // The browser discarded this request; do not turn cancellation into an authentication failure.
            context.RejectPrincipal();
        }
    }

    private static async Task ValidateCookieCore(CookieValidatePrincipalContext context)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<ColdCallDbContext>();
        var id = context.Principal?.FindFirstValue(UserIdClaim);
        var googleSubject = context.Principal?.FindFirstValue(GoogleSubjectClaim);
        if (googleSubject != null)
        {
            if (!int.TryParse(id, out var userId)) { context.RejectPrincipal(); return; }
            var enabled = await db.Users.Where(u => u.Id == userId && u.GoogleSubject == googleSubject)
                .Select(u => (bool?)u.IsEnabled).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (enabled.HasValue) context.HttpContext.Items[AccountEnabledMiddleware.EnabledItem] = enabled.Value;
            if (enabled != true) context.RejectPrincipal();
            return;
        }
        var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<LocalAccount>>();
        var account = id == null ? null : await manager.FindByIdAsync(id);
        if (account == null || !account.EmailConfirmed || account.SecurityStamp != context.Principal?.FindFirstValue(StampClaim))
        { context.RejectPrincipal(); return; }
        var localEnabled = await db.Users.Where(u => u.Id == account.Id).Select(u => (bool?)u.IsEnabled)
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        if (localEnabled.HasValue) context.HttpContext.Items[AccountEnabledMiddleware.EnabledItem] = localEnabled.Value;
        if (localEnabled != true) context.RejectPrincipal();
    }
}
