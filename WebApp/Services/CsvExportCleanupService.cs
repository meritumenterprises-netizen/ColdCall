using ColdCall.Data;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public class CsvExportCleanupService(ColdCallDbContext db, IWebHostEnvironment environment,
    IConfiguration configuration, ILogger<CsvExportCleanupService> logger)
{
    public async Task<int> DeleteForUserAsync(int userId, CancellationToken token)
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
        var userLock = "UserExports:" + userId;
        await LockAsync(userLock, token);
        var profileIds = await db.SearchProfiles.Where(p => p.AppUserId == userId).OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(token);
        foreach (var profileId in profileIds) await LockAsync("CSVExport:" + profileId, token);

        var files = new List<string>();
        if (Directory.Exists(userFolder)) CollectFiles(userFolder, files);
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
            var deleted = await db.CsvExports.Where(e => e.SearchProfileId.HasValue && profileIds.Contains(e.SearchProfileId.Value))
                .ExecuteDeleteAsync(token);
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
            else logger.LogError("CSV metadata removed, but staging cleanup failed for user {UserId} in {Staging}", userId, staging);
            throw;
        }
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
