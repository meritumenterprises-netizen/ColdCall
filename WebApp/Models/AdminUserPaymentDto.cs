namespace ColdCall.Models;

public class AdminUserPaymentDto
{
    public string PlanCode { get; set; } = "";
    public DateTime PaidAtUtc { get; set; }
    public decimal AmountPaid { get; set; }
    public string StripeSessionId { get; set; } = "";
    public bool HasInvoice { get; set; }
}
