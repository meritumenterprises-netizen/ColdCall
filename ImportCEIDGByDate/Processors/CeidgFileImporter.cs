using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Data.FirmyImport;
using ImportCEIDGByDate.Data.FirmyTemp;
using ImportCEIDGByDate.Repositories;
using ImportCEIDGByDate.Utils;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ImportCEIDGByDate
{
    public class CeidgFileImporter
    {
        public Statistics Statistics { get; set; }
        public List<Wojewodztwa> Wojewodztwa { get; set; }

        public FirmyImportRepository ImportRepository { get; set; }
        public FirmyImportDbContext ImportDbContext { get; set; }
        public FirmyTempDbContext FirmyTempDbContext { get; set; }

        public bool SkipImports { get; set; }
        public GlobalSettings GlobalSettings { get; set; }
        private HashSet<string>? newSnapshotOriginNips;

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

        public CeidgFileImporter(Statistics statistics, List<Wojewodztwa> wojewodztwa, FirmyImportRepository repository, FirmyImportDbContext context, FirmyTempDbContext firmyTempDbContext, GlobalSettings settings)
        {
            this.GlobalSettings = settings;
            this.Statistics = statistics;
            this.ImportRepository = repository;
            this.ImportDbContext = context;
            this.FirmyTempDbContext = firmyTempDbContext;
            this.Wojewodztwa = wojewodztwa;
        }

        //public async Task ImportOldFiles()
        //{
        //    if (GlobalSettings.SkipImportOldFiles == false)
        //    {
        //        var swGlobal = Stopwatch.StartNew();
        //        var globalRecordCounter = 0;
        //        Console.WriteLine($"Importing old files {Statistics.OldDate:yyyy-MM-dd}...");
        //        // Staged Origin hashes must not be treated as persisted hashes by Target.
        //        var globalMd5s = new HashSet<string>(this.ContentXmlMd5s, StringComparer.OrdinalIgnoreCase);
        //        {
        //            Statistics.StartNewPhase(ImportPhasesEnum.ImportOldFilesToTable, "");
        //            if (SkipImports || GlobalSettings.SkipXmlImports)
        //            {
        //                const string message = "XML import disabled; retaining ImportOrigin records.";
        //                Console.WriteLine(message);
        //                Statistics.CurrentPhase.PhaseSkipped = true;
        //                Statistics.FinishCurrentPhase(message);
        //                return;
        //            }
        //            if (!GlobalSettings.TruncateImportOriginAtTheBeginning && await ImportRepository.ImportOrigins.AsNoTracking().AnyAsync())
        //            {
        //                const string message = "ImportOrigin already contains records; skipping XML import and retaining existing data.";
        //                Console.WriteLine(message);
        //                Statistics.CurrentPhase.PhaseSkipped = true;
        //                Statistics.FinishCurrentPhase(message);
        //                return;
        //            }
        //            Statistics.CurrentPhase.EntriesCount = Statistics.OldFilesCount;


        //            if (this.GlobalSettings.TruncateImportOriginAtTheBeginning)
        //            {
        //                var dbUtils = new CeidgDatabaseUtils(this.ImportDbContext);
        //                Console.Write($"Truncating table ImportOrigin... ");
        //                await dbUtils.TruncateImportOriginAsync();
        //                Console.WriteLine("Done.");
        //                if (Statistics.OldFilesCount == 0)
        //                {
        //                    Statistics.StartNewPhase(ImportPhasesEnum.ImportOldFilesToTable, "No previous-day reports.");
        //                    Statistics.CurrentPhase.PhaseSkipped = true;
        //                    Statistics.FinishCurrentPhase("Skipped; ImportOrigin cleared to avoid stale previous-day data.");
        //                    return;
        //                }
        //            }
        //            // disable transactions for inserts
        //            //this.ImportDbContext.Database.AutoTransactionBehavior = Microsoft.EntityFrameworkCore.AutoTransactionBehavior.Never;

        //            var culture = CultureInfo.GetCultureInfo("pl-PL");
        //            var xmlDocument = new XmlDocument();
        //            var flushTableEveryRecordCount = 10000;

        //            //var pendingMd5s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        //            int fileCounter = 1;
        //            int fileCount = Statistics.OldReports.Sum((r) => r.Value.Count);

        //            foreach (var key in Statistics.OldReports.Keys)
        //            {
        //                foreach (var report in Statistics.OldReports[key])
        //                {
        //                    Statistics.StartNewChildPhase(ImportPhasesEnum.ImportOldFileToTable, $"Importing file {report.FileName} to table ImportOrigin");

        //                    double progress = fileCount > 0 ? (double)fileCounter / fileCount * 100.0 : 0;
        //                    var recordCounter = 0;
        //                    var skippedRecordCounter = 0;
        //                    var parsedRecordCounter = 0;
        //                    var skippedKnownMd5Counter = 0;
        //                    var skippedDuplicateCounter = 0;
        //                    var skippedBlankNips = 0;

        //                    Console.WriteLine($"Importing {progress:F2}% {fileCounter}/{fileCount} - {Path.GetFileName(report.FileName)} to table ImportOrigin");

        //                    var match = Regex.Match(report.FileName, @"województwo (?<nazwa>[^_]+)_");
        //                    string? wojewodztwo = match.Success ? match.Groups["nazwa"].Value : null;

        //                    if (!match.Success && Regex.IsMatch(report.FileName, @"brak województwa", RegexOptions.IgnoreCase))
        //                    {
        //                        wojewodztwo = "Brak województwa";
        //                    }

        //                    wojewodztwo = culture.TextInfo.ToTitleCase(wojewodztwo.Replace("-", " ")).Trim();
        //                    var wojewodztwoId = this.Wojewodztwa.Where((w) => w.Name.Equals(wojewodztwo, StringComparison.OrdinalIgnoreCase)).FirstOrDefault()?.Id; ;
        //                    if (!wojewodztwoId.HasValue)
        //                        throw new InvalidDataException($"Unknown wojewodztwo: {wojewodztwo}");

        //                    var settings = new XmlReaderSettings
        //                    {
        //                        DtdProcessing = DtdProcessing.Prohibit,
        //                        XmlResolver = null,
        //                        IgnoreWhitespace = true,
        //                    };

        //                    using var reader = XmlReader.Create(Path.Combine(Statistics.OldFilesFolder, report.FileName), settings);

        //                    var sw = Stopwatch.StartNew();

        //                    while (reader.Read())
        //                    {
        //                        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Detail")
        //                            continue;

        //                        parsedRecordCounter++;
        //                        using var subtree = reader.ReadSubtree();
        //                        var detail = XElement.Load(subtree);

        //                        string? GetValue(string name) => detail.Elements()
        //                                .FirstOrDefault(element => element.Name.LocalName == name)
        //                                ?.Value.Trim();

        //                        xmlDocument.LoadXml(detail.ToString());
        //                        var nip = xmlDocument.SelectSingleNode("Detail/Nip")?.InnerText ?? "";

        //                        var xmlText = Regex.Replace(detail.ToString(SaveOptions.DisableFormatting), @"<Lp\.>\d+</Lp\.>\r?\n?", "");
        //                        var md5 = MD5Utils.CalculateMD5(xmlText);

        //                        if (globalMd5s.Contains(md5))
        //                        {
        //                            skippedRecordCounter++;
        //                            skippedKnownMd5Counter++;
        //                            continue;
        //                        }

        //                        //if (pendingMd5s.Contains(md5))
        //                        //{
        //                        //    skippedRecordCounter++;
        //                        //    skippedDuplicateCounter++;
        //                        //    continue;
        //                        //}

        //                        if (string.IsNullOrEmpty(nip))
        //                        {
        //                            skippedBlankNips++;
        //                            continue;
        //                        }

        //                        //pendingMd5s.Add(md5);


        //                        ImportRepository.ImportOrigins.Add(new ImportOrigin
        //                        {
        //                            WojewodztwoId = wojewodztwoId.Value,
        //                            CompanyXml = xmlText,
        //                            Md5 = md5,
        //                            Nip = nip,
        //                            ImportDate = Statistics.OldDate
        //                        });
        //                        //pendingMd5s.Add(md5);
        //                        globalMd5s.Add(md5);
        //                        recordCounter++;
        //                        if (recordCounter % flushTableEveryRecordCount == 0)
        //                        {
        //                            Console.WriteLine($"Writing {flushTableEveryRecordCount} records of {recordCounter} records to database, skipped {skippedRecordCounter} records...");
        //                            await ImportRepository.SaveAndClearAsync();

        //                        }
        //                    }
        //                    await ImportRepository.SaveAndClearAsync();

        //                    sw.Stop();
        //                    globalRecordCounter += recordCounter;
        //                    Console.WriteLine($"Done importing. Read {parsedRecordCounter} XML records; imported {recordCounter}; skipped {skippedRecordCounter} (known MD5: {skippedKnownMd5Counter}, duplicate MD5: {skippedDuplicateCounter}); blank NIPs: {skippedBlankNips}. Elapsed time: {sw.ElapsedTime()}");
        //                    fileCounter++;
        //                    Statistics.CurrentPhase.EntriesCount = recordCounter;
        //                    Statistics.FinishChildPhase("");
        //                }
        //            }
        //            //pendingMd5s.Clear();

        //            await ImportRepository.SaveAndClearAsync(); 
        //        }
        //        swGlobal.Stop();
        //        Console.WriteLine($"Done importing. Total records: {globalRecordCounter}. Total elapsed time: {swGlobal.ElapsedTime()}");

        //        Statistics.ImportOriginRecordCount = this.ImportDbContext.ImportOrigins.Count();

        //        Statistics.FinishCurrentPhase("", globalRecordCounter);
        //    }
        //}

        

        public async Task ImportNewFiles()
        {
            newSnapshotOriginNips = null;
            if (GlobalSettings.SkipImportNewFiles == false)
            {
                Console.WriteLine($"Importing new files {Statistics.NewDate:yyyy-MM-dd}...");
                var swGlobal = Stopwatch.StartNew();
                var globalRecordCounter = 0;
                var globalMd5s = new HashSet<string>(this.ContentXmlMd5s, StringComparer.OrdinalIgnoreCase);
                var globalNips = this.FirmyTempDbContext.Companies.Select((c) => c.Nip).Distinct().ToHashSet<string>();
                var newNips = new List<string>();

                {
                    Statistics.StartNewPhase(ImportPhasesEnum.ImportNewFilesToTable, "");
                    if (SkipImports || GlobalSettings.SkipXmlImports)
                    {
                        const string message = "XML import disabled; retaining ImportTarget records.";
                        Console.WriteLine(message);
                        Statistics.CurrentPhase.PhaseSkipped = true;
                        Statistics.FinishCurrentPhase(message);
                        return;
                    }
                    if (!GlobalSettings.TruncateImportTargetAtTheBeginning && await ImportRepository.ImportTargets.AsNoTracking().AnyAsync())
                    {
                        const string message = "ImportTarget already contains records; skipping XML import and retaining existing data.";
                        Console.WriteLine(message);

                        Statistics.CurrentPhase.PhaseSkipped = true;
                        Statistics.FinishCurrentPhase(message);
                        return;
                    }
                    Statistics.CurrentPhase.EntriesCount = Statistics.NewFilesCount;
                    var matchedOriginNips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var dbUtils = new CeidgDatabaseUtils(this.ImportDbContext);

                    if (this.GlobalSettings.TruncateImportTargetAtTheBeginning)
                    {
                        Console.Write($"Truncating table ImportTarget... ");
                        await dbUtils.TruncateImportTargetAsync();
                        Console.WriteLine("Done.");
                    }


                    var culture = CultureInfo.GetCultureInfo("pl-PL");
                    var xmlDocument = new XmlDocument();
                    var flushTableEveryRecordCount = 10000;

                    Console.WriteLine($"Processing XML files");

                    int fileCounter = 1;
                    int fileCount = Statistics.NewReports.Sum((r) => r.Value.Count);

                    foreach (var key in Statistics.NewReports.Keys)
                    {
                        foreach (var report in Statistics.NewReports[key])
                        {
                            var localNewNips = new List<string>();
                            Statistics.StartNewChildPhase(ImportPhasesEnum.ImportNewFileToTable, $"Importing file {report.FileName} to table ImportTarget");
                            var recordCounter = 0;
                            var skippedRecordCounter = 0;
                            var parsedRecordCounter = 0;
                            var skippedKnownMd5Counter = 0;
                            var skippedDuplicateCounter = 0;
                            var skippedBlankNips = 0;
                            double progress = fileCount > 0 ? (double)fileCounter / fileCount * 100.0 : 0;

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

                                parsedRecordCounter++;
                                using var subtree = reader.ReadSubtree();
                                var detail = XElement.Load(subtree);

                                string? GetValue(string name) => detail.Elements()
                                        .FirstOrDefault(element => element.Name.LocalName == name)
                                        ?.Value.Trim();

                                xmlDocument.LoadXml(detail.ToString());
                                var nip = xmlDocument.SelectSingleNode("Detail/Nip")?.InnerText.Trim() ?? "";

                                if (!string.IsNullOrEmpty(nip) && !globalNips.Contains(nip))
                                {
                                    localNewNips.Add(nip);
                                    Console.WriteLine($"{localNewNips.Count}# New NIP={nip}");
                                }

                                var xmlText = Regex.Replace(detail.ToString(SaveOptions.DisableFormatting), @"<Lp\.>\d+</Lp\.>\r?\n?", "");
                                var md5 = MD5Utils.CalculateMD5(xmlText);

                                if (globalMd5s.Contains(md5) && globalNips.Contains(nip))
                                {
                                    skippedRecordCounter++;
                                    skippedKnownMd5Counter++;
                                    continue;
                                }

                                if (string.IsNullOrEmpty(nip))
                                {
                                    skippedBlankNips++;
                                    continue;
                                }

                                var importTarget = this.ImportDbContext.ImportTargets.Where((t) => t.Nip == nip).FirstOrDefault();
                                if (importTarget != null)
                                {
                                    importTarget.ImportDate = Statistics.NewDate;
                                    importTarget.CompanyXml = xmlText;
                                    importTarget.WojewodztwoId = wojewodztwoId.Value;
                                    importTarget.Md5 = md5;
                                    importTarget.Nip = nip;
                                    importTarget.IsNew = localNewNips.Contains(nip);
                                    importTarget.IsUpdated = false;

                                    await this.ImportDbContext.SaveChangesAsync();
                                }
                                else
                                {
                                    ImportTarget target = new ImportTarget();
                                    target.ImportDate = Statistics.NewDate;
                                    target.CompanyXml = xmlText;
                                    target.WojewodztwoId = wojewodztwoId.Value;
                                    target.Md5 = md5;
                                    target.Nip = nip;
                                    target.IsNew = localNewNips.Contains(nip);
                                    target.Imported = false;
                                    target.IsUpdated = false;

                                    this.ImportDbContext.ImportTargets.Add(target);
                                    await this.ImportDbContext.SaveChangesAsync();
                                }

                                recordCounter++;
                                if (recordCounter % flushTableEveryRecordCount == 0)
                                {
                                    Console.WriteLine($"Writing {flushTableEveryRecordCount} records of {recordCounter} records to database, skipped {skippedRecordCounter} records...");
                                    await ImportRepository.SaveAndClearAsync();
                                }
                                
                            }
                            await ImportRepository.SaveAndClearAsync();

                            sw.Stop();
                            Statistics.CurrentPhase.EntriesCount = recordCounter;
                            Console.WriteLine($"Done importing. Read {parsedRecordCounter} XML records; imported {recordCounter}; skipped {skippedRecordCounter} (known MD5: {skippedKnownMd5Counter}, duplicate MD5: {skippedDuplicateCounter}); blank NIPs: {skippedBlankNips}, new NIPs: {localNewNips.Count}. Elapsed time: {sw.ElapsedTime()}");
                            globalRecordCounter = await ImportDbContext.ImportTargets.CountAsync();
                            fileCounter++;
                            Statistics.CurrentPhase.EntriesCount = recordCounter;
                            Statistics.FinishChildPhase("");

                            newNips.AddRange(localNewNips);
                            newNips = newNips.Distinct().ToList();
                        }
                    }

                    Statistics.FinishCurrentPhase("", globalRecordCounter);
                    await ImportRepository.SaveAndClearAsync();
                    newSnapshotOriginNips = matchedOriginNips;
                }
                swGlobal.Stop();
                globalRecordCounter = ImportDbContext.ImportTargets.Count();
                Console.WriteLine($"Done importing. Total records: {globalRecordCounter}. Total new NIPs: {newNips.Count}. Total elapsed time: {swGlobal.ElapsedTime()}");
                Statistics.ImportTargetRecordCount = globalRecordCounter;
            }
        }

        public async Task UpdateImportTargetMetadata()
        {
            if (GlobalSettings.SkipUpdateImportTargetMetadata == false)
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

        public async Task RemoveNIPsPresentInImportTargetFromImportSource()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.RemoveNIPsFromImportOriginThatArePresentInOriginTarget, "");
            var snapshotNips = newSnapshotOriginNips ?? await FindOriginNipsInNewSnapshotAsync();
            var deleted = await ImportRepository.ImportOrigins
                .Where(origin => origin.Nip != null &&
                    ImportRepository.ImportTargets.Any(target => target.Nip == origin.Nip))
                .ExecuteDeleteAsync();
            foreach (var nips in snapshotNips.Chunk(1000))
                deleted += await ImportRepository.ImportOrigins
                    .Where(origin => nips.Contains(origin.Nip!)).ExecuteDeleteAsync();
            // Bulk deletes bypass tracking; detach only the Origin rows that no longer exist.
            var trackedOrigins = ImportDbContext.ChangeTracker.Entries<ImportOrigin>().ToArray();
            if (trackedOrigins.Length > 0)
            {
                var trackedIds = trackedOrigins.Select(entry => entry.Entity.Id).ToArray();
                var survivingIds = await ImportRepository.ImportOrigins.AsNoTracking()
                    .Where(row => trackedIds.Contains(row.Id)).Select(row => row.Id).ToHashSetAsync();
                foreach (var entry in trackedOrigins)
                    if (entry.State != EntityState.Added && !survivingIds.Contains(entry.Entity.Id))
                        entry.State = EntityState.Detached;
            }
            Console.WriteLine($"Removed {deleted} ImportOrigin records with NIPs present in ImportTarget or the new-date XML snapshot.");
            Statistics.FinishCurrentPhase("Finished", deleted);
        }

        private async Task<HashSet<string>> FindOriginNipsInNewSnapshotAsync()
        {
            var remaining = await ImportRepository.ImportOrigins.AsNoTracking()
                .Where(row => row.Nip != null).Select(row => row.Nip!)
                .ToHashSetAsync(StringComparer.OrdinalIgnoreCase);
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (remaining.Count == 0) return found;
            // Resume/skip paths have not parsed XML in this instance, so read only NIPs.
            foreach (var report in Statistics.NewReports.Values.SelectMany(reports => reports))
            {
                using var reader = XmlReader.Create(Path.Combine(Statistics.NewFilesFolder, report.FileName),
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true });
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Nip") continue;
                    var nip = reader.ReadElementContentAsString();
                    if (remaining.Remove(nip)) found.Add(nip);
                }
                if (remaining.Count == 0) break;
            }
            return found;
        }
    }

}
