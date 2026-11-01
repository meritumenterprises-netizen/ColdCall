using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class UpdateUserAdminRequest
{
    [Required]
    public bool? IsAdmin { get; set; }
}
