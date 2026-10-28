using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using ImportCEIDGByDate.Repositories;
using GeoPortalAddressImporter;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using ImportCEIDGByDate.Utils;

namespace ImportCEIDGByDate
{
    public class CeidgCompanyUpdater
    {
        public FirmyTempRepository FirmyTempRepository { get; private set; }
        public FirmyImportRepository FirmyImportRepository { get; private set; }

        public FirmyImportDbContext FirmyImportDbContext { get; private set; }
        public FirmyTempDbContext FirmyTempDbContext { get; private set; }

        private Statistics Statistics { get; set; }

        private CeidgCompanyXmlConverter CeidgConverter { get; set; }
        private readonly CeidgDeferredAddressImport deferredAddresses;
        public bool DeferAddressAssignment { get; init; }

        public GlobalSettings GlobalSettings { get; set; }

        public async Task ImportDeferredAddressesAsync(CeidgCompanyAddressUpdater? updater = null)
        {
            if (GlobalSettings.SkipImportDeferredAddressesAsync == false)
            {
                Statistics.StartNewPhase(ImportPhasesEnum.ImportDeferredAddressesAsync, "");

                if (updater != null)
                {
                    await deferredAddresses.RunAsync(Statistics, updater);
                    return;
                }
                using var ownedUpdater = new CeidgCompanyAddressUpdater(FirmyTempDbContext, this.Statistics);
                await deferredAddresses.RunAsync(Statistics, ownedUpdater);
                Statistics.FinishCurrentPhase("");
            }
        }

        public CeidgCompanyUpdater(
            Statistics statistics,
            FirmyTempRepository firmyTempRepository,
            FirmyImportRepository firmyImportRepository,
            FirmyImportDbContext firmyImportDbContext,
            FirmyTempDbContext firmyTempDbContext,
            GlobalSettings settings)
        {
            this.Statistics = statistics;
            this.FirmyTempRepository = firmyTempRepository;
            this.FirmyImportRepository = firmyImportRepository;

            this.FirmyImportDbContext = firmyImportDbContext;
            this.FirmyTempDbContext = firmyTempDbContext;
            deferredAddresses = new CeidgDeferredAddressImport(firmyTempDbContext, firmyImportRepository);

            this.CeidgConverter = new CeidgCompanyXmlConverter(statistics, firmyTempRepository, firmyImportRepository, firmyImportDbContext, firmyTempDbContext);
            this.GlobalSettings = settings;
        }

        public async Task UpdateExistingCompaniesFromOrigin()
        {
            if (GlobalSettings.SkipUpdateExistingCompaniesFromOrigin == false)
            {
                Statistics.StartNewPhase(ImportPhasesEnum.UpdateExistingCompaniesFromOrigin, "");
                var origins = FirmyImportRepository.ImportOrigins.AsNoTracking()
                    .Where(row => row.Nip != null && row.Nip.Trim() != "")
                    .Select(row => row.Nip!).Distinct();
                Statistics.CurrentPhase.EntriesCount = origins.Count();
                var nips = await origins.ToListAsync();
                var companyTotal = 0;
                foreach (var nipBatch in nips.Chunk(100))
                    companyTotal += await FirmyTempDbContext.Companies.CountAsync(company => nipBatch.Contains(company.Nip!));
                var progress = new AddressImportProgress("Adresy firm z ImportOrigin", companyTotal);
                Statistics.CurrentPhase.EntriesCount = companyTotal;
                using var updater = new CeidgCompanyAddressUpdater(FirmyTempDbContext, Statistics);
                // Buffer Origin NIPs before querying the other database; never hold a SQL reader open.
                foreach (var nipBatch in nips.Chunk(100))
                {
                    var companies = await FirmyTempDbContext.Companies
                        .Where(company => nipBatch.Contains(company.Nip!))
                        .OrderBy(company => company.Id).ToListAsync();
                    var results = await updater.UpdateBatchAsync(companies, progress: progress);
                    foreach (var result in results) progress.Complete(result);
                    FirmyTempDbContext.ChangeTracker.Clear();
                }
                Statistics.FinishCurrentPhase("Finished", progress.Processed);
            }
        }

        public async Task AddNewCompaniesFromImportToTemp()
        {
            if (GlobalSettings.SkipAddNewCompaniesFromImportToTemp == false)
            {
                Statistics.StartNewPhase(ImportPhasesEnum.ImportNewCompanies, "Importing new companies");
                var companyTotal = await FirmyImportRepository.CountPendingAsync(true);
                Statistics.CurrentPhase.EntriesCount = FirmyImportDbContext.ImportTargets.Where((t) => t.IsNew).Count();
                var companyCounter = 0;
                var addressProgress = new AddressImportProgress("Adresy nowych firm", companyTotal);
                var pendingAddresses = new List<PendingCompanyAddressImport>();
                using var addressUpdater = new CeidgCompanyAddressUpdater(FirmyTempDbContext, this.Statistics);
                var afterId = 0;
                while (true)
                {
                    var newCompanies = await FirmyImportRepository.ReadPendingBatchAsync(true, afterId);
                    if (newCompanies.Count == 0) break;
                    afterId = newCompanies[^1].Id;
                    await CeidgConverter.Lookups.PrepareBatchAsync(newCompanies.Select(item => item.CompanyXml));
                    foreach (var company in newCompanies)
                    {
                        XmlDocument xmlDoc = new XmlDocument();
                        xmlDoc.LoadXml(company.CompanyXml);
                        var nip = xmlDoc.GetInnerTextOrEmpty("Nip");
                        var alreadyExisting = FirmyTempDbContext.Companies.Local.FirstOrDefault(comp => comp.Nip == nip)
                            ?? await FirmyTempDbContext.Companies.FirstOrDefaultAsync(comp => comp.Nip == nip);
                        if (alreadyExisting != null)
                        {
                            Console.WriteLine($"Company with NIP={nip} already exists, skipping");
                            if (DeferAddressAssignment)
                                pendingAddresses.Add(new PendingCompanyAddressImport { Company = alreadyExisting, Target = company });
                            addressProgress.Complete(false);
                            continue;
                        }
                        var newCompany = await CeidgConverter.ConvertXmlToNewCompany(company.WojewodztwoId, company.CompanyXml);
                        if (companyCounter % 100 == 0)
                        {
                            Console.WriteLine($"Adding a new company {companyCounter + 1}/{companyTotal}");
                        }
                        FirmyTempDbContext.Companies.Add(newCompany);
                        pendingAddresses.Add(new PendingCompanyAddressImport { Company = newCompany, Target = company });
                        if (pendingAddresses.Count == 100)
                            await FlushAddressesAsync(pendingAddresses, addressUpdater, addressProgress);
                        companyCounter++;
                    }
                    await FlushAddressesAsync(pendingAddresses, addressUpdater, addressProgress);
                    FirmyTempDbContext.ChangeTracker.Clear();
                }
                await FlushAddressesAsync(pendingAddresses, addressUpdater, addressProgress);
                Console.WriteLine($"Added new companies: {companyCounter}");
                Statistics.CurrentPhase.EntriesCount = companyTotal;
                Statistics.FinishCurrentPhase("Finished");
            }
        }

        public async Task UpdateExistingCompaniesFromImportToTemp()
        {
            if (GlobalSettings.SkipUpdateExistingCompaniesFromImportToTemp == false)
            {
                Statistics.StartNewPhase(ImportPhasesEnum.UpdateExistingCompaniesFromTarget, "Updating existing companies");
                var companyTotal = await FirmyImportRepository.CountPendingAsync(false);
                Statistics.CurrentPhase.EntriesCount = FirmyImportDbContext.ImportTargets.Where((t) => t.IsUpdated).Count();
                var companyCounter = 0;
                var addressProgress = new AddressImportProgress("Adresy aktualizowanych firm", companyTotal);
                var pendingAddresses = new List<PendingCompanyAddressImport>();
                using var addressUpdater = new CeidgCompanyAddressUpdater(FirmyTempDbContext, this.Statistics);
                var afterId = 0;
                while (true)
                {
                    var existingCompanies = await FirmyImportRepository.ReadPendingBatchAsync(false, afterId);
                    if (existingCompanies.Count == 0) break;
                    afterId = existingCompanies[^1].Id;
                    await CeidgConverter.Lookups.PrepareBatchAsync(existingCompanies.Select(item => item.CompanyXml));
                    foreach (var company in existingCompanies)
                    {
                        var dbCompany = string.IsNullOrWhiteSpace(company.Nip)
                            ? null : await FirmyTempRepository.FindByNipAsync(company.Nip);
                        if (dbCompany == null)
                            throw new InvalidDataException($"Company with NIP={company.Nip} was not found.");

                        var existingCompany = await CeidgConverter.ConvertXmlToExistingCompany(company.WojewodztwoId, company.CompanyXml, dbCompany);
                        if (existingCompany == null)
                            throw new InvalidDataException($"Company with NIP={company.Nip} was not found.");

                        await FirmyTempRepository.SaveChangesAsync(); // update existingCompany

                        if (companyCounter % 100 == 0)
                        {
                            Console.WriteLine($"Updating existing company {companyCounter + 1}/{companyTotal}");
                        }
                        pendingAddresses.Add(new PendingCompanyAddressImport { Company = existingCompany, Target = company });
                        if (pendingAddresses.Count == 100)
                            await FlushAddressesAsync(pendingAddresses, addressUpdater, addressProgress);
                        companyCounter++;
                    }
                    await FlushAddressesAsync(pendingAddresses, addressUpdater, addressProgress);
                    FirmyTempDbContext.ChangeTracker.Clear();
                }
                await FlushAddressesAsync(pendingAddresses, addressUpdater, addressProgress);
                Console.WriteLine($"Updated existing companies: {companyTotal}");
                Statistics.CurrentPhase.EntriesCount = companyTotal;
                Statistics.FinishCurrentPhase("Finished");
            }
        }

        private async Task FlushAddressesAsync(List<PendingCompanyAddressImport> pending,
            CeidgCompanyAddressUpdater updater, AddressImportProgress progress)
        {
            if (pending.Count == 0) return;
            await FirmyTempDbContext.SaveChangesAsync();
            if (DeferAddressAssignment)
            {
                foreach (var item in pending) deferredAddresses.Enqueue(item.Company.Id, item.Target.Id);
                pending.Clear();
                return;
            }
            var saved = await updater.UpdateBatchAsync(pending.Select(item => item.Company).ToArray(), progress: progress);
            await FirmyImportRepository.MarkImportedAsync(pending.Select(item => item.Target.Id).ToArray());
            foreach (var result in saved) progress.Complete(result);
            pending.Clear();
        }
    }
}
