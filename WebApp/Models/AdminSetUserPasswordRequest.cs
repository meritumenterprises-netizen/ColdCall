using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class AdminSetUserPasswordRequest
{
    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = "";
}
