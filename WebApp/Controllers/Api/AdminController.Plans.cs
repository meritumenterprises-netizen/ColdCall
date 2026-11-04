using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpGet("plans")]
    public async Task<IActionResult> ListPlans([FromQuery] bool upgrade = false, CancellationToken token = default)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        return Ok(await _db.PlanDefinitions.AsNoTracking().Where(p => p.IsUpgrade == upgrade)
            .OrderBy(p => p.Order).ThenBy(p => p.Code).ThenBy(p => p.Id)
            .Select(p => new AdminPlanDefinitionDto
            {
                Id = p.Id, Code = p.Code, MaxNumberOfSearches = p.MaxNumberOfSearches,
                MaxNumberOfCSVExportsPerSearch = p.MaxNumberOfCSVExportsPerSearch,
                MaxNumberOfExportedRecords = p.MaxNumberOfExportedRecords, ExpiresAfterDays = p.ExpiresAfterDays,
                ShowInOffer = p.ShowInOffer, EnableCSVDeleting = p.EnableCSVDeleting,
                Price = p.Price, DescriptionHtml = p.DescriptionHtml, IsUpgrade = p.IsUpgrade, IsTrial = p.IsTrial
            }).ToListAsync(token));
    }

    [HttpPut("plans/{id:int}/offer")]
    public async Task<IActionResult> UpdatePlanOffer(int id, UpdatePlanOfferRequest request, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (!request.ShowInOffer.HasValue) return BadRequest();
        var plan = await _db.PlanDefinitions.SingleOrDefaultAsync(p => p.Id == id, token);
        if (plan == null) return NotFound();
        plan.ShowInOffer = request.ShowInOffer.Value;
        await _db.SaveChangesAsync(token);
        return NoContent();
    }

    [HttpPut("plans/{id:int}")]
    public async Task<IActionResult> SavePlanDefinition(int id, AdminPlanDefinitionDto request, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Code) || request.Price != decimal.Round(request.Price, 2))
            return BadRequest(new { message = "Podaj nazwe planu i cene z maksymalnie dwoma miejscami po przecinku." });
        var plan = await _db.PlanDefinitions.SingleOrDefaultAsync(p => p.Id == id, token);
        if (plan == null) return NotFound();
        plan.Code = request.Code.Trim();
        plan.MaxNumberOfSearches = request.MaxNumberOfSearches;
        plan.MaxNumberOfCSVExportsPerSearch = request.MaxNumberOfCSVExportsPerSearch;
        plan.MaxNumberOfExportedRecords = request.MaxNumberOfExportedRecords;
        plan.ExpiresAfterDays = request.ExpiresAfterDays;
        plan.ShowInOffer = request.ShowInOffer;
        plan.EnableCSVDeleting = request.EnableCSVDeleting;
        plan.Price = request.Price;
        plan.DescriptionHtml = request.DescriptionHtml ?? "";
        await _db.SaveChangesAsync(token);
        return NoContent();
    }
}
