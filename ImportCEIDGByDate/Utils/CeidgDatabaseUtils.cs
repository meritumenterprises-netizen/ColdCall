using ImportCEIDGByDate.Data;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate;

public sealed class CeidgDatabaseUtils(FirmyImportDbContext context)
{
    public async Task TruncateImportTablesAsync(CancellationToken token = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await TruncateImportOriginAsync(token);
        await TruncateImportTargetAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task TruncateImportOriginAsync(CancellationToken token = default)
    {
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE [dbo].[ImportOrigin];", token);
        foreach (var entry in context.ChangeTracker.Entries<Data.FirmyImport.ImportOrigin>().ToList())
            entry.State = EntityState.Detached;
    }

    public async Task TruncateImportTargetAsync(CancellationToken token = default)
    {
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE [dbo].[ImportTarget];", token);
        foreach (var entry in context.ChangeTracker.Entries<Data.FirmyImport.ImportTarget>().ToList())
            entry.State = EntityState.Detached;
    }
}
