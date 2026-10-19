using ColdCall.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/address")]
[Authorize]
public class AddressController(ColdCallDbContext db) : ControllerBase
{
    [HttpGet("distance-localities")]
    public async Task<IActionResult> DistanceLocalities(string? term, CancellationToken cancellationToken)
    {
        var prefix = term?.Trim() ?? "";
        if (prefix.Length < 3) return Ok(Array.Empty<object>());
        return Ok(await db.Miejscowosci.AsNoTracking()
            .Where(locality => locality.Name.StartsWith(prefix))
            .OrderBy(locality => locality.Name).ThenBy(locality => locality.Id)
            .Select(locality => new { locality.Id, locality.Name })
            .ToListAsync(cancellationToken));
    }

    [HttpGet("wojewodztwa")]
    public async Task<IActionResult> Wojewodztwa(CancellationToken cancellationToken) =>
        Ok(await db.Wojewodztwa.AsNoTracking()
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken));

    [HttpGet("powiaty")]
    public async Task<IActionResult> Powiaty(int wojewodztwoId, CancellationToken cancellationToken) =>
        Ok(await db.Powiaty.AsNoTracking()
            .Where(x => db.Adresy.Any(a => a.WojewodztwoId == wojewodztwoId && a.PowiatId == x.Id))
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken));

    [HttpGet("gminy")]
    public async Task<IActionResult> Gminy(int wojewodztwoId, int powiatId, CancellationToken cancellationToken) =>
        Ok(await db.Gminy.AsNoTracking()
            .Where(x => db.Adresy.Any(a => a.WojewodztwoId == wojewodztwoId && a.PowiatId == powiatId && a.GminaId == x.Id))
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken));

    [HttpGet("miejscowosci")]
    public async Task<IActionResult> Miejscowosci(int? wojewodztwoId, int? powiatId, int? gminaId, CancellationToken cancellationToken) =>
        Ok(await db.Miejscowosci.AsNoTracking()
            .Where(x => wojewodztwoId.HasValue && powiatId.HasValue && gminaId.HasValue
                && db.Adresy.Any(a => a.WojewodztwoId == wojewodztwoId
                    && a.PowiatId == powiatId && a.GminaId == gminaId && a.MiejscowoscId == x.Id))
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken));
}
