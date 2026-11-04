using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class UpdatePlanOfferRequest
{
    [Required]
    public bool? ShowInOffer { get; set; }
}
