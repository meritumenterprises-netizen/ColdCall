using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class AdminSetUserPasswordRequest
{
    [Required, StringLength(128, MinimumLength = 12)]
    [RegularExpression(@"(?s)^(?=.*\p{L})(?=.*[0-9])(?=.*[^\p{L}\p{N}\s]).*$",
        ErrorMessage = "Hasło musi zawierać litery, cyfry i znaki specjalne.")]
    public string Password { get; set; } = "";
}
