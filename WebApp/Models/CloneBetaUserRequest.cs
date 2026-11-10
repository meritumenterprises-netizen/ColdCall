using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class CloneBetaUserRequest : EmailRequest
{
    [Required, StringLength(100)]
    public string FirstName { get; set; } = "";
    [Required, StringLength(100)]
    public string LastName { get; set; } = "";
}
