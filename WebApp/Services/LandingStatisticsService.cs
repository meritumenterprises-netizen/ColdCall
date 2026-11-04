using ColdCall.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public sealed record LandingStatistics(long Count, long GeoAddressCount, long CompaniesWithPhoneCount,
    long CompaniesWithEmailCount, long RecentlyImportedCount, string? LatestImportDate);

// Requests read the last complete snapshot; only this worker queries the database.
public sealed class LandingStatisticsService(IServiceScopeFactory scopes, ILogger<LandingStatisticsService> logger, IHostEnvironment environment)
    : BackgroundService
{
    private readonly string snapshotPath = Path.Combine(environment.ContentRootPath, "App_Data", "landing-statistics.json");
    private LandingStatistics? snapshot = ReadSnapshot(environment, logger);
    public LandingStatistics? Current => Volatile.Read(ref snapshot);

    private static LandingStatistics? ReadSnapshot(IHostEnvironment environment, ILogger logger)
    {
        try
        {
            var path = Path.Combine(environment.ContentRootPath, "App_Data", "landing-statistics.json");
            if (!File.Exists(path)) { return null; }
            var cached = JsonSerializer.Deserialize<LandingStatistics>(File.ReadAllText(path));
            return cached is not null && cached.Count >= 0 && cached.GeoAddressCount >= 0
                && cached.CompaniesWithPhoneCount >= 0 && cached.CompaniesWithEmailCount >= 0
                && cached.RecentlyImportedCount >= 0 ? cached : null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read the saved landing statistics.");
            return null;
        }
    }

    private async Task SaveSnapshotAsync(LandingStatistics current, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            var temporaryPath = snapshotPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(current), cancellationToken);
            File.Move(temporaryPath, snapshotPath, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not save landing statistics for the next application restart.");
        }
    }
    private readonly TaskCompletionSource<LandingStatistics> firstSnapshot = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<LandingStatistics> GetAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref snapshot);
        return current is null ? firstSnapshot.Task.WaitAsync(cancellationToken) : Task.FromResult(current);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ColdCallDbContext>();
                // Large imports can make the aggregate slower than a normal web request.
                db.Database.SetCommandTimeout(120);
                var cancellationToken = stoppingToken;
                var now = DateTime.Now;
                var since = now.AddDays(-7);
                var totals = await db.Companies.GroupBy(company => 1)
                    .Select(group => new
                    {
                        Count = group.LongCount(),
                        WithPhone = group.LongCount(company => company.Phone != null && company.Phone.Trim() != ""),
                        WithEmail = group.LongCount(company => company.Email != null && company.Email.Trim() != ""),
                        RecentlyImported = group.LongCount(company => company.ImportedOn >= since && company.ImportedOn <= now)
                    }).SingleOrDefaultAsync(cancellationToken);
                var count = totals?.Count ?? 0;
                var companiesWithPhoneCount = totals?.WithPhone ?? 0;
                var companiesWithEmailCount = totals?.WithEmail ?? 0;
                var recentlyImportedCount = totals?.RecentlyImported ?? 0;
                var geoAddressCount = await db.Adresy.LongCountAsync(cancellationToken);
                var latestImport = await db.ImportByDates.Where(import => import.ImportTargetRecords > 0)
                    .MaxAsync(import => (DateTime?)import.ImportDate, cancellationToken);
                var latestImportDate = latestImport?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var current = new LandingStatistics(count, geoAddressCount, companiesWithPhoneCount,
                    companiesWithEmailCount, recentlyImportedCount, latestImportDate);
                Volatile.Write(ref snapshot, current);
                firstSnapshot.TrySetResult(current);
                await SaveSnapshotAsync(current, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not refresh landing page statistics; retaining the last snapshot.");
            }

            try
            {
                await Task.Delay(Volatile.Read(ref snapshot) is null ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
        firstSnapshot.TrySetCanceled(stoppingToken);
    }
}
