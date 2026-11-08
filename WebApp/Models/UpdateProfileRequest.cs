using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class UpdateProfileRequest
{
    [Required, StringLength(32)] public string Phone { get; set; } = "";
    [Required, StringLength(100)] public string FirstName { get; set; } = "";
    [Required, StringLength(100)] public string LastName { get; set; } = "";
    [StringLength(200)] public string? CompanyName { get; set; }
    [PolishNip, StringLength(10)] public string? Nip { get; set; }
    [Required, RegularExpression(@"[0-9]{2}-[0-9]{3}")] public string PostalCode { get; set; } = "";
    [Required, StringLength(100)] public string City { get; set; } = "";
    [Required, StringLength(200)] public string Street { get; set; } = "";
    [Required, StringLength(50)] public string StreetNumber { get; set; } = "";
}
