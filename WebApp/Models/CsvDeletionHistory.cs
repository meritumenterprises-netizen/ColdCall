namespace ColdCall.Models;

public class CsvDeletionHistory
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public int ExportId { get; set; }
    public DateTime DeletedAtUtc { get; set; }
    public DateOnly QuotaDate { get; set; }
}
