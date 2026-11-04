using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class FeedbackRequest
{
    [Required, StringLength(2048)]
    public string TurnstileToken { get; set; } = "";
    [Required, StringLength(100)]
    public string FirstName { get; set; } = "";
    [Required, StringLength(100)]
    public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";
    [Required, StringLength(10000)]
    public string Comment { get; set; } = "";
}
