using ImportCEIDGByDate.Data.FirmyTemp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace ImportCEIDGByDate
{
    public class CeidgReportDownloader
    {
        private string Report { get; set; } = "";
        private Dictionary<string, List<CeidgReport>> AllReports { get; set; } = new Dictionary<string, List<CeidgReport>>();

        protected ImportStatistics Statistics { get; set; }
        protected List<Wojewodztwa> Wojewodztwa { get; set; }

        public CeidgReportDownloader(ImportStatistics statistics, List<Wojewodztwa> wojewodztwa)
        {
            this.Statistics = statistics;
            this.Wojewodztwa = wojewodztwa;
        }
        public async Task<bool> ParseReport(CancellationToken token = default)
        {
            this.Report = "";
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<CeidgReportDownloader>(optional: true)
                .Build();
            var bearerToken = Environment.GetEnvironmentVariable("CEIDG_API_TOKEN")
                ?? configuration["Ceidg:Token"];
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                Console.Error.WriteLine("Set CEIDG_API_TOKEN or the user secret Ceidg:Token.");
                return false;
            }

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken.Trim());
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            try
            {
                using var response = await http.GetAsync(
                    $"https://dane.biznes.gov.pl/api/ceidg/v3/raporty?dataod={Statistics.OldDate:yyyy-MM-dd}&datado={Statistics.NewDate.AddDays(1):yyyy-MM-dd}", token);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Console.Error.WriteLine($"CEIDG /raporty returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
                    return false;
                }

                this.Report = await response.Content.ReadAsStringAsync(token);
                CeidgReports raporty;
                try
                {
                    raporty = JsonSerializer.Deserialize<CeidgReports>(this.Report)
                        ?? throw new InvalidDataException("Empty CEIDG reports response.");
                    if (raporty.Reports == null || raporty.Reports.Any(report => report == null))
                        throw new InvalidDataException("Invalid raporty array in CEIDG response.");
                }
                catch (JsonException)
                {
                    Console.Error.WriteLine($"CEIDG /raporty returned HTTP 200 but invalid JSON. Content-Type: {response.Content.Headers.ContentType}.");
                    return false;
                }

                this.AllReports = new Dictionary<string, List<CeidgReport>>();

                var matchingReports = raporty.Reports.Where(r => r?.Name?.StartsWith("Zarejestrowane działalności - ", StringComparison.OrdinalIgnoreCase) == true && r.Format == ".xml").ToList();
                this.Statistics.CurrentPhase.EntriesCount = matchingReports.Count;
                matchingReports.ForEach((matchingReport) =>
                {
                    if (!this.AllReports.ContainsKey(matchingReport.Name))
                    {
                        this.AllReports.Add(matchingReport.Name, new List<CeidgReport>());
                    }
                    this.AllReports[matchingReport.Name].Add(matchingReport);
                });

                this.Statistics.OldReports = new Dictionary<string, List<CeidgReport>>();
                this.AllReports.Keys.ToList().ForEach((key) =>
                {
                    if (this.Statistics.OldReports.ContainsKey(key) == false)
                    {
                        this.Statistics.OldReports.Add(key, new List<CeidgReport>());
                    }
                    this.Statistics.OldReports[key].AddRange(this.AllReports[key].Where(r => r.CreatedAtDate == Statistics.OldDate.ToString("yyyy-MM-dd")));
                });

                this.AllReports.Keys.ToList().ForEach((key) =>
                {
                    if (this.Statistics.NewReports.ContainsKey(key) == false)
                    {
                        this.Statistics.NewReports.Add(key, new List<CeidgReport>());
                    }
                    this.Statistics.NewReports[key].AddRange(this.AllReports[key].Where(r => r.CreatedAtDate == Statistics.NewDate.ToString("yyyy-MM-dd")));
                });

                return true;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"Unable to download CEIDG reports: {ex.Message}");
                return false;
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return false;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                Console.Error.WriteLine("CEIDG reports request timed out.");
                return false;
            }
        }

        private async Task DownloadIndividualFile(CeidgReport report, string targetFolder, string fileName)
        {
            ArgumentNullException.ThrowIfNull(report);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);
            if (!Uri.TryCreate(report.Report, UriKind.Absolute, out var reportUrl)
                || reportUrl.Scheme != Uri.UriSchemeHttps
                || !string.Equals(reportUrl.Host, "dane.biznes.gov.pl", StringComparison.OrdinalIgnoreCase)
                || !reportUrl.IsDefaultPort || !string.IsNullOrEmpty(reportUrl.UserInfo)
                || !reportUrl.AbsolutePath.StartsWith("/api/ceidg/v3/raport/", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid CEIDG report URL; bearer token was not sent.");

            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Invalid report filename.");

            PartialDownloadCleanup.ForFolder(targetFolder);
            PartialDownloadCleanup.ForReport(report, targetFolder);

            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<CeidgReportDownloader>(optional: true)
                .Build();
            var bearerToken = Environment.GetEnvironmentVariable("CEIDG_API_TOKEN")
                ?? configuration["Ceidg:Token"];
            if (string.IsNullOrWhiteSpace(bearerToken))
                throw new InvalidOperationException("Set CEIDG_API_TOKEN or the user secret Ceidg:Token.");

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, reportUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException($"CEIDG report download returned HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                    null, response.StatusCode);

            Directory.CreateDirectory(targetFolder);
            var targetPath = Path.Combine(Path.GetFullPath(targetFolder), fileName);
            var partialPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 81920, FileOptions.Asynchronous))
                    await input.CopyToAsync(output, timeout.Token);

                File.Move(partialPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(partialPath)) File.Delete(partialPath);
            }
        }

        public async Task DownloadOldFiles()
        {
            PartialDownloadCleanup.ForFolder(this.Statistics.OldFilesFolder);
            Statistics.StartNewPhase(ImportPhasesEnum.DownloadOldFiles, $"Downloading old files for date {Statistics.OldDate:yyyy-MM-ss}");
            Statistics.CurrentPhase.EntriesCount = this.Statistics.OldReports.Values.Sum(reports => reports.Count);
            Console.WriteLine($"Downloading {this.Statistics.OldReports.Values.Sum(reports => reports.Count)} files for {this.Statistics.OldDate.ToString("yy-MM-dd")}");

            var skippedReportCount = 0;

            foreach (var wojewodztwoPlik in this.Statistics.OldReports.Keys)
            {
                Console.WriteLine($"Downloading {wojewodztwoPlik} {this.Statistics.OldReports[wojewodztwoPlik].Count} file(s).");
                foreach (var ceidgReport in this.Statistics.OldReports[wojewodztwoPlik])
                {
                    PartialDownloadCleanup.ForReport(ceidgReport, this.Statistics.OldFilesFolder);
                    var fullFile = Path.Combine(this.Statistics.OldFilesFolder, ceidgReport.FileNameZip);
                    if (File.Exists(fullFile))
                    {
                        Console.WriteLine($"\tFile exists, skipping");
                        skippedReportCount++;
                        continue;
                    }
                    Stopwatch sw = Stopwatch.StartNew();
                    Console.Write($"\tDownloading {ceidgReport.Report}... ");
                    await DownloadIndividualFile(ceidgReport, this.Statistics.OldFilesFolder, ceidgReport.FileNameZip);
                    sw.Stop();
                    Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F2} second(s).");
                }
            }

            if (Statistics.CurrentPhase.EntriesCount == skippedReportCount)
            {
                Statistics.CurrentPhase.PhaseSkipped = true;
            }
            Statistics.FinishCurrentPhase("Finished");
        }

        public async Task DeduplicateOldFiles()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.DeduplicateOldFiles, "Deduplication of old files");
            Console.WriteLine("Deduplication of old files");

            foreach (var key in Statistics.OldReports.Keys)
            {
                foreach (var report in Statistics.OldReports[key])
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    Console.Write($"Calculating MD5 of {report.FileNameZip}... ");
                    report.MD5 = MD5Utils.CalculateFileMD5(Path.Combine(this.Statistics.OldFilesFolder, report.FileNameZip));
                    sw.Stop();
                    Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F2} second(s)");
                }
            }

            foreach (var key in Statistics.OldReports.Keys.ToList())
            {
                Statistics.OldReports[key] = Statistics.OldReports[key]
                    .DistinctBy(report => report.MD5, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            Console.WriteLine($"Old files count after deduplication: {Statistics.OldReports.Sum((r) => r.Value.Count)}");
            Statistics.FinishCurrentPhase("Finished");
        }
        

        public async Task DeduplicateNewFiles()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.DeduplicateNewFiles, "Deduplication of new files");
            Console.WriteLine("Deduplication of new files");

            foreach (var key in Statistics.NewReports.Keys)
            {
                foreach (var report in Statistics.NewReports[key])
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    Console.Write($"Calculating MD5 of {report.FileNameZip}... ");
                    report.MD5 = MD5Utils.CalculateFileMD5(Path.Combine(this.Statistics.NewFilesFolder, report.FileNameZip));
                    sw.Stop();
                    Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F2} second(s)");
                }
            }
            foreach (var key in Statistics.NewReports.Keys.ToList())
            {
                Statistics.NewReports[key] = Statistics.NewReports[key]
                    .DistinctBy(report => report.MD5, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            Console.WriteLine($"New files count after deduplication: {Statistics.NewReports.Sum((r) => r.Value.Count)}");
            Statistics.FinishCurrentPhase("Finished");
        }

        public async Task DownloadNewFiles()
        {
            PartialDownloadCleanup.ForFolder(this.Statistics.NewFilesFolder);
            Statistics.StartNewPhase(ImportPhasesEnum.DownloadNewFiles, $"Downloading new files for date {Statistics.NewDate:yyyy-MM-ss}");
            Statistics.CurrentPhase.EntriesCount = this.Statistics.NewReports.Values.Sum(reports => reports.Count);
            Console.WriteLine($"Downloading {this.Statistics.NewReports.Values.Sum(reports => reports.Count)} files for {this.Statistics.NewDate.ToString("yy-MM-dd")}");

            var skippedReportCount = 0;

            foreach (var wojewodztwoPlik in this.Statistics.NewReports.Keys)
            {
                Console.WriteLine($"Downloading {wojewodztwoPlik} {this.Statistics.NewReports[wojewodztwoPlik].Count} file(s).");
                foreach (var ceidgReport in this.Statistics.NewReports[wojewodztwoPlik])
                {
                    PartialDownloadCleanup.ForReport(ceidgReport, this.Statistics.NewFilesFolder);
                    var fullFile = Path.Combine(this.Statistics.NewFilesFolder, ceidgReport.FileNameZip);
                    if (File.Exists(fullFile))
                    {
                        Console.WriteLine($"\tFile exists, skipping");
                        skippedReportCount++;
                        continue;
                    }
                    Stopwatch sw = Stopwatch.StartNew();
                    Console.Write($"\tDownloading {ceidgReport.Report}... ");
                    await DownloadIndividualFile(ceidgReport, this.Statistics.NewFilesFolder, ceidgReport.FileNameZip);
                    sw.Stop();
                    Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F2} second(s).");
                }
            }
            if (Statistics.CurrentPhase.EntriesCount == skippedReportCount)
            {
                Statistics.CurrentPhase.PhaseSkipped = true;
            }
            Statistics.FinishCurrentPhase("Finished");
        }

        public static string UnpackIndividualFile(string zipFilePath, string fileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(zipFilePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName == "." || fileName == ".."
                || fileName.EndsWith('.') || fileName.EndsWith(' '))
                throw new ArgumentException("Provide a filename without a directory path.", nameof(fileName));

            var fullZipPath = Path.GetFullPath(zipFilePath);
            var targetPath = Path.Combine(Path.GetDirectoryName(fullZipPath)!, fileName);
            if (string.Equals(fullZipPath, targetPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The extracted file cannot overwrite its ZIP archive.", nameof(fileName));

            using var archive = ZipFile.OpenRead(fullZipPath);
            var entries = archive.Entries.Where(entry => entry.Name.Length > 0).ToArray();
            if (entries.Length != 1)
                throw new InvalidDataException("The report ZIP must contain exactly one file.");

            var partialPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                // Ignore the entry's archived path and use the caller's filename.
                entries[0].ExtractToFile(partialPath);
                File.Move(partialPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(partialPath)) File.Delete(partialPath);
            }
            return targetPath;
        }

        public void UnpackOldReports()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.UnpackOldFiles, $"Unpacking old files");
            Statistics.CurrentPhase.EntriesCount = Statistics.OldReports.Sum((r) => r.Value.Count);
            var skippedFiles = 0;
            foreach (var key in Statistics.OldReports.Keys)
            {
                foreach (var report in Statistics.OldReports[key])
                {
                    if (File.Exists(Path.Combine(Statistics.OldFilesFolder, report.FileName)))
                    {
                        skippedFiles++;
                        Console.WriteLine($"File {report.FileName} exists, skipped.");
                        continue;
                    }

                    Console.Write($"Unpacking {report.FileName}... ");
                    Console.Write($"Processing... ");
                    Stopwatch sw = Stopwatch.StartNew();
                    UnpackIndividualFile(Path.Combine(Statistics.OldFilesFolder, report.FileNameZip), report.FileName);
                    sw.Stop();
                    Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F2} second(s)");
                }
            }
            if (skippedFiles == Statistics.CurrentPhase.EntriesCount)
            {
                Statistics.CurrentPhase.PhaseSkipped = true;
            }
            Statistics.FinishCurrentPhase("Finished");
        }

        public void UnpackNewReports()
        {
            Statistics.StartNewPhase(ImportPhasesEnum.UnpackNewFiles, $"Unpacking New files");
            Statistics.CurrentPhase.EntriesCount = Statistics.NewReports.Sum((r) => r.Value.Count);
            var skippedFiles = 0;
            foreach (var key in Statistics.NewReports.Keys)
            {
                foreach (var report in Statistics.NewReports[key])
                {
                    if (File.Exists(Path.Combine(Statistics.NewFilesFolder, report.FileName)))
                    {
                        skippedFiles++;
                        Console.WriteLine($"File {report.FileName} exists, skipped.");
                        continue;
                    }

                    Console.Write($"Unpacking {report.FileName}... ");
                    Console.Write($"Processing... ");
                    Stopwatch sw = Stopwatch.StartNew();
                    UnpackIndividualFile(Path.Combine(Statistics.NewFilesFolder, report.FileNameZip), report.FileName);
                    sw.Stop();
                    Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F2} second(s)");
                }
            }
            if (skippedFiles == Statistics.CurrentPhase.EntriesCount)
            {
                Statistics.CurrentPhase.PhaseSkipped = true;
            }
            Statistics.FinishCurrentPhase("Finished");
        }
    }
}
