using System.Security.Claims;
using ColdCall.Data;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public sealed class AccountEnabledMiddleware(RequestDelegate next)
{
    internal const string EnabledItem = "ColdCall.AccountEnabled";

    public async Task InvokeAsync(HttpContext context, ColdCallDbContext db)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api") || path.Equals(new PathString("/api/account/logout"))
            || path.Equals(new PathString("/api/account/csrf")))
        {
            await next(context);
            return;
        }

        var enabled = context.Items[EnabledItem] as bool?;
        if (!enabled.HasValue && context.User.Identity?.IsAuthenticated == true)
        {
            var id = context.User.FindFirstValue(LocalAuthentication.UserIdClaim);
            if (int.TryParse(id, out var userId))
                enabled = await db.Users.Where(u => u.Id == userId).Select(u => (bool?)u.IsEnabled)
                    .SingleOrDefaultAsync(context.RequestAborted);
            else
            {
                var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.FindFirstValue("email");
                if (email != null)
                    enabled = await db.Users.Where(u => u.GoogleEmail == email).Select(u => (bool?)u.IsEnabled)
                        .FirstOrDefaultAsync(context.RequestAborted);
            }
        }
        if (enabled == false)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new
            {
                code = "AccountDisabled",
                message = "Administrator zablokowa\u0142 Twoje konto"
            }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
