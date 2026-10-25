using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class ChangeUserPlanRequest
{
    [Range(1, int.MaxValue)]
    public int PlanDefinitionId { get; set; }
}
