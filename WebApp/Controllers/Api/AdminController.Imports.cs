using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

public partial class AdminController
{
    [HttpDelete("imports/{id:int}")]
    public async Task<IActionResult> DeleteImport(int id, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        var deleted = await _db.ImportByDates.Where(row => row.ImportId == id).ExecuteDeleteAsync(token);
        return deleted == 0 ? NotFound() : NoContent();
    }

    [HttpGet("imports")]
    public async Task<IActionResult> ListImports(CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        return Ok(await _db.ImportByDates.AsNoTracking()
            .OrderByDescending(row => row.StartedAt).ThenByDescending(row => row.ImportId)
            .Select(row => new {
                row.ImportId, row.ImportDate, row.PrevDate, row.ImportOriginRecords,
                row.ImportTargetRecords, row.StartedAt, row.FinishedAt, row.Duration
            }).ToListAsync(token));
    }

    [HttpGet("imports/{id:int}/summary")]
    public async Task<IActionResult> ImportSummary(int id, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        var row = await _db.ImportByDates.AsNoTracking().Where(row => row.ImportId == id)
            .Select(row => new { row.Summary }).SingleOrDefaultAsync(token);
        return row == null ? NotFound() : Ok(row);
    }
}
