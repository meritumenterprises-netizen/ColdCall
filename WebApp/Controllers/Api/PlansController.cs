using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;
using Stripe;

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
        var currentPlan = await _db.UserPlans
            .Where(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .Select(x => new
            {
                x.PlanDefinitionId,
                x.PlanDefinition.Code,
                x.PlanDefinition.IsTrial,
                x.PlanDefinition.IsUpgrade,
                x.PlanDefinition.UpgradeFrom,
                x.PlanDefinition.MaxNumberOfSearches
            })
            .FirstOrDefaultAsync();
        var currentPlanId = currentPlan?.PlanDefinitionId;
        var showUpgradePlans = currentPlan != null && !currentPlan.IsTrial;
        var upgradeSourceId = currentPlan != null && currentPlan.IsUpgrade
            ? currentPlan.UpgradeFrom : currentPlanId;
        var currentSearchLimit = currentPlan?.MaxNumberOfSearches ?? 0;
        var specificUpgradeCodes = PlanOfferRules.UpgradeCodesFor(currentPlan?.Code);
        var currentPlanOrder = await _db.UserPlans
            .Where(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow)
            .Select(x => (int?)x.PlanDefinition.Order).MaxAsync();
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
            x => (DateTime?)DateTime.SpecifyKind(x.ActiveUntil, DateTimeKind.Utc));

        var plans = await _db.PlanDefinitions
            .Where(x => (showUpgradePlans && x.Id == currentPlanId)
                || (x.ShowInOffer && !x.IsTrial
                    && (showUpgradePlans
                        ? x.IsUpgrade && (specificUpgradeCodes.Length > 0
                            ? specificUpgradeCodes.Contains(x.Code)
                            : x.UpgradeFrom == upgradeSourceId && x.MaxNumberOfSearches > currentSearchLimit)
                        : !x.IsUpgrade)))
            .OrderByDescending(x => showUpgradePlans && x.Id == currentPlanId)
            .ThenBy(x => x.Order).ThenBy(x => x.Id)
            .ToListAsync();

        return plans
            .Select(x => new PlanDto(
                x.Code,
                x.MaxNumberOfSearches,
                x.DescriptionHtml,
                x.Price,
                x.Currency,
                activeUntilByDefinitionId.TryGetValue(x.Id, out var activeUntil) ? activeUntil : null,
                x.Order,
                x.Id != currentPlanId && ((showUpgradePlans && x.IsUpgrade && (specificUpgradeCodes.Length > 0
                        ? specificUpgradeCodes.Contains(x.Code)
                        : x.UpgradeFrom == upgradeSourceId && x.MaxNumberOfSearches > currentSearchLimit))
                    || !currentPlanOrder.HasValue || x.Order > currentPlanOrder.Value)))
            .ToList();
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(CheckoutRequest request, [FromServices] StripePaymentService payments, CancellationToken token)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        try
        {
            var session = await payments.CreateAsync(user, request.PlanCode, token);
            return Ok(new { session.Url });
        }
        catch (InvalidOperationException e) { return BadRequest(new { message = e.Message }); }
        catch (StripeException)
        {
            return StatusCode(502, new { message = "Nie udalo sie utworzyc platnosci Stripe. Sprobuj ponownie." });
        }
    }

    [HttpPost("confirm-checkout")]
    public async Task<IActionResult> ConfirmCheckout(ConfirmCheckoutRequest request,
        [FromServices] StripePaymentService payments, CancellationToken token)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        try
        {
            var paid = await payments.FulfillAsync(request.SessionId, user.Id, token);
            return Ok(new { paid });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException e) { return BadRequest(new { message = e.Message }); }
        catch (StripeException) { return StatusCode(502, new { message = "Nie udalo sie potwierdzic platnosci Stripe." }); }
    }

    [AllowAnonymous, HttpPost("stripe-webhook")]
    public async Task<IActionResult> StripeWebhook([FromServices] StripePaymentService payments,
        [FromServices] IConfiguration configuration, CancellationToken token)
    {
        var secret = configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret)) return StatusCode(503);
        var signature = Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature)) return BadRequest();
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(token);
        Event stripeEvent;
        try { stripeEvent = EventUtility.ConstructEvent(payload, signature, secret, throwOnApiVersionMismatch: false); }
        catch (Exception e) when (e is StripeException or System.Text.Json.JsonException or Newtonsoft.Json.JsonException)
        { return BadRequest(); }
        if (stripeEvent.Type is "checkout.session.completed" or "checkout.session.async_payment_succeeded"
            && stripeEvent.Data.Object is Session session && session.Metadata?.GetValueOrDefault("application") == "ColdCall")
            await payments.FulfillAsync(session.Id, null, token);
        return Ok();
    }
}
