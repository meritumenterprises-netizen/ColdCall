using System.Data;
using ImportCEIDGByDate.Data.FirmyImport;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate.Repositories;

public sealed partial class FirmyImportRepository
{
    public DbSet<ImportReport> ImportReports => context.ImportReports;

    public async Task PrepareReportHistoryAsync(DateTime importDate, CancellationToken token = default)
    {
        var from = importDate.Date;
        var to = from.AddDays(1);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (!await ImportReports.AnyAsync(report => report.ImportDate >= from && report.ImportDate < to, token))
        {
            await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE [dbo].[ImportReports];", token);
            foreach (var entry in context.ChangeTracker.Entries<ImportReport>().ToList())
                entry.State = EntityState.Detached;
        }
        await transaction.CommitAsync(token);
    }

    public Task<bool> WasReportDownloadedAsync(DateTime importDate, string url, CancellationToken token = default)
    {
        var from = importDate.Date;
        var to = from.AddDays(1);
        return ImportReports.AnyAsync(report => report.ImportDate >= from && report.ImportDate < to
            && EF.Functions.Collate(report.Url, "Latin1_General_100_BIN2") == url, token);
    }

    public async Task RecordDownloadedReportAsync(DateTime importDate, string url, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (!await WasReportDownloadedAsync(importDate, url, token))
        {
            ImportReports.Add(new ImportReport { ImportDate = importDate.Date, Url = url });
            await context.SaveChangesAsync(token);
        }
        await transaction.CommitAsync(token);
    }
}
