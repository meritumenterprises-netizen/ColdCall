using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class TermsDecisionRequest
{
    [Required]
    public bool? Accepted { get; set; }
}
