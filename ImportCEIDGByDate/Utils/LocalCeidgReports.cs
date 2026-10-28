using System.Globalization;
using System.Text.RegularExpressions;

namespace ImportCEIDGByDate;

public static class LocalCeidgReports
{
    public static void AddExisting(Dictionary<string, List<CeidgReport>> reports, string folder, DateTime date)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(x => x, StringComparer.Ordinal))
        {
            var match = Regex.Match(Path.GetFileName(file),
                @"^(?<name>Zarejestrowane działalności - .+)_(?<date>\d{8}_\d{6})\.(xml|zip)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || !DateTime.TryParseExact(match.Groups["date"].Value, "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) || created.Date != date.Date)
                continue;
            var name = match.Groups["name"].Value;
            if (!reports.TryGetValue(name, out var entries)) reports.Add(name, entries = new List<CeidgReport>());
            var report = new CeidgReport
            {
                Id = "local:" + Path.GetFileNameWithoutExtension(file), Name = name, Format = ".xml",
                Report = "", CreatedAt = created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
            if (!entries.Any(r => string.Equals(r.FileName, report.FileName, StringComparison.OrdinalIgnoreCase)))
                entries.Add(report);
        }
    }

    public static void SortReports(Dictionary<string, List<CeidgReport>> reports)
    {
        foreach (var key in reports.Keys)
        {
            reports[key].Sort((r1,r2) => r1.FileName.CompareTo(r2.FileName));
        }
    }
}
