using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class ExportCompaniesRequest
{
    [Range(1, int.MaxValue)]
    public int SearchProfileId { get; set; }
    [StringLength(200)]
    public string? SearchName { get; set; }
    public int? ModifiedWithinDays { get; set; }
    [Required, RegularExpression("^(name|nip|fullAddress|phone|email|www|dataAktywnosci)$")]
    public string SortBy { get; set; } = "dataAktywnosci";
    [Required, RegularExpression("^(asc|desc)$")]
    public string Direction { get; set; } = "desc";
}
