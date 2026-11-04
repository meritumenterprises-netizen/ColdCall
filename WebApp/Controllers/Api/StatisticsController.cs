using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/statistics")]
public class StatisticsController(LandingStatisticsService statistics) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("companies")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Companies(CancellationToken cancellationToken)
    {
        try { return Ok(await statistics.GetAsync(cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }
}
