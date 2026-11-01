using ColdCall.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/public/regulamin")]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PublicRegulaminController(ColdCallDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        var row = await db.GlobalSettings.AsNoTracking().OrderByDescending(settings => settings.GlobalSettingsId)
            .Select(settings => new { settings.Regulamin }).FirstOrDefaultAsync(token);
        return row == null ? NotFound() : Ok(row);
    }
}
