using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly ColdCallDbContext _db;
    private readonly AdminAccessService _adminAccess;
    private readonly IEmailSender _emailSender;

    public AdminController(ColdCallDbContext db, AdminAccessService adminAccess, IEmailSender emailSender)
    {
        _db = db;
        _adminAccess = adminAccess;
        _emailSender = emailSender;
    }

    [HttpGet("companies")]
    public async Task<ActionResult<PagedResult<CompanyDto>>> Companies(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = "name",
        [FromQuery] string? direction = "asc",
        [FromQuery] string? filter = null,
        [FromQuery] int[]? pkdIds = null)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null)
        {
            return Forbid();
        }

        pageSize = 10;
        var query = _db.Companies
            .Include(x => x.BusinessType)
            .Include(x => x.Wojewodztwo)
            .Include(x => x.Miejscowosc)
            .Include(x => x.Gmina)
            .Include(x => x.Powiat)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(x =>
                x.Name.Contains(filter) ||
                (x.Nip != null && x.Nip.Contains(filter)) ||
                (x.Krs != null && x.Krs.Contains(filter)) ||
                (x.Email != null && x.Email.Contains(filter)) ||
                (x.Phone != null && x.Phone.Contains(filter)));
        }

        var selectedPkdIds = pkdIds?.Where(x => x > 0).Distinct().ToArray() ?? Array.Empty<int>();
        if (selectedPkdIds.Length > 0)
        {
            query = query.Where(company => company.CompanyPkds.Any(companyPkd => selectedPkdIds.Contains(companyPkd.PkdCodeId)));
        }

        var total = await query.CountAsync();
        var items = await query.ApplyCompanySort(sortBy, direction)
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new CompanyDto(
                x.Id, x.Name, x.BusinessTypeId, x.BusinessType != null ? x.BusinessType.TypDzialalnosci : null, x.FirstName, x.LastName, x.Krs, x.Nip,
                x.Wojewodztwo!.Name, x.Miejscowosc!.Name, x.Gmina!.Name, x.Powiat!.Name, x.PostalCode,
                x.Street, x.BuildingNumber, x.ApartmentNumber, x.Phone, x.Email, x.GoogleEmail,
                x.Website, x.InstagramAccount, x.FacebookAccount, x.StartedAt, x.CreatedOn,
                x.Regon, x.StatusDzialalnosciId, x.StatusDzialalnosci!.Nazwa, x.AddressIsMissing, x.BlockedByRODO,
                x.WojewodztwoId, x.MiejscowoscId, x.GminaId, x.PowiatId))
            .ToListAsync();

        return Ok(new PagedResult<CompanyDto>(items, total, page, pageSize, QueryableExtensions.CompanySortableColumns));
    }

    [HttpPut("companies/{id:int}")]
    public async Task<IActionResult> UpdateCompany(int id, CompanyUpdateDto request)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null)
        {
            return Forbid();
        }

        var company = await _db.Companies.FindAsync(id);
        if (company == null)
        {
            return NotFound();
        }

        company.Name = request.Name;
        if (!await _db.BusinessTypes.AnyAsync(x => x.TypDzialalnosciId == request.BusinessTypeId))
        {
            return BadRequest(new { message = "Nieprawidlowy typ dzialalnosci." });
        }

        company.BusinessTypeId = request.BusinessTypeId;
        company.FirstName = request.FirstName;
        company.LastName = request.LastName;
        company.Krs = request.Krs;
        company.Nip = request.Nip;
        company.WojewodztwoId = request.WojewodztwoId;
        company.MiejscowoscId = request.MiejscowoscId;
        company.GminaId = request.GminaId;
        company.PowiatId = request.PowiatId;
        company.PostalCode = request.PostalCode;
        company.Street = request.Street;
        company.BuildingNumber = request.BuildingNumber;
        company.ApartmentNumber = request.ApartmentNumber;
        company.Phone = request.Phone;
        company.Email = request.Email;
        company.GoogleEmail = request.GoogleEmail;
        company.Website = request.Website;
        company.InstagramAccount = request.InstagramAccount;
        company.FacebookAccount = request.FacebookAccount;
        company.StartedAt = request.StartedAt;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> Users()
    {
        if (await _adminAccess.RequireAdminAsync(User) == null)
        {
            return Forbid();
        }

        return await _db.Users
            .Include(x => x.Plans)
            .ThenInclude(x => x.PlanDefinition)
            .OrderBy(x => x.GoogleEmail)
            .Select(x => new UserDto(
                x.Id,
                x.GoogleEmail,
                x.DisplayName,
                x.Plans.OrderByDescending(p => p.ExpiresAtUtc)
                    .Select(p => new UserPlanDto(p.PlanDefinition.Code, p.PurchasedAtUtc, p.ExpiresAtUtc, p.Source))
                    .ToList()))
            .ToListAsync();
    }

    [HttpGet("business-types")]
    public async Task<ActionResult<IReadOnlyList<BusinessTypeDto>>> BusinessTypes()
    {
        if (await _adminAccess.RequireAdminAsync(User) == null)
        {
            return Forbid();
        }

        return await _db.BusinessTypes
            .OrderBy(x => x.TypDzialalnosci)
            .Select(x => new BusinessTypeDto(x.TypDzialalnosciId, x.TypDzialalnosci))
            .ToListAsync();
    }

    [HttpPost("users/grant-plan")]
    public async Task<IActionResult> GrantPlan(GrantPlanDto request)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null)
        {
            return Forbid();
        }

        var user = await _db.Users.FindAsync(request.UserId);
        var plan = await _db.PlanDefinitions.FirstOrDefaultAsync(x => x.Code == request.PlanCode);
        if (user == null || plan == null)
        {
            return NotFound();
        }

        var userPlan = new UserPlan
        {
            AppUserId = user.Id,
            PlanDefinitionId = plan.Id,
            ExpiresAtUtc = DateTime.UtcNow.AddMonths(1),
            PricePaid = 0,
            Source = "Admin"
        };
        _db.UserPlans.Add(userPlan);
        await _db.SaveChangesAsync();
        userPlan.PlanDefinition = plan;
        await _emailSender.SendPlanGrantedAsync(user, userPlan);
        return Ok();
    }

    [HttpPost("admins")]
    public async Task<IActionResult> AddAdmin([FromBody] string email)
    {
        var owner = await _adminAccess.RequireOwnerAsync(User);
        if (owner == null)
        {
            return Forbid();
        }

        email = email.Trim().ToLowerInvariant();
        if (!await _db.AdminEmails.AnyAsync(x => x.Email == email))
        {
            _db.AdminEmails.Add(new AdminEmail { Email = email, AddedByEmail = owner.GoogleEmail });
            await _db.SaveChangesAsync();
        }

        return Ok();
    }
}
