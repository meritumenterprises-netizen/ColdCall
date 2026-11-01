using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpGet("global-settings")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetGlobalSettings(CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        return Ok(await _db.GlobalSettings.AsNoTracking().OrderByDescending(row => row.GlobalSettingsId)
            .Select(row => new AdminGlobalSettingsDto
            {
                GlobalSettingsId = row.GlobalSettingsId,
                GlobalDegreeOfParallelismImportByDate = row.GlobalDegreeOfParallelismImportByDate,
                ApplicationInService = row.ApplicationInService
            }).ToListAsync(token));
    }

    [HttpPut("global-settings/{id:int}")]
    public async Task<IActionResult> SaveGlobalSettings(int id, AdminGlobalSettingsDto request, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (id != request.GlobalSettingsId) return BadRequest();
        var row = await _db.GlobalSettings.SingleOrDefaultAsync(item => item.GlobalSettingsId == id, token);
        if (row == null) return NotFound();
        row.GlobalDegreeOfParallelismImportByDate = request.GlobalDegreeOfParallelismImportByDate;
        row.ApplicationInService = request.ApplicationInService;
        await _db.SaveChangesAsync(token);
        return NoContent();
    }
}
