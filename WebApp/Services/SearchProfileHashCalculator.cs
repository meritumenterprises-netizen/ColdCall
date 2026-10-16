using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ColdCall.Models;
using ColdCall.Data;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public static class SearchProfileHashCalculator
{
    public static async Task FreezeLegacyExportsAsync(ColdCallDbContext db, SearchProfile profile,
        CancellationToken cancellationToken = default)
    {
        var exports = await db.CsvExports.Where(e => e.SearchProfileId == profile.Id && e.SearchProfileHash == null)
            .ToListAsync(cancellationToken);
        if (exports.Count == 0) return;
        var hash = Calculate(profile);
        foreach (var export in exports) export.SearchProfileHash = hash;
    }

    public static string Calculate(SearchProfile profile)
    {
        var c = profile.Criteria ?? new SearchProfileCriteria();
        // Explicit fields keep identities, navigation properties and the display name out of the fingerprint.
        var payload = JsonSerializer.Serialize(new
        {
            PkdCodeIds = profile.PkdCodes.Select(p => p.PkdCodeId).Distinct().OrderBy(id => id).ToArray(),
            c.CompanyName, c.SortColumn, c.SortDirection, c.Address,
            c.HasPhone, c.HasEmail, c.HasWebsite, c.DateRangeEnabled,
            c.DateFrom, c.DateTo, c.RecordLimit, c.WojewodztwoId, c.PowiatId,
            c.GminaId, c.MiejscowoscId, c.HasFirmyWPromieniu, c.FirmyWPromieniu
        });
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
