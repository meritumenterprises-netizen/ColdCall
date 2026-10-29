using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class AdminPlanDefinitionDto
{
    public int Id { get; set; }
    [Required]
    public string Code { get; set; } = "";
    [Range(0, int.MaxValue)]
    public int MaxNumberOfSearches { get; set; }
    [Range(0, int.MaxValue)]
    public int? MaxNumberOfCSVExportsPerSearch { get; set; }
    [Range(0, int.MaxValue)]
    public int? MaxNumberOfExportedRecords { get; set; }
    [Range(1, int.MaxValue)]
    public int? ExpiresAfterDays { get; set; }
    public bool ShowInOffer { get; set; }
    public bool EnableCSVDeleting { get; set; } = true;
    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal Price { get; set; }
    public string DescriptionHtml { get; set; } = "";
    public bool IsUpgrade { get; set; }
    public bool IsTrial { get; set; }
}
