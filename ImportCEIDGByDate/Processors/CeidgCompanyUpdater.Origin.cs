using ImportCEIDGByDate.Data.FirmyImport;
using ImportCEIDGByDate.Data.FirmyTemp;
using Microsoft.EntityFrameworkCore;
using System.Xml;

namespace ImportCEIDGByDate;

public partial class CeidgCompanyUpdater
{
    public async Task UpdateExistingCompaniesFromImportOrigin(CeidgCompanyAddressUpdater? addressUpdater = null)
    {
        if (GlobalSettings.SkipUpdateExistingCompaniesFromOrigin) return;

        Statistics.StartNewPhase(ImportPhasesEnum.UpdateExistingCompaniesFromOrigin, "Upserting companies from ImportOrigin");
        var origins = FirmyImportRepository.ImportOrigins.AsNoTracking()
            .Where(origin => origin.Nip != null && origin.Nip.Trim() != "");
        var eligibleIds = await FindOriginIdsNotOlderThanCompaniesAsync(origins);
        origins = origins.Where(origin => eligibleIds.Contains(origin.Id));
        var total = await origins.CountAsync();
        Statistics.CurrentPhase.EntriesCount = total;
        var progress = new AddressImportProgress("Adresy firm z ImportOrigin", total);
        using var ownedAddressUpdater = addressUpdater == null
            ? new CeidgCompanyAddressUpdater(FirmyTempDbContext, Statistics) : null;
        addressUpdater ??= ownedAddressUpdater!;

        var lastOriginId = 0;
        var added = 0;
        var updated = 0;
        while (true)
        {
            // Read the whole batch before querying or saving companies.
            var batch = await origins.Where(origin => origin.Id > lastOriginId)
                .OrderBy(origin => origin.Id).Take(100).ToListAsync();
            if (batch.Count == 0) break;
            lastOriginId = batch[^1].Id;

            await CeidgConverter.Lookups.PrepareBatchAsync(batch.Select(origin => origin.CompanyXml));
            var nips = batch.Select(origin => origin.Nip!).Distinct().ToArray();
            var companiesByNip = await FirmyTempDbContext.Companies
                .Include(company => company.CompanyPkds)
                .Where(company => nips.Contains(company.Nip!))
                .ToDictionaryAsync(company => company.Nip!, StringComparer.OrdinalIgnoreCase);
            var companiesForAddressUpdate = new List<Company>();

            foreach (var origin in batch)
            {
                companiesByNip.TryGetValue(origin.Nip!, out var existingCompany);
                var company = await ConvertOriginCompanyAsync(origin, existingCompany);
                if (existingCompany == null)
                {
                    FirmyTempDbContext.Companies.Add(company);
                    companiesByNip.Add(origin.Nip!, company);
                    added++;
                }
                else
                {
                    updated++;
                }

                if (!companiesForAddressUpdate.Contains(company))
                    companiesForAddressUpdate.Add(company);
            }

            // Companies and PKDs must be saved before their address links.
            await FirmyTempRepository.SaveChangesAsync();
            var addressResults = await addressUpdater.UpdateBatchAsync(companiesForAddressUpdate, progress: progress);
            await SaveOriginXmlSnapshotsAsync(batch);

            foreach (var origin in batch)
            {
                var company = companiesByNip[origin.Nip!];
                var addressIndex = companiesForAddressUpdate.IndexOf(company);
                progress.Complete(addressResults[addressIndex]);
            }
            FirmyTempDbContext.ChangeTracker.Clear();
        }

        Console.WriteLine($"ImportOrigin: added={added}; updated={updated}.");
        Statistics.FinishCurrentPhase("Finished", added + updated);
    }

    private async Task<List<int>> FindOriginIdsNotOlderThanCompaniesAsync(IQueryable<ImportOrigin> origins)
    {
        // Only comparison fields cross database boundaries; XML stays in the paged query.
        var originDates = await origins.Select(origin => new { origin.Id, origin.Nip, origin.ImportDate }).ToListAsync();
        var eligibleIds = new List<int>();
        foreach (var batch in originDates.Chunk(100))
        {
            var nips = batch.Select(origin => origin.Nip!).Distinct().ToArray();
            var importedDates = await FirmyTempDbContext.Companies.AsNoTracking()
                .Where(company => nips.Contains(company.Nip!) && company.ImportedOn != null
                    && FirmyTempDbContext.CompanyXml.Any(snapshot => snapshot.Nip == company.Nip))
                .Select(company => new { company.Nip, company.ImportedOn })
                .ToDictionaryAsync(company => company.Nip!, company => company.ImportedOn!.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var origin in batch)
                if (importedDates.TryGetValue(origin.Nip!, out var importedOn) && importedOn <= origin.ImportDate)
                    eligibleIds.Add(origin.Id);
        }
        return eligibleIds;
    }

    private async Task<Company> ConvertOriginCompanyAsync(ImportOrigin origin, Company? existingCompany)
    {
        var xml = new XmlDocument();
        xml.LoadXml(origin.CompanyXml);
        var xmlNip = xml.GetInnerTextOrEmpty("Nip");
        if (!string.Equals(xmlNip, origin.Nip, StringComparison.Ordinal))
            throw new InvalidDataException($"Origin NIP={origin.Nip} does not match XML NIP={xmlNip}.");

        if (existingCompany == null)
            return await CeidgConverter.ConvertXmlToNewCompany(origin.WojewodztwoId, origin.CompanyXml);

        return await CeidgConverter.ConvertXmlToExistingCompany(origin.WojewodztwoId, origin.CompanyXml, existingCompany)
            ?? throw new InvalidDataException($"Cannot update company with NIP={origin.Nip}.");
    }

    private async Task SaveOriginXmlSnapshotsAsync(IReadOnlyList<ImportOrigin> origins)
    {
        var nips = origins.Select(origin => origin.Nip!).Distinct().ToArray();
        var snapshotsByNip = await FirmyTempDbContext.CompanyXml
            .Where(snapshot => nips.Contains(snapshot.Nip!))
            .ToDictionaryAsync(snapshot => snapshot.Nip!, StringComparer.OrdinalIgnoreCase);

        foreach (var origin in origins)
        {
            if (!snapshotsByNip.TryGetValue(origin.Nip!, out var snapshot))
            {
                snapshot = new CompanyXml { Nip = origin.Nip };
                FirmyTempDbContext.CompanyXml.Add(snapshot);
                snapshotsByNip.Add(origin.Nip!, snapshot);
            }
            snapshot.XmlContent = origin.CompanyXml;
            snapshot.Md5 = origin.Md5;
            snapshot.DateModified = DateTime.Now;
        }
        await FirmyTempRepository.SaveChangesAsync();
    }
}
