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
        var profiles = await _db.SearchProfiles.AsNoTracking()
            .Where(x => x.AppUserId == user.Id)
            .OrderBy(x => x.Name)
            .Include(x => x.Criteria)
            .Include(x => x.PkdCodes).ThenInclude(x => x.PkdCode)
            .AsSplitQuery()
            .ToListAsync();
        return profiles.Select(x => new SearchProfileDto(x.Id, x.Name,
            x.PkdCodes.Select(p => new PkdDto(p.PkdCode.Id, p.PkdCode.Code, p.PkdCode.Name)).OrderBy(p => p.Code).ToList(),
            ToCriteriaDto(x.Criteria))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<SearchProfileDto>> Save(SaveSearchProfileDto request)
    {
        var criteria = request.Criteria ?? new SearchProfileCriteriaDto();
        var validation = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(criteria,
            new System.ComponentModel.DataAnnotations.ValidationContext(criteria), validation, true))
            return BadRequest(new { message = string.Join(" ", validation.Select(x => x.ErrorMessage)) });
        if (!criteria.DateRangeEnabled) { criteria.DateFrom = null; criteria.DateTo = null; }
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

        var profile = new SearchProfile
        {
            AppUserId = user.Id, Name = request.Name,
            Criteria = new SearchProfileCriteria
            {
                SortColumn = criteria.SortColumn, SortDirection = criteria.SortDirection,
                Address = criteria.Address, HasPhone = criteria.HasPhone, HasEmail = criteria.HasEmail,
                HasWebsite = criteria.HasWebsite, DateRangeEnabled = criteria.DateRangeEnabled,
                DateFrom = criteria.DateFrom, DateTo = criteria.DateTo, RecordLimit = criteria.RecordLimit
            }
        };
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

        return Ok(new SearchProfileDto(profile.Id, profile.Name, pkds, ToCriteriaDto(profile.Criteria)));
    }

    private static SearchProfileCriteriaDto ToCriteriaDto(SearchProfileCriteria? criteria) =>
        criteria == null ? new SearchProfileCriteriaDto() : new SearchProfileCriteriaDto
        {
            SortColumn = criteria.SortColumn, SortDirection = criteria.SortDirection,
            Address = criteria.Address, HasPhone = criteria.HasPhone, HasEmail = criteria.HasEmail,
            HasWebsite = criteria.HasWebsite, DateRangeEnabled = criteria.DateRangeEnabled,
            DateFrom = criteria.DateFrom, DateTo = criteria.DateTo, RecordLimit = criteria.RecordLimit
        };
}
