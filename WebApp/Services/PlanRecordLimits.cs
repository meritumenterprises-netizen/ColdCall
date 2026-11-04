using ColdCall.Data;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public static class PlanRecordLimits
{
    // Keep the legacy fallback for NULL limits and admins without a plan.
    public const int DefaultLimit = 10000;

    public static int Resolve(IEnumerable<int?> limits, bool isAdmin) =>
        limits.Select(limit => Math.Max(limit ?? DefaultLimit, 0))
            .DefaultIfEmpty(isAdmin ? DefaultLimit : 0).Max();

    public static async Task<int> ForUserAsync(ColdCallDbContext db, int userId, bool isAdmin)
    {
        var now = DateTime.UtcNow;
        var limits = await db.UserPlans.AsNoTracking()
            .Where(plan => plan.AppUserId == userId && plan.ExpiresAtUtc > now)
            .Select(plan => plan.PlanDefinition.MaxNumberOfExportedRecords)
            .ToListAsync();
        return Resolve(limits, isAdmin);
    }
}
