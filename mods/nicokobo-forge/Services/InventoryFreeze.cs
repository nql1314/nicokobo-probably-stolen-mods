using HarmonyLib;
namespace Nicokobo.ContentInterop;
internal static class NativeInventoryFreezeApi
{
    private static readonly Dictionary<GameItem, (string? Group, int Count)> Frozen = new();
    internal static bool IsFrozen(GameItem item) => Frozen.ContainsKey(item);
    internal static IDisposable? TryAcquire(IEnumerable<GameItem> items, string? group = null)
    {
        var snapshot = items.Distinct().ToArray();
        if (snapshot.Any(item => item == null || Frozen.TryGetValue(item, out var held) && (group == null || held.Group != group))) return null;
        foreach (var item in snapshot) Frozen[item] = Frozen.TryGetValue(item, out var held) ? (held.Group, checked(held.Count + 1)) : (group, 1);
        return new CallbackLease(() => { foreach (var item in snapshot) { var held = Frozen[item]; if (held.Count == 1) Frozen.Remove(item); else Frozen[item] = (held.Group, held.Count - 1); } });
    }
}
[HarmonyPatch(typeof(GameItem), nameof(GameItem.MayRemove))]
internal static class FreezeRemovePatch
{
    private static bool Prefix(GameItem __instance, ref bool __result)
    { if (!NativeInventoryFreezeApi.IsFrozen(__instance)) return true; __result = false; return false; }
}

[HarmonyPatch]
internal static class FreezeExpelPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        new[] {
            AccessTools.Method(typeof(GameGridInventory), nameof(GameGridInventory.Expel), new[] { typeof(GameItem) }),
            AccessTools.Method(typeof(GameGridScrollableInventory), nameof(GameGridScrollableInventory.Expel), new[] { typeof(GameItem) }),
            AccessTools.Method(typeof(GameSlotInventory), nameof(GameSlotInventory.Expel), new[] { typeof(GameItem) }) }.Distinct();
    private static bool Prefix(GameItem __0, ref bool __result)
    { if (!NativeInventoryFreezeApi.IsFrozen(__0)) return true; __result = false; return false; }
}
