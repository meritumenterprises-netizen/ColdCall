using ColdCall.Data;
using ColdCall.Models;
using ColdCall.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace ColdCall.Controllers.Api;

[ApiController]
[Route("api/companies")]
[Authorize]
public partial class CompaniesController : ControllerBase
{
    private readonly ColdCallDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly CompanyCsvExportService _csvExporter;
    private readonly IMemoryCache _searchCache;
    private readonly CompanySearchSnapshotCache? _snapshots;
    private sealed record SearchSummary(int TotalWithoutLimit, bool HasNotes);

    public CompaniesController(ColdCallDbContext db, ICurrentUserService currentUserService, CompanyCsvExportService csvExporter, IMemoryCache searchCache,
        CompanySearchSnapshotCache? snapshots = null)
    {
        _db = db;
        _currentUserService = currentUserService;
        _csvExporter = csvExporter;
        _searchCache = searchCache;
        _snapshots = snapshots;
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
        [FromQuery] int? modifiedWithinDays = null,
        [FromQuery] string? companyStatusIds = null,
        [FromQuery] bool notesOnly = false,
        [FromQuery] bool refreshCount = true)
    {
        var watch = Stopwatch.StartNew();
        var timings = new List<string>();
        var previousMilliseconds = 0d;
        void Mark(string stage)
        {
            var elapsed = watch.Elapsed.TotalMilliseconds;
            timings.Add(FormattableString.Invariant($"{stage};dur={elapsed - previousMilliseconds:F1}"));
            previousMilliseconds = elapsed;
        }
        try
        {
            var user = await _currentUserService.GetOrCreateAsync(User);
            Mark("user");
            var hasActivePlan = await _db.UserPlans.AnyAsync(x => x.AppUserId == user.Id && x.ExpiresAtUtc > DateTime.UtcNow);
            var isAdmin = user.IsAdmin;
            if (!isAdmin && !hasActivePlan)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, new { message = "Plan wygasl albo nie zostal jeszcze zakupiony." });
            }

            _db.Database.SetCommandTimeout(120);
            pageSize = 10;
            var maximumRecordLimit = await PlanRecordLimits.ForUserAsync(_db, user.Id, isAdmin);
            if (maximumRecordLimit <= 0)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Plan nie pozwala na pobieranie rekordow." });
            // The plan, not a request parameter or a saved search, determines the result cap.
            recordLimit = maximumRecordLimit;
            Mark("plan");
            IQueryable<Company> query;
            var search = new CompanySearchQuery(_db);
            bool hasNotes;
            int totalWithoutLimit;
            try
            {
                var criteria = new SearchProfileCriteria
                {
                    CompanyName = companyName ?? "", Address = address ?? "", HasPhone = hasPhone, HasEmail = hasEmail,
                    CompanyStatusIds = CompanyStatusSelection.Serialize(CompanyStatusSelection.Parse(companyStatusIds)),
                    HasWebsite = hasWebsite, DateRangeEnabled = startedFrom.HasValue || startedTo.HasValue,
                    DateFrom = startedFrom, DateTo = startedTo, WojewodztwoId = wojewodztwoId, PowiatId = powiatId,
                    GminaId = gminaId, MiejscowoscId = miejscowoscId,
                    HasFirmyWPromieniu = radiusKm.HasValue, FirmyWPromieniu = radiusKm
                };
                if (modifiedWithinDays.HasValue && modifiedWithinDays is not (3 or 7 or 30))
                    throw new ArgumentException("Dopuszczalne okresy modyfikacji to 3, 7 lub 30 dni.");
                if (_snapshots != null && (pkdIds?.Any(id => id > 0) == true
                    || (criteria.CompanyName?.Trim().Length ?? 0) >= 3))
                {
                    var snapshotPage = await TrySnapshotPageAsync(search, criteria, pkdIds ?? Array.Empty<int>(), user.Id,
                        sortBy, direction, addressLocality, notesOnly, modifiedWithinDays, refreshCount,
                        maximumRecordLimit, page, pageSize, Mark, HttpContext.RequestAborted);
                    if (snapshotPage != null) return Ok(snapshotPage);
                }
                query = await search.BuildCompaniesAsync(criteria, pkdIds, modifiedWithinDays, addressLocality,
                    token: HttpContext.RequestAborted);
                Mark("prepare");
                // A sort/page request reuses counts only for the same user and exact
                // filters. New searches and explicit refreshes always recalculate.
                var summaryKey = "CompanySearchSummary:" + Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                    {
                        userId = user.Id, criteria,
                        notesVersion = _searchCache.Get<Guid?>("CompanyNotesVersion:" + user.Id),
                        pkdIds = pkdIds?.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray(),
                        modifiedWithinDays, addressLocality, notesOnly
                    }))));
                if (!refreshCount && _searchCache.TryGetValue(summaryKey, out SearchSummary? cached) && cached is not null)
                {
                    hasNotes = cached.HasNotes;
                    totalWithoutLimit = cached.TotalWithoutLimit;
                    if (notesOnly) query = search.WithUserNotes(query, user.Id);
                }
                else
                {
                    hasNotes = await _db.CompanyUserNotes.AsNoTracking()
                        .AnyAsync(note => note.UserId == user.Id && note.Notes.Trim() != "", HttpContext.RequestAborted);
                    if (hasNotes)
                    {
                        var allResults = modifiedWithinDays.HasValue
                            ? await search.BuildCompaniesAsync(criteria, pkdIds, null, addressLocality,
                                token: HttpContext.RequestAborted) : query;
                        hasNotes = await search.WithUserNotes(allResults, user.Id).AnyAsync(HttpContext.RequestAborted);
                    }
                    if (notesOnly) query = search.WithUserNotes(query, user.Id);
                    totalWithoutLimit = await query.TagWith("CompanySearch.Count").CountAsync(HttpContext.RequestAborted);
                    _searchCache.Set(summaryKey, new SearchSummary(totalWithoutLimit, hasNotes),
                        new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(2))
                            .SetAbsoluteExpiration(TimeSpan.FromMinutes(10)));
                }
            }
            catch (ArgumentException exception)
            {
                return BadRequest(new { message = exception.Message });
            }

            var total = Math.Min(totalWithoutLimit, recordLimit.Value);
            Mark("summary");
            page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
            var offset = (page - 1) * pageSize;
            List<CompanyShortDto> items;
            var take = Math.Min(pageSize, total - offset);
            // Page on narrow keys before resolving formatted address/business type.
            var orderedIds = await search.OrderedIds(query, sortBy, direction)
                .Skip(offset).Take(take).TagWith("CompanySearch.PageIds").ToArrayAsync(HttpContext.RequestAborted);
            Mark("page");
            // IDs have already passed all criteria and user/notes filters above.
            items = await search.SelectDetails(_db.Companies.AsNoTracking().Where(row => orderedIds.Contains(row.Id)))
                .TagWith("CompanySearch.PageDetails").ToListAsync(HttpContext.RequestAborted);
            var positions = orderedIds.Select((id, position) => new { id, position })
                .ToDictionary(row => row.id, row => row.position);
            items.Sort((left, right) => positions[left.Id].CompareTo(positions[right.Id]));
            Mark("details");

            var pageIds = items.Select(item => item.Id).ToArray();
            var notes = await _db.CompanyUserNotes.AsNoTracking()
                .Where(note => note.UserId == user.Id && pageIds.Contains(note.CompanyId))
                .ToDictionaryAsync(note => note.CompanyId, note => note.Notes, HttpContext.RequestAborted);
            foreach (var item in items)
                item.UserNotes = notes.GetValueOrDefault(item.Id);
            Mark("notes");
            return Ok(new PagedResult<CompanyShortDto>(items, total, page, pageSize, QueryableExtensions.CompanyShortSortableColumns, totalWithoutLimit, hasNotes));
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (SqlException exception) when (SqlRequestCancellation.IsExpected(exception, HttpContext.RequestAborted))
        {
            return new EmptyResult();
        }
        catch (SqlException exception) when (exception.Number == -2)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "SearchTimeout",
                message = "Wyszukiwanie przekroczyło limit czasu. Spróbuj ponownie lub zawęź kryteria."
            });
        }
        finally
        {
            if (!Response.HasStarted)
            {
                timings.Add(FormattableString.Invariant($"controller;dur={watch.Elapsed.TotalMilliseconds:F1}"));
                Response.Headers["Server-Timing"] = string.Join(", ", timings);
            }
        }
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
                x.WojewodztwoId, x.MiejscowoscId, x.GminaId, x.PowiatId, x.DataModifiedOn, x.ResumedOn, null, x.SuspendedOn))
            .SingleOrDefaultAsync(cancellationToken);
        if (company == null) return NotFound();

        var pkds = await _db.CompanyPkds.AsNoTracking().Where(x => x.CompanyId == id)
            .OrderByDescending(x => x.Prevailing).ThenBy(x => x.PkdCode.Code)
            .Select(x => new CompanyPkdDto(x.PkdCode.Id, x.PkdCode.Code, x.PkdCode.Name, x.Prevailing))
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
        var days = request.ResultTab switch { "30" => 30, "7" => 7, "3" => 3, "all" or "notes" => (int?)null, _ => request.ModifiedWithinDays };
        var notesOnly = request.ResultTab == null ? request.NotesOnly : request.ResultTab == "notes";
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
            var userLock = "UserExports:" + user.Id;
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={userLock}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 50001, 'CSV user lock unavailable', 1;", cancellationToken);
            var lockName = "CSVExport:" + profile.Id;
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={lockName}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 50001, 'CSV export lock unavailable', 1;", cancellationToken);
            var quota = await CsvExportLimits.ForSearchAsync(_db, user.Id, profile.Id, cancellationToken);
            if (quota.ExportBlockedUntilUtc.HasValue)
                return StatusCode(429, new { code = "CsvExportCooldown", message = "Po usunięciu 3 plików CSV eksport jest zablokowany na 24 godziny.", quota.ExportBlockedUntilUtc });
            if (quota.Remaining == 0)
                return StatusCode(409, new { message = "Osiągnięto limit eksportów CSV dla tego wyszukiwania." });
            var search = new CompanySearchQuery(_db);
            var query = await search.BuildCompaniesAsync(profile.Criteria ?? new SearchProfileCriteria(),
                profile.PkdCodes.Select(x => x.PkdCodeId).ToArray(), days, token: cancellationToken);
            if (notesOnly) query = search.WithUserNotes(query, user.Id);
            var ids = await search.OrderedIds(query, request.SortBy, request.Direction)
                .Skip(request.Offset).Take(limit).ToListAsync(cancellationToken);
            if (ids.Count == 0) return BadRequest(new { message = "Brak rekordow do eksportu." });
            var csv = await _csvExporter.ExportAsync(user, profile, ids, days, cancellationToken, request.SearchName, notesOnly);
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

    [HttpDelete("exports/{id:int}")]
    public async Task<IActionResult> DeleteExport(int id, [FromServices] CsvExportCleanupService cleanup,
        CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        if (!user.IsAdmin)
        {
            var now = DateTime.UtcNow;
            var allowed = await _db.UserPlans.AsNoTracking()
                .Where(p => p.AppUserId == user.Id && p.ExpiresAtUtc > now)
                .OrderByDescending(p => p.ExpiresAtUtc).ThenByDescending(p => p.Id)
                .Select(p => p.PlanDefinition.EnableCSVDeleting).FirstOrDefaultAsync(cancellationToken);
            if (!allowed) return StatusCode(403, new { message = "Aktualny plan nie pozwala na usuwanie plikow CSV." });
        }
        try
        {
            return await cleanup.DeleteExportAsync(user.Id, id, cancellationToken) ? NoContent() : NotFound();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<CompaniesController>>()
                .LogError(ex, "CSV deletion failed for user {UserId}, export {ExportId}", user.Id, id);
            return StatusCode(500, new { message = "Nie udalo sie usunac pliku CSV." });
        }
        catch (CsvDailyDeletionLimitException ex)
        {
            return StatusCode(429, new { code = "DailyCsvDeletionLimit", message = ex.Message, remaining = ex.Remaining });
        }
    }

    [HttpGet("DownloadCSV")]
    public async Task<IActionResult> DownloadCSV([FromQuery] int searchProfileId,
        [FromQuery] string fileName, CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetOrCreateAsync(User);
        var ownsExport = await _db.CsvExports.AsNoTracking().AnyAsync(e => e.SearchProfileId == searchProfileId
            && e.CsvFileName == fileName && e.SearchProfile!.AppUserId == user.Id, cancellationToken);
        if (!ownsExport) return NotFound();
        var path = _csvExporter.FindFile(user.Id, searchProfileId, fileName);
        return path == null ? NotFound() : PhysicalFile(path, "text/csv; charset=utf-8", fileName, enableRangeProcessing: true);
    }
}
