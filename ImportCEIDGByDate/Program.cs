using System.Globalization;
using System.Net.Http.Headers;
using Dapper;
using System.Data;
using Microsoft.Data.SqlClient;
using System.IO.Compression;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using ImportCEIDGByDate;
using ImportCEIDGByDate.Data;
using ImportCEIDGByDate.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ImportCEIDGByDate.Processors;
using ImportCEIDGByDate.Utils;
using System.Runtime.CompilerServices;
using ImportCEIDGByDate.Data.FirmyTemp;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var globalSettings = new GlobalSettings();


if (args.Count() < 1)
{
    Console.WriteLine("Please provide a date in the format YYYY-MM-DD as an argument.");
    return 1;
}

var newDate = Convert.ToDateTime(args[0]);

var newFilesFolder = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), newDate.ToString("yyyy-MM-dd")));
if (!Directory.Exists(newFilesFolder))
{
    Console.WriteLine($"New files folder '{newFilesFolder}' does not exist. Creating it.");
    Directory.CreateDirectory(newFilesFolder);
}
else
{
    Console.WriteLine($"New files folder '{newFilesFolder} exists.");
}

var statistics = new Statistics();

statistics.Init($"Import CEIDG by date {newDate:yyyy-MM-dd}, New files folder: {newFilesFolder}",newDate, newFilesFolder);

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Statistics>(optional: true)
    .Build();

var services = new ServiceCollection();
services.AddImportRepositories(configuration);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var firmyTempRepository = scope.ServiceProvider.GetRequiredService<FirmyTempRepository>();
var firmyImportRepository = scope.ServiceProvider.GetRequiredService<FirmyImportRepository>();
var firmyImportDbContext = scope.ServiceProvider.GetRequiredService<FirmyImportDbContext>();
var firmyTempDbContext = scope.ServiceProvider.GetRequiredService<FirmyTempDbContext>();

var configuredParallelism = await firmyTempRepository.GetLatestImportDegreeOfParallelismAsync();
if (configuredParallelism.HasValue)
{
    if (configuredParallelism.Value <= 0)
        throw new InvalidOperationException("ColdCall.GlobalSettings.GlobalDegreeOfParallelismImportByDate must be greater than zero.");
    globalSettings.GlobalDegreeOfParallelism = configuredParallelism.Value;
    Console.WriteLine($"GlobalDegreeOfParallelism={globalSettings.GlobalDegreeOfParallelism} (latest ColdCall.GlobalSettings record).");
}
else
{
    Console.WriteLine($"ColdCall.GlobalSettings is empty; using default GlobalDegreeOfParallelism={globalSettings.GlobalDegreeOfParallelism}.");
}

var wojewodztwa = await firmyTempRepository.Wojewodztwa.AsNoTracking().ToListAsync();

var import = new ImportByDate
{
    ImportDate = statistics.NewDate,
    StartedAt = statistics.ProcessStart
};
firmyTempDbContext.ImportByDates.Add(import);
await firmyTempDbContext.SaveChangesAsync();

globalSettings.TruncateImportOriginAtTheEnd = false;
globalSettings.TruncateImportTargetAtTheEnd = false;
globalSettings.SkipProcessingAddresses = true;

statistics.FinishCurrentPhase("Initialization complete.");

var ceidgDownloadFolderCleanup = new CeidgDownloadFolderCleanup(statistics);
ceidgDownloadFolderCleanup.CleanUp();

statistics.StartNewPhase(ImportPhasesEnum.DownloadAndParseReport, "Downloading and parsing reports from CEIDG website.");

var ceidgDownloader = new CeidgReportDownloader(statistics, wojewodztwa, globalSettings);
var exitCode = 0;
bool parseReport = await ceidgDownloader.ParseReport();
if (!parseReport)
{
    statistics.CurrentPhase.IsCriticalError = true;
    statistics.CurrentPhase.CriticalErrorMessage = "Unable to download and/or parse reports from CEIDG website";
    exitCode = 1;
    goto summary;
}
LocalCeidgReports.AddExisting(statistics.NewReports, statistics.NewFilesFolder, newDate);
LocalCeidgReports.SortReports(statistics.NewReports);

if (statistics.NewReports.Values.Sum(reports => reports.Count) == 0)
{
    statistics.CurrentPhase.IsCriticalError = true;
    statistics.CurrentPhase.CriticalErrorMessage = $"Unable to download files for new date {statistics.NewDate}";
    Console.Error.WriteLine($"No API or local reports for requested date {newDate:yyyy-MM-dd}. Import cancelled.");
    exitCode = 1;
    goto summary;
}

globalSettings.TruncateImportOriginAtTheEnd = false;
globalSettings.TruncateImportTargetAtTheEnd = false;
//globalSettings.SkipImportOldFiles = true;
//globalSettings.SkipImportNewFiles = true;
//globalSettings.SkipDownloadOldFiles = true;
//globalSettings.SkipDownloadNewFiles = true;
//globalSettings.SkipUnpackOldFiles = true;
//globalSettings.SkipUnpackNewFiles = true;
//globalSettings.SkipImportOldFiles = true;
//globalSettings.SkipImportNewFiles = true;
//globalSettings.SkipUpdateImportTargetMetadata = true;
//globalSettings.SkipUpdateExistingCompaniesFromOrigin = true;
//globalSettings.SkipAddNewCompaniesFromImportToTemp = true;
//globalSettings.SkipUpdateExistingCompaniesFromImportToTemp = true;
//globalSettings.SkipImportDeferredAddressesAsync = true;

var contentXmlCleaner = new CeidgContentXmlCleaner(statistics, firmyTempRepository, firmyImportRepository, firmyImportDbContext, firmyTempDbContext, globalSettings);
await contentXmlCleaner.PerformContentXmlCleanup();


await ceidgDownloader.DownloadNewFiles();

ceidgDownloader.UnpackNewReports();

var ceidgImporter = new CeidgFileImporter(statistics, wojewodztwa, firmyImportRepository, firmyImportDbContext, firmyTempDbContext, globalSettings);

await ceidgImporter.ImportNewFiles();
//await ceidgImporter.RemoveNIPsPresentInImportTargetFromImportSource();
await ceidgImporter.UpdateImportTargetMetadata();

var ceidgUpdater = new CeidgCompanyUpdater(statistics, firmyTempRepository, firmyImportRepository, firmyImportDbContext, firmyTempDbContext, globalSettings)
{
    DeferAddressAssignment = false
};

await ceidgUpdater.AddNewCompaniesFromImportTargetToTemp();
await ceidgUpdater.UpdateExistingCompaniesFromImportTargetToTemp();

var ceidgCompanyXmlUpdater = new CeidgCompanyXmlUpdater(statistics, firmyTempRepository, firmyImportRepository, firmyImportDbContext, firmyTempDbContext, globalSettings);
await ceidgCompanyXmlUpdater.UpdateCompaniesXml();



summary:

statistics.Finish(exitCode == 0 ? "Finished" : "Failed");
var summaryText = statistics.Summary;

Console.WriteLine();
Console.WriteLine("===== SUMMARY =====");
Console.WriteLine(summaryText);

import.FinishedAt = statistics.ProcessEnd.Value;
import.ImportTargetRecords = statistics.ImportTargetRecordCount;
import.Summary = summaryText;
import.Duration = import.FinishedAt.Value.Subtract(import.StartedAt).AsString();
// Batch processing may have detached the startup record.
firmyTempDbContext.ImportByDates.Update(import);
await firmyTempDbContext.SaveChangesAsync();


return exitCode;
