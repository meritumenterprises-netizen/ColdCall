namespace ImportCEIDGByDate.Data.FirmyImport;

public class ImportReport
{
    public int ReportId { get; set; }
    public DateTime ImportDate { get; set; }
    public string Url { get; set; } = "";
}
