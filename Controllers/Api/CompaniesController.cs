using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/companies")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly ColdCallDbContext _db;
    private readonly ICurrentUserService _currentUserService;

    public CompaniesController(ColdCallDbContext db, ICurrentUserService currentUserService)
    {
        _db = db;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CompanyDto>>> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortBy = "name",
        [FromQuery] string? direction = "asc",
        [FromQuery] int[]? pkdIds = null)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var hasActivePlan = await _db.UserPlans.AnyAsync(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow);
        var isAdmin = await _db.AdminEmails.AnyAsync(x => x.Email == user.GoogleEmail);
        if (!isAdmin && !hasActivePlan)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Plan wygasl albo nie zostal jeszcze zakupiony." });
        }

        var query = BaseCompanyQuery();
        if (pkdIds is { Length: > 0 })
        {
            query = query.Where(x => x.CompanyPkds.Any(p => pkdIds.Contains(p.PkdCodeId)));
        }

        var total = await query.CountAsync();
        var items = await query
            .ApplyCompanySort(sortBy, direction)
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync();

        return Ok(new PagedResult<CompanyDto>(items, total, page, pageSize));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int[]? pkdIds = null)
    {
        var query = BaseCompanyQuery();
        if (pkdIds is { Length: > 0 })
        {
            query = query.Where(x => x.CompanyPkds.Any(p => pkdIds.Contains(p.PkdCodeId)));
        }

        var rows = await query.OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync();
        var csv = "Nazwa;Imie;Nazwisko;KRS;NIP;Miejscowosc;Kod;Ulica;Nr budynku;Nr lokalu;Telefon;Email;WWW;Start\n"
            + string.Join("\n", rows.Select(x => string.Join(';', new[]
            {
                Csv(x.Name), Csv(x.FirstName), Csv(x.LastName), Csv(x.Krs), Csv(x.Nip), Csv(x.City), Csv(x.PostalCode),
                Csv(x.Street), Csv(x.BuildingNumber), Csv(x.ApartmentNumber), Csv(x.Phone), Csv(x.Email), Csv(x.Website),
                Csv(x.StartedAt?.ToString("yyyy-MM-dd"))
            })));
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "firmy.csv");
    }

    private IQueryable<Company> BaseCompanyQuery() => _db.Companies
        .Include(x => x.Wojewodztwo)
        .Include(x => x.Miejscowosc)
        .Include(x => x.Gmina)
        .Include(x => x.Powiat)
        .Include(x => x.CompanyPkds);

    private static CompanyDto ToDto(Company x) => new(
        x.Id,
        x.Name,
        x.BusinessType.ToString(),
        x.FirstName,
        x.LastName,
        x.Krs,
        x.Nip,
        x.Wojewodztwo?.Name,
        x.Miejscowosc?.Name,
        x.Gmina?.Name,
        x.Powiat?.Name,
        x.PostalCode,
        x.Street,
        x.BuildingNumber,
        x.ApartmentNumber,
        x.Phone,
        x.Email,
        x.GoogleEmail,
        x.Website,
        x.InstagramAccount,
        x.FacebookAccount,
        x.StartedAt);

    private static string Csv(string? value)
    {
        value ??= "";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
