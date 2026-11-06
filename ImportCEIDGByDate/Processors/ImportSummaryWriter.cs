using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate.Processors;

public class ImportSummaryWriter(FirmyTempDbContext context)
{
    public async Task<bool> SaveAsync(ImportByDate import, CancellationToken token = default)
    {
        var updated = await context.ImportByDates.Where(row => row.ImportId == import.ImportId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.FinishedAt, import.FinishedAt)
                .SetProperty(row => row.ImportTargetRecords, import.ImportTargetRecords)
                .SetProperty(row => row.Summary, import.Summary)
                .SetProperty(row => row.Duration, import.Duration), token);
        return updated == 1;
    }
}
