using ColdCall.Data;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly ColdCallDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly TrialPlanService _trialPlans;

    public AuthController(ColdCallDbContext db, ICurrentUserService currentUserService, TrialPlanService trialPlans)
    {
        _db = db;
        _currentUserService = currentUserService;
        _trialPlans = trialPlans;
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var isAdmin = user.IsAdmin;
        var activePlan = await _db.UserPlans
            .Include(x => x.PlanDefinition)
            .Where(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefaultAsync();

        var maxNumberOfExportedRecords = await PlanRecordLimits.ForUserAsync(_db, user.Id, isAdmin);
        var trialOffer = _currentUserService.WasCreated ? await _trialPlans.OfferAsync(user.Id) : null;
        return Ok(new
        {
            user.Id,
            user.GoogleEmail,
            user.UsesGmail,
            DisplayName = user.DisplayName ?? User.FindFirstValue("name"),
            PictureUrl = User.FindFirstValue("picture"),
            isAdmin,
            hasActivePlan = activePlan != null,
            trialOffer,
            maxNumberOfSearches = activePlan?.PlanDefinition.MaxNumberOfSearches ?? 0,
            maxNumberOfExportedRecords,
            plan = activePlan == null ? null : new
            {
                activePlan.PlanDefinition.Code,
                activePlan.PlanDefinition.DescriptionHtml,
                activePlan.PlanDefinition.MaxNumberOfSearches,
                activePlan.ExpiresAtUtc
            }
        });
    }

    [HttpPost("accept-trial")]
    public async Task<IActionResult> AcceptTrial(TrialAcceptance request, CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        if (!await _trialPlans.AcceptAsync(user.Id, request, cancellationToken))
            return BadRequest(new { message = "Nie mozna aktywowac tej oferty probnej. Oferta wygasla, zostala wykorzystana lub nie zaakceptowano warunkow." });
        return Ok();
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var plan = await _db.UserPlans.AsNoTracking()
            .Where(x => x.AppUserId == user.Id)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .Select(x => new
            {
                x.PlanDefinition.Code,
                x.PlanDefinition.DescriptionHtml,
                x.PlanDefinition.MaxNumberOfSearches,
                x.ExpiresAtUtc
            }).FirstOrDefaultAsync();
        var profilePlan = plan == null ? null : new
        {
            plan.Code, plan.DescriptionHtml, plan.MaxNumberOfSearches,
            ExpiresAtUtc = DateTime.SpecifyKind(plan.ExpiresAtUtc, DateTimeKind.Utc),
            IsExpired = plan.ExpiresAtUtc <= DateTime.UtcNow
        };
        return Ok(new { user.DisplayName, user.GoogleEmail, user.IsAdmin, plan = profilePlan });
    }
}
