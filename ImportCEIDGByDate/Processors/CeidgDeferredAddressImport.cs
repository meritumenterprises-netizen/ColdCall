using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ImportCEIDGByDate;

public sealed class CeidgDeferredAddressImport(FirmyTempDbContext context, FirmyImportRepository repository)
{
    private readonly Dictionary<int, HashSet<int>> pending = new();

    public void Enqueue(int companyId, int? importTargetId = null)
    {
        if (!pending.TryGetValue(companyId, out var targets))
            pending.Add(companyId, targets = new HashSet<int>());
        if (importTargetId.HasValue) targets.Add(importTargetId.Value);
    }

    public async Task RunAsync(Statistics statistics, CeidgCompanyAddressUpdater updater)
    {
        statistics.StartNewPhase(ImportPhasesEnum.UpdateAdresyForChangedCompaniesFromTarget, "Importing addresses after company import");
        statistics.CurrentPhase.EntriesCount = pending.Count;
        var progress = new AddressImportProgress("Adresy zaimportowanych firm", pending.Count);
        foreach (var ids in pending.Keys.OrderBy(id => id).ToArray().Chunk(100))
        {
            var companies = await context.Companies.Where(company => ids.Contains(company.Id))
                .OrderBy(company => company.Id).ToListAsync();
            if (companies.Count != ids.Length)
                throw new InvalidDataException("A queued company was removed before address import.");
            foreach (var company in companies.Where(company => company.WojewodztwoId == 1))
            {
                Console.WriteLine($"GeoPortal: firma {company.Id}: WojewodztwoId=1; pominieto adres.");
                progress.Complete(false);
            }
            var companiesWithProvince = companies.Where(company => company.WojewodztwoId != 1).ToArray();
            var results = await updater.UpdateBatchAsync(companiesWithProvince, progress: progress);
            // Confirm staging rows only after the address batch commits (or is explicitly skipped).
            var targets = ids.SelectMany(id => pending[id]).Distinct().ToArray();
            foreach (var targetBatch in targets.Chunk(1000))
                await repository.MarkImportedAsync(targetBatch);
            foreach (var result in results) progress.Complete(result);
            foreach (var id in ids) pending.Remove(id);
            context.ChangeTracker.Clear();
        }
        statistics.FinishCurrentPhase("Finished", progress.Total);
    }
}
