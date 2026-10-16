namespace ColdCall.Models;

public class CsvExport
{
    public int EksportCsvId { get; set; }
    public int? SearchProfileId { get; set; }
    public SearchProfile? SearchProfile { get; set; }
    public DateTime CreatedOn { get; set; }
    public Guid CsvFileNameId { get; set; }
    public string CsvFileName { get; set; } = "";
    public int? NumberOfRecords { get; set; }
    public string? SearchProfileHash { get; set; }
}
