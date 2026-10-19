using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ColdCall.Data;
using ColdCall.Models;
using Microsoft.EntityFrameworkCore;

namespace ColdCall.Services;

public sealed record CompanyCsvFile(string Name, byte[] Content);

public class CompanyCsvExportService
{
    private readonly ColdCallDbContext _db;
    private readonly string _root;
    private readonly TimeProvider _time;

    public CompanyCsvExportService(ColdCallDbContext db, IWebHostEnvironment environment, IConfiguration configuration, TimeProvider? time = null)
    {
        _db = db;
        var folder = configuration["CSVExportFolder"];
        if (string.IsNullOrWhiteSpace(folder))
            throw new InvalidOperationException("Ustaw CSVExportFolder w konfiguracji backendu.");
        _root = Path.GetFullPath(folder, environment.ContentRootPath);
        _time = time ?? TimeProvider.System;
    }

    public async Task<CompanyCsvFile> ExportAsync(AppUser user, SearchProfile profile, IReadOnlyList<int> ids,
        int? days, CancellationToken cancellationToken, string? searchName = null)
    {
        if (user.Id <= 0 || profile.Id <= 0)
            throw new ArgumentException("Brak identyfikatora uzytkownika lub wyszukiwania.");
        var companies = await _db.Companies.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Include(x => x.BusinessType).Include(x => x.Wojewodztwo).Include(x => x.Miejscowosc)
            .Include(x => x.Gmina).Include(x => x.Powiat).ToDictionaryAsync(x => x.Id, cancellationToken);
        var pkdRows = await _db.CompanyPkds.AsNoTracking().Where(x => ids.Contains(x.CompanyId))
            .Select(x => new { x.CompanyId, x.PkdCode.Code, x.PkdCode.Name }).Distinct()
            .OrderBy(x => x.CompanyId).ThenBy(x => x.Code).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        var pkds = pkdRows.GroupBy(x => x.CompanyId).ToDictionary(group => group.Key,
            group => string.Join("; ", group.Select(x => x.Code + " " + x.Name)));
        var rows = ids.Where(companies.ContainsKey).Select(id => companies[id]).ToArray();
        var notes = await _db.CompanyUserNotes.AsNoTracking()
            .Where(x => x.UserId == user.Id && ids.Contains(x.CompanyId))
            .ToDictionaryAsync(x => x.CompanyId, x => x.Notes, cancellationToken);
        var content = CreateCsv(rows, pkds, notes);
        var directory = Path.Combine(_root, user.Id.ToString(CultureInfo.InvariantCulture), profile.Id.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var date = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));
                var name = FileName(searchName ?? profile.Name, days, date);
                var path = Path.Combine(directory, name);
                try
                {
                    File.Move(temporary, path, overwrite: false);
                }
                catch (IOException) when (File.Exists(path) && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }
                var export = new CsvExport
                {
                    SearchProfileId = profile.Id,
                    CsvFileName = name,
                    NumberOfRecords = rows.Length,
                    SearchProfileHash = SearchProfileHashCalculator.Calculate(profile)
                };
                try
                {
                    _db.CsvExports.Add(export);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch
                {
                    _db.Entry(export).State = EntityState.Detached;
                    File.Delete(path);
                    throw;
                }
                return new CompanyCsvFile(name, content);
            }
            throw new IOException("Nie udalo sie utworzyc unikalnego pliku eksportu.");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public string? FindFile(int userId, int searchProfileId, string fileName)
    {
        if (userId <= 0 || searchProfileId <= 0 || string.IsNullOrWhiteSpace(fileName)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.Contains('/') || fileName.Contains('\\')
            || !fileName.EndsWith(".CSV", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Path.Combine(_root, userId.ToString(CultureInfo.InvariantCulture),
            searchProfileId.ToString(CultureInfo.InvariantCulture), fileName);
        return File.Exists(path) ? path : null;
    }

    public static string FileName(string searchName, int? days, DateTimeOffset date)
    {
        var name = Regex.Replace(searchName.Normalize(NormalizationForm.FormC), "[<>:\"/\\\\|?*\\x00-\\x1F]", "_").Trim(' ', '.');
        if (name.Length > 80) name = name[..80].TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(name)) name = "wyszukiwanie";
        var tab = days switch { null => "all", 30 => "30 days", 7 => "7 days", _ => throw new ArgumentException("Niepoprawna zakladka eksportu.") };
        return $"{name}_{tab}_{date.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.CSV";
    }

    public byte[] CreateCsv(IEnumerable<Company> companies, IReadOnlyDictionary<int, string> pkds,
        IReadOnlyDictionary<int, string>? notes = null)
    {
        var headers = new[]
        {
            "Nazwa", "Typ Działalności", "Imię", "Nazwisko", "KRS", "NIP", "Regon", "Województwo",
            "Miejscowość", "Gmina", "Powiat", "Ulica", "Nr Budynku", "Nr lokalu", "Telefon",
            "Email", "Witryna WWW", "Data Aktywności", "PKD", "Notatki"
        };
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', headers.Select(Cell)));
        foreach (var company in companies)
        {
            var values = new[]
            {
                company.Name, company.BusinessType?.TypDzialalnosci, company.FirstName, company.LastName,
                company.Krs, company.Nip, company.Regon, company.Wojewodztwo?.Name,
                company.Miejscowosc?.Name, company.Gmina?.Name, company.Powiat?.Name,
                company.Street, company.BuildingNumber, company.ApartmentNumber, company.Phone,
                company.Email, company.Website, company.StartedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                pkds.TryGetValue(company.Id, out var pkd) ? pkd : "",
                notes != null && notes.TryGetValue(company.Id, out var note)
                    ? Regex.Replace(note, @"\r\n|[\r\n\u0085\u2028\u2029]", " ") : ""
            };
            csv.AppendLine(string.Join(',', values.Select((value, index) => index switch
            {
                5 or 6 => IdentifierCell(value),
                14 => ExcelTextCell(value),
                _ => Cell(value)
            })));
        }
        var encoding = new UTF8Encoding(true);
        return encoding.GetPreamble().Concat(encoding.GetBytes(csv.ToString())).ToArray();
    }

    private static string IdentifierCell(string? value)
    {
        value = value?.Trim();
        // Only digit-only identifiers may use Excel's text formula; arbitrary input stays protected.
        if (!string.IsNullOrEmpty(value) && value.All(char.IsAsciiDigit))
            return ExcelTextCell(value);
        return Cell(value);
    }

    private static string ExcelTextCell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Cell(value);
        var formula = "=\"" + value.Replace("\"", "\"\"") + "\"";
        return "\"" + formula.Replace("\"", "\"\"") + "\"";
    }

    private static string Cell(string? value)
    {
        value ??= "";
        // Quoting handles separators/newlines; the prefix prevents spreadsheet formula execution.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
