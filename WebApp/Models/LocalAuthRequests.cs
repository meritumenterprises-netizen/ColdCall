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
    [Required, StringLength(200, MinimumLength = 2)]
    public string DisplayName { get; set; } = "";
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
