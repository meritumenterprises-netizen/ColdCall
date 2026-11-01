using ColdCall.Data;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public record CsvExportQuota(int Used, int? Limit, int? Remaining)
{
    public DateTime? ExportBlockedUntilUtc { get; set; }
}

public static class CsvExportLimits
{
    public static async Task<CsvExportQuota> ForSearchAsync(ColdCallDbContext db, int userId, int searchId,
        CancellationToken token = default)
    {
        var plan = await db.UserPlans.AsNoTracking()
            .Where(p => p.AppUserId == userId && p.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(p => p.PlanDefinition.Order).ThenByDescending(p => p.ExpiresAtUtc).ThenByDescending(p => p.Id)
            .Select(p => new { Limit = p.PlanDefinition.MaxNumberOfCSVExportsPerSearch }).FirstOrDefaultAsync(token);
        var limit = plan == null ? 0 : plan.Limit;
        var used = await db.CsvExports.CountAsync(e => e.SearchProfileId == searchId, token);
        var deletionQuota = await CsvDeletionQuota.GetAsync(db, userId, token);
        return new CsvExportQuota(used, limit, limit.HasValue ? Math.Max(0, limit.Value - used) : null)
        {
            ExportBlockedUntilUtc = deletionQuota.ExportBlockedUntilUtc
        };
    }
}
