using ImportCEIDGByDate.Data;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate;

public sealed class CeidgDatabaseUtils(FirmyImportDbContext context)
{

    public async Task TruncateImportTargetAsync(CancellationToken token = default)
    {
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE [dbo].[ImportTarget];", token);
        foreach (var entry in context.ChangeTracker.Entries<Data.FirmyImport.ImportTarget>().ToList())
            entry.State = EntityState.Detached;
    }
}
