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
    private readonly CompanyCsvExportService _csvExporter;

    public CompaniesController(ColdCallDbContext db, ICurrentUserService currentUserService, CompanyCsvExportService csvExporter)
    {
        _db = db;
        _currentUserService = currentUserService;
        _csvExporter = csvExporter;
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
        [FromQuery] int? recordLimit = null,
        [FromQuery] string? companyName = null,
        [FromQuery] int? wojewodztwoId = null,
        [FromQuery] int? powiatId = null,
        [FromQuery] int? gminaId = null,
        [FromQuery] int? miejscowoscId = null,
        [FromQuery] int? radiusKm = null,
        [FromQuery] bool addressLocality = false,
        [FromQuery] int? modifiedWithinDays = null)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var hasActivePlan = await _db.UserPlans.AnyAsync(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow);
        var isAdmin = user.IsAdmin;
        if (!isAdmin && !hasActivePlan)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Plan wygasl albo nie zostal jeszcze zakupiony." });
        }

        pageSize = 10;
        var maximumRecordLimit = await PlanRecordLimits.ForUserAsync(_db, user.Id, isAdmin);
        if (maximumRecordLimit <= 0)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Plan nie pozwala na pobieranie rekordow." });
        // The plan, not a request parameter or a saved search, determines the result cap.
        recordLimit = maximumRecordLimit;
        IQueryable<CompanyShortDto> query;
        try
        {
            query = await new CompanySearchQuery(_db).BuildAsync(new SearchProfileCriteria
            {
                CompanyName = companyName ?? "", Address = address ?? "", HasPhone = hasPhone, HasEmail = hasEmail,
                HasWebsite = hasWebsite, DateRangeEnabled = startedFrom.HasValue || startedTo.HasValue,
                DateFrom = startedFrom, DateTo = startedTo, WojewodztwoId = wojewodztwoId, PowiatId = powiatId,
                GminaId = gminaId, MiejscowoscId = miejscowoscId,
                HasFirmyWPromieniu = radiusKm.HasValue, FirmyWPromieniu = radiusKm
            }, pkdIds, modifiedWithinDays, addressLocality);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }

        var total = await query.OrderBy(x => x.Id).Select(x => x.Id).Take(recordLimit.Value).CountAsync();
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
        var isAdmin = user.IsAdmin;
        var hasActivePlan = await _db.UserPlans.AnyAsync(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow, cancellationToken);
        if (!isAdmin && !hasActivePlan)
            return StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Plan wygasl albo nie zostal jeszcze zakupiony." });

        var company = await _db.Companies.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new CompanyDto(
                x.Id, x.Name, x.BusinessTypeId, x.BusinessType!.TypDzialalnosci,
                x.FirstName, x.LastName, x.Krs, x.Nip,
                x.Wojewodztwo!.Name, x.Miejscowosc!.Name, x.Gmina!.Name, x.Powiat!.Name,
                x.PostalCode, x.Street, x.BuildingNumber, x.ApartmentNumber,
                x.Phone, x.Email, x.GoogleEmail, x.Website, x.InstagramAccount, x.FacebookAccount, x.StartedAt, x.CreatedOn,
                x.Regon, x.StatusDzialalnosciId, x.StatusDzialalnosci!.Nazwa, x.AddressIsMissing, x.BlockedByRODO,
                x.WojewodztwoId, x.MiejscowoscId, x.GminaId, x.PowiatId))
            .SingleOrDefaultAsync(cancellationToken);
        if (company == null) return NotFound();

        var pkds = await _db.CompanyPkds.AsNoTracking().Where(x => x.CompanyId == id)
            .OrderBy(x => x.PkdCode.Code)
            .Select(x => new PkdDto(x.PkdCode.Id, x.PkdCode.Code, x.PkdCode.Name))
            .ToListAsync(cancellationToken);
        var addresses = await _db.CompanyAddresses.AsNoTracking().Where(x => x.CompanyId == id)
            .OrderBy(x => x.CompanyAdresId)
            .Select(x => new CompanyAddressDto(
                x.CompanyAdresId, x.CompanyId, x.AddressId,
                x.Address.WojewodztwoId, x.Address.PowiatId, x.Address.GminaId, x.Address.MiejscowoscId,
                x.Address.Wojewodztwo.Name, x.Address.Powiat.Name, x.Address.Gmina.Name, x.Address.Miejscowosc.Name,
                x.Address.Ulica, x.Address.NrUlicy, x.Address.Rodzaj,
                x.Address.Latitude, x.Address.Longitude, x.Address.PrngId))
            .ToListAsync(cancellationToken);
        return Ok(new CompanyDetailsDto(company, pkds, addresses));
    }

    [HttpPost("export")]
    public async Task<IActionResult> Export(ExportCompaniesRequest request, CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var isAdmin = user.IsAdmin;
        var hasPlan = await _db.UserPlans.AnyAsync(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow, cancellationToken);
        if (!isAdmin && !hasPlan) return StatusCode(402, new { message = "Plan wygasl." });
        var limit = await PlanRecordLimits.ForUserAsync(_db, user.Id, isAdmin);
        if (limit <= 0) return StatusCode(403, new { message = "Plan nie pozwala na eksport rekordow." });
        var profile = await _db.SearchProfiles.AsNoTracking().Include(x => x.Criteria).Include(x => x.PkdCodes)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == request.SearchProfileId && x.AppUserId == user.Id, cancellationToken);
        if (profile == null) return NotFound(new { message = "Nie znaleziono zapisanego wyszukiwania." });
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var lockName = "CSVExport:" + profile.Id;
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={lockName}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 50001, 'CSV export lock unavailable', 1;", cancellationToken);
            var quota = await CsvExportLimits.ForSearchAsync(_db, user.Id, profile.Id, cancellationToken);
            if (quota.Remaining == 0)
                return StatusCode(409, new { message = "Osiagnieto limit eksportow CSV dla tego wyszukiwania." });
            var query = await new CompanySearchQuery(_db).BuildAsync(profile.Criteria ?? new SearchProfileCriteria(),
                profile.PkdCodes.Select(x => x.PkdCodeId).ToArray(), request.ModifiedWithinDays);
            var ids = await query.ApplyCompanyShortSort(request.SortBy, request.Direction).Select(x => x.Id)
                .Take(limit).ToListAsync(cancellationToken);
            if (ids.Count == 0) return BadRequest(new { message = "Brak rekordow do eksportu." });
            var csv = await _csvExporter.ExportAsync(user, profile, ids, request.ModifiedWithinDays, cancellationToken, request.SearchName);
            await transaction.CommitAsync(cancellationToken);
            return File(csv.Content, "text/csv; charset=utf-8", csv.Name);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<CompaniesController>>().LogError(exception, "CSV export failed for search {SearchId}", profile.Id);
            return StatusCode(500, new { message = "Nie udalo sie zapisac pliku CSV." });
        }
    }

    [HttpGet("export-quota")]
    public async Task<ActionResult<CsvExportQuota>> ExportQuota([FromQuery] int searchProfileId, CancellationToken cancellationToken)
    {
        try
        {
        var user = await _currentUserService.GetOrCreateAsync(User);
        if (!await _db.SearchProfiles.AnyAsync(p => p.Id == searchProfileId && p.AppUserId == user.Id, cancellationToken))
            return NotFound();
        return Ok(await CsvExportLimits.ForSearchAsync(_db, user.Id, searchProfileId, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }

    [HttpGet("exports")]
    public async Task<ActionResult<IReadOnlyList<CsvExportDto>>> Exports([FromQuery] int searchProfileId,
        [FromQuery] string sortBy = "createdOn", [FromQuery] string direction = "desc", CancellationToken cancellationToken = default)
    {
        try
        {
        var user = await _currentUserService.GetOrCreateAsync(User);
        if (sortBy is not ("csvFileName" or "createdOn" or "numberOfRecords") || direction is not ("asc" or "desc"))
            return BadRequest(new { message = "Niepoprawne sortowanie eksportow." });
        if (!await _db.SearchProfiles.AnyAsync(p => p.Id == searchProfileId && p.AppUserId == user.Id, cancellationToken))
            return NotFound();
        var query = _db.CsvExports.AsNoTracking().Where(e => e.SearchProfileId == searchProfileId);
        var ordered = sortBy == "csvFileName"
            ? direction == "asc" ? query.OrderBy(e => e.CsvFileName) : query.OrderByDescending(e => e.CsvFileName)
            : sortBy == "numberOfRecords"
                ? direction == "asc" ? query.OrderBy(e => e.NumberOfRecords) : query.OrderByDescending(e => e.NumberOfRecords)
                : direction == "asc" ? query.OrderBy(e => e.CreatedOn) : query.OrderByDescending(e => e.CreatedOn);
        return Ok(await ordered.ThenBy(e => e.EksportCsvId)
            .Select(e => new CsvExportDto(e.EksportCsvId, user.Id, searchProfileId, e.CsvFileName, e.CreatedOn, e.NumberOfRecords))
            .ToListAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }

    [HttpGet("DownloadCSV")]
    public async Task<IActionResult> DownloadCSV([FromQuery] int userId, [FromQuery] int searchProfileId,
        [FromQuery] string fileName, CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        if (userId != user.Id) return Forbid();
        var ownsExport = await _db.CsvExports.AsNoTracking().AnyAsync(e => e.SearchProfileId == searchProfileId
            && e.CsvFileName == fileName && e.SearchProfile!.AppUserId == user.Id, cancellationToken);
        if (!ownsExport) return NotFound();
        var path = _csvExporter.FindFile(user.Id, searchProfileId, fileName);
        return path == null ? NotFound() : PhysicalFile(path, "text/csv; charset=utf-8", fileName, enableRangeProcessing: true);
    }
}
