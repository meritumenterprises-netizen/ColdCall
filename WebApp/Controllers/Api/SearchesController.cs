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
            .OrderByDescending(x => x.LastSavedAtUtc ?? x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Include(x => x.Criteria)
            .Include(x => x.PkdCodes).ThenInclude(x => x.PkdCode)
            .AsSplitQuery()
            .ToListAsync();
        return profiles.Select(x => new SearchProfileDto(x.Id, x.Name,
            x.PkdCodes.Select(p => new PkdDto(p.PkdCode.Id, p.PkdCode.Code, p.PkdCode.Name)).OrderBy(p => p.Code).ToList(),
            ToCriteriaDto(x.Criteria))).ToList();
    }

    [HttpPost]
    public Task<ActionResult<SearchProfileDto>> Save(SaveSearchProfileDto request) => SaveCore(request, null);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromServices] CsvExportCleanupService cleanup, CancellationToken token)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        return await cleanup.DeleteSearchAsync(user.Id, id, token) ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/check-csv-export")]
    public async Task<ActionResult> CheckCsvExport(int id, CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var profile = await _db.SearchProfiles.Include(p => p.Criteria).Include(p => p.PkdCodes)
            .AsSplitQuery().SingleOrDefaultAsync(p => p.Id == id && p.AppUserId == user.Id, cancellationToken);
        if (profile == null) return NotFound();
        profile.SearchProfileHash = SearchProfileHashCalculator.Calculate(profile);
        var exportedProfiles = await _db.SearchProfiles.Include(p => p.Criteria).Include(p => p.PkdCodes)
            .Where(p => p.AppUserId == user.Id && _db.CsvExports.Any(e => e.SearchProfileId == p.Id && e.SearchProfileHash == null))
            .AsSplitQuery().ToListAsync(cancellationToken);
        // Preserve a snapshot, rather than comparing exports against mutable profile criteria.
        foreach (var exported in exportedProfiles)
            await SearchProfileHashCalculator.FreezeLegacyExportsAsync(_db, exported, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        var exists = await _db.CsvExports.AnyAsync(e => e.SearchProfile != null && e.SearchProfile.AppUserId == user.Id
            && e.SearchProfileHash == profile.SearchProfileHash, cancellationToken);
        return Ok(new { hash = profile.SearchProfileHash, exists });
    }

    [HttpPut("{id:int}")]
    public Task<ActionResult<SearchProfileDto>> Update(int id, SaveSearchProfileDto request) => SaveCore(request, id);

    private async Task<ActionResult<SearchProfileDto>> SaveCore(SaveSearchProfileDto request, int? id)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 128)
            return BadRequest(new { message = "Podaj nazwe wyszukiwania (maksymalnie 128 znakow)." });
        var criteria = request.Criteria ?? new SearchProfileCriteriaDto();
        var validation = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(criteria,
            new System.ComponentModel.DataAnnotations.ValidationContext(criteria), validation, true))
            return BadRequest(new { message = string.Join(" ", validation.Select(x => x.ErrorMessage)) });
        if (!criteria.DateRangeEnabled) { criteria.DateFrom = null; criteria.DateTo = null; }
        var user = await _currentUserService.GetOrCreateAsync(User);
        var profile = id.HasValue ? await _db.SearchProfiles.Include(x => x.Criteria).Include(x => x.PkdCodes)
            .SingleOrDefaultAsync(x => x.Id == id.Value && x.AppUserId == user.Id) : null;
        if (id.HasValue && profile == null) return NotFound();
        var name = request.Name.Trim();
        if (await _db.SearchProfiles.AnyAsync(x => x.AppUserId == user.Id && x.Name == name && (!id.HasValue || x.Id != id.Value)))
            return Conflict(new { message = "Nazwa wyszukiwania ju\u017c istnieje" });
        var activePlan = await _db.UserPlans
            .Include(x => x.PlanDefinition)
            .Where(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefaultAsync();
        if (activePlan == null)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Plan wygasl." });
        }

        var maximumRecordLimit = await PlanRecordLimits.ForUserAsync(_db, user.Id, false);
        if (maximumRecordLimit <= 0)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Plan nie pozwala na pobieranie rekordow." });
        criteria.RecordLimit = maximumRecordLimit;
        var currentCount = await _db.SearchProfiles.CountAsync(x => x.AppUserId == user.Id);
        if (!id.HasValue && currentCount >= activePlan.PlanDefinition.MaxNumberOfSearches)
        {
            return BadRequest(new { message = $"Limit planu {activePlan.PlanDefinition.Code} to {activePlan.PlanDefinition.MaxNumberOfSearches} zapisanych wyszukan." });
        }

        if (profile == null)
        {
            profile = new SearchProfile { AppUserId = user.Id };
            _db.SearchProfiles.Add(profile);
        }
        if (id.HasValue) await SearchProfileHashCalculator.FreezeLegacyExportsAsync(_db, profile);
        profile.Name = request.Name.Trim();
        profile.Criteria ??= new SearchProfileCriteria();
        _db.Entry(profile.Criteria).CurrentValues.SetValues(new SearchProfileCriteria
            {
                Id = profile.Criteria.Id, SearchProfileId = profile.Id, Name = profile.Name,
                SortColumn = criteria.SortColumn, SortDirection = criteria.SortDirection,
                CompanyName = criteria.CompanyName, Address = criteria.Address, HasPhone = criteria.HasPhone, HasEmail = criteria.HasEmail,
                HasWebsite = criteria.HasWebsite, DateRangeEnabled = criteria.DateRangeEnabled,
                DateFrom = criteria.DateFrom, DateTo = criteria.DateTo, RecordLimit = criteria.RecordLimit,
                WojewodztwoId = criteria.WojewodztwoId, PowiatId = criteria.PowiatId,
                GminaId = criteria.GminaId, MiejscowoscId = criteria.MiejscowoscId,
                HasFirmyWPromieniu = criteria.HasFirmyWPromieniu, FirmyWPromieniu = criteria.FirmyWPromieniu
            });
        var pkdIds = request.PkdCodeIds.Distinct().ToArray();
        if (pkdIds.Length > 10 || await _db.PkdCodes.CountAsync(x => pkdIds.Contains(x.Id)) != pkdIds.Length)
            return BadRequest(new { message = "Wybierz maksymalnie 10 istniejacych kodow PKD." });
        foreach (var removed in profile.PkdCodes.Where(x => !pkdIds.Contains(x.PkdCodeId)).ToArray())
        {
            profile.PkdCodes.Remove(removed);
            _db.SearchProfilePkds.Remove(removed);
        }
        foreach (var pkdId in pkdIds.Where(pkdId => !profile.PkdCodes.Any(x => x.PkdCodeId == pkdId)))
        {
            profile.PkdCodes.Add(new SearchProfilePkd { PkdCodeId = pkdId });
        }

        profile.SearchProfileHash = SearchProfileHashCalculator.Calculate(profile);
        profile.LastSavedAtUtc = DateTime.UtcNow;
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
            CompanyName = criteria.CompanyName, Address = criteria.Address, HasPhone = criteria.HasPhone, HasEmail = criteria.HasEmail,
            HasWebsite = criteria.HasWebsite, DateRangeEnabled = criteria.DateRangeEnabled,
            DateFrom = criteria.DateFrom, DateTo = criteria.DateTo, RecordLimit = criteria.RecordLimit,
            WojewodztwoId = criteria.WojewodztwoId, PowiatId = criteria.PowiatId,
            GminaId = criteria.GminaId, MiejscowoscId = criteria.MiejscowoscId,
            HasFirmyWPromieniu = criteria.HasFirmyWPromieniu, FirmyWPromieniu = criteria.FirmyWPromieniu
        };
}
