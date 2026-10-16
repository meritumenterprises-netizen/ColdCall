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
}
