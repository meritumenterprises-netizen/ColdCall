namespace ImportCEIDGByDate.Data.FirmyTemp;

public class SearchProfileMcp
{
    public int SearchProfileMcpId { get; set; }
    public int SearchProfileId { get; set; }
    public int SearchCompaniesCount { get; set; }
    public int SearchCompaniesMaxCount { get; set; }
    public int GetCompanyCount { get; set; }
    public int GetCompanyMaxCount { get; set; }
    public int CsvExportCount { get; set; }
    public int CsvExportMaxCount { get; set; }
    public int GetPkdCodesCount { get; set; }
    public int GetPkdCodesMaxCount { get; set; }
    public virtual SearchProfile SearchProfile { get; set; } = null!;
}
