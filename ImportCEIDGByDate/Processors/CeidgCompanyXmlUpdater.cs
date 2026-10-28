using ImportCEIDGByDate.Data;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using ImportCEIDGByDate.Utils;

namespace ImportCEIDGByDate.Repositories
{
    public class CeidgCompanyXmlUpdater
    {
        public FirmyTempRepository FirmyTempRepository { get; private set; }
        public FirmyImportRepository FirmyImportRepository { get; private set; }

        public FirmyImportDbContext FirmyImportDbContext { get; private set; }
        public FirmyTempDbContext FirmyTempDbContext { get; private set; }

        public GlobalSettings GlobalSettings { get; private set; }

        private HashSet<string> _md5;
        public HashSet<string> ContentXmlMd5s
        {

            get
            {
                if (_md5 == null)
                {
                    var globalMd5s = FirmyTempDbContext.CompanyXml
                   .AsNoTracking()
                   .Select(companyXml => companyXml.Md5)
                   .ToHashSetAsync(StringComparer.OrdinalIgnoreCase).Result;
                    _md5 = globalMd5s;
                }
                return _md5;
            }
        }

        public void ContentXmlMd5sRefresh()
        {
            var globalMd5s = FirmyTempDbContext.CompanyXml
           .AsNoTracking()
           .Select(companyXml => companyXml.Md5)
           .ToHashSetAsync(StringComparer.OrdinalIgnoreCase).Result;
            _md5 = globalMd5s;
        }
        private Statistics Statistics { get; set; }

        public CeidgCompanyXmlUpdater(
            Statistics statistics,
            FirmyTempRepository firmyTempRepository,
            FirmyImportRepository firmyImportRepository,
            FirmyImportDbContext firmyImportDbContext,
            FirmyTempDbContext firmyTempDbContext,
            GlobalSettings globalSettings)
        {
            this.Statistics = statistics;
            this.FirmyTempRepository = firmyTempRepository;
            this.FirmyImportRepository = firmyImportRepository;

            this.FirmyImportDbContext = firmyImportDbContext;
            this.FirmyTempDbContext = firmyTempDbContext;
            this.GlobalSettings = globalSettings;
        }

        public async Task UpdateCompaniesXml()
        {
            _md5 ??= await FirmyTempDbContext.CompanyXml.AsNoTracking()
                .Select(row => row.Md5).ToHashSetAsync(StringComparer.OrdinalIgnoreCase);
            Statistics.StartNewPhase(ImportPhasesEnum.UpdateCompanyXml, "Updating companies in CompanyXML");
            Statistics.CurrentPhase.EntriesCount = this.FirmyImportRepository.ImportOrigins.Count() + this.FirmyImportRepository.ImportTargets.Count();

            Stopwatch sw = Stopwatch.StartNew();
            Console.WriteLine($"Updating Companies XML from ImportOrigin table");

            var recordCount = 0;
            var recordSize = 1000;
            Statistics.StartNewChildPhase(ImportPhasesEnum.UpdateCompanyXmlFromImportOrigin, "");
            Statistics.CurrentPhase.EntriesCount = this.FirmyImportDbContext.ImportOrigins.Where((origin) => !string.IsNullOrEmpty(origin.Nip)).Count();
            foreach (var batch in this.FirmyImportDbContext.ImportOrigins.AsNoTracking().Where(origin => !string.IsNullOrEmpty(origin.Nip)).OrderBy(origin => origin.Id).AsEnumerable().Chunk(recordSize))
            {
                var nips = batch.Select(source => source.Nip).ToArray();
                var existing = await FirmyTempDbContext.CompanyXml.Where(row => nips.Contains(row.Nip)).ToDictionaryAsync(row => row.Nip!, StringComparer.OrdinalIgnoreCase);
                foreach (var sourceCompany in batch)
                {
                    var md5 = MD5Utils.CalculateMD5(sourceCompany.CompanyXml);
                    if (ContentXmlMd5s.Contains(md5)) continue;
                    existing.TryGetValue(sourceCompany.Nip!, out var sourceCompanyXmlRecord);
                    if (sourceCompanyXmlRecord == null)
                    {
                        sourceCompanyXmlRecord = new Data.FirmyTemp.CompanyXml();
                        sourceCompanyXmlRecord.Nip = sourceCompany.Nip;
                        sourceCompanyXmlRecord.XmlContent = sourceCompany.CompanyXml;
                        sourceCompanyXmlRecord.Md5 = md5;
                        FirmyTempDbContext.CompanyXml.Add(sourceCompanyXmlRecord);
                        existing.Add(sourceCompany.Nip!, sourceCompanyXmlRecord);
                    }
                    else
                    {
                        sourceCompanyXmlRecord.Nip = sourceCompany.Nip;
                        sourceCompanyXmlRecord.XmlContent = sourceCompany.CompanyXml;
                        sourceCompanyXmlRecord.Md5 = md5;
                    }

                    recordCount++;
                    Console.WriteLine($"Writing {recordCount} record(s) to CompanyXml");
                }
                await FirmyTempDbContext.SaveChangesAsync();
                foreach (var row in existing.Values) FirmyTempDbContext.Entry(row).State = EntityState.Detached;
            }
            Statistics.FinishChildPhase("");
            await this.FirmyTempDbContext.SaveChangesAsync();
            if (GlobalSettings.TruncateImportOriginAtTheEnd)
            {
                await new CeidgDatabaseUtils(FirmyImportDbContext).TruncateImportOriginAsync();
                Console.WriteLine("ImportOrigin cleared after saving CompanyXml.");
            }

            Console.WriteLine($"Updating Companies XML from ImportTarget table");

            ContentXmlMd5sRefresh();

            Statistics.StartNewChildPhase(ImportPhasesEnum.UpdateCompanyXmlFromImportTarget, "");
            Statistics.CurrentPhase.EntriesCount = this.FirmyImportDbContext.ImportTargets.Where((origin) => !string.IsNullOrEmpty(origin.Nip)).Count();
            recordCount = 0;
            foreach (var batch in this.FirmyImportDbContext.ImportTargets.AsNoTracking().Where(origin => !string.IsNullOrEmpty(origin.Nip)).OrderBy(origin => origin.Id).AsEnumerable().Chunk(recordSize))
            {
                var nips = batch.Select(source => source.Nip).ToArray();
                var existing = await FirmyTempDbContext.CompanyXml.Where(row => nips.Contains(row.Nip)).ToDictionaryAsync(row => row.Nip!, StringComparer.OrdinalIgnoreCase);
                foreach (var sourceCompany in batch)
                {
                    var md5 = MD5Utils.CalculateMD5(sourceCompany.CompanyXml);
                    if (ContentXmlMd5s.Contains(md5)) continue;
                    existing.TryGetValue(sourceCompany.Nip!, out var sourceCompanyXmlRecord);
                    if (sourceCompanyXmlRecord == null)
                    {
                        sourceCompanyXmlRecord = new Data.FirmyTemp.CompanyXml();
                        sourceCompanyXmlRecord.Nip = sourceCompany.Nip;
                        sourceCompanyXmlRecord.XmlContent = sourceCompany.CompanyXml;
                        sourceCompanyXmlRecord.Md5 = md5;
                        FirmyTempDbContext.CompanyXml.Add(sourceCompanyXmlRecord);
                        existing.Add(sourceCompany.Nip!, sourceCompanyXmlRecord);
                    }
                    else
                    {
                        sourceCompanyXmlRecord.XmlContent = sourceCompany.CompanyXml;
                        sourceCompanyXmlRecord.Md5 = md5;
                    }

                    recordCount++;
                    Console.WriteLine($"Writing {recordCount} record(s) to CompanyXml");

                }
                await FirmyTempDbContext.SaveChangesAsync();
                foreach (var row in existing.Values) FirmyTempDbContext.Entry(row).State = EntityState.Detached;
            }
            await this.FirmyTempDbContext.SaveChangesAsync();

            if (GlobalSettings.TruncateImportTargetAtTheEnd)
            {
                await new CeidgDatabaseUtils(FirmyImportDbContext).TruncateImportTargetAsync();
                Console.WriteLine("ImportTarget cleared after saving CompanyXml.");
            }

            Statistics.FinishChildPhase("");

            sw.Stop();
            Statistics.FinishCurrentPhase("Finished");
            Console.WriteLine($"Finished in {sw.ElapsedTime()}");
        }
    }
}
