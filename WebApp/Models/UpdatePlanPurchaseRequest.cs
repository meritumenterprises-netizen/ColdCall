using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class UpdatePlanPurchaseRequest
{
    [Required]
    public bool? DisablePlanPurchase { get; set; }
}
