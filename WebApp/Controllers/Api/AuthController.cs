using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/auth")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController : ControllerBase
{
    private readonly ColdCallDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly TrialPlanService _trialPlans;
    private readonly CsvExportCleanupService _csvCleanup;

    public AuthController(ColdCallDbContext db, ICurrentUserService currentUserService, TrialPlanService trialPlans,
        CsvExportCleanupService csvCleanup)
    {
        _db = db;
        _currentUserService = currentUserService;
        _trialPlans = trialPlans;
        _csvCleanup = csvCleanup;
    }

    [HttpPost("session")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> CreateSession()
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var claims = new List<Claim>
        {
            new(LocalAuthentication.UserIdClaim, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(LocalAuthentication.GoogleSubjectClaim, user.GoogleSubject),
            new(ClaimTypes.Email, user.GoogleEmail)
        };
        foreach (var type in new[] { "name", "picture" })
            if (User.FindFirstValue(type) is string value) claims.Add(new Claim(type, value));
        var session = LocalAuthentication.SessionProperties();
        await HttpContext.SignInAsync(LocalAuthentication.Scheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, LocalAuthentication.Scheme)),
            session);
        return Ok(new { sessionExpiresAtUtc = session.ExpiresUtc });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var isAdmin = user.IsAdmin;
        await _csvCleanup.DeleteOrphanedAsync(HttpContext.RequestAborted);
        if (!isAdmin)
        {
            await _trialPlans.DeactivateExpiredAsync(user.Id, HttpContext.RequestAborted);
            await _trialPlans.EnsureAssignedAsync(user.Id, HttpContext.RequestAborted);
        }
        var activePlan = await _db.UserPlans
            .Include(x => x.PlanDefinition)
            .Where(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        var maxNumberOfExportedRecords = await PlanRecordLimits.ForUserAsync(_db, user.Id, isAdmin);
        TrialOffer? trialOffer = null;
        var hasExpiredPlan = !isAdmin && activePlan == null && await _db.UserPlans.AnyAsync(
            p => p.AppUserId == user.Id && p.ExpiresAtUtc <= DateTime.UtcNow,
            HttpContext.RequestAborted);
        var hasExpiredTrial = !isAdmin && activePlan == null && await _db.UserPlans.AnyAsync(
            p => p.AppUserId == user.Id && p.PlanDefinition.IsTrial && p.ExpiresAtUtc <= DateTime.UtcNow,
            HttpContext.RequestAborted);
        return Ok(new
        {
            user.Id,
            user.GoogleEmail,
            user.UsesGmail,
            user.DisablePlanPurchase,
            DisplayName = user.DisplayName ?? User.FindFirstValue("name"),
            PictureUrl = User.FindFirstValue("picture"),
            isAdmin,
            hasActivePlan = activePlan != null,
            hasExpiredTrial,
            hasExpiredPlan,
            trialOffer,
            maxNumberOfSearches = activePlan?.PlanDefinition.MaxNumberOfSearches ?? 0,
            maxNumberOfExportedRecords,
            plan = activePlan == null ? null : new
            {
                activePlan.PlanDefinition.Code,
                activePlan.PlanDefinition.IsTrial,
                activePlan.PlanDefinition.DescriptionHtml,
                activePlan.PlanDefinition.MaxNumberOfSearches,
                activePlan.PlanDefinition.EnableCSVDeleting,
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

    [HttpGet("purchases")]
    public async Task<ActionResult<IReadOnlyList<StripePurchaseDto>>> Purchases(
        [FromQuery] string sortBy = "paidAtUtc", [FromQuery] string direction = "desc", CancellationToken token = default)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        if (user.IsAdmin) return Forbid();
        if (sortBy is not ("planCode" or "maxNumberOfSearches" or "maxNumberOfCSVExportsPerSearch"
            or "maxNumberOfExportedRecords" or "amountPaid" or "paidAtUtc") || direction is not ("asc" or "desc"))
            return BadRequest(new { message = "Niepoprawne sortowanie zakupow." });
        var query = _db.StripePayments.AsNoTracking().Where(p => p.AppUserId == user.Id);
        var ordered = (sortBy, direction) switch
        {
            ("planCode", "asc") => query.OrderBy(p => p.PlanDefinition.Code),
            ("planCode", "desc") => query.OrderByDescending(p => p.PlanDefinition.Code),
            ("maxNumberOfSearches", "asc") => query.OrderBy(p => p.PlanDefinition.MaxNumberOfSearches),
            ("maxNumberOfSearches", "desc") => query.OrderByDescending(p => p.PlanDefinition.MaxNumberOfSearches),
            ("maxNumberOfCSVExportsPerSearch", "asc") => query.OrderBy(p => p.PlanDefinition.MaxNumberOfCSVExportsPerSearch),
            ("maxNumberOfCSVExportsPerSearch", "desc") => query.OrderByDescending(p => p.PlanDefinition.MaxNumberOfCSVExportsPerSearch),
            ("maxNumberOfExportedRecords", "asc") => query.OrderBy(p => p.PlanDefinition.MaxNumberOfExportedRecords),
            ("maxNumberOfExportedRecords", "desc") => query.OrderByDescending(p => p.PlanDefinition.MaxNumberOfExportedRecords),
            ("amountPaid", "asc") => query.OrderBy(p => p.AmountPaid),
            ("amountPaid", "desc") => query.OrderByDescending(p => p.AmountPaid),
            ("paidAtUtc", "asc") => query.OrderBy(p => p.PaidAtUtc),
            _ => query.OrderByDescending(p => p.PaidAtUtc)
        };
        var purchases = await ordered.ThenBy(p => p.StripeSessionId)
            .Select(p => new StripePurchaseDto(p.PlanDefinition.Code, p.PlanDefinition.MaxNumberOfSearches,
                p.PlanDefinition.MaxNumberOfCSVExportsPerSearch, p.PlanDefinition.MaxNumberOfExportedRecords,
                p.AmountPaid, p.PaidAtUtc)).ToListAsync(token);
        return Ok(purchases.Select(p => p with { PaidAtUtc = DateTime.SpecifyKind(p.PaidAtUtc, DateTimeKind.Utc) }).ToArray());
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
                x.PlanDefinition.IsTrial,
                x.PlanDefinition.DescriptionHtml,
                x.PlanDefinition.MaxNumberOfSearches,
                x.PlanDefinition.EnableCSVDeleting,
                x.ExpiresAtUtc
            }).FirstOrDefaultAsync();
        var profilePlan = plan == null ? null : new
        {
            plan.Code, plan.IsTrial, plan.DescriptionHtml, plan.MaxNumberOfSearches, plan.EnableCSVDeleting,
            ExpiresAtUtc = DateTime.SpecifyKind(plan.ExpiresAtUtc, DateTimeKind.Utc),
            IsExpired = plan.ExpiresAtUtc <= DateTime.UtcNow
        };
        return Ok(new { user.DisplayName, user.GoogleEmail, user.IsAdmin, plan = profilePlan });
    }
}
