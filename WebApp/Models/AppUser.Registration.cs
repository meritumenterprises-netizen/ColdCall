namespace ColdCall.Models;

public partial class AppUser
{
    public string? Phone { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? CompanyName { get; set; }
    public string? Nip { get; set; }
    public bool? TermsOfServiceAccepted { get; set; }
    public bool IsBetaUser { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Street { get; set; }
    public string? StreetNumber { get; set; }
}
