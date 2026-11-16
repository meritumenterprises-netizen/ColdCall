using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using ImportCEIDGByDate.Repositories;
using ImportCEIDGByDate.Geoportal;
using Company = ImportCEIDGByDate.Data.FirmyTemp.Company;
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

        public async Task AddNewCompaniesFromAdjustName()
        {
            Console.WriteLine("Importing new companies from AdjustName");
            Statistics.StartNewPhase(ImportPhasesEnum.ImportNewCompaniesToDatabaseFromAdjustName,
                "Importing new companies from AdjustName");
            var pending = FirmyTempDbContext.AdjustNames.Where(row => !row.Processed
                && !FirmyTempDbContext.Companies.Any(company => company.Nip == row.Nip));
            var total = await pending.CountAsync();
            Statistics.CurrentPhase.EntriesCount = total;
            var added = 0;
            var processed = 0;
            var batchNumber = 0;
            Console.WriteLine($"AdjustName: do przetworzenia {total} rekordow; rozmiar batcha: 100.");
            var afterId = 0;
            var addressProgress = new AddressImportProgress("Adresy nowych firm z AdjustName", total);
            using var addressUpdater = new CeidgCompanyAddressUpdater(FirmyTempDbContext, Statistics, GlobalSettings);
            while (true)
            {
                var batch = await pending.Where(row => row.AdjustId > afterId)
                    .OrderBy(row => row.AdjustId).Take(100).ToListAsync();
                if (batch.Count == 0)
                    break;
                afterId = batch[^1].AdjustId;
                Console.WriteLine($"AdjustName: rozpoczynam batch {batchNumber + 1} ({batch.Count} rekordow); przetworzono {processed}/{total}, pozostalo {Math.Max(0, total - processed)}.");
                var nips = batch.Select(row => row.Nip).ToArray();
                var xmlRows = await FirmyTempDbContext.CompanyXml.Where(row => nips.Contains(row.Nip))
                    .ToDictionaryAsync(row => row.Nip!, StringComparer.Ordinal);
                var newCompanies = new List<Company>();
                var validRows = new List<AdjustName>();
                foreach (var row in batch)
                {
                    if (string.IsNullOrWhiteSpace(row.Nip) || string.IsNullOrWhiteSpace(row.Nazwa)
                        || string.IsNullOrWhiteSpace(row.CompanyXml) || !row.WojewodztwoId.HasValue)
                    {
                        Console.WriteLine($"AdjustName Id={row.AdjustId}, NIP={row.Nip}: missing NIP, name, XML or province; skipped.");
                        continue;
                    }

                    try
                    {
                        var document = new XmlDocument { XmlResolver = null };
                        document.LoadXml(row.CompanyXml);
                        if (document.DocumentElement?.Name != "Detail")
                            throw new XmlException("Expected a Detail XML record.");
                        if (string.Equals(document.GetInnerTextOrEmpty("StatusDzialalnosci").Trim(),
                            "Wykreślony", StringComparison.OrdinalIgnoreCase))
                        {
                            row.Processed = true;
                            Console.WriteLine($"AdjustName Id={row.AdjustId}, NIP={row.Nip}: status Wykreślony; pominieto import firmy i XML.");
                            continue;
                        }
                    }
                    catch (XmlException ex)
                    {
                        Console.WriteLine($"AdjustName Id={row.AdjustId}, NIP={row.Nip}: invalid XML; {ex.Message}");
                        continue;
                    }

                    validRows.Add(row);
                }
                await CeidgConverter.Lookups.PrepareBatchAsync(validRows.Select(row => row.CompanyXml!));
                foreach (var row in validRows)
                {
                    var company = await CeidgConverter.ConvertXmlToNewCompany(row.WojewodztwoId!.Value, row.CompanyXml!);
                    // Explicit staging columns are authoritative, even when XML values differ.
                    company.Nip = row.Nip;
                    company.Name = row.Nazwa;
                    company.WojewodztwoId = row.WojewodztwoId.Value;
                    FirmyTempDbContext.Companies.Add(company);
                    newCompanies.Add(company);

                    if (!xmlRows.TryGetValue(row.Nip, out var companyXml))
                    {
                        companyXml = new CompanyXml { Nip = row.Nip };
                        FirmyTempDbContext.CompanyXml.Add(companyXml);
                        xmlRows.Add(row.Nip, companyXml);
                    }
                    companyXml.XmlContent = row.CompanyXml;
                    companyXml.Md5 = MD5Utils.CalculateMD5(row.CompanyXml);
                    companyXml.WojewodztwoId = row.WojewodztwoId;
                    companyXml.DateModified = row.DateModified;
                    row.Processed = true;
                }

                // Save the company, XML and processing flag atomically before resolving addresses.
                await FirmyTempDbContext.SaveChangesAsync();
                added += newCompanies.Count;
                if (!GlobalSettings.SkipProcessingAddresses && newCompanies.Count > 0)
                {
                    var results = await addressUpdater.UpdateBatchAsync(newCompanies, progress: addressProgress);
                    foreach (var result in results)
                        addressProgress.Complete(result);
                }
                processed += batch.Count;
                batchNumber++;
                var remaining = Math.Max(0, total - processed);
                var percent = total == 0 ? 100 : 100d * processed / total;
                Console.WriteLine($"AdjustName BATCH {batchNumber} ZAKONCZONY | Przetworzono: {processed}/{total} | Pozostalo: {remaining} | Postep: {percent:F2}% | Dodano firm: {added} | Pominieto: {processed - added}");
                FirmyTempDbContext.ChangeTracker.Clear();
            }
            Statistics.FinishCurrentPhase("Finished", added);
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
