using System.Runtime.CompilerServices;
using HarmonyLib;
using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class CyberneticTrading
{
    private sealed class BudgetState { internal int Applied; }
    private static ConditionalWeakTable<StoreClient, BudgetState> Budgets = new();
    internal static void Reset() => Budgets = new();
    internal static bool Accepts(StoreClient client, GameItem item) => SynthesisMod.Active && client.isAug &&
        item.unitCount > 0 && item.IsGameItemType("CYBERNETIC") && !item.IsTag("NOT_FOR_RESALE");
    internal static void Budget(StoreClient client)
    {
        if (!SynthesisMod.Active || !client.isAug || !client.useClientBudget) return;
        int current = client.GetBudget();
        if (current <= 0) return;
        var state = Budgets.GetValue(client, _ => new());
        if (state.Applied == current) return;
        int updated = (int)Math.Min(int.MaxValue, (long)current * SynthesisNumbers.Effects.AugCustomerBudgetMultiplier);
        client.OverrideBudget(updated); state.Applied = updated;
    }
}

[HarmonyPatch(typeof(StoreClient), nameof(StoreClient.IsClientBuyingThisItem), new[] { typeof(GameItem) })]
internal static class AugBuyingCyberneticPatch
{
    private static bool Prefix(StoreClient __instance, GameItem __0, ref bool __result)
    { if (!CyberneticTrading.Accepts(__instance, __0)) return true; __result = true; return false; }
}

[HarmonyPatch]
internal static class AugAcceptingCyberneticPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(StoreClient), nameof(StoreClient.IsClientRefusingItem), new[] { typeof(GameItem), typeof(bool) });
        yield return AccessTools.Method(typeof(StoreClient), nameof(StoreClient.IsItemInVarietyList), new[] { typeof(GameItem) });
    }
    private static bool Prefix(StoreClient __instance, GameItem __0, ref bool __result)
    { if (!CyberneticTrading.Accepts(__instance, __0)) return true; __result = false; return false; }
}

[HarmonyPatch]
internal static class AugBudgetPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(StoreClient), nameof(StoreClient.SetBudget), new[] { typeof(int) });
        yield return AccessTools.Method(typeof(StoreClient), nameof(StoreClient.SetBudget), new[] { typeof(int), typeof(int) });
        yield return AccessTools.Method(typeof(StoreClient), nameof(StoreClient.SetClientBudget), new[] { typeof(int) });
        yield return AccessTools.Method(typeof(StoreClient), nameof(StoreClient.SetClientBudget), new[] { typeof(int), typeof(int) });
    }
    private static void Postfix(StoreClient __instance) => CyberneticTrading.Budget(__instance);
}

[HarmonyPatch(typeof(AugHelper), nameof(AugHelper.MakeDisguisedAug))]
internal static class AugDisguiseBudgetPatch
{
    private static void Prefix(StoreClient __0, out bool __state) => __state = !__0.isAug;
    private static void Postfix(StoreClient __0, bool __state) { if (__state) CyberneticTrading.Budget(__0); }
}

[HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeDisplayName))]
internal static class CyberneticTypeLabelPatch
{
    private static void Postfix(string __0, ref string __result)
    { if (SynthesisMod.Active && __0 == "CYBERNETIC") __result = SynthesisText.Get("synthesis.cybernetic"); }
}

[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.CanSellThisItem))]
internal static class CyberEyeCanSellPatch
{
    private static void Postfix(GameItem __0, ref bool __result)
    { if (SynthesisMod.Active && __0.identifier == SynthesisDefinitions.CyberEye.Id) __result = false; }
}

[HarmonyPatch]
internal static class CyberEyeSalePatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.SellItem), new[] { typeof(GameItem) });
        yield return AccessTools.Method(typeof(NegociationUIManager), nameof(NegociationUIManager.UpdateUI), new[] { typeof(GameItem) });
    }
    private static bool Prefix(GameItem __0)
    {
        if (!SynthesisMod.Active) return true;
        if (__0.identifier == SynthesisDefinitions.CyberEye.Id) return false;
        ManufacturedEffectsRuntime.RefreshSale(__0); return true;
    }
}

[HarmonyPatch(typeof(NegociationUIManager), nameof(NegociationUIManager.SellMultiple))]
internal static class CyberEyeBulkSalePatch
{
    private static void Prefix(ref List<GameItem> __0)
    { if (SynthesisMod.Active) __0 = __0.Where(item => item.identifier != SynthesisDefinitions.CyberEye.Id).ToList(); }
}
