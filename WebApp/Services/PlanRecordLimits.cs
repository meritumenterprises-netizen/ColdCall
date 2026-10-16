using ColdCall.Data;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public static class PlanRecordLimits
{
    // Preserve the existing storage/API ceiling and legacy NULL-limit behavior.
    public const int SystemMaximum = 10000;

    public static int Resolve(IEnumerable<int?> limits, bool isAdmin) =>
        limits.Select(limit => Math.Clamp(limit ?? SystemMaximum, 0, SystemMaximum))
            .DefaultIfEmpty(isAdmin ? SystemMaximum : 0).Max();

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
