using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class SynthesisIngredients
{
    internal static bool Matches(GameItem item, RecipeRequirement rule) =>
        (rule.Kind == RecipeResourceKind.TaggedItem ? item.IsTag(rule.ResourceId) : item.identifier == rule.ResourceId) &&
        // Empty, fermenting and finished wine share one native item ID.
        (rule.ResourceId != "wine_bottle" || item.GetTagReadonly("WINE_CONTAINER_STATE").GetInt() == 0 &&
            WaterHelper.GetTotalVolume(item) == 0 && item.FindAllChildItems().Count == 0) &&
        rule.Conditions.All(condition => condition.Property switch
        {
            RecipeProperty.MetalPurity => (int)IngotPurityRules.FromNativeTag(IngotPurityHelper.GetPurity(item)) >= condition.MinimumValue,
            RecipeProperty.ScavScannerLevel => item.identifier == "metal_scanner" && item.IsTag(ScavengingRobotRules.ScannerChanceTag) &&
                ScavengingRobotRules.IsValidScannerChance(item.GetTagReadonly(ScavengingRobotRules.ScannerChanceTag).valueInt) &&
                item.GetTagReadonly(ScavengingRobotRules.ScannerChanceTag).valueInt >= condition.MinimumValue,
            RecipeProperty.EmptyStorage => StorageShape.TryCapture(item, out _, out _) && item.FindAllChildItems().Count == 0,
            _ => false
        });

    internal static bool HasBlast(GameItem machine) =>
        MachineHelper.GetModuleInv(machine).childItems.Any(item => item.identifier == RefinerMetalRules.BlastModuleId);
}
