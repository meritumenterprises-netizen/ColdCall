using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyTemp;
using ImportCEIDGByDate.Data.FirmyImport;
using ImportCEIDGByDate.Repositories;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace ImportCEIDGByDate
{
    public class CeidgFileImporter
    {
        public ImportStatistics Statistics { get; set; }
        public List<Wojewodztwa> Wojewodztwa { get; set; }

        public FirmyImportRepository ImportRepository { get; set; }
        public FirmyImportDbContext ImportDbContext { get; set; }
        public FirmyTempDbContext FirmyTempDbContext { get; set; }

        public CeidgFileImporter(ImportStatistics statistics, List<Wojewodztwa> wojewodztwa, FirmyImportRepository repository, FirmyImportDbContext context, FirmyTempDbContext firmyTempDbContext)
        {
            this.Statistics = statistics;
            this.ImportRepository = repository;
            this.ImportDbContext = context;
            this.FirmyTempDbContext = firmyTempDbContext;
            this.Wojewodztwa = wojewodztwa;
        }

        public async Task ImportOldFiles()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.ImportNewFilesToTable, "Importing old files to ImportOrigin");
            Statistics.CurrentPhase.EntriesCount = Statistics.OldReports.Sum((r) => r.Value.Count);
            Console.WriteLine($"Importing old files {Statistics.OldDate:yyyy-MM-dd}...");
            var dbUtils = new CeidgDatabaseUtils(this.ImportDbContext);
            Console.Write($"Truncating table ImportOrigin... ");
            await dbUtils.TruncateImportOriginAsync();
            Console.WriteLine("Done.");
            // disable transactions for inserts
            this.ImportDbContext.Database.AutoTransactionBehavior = Microsoft.EntityFrameworkCore.AutoTransactionBehavior.Never;

            var culture = CultureInfo.GetCultureInfo("pl-PL");
            var xmlDocument = new XmlDocument();
            var flushTableEveryRecordCount = 10000;

            var globalRecordCounter = 0;
            var skippedBlankNips = 0;
            var pendingMd5s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var globalMd5s = await FirmyTempDbContext.CompanyXml
                .AsNoTracking()
                .Select(companyXml => companyXml.Md5)
                .ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

            var swGlobal = Stopwatch.StartNew();
            int fileCounter = 1;
            int fileCount = Statistics.OldReports.Sum((r) => r.Value.Count);
            foreach (var key in Statistics.OldReports.Keys)
            {
                foreach (var report in Statistics.OldReports[key])
                {
                    double progress = fileCount > 0 ? (double)fileCounter / fileCount * 100.0 : 0;
                    var recordCounter = 0;
                    var skippedRecordCounter = 0;

                    Statistics.StartNewPhase(ImportPhasesEnum.ImportOldFileToTable, $"Importing file {report.FileName} to table ImportOrigin");
                    Console.WriteLine($"Importing {progress:F2}% {fileCounter}/{fileCount} - {Path.GetFileName(report.FileName)} to table ImportOrigin");

                    var match = Regex.Match(report.FileName, @"województwo (?<nazwa>[^_]+)_");
                    string? wojewodztwo = match.Success ? match.Groups["nazwa"].Value : null;

                    if (!match.Success && Regex.IsMatch(report.FileName, @"brak województwa", RegexOptions.IgnoreCase))
                    {
                        wojewodztwo = "Brak województwa";
                    }

                    wojewodztwo = culture.TextInfo.ToTitleCase(wojewodztwo.Replace("-", " ")).Trim();
                    var wojewodztwoId = this.Wojewodztwa.Where((w) => w.Name.Equals(wojewodztwo, StringComparison.OrdinalIgnoreCase)).FirstOrDefault()?.Id; ;
                    if (!wojewodztwoId.HasValue)
                        throw new InvalidDataException($"Unknown wojewodztwo: {wojewodztwo}");

                    var settings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        IgnoreWhitespace = true
                    };

                    using var reader = XmlReader.Create(Path.Combine(Statistics.OldFilesFolder, report.FileName), settings);

                    var sw = Stopwatch.StartNew();

                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Detail")
                            continue;

                        using var subtree = reader.ReadSubtree();
                        var detail = XElement.Load(subtree);

                        string? GetValue(string name) => detail.Elements()
                                .FirstOrDefault(element => element.Name.LocalName == name)
                                ?.Value.Trim();

                        xmlDocument.LoadXml(detail.ToString());
                        var elemLp = xmlDocument.SelectSingleNode("Detail/Lp.");
                        elemLp.ParentNode.RemoveChild(elemLp);
                        var nip = xmlDocument.SelectSingleNode("Detail/Nip")?.InnerText ?? "";

                        var xml = xmlDocument.OuterXml;
                        var md5 = MD5Utils.CalculateMD5(xml);

                        if (globalMd5s.Contains(md5))
                        {
                            skippedRecordCounter++;
                            continue;
                        }

                        if (pendingMd5s.Contains(md5)
                            || await ImportRepository.ImportOrigins.AnyAsync(origin => origin.Md5 == md5))
                        {
                            skippedRecordCounter++;
                            continue;
                        }

                        if (string.IsNullOrEmpty(nip))
                        {
                            skippedBlankNips++;
                            continue;
                        }

                        ImportRepository.ImportOrigins.Add(new ImportOrigin
                        {
                            WojewodztwoId = wojewodztwoId.Value,
                            CompanyXml = xml,
                            Md5 = md5,
                            Nip = nip,
                            ImportDate = Statistics.OldDate
                        });
                        pendingMd5s.Add(md5);
                        recordCounter++;
                        if (recordCounter % flushTableEveryRecordCount == 0)
                        {
                            Console.WriteLine($"Writing {flushTableEveryRecordCount} records of {recordCounter} records to database, skipped {skippedRecordCounter} records...");
                            await ImportRepository.SaveChangesAsync();

                        }
                    }
                    await ImportRepository.SaveChangesAsync();

                    sw.Stop();
                    Statistics.CurrentPhase.EntriesCount = recordCounter;
                    globalRecordCounter += recordCounter;
                    Console.WriteLine($"Done importing. Imported {recordCounter} records, skipped {skippedRecordCounter} records, skipped {skippedBlankNips} blank NIPs. Elapsed time: {sw.ElapsedTime()}");
                    fileCounter++;
                }
            }
            pendingMd5s.Clear();

            await ImportRepository.SaveChangesAsync();
            swGlobal.Stop();
            Console.WriteLine($"Done importing. Total records: {globalRecordCounter}. Total elapsed time: {swGlobal.ElapsedTime()}");
            Statistics.StartNewPhase(ImportPhasesEnum.FinishedImportingNewFiles, "Finished importing old files to ImportOrigin");
            Statistics.FinishCurrentPhase("Finished");
        }

        public async Task ImportNewFiles()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.ImportNewFilesToTable, "Importing new files to ImportTarget");
            Statistics.CurrentPhase.EntriesCount = Statistics.NewReports.Sum((r) => r.Value.Count);
            Console.WriteLine($"Importing new files {Statistics.NewDate:yyyy-MM-dd}...");
            var dbUtils = new CeidgDatabaseUtils(this.ImportDbContext);
            Console.Write($"Truncating table ImportTarget... ");
            await dbUtils.TruncateImportTargetAsync();
            Console.WriteLine("Done.");
            // disable transactions for inserts
            this.ImportDbContext.Database.AutoTransactionBehavior = Microsoft.EntityFrameworkCore.AutoTransactionBehavior.Never;

            var globalRecordCounter = 0;
            var skippedBlankNips = 0;

            var culture = CultureInfo.GetCultureInfo("pl-PL");
            var xmlDocument = new XmlDocument();
            var flushTableEveryRecordCount = 10000;

            var globalMd5s = await FirmyTempDbContext.CompanyXml
                .AsNoTracking()
                .Select(companyXml => companyXml.Md5)
                .ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

            Console.Write($"Reading MD5 hashes from table ImportOrigin...");
            Stopwatch swPendingMd5s = Stopwatch.StartNew();
            var pendingMd5s = await ImportRepository.ImportOrigins
                .AsNoTracking()
                .Select(origin => origin.Md5)
                .ToHashSetAsync(StringComparer.OrdinalIgnoreCase);
            swPendingMd5s.Stop();
            Console.WriteLine($"Finished in {swPendingMd5s.Elapsed.TotalSeconds:F2} second(s)");
            var swGlobal = Stopwatch.StartNew();
            Console.WriteLine($"Processing XML files");

            int fileCounter = 1;
            int fileCount = Statistics.NewReports.Sum((r) => r.Value.Count);

            foreach (var key in Statistics.NewReports.Keys)
            {
                foreach (var report in Statistics.NewReports[key])
                {
                    var recordCounter = 0;
                    var skippedRecordCounter = 0;
                    double progress = fileCount > 0 ? (double)fileCounter / fileCount * 100.0 : 0;

                    Statistics.StartNewPhase(ImportPhasesEnum.ImportNewFileToTable, $"Importing file {report.FileName} to table ImportTarget");
                    Console.WriteLine($"Importing {progress:F2}% {fileCounter}/{fileCount} - {Path.GetFileName(report.FileName)} to table ImportTarget");

                    var match = Regex.Match(report.FileName, @"województwo (?<nazwa>[^_]+)_");
                    string? wojewodztwo = match.Success ? match.Groups["nazwa"].Value : null;

                    if (!match.Success && Regex.IsMatch(report.FileName, @"brak województwa", RegexOptions.IgnoreCase))
                    {
                        wojewodztwo = "Brak województwa";
                    }

                    wojewodztwo = culture.TextInfo.ToTitleCase(wojewodztwo.Replace("-", " ")).Trim();
                    var wojewodztwoId = this.Wojewodztwa.Where((w) => w.Name.Equals(wojewodztwo, StringComparison.OrdinalIgnoreCase)).FirstOrDefault()?.Id; ;
                    if (!wojewodztwoId.HasValue)
                        throw new InvalidDataException($"Unknown wojewodztwo: {wojewodztwo}");

                    var settings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null,
                        IgnoreWhitespace = true
                    };

                    using var reader = XmlReader.Create(Path.Combine(Statistics.NewFilesFolder, report.FileName), settings);

                    var sw = Stopwatch.StartNew();

                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Detail")
                            continue;

                        using var subtree = reader.ReadSubtree();
                        var detail = XElement.Load(subtree);

                        string? GetValue(string name) => detail.Elements()
                                .FirstOrDefault(element => element.Name.LocalName == name)
                                ?.Value.Trim();

                        xmlDocument.LoadXml(detail.ToString());
                        var elemLp = xmlDocument.SelectSingleNode("Detail/Lp.");
                        elemLp.ParentNode.RemoveChild(elemLp);
                        var nip = xmlDocument.SelectSingleNode("Detail/Nip")?.InnerText ?? "";

                        var xml = xmlDocument.OuterXml;
                        var md5 = MD5Utils.CalculateMD5(xml);

                        if (globalMd5s.Contains(md5))
                        {
                            skippedRecordCounter++;
                            continue;
                        }

                        if (pendingMd5s.Contains(md5)
                            || await ImportRepository.ImportOrigins.AnyAsync(origin => origin.Md5 == md5))
                        {
                            skippedRecordCounter++;
                            continue;
                        }

                        if (string.IsNullOrEmpty(nip))
                        {
                            skippedBlankNips++;
                            continue;
                        }

                        ImportRepository.ImportTargets.Add(new ImportTarget
                        {
                            WojewodztwoId = wojewodztwoId.Value,
                            CompanyXml = xml,
                            Md5 = md5,
                            Nip = nip,
                            ImportDate = Statistics.NewDate,
                            IsNew = false,
                            IsUpdated = false,
                            Imported = false
                        });
                        pendingMd5s.Add(md5);
                        recordCounter++;
                        if (recordCounter % flushTableEveryRecordCount == 0)
                        {
                            Console.WriteLine($"Writing {flushTableEveryRecordCount} records of {recordCounter} records to database, skipped {skippedRecordCounter} records...");
                            await ImportRepository.SaveChangesAsync();
                        }
                    }
                    await ImportRepository.SaveChangesAsync();

                    sw.Stop();
                    Statistics.CurrentPhase.EntriesCount = recordCounter;
                    Console.WriteLine($"Done importing. Imported {recordCounter} records, skipped {skippedRecordCounter} records, skipped {skippedBlankNips} blank NIPs. Elapsed time: {sw.ElapsedTime()}"); globalRecordCounter += recordCounter;
                    fileCounter++;
                }
            }
            pendingMd5s.Clear();

            await ImportRepository.SaveChangesAsync();
            swGlobal.Stop();
            Console.WriteLine($"Done importing. Total records: {globalRecordCounter}. Total elapsed time: {swGlobal.ElapsedTime()}");
            Statistics.StartNewPhase(ImportPhasesEnum.FinishedImportingNewFiles, "Finished importing new files to ImportTarget");
            Statistics.FinishCurrentPhase("Finished");
        }
        public async Task UpdateImportTargetMetadata()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.UpdateImportTargetMetadata, "Updating metadata in table ImportTarget");
            Console.WriteLine($"Updating metadata in table ImportTarget...");
            var dbUtils = new CeidgDatabaseUtils(this.ImportDbContext);

            Console.Write($"Finding NIPs in ImportTarget that do not exist in Companies... ");
            var newNips = await ImportDbContext.Database.SqlQueryRaw<string>("""
                SELECT DISTINCT target.NIP AS [Value]
                FROM [dbo].[ImportTarget] AS target
                WHERE target.NIP IS NOT NULL AND LTRIM(RTRIM(target.NIP)) <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM [Firmy_temp].[ColdCall].[Companies] AS company
                      WHERE company.Nip COLLATE DATABASE_DEFAULT = target.NIP COLLATE DATABASE_DEFAULT
                  )
                """).ToListAsync();
            var newImportTargets = await ImportRepository.ImportTargets
                .Where(target => target.Nip != null && newNips.Contains(target.Nip))
                .ToListAsync();
            Console.WriteLine($"Done. Found {newNips.Count} new NIPs");
            foreach (var newImportTarget in newImportTargets)
            {
                newImportTarget.IsNew = true;
            }
            await this.ImportRepository.SaveChangesAsync();

            Console.Write($"Finding NIPs in ImportTarget that exist in Companies... ");
            var existingNips = await ImportDbContext.Database.SqlQueryRaw<string>("""
                SELECT DISTINCT target.NIP AS [Value]
                FROM [dbo].[ImportTarget] AS target
                WHERE target.NIP IS NOT NULL AND LTRIM(RTRIM(target.NIP)) <> ''
                  AND EXISTS (
                      SELECT 1 FROM [Firmy_temp].[ColdCall].[Companies] AS company
                      WHERE company.Nip COLLATE DATABASE_DEFAULT = target.NIP COLLATE DATABASE_DEFAULT
                  )
                """).ToListAsync();
            var existingImportTargets = await ImportRepository.ImportTargets
                .Where(target => target.Nip != null && existingNips.Contains(target.Nip))
                .ToListAsync();
            foreach (var existingImportTarget in existingImportTargets)
            {
                existingImportTarget.IsUpdated = true;
            }
            await this.ImportRepository.SaveChangesAsync();

            Console.WriteLine($"Done. Found {existingNips.Count} existing NIPs");

            Console.WriteLine($"Finished updating metadata in table ImportTarget.");
            Statistics.FinishCurrentPhase("Finished");
        }
    }
}
