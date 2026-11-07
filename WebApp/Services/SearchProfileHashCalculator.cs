using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ColdCall.Models;
using ColdCall.Data;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public static class SearchProfileHashCalculator
{
    public static string CalculateExport(SearchProfile profile, string resultTab) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(Calculate(profile) + ":" + resultTab)));

    public static bool LegacyExportMatchesTab(string fileName, string resultTab)
    {
        var suffix = resultTab switch
        {
            "all" => "all", "3" => "3 days", "7" => "7 days", "notes" => "notes",
            _ => throw new ArgumentException("Niepoprawna zakladka eksportu.")
        };
        return System.Text.RegularExpressions.Regex.IsMatch(fileName,
            "_" + suffix + @"_[0-9]{8}_[0-9]{6}\.CSV$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

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
            CompanyStatusIds = CompanyStatusSelection.Parse(c.CompanyStatusIds),
            c.CompanyName, c.SortColumn, c.SortDirection, c.Address,
            c.HasPhone, c.HasEmail, c.HasWebsite, c.DateRangeEnabled,
            c.DateFrom, c.DateTo, c.RecordLimit, c.WojewodztwoId, c.PowiatId,
            c.GminaId, c.MiejscowoscId, c.HasFirmyWPromieniu, c.FirmyWPromieniu
        });
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
