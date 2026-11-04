using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class SearchProfileCriteriaDto : IValidatableObject
{
    public int[] CompanyStatusIds { get; set; } = new[] { 1 };
    [Required(AllowEmptyStrings = true), StringLength(1000)]
    public string CompanyName { get; set; } = "";
    [Required, RegularExpression("^(name|nip|fullAddress|phone|email|www|dataAktywnosci|dataModifiedOn)$")]
    public string SortColumn { get; set; } = "dataAktywnosci";
    [Required, RegularExpression("^(asc|desc)$")]
    public string SortDirection { get; set; } = "desc";
    [Required(AllowEmptyStrings = true), StringLength(1000)]
    public string Address { get; set; } = "";
    public bool? HasPhone { get; set; }
    public bool? HasEmail { get; set; }
    public bool? HasWebsite { get; set; }
    public bool DateRangeEnabled { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    [Range(1, int.MaxValue)]
    public int RecordLimit { get; set; } = 1000;
    public int? WojewodztwoId { get; set; }
    public int? PowiatId { get; set; }
    public int? GminaId { get; set; }
    public int? MiejscowoscId { get; set; }
    public bool? HasFirmyWPromieniu { get; set; }
    public int? FirmyWPromieniu { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CompanyStatusIds == null || CompanyStatusIds.Length > 255 || CompanyStatusIds.Any(id => id < 1 || id > 255))
            yield return new ValidationResult("Niepoprawne statusy dzialalnosci.", new[] { nameof(CompanyStatusIds) });
        if (DateRangeEnabled && (!DateFrom.HasValue || !DateTo.HasValue || DateTo < DateFrom))
            yield return new ValidationResult("Both dates are required and the end date must be on or after the start date.", new[] { nameof(DateFrom), nameof(DateTo) });
    }
}
