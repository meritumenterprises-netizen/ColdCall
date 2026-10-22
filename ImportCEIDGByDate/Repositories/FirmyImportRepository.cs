using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyImport;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate.Repositories;

public sealed class FirmyImportRepository(FirmyImportDbContext context)
{
    public DbSet<ImportOrigin> ImportOrigins => context.ImportOrigins;
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
}
