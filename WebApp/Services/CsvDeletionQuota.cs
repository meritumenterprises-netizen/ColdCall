using ColdCall.Data;
using ColdCall.Models;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public static class CsvDeletionQuota
{
    public static DateOnly PolishDate(DateTime utc) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")));

    public static async Task<CsvDeletionQuotaDto> GetAsync(ColdCallDbContext db, int userId, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        var result = new CsvDeletionQuotaDto { QuotaDate = PolishDate(now) };
        if (await db.Users.AnyAsync(user => user.Id == userId && user.IsAdmin, token)) return result;
        var code = await db.UserPlans.AsNoTracking().Where(plan => plan.AppUserId == userId && plan.ExpiresAtUtc > now)
            .OrderByDescending(plan => plan.ExpiresAtUtc).ThenByDescending(plan => plan.Id)
            .Select(plan => plan.PlanDefinition.Code).FirstOrDefaultAsync(token);
        if (!string.Equals(code, "PX0", StringComparison.OrdinalIgnoreCase)) return result;
        result.Limit = 3;
        result.Used = await db.CsvDeletionHistory.CountAsync(row => row.UserId == userId && row.QuotaDate == result.QuotaDate, token);
        var earliestDay = result.QuotaDate.AddDays(-2);
        var lastLimitReached = await db.CsvDeletionHistory.Where(row => row.UserId == userId && row.QuotaDate >= earliestDay)
            .GroupBy(row => row.QuotaDate).Where(group => group.Count() >= 3)
            .Select(group => (DateTime?)group.Max(row => row.DeletedAtUtc)).OrderByDescending(date => date).FirstOrDefaultAsync(token);
        if (lastLimitReached.HasValue && lastLimitReached.Value.AddDays(1) > now)
            result.ExportBlockedUntilUtc = DateTime.SpecifyKind(lastLimitReached.Value.AddDays(1), DateTimeKind.Utc);
        return result;
    }

    // Caller holds the UserExports application lock and an open transaction shared with deletion.
    public static async Task ReserveAsync(ColdCallDbContext db, int userId, IReadOnlyCollection<int> exportIds, CancellationToken token)
    {
        if (exportIds.Count == 0) return;
        var quota = await GetAsync(db, userId, token);
        if (quota.Limit == null) return;
        if (exportIds.Count > quota.Remaining!.Value) throw new CsvDailyDeletionLimitException(quota.Remaining.Value);
        var now = DateTime.UtcNow;
        db.CsvDeletionHistory.AddRange(exportIds.Select(id => new CsvDeletionHistory
        {
            UserId = userId, ExportId = id, DeletedAtUtc = now, QuotaDate = quota.QuotaDate
        }));
        await db.SaveChangesAsync(token);
    }
}
