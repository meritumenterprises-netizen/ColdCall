using System.ComponentModel.DataAnnotations;

namespace ColdCall.Models;

public class AdminUpdateUserDetailsRequest
{
    [Range(0, 36500)] public int? ExpiresInDays { get; set; }
    [StringLength(100)] public string? FirstName { get; set; }
    [StringLength(100)] public string? LastName { get; set; }
    [StringLength(200)] public string? CompanyName { get; set; }
    [StringLength(10), RegularExpression(@"[0-9]{10}")] public string? Nip { get; set; }
    [StringLength(32)] public string? Phone { get; set; }
    [StringLength(20), RegularExpression(@"[0-9]{2}-[0-9]{3}")] public string? PostalCode { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(200)] public string? Street { get; set; }
    [StringLength(50)] public string? StreetNumber { get; set; }
}
