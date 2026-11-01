namespace ColdCall.Models;

public class CsvDeletionQuotaDto
{
    public int? Limit { get; set; }
    public int Used { get; set; }
    public int? Remaining => Limit.HasValue ? Math.Max(0, Limit.Value - Used) : null;
    public DateOnly QuotaDate { get; set; }
    public DateTime? ExportBlockedUntilUtc { get; set; }
}
