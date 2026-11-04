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
        return Ok(await db.PlanDefinitions.AsNoTracking()
            .Where(plan => plan.IsActive && plan.ShowInOffer && !plan.IsTrial && !plan.IsUpgrade)
            .OrderBy(plan => plan.Order).ThenBy(plan => plan.Id)
            .Select(plan => new
            {
                plan.Id, plan.Code, plan.DescriptionHtml, plan.Price, plan.Currency
            }).ToListAsync(token));
    }
}
