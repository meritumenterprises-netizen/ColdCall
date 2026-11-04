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
            var totals = await _db.Companies.GroupBy(company => 1)
                .Select(group => new
                {
                    Count = group.LongCount(),
                    WithPhone = group.LongCount(company => company.Phone != null && company.Phone.Trim() != ""),
                    WithEmail = group.LongCount(company => company.Email != null && company.Email.Trim() != ""),
                    RecentlyImported = group.LongCount(company => company.ImportedOn >= since && company.ImportedOn <= now)
                }).SingleOrDefaultAsync(cancellationToken);
            var count = totals?.Count ?? 0;
            var companiesWithPhoneCount = totals?.WithPhone ?? 0;
            var companiesWithEmailCount = totals?.WithEmail ?? 0;
            var recentlyImportedCount = totals?.RecentlyImported ?? 0;
            var geoAddressCount = await _db.Adresy.LongCountAsync(cancellationToken);
            var latestImport = await _db.ImportByDates.Where(import => import.ImportTargetRecords > 0)
                .MaxAsync(import => (DateTime?)import.ImportDate, cancellationToken);
            var latestImportDate = latestImport?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return Ok(new { count, geoAddressCount, companiesWithPhoneCount, companiesWithEmailCount, recentlyImportedCount, latestImportDate });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }
}
