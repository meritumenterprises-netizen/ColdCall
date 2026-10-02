using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class SearchProfileCriteriaDto : IValidatableObject
{
    [Required(AllowEmptyStrings = true), StringLength(1000)]
    public string CompanyName { get; set; } = "";
    [Required, RegularExpression("^(name|nip|fullAddress|phone|email|www|dataAktywnosci)$")]
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
    [Range(1, 10000)]
    public int RecordLimit { get; set; } = 1000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateRangeEnabled && (!DateFrom.HasValue || !DateTo.HasValue || DateTo <= DateFrom))
            yield return new ValidationResult("Both dates are required and the end date must be later than the start date.", new[] { nameof(DateFrom), nameof(DateTo) });
    }
}
