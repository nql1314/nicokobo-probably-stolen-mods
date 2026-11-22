using HarmonyLib;
using System.Reflection;
namespace Nicokobo.ContentInterop;
internal static class NativeInventoryAdmissionApi
{
    private static readonly Dictionary<(string Device, string Inventory), (string Owner, Func<GameItem, GameGridInventory, bool> Predicate)> Rules = new();
    internal static IDisposable Register(string owner, string device, string inventory, Func<GameItem, GameGridInventory, bool> predicate)
    { ContentRuntime.RequireOwner(owner); Rules.Add((device, inventory), (owner, predicate)); return new CallbackLease(() => Rules.Remove((device, inventory))); }
    internal static void Remove(string owner)
    { foreach (var key in Rules.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray()) Rules.Remove(key); }
    internal static bool Allows(GameInventory inventory, GameItem item) => inventory is not GameGridInventory grid ||
        inventory.GetParentItem() is not { } parent || !Rules.TryGetValue((parent.identifier, inventory.identifier), out var rule) || rule.Predicate(item, grid);
}
[HarmonyPatch]
internal static class AdmissionPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] {
        AccessTools.Method(typeof(GameGridInventory), nameof(GameGridInventory.MayHaveValidInventorySlot), new[] { typeof(GameItem) }),
        AccessTools.Method(typeof(GameGridScrollableInventory), nameof(GameGridScrollableInventory.MayHaveValidInventorySlot), new[] { typeof(GameItem) }),
        AccessTools.Method(typeof(GameSlotInventory), nameof(GameSlotInventory.MayHaveValidInventorySlot), new[] { typeof(GameItem) }) }.Distinct();
    private static void Postfix(GameInventory __instance, GameItem __0, ref bool __result)
    { if (__result) __result = NativeInventoryAdmissionApi.Allows(__instance, __0); }
}
