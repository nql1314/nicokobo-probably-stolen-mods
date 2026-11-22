using HarmonyLib;
using Nicokobo.FoodProcessing.Definitions;

namespace Nicokobo.FoodProcessing;

internal static class FoodRefrigeration
{
    internal static bool Chills(GameItem item) => item.identifier == FoodDefinitions.CaviarEelPlatter.Id ||
        item.identifier == FoodDefinitions.FishFiberSalad.Id;
    internal static bool Freezes(GameItem item) => item.identifier == FoodDefinitions.GalaxySundae.Id ||
        item.identifier == FoodDefinitions.SupplyJelly.Id || item.identifier == FoodDefinitions.RoeFiberPatty.Id;
}

// The Mono fridge calls these predicates from its normal cycle. Native power
// draw and chilled/frozen feature creation remain owned by MachineFridge.
[HarmonyPatch(typeof(MachineFridge), nameof(MachineFridge.IsChillable))]
internal static class ChillableFoodPatch
{
    private static void Postfix(GameItem item, ref bool __result)
    {
        if (!__result && item != null) __result = FoodRefrigeration.Chills(item);
    }
}

[HarmonyPatch(typeof(MachineFridge), nameof(MachineFridge.IsFreezable))]
internal static class FreezableFoodPatch
{
    private static void Postfix(GameItem item, ref bool __result)
    {
        if (!__result && item != null) __result = FoodRefrigeration.Freezes(item);
    }
}

[HarmonyPatch(typeof(MachineFridge), nameof(MachineFridge.MayZoneAcceptItem))]
internal static class RefrigeratedFoodAdmissionPatch
{
    private static void Postfix(GameItem item, ref bool __result)
    {
        if (!__result && item != null && GeneralHelper.IsItemOwned(item))
            __result = FoodRefrigeration.Chills(item) || FoodRefrigeration.Freezes(item);
    }
}
