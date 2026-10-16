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
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Companies(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(new { count = await _db.Companies.LongCountAsync(cancellationToken) });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }
}
