namespace ColdCall.Services;

public static class PlanOfferRules
{
    public static string[] UpgradeCodesFor(string? currentPlanCode) => currentPlanCode switch
    {
        "PX1 to PX3" => new[] { "PX3 to PX10", "PX3 to PX20", "PX3 to PX50" },
        "PX1 to PX10" => new[] { "PX10 to PX20", "PX10 to PX50" },
        "PX1 to PX20" => new[] { "PX20 to PX50" },
        "PX3 to PX10" => new[] { "PX10 to PX20", "PX10 to PX50" },
        _ => Array.Empty<string>()
    };
}
