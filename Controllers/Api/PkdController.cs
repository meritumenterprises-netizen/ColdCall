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
    public async Task<IReadOnlyList<PkdDto>> Search([FromQuery] string term)
    {
        term = term.Trim();
        if (term.Length < 2)
        {
            return Array.Empty<PkdDto>();
        }

        return await _db.PkdCodes
            .Where(x => x.Code.Contains(term) || x.Name.Contains(term))
            .OrderBy(x => x.Code)
            .Take(20)
            .Select(x => new PkdDto(x.Id, x.Code, x.Name))
            .ToListAsync();
    }
}
