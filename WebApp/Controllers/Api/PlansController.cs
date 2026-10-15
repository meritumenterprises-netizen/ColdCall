using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/plans")]
[Authorize]
public class PlansController : ControllerBase
{
    private readonly ColdCallDbContext _db;
    private readonly ICurrentUserService _currentUserService;

    public PlansController(ColdCallDbContext db, ICurrentUserService currentUserService)
    {
        _db = db;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<IReadOnlyList<PlanDto>> Get()
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var activePlansByDefinitionId = await _db.UserPlans
            .Where(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow)
            .GroupBy(x => x.PlanDefinitionId)
            .Select(x => new
            {
                PlanDefinitionId = x.Key,
                ActiveUntil = x.Max(p => p.ExpiresAtUtc)
            })
            .ToListAsync();
        var activeUntilByDefinitionId = activePlansByDefinitionId.ToDictionary(
            x => x.PlanDefinitionId,
            x => (DateTime?)x.ActiveUntil);

        var plans = await _db.PlanDefinitions
            .Where(x => x.ShowInOffer && !x.IsTrial)
            .OrderBy(x => x.MaxNumberOfSearches)
            .ToListAsync();

        return plans
            .Select(x => new PlanDto(
                x.Code,
                x.MaxNumberOfSearches,
                x.DescriptionHtml,
                x.Price,
                x.Currency,
                activeUntilByDefinitionId.TryGetValue(x.Id, out var activeUntil) ? activeUntil : null))
            .ToList();
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(CheckoutRequest request)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var plan = await _db.PlanDefinitions.FirstOrDefaultAsync(x => x.Code == request.PlanCode);
        if (plan == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(plan.StripePriceId))
        {
            return BadRequest(new { message = "Dla planu nie skonfigurowano StripePriceId w bazie danych." });
        }

        var service = new SessionService();
        var session = await service.CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            CustomerEmail = user.GoogleEmail,
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Price = plan.StripePriceId, Quantity = 1 }
            },
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = user.Id.ToString(),
                ["planCode"] = plan.Code
            }
        });

        return Ok(new { session.Url });
    }
}
