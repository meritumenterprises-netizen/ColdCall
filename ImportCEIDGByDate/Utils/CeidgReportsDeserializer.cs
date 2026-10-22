using System.Text.Json;

namespace ImportCEIDGByDate;

public static class CeidgReportsDeserializer
{
    public static async Task<CeidgReports> DeserializeFileAsync(
        string filePath, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var reports = await JsonSerializer.DeserializeAsync<CeidgReports>(stream,
            cancellationToken: token);
        if (reports?.Reports == null || reports.Reports.Any(report => report == null))
            throw new InvalidDataException("CEIDG JSON must contain a non-null raporty array with no null entries.");
        return reports;
    }
}
