using ColdCall.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/statistics")]
public class StatisticsController : ControllerBase
{
    private readonly ColdCallDbContext _db;

    public StatisticsController(ColdCallDbContext db) => _db = db;

    [AllowAnonymous]
    [HttpGet("companies")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Companies(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.Now;
            var since = now.AddDays(-7);
            var count = await _db.Companies.LongCountAsync(cancellationToken);
            var geoAddressCount = await _db.Adresy.LongCountAsync(cancellationToken);
            var recentlyImportedCount = await _db.Companies.LongCountAsync(
                company => company.ImportedOn >= since && company.ImportedOn <= now, cancellationToken);
            return Ok(new { count, geoAddressCount, recentlyImportedCount });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }
}
