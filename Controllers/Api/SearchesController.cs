using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/searches")]
[Authorize]
public class SearchesController : ControllerBase
{
    private readonly ColdCallDbContext _db;
    private readonly ICurrentUserService _currentUserService;

    public SearchesController(ColdCallDbContext db, ICurrentUserService currentUserService)
    {
        _db = db;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<IReadOnlyList<SearchProfileDto>> Get()
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        return await _db.SearchProfiles
            .Where(x => x.AppUserId == user.Id)
            .OrderBy(x => x.Name)
            .Select(x => new SearchProfileDto(
                x.Id,
                x.Name,
                x.PkdCodes.Select(p => new PkdDto(p.PkdCode.Id, p.PkdCode.Code, p.PkdCode.Name)).ToList()))
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<SearchProfileDto>> Save(SaveSearchProfileDto request)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var activePlan = await _db.UserPlans
            .Include(x => x.PlanDefinition)
            .Where(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefaultAsync();
        if (activePlan == null)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Plan wygasl." });
        }

        var currentCount = await _db.SearchProfiles.CountAsync(x => x.AppUserId == user.Id);
        if (currentCount >= activePlan.PlanDefinition.SearchLimit)
        {
            return BadRequest(new { message = $"Limit planu {activePlan.PlanDefinition.Code} to {activePlan.PlanDefinition.SearchLimit} zapisanych wyszukan." });
        }

        var profile = new SearchProfile { AppUserId = user.Id, Name = request.Name };
        foreach (var pkdId in request.PkdCodeIds.Distinct())
        {
            profile.PkdCodes.Add(new SearchProfilePkd { PkdCodeId = pkdId });
        }

        _db.SearchProfiles.Add(profile);
        await _db.SaveChangesAsync();

        var pkds = await _db.PkdCodes
            .Where(x => request.PkdCodeIds.Contains(x.Id))
            .Select(x => new PkdDto(x.Id, x.Code, x.Name))
            .ToListAsync();

        return Ok(new SearchProfileDto(profile.Id, profile.Name, pkds));
    }
}
