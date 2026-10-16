using ColdCall.Data;
using ColdCall.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace ColdCall.Services;

public class CompanySearchQuery
{
    private readonly ColdCallDbContext _db;
    public CompanySearchQuery(ColdCallDbContext db) => _db = db;

    public async Task<IQueryable<CompanyShortDto>> BuildAsync(SearchProfileCriteria criteria, int[]? pkdIds,
        int? modifiedWithinDays, bool addressLocality = false, bool includeAllCompanies = false)
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
        if (modifiedWithinDays.HasValue && modifiedWithinDays is not (7 or 30))
            throw new ArgumentException("Dopuszczalne okresy aktywnosci to 7 lub 30 dni.");
        if (startedFrom.HasValue != startedTo.HasValue || (startedFrom.HasValue && startedTo <= startedFrom))
            throw new ArgumentException("Both dates are required and the end date must be later than the start date.");
        if (new[] { wojewodztwoId, powiatId, gminaId, miejscowoscId }.Any(id => id is <= 0))
            throw new ArgumentException("Niepoprawny identyfikator adresu.");
        if (addressLocality && !miejscowoscId.HasValue)
            throw new ArgumentException("Wybierz miejscowosc.");
        if (radiusKm.HasValue && (!new[] { 1, 5, 10, 20, 50, 100, 200, 500 }.Contains(radiusKm.Value)
            || !miejscowoscId.HasValue))
            throw new ArgumentException("Wybierz miejscowosc i poprawny promien.");
        var query = ApplyDetailFilters(ApplyPkdFilter(BaseCompanyQuery(includeAllCompanies), pkdIds),
            address, hasPhone, hasEmail, hasWebsite, startedFrom, startedTo);
        if (radiusKm.HasValue)
        {
            var center = await LocalityCenter(null, null, gminaId, miejscowoscId!.Value);
            if (center == null) throw new ArgumentException("Brak wspolrzednych dla wybranej miejscowosci.");
            query = ApplyRadiusFilter(query, center, radiusKm.Value);
        }
        else if (addressLocality)
            query = query.Where(company => _db.CompanyAddresses.Any(link => link.CompanyId == company.Id
                && link.Address.MiejscowoscId == miejscowoscId
                && (!gminaId.HasValue || link.Address.GminaId == gminaId)));
        else
            query = ApplyAdministrativeFilter(query, wojewodztwoId, powiatId, gminaId, miejscowoscId);
        if (!string.IsNullOrWhiteSpace(companyName) && companyName.Trim().Length >= 3)
            query = query.Where(x => x.Name.Contains(companyName.Trim()));
        if (modifiedWithinDays.HasValue)
            query = query.Where(row => _db.Companies.Any(company => company.Id == row.Id
                && company.DataModifiedOn.HasValue
                && company.DataModifiedOn >= DateTime.Now.AddDays(-modifiedWithinDays.Value)
                && company.DataModifiedOn <= DateTime.Now));
        return query;
    }

    private IQueryable<CompanyShortDto> BaseCompanyQuery(bool includeAllCompanies) => includeAllCompanies
        ? _db.Companies.AsNoTracking().Select(c => new CompanyShortDto
        {
            Id = c.Id, Name = c.Name, Nip = c.Nip, Phone = c.Phone, Email = c.Email,
            Www = c.Website, DataAktywnosci = c.StartedAt,
            FullAddress = (c.Miejscowosc!.Name ?? "") + " " + (c.PostalCode ?? "") + " " + (c.Street ?? "") + " "
                + (c.BuildingNumber ?? "") + (c.ApartmentNumber == null ? "" : "/" + c.ApartmentNumber)
        })
        :
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

    private IQueryable<CompanyShortDto> ApplyAdministrativeFilter(IQueryable<CompanyShortDto> query,
        int? wojewodztwoId, int? powiatId, int? gminaId, int? miejscowoscId)
    {
        if (!wojewodztwoId.HasValue && !powiatId.HasValue && !gminaId.HasValue && !miejscowoscId.HasValue)
            return query;
        return query.Where(row => _db.Companies.Any(company => company.Id == row.Id
            && (!wojewodztwoId.HasValue || company.WojewodztwoId == wojewodztwoId)
            && (!powiatId.HasValue || company.PowiatId == powiatId)
            && (!gminaId.HasValue || company.GminaId == gminaId)
            && (!miejscowoscId.HasValue || company.MiejscowoscId == miejscowoscId)));
    }

    private async Task<Point?> LocalityCenter(int? wojewodztwoId, int? powiatId, int? gminaId, int miejscowoscId)
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
            .SingleOrDefaultAsync();
        return coordinates?.Longitude == null || coordinates.Latitude == null
            ? null : new Point((double)coordinates.Longitude.Value, (double)coordinates.Latitude.Value) { SRID = 4326 };
    }

    private IQueryable<CompanyShortDto> ApplyRadiusFilter(IQueryable<CompanyShortDto> query, Point center, int radiusKm)
    {
        var radiusMeters = radiusKm * 1000d;
        // Keep distance evaluation in SQL: geography.STDistance returns meters, unlike client-side NTS.
        return query.Where(company => _db.CompanyAddresses.Any(link => link.CompanyId == company.Id
            && link.Address.Location != null && link.Address.Location.Distance(center) <= radiusMeters));
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

}
