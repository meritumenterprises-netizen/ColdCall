namespace ColdCall.Models;

public sealed record InFaktInvoiceBuyer(string? CompanyName, string? FirstName, string? LastName,
    string? Nip, string Street, string StreetNumber, string City, string PostalCode);
