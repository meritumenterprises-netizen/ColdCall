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

        
        public async Task ImportNewFiles()
        {
            if (GlobalSettings.SkipImportNewFiles == false)
            {
                Console.WriteLine($"Importing new files {Statistics.NewDate:yyyy-MM-dd}...");
                // Validate every input before truncating ImportTarget or importing any rows.
                if (!SkipImports && !GlobalSettings.SkipXmlImports)
                {
                    foreach (var report in Statistics.NewReports.Values.SelectMany(reports => reports))
                    {
                        var xmlPath = Path.Combine(Statistics.NewFilesFolder, report.FileName);
                        if (!File.Exists(xmlPath))
                            throw new FileNotFoundException(
                                "Required CEIDG XML is missing. ImportTarget has not been cleared. "
                                + "Run the download and unpack phases again before retrying the import.", xmlPath);
                    }
                }
                var swGlobal = Stopwatch.StartNew();
                var globalRecordCounter = 0;
                var globalMd5s = this.ContentXmlMd5s;
                var globalNips = this.FirmyTempDbContext.Companies.Select((c) => c.Nip).Distinct().ToHashSet<string>();
                var newNips = new List<string>();
                var stagedNips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                {
                    Statistics.StartNewPhase(ImportPhasesEnum.ImportNewFilesToTargetImportTable, "");
                    if (SkipImports || GlobalSettings.SkipXmlImports)
                    {
                        const string message = "XML import disabled; retaining ImportTarget records.";
                        Console.WriteLine(message);
                        Statistics.CurrentPhase.PhaseSkipped = true;
                        Statistics.FinishCurrentPhase(message);
                        return;
                    }
                    Statistics.CurrentPhase.EntriesCount = Statistics.NewFilesCount;
                    var dbUtils = new CeidgDatabaseUtils(this.ImportDbContext);

                    Console.Write("Truncating table ImportTarget... ");
                    await dbUtils.TruncateImportTargetAsync();
                    Console.WriteLine("Done.");


                    var culture = CultureInfo.GetCultureInfo("pl-PL");
                    var flushTableEveryRecordCount = 100;

                    Console.WriteLine($"Processing XML files");

                    int fileCounter = 1;
                    int fileCount = Statistics.NewReports.Sum((r) => r.Value.Count);

                    // Process all provinces chronologically so the newest report wins for each NIP.
                    foreach (var report in Statistics.NewReports.Values.SelectMany(reports => reports)
                        .OrderBy(report => report.CreatedAtDateTime, StringComparer.Ordinal)
                        .ThenBy(report => report.Name, StringComparer.Ordinal)
                        .ThenBy(report => report.Id, StringComparer.Ordinal))
                        {
                            var localNewNips = new List<string>();
                            var fileStagedNips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            Statistics.StartNewChildPhase(ImportPhasesEnum.ImportNewFileToTargetImportTable, $"Importing file {report.FileName} to table ImportTarget");
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

                                var nip = detail.Elements()
                                    .FirstOrDefault(element => element.Name.LocalName == "Nip")?.Value.Trim() ?? "";

                                if (!string.IsNullOrEmpty(nip) && !globalNips.Contains(nip))
                                {
                                    localNewNips.Add(nip);
                                    Console.WriteLine($"{localNewNips.Count}# New NIP={nip}");
                                }

                                var xmlText = Regex.Replace(detail.ToString(SaveOptions.DisableFormatting), @"<Lp\.>\d+</Lp\.>\r?\n?", "");
                                var md5 = MD5Utils.CalculateMD5(xmlText);

                                if (globalMd5s.Contains(md5) && globalNips.Contains(nip))
                                {
                                    // A later known version supersedes an earlier staged version of this NIP.
                                    if (stagedNips.Remove(nip))
                                    {
                                        var superseded = await ImportDbContext.ImportTargets
                                            .SingleOrDefaultAsync(target => target.Nip == nip);
                                        if (superseded != null)
                                        {
                                            ImportDbContext.ImportTargets.Remove(superseded);
                                            await ImportDbContext.SaveChangesAsync();
                                            ImportDbContext.Entry(superseded).State = EntityState.Detached;
                                        }
                                    }
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
                                    this.ImportDbContext.Entry(importTarget).State = EntityState.Detached;
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
                                    this.ImportDbContext.Entry(target).State = EntityState.Detached;
                                }

                                stagedNips.Add(nip);
                                fileStagedNips.Add(nip);
                                recordCounter++;
                                if (recordCounter % flushTableEveryRecordCount == 0)
                                {
                                    Console.WriteLine($"Writing {flushTableEveryRecordCount} records of {recordCounter} records to database Firmy_Import, skipped {skippedRecordCounter} records...");
                                    await ImportRepository.SaveAndClearAsync();
                                }
                                
                            }
                            await ImportRepository.SaveAndClearAsync();

                            sw.Stop();
                            var pendingFileRecords = fileStagedNips.Count(stagedNips.Contains);
                            Statistics.CurrentPhase.EntriesCount = pendingFileRecords;
                            Console.WriteLine($"Done reading. Read {parsedRecordCounter} XML records; staging writes {recordCounter}; pending NIPs from this file {pendingFileRecords}; skipped {skippedRecordCounter} (known MD5: {skippedKnownMd5Counter}, duplicate MD5: {skippedDuplicateCounter}); blank NIPs: {skippedBlankNips}, new NIPs: {localNewNips.Count}. Elapsed time: {sw.ElapsedTime()}");
                            globalRecordCounter = await ImportDbContext.ImportTargets.CountAsync();
                            fileCounter++;
                            Statistics.CurrentPhase.EntriesCount = pendingFileRecords;
                            Statistics.FinishChildPhase("");

                            newNips.AddRange(localNewNips);
                            newNips = newNips.Distinct().ToList();
                        }

                    Statistics.FinishCurrentPhase("", globalRecordCounter);
                    await ImportRepository.SaveAndClearAsync();
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

    }

}
