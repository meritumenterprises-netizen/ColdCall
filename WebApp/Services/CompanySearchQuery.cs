using ColdCall.Data;
using ColdCall.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace ColdCall.Services;

public class CompanySearchQuery
{
    private readonly ColdCallDbContext _db;
    public CompanySearchQuery(ColdCallDbContext db) => _db = db;

    public async Task<IQueryable<CompanyShortDto>> BuildAsync(SearchProfileCriteria criteria, int[]? pkdIds,
        int? modifiedWithinDays, bool addressLocality = false, bool includeAllCompanies = false,
        CancellationToken token = default) =>
        SelectDetails(await BuildCompaniesAsync(criteria, pkdIds, modifiedWithinDays, addressLocality,
            includeAllCompanies, token: token), includeAllCompanies);

    public async Task<IQueryable<Company>> BuildCompaniesAsync(SearchProfileCriteria criteria, int[]? pkdIds,
        int? modifiedWithinDays, bool addressLocality = false, bool includeAllCompanies = false,
        IQueryable<Company>? source = null, CancellationToken token = default)
    {
        var address = criteria.Address;
        var hasPhone = criteria.HasPhone;
        var hasEmail = criteria.HasEmail;
        var hasWebsite = criteria.HasWebsite;
        var startedFrom = criteria.DateRangeEnabled ? criteria.DateFrom : null;
        var startedTo = criteria.DateRangeEnabled ? criteria.DateTo : null;
        var wojewodztwoId = criteria.WojewodztwoId;
        var powiatId = criteria.PowiatId;
        var gminaId = criteria.GminaId;
        var miejscowoscId = criteria.MiejscowoscId;
        var radiusKm = criteria.HasFirmyWPromieniu == true ? criteria.FirmyWPromieniu : null;
        var companyName = criteria.CompanyName;
        if (modifiedWithinDays.HasValue && modifiedWithinDays is not (3 or 7))
            throw new ArgumentException("Dopuszczalne okresy modyfikacji to 3 lub 7 dni.");
        if (startedFrom.HasValue != startedTo.HasValue || (startedFrom.HasValue && startedTo < startedFrom))
            throw new ArgumentException("Both dates are required and the end date must be on or after the start date.");
        if (new[] { wojewodztwoId, powiatId, gminaId, miejscowoscId }.Any(id => id is <= 0))
            throw new ArgumentException("Niepoprawny identyfikator adresu.");
        if (addressLocality && !miejscowoscId.HasValue)
            throw new ArgumentException("Wybierz miejscowosc.");
        if (radiusKm.HasValue && (!new[] { 1, 5, 10, 20, 50, 100, 200, 500 }.Contains(radiusKm.Value)
            || !miejscowoscId.HasValue))
            throw new ArgumentException("Wybierz miejscowosc i poprawny promien.");
        var companies = source ?? _db.Companies.AsNoTracking();
        if (!includeAllCompanies || criteria.CompanyStatusIds != null)
        {
            var statuses = CompanyStatusSelection.Parse(criteria.CompanyStatusIds).Select(status => (byte)status).ToArray();
            companies = companies.Where(company => statuses.Contains(company.StatusDzialalnosciId));
        }
        if (modifiedWithinDays.HasValue)
        {
            var now = DateTime.Now;
            var since = now.AddDays(-modifiedWithinDays.Value);
            companies = companies.Where(company => company.ImportedOn >= since && company.ImportedOn <= now);
        }
        if (!radiusKm.HasValue && !addressLocality)
        {
            if (wojewodztwoId.HasValue) companies = companies.Where(c => c.WojewodztwoId == wojewodztwoId);
            if (powiatId.HasValue) companies = companies.Where(c => c.PowiatId == powiatId);
            if (gminaId.HasValue) companies = companies.Where(c => c.GminaId == gminaId);
            if (miejscowoscId.HasValue) companies = companies.Where(c => c.MiejscowoscId == miejscowoscId);
        }
        companies = ApplyDetailFilters(ApplyPkdFilter(companies, pkdIds),
            radiusKm.HasValue ? null : address, includeAllCompanies, hasPhone, hasEmail, hasWebsite, startedFrom, startedTo);
        if (radiusKm.HasValue)
        {
            // Administrative selections identify the center only; candidates are filtered solely by distance.
            var center = await LocalityCenter(wojewodztwoId, powiatId, gminaId, miejscowoscId!.Value, token);
            if (center == null) throw new ArgumentException("Brak wspolrzednych dla wybranej miejscowosci.");
            companies = await ApplyRadiusFilterAsync(companies, center, radiusKm.Value, token);
        }
        else if (addressLocality)
            companies = companies.Where(company => _db.CompanyAddresses.Any(link => link.CompanyId == company.Id
                && link.Address.MiejscowoscId == miejscowoscId
                && (!gminaId.HasValue || link.Address.GminaId == gminaId)));
        if (!string.IsNullOrWhiteSpace(companyName) && companyName.Trim().Length >= 3)
        {
            var name = companyName.Trim();
            var indexReady = await _db.Database.SqlQuery<int>(
                $"SELECT CONVERT(int, IsReady) AS [Value] FROM ColdCall.CompanyNameGramBuild WHERE Id = 1")
                .SingleAsync(token);
            var useTrigrams = indexReady == 1 && name.All(character => character >= ' ' && character <= '~'
                || "ĄĆĘŁŃÓŚŹŻąćęłńóśźż".Contains(character));
            if (useTrigrams)
            {
                var candidateEstimate = await _db.Database.SqlQuery<long>($"""
                    SELECT COALESCE(MIN(COALESCE(f.CompanyCount, CONVERT(bigint, 0))), CONVERT(bigint, 0)) AS [Value]
                    FROM ColdCall.CompanyNameGramPositions p
                    LEFT JOIN ColdCall.CompanyNameGramFrequency f ON f.Gram = SUBSTRING({name}, p.Position, 3)
                    WHERE p.Position <= DATALENGTH({name}) / 2 - 2
                    """).SingleAsync(token);
                // Broad terms should not materialize hundreds of thousands of matching IDs.
                useTrigrams = candidateEstimate <= 100000;
            }
            if (useTrigrams)
            {
                var candidateIds = _db.Database.SqlQuery<int>(
                    $"SELECT CompanyId AS [Value] FROM ColdCall.CompanyNameCandidates({name})");
                // The function intersects selective grams and verifies the complete literal substring.
                companies = companies.Where(x => candidateIds.Contains(x.Id));
            }
            else
                companies = companies.Where(x => x.Name.Contains(name));
        }
        return companies;
    }

    public IQueryable<CompanyShortDto> SelectDetails(IQueryable<Company> companies, bool includeAllCompanies = false) =>
        BaseCompanyQuery(companies, includeAllCompanies);

    public IQueryable<int> OrderedIds(IQueryable<Company> query, string? sortBy, string? direction)
    {
        var key = (sortBy ?? "name").ToLowerInvariant() switch
        {
            "dataaktywnosci" => "startedAt",
            "www" => "website",
            "typydzialalnosci" => "businessTypeName",
            _ => sortBy
        };
        if (string.Equals(sortBy, "id", StringComparison.OrdinalIgnoreCase))
            return (string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase)
                ? query.OrderByDescending(company => company.Id) : query.OrderBy(company => company.Id))
                .Select(company => company.Id);
        return query.ApplyCompanySort(key, direction).ThenBy(company => company.Id).Select(company => company.Id);
    }

    public IQueryable<Company> WithUserNotes(IQueryable<Company> query, int userId) =>
        query.Where(company => _db.CompanyUserNotes.Any(note => note.CompanyId == company.Id
            && note.UserId == userId && note.Notes.Trim() != ""));

    // Sort indexed address/name keys before materializing the formatted view.
    // The view's FullAddress is calculated from several columns and cannot use
    // the existing AddressSort/Id indexes.
    public IQueryable<int> OrderedIds(IQueryable<CompanyShortDto> query, string? sortBy, string? direction)
    {
        if (string.Equals(sortBy, "fullAddress", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sortBy, "name", StringComparison.OrdinalIgnoreCase))
        {
            var matchingIds = query.Select(row => row.Id);
            return _db.Companies.AsNoTracking().Where(company => matchingIds.Contains(company.Id))
                .ApplyCompanySort(sortBy, direction).ThenBy(company => company.Id)
                .Select(company => company.Id);
        }
        return query.ApplyCompanyShortSort(sortBy, direction).Select(row => row.Id);
    }

    public IQueryable<CompanyShortDto> WithUserNotes(IQueryable<CompanyShortDto> query, int userId) =>
        query.Where(row => _db.CompanyUserNotes.Any(note => note.CompanyId == row.Id
            && note.UserId == userId && note.Notes.Trim() != ""));

    private IQueryable<CompanyShortDto> BaseCompanyQuery(IQueryable<Company> companies, bool includeAllCompanies) => includeAllCompanies
        ? companies.Select(c => new CompanyShortDto
        {
            Id = c.Id, Name = c.Name, Nip = c.Nip, Phone = c.Phone, Email = c.Email,
            Www = c.Website,
            DataAktywnosci = c.StartedAt,
            DataModifiedOn = c.DataModifiedOn,
            FullAddress = (c.Miejscowosc!.Name ?? "") + " " + (c.PostalCode ?? "") + " " + (c.Street ?? "") + " "
                + (c.BuildingNumber ?? "") + (c.ApartmentNumber == null ? "" : "/" + c.ApartmentNumber)
        })
        :
        // Keep matching/counting on Companies. Resolve presentation fields only
        // when rows are materialized, rather than joining the view for every match.
        companies.Select(c => new CompanyShortDto
        {
            Id = c.Id, Name = c.Name, Nip = c.Nip, Phone = c.Phone, Email = c.Email,
            Www = c.Website, DataAktywnosci = c.StartedAt, DataModifiedOn = c.DataModifiedOn,
            FullAddress = _db.CompaniesShort.Where(row => row.Id == c.Id)
                .Select(row => row.FullAddress).FirstOrDefault()!,
            TypyDzialalnosci = c.BusinessType!.TypDzialalnosci
        });

    private IQueryable<Company> ApplyPkdFilter(IQueryable<Company> query, int[]? pkdIds)
    {
        var selectedPkdIds = pkdIds?.Where(x => x > 0).Distinct().ToArray() ?? Array.Empty<int>();
        return selectedPkdIds.Length == 0
            ? query
            : query.Where(company => _db.CompanyPkds.Any(companyPkd =>
                companyPkd.CompanyId == company.Id && selectedPkdIds.Contains(companyPkd.PkdCodeId)));
    }

    private async Task<Point?> LocalityCenter(int? wojewodztwoId, int? powiatId, int? gminaId, int miejscowoscId, CancellationToken token)
    {
        // The dictionary has no center point; estimate it from distinct valid address coordinates.
        var coordinates = await _db.Adresy.AsNoTracking()
            .Where(a => (!wojewodztwoId.HasValue || a.WojewodztwoId == wojewodztwoId)
                && (!powiatId.HasValue || a.PowiatId == powiatId)
                && (!gminaId.HasValue || a.GminaId == gminaId) && a.MiejscowoscId == miejscowoscId
                && a.Latitude.HasValue && a.Longitude.HasValue
                && a.Latitude >= -90 && a.Latitude <= 90 && a.Longitude >= -180 && a.Longitude <= 180
                && (a.Latitude != 0 || a.Longitude != 0))
            .Select(a => new { a.Latitude, a.Longitude }).Distinct()
            .GroupBy(a => 1)
            .Select(group => new { Latitude = group.Average(a => a.Latitude), Longitude = group.Average(a => a.Longitude) })
            .SingleOrDefaultAsync(token);
        return coordinates?.Longitude == null || coordinates.Latitude == null
            ? null : new Point((double)coordinates.Longitude.Value, (double)coordinates.Latitude.Value) { SRID = 4326 };
    }

    private async Task<IQueryable<Company>> ApplyRadiusFilterAsync(
        IQueryable<Company> query, Point center, int radiusKm, CancellationToken token)
    {
        var radiusMeters = radiusKm * 1000d;
        // Keep distance evaluation in SQL: geography.STDistance returns meters, unlike client-side NTS.
        var nearbyAddressIds = _db.Adresy.AsNoTracking()
            .Where(address => address.Location != null && address.Location.Distance(center) <= radiusMeters)
            .Select(address => address.Id);
        // Resolve small spatial sets before TOP/ORDER BY can favor scanning the whole company index.
        // Large radii keep the database-side query rather than loading unbounded ID lists.
        const int addressLimit = 20000;
        const int companyLimit = 50000;
        var addressIds = await nearbyAddressIds.Take(addressLimit + 1).ToArrayAsync(token);
        if (addressIds.Length > addressLimit)
            return await ApplyWideRadiusFilterAsync(query, center, radiusMeters, token);
        var companyIds = await _db.CompanyAddresses.AsNoTracking()
            .Where(link => addressIds.Contains(link.AddressId))
            .Select(link => link.CompanyId).Distinct().Take(companyLimit + 1).ToArrayAsync(token);
        return companyIds.Length > companyLimit
            ? await ApplyWideRadiusFilterAsync(query, center, radiusMeters, token)
            : query.Where(company => companyIds.Contains(company.Id));
    }

    private async Task<IQueryable<Company>> ApplyWideRadiusFilterAsync(
        IQueryable<Company> query, Point center, double radiusMeters, CancellationToken token)
    {
        // Keep the connection open until the request's DbContext is disposed so all
        // count/page/detail queries share these candidates. Pool reset removes the table.
        if (_db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            await _db.Database.OpenConnectionAsync(token);
        var table = "#RadiusCompanies_" + Guid.NewGuid().ToString("N");
        // A straight path in latitude/longitude is bounded by the maximum WGS84
        // metric (111700 m/degree). The 1% margin also exceeds STDistance's error.
        // Only this strictly interior rectangle can bypass the expensive distance call.
        var latitudeHalfWidth = radiusMeters * 0.99 / (Math.Sqrt(2) * 111700);
        var minLatitude = Math.Max(-90, center.Y - latitudeHalfWidth);
        var maxLatitude = Math.Min(90, center.Y + latitudeHalfWidth);
        var closestLatitudeToEquator = minLatitude <= 0 && maxLatitude >= 0
            ? 0 : Math.Min(Math.Abs(minLatitude), Math.Abs(maxLatitude));
        var longitudeHalfWidth = Math.Min(180, latitudeHalfWidth /
            Math.Cos(closestLatitudeToEquator * Math.PI / 180));
        await _db.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE [{table}] ([Value] int NOT NULL PRIMARY KEY)", token);
        await _db.Database.ExecuteSqlRawAsync($"""
            DECLARE @Center geography = geography::Point(@Latitude, @Longitude, 4326);
            INSERT INTO [{table}] ([Value])
            SELECT DISTINCT link.CompanyId
            FROM ColdCall.Adresy AS address
            INNER JOIN ColdCall.CompanyAddress AS link ON link.AddressId = address.Id
            WHERE address.Location IS NOT NULL
              AND CASE WHEN address.Location.Lat BETWEEN @MinLatitude AND @MaxLatitude
                         AND address.Location.Long BETWEEN @MinLongitude AND @MaxLongitude THEN 1
                       WHEN address.Location.STDistance(@Center) <= @RadiusMeters THEN 1
                       ELSE 0 END = 1
            OPTION (RECOMPILE);
            """, new object[]
            {
                new SqlParameter("@Latitude", center.Y),
                new SqlParameter("@Longitude", center.X),
                new SqlParameter("@RadiusMeters", radiusMeters),
                new SqlParameter("@MinLatitude", minLatitude),
                new SqlParameter("@MaxLatitude", maxLatitude),
                new SqlParameter("@MinLongitude", center.X - longitudeHalfWidth),
                new SqlParameter("@MaxLongitude", center.X + longitudeHalfWidth)
            }, token);
        // Only the generated GUID identifier is interpolated; all coordinates are parameters.
        var ids = _db.Database.SqlQueryRaw<int>($"SELECT [Value] FROM [{table}]");
        return query.Where(company => ids.Contains(company.Id));
    }

    private IQueryable<Company> ApplyDetailFilters(
        IQueryable<Company> query, string? address, bool includeAllCompanies, bool? hasPhone, bool? hasEmail,
        bool? hasWebsite, DateOnly? startedFrom, DateOnly? startedTo)
    {
        if (!string.IsNullOrWhiteSpace(address))
        {
            var term = address.Trim();
            query = includeAllCompanies
                ? query.Where(c => ((c.Miejscowosc!.Name ?? "") + " " + (c.PostalCode ?? "") + " " + (c.Street ?? "") + " "
                    + (c.BuildingNumber ?? "") + (c.ApartmentNumber == null ? "" : "/" + c.ApartmentNumber)).Contains(term))
                : query.Where(c => _db.CompaniesShort.Any(row => row.Id == c.Id && row.FullAddress.Contains(term)));
        }
        if (hasPhone.HasValue)
            query = hasPhone.Value ? query.Where(x => x.Phone != null && x.Phone.Trim() != "")
                : query.Where(x => x.Phone == null || x.Phone.Trim() == "");
        if (hasEmail.HasValue)
            query = hasEmail.Value ? query.Where(x => x.Email != null && x.Email.Trim() != "")
                : query.Where(x => x.Email == null || x.Email.Trim() == "");
        if (hasWebsite.HasValue)
            query = hasWebsite.Value ? query.Where(x => x.Website != null && x.Website.Trim() != "")
                : query.Where(x => x.Website == null || x.Website.Trim() == "");
        if (startedFrom.HasValue && startedTo.HasValue)
        {
            var from = startedFrom.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(x => x.StartedAt >= from);
            if (startedTo.Value == DateOnly.MaxValue)
                query = query.Where(x => x.StartedAt <= DateTime.MaxValue);
            else
            {
                var until = startedTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
                query = query.Where(x => x.StartedAt < until);
            }
        }
        return query;
    }

}
