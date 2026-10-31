using System.Globalization;
using System.Text.RegularExpressions;

namespace ImportCEIDGByDate;

public static class CeidgReportSelection
{
    public static void KeepLatest(Dictionary<string, List<CeidgReport>> reports)
    {
        foreach (var key in reports.Keys.ToArray())
        {
            var datedReports = reports[key].Select(report => new
            {
                Report = report,
                Timestamp = FileTimestamp(report.FileName)
            }).ToList();
            if (datedReports.Count == 0) continue;
            var latest = datedReports.Max(item => item.Timestamp);
            reports[key] = datedReports.Where(item => item.Timestamp == latest)
                .Select(item => item.Report)
                .DistinctBy(report => report.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static DateTime FileTimestamp(string fileName)
    {
        var match = Regex.Match(fileName, @"_(?<timestamp>\d{8}_\d{6})\.xml$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !DateTime.TryParseExact(match.Groups["timestamp"].Value, "yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            throw new InvalidDataException($"Invalid report filename timestamp: {fileName}");
        return timestamp;
    }
}
