using ColdCall.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/company-statuses")]
public class CompanyStatusesController(ColdCallDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await db.Set<ColdCall.Models.CompanyStatus>().AsNoTracking()
            .OrderBy(s => s.StatusId)
            .Select(s => new { Id = (int)s.StatusId, Name = s.Nazwa })
            .ToArrayAsync(cancellationToken));
}
