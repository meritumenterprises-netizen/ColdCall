using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class UpdateUserEnabledRequest
{
    [Required]
    public bool? IsEnabled { get; set; }
}
