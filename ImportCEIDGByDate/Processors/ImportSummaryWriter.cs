using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using ImportCEIDGByDate;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate.Processors;

public class ImportSummaryWriter(FirmyTempDbContext context)
{
    public async Task<bool> SaveAsync(ImportByDate import, CancellationToken token = default)
    {
        import.Version = ImporterVersion.Current;
        var updated = await context.ImportByDates.Where(row => row.ImportId == import.ImportId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.FinishedAt, import.FinishedAt)
                .SetProperty(row => row.ImportTargetRecords, import.ImportTargetRecords)
                .SetProperty(row => row.Summary, import.Summary)
                .SetProperty(row => row.Duration, import.Duration)
                .SetProperty(row => row.Version, import.Version), token);
        return updated == 1;
    }
}
