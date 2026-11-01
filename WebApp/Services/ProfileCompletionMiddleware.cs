using System.Security.Claims;
using ColdCall.Data;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public class ProfileCompletionMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/session", "/api/auth/me", "/api/auth/terms", "/api/auth/profile"
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
        var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.FindFirstValue("email");
        var user = int.TryParse(id, out var userId)
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == userId, context.RequestAborted)
            : await db.Users.AsNoTracking().FirstOrDefaultAsync(row => row.GoogleEmail == email, context.RequestAborted);
        if (user != null && UserProfileCompletion.IsRequired(user))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { code = "ProfileCompletionRequired", message = "Uzupelnij wymagane dane w Profil / Dane." }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
