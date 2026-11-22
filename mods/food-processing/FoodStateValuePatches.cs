using HarmonyLib;
using System.Reflection;

namespace Nicokobo.FoodProcessing;

[HarmonyPatch]
internal static class RemainingFoodValuePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(GameItem), nameof(GameItem.GetCurrentValue)),
        AccessTools.Method(typeof(GameItem), nameof(GameItem.GetRefreshedValue)),
        AccessTools.Method(typeof(GameItem), nameof(GameItem.GetBaseValue))
    };
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(GameItem __instance) => FoodStateValue.Refresh(__instance);
}

[HarmonyPatch]
internal static class FoodUseTransferPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(HusbandryDirectory), "SerumGreen"),
        AccessTools.Method(typeof(HusbandryDirectory), "SerumRed")
    };
    private static void Postfix(GameItem __result)
    {
        var item = __result;
        var native = item.onThisTargetItemFunc;
        item.onThisTargetItemFunc = (source, target) =>
        {
            bool track = FoodStateValue.Supported(target) && (FoodStateValue.HasRate(source) || FoodStateValue.HasRate(target));
            int sourceBefore = FoodStateValue.Units(source), targetBefore = FoodStateValue.Units(target);
            var sourceBasis = track ? FoodStateValue.Read(source) : null;
            var targetBasis = track ? FoodStateValue.Read(target) : null;
            native?.Invoke(source, target);
            if (track) FoodStateValue.Transfer(source, target, sourceBefore, targetBefore, sourceBasis!, targetBasis!);
        };
    }
}

[HarmonyPatch]
internal static class FoodUseValuePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(UseCountHelper), nameof(UseCountHelper.Use)),
        AccessTools.Method(typeof(UseCountHelper), nameof(UseCountHelper.SetCurrentUseCount)),
        AccessTools.Method(typeof(UseCountHelper), nameof(UseCountHelper.ReplenishUseCount)),
        AccessTools.Method(typeof(UseCountHelper), nameof(UseCountHelper.ModMaxUseCount))
    };
    private static void Postfix(GameItem item) => FoodStateValue.Refresh(item);
}

[HarmonyPatch(typeof(IndustrialFishFeedHelper), "Split")]
internal static class FoodFeedSplitPatch
{
    internal static GameItem? Source;
    private static void Prefix(GameItem feed, out GameItem? __state) { __state = Source; Source = feed; }
    private static void Finalizer(GameItem? __state) => Source = __state;
}

[HarmonyPatch(typeof(IndustrialFishFeedHelper), nameof(IndustrialFishFeedHelper.SetKcal))]
internal static class FoodFeedValuePatch
{
    private static void Postfix(GameItem feed, int kcal)
    {
        if (kcal <= 0) return;
        var source = FoodFeedSplitPatch.Source;
        if (source != null && source != feed && FoodStateValue.HasRate(source))
            FoodStateValue.Write(feed, FoodStateValue.Read(source));
        else FoodStateValue.Refresh(feed);
    }
}

[HarmonyPatch(typeof(IndustrialFishFeedHelper), "Merge")]
internal static class FoodFeedMergePatch
{
    private static void Prefix(GameItem source, GameItem target, out (int Units, decimal Value)? __state)
    {
        __state = null;
        if ((!FoodStateValue.HasRate(source) && !FoodStateValue.HasRate(target)) || !IndustrialFishFeedHelper.CanMerge(source, target)) return;
        int sourceUnits = FoodStateValue.Units(source), targetUnits = FoodStateValue.Units(target);
        if (sourceUnits > 0) __state = (sourceUnits + targetUnits, FoodStateValue.Read(source).ForUnits(sourceUnits) + FoodStateValue.Read(target).ForUnits(targetUnits));
    }
    private static void Postfix(GameItem target, (int Units, decimal Value)? __state)
    {
        if (__state is { } state && FoodStateValue.Units(target) == state.Units)
            FoodStateValue.Write(target, new(state.Value, state.Units));
    }
}
