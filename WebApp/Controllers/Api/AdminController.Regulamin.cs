using ColdCall.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpGet("regulamin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetRegulamin(CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        var result = await _db.GlobalSettings.AsNoTracking().OrderByDescending(row => row.GlobalSettingsId)
            .Select(row => new AdminRegulaminDto { GlobalSettingsId = row.GlobalSettingsId, Regulamin = row.Regulamin })
            .FirstOrDefaultAsync(token);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPut("regulamin/{id:int}")]
    public async Task<IActionResult> SaveRegulamin(int id, AdminRegulaminDto request, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (id != request.GlobalSettingsId) return BadRequest();
        var updated = await _db.GlobalSettings.Where(row => row.GlobalSettingsId == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Regulamin, request.Regulamin), token);
        return updated == 1 ? NoContent() : NotFound();
    }
}
