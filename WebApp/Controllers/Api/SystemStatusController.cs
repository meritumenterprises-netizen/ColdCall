using ColdCall.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/system/status")]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SystemStatusController(ColdCallDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        var applicationInService = await db.GlobalSettings.AsNoTracking()
            .OrderByDescending(settings => settings.GlobalSettingsId)
            .Select(settings => settings.ApplicationInService).FirstOrDefaultAsync(token);
        return Ok(new { applicationInService });
    }
}
