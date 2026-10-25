using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class AdminCreateUserRequest : EmailRequest
{
    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = "";

    public bool DisablePlanPurchase { get; set; }

    [Range(1, int.MaxValue)]
    public int PlanDefinitionId { get; set; }
}
