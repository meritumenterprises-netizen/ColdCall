namespace ColdCall.Models;

public class StripePayment
{
    public string StripeSessionId { get; set; } = "";
    public int AppUserId { get; set; }
    public AppUser AppUser { get; set; } = null!;
    public int PlanDefinitionId { get; set; }
    public PlanDefinition PlanDefinition { get; set; } = null!;
    public DateTime PaidAtUtc { get; set; }
    public decimal AmountPaid { get; set; }
    public byte[]? PDFInvoice { get; set; }
    public string? InvoiceId { get; set; }
    public int? ClientId { get; set; }
    public string? InvoiceNumber { get; set; }
}
