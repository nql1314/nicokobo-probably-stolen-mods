using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal static class LiquidFeeding
{
    internal static bool SameOutputQuality(GameItem source, GameItem target, bool allowContaminatedWater = false, bool exactComposition = false)
    {
        if (source.Handle() == target.Handle() || !NativeInventoryApi.IsPlayerOwned(target) ||
            target.parentInventory == null || target.parentInventory.IsInsertLocked()) return false;
        var from = NativeLiquidApi.Capture(source); var into = NativeLiquidApi.Capture(target);
        return from != null && into != null && from.TotalParts > 0 && into.TotalParts < into.CapacityParts &&
            WaterHelper.GetFilterItem(source) == null && WaterHelper.GetFilterItem(target) == null &&
            (into.TotalParts == 0 || (exactComposition ? NativeLiquidCompositionMath.SameSpecification(from, into) : CompatibleSources(source, target, allowContaminatedWater)));
    }

    private static bool CompatibleSources(GameItem source, GameItem target, bool allowContaminatedWater)
    {
        var from = NativeLiquidApi.Capture(source); var into = NativeLiquidApi.Capture(target);
        if (from == null || into == null || from.TotalParts <= 0 || into.TotalParts <= 0) return false;
        var a = from.Contents.Where(part => part.Parts > 0).ToDictionary(part => part.LiquidId, part => part.Parts);
        var b = into.Contents.Where(part => part.Parts > 0).ToDictionary(part => part.LiquidId, part => part.Parts);
        if (a.ContainsKey("water") || b.ContainsKey("water"))
        {
            int fromQuality = NativeLiquidApi.WaterQuality(source), intoQuality = NativeLiquidApi.WaterQuality(target);
            // Native's quality zero also covers Ghostwater, Rustwater and
            // Gutterflow. Compare their real native tiers when ejecting;
            // purifier raw-feed matching below still requires the exact mixture.
            if (!allowContaminatedWater && fromQuality == 0 && intoQuality == 0)
            {
                int grade = WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(source));
                return a.GetValueOrDefault("water") > 0 && b.GetValueOrDefault("water") > 0 && grade is >= 3 and <= 5 &&
                    grade == WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(target));
            }
            if (!allowContaminatedWater || fromQuality != 0 || intoQuality != 0)
                return LiquidRules.CompatibleWater(a, b, fromQuality, intoQuality);
        }
        if (!LiquidRules.SameMixture(a, b)) return false;
        foreach (var part in from.Contents.Where(part => part.Parts > 0))
        {
            var other = into.Contents.First(part2 => part2.LiquidId == part.LiquidId);
            if (part.Value.HasValue != other.Value.HasValue || part.QualityBasis.HasValue != other.QualityBasis.HasValue) return false;
            if (part.Value.HasValue && part.Value.Value * other.Parts != other.Value!.Value * part.Parts) return false;
            if (part.QualityBasis.HasValue && part.QualityBasis.Value * other.Parts != other.QualityBasis!.Value * part.Parts) return false;
        }
        return true;
    }

}
