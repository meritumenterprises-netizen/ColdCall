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

    public AuthController(ColdCallDbContext db, ICurrentUserService currentUserService)
    {
        _db = db;
        _currentUserService = currentUserService;
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var isAdmin = await _db.AdminEmails.AnyAsync(x => x.Email == user.GoogleEmail);
        var activePlan = await _db.UserPlans
            .Include(x => x.PlanDefinition)
            .Where(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefaultAsync();

        var maxNumberOfExportedRecords = await PlanRecordLimits.ForUserAsync(_db, user.Id, isAdmin);
        return Ok(new
        {
            user.Id,
            user.GoogleEmail,
            user.UsesGmail,
            DisplayName = user.DisplayName ?? User.FindFirstValue("name"),
            PictureUrl = User.FindFirstValue("picture"),
            isAdmin,
            hasActivePlan = activePlan != null,
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

    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var plan = await _db.UserPlans.AsNoTracking()
            .Where(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .Select(x => new
            {
                x.PlanDefinition.Code,
                x.PlanDefinition.DescriptionHtml,
                x.PlanDefinition.MaxNumberOfSearches,
                x.ExpiresAtUtc
            }).FirstOrDefaultAsync();
        return Ok(new { user.DisplayName, user.GoogleEmail, user.IsAdmin, plan });
    }
}
