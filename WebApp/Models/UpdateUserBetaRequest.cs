using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class UpdateUserBetaRequest
{
    [Required]
    public bool? IsBetaUser { get; set; }
}
