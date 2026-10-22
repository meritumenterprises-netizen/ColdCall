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

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

if (args.Count() < 1)
{
    Console.WriteLine("Please provide a date in the format YYYY-MM-DD as an argument.");
    return 1;
}

var newDate = Convert.ToDateTime(args[0]);
var oldDate = newDate.AddDays(-1);

var oldFilesFolder = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), oldDate.ToString("yyyy-MM-dd")));

if (!Directory.Exists(oldFilesFolder))
{
    Console.WriteLine($"Old files folder '{oldFilesFolder}' does not exist. Creating it.");
    Directory.CreateDirectory(oldFilesFolder);
}
else
{
    Console.WriteLine($"Old files folder '{oldFilesFolder} exists.");
}

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

var statistics = new ImportStatistics();

statistics.Init($"Import CEIDG by date {newDate:yyyy-MM-dd}, diffed from date {oldDate:yyyy-MM-dd}\r\nOld files folder: {oldFilesFolder}\\r\\nNew files folder: {newFilesFolder}", newDate, oldDate, oldFilesFolder, newFilesFolder);

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<ImportStatistics>(optional: true)
    .Build();

var services = new ServiceCollection();
services.AddImportRepositories(configuration);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var firmyTempRepository = scope.ServiceProvider.GetRequiredService<FirmyTempRepository>();
var firmyImportRepository = scope.ServiceProvider.GetRequiredService<FirmyImportRepository>();
var firmyImportDbContext = scope.ServiceProvider.GetRequiredService<FirmyImportDbContext>();
var firmyTempDbContext = scope.ServiceProvider.GetRequiredService<FirmyTempDbContext>();

var wojewodztwa = await firmyTempRepository.Wojewodztwa.AsNoTracking().ToListAsync();

statistics.FinishCurrentPhase("Initialization complete.");

statistics.StartNewPhase(ImportPhasesEnum.DownloadAndParseReport, "Downloading and parsing reports from CEIDG website.");

var ceidgDownloader = new CeidgReportDownloader(statistics, wojewodztwa);
bool parseReport = await ceidgDownloader.ParseReport();
if (!parseReport)
{
    statistics.CurrentPhase.IsCriticalError = true;
    statistics.CurrentPhase.CriticalErrorMessage = "Unable to download and/or parse reports from CEIDG website";
    goto summary;
}
if (statistics.OldReports.Count == 0)
{
    statistics.CurrentPhase.IsCriticalError = true;
    statistics.CurrentPhase.CriticalErrorMessage = $"Unable to download files for old date {statistics.OldDate}";
    goto summary;
}

if (statistics.NewReports.Count == 0)
{
    statistics.CurrentPhase.IsCriticalError = true;
    statistics.CurrentPhase.CriticalErrorMessage = $"Unable to download files for new date {statistics.NewDate}";
    goto summary;
}

await ceidgDownloader.DownloadOldFiles();
await ceidgDownloader.DownloadNewFiles();

ceidgDownloader.UnpackOldReports();
ceidgDownloader.UnpackNewReports();

await ceidgDownloader.DeduplicateOldFiles();
await ceidgDownloader.DeduplicateNewFiles();

var ceidgImporter = new CeidgFileImporter(statistics, wojewodztwa, firmyImportRepository, firmyImportDbContext, firmyTempDbContext);
await ceidgImporter.ImportOldFiles();
await ceidgImporter.ImportNewFiles();
await ceidgImporter.UpdateImportTargetMetadata();

var ceidgConverter = new CeidgCompanyXmlConverter(statistics, firmyTempRepository, firmyImportRepository, firmyImportDbContext, firmyTempDbContext);
await ceidgConverter.AddNewCompaniesFromImportToTemp();
await ceidgConverter.UpdateExistingCompaniesFromImportToTemp();

var ceidgCompanyXmlUpdater = new CeidgCompanyXmlUpdater(statistics, firmyTempRepository, firmyImportRepository, firmyImportDbContext, firmyTempDbContext);
await ceidgCompanyXmlUpdater.UpdateCompaniesXml();



summary:

statistics.Finish("Finished");

Console.WriteLine();
Console.WriteLine("===== SUMMARY =====");
Console.WriteLine(statistics.Summary);

return 0;
