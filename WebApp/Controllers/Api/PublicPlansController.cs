using System.Security.Claims;
using ColdCall.Services;
using ColdCall.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[AllowAnonymous]
[Route("api/public/plans")]
public sealed class PublicPlansController(ColdCallDbContext db) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var id = User.FindFirstValue(LocalAuthentication.UserIdClaim);
            var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            var blocked = int.TryParse(id, out var userId)
                ? await db.Users.AnyAsync(user => user.Id == userId && user.DisablePlanPurchase, token)
                : email != null && await db.Users.AnyAsync(user => user.GoogleEmail == email && user.DisablePlanPurchase, token);
            if (blocked) return Ok(Array.Empty<object>());
        }
        return Ok(await db.PlanDefinitions.AsNoTracking()
            .Where(plan => plan.IsActive && plan.ShowInOffer && !plan.IsTrial && !plan.IsUpgrade)
            .OrderBy(plan => plan.Code == "PX100" ? 1 : 0)
            .ThenBy(plan => plan.Order).ThenBy(plan => plan.Id)
            .Select(plan => new
            {
                plan.Id, plan.Code, plan.DescriptionHtml, plan.Price, plan.Currency
            }).ToListAsync(token));
    }
}
