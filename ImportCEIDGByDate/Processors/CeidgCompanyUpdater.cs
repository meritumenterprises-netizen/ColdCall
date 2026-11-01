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
    public partial class CeidgCompanyUpdater
    {
        public FirmyTempRepository FirmyTempRepository { get; private set; }
        public FirmyImportRepository FirmyImportRepository { get; private set; }

        public FirmyImportDbContext FirmyImportDbContext { get; private set; }
        public FirmyTempDbContext FirmyTempDbContext { get; private set; }

        private Statistics Statistics { get; set; }

        private CeidgCompanyXmlConverter CeidgConverter { get; set; }

        public GlobalSettings GlobalSettings { get; set; }

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

            this.CeidgConverter = new CeidgCompanyXmlConverter(statistics, firmyTempRepository, firmyImportRepository, firmyImportDbContext, firmyTempDbContext);
            this.GlobalSettings = settings;
        }


        public async Task AddNewCompaniesFromImportTargetToTemp()
        {
            if (GlobalSettings.SkipAddNewCompaniesFromImportToTemp == false)
            {
                Statistics.StartNewPhase(ImportPhasesEnum.ImportNewCompaniesToDatabase, "Importing new companies");
                var companyTotal = await FirmyImportRepository.CountPendingAsync(true);
                Statistics.CurrentPhase.EntriesCount = FirmyImportDbContext.ImportTargets.Where((t) => t.IsNew).Count();
                var companyCounter = 0;
                var addressProgress = new AddressImportProgress("Adresy nowych firm", companyTotal);
                var pendingAddresses = new List<PendingCompanyAddressImport>();
                using var addressUpdater = new CeidgCompanyAddressUpdater(FirmyTempDbContext, this.Statistics, GlobalSettings);
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
                            await FirmyTempDbContext.SaveChangesAsync();
                            await FirmyImportRepository.MarkImportedAsync(new[] { company.Id });
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
                Statistics.FinishCurrentPhase("Finished", companyCounter);
            }
        }

        public async Task UpdateExistingCompaniesFromImportTargetToTemp()
        {
            if (GlobalSettings.SkipUpdateExistingCompaniesFromImportToTemp == false)
            {
                Statistics.StartNewPhase(ImportPhasesEnum.UpdateExistingCompaniesFromTargetInDatabase, "Updating existing companies");
                var companyTotal = await FirmyImportRepository.CountPendingAsync(false);
                Statistics.CurrentPhase.EntriesCount = FirmyImportDbContext.ImportTargets.Where((t) => t.IsUpdated).Count();
                var companyCounter = 0;
                var addressProgress = new AddressImportProgress("Adresy aktualizowanych firm", companyTotal);
                var pendingAddresses = new List<PendingCompanyAddressImport>();
                using var addressUpdater = new CeidgCompanyAddressUpdater(FirmyTempDbContext, this.Statistics, GlobalSettings);
                var afterId = 0;
                while (true)
                {
                    var existingCompanies = await FirmyImportRepository.ReadPendingBatchAsync(false, afterId);
                    if (existingCompanies.Count == 0) break;
                    afterId = existingCompanies[^1].Id;
                    foreach (var company in existingCompanies)
                    {
                        var dbCompany = string.IsNullOrWhiteSpace(company.Nip)
                            ? null : await FirmyTempRepository.FindByNipAsync(company.Nip);
                        if (dbCompany == null)
                            throw new InvalidDataException($"Company with NIP={company.Nip} was not found.");

                        //if (dbCompany.ImportedOn.HasValue && dbCompany.ImportedOn.Value > company.ImportDate)
                        //{
                        //    Console.WriteLine($"Skipping company with NIP={company.Nip}: ImportedOn is later than ImportTarget.ImportDate.");
                        //    addressProgress.Complete(false);
                        //    continue;
                        //}

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
                Console.WriteLine($"Updated existing companies: {companyCounter}");
                Statistics.FinishCurrentPhase("Finished", companyCounter);
            }
        }

        private async Task FlushAddressesAsync(List<PendingCompanyAddressImport> pending,
            CeidgCompanyAddressUpdater updater, AddressImportProgress progress)
        {
            if (pending.Count == 0) return;
            await FirmyTempDbContext.SaveChangesAsync();
            if (GlobalSettings.SkipProcessingAddresses)
            {
                await FirmyImportRepository.MarkImportedAsync(pending.Select(item => item.Target.Id).ToArray());
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
