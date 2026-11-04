using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/admin")]
[Authorize]
public partial class AdminController : ControllerBase
{
    private readonly ColdCallDbContext _db;
    private readonly AdminAccessService _adminAccess;
    private readonly IEmailSender _emailSender;
    private readonly IMemoryCache _companyCounts;

    public AdminController(ColdCallDbContext db, AdminAccessService adminAccess, IEmailSender emailSender, IMemoryCache companyCounts)
    {
        _db = db;
        _adminAccess = adminAccess;
        _emailSender = emailSender;
        _companyCounts = companyCounts;
    }

    [HttpGet("companies")]
    public async Task<ActionResult<PagedResult<CompanyDto>>> Companies(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = "name",
        [FromQuery] string? direction = "asc",
        [FromQuery] string? filter = null,
        [FromQuery] int[]? pkdIds = null,
        [FromQuery] string? companyName = null,
        [FromQuery] bool? hasPhone = null, [FromQuery] bool? hasEmail = null, [FromQuery] bool? hasWebsite = null,
        [FromQuery] DateOnly? startedFrom = null, [FromQuery] DateOnly? startedTo = null,
        [FromQuery] int? wojewodztwoId = null, [FromQuery] int? powiatId = null,
        [FromQuery] int? gminaId = null, [FromQuery] int? miejscowoscId = null, [FromQuery] int? radiusKm = null,
        [FromQuery] string? nip = null, [FromQuery] bool? blockedByRODO = null,
        [FromQuery] string? companyStatusIds = null,
        [FromQuery] bool refreshCount = true)
    {
        var admin = await _adminAccess.RequireAdminAsync(User);
        if (admin == null)
        {
            return Forbid();
        }

        pageSize = 10;
        var query = _db.Companies
            .AsNoTracking()
            .Include(x => x.BusinessType)
            .Include(x => x.Wojewodztwo)
            .Include(x => x.Miejscowosc)
            .Include(x => x.Gmina)
            .Include(x => x.Powiat)
            .AsQueryable();

        if (blockedByRODO.HasValue)
            query = query.Where(c => c.BlockedByRODO == blockedByRODO.Value);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(x =>
                x.Name.Contains(filter) ||
                (x.Nip != null && x.Nip.Contains(filter)) ||
                (x.Krs != null && x.Krs.Contains(filter)) ||
                (x.Email != null && x.Email.Contains(filter)) ||
                (x.Phone != null && x.Phone.Contains(filter)));
        }

        if (!string.IsNullOrWhiteSpace(nip))
        {
            var normalizedNip = nip.Trim().Replace("-", "").Replace(" ", "");
            if (normalizedNip.Length > 10 || normalizedNip.Any(c => c < '0' || c > '9'))
                return BadRequest(new { message = "NIP moze zawierac maksymalnie 10 cyfr." });
            query = normalizedNip.Length == 10
                ? query.Where(c => c.Nip == normalizedNip)
                : query.Where(c => c.Nip != null && c.Nip.Contains(normalizedNip));
        }

        if ((companyName?.Trim().Length ?? 0) >= 3 || hasPhone.HasValue || hasEmail.HasValue || hasWebsite.HasValue
            || startedFrom.HasValue || startedTo.HasValue || wojewodztwoId.HasValue || powiatId.HasValue
            || gminaId.HasValue || miejscowoscId.HasValue || radiusKm.HasValue || pkdIds?.Length > 0
            || companyStatusIds != null)
        {
        try
        {
            var matches = await new CompanySearchQuery(_db).BuildAsync(new SearchProfileCriteria
            {
                CompanyName = companyName ?? "", HasPhone = hasPhone, HasEmail = hasEmail, HasWebsite = hasWebsite,
                CompanyStatusIds = companyStatusIds == null ? null
                    : CompanyStatusSelection.Serialize(CompanyStatusSelection.Parse(companyStatusIds)),
                DateRangeEnabled = startedFrom.HasValue || startedTo.HasValue, DateFrom = startedFrom, DateTo = startedTo,
                WojewodztwoId = wojewodztwoId, PowiatId = powiatId, GminaId = gminaId, MiejscowoscId = miejscowoscId,
                HasFirmyWPromieniu = radiusKm.HasValue, FirmyWPromieniu = radiusKm
            }, pkdIds, null, includeAllCompanies: true);
            query = query.Where(company => matches.Select(row => row.Id).Contains(company.Id));
        }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        }

        var countKey = "AdminCompanyCount:" + admin.Id + ":" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(query.ToQueryString())));
        if (refreshCount || !_companyCounts.TryGetValue(countKey, out int total))
        {
            total = await query.CountAsync(HttpContext.RequestAborted);
            _companyCounts.Set(countKey, total, new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(2)).SetAbsoluteExpiration(TimeSpan.FromMinutes(10)));
        }
        var offset = (Math.Max(page, 1) - 1) * pageSize;
        int[] addressPageIds;
        IQueryable<Company> pageQuery;
        if (string.Equals(sortBy, "fullAddress", StringComparison.OrdinalIgnoreCase))
        {
            addressPageIds = await AdminCompanyAddressPage.LoadIdsAsync(query,
                string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase), offset, pageSize,
                HttpContext.RequestAborted);
            pageQuery = query.Where(company => addressPageIds.Contains(company.Id));
        }
        else
        {
            // Page over the narrow index keys before loading all display columns and dictionaries.
            addressPageIds = await query.ApplyCompanySort(sortBy, direction).ThenBy(x => x.Id)
                .Skip(offset).Take(pageSize).Select(x => x.Id).ToArrayAsync(HttpContext.RequestAborted);
            pageQuery = query.Where(company => addressPageIds.Contains(company.Id));
        }
        var items = await pageQuery
            .Select(x => new CompanyDto(
                x.Id, x.Name, x.BusinessTypeId, x.BusinessType != null ? x.BusinessType.TypDzialalnosci : null, x.FirstName, x.LastName, x.Krs, x.Nip,
                x.Wojewodztwo!.Name, x.Miejscowosc!.Name, x.Gmina!.Name, x.Powiat!.Name, x.PostalCode,
                x.Street, x.BuildingNumber, x.ApartmentNumber, x.Phone, x.Email, x.GoogleEmail,
                x.Website, x.InstagramAccount, x.FacebookAccount,
                x.StartedAt,
                x.CreatedOn,
                x.Regon, x.StatusDzialalnosciId, x.StatusDzialalnosci!.Nazwa, x.AddressIsMissing, x.BlockedByRODO,
                x.WojewodztwoId, x.MiejscowoscId, x.GminaId, x.PowiatId, x.DataModifiedOn,
                null, null, null, EF.Property<string>(x, "AddressSort")))
            .ToListAsync(HttpContext.RequestAborted);

        if (addressPageIds != null)
        {
            var positions = addressPageIds.Select((id, index) => new { id, index }).ToDictionary(item => item.id, item => item.index);
            items = items.OrderBy(item => positions[item.Id]).ToList();
        }
        return Ok(new PagedResult<CompanyDto>(items, total, page, pageSize, QueryableExtensions.CompanySortableColumns));
    }

    public record RodoRequest(bool? BlockedByRODO);

    [HttpPut("companies/{id:int}/rodo")]
    public async Task<IActionResult> SetRodo(int id, RodoRequest request, CancellationToken cancellationToken)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (!request.BlockedByRODO.HasValue) return BadRequest();
        var affected = await _db.Companies.Where(c => c.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.BlockedByRODO, request.BlockedByRODO.Value), cancellationToken);
        return affected == 0 ? NotFound() : Ok(new { blockedByRODO = request.BlockedByRODO.Value });
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
        var cityName = await _db.Miejscowosci.Where(city => city.Id == company.MiejscowoscId)
            .Select(city => city.Name).FirstOrDefaultAsync() ?? "";
        _db.Entry(company).Property<string>("AddressSort").CurrentValue = cityName + " " + (company.PostalCode ?? "")
            + " " + (company.Street ?? "") + " " + (company.BuildingNumber ?? "")
            + (string.IsNullOrWhiteSpace(company.ApartmentNumber) ? "" : "/" + company.ApartmentNumber.Trim());
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

        var users = await _db.Users
            .Include(x => x.Plans)
            .ThenInclude(x => x.PlanDefinition)
            .OrderBy(x => x.GoogleEmail)
            .Select(x => new UserDto(
                x.Id,
                x.GoogleEmail,
                x.DisplayName,
                x.Plans.OrderByDescending(p => p.ExpiresAtUtc).ThenByDescending(p => p.Id)
                    .Select(p => new UserPlanDto(p.PlanDefinition.Code, p.PurchasedAtUtc, p.ExpiresAtUtc, p.Source, p.PlanDefinition.DescriptionHtml,
                        _db.StripePayments.Where(payment => payment.StripeSessionId == p.StripeSessionId
                            && payment.AppUserId == x.Id && payment.PlanDefinitionId == p.PlanDefinitionId)
                            .Select(payment => (DateTime?)payment.PaidAtUtc).FirstOrDefault(),
                        _db.StripePayments.Where(payment => payment.StripeSessionId == p.StripeSessionId
                            && payment.AppUserId == x.Id && payment.PlanDefinitionId == p.PlanDefinitionId)
                            .Select(payment => (decimal?)payment.AmountPaid).FirstOrDefault()))
                    .ToList(), DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc), x.DisablePlanPurchase, x.IsEnabled,
                x.FirstName, x.LastName, x.CompanyName, x.PostalCode, x.City, x.Street, x.StreetNumber, x.IsAdmin, x.Nip, x.Phone, x.IsBetaUser))
            .ToListAsync();
        return users.Select(user => user with
        {
            Plans = user.Plans.Select(plan => plan with
            {
                PaidAtUtc = plan.PaidAtUtc.HasValue ? DateTime.SpecifyKind(plan.PaidAtUtc.Value, DateTimeKind.Utc) : null
            }).ToArray()
        }).ToArray();
    }

    [HttpPut("users/{id:int}/plan-purchase")]
    public async Task<IActionResult> UpdatePlanPurchase(int id, UpdatePlanPurchaseRequest request, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (!request.DisablePlanPurchase.HasValue)
            return BadRequest(new { message = "Podaj ustawienie blokady zakupu planu." });
        var disabled = request.DisablePlanPurchase.Value;
        var updated = await _db.Users.Where(u => u.Id == id && !u.IsAdmin)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.DisablePlanPurchase, disabled), token);
        return updated == 1 ? Ok(new { DisablePlanPurchase = disabled }) : NotFound();
    }

    [HttpGet("plan-definitions")]
    public async Task<IActionResult> PlanDefinitions([FromQuery] bool includeUpgrades = false, CancellationToken token = default)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        return Ok(await _db.PlanDefinitions.AsNoTracking()
            .Where(p => includeUpgrades || !p.IsUpgrade)
            .OrderBy(p => p.Order).ThenBy(p => p.Code).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.Code, p.DescriptionHtml }).ToListAsync(token));
    }

    [HttpPut("users/{id:int}/plan")]
    public async Task<IActionResult> ChangePlan(int id, ChangeUserPlanRequest request, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (!await _db.Users.AnyAsync(u => u.Id == id && !u.IsAdmin, token)) return NotFound();
        var definition = await _db.PlanDefinitions.AsNoTracking().Where(p => p.Id == request.PlanDefinitionId)
            .Select(p => new { p.ExpiresAfterDays }).SingleOrDefaultAsync(token);
        if (definition == null)
            return BadRequest(new { message = "Wybrany plan nie istnieje." });
        var currentPlan = await _db.UserPlans.AsNoTracking().Where(p => p.AppUserId == id)
            .OrderByDescending(p => p.ExpiresAtUtc).ThenByDescending(p => p.Id)
            .Select(p => new { p.Id, p.ExpiresAtUtc }).FirstOrDefaultAsync(token);
        if (currentPlan == null)
            return BadRequest(new { message = "Uzytkownik nie ma planu do zmiany." });
        var now = DateTime.UtcNow;
        var expired = currentPlan.ExpiresAtUtc <= now;
        var expiration = currentPlan.ExpiresAtUtc;
        if (expired)
        {
            if (definition.ExpiresAfterDays is not > 0)
                return BadRequest(new { message = "Wybrany plan musi miec dodatnia liczbe dni waznosci." });
            try { expiration = now.AddDays(definition.ExpiresAfterDays.Value); }
            catch (ArgumentOutOfRangeException)
            {
                return BadRequest(new { message = "Niepoprawna liczba dni waznosci planu." });
            }
        }
        var updated = await _db.UserPlans.Where(p => p.Id == currentPlan.Id && p.AppUserId == id
                && p.ExpiresAtUtc == currentPlan.ExpiresAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.PlanDefinitionId, request.PlanDefinitionId)
                .SetProperty(p => p.ExpiresAtUtc, expiration)
                .SetProperty(p => p.IsActive, p => expired ? (bool?)true : p.IsActive), token);
        return updated == 1 ? NoContent() : Conflict(new { message = "Plan zostal zmieniony. Odswiez dane i sprobuj ponownie." });
    }

    [HttpPost("users/{id:int}/extend-plan")]
    public async Task<IActionResult> ExtendPlan(int id, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (!await _db.Users.AnyAsync(u => u.Id == id && !u.IsAdmin, token)) return NotFound();
        var planId = await _db.UserPlans.Where(p => p.AppUserId == id)
            .OrderByDescending(p => p.ExpiresAtUtc).ThenByDescending(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync(token);
        if (!planId.HasValue) return BadRequest(new { message = "Uzytkownik nie ma planu do przedluzenia." });
        var now = DateTime.UtcNow;
        await _db.UserPlans.Where(p => p.Id == planId.Value)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.ExpiresAtUtc, p => p.ExpiresAtUtc <= now ? now.AddDays(7) : p.ExpiresAtUtc.AddDays(7))
                .SetProperty(p => p.IsActive, true), token);
        return Ok();
    }

    [HttpDelete("users/{id:int}/csv-exports")]
    public async Task<IActionResult> DeleteCsvExports(int id, [FromServices] CsvExportCleanupService cleanup, CancellationToken token)
    {
        if (await _adminAccess.RequireAdminAsync(User) == null) return Forbid();
        if (!await _db.Users.AnyAsync(u => u.Id == id && !u.IsAdmin, token)) return NotFound();
        try { return Ok(new { deleted = await cleanup.DeleteForUserAsync(id, token) }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<AdminController>>().LogError(e, "CSV cleanup failed for user {UserId}", id);
            return StatusCode(500, new { message = "Nie udalo sie usunac wszystkich eksportow CSV." });
        }
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
        var user = await _db.Users.SingleOrDefaultAsync(x => x.GoogleEmail == email);
        if (user == null) return NotFound(new { message = "Uzytkownik musi najpierw zarejestrowac konto." });
        user.IsAdmin = true;
        await _db.SaveChangesAsync();

        return Ok();
    }
}
