namespace ImportCEIDGByDate.Data.FirmyTemp;

public partial class ImportByDate
{
    public int ImportId { get; set; }
    public DateTime ImportDate { get; set; }
    public int? ImportTargetRecords { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Summary { get; set; }
    public string? Duration { get; set; }
    public string? Version { get; set; }
}
