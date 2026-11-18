using ColdCall.Data;
using ColdCall.Models;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public sealed class AiCompanySearchService(ColdCallDbContext db)
{
    public async Task<AppUser> AuthorizeAsync(int userId, CancellationToken token)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == userId && row.IsEnabled, token)
            ?? throw new UnauthorizedAccessException("Konto jest niedostepne.");
        if (!user.IsAdmin && !await db.UserPlans.AnyAsync(row => row.AppUserId == userId
                && row.ExpiresAtUtc > DateTime.UtcNow && !row.PlanDefinition.IsTrial, token))
            throw new UnauthorizedAccessException("Wyszukiwanie AI wymaga aktywnego platnego planu.");
        return user;
    }

    public async Task<AiCompanySearchResult> SearchAsync(int userId, AiCompanySearchRequest request, CancellationToken token)
    {
        var user = await AuthorizeAsync(userId, token);
        var limit = await PlanRecordLimits.ForUserAsync(db, userId, user.IsAdmin);
        if (limit <= 0) throw new UnauthorizedAccessException("Plan nie pozwala na pobieranie rekordow.");
        Validate(request);
        if (await db.CompanyStatuses.CountAsync(row => request.StatusIds.Contains(row.StatusId), token) != request.StatusIds.Distinct().Count())
            throw new ArgumentException("Nie rozpoznano statusu dzialalnosci.");
        var codes = request.PkdCodes.Select(code => code.Replace(".", "").Trim().ToUpperInvariant()).Distinct().ToArray();
        var pkds = await db.PkdCodes.AsNoTracking().Where(row => codes.Contains(row.CodeWithoutDots!))
            .Select(row => new PkdDto(row.Id, row.Code, row.Name)).ToListAsync(token);
        if (pkds.Select(row => row.Code.Replace(".", "")).Distinct().Count() != codes.Length)
            throw new ArgumentException("Nie rozpoznano wszystkich kodow PKD.");
        int? cityId = null;
        int? voivodeshipId = null;
        int? countyId = null;
        int? municipalityId = null;
        if (!string.IsNullOrWhiteSpace(request.Voivodeship))
        {
            var regions = await db.Wojewodztwa.AsNoTracking().Select(row => new { row.Id, row.Name }).ToListAsync(token);
            var matches = regions.Where(row => AddressNameEquals(row.Name, request.Voivodeship)).ToList();
            if (matches.Count != 1) throw new ArgumentException("Nie rozpoznano jednoznacznie wojewodztwa.");
            voivodeshipId = matches[0].Id;
        }
        if (!string.IsNullOrWhiteSpace(request.County))
        {
            var counties = await db.Powiaty.AsNoTracking().Select(row => new { row.Id, row.Name }).ToListAsync(token);
            var matches = counties.Where(row => AddressNameEquals(row.Name, request.County)).ToList();
            if (matches.Count != 1) throw new ArgumentException("Nie rozpoznano jednoznacznie powiatu.");
            countyId = matches[0].Id;
        }
        if (!string.IsNullOrWhiteSpace(request.Municipality))
        {
            var municipalities = await db.Gminy.AsNoTracking().Select(row => new { row.Id, row.Name }).ToListAsync(token);
            var matches = municipalities.Where(row => AddressNameEquals(row.Name, request.Municipality)).ToList();
            if (matches.Count != 1) throw new ArgumentException("Nie rozpoznano jednoznacznie gminy.");
            municipalityId = matches[0].Id;
        }
        if (countyId.HasValue || municipalityId.HasValue)
        {
            // Infer only the parents required by cascading address controls, never children.
            var parents = await db.Adresy.AsNoTracking()
                .Where(row => (!voivodeshipId.HasValue || row.WojewodztwoId == voivodeshipId)
                    && (!countyId.HasValue || row.PowiatId == countyId)
                    && (!municipalityId.HasValue || row.GminaId == municipalityId))
                .Select(row => new { row.WojewodztwoId, row.PowiatId })
                .Union(db.Companies.AsNoTracking()
                    .Where(row => row.WojewodztwoId.HasValue && row.PowiatId.HasValue
                        && (!voivodeshipId.HasValue || row.WojewodztwoId == voivodeshipId)
                        && (!countyId.HasValue || row.PowiatId == countyId)
                        && (!municipalityId.HasValue || row.GminaId == municipalityId))
                    .Select(row => new { WojewodztwoId = row.WojewodztwoId!.Value, PowiatId = row.PowiatId!.Value }))
                .Take(2).ToListAsync(token);
            if (parents.Count != 1)
                throw new ArgumentException("Nie mozna jednoznacznie ustalic powiatu/gminy. Doprecyzuj wojewodztwo i powiat.");
            voivodeshipId = parents[0].WojewodztwoId;
            countyId = parents[0].PowiatId;
        }
        if (!string.IsNullOrWhiteSpace(request.City))
        {
            var cities = await db.Miejscowosci.AsNoTracking().Where(row => row.Name == request.City.Trim())
                .Select(row => row.Id).Take(2).ToListAsync(token);
            if (cities.Count != 1) throw new ArgumentException("Nie rozpoznano jednoznacznie miejscowosci. Podaj jej pelna nazwe.");
            cityId = cities[0];
        }
        if (request.RadiusKm.HasValue && !cityId.HasValue)
            throw new ArgumentException("Promien wymaga miejscowosci.");
        var locations = cityId.HasValue ? await db.Adresy.AsNoTracking().Where(row => row.MiejscowoscId == cityId)
            .Select(row => new { row.WojewodztwoId, row.PowiatId, row.GminaId }).Distinct().ToListAsync(token) : null;
        if (voivodeshipId.HasValue && locations != null)
            locations = locations.Where(row => row.WojewodztwoId == voivodeshipId.Value).ToList();
        if (countyId.HasValue && locations != null)
            locations = locations.Where(row => row.PowiatId == countyId.Value).ToList();
        if (municipalityId.HasValue && locations != null)
            locations = locations.Where(row => row.GminaId == municipalityId.Value).ToList();
        if (locations?.Count > 1)
        {
            // A city with county rights can share its name with a small village.
            var cityCounties = await db.Powiaty.Where(row => row.Name == request.City!.Trim())
                .Select(row => row.Id).ToListAsync(token);
            locations = locations.Where(row => cityCounties.Contains(row.PowiatId)).ToList();
        }
        if (locations?.Count > 1)
        {
            // Large cities can have both city-wide and district municipality entries.
            var cityMunicipalities = await db.Gminy.Where(row => row.Name == request.City!.Trim())
                .Select(row => row.Id).ToListAsync(token);
            locations = locations.Where(row => cityMunicipalities.Contains(row.GminaId)).ToList();
        }
        if (cityId.HasValue && locations?.Count != 1)
            throw new ArgumentException("Nie mozna jednoznacznie ustalic lokalizacji miejscowosci.");
        var location = locations?.Single();
        var criteria = new SearchProfileCriteria
        {
            CompanyName = request.CompanyName?.Trim() ?? "",
            CompanyStatusIds = CompanyStatusSelection.Serialize(request.StatusIds),
            HasPhone = request.HasPhone, HasEmail = request.HasEmail, HasWebsite = request.HasWebsite,
            DateRangeEnabled = request.StartedFrom.HasValue,
            DateFrom = request.StartedFrom, DateTo = request.StartedTo,
            MiejscowoscId = cityId, HasFirmyWPromieniu = request.RadiusKm.HasValue,
            WojewodztwoId = voivodeshipId ?? location?.WojewodztwoId,
            PowiatId = countyId ?? location?.PowiatId, GminaId = municipalityId ?? location?.GminaId,
            FirmyWPromieniu = request.RadiusKm, RecordLimit = limit
        };
        var search = new CompanySearchQuery(db);
        var companies = await search.BuildCompaniesAsync(criteria, pkds.Select(row => row.Id).ToArray(),
            null, source: db.Companies.AsNoTracking().Where(row => !row.BlockedByRODO), token: token);
        var query = search.SelectDetails(companies);
        var total = await query.CountAsync(token);
        var accessible = Math.Min(total, limit);
        if ((long)(request.Page - 1) * 10 >= accessible && request.Page != 1)
            throw new ArgumentException("Strona przekracza limit dostepnych wynikow.");
        var items = await query.OrderBy(row => row.Name).ThenBy(row => row.Id).Skip((request.Page - 1) * 10)
            .Take(Math.Min(10, Math.Max(0, accessible - (request.Page - 1) * 10))).ToListAsync(token);
        var dto = new SearchProfileCriteriaDto
        {
            CompanyName = criteria.CompanyName, CompanyStatusIds = request.StatusIds,
            HasPhone = criteria.HasPhone, HasEmail = criteria.HasEmail, HasWebsite = criteria.HasWebsite,
            DateRangeEnabled = criteria.DateRangeEnabled, DateFrom = criteria.DateFrom, DateTo = criteria.DateTo,
            MiejscowoscId = cityId, HasFirmyWPromieniu = criteria.HasFirmyWPromieniu,
            WojewodztwoId = criteria.WojewodztwoId, PowiatId = criteria.PowiatId, GminaId = criteria.GminaId,
            FirmyWPromieniu = criteria.FirmyWPromieniu, RecordLimit = limit,
            SortColumn = "name", SortDirection = "asc"
        };
        return new(dto, pkds, total, limit, request.Page, items, $"Znaleziono {total:N0} firm.");
    }

    public async Task<CompanyShortDto?> GetCompanyAsync(int userId, string nip, CancellationToken token)
    {
        await AuthorizeAsync(userId, token);
        if (nip.Length != 10 || !nip.All(char.IsAsciiDigit)) throw new ArgumentException("Niepoprawny NIP.");
        var query = new CompanySearchQuery(db).SelectDetails(
            db.Companies.AsNoTracking().Where(row => !row.BlockedByRODO));
        return await query.FirstOrDefaultAsync(row => row.Nip == nip, token);
    }

    private static bool AddressNameEquals(string stored, string requested) =>
        System.Globalization.CultureInfo.GetCultureInfo("pl-PL").CompareInfo.Compare(stored.Trim(), requested.Trim(),
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace
                | System.Globalization.CompareOptions.IgnoreSymbols) == 0;

    public static void Validate(AiCompanySearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CompanyName) && request.CompanyName.Trim().Length < 3)
            throw new ArgumentException("Nazwa firmy musi miec co najmniej 3 znaki.");
        if (request.Page < 1 || request.Page > 100000 || (request.City?.Length ?? 0) > 250 || (request.Voivodeship?.Length ?? 0) > 250
            || (request.County?.Length ?? 0) > 250 || (request.Municipality?.Length ?? 0) > 250
            || (request.CompanyName?.Length ?? 0) > 1000 || request.PkdCodes == null
            || request.PkdCodes.Length > 20 || request.PkdCodes.Any(code => string.IsNullOrWhiteSpace(code) || code.Length > 12)
            || request.StatusIds == null || request.StatusIds.Length == 0
            || request.StatusIds.Any(id => id < 1 || id > 255))
            throw new ArgumentException("Niepoprawne kryteria wyszukiwania.");
        if (request.RadiusKm.HasValue && !new[] { 1, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 150, 200, 300, 400, 500 }.Contains(request.RadiusKm.Value))
            throw new ArgumentException("Dopuszczalny promien: 1, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 150, 200, 300, 400 lub 500 km.");
        if (request.StartedFrom.HasValue != request.StartedTo.HasValue || request.StartedTo < request.StartedFrom)
            throw new ArgumentException("Podaj poprawny zakres dat.");
    }
}
