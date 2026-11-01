using System.Security.Claims;
using ColdCall.Data;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public class TermsAcceptanceMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/session", "/api/auth/me", "/api/auth/terms", "/api/public/regulamin"
    };

    public async Task InvokeAsync(HttpContext context, ColdCallDbContext db)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api") || path.StartsWithSegments("/api/account")
            || context.User.Identity?.IsAuthenticated != true || AllowedPaths.Contains(path.Value?.TrimEnd('/') ?? ""))
        {
            await next(context);
            return;
        }
        var id = context.User.FindFirstValue(LocalAuthentication.UserIdClaim);
        bool accepted;
        if (int.TryParse(id, out var userId))
            accepted = await db.Users.AnyAsync(user => user.Id == userId && (user.IsAdmin || user.TermsOfServiceAccepted == true), context.RequestAborted);
        else
        {
            var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.FindFirstValue("email");
            accepted = email != null && await db.Users.AnyAsync(user => user.GoogleEmail == email && (user.IsAdmin || user.TermsOfServiceAccepted == true), context.RequestAborted);
        }
        if (!accepted)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { code = "TermsAcceptanceRequired", message = "Wymagana akceptacja regulaminu." }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
