using ImportCEIDGByDate.Data;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace ImportCEIDGByDate.Repositories
{
    public class CeidgCompanyXmlUpdater
    {
        public FirmyTempRepository FirmyTempRepository { get; private set; }
        public FirmyImportRepository FirmyImportRepository { get; private set; }

        public FirmyImportDbContext FirmyImportDbContext { get; private set; }
        public FirmyTempDbContext FirmyTempDbContext { get; private set; }

        private ImportStatistics Statistics { get; set; }

        public CeidgCompanyXmlUpdater(
            ImportStatistics statistics,
            FirmyTempRepository firmyTempRepository,
            FirmyImportRepository firmyImportRepository,
            FirmyImportDbContext firmyImportDbContext,
            FirmyTempDbContext firmyTempDbContext)
        {
            this.Statistics = statistics;
            this.FirmyTempRepository = firmyTempRepository;
            this.FirmyImportRepository = firmyImportRepository;

            this.FirmyImportDbContext = firmyImportDbContext;
            this.FirmyTempDbContext = firmyTempDbContext;
        }

        public async Task UpdateCompaniesXml()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.UpdateCompanyXml, "Updating companies XML");
            Statistics.CurrentPhase.EntriesCount = this.FirmyImportRepository.ImportOrigins.Count();

            this.FirmyTempDbContext.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;

            Stopwatch sw = Stopwatch.StartNew();
            Console.WriteLine($"Updating Companies XML from ImportOrigin table");

            var recordCount = 0;
            var recordSize = 10000;
            foreach (var sourceCompany in this.FirmyImportDbContext.ImportOrigins.Where((origin) => !string.IsNullOrEmpty(origin.Nip)))
            {
                var sourceCompanyXmlRecord = this.FirmyTempDbContext.CompanyXml.Where((c) => c.Nip == sourceCompany.Nip).FirstOrDefault();
                if (sourceCompanyXmlRecord == null)
                {
                    sourceCompanyXmlRecord = new Data.FirmyTemp.CompanyXml();
                    sourceCompanyXmlRecord.Nip = sourceCompany.Nip;
                    sourceCompanyXmlRecord.XmlContent = sourceCompany.CompanyXml;
                    sourceCompanyXmlRecord.Md5 = MD5Utils.CalculateMD5(sourceCompany.CompanyXml);
                    FirmyTempDbContext.CompanyXml.Add(sourceCompanyXmlRecord);
                }
                else
                {
                    sourceCompanyXmlRecord.XmlContent = sourceCompany.CompanyXml;
                    sourceCompanyXmlRecord.Md5 = MD5Utils.CalculateMD5(sourceCompany.CompanyXml);
                }

                recordCount++;
                if (recordCount % recordSize == 0)
                {
                    Console.WriteLine($"Writing {recordCount} to CompanyXml");
                    await this.FirmyTempDbContext.SaveChangesAsync();
                }
            }
            await this.FirmyTempDbContext.SaveChangesAsync();

            Console.WriteLine($"Updating Companies XML from ImportTarget table");

            recordCount = 0;
            foreach (var sourceCompany in this.FirmyImportDbContext.ImportTargets.Where((origin) => !string.IsNullOrEmpty(origin.Nip)))
            {
                var sourceCompanyXmlRecord = this.FirmyTempDbContext.CompanyXml.Where((c) => c.Nip == sourceCompany.Nip).FirstOrDefault();
                if (sourceCompanyXmlRecord == null)
                {
                    sourceCompanyXmlRecord = new Data.FirmyTemp.CompanyXml();
                    sourceCompanyXmlRecord.Nip = sourceCompany.Nip;
                    sourceCompanyXmlRecord.XmlContent = sourceCompany.CompanyXml;
                    sourceCompanyXmlRecord.Md5 = MD5Utils.CalculateMD5(sourceCompany.CompanyXml);
                    FirmyTempDbContext.CompanyXml.Add(sourceCompanyXmlRecord);
                }
                else
                {
                    sourceCompanyXmlRecord.XmlContent = sourceCompany.CompanyXml;
                    sourceCompanyXmlRecord.Md5 = MD5Utils.CalculateMD5(sourceCompany.CompanyXml);
                }

                recordCount++;
                if (recordCount % recordSize == 0)
                {
                    Console.WriteLine($"Writing {recordCount} to CompanyXml");
                    await this.FirmyTempDbContext.SaveChangesAsync();
                }
            }
            await this.FirmyTempDbContext.SaveChangesAsync();

            this.FirmyTempDbContext.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
            sw.Stop();
            Statistics.FinishCurrentPhase("Finished");
            Console.WriteLine($"Finished in {sw.ElapsedTime()}");
        }
    }
}
