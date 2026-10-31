using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyImport;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate.Repositories;

public sealed partial class FirmyImportRepository(FirmyImportDbContext context)
{
    public DbSet<ImportTarget> ImportTargets => context.ImportTargets;


    public List<ImportTarget> GetNewImportCompanies()
    {
        return ImportTargets.Where(company => company.IsNew && !company.Imported).ToList();
    }

    public List<ImportTarget> GetUpdateImportCompanies()
    {
        return ImportTargets.Where(company => company.IsUpdated && !company.Imported).ToList();
    }

    public Task<int> SaveChangesAsync(CancellationToken token = default) =>
        context.SaveChangesAsync(token);

    public async Task SaveAndClearAsync()
    {
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    public Task<int> CountPendingAsync(bool isNew) => ImportTargets.AsNoTracking()
        .CountAsync(x => !x.Imported && (isNew ? x.IsNew : x.IsUpdated));

    public Task<List<ImportTarget>> ReadPendingBatchAsync(bool isNew, int afterId, int count = 100) => ImportTargets.AsNoTracking()
        .Where(x => x.Id > afterId && !x.Imported && (isNew ? x.IsNew : x.IsUpdated))
        .OrderBy(x => x.Id).Take(count).ToListAsync();

    public Task<int> MarkImportedAsync(int[] ids)
    {
        var now = DateTime.Now;
        return ImportTargets.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Imported, true).SetProperty(x => x.ImportDate, now));
    }
}
