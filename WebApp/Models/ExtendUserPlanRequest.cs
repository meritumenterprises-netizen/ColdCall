using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class ExtendUserPlanRequest
{
    [Required, RegularExpression("^(add|fromNow)$")]
    public string Mode { get; set; } = "";
    [Range(1, int.MaxValue)]
    public int Days { get; set; }
}
