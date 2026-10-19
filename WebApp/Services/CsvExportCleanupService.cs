using ColdCall.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ColdCall.Services;

public class CsvExportCleanupService(ColdCallDbContext db, IWebHostEnvironment environment,
    IConfiguration configuration, ILogger<CsvExportCleanupService> logger)
{
    public async Task<int> DeleteOrphanedAsync(CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await LockAsync("OrphanCSVExports", token);
        var orphans = await db.CsvExports.AsNoTracking()
            .Where(e => e.SearchProfileId == null || !db.SearchProfiles.Any(p => p.Id == e.SearchProfileId))
            .Select(e => new { e.EksportCsvId, e.SearchProfileId, e.CsvFileName }).ToListAsync(token);
        if (orphans.Count == 0) return 0;
        var configuredRoot = configuration["CSVExportFolder"];
        if (string.IsNullOrWhiteSpace(configuredRoot)) throw new InvalidOperationException("Brak CSVExportFolder.");
        var root = Path.GetFullPath(configuredRoot, environment.ContentRootPath);
        EnsureSafeDirectory(root);
        var files = new List<string>();
        if (Directory.Exists(root))
        {
            var existingProfiles = await db.SearchProfiles.Select(p => p.Id).ToHashSetAsync(token);
            var namesWithoutProfile = orphans.Where(e => e.SearchProfileId == null)
                .Select(e => e.CsvFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var namesByProfile = orphans.Where(e => e.SearchProfileId.HasValue)
                .ToLookup(e => e.SearchProfileId!.Value, e => e.CsvFileName);
            foreach (var userFolder in Directory.EnumerateDirectories(root))
            {
                if (!int.TryParse(Path.GetFileName(userFolder), out var userId) || userId <= 0) continue;
                EnsureSafeDirectory(userFolder);
                foreach (var profileFolder in Directory.EnumerateDirectories(userFolder))
                {
                    token.ThrowIfCancellationRequested();
                    if (!int.TryParse(Path.GetFileName(profileFolder), out var profileId) || profileId <= 0
                        || existingProfiles.Contains(profileId)) continue;
                    EnsureSafeDirectory(profileFolder);
                    var names = namesByProfile[profileId].ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in Directory.EnumerateFiles(profileFolder))
                    {
                        if (!string.Equals(Path.GetExtension(file), ".csv", StringComparison.OrdinalIgnoreCase)
                            || (!names.Contains(Path.GetFileName(file)) && !namesWithoutProfile.Contains(Path.GetFileName(file)))) continue;
                        if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                            throw new IOException("Plik eksportu nie moze byc dowiazaniem.");
                        files.Add(file);
                    }
                }
            }
        }
        var ids = orphans.Select(e => e.EksportCsvId).ToArray();
        return await StageAndCommitAsync(root, files, transaction, () => db.CsvExports
            .Where(e => ids.Contains(e.EksportCsvId)
                && (e.SearchProfileId == null || !db.SearchProfiles.Any(p => p.Id == e.SearchProfileId)))
            .ExecuteDeleteAsync(token), token);
    }

    public async Task<int> DeleteForUserAsync(int userId, CancellationToken token)
        => await DeleteAsync(userId, false, token);

    public async Task<bool> DeleteSearchAsync(int userId, int profileId, CancellationToken token)
    {
        if (userId <= 0 || profileId <= 0) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await LockAsync("UserExports:" + userId, token);
        await LockAsync("CSVExport:" + profileId, token);
        if (!await db.SearchProfiles.AnyAsync(p => p.Id == profileId && p.AppUserId == userId, token)) return false;
        var configuredRoot = configuration["CSVExportFolder"];
        if (string.IsNullOrWhiteSpace(configuredRoot)) throw new InvalidOperationException("Brak CSVExportFolder.");
        var root = Path.GetFullPath(configuredRoot, environment.ContentRootPath);
        var folder = Path.GetFullPath(Path.Combine(root,
            userId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            profileId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (!folder.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Niepoprawny katalog eksportow.");
        EnsureSafeDirectory(folder);
        var files = new List<string>();
        if (Directory.Exists(folder)) CollectFiles(folder, files);
        await StageAndCommitAsync(root, files, transaction, async () =>
        {
            await db.CsvExports.Where(e => e.SearchProfileId == profileId).ExecuteDeleteAsync(token);
            await db.SearchProfileCriteria.Where(c => c.SearchProfileId == profileId).ExecuteDeleteAsync(token);
            await db.SearchProfilePkds.Where(p => p.SearchProfileId == profileId).ExecuteDeleteAsync(token);
            return await db.SearchProfiles.Where(p => p.Id == profileId && p.AppUserId == userId).ExecuteDeleteAsync(token);
        }, token);
        return true;
    }

    public async Task<int> DeleteForExpiredTrialAsync(int userId, CancellationToken token)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsAdmin, token)) return 0;
        if (!await db.UserPlans.AnyAsync(p => p.AppUserId == userId && p.PlanDefinition.IsTrial
            && p.ExpiresAtUtc <= DateTime.UtcNow, token)) return 0;
        return await DeleteAsync(userId, true, token);
    }

    private async Task<int> DeleteAsync(int userId, bool expiredTrial, CancellationToken token)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        var configuredRoot = configuration["CSVExportFolder"];
        if (string.IsNullOrWhiteSpace(configuredRoot)) throw new InvalidOperationException("Brak CSVExportFolder.");
        var root = Path.GetFullPath(configuredRoot, environment.ContentRootPath);
        var userFolder = Path.GetFullPath(Path.Combine(root, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (!userFolder.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Niepoprawny katalog eksportow.");
        for (var directory = new DirectoryInfo(userFolder); directory != null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Katalog eksportow nie moze byc dowiazaniem.");

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (expiredTrial)
        {
            await LockAsync("TrialPlan:" + userId, token);
            var now = DateTime.UtcNow;
            if (!await db.Users.AnyAsync(u => u.Id == userId && !u.IsAdmin, token)) return 0;
            if (!await db.UserPlans.AnyAsync(p => p.AppUserId == userId && p.PlanDefinition.IsTrial
                && p.ExpiresAtUtc <= now, token)) return 0;
            await db.UserPlans.Where(p => p.AppUserId == userId && p.PlanDefinition.IsTrial
                    && p.ExpiresAtUtc <= now && (p.IsActive == null || p.IsActive == true))
                .ExecuteUpdateAsync(updates => updates.SetProperty(p => p.IsActive, (bool?)false), token);
            if (await db.UserPlans.AnyAsync(p => p.AppUserId == userId && p.ExpiresAtUtc > now, token))
            {
                await transaction.CommitAsync(token);
                return 0;
            }
        }
        var userLock = "UserExports:" + userId;
        await LockAsync(userLock, token);
        var profileIds = await db.SearchProfiles.Where(p => p.AppUserId == userId).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(token);
        foreach (var profileId in profileIds) await LockAsync("CSVExport:" + profileId, token);

        var files = new List<string>();
        if (Directory.Exists(userFolder)) CollectFiles(userFolder, files);
        return await StageAndCommitAsync(root, files, transaction, async () =>
        {
            var deleted = await db.CsvExports.Where(e => e.SearchProfileId.HasValue && profileIds.Contains(e.SearchProfileId.Value))
                .ExecuteDeleteAsync(token);
            if (expiredTrial)
                await db.SearchProfiles.Where(p => p.AppUserId == userId && profileIds.Contains(p.Id)).ExecuteDeleteAsync(token);
            return deleted;
        }, token);
    }

    private async Task<int> StageAndCommitAsync(string root, List<string> files,
        IDbContextTransaction transaction, Func<Task<int>> deleteRecords, CancellationToken token)
    {
        var staging = Path.Combine(root, ".cleanup-" + Guid.NewGuid().ToString("N"));
        var moved = new List<(string Source, string Staged)>();
        var committed = false;
        try
        {
            if (files.Count > 0) Directory.CreateDirectory(staging);
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var staged = Path.Combine(staging, Guid.NewGuid().ToString("N"));
                File.Move(file, staged);
                moved.Add((file, staged));
            }
            var deleted = await deleteRecords();
            await transaction.CommitAsync(token);
            committed = true;
            foreach (var file in moved) File.Delete(file.Staged);
            if (Directory.Exists(staging)) Directory.Delete(staging, false);
            return deleted;
        }
        catch
        {
            if (!committed)
            {
                foreach (var file in moved.AsEnumerable().Reverse())
                    if (File.Exists(file.Staged)) File.Move(file.Staged, file.Source);
                if (Directory.Exists(staging)) Directory.Delete(staging, false);
            }
            else logger.LogError("CSV metadata removed, but staging cleanup failed in {Staging}", staging);
            throw;
        }
    }

    private static void EnsureSafeDirectory(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Katalog eksportow nie moze byc dowiazaniem.");
    }

    private async Task LockAsync(string resource, CancellationToken token) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result < 0 THROW 50001, 'CSV cleanup lock unavailable', 1;", token);

    private static void CollectFiles(string directory, List<string> files)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Dowiazania w katalogu eksportow sa niedozwolone.");
            if (entry is DirectoryInfo child) CollectFiles(child.FullName, files);
            else if (string.Equals(entry.Extension, ".csv", StringComparison.OrdinalIgnoreCase)) files.Add(entry.FullName);
        }
    }
}
