using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class EmailRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";
}

public class PasswordLoginRequest : EmailRequest
{
    [Required, StringLength(128)]
    public string Password { get; set; } = "";
}

public class RegistrationRequest : PasswordLoginRequest
{
    [Required, StringLength(2048)]
    public string TurnstileToken { get; set; } = "";
    [Required, StringLength(100)]
    public string FirstName { get; set; } = "";
    [Required, StringLength(100)]
    public string LastName { get; set; } = "";
    [StringLength(200)]
    public string? CompanyName { get; set; }
    [Required, StringLength(6, MinimumLength = 6), RegularExpression(@"[0-9]{2}-[0-9]{3}")]
    public string PostalCode { get; set; } = "";
    [Required, StringLength(100)]
    public string City { get; set; } = "";
    [Required, StringLength(200)]
    public string Street { get; set; } = "";
    [Required, StringLength(50)]
    public string StreetNumber { get; set; } = "";
}

public class EmailTokenRequest : EmailRequest
{
    [Required, StringLength(4096)]
    public string Token { get; set; } = "";
}

public class ResetPasswordRequest : EmailTokenRequest
{
    [Required, StringLength(128)]
    public string Password { get; set; } = "";
}
