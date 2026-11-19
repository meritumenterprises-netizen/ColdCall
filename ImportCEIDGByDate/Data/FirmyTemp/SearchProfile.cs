namespace ImportCEIDGByDate.Data.FirmyTemp;

public class SearchProfile
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string? SearchProfileHash { get; set; }
    public DateTime? LastSavedAtUtc { get; set; }
    public int? RecordCount { get; set; }
    public virtual ICollection<SearchProfileMcp> SearchProfileMcps { get; set; } = new List<SearchProfileMcp>();
}
