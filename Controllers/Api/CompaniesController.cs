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
    public async Task<ActionResult<PagedResult<CompanyShortDto>>> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = "dataAktywnosci",
        [FromQuery] string? direction = "desc",
        [FromQuery] int[]? pkdIds = null,
        [FromQuery] string? address = null,
        [FromQuery] bool? hasPhone = null,
        [FromQuery] bool? hasEmail = null,
        [FromQuery] bool? hasWebsite = null,
        [FromQuery] DateOnly? startedFrom = null,
        [FromQuery] DateOnly? startedTo = null,
        [FromQuery] int? recordLimit = 1000)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var hasActivePlan = await _db.UserPlans.AnyAsync(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow);
        var isAdmin = await _db.AdminEmails.AnyAsync(x => x.Email == user.GoogleEmail);
        if (!isAdmin && !hasActivePlan)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Plan wygasl albo nie zostal jeszcze zakupiony." });
        }

        pageSize = 10;
        recordLimit ??= 1000;
        if (recordLimit is <= 0) return BadRequest(new { message = "Record limit must be positive." });
        if (recordLimit > 10000) return BadRequest(new { message = "Nie mo\u017cna jednorazowo wy\u015bwietli\u0107 wi\u0119cej ni\u017c 10 000 rekord\u00f3w. Nale\u017cy zaw\u0119zi\u0107 kryteria poszukiwania." });
        if (startedFrom.HasValue != startedTo.HasValue || (startedFrom.HasValue && startedTo <= startedFrom))
            return BadRequest(new { message = "Both dates are required and the end date must be later than the start date." });
        var query = ApplyDetailFilters(ApplyPkdFilter(BaseCompanyQuery(), pkdIds),
            address, hasPhone, hasEmail, hasWebsite, startedFrom, startedTo);

        var total = recordLimit.HasValue
            ? await query.Select(x => x.Id).Take(recordLimit.Value).CountAsync()
            : await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var offset = (page - 1) * pageSize;
        var items = await query
            .ApplyCompanyShortSort(sortBy, direction)
            .Skip(offset)
            .Take(Math.Min(pageSize, total - offset))
            .ToListAsync();

        return Ok(new PagedResult<CompanyShortDto>(items, total, page, pageSize, QueryableExtensions.CompanyShortSortableColumns));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CompanyDetailsDto>> Details(int id, CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var isAdmin = await _db.AdminEmails.AnyAsync(x => x.Email == user.GoogleEmail, cancellationToken);
        var hasActivePlan = await _db.UserPlans.AnyAsync(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow, cancellationToken);
        if (!isAdmin && !hasActivePlan)
            return StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Plan wygasl albo nie zostal jeszcze zakupiony." });

        var company = await _db.Companies.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new CompanyDto(
                x.Id, x.Name, x.BusinessTypeId, x.BusinessType!.TypDzialalnosci,
                x.FirstName, x.LastName, x.Krs, x.Nip,
                x.Wojewodztwo!.Name, x.Miejscowosc!.Name, x.Gmina!.Name, x.Powiat!.Name,
                x.PostalCode, x.Street, x.BuildingNumber, x.ApartmentNumber,
                x.Phone, x.Email, x.GoogleEmail, x.Website, x.InstagramAccount, x.FacebookAccount, x.StartedAt, x.CreatedOn))
            .SingleOrDefaultAsync(cancellationToken);
        if (company == null) return NotFound();

        var pkds = await _db.CompanyPkds.AsNoTracking().Where(x => x.CompanyId == id)
            .OrderBy(x => x.PkdCode.Code)
            .Select(x => new PkdDto(x.PkdCode.Id, x.PkdCode.Code, x.PkdCode.Name))
            .ToListAsync(cancellationToken);
        return Ok(new CompanyDetailsDto(company, pkds));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int[]? pkdIds = null)
    {
        var query = ApplyPkdFilter(BaseCompanyQuery(), pkdIds);

        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync();
        var csv = "Id;Name;Nip;FullAddress;Phone;Email;Www;DataAktywnosci;TypyDzialalnosci\n"
            + string.Join("\n", rows.Select(x => string.Join(';', new[]
            {
                Csv(x.Id.ToString()), Csv(x.Name), Csv(x.Nip), Csv(x.FullAddress),
                Csv(x.Phone), Csv(x.Email), Csv(x.Www),
                Csv(x.DataAktywnosci?.ToString("yyyy-MM-dd")), Csv(x.TypyDzialalnosci)
            })));
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "firmy.csv");
    }

    private IQueryable<CompanyShortDto> BaseCompanyQuery() =>
        from row in _db.CompaniesShort
        select new CompanyShortDto
        {
            Id = row.Id,
            Name = row.Name,
            Nip = row.Nip,
            FullAddress = row.FullAddress,
            Phone = row.Phone,
            Email = row.Email,
            Www = row.Www,
            DataAktywnosci = row.DataAktywnosci,
            TypyDzialalnosci = row.TypDzialalnosci
        };

    private IQueryable<CompanyShortDto> ApplyPkdFilter(IQueryable<CompanyShortDto> query, int[]? pkdIds)
    {
        var selectedPkdIds = pkdIds?.Where(x => x > 0).Distinct().ToArray() ?? Array.Empty<int>();
        return selectedPkdIds.Length == 0
            ? query
            : query.Where(company => _db.CompanyPkds.Any(companyPkd =>
                companyPkd.CompanyId == company.Id && selectedPkdIds.Contains(companyPkd.PkdCodeId)));
    }

    private static IQueryable<CompanyShortDto> ApplyDetailFilters(
        IQueryable<CompanyShortDto> query, string? address, bool? hasPhone, bool? hasEmail,
        bool? hasWebsite, DateOnly? startedFrom, DateOnly? startedTo)
    {
        if (!string.IsNullOrWhiteSpace(address))
            query = query.Where(x => x.FullAddress.Contains(address.Trim()));
        if (hasPhone.HasValue)
            query = hasPhone.Value ? query.Where(x => x.Phone != null && x.Phone.Trim() != "")
                : query.Where(x => x.Phone == null || x.Phone.Trim() == "");
        if (hasEmail.HasValue)
            query = hasEmail.Value ? query.Where(x => x.Email != null && x.Email.Trim() != "")
                : query.Where(x => x.Email == null || x.Email.Trim() == "");
        if (hasWebsite.HasValue)
            query = hasWebsite.Value ? query.Where(x => x.Www != null && x.Www.Trim() != "")
                : query.Where(x => x.Www == null || x.Www.Trim() == "");
        if (startedFrom.HasValue && startedTo.HasValue)
        {
            var from = startedFrom.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(x => x.DataAktywnosci >= from);
            if (startedTo.Value == DateOnly.MaxValue)
                query = query.Where(x => x.DataAktywnosci <= DateTime.MaxValue);
            else
            {
                var until = startedTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
                query = query.Where(x => x.DataAktywnosci < until);
            }
        }
        return query;
    }

    private static string Csv(string? value)
    {
        value ??= "";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
