using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpGet("regulamin-beta")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetRegulaminBeta(CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        var result = await _db.GlobalSettings.AsNoTracking().OrderByDescending(row => row.GlobalSettingsId)
            .Select(row => new AdminRegulaminBetaDto { GlobalSettingsId = row.GlobalSettingsId, RegulaminBeta = row.RegulaminBeta })
            .FirstOrDefaultAsync(token);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPut("regulamin-beta/{id:int}")]
    public async Task<IActionResult> SaveRegulaminBeta(int id, AdminRegulaminBetaDto request, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (id != request.GlobalSettingsId) return BadRequest();
        var updated = await _db.GlobalSettings.Where(row => row.GlobalSettingsId == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RegulaminBeta, request.RegulaminBeta), token);
        return updated == 1 ? NoContent() : NotFound();
    }
}
