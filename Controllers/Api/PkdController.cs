using ColdCall.Data;
using ColdCall.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/pkd")]
[Authorize]
public class PkdController : ControllerBase
{
    private readonly ColdCallDbContext _db;

    public PkdController(ColdCallDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IReadOnlyList<PkdDto>> Search([FromQuery] string? term = null)
    {
        term = term?.Trim();

        var query = _db.PkdCodes.AsQueryable();
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(x => x.Code.Contains(term) || x.Name.Contains(term));
        }

        return await query
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .Select(x => new PkdDto(x.Id, x.Code, x.Name))
            .ToListAsync();
    }
}
