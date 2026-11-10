using ColdCall.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController, AllowAnonymous, Route("api/public/app-version")]
public class PublicAppVersionController(ColdCallDbContext db) : ControllerBase
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        var value = await db.GlobalSettings.AsNoTracking().OrderByDescending(row => row.GlobalSettingsId)
            .Select(row => row.AppVersion).FirstOrDefaultAsync(token);
        return Ok(new { appVersion = value?.Trim() ?? "" });
    }
}
