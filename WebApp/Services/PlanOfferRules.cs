using ColdCall.Models;

namespace ColdCall.Services;

public static class PlanOfferRules
{
    public const string HighestPlanCode = "PX100";

    public static string EffectiveCode(string? code)
    {
        var value = (code ?? "").Trim();
        var separator = value.LastIndexOf(" to ", StringComparison.OrdinalIgnoreCase);
        return (separator < 0 ? value : value[(separator + 4)..]).Trim().ToUpperInvariant();
    }

    public static string[] UpgradeCodesFor(string? currentPlanCode) => EffectiveCode(currentPlanCode) switch
    {
        "PX1" => new[] { "PX1 to PX3", "PX1 to PX10", "PX1 to PX20", "PX1 to PX50", "PX1 to PX100" },
        "PX3" => new[] { "PX3 to PX10", "PX3 to PX20", "PX3 to PX50", "PX3 to PX100" },
        "PX10" => new[] { "PX10 to PX20", "PX10 to PX50", "PX10 to PX100" },
        "PX20" => new[] { "PX20 to PX50", "PX20 to PX100" },
        "PX50" => new[] { "PX50 to PX100" },
        _ => Array.Empty<string>()
    };

    public static bool CanPurchase(PlanDefinition? current, PlanDefinition target)
    {
        if (!target.IsActive || !target.ShowInOffer || target.IsTrial) return false;
        if (current == null || current.IsTrial) return !target.IsUpgrade;
        if (current.Id == target.Id || EffectiveCode(current.Code) == HighestPlanCode) return false;
        var codes = UpgradeCodesFor(current.Code);
        if (codes.Length > 0)
            return target.IsUpgrade && codes.Contains(target.Code.Trim(), StringComparer.OrdinalIgnoreCase);
        return target.IsUpgrade
            && target.UpgradeFrom == (current.IsUpgrade ? current.UpgradeFrom : current.Id)
            && target.MaxNumberOfSearches > current.MaxNumberOfSearches;
    }
}
