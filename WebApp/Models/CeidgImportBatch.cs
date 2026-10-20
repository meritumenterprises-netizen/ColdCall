namespace ColdCall.Models;

public class CeidgImportBatch
{
    public int CeidgImportBatchId { get; set; }
    public DateTime ImportDate { get; set; }
    public int CompaniesAdded { get; set; }
    public int CompaniesUpdated { get; set; }
    public int CompaniesSkipped { get; set; }
    public int CompaniesTotal { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreatedOn { get; set; }
}
