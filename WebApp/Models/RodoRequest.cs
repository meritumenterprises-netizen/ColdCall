using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public sealed class RodoRequest : FeedbackRequest
{
    [Required, RegularExpression(@"^[0-9]{10}$"), StringLength(10, MinimumLength = 10)]
    public string Nip { get; set; } = "";
}
