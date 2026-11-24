using DefinitelyStolen.Domain;
using HarmonyLib;

namespace DefinitelyStolen.Integration;

internal static class NativeHooks
{
    internal static Runtime.RunController? Run => Plugin.Active?.Enabled == true ? Plugin.Active.Run : null;

    internal static void Save(PlayerStore __instance) => Run?.BeforeNativeSave(__instance);
    internal static void Initial(PlayerStore __instance) => Run?.FinishInitial(__instance, true);

    internal static bool CanReturn(MapUIManager __instance) => !Locations.IsOwn(__instance.Current?.Id) || Run?.State?.Night?.Timing == null;
    internal static bool ReturnNow(MapUIManager __instance) => !Locations.IsOwn(__instance.Current?.Id) || Run?.PrepareLeave() != false;
    // MapUIManager clears Current before calling OnArriveStore. Its own leave
    // event carries the location identity and tells us to suppress the native
    // second charge for an outing already charged on the first theft attempt.
    internal static bool _theftReturn;
    internal static void Leaving(string id) { if (Locations.IsOwn(id)) _theftReturn = true; }
    internal static void Arrival(ref bool fromMap) { if (_theftReturn && Run != null) fromMap = true; }
    internal static Exception? Arrived(Exception? __exception) { _theftReturn = false; return __exception; }
    internal static void Reset() => _theftReturn = false;

    internal static void Thief(StoreClient __result)
    {
        if (__result == null || Run == null) return;
        __result.OnArrival += () => Run?.IntelligenceArrived();
        // Build 26337366 generates the thief's stock after the second greeting.
        __result.mainDialogue.nextDialogue.endAction += () =>
        {
            if (Run != null && RNG.GetRandomDouble(0, 1) < EscapeSmokeRules.StockChance)
                PlayerStore.Instance.AddDirectSellingItemToTable(EscapeSmokeRules.NativeItemId, false, true);
        };
    }

    internal static bool Accept(SlotMarker __instance, ref int __result)
    {
        var run = Run;
        if (run == null || run.Busy || !Native.Alive(__instance.item) || run.State?.Night is not { Started: true, Ended: false }) return true;
        var destination = __instance.inventory;
        if (run.CanMoveCustody(__instance.item, NativeCarry.InventoryParents(destination), NativeCarry.ContainsInventory(destination), __instance.targetItem == null)) return true;
        run.Notify(Native.Localize("tonight_s_loot_can_be_sorted_or"));
        __result = 0; return false;
    }
    internal static bool Use(GameItem __instance, ref bool __result)
    { var run = Run; if (run == null || run.Busy || !run.Custody(__instance)) return true; __result = false; return false; }
    internal static bool Target(GameItem __instance, GameItem __0, ref bool __result)
    {
        var run = Run;
        if (run == null || run.Busy || !run.Custody(__instance) && (!run.Custody(__0) || __instance.identifier is "magnifier" or "black_lamp")) return true;
        __result = false; return false;
    }
    internal static bool Stack(GameItem __instance, GameItem __0, ref bool __result)
    { var run = Run; if (run == null || run.Busy || !run.Custody(__instance) && !run.Custody(__0)) return true; __result = false; return false; }
    internal static void PerkLocked(StartingPerk perk, ref bool __result) { if (Run != null && Perks.MenuLocked(perk.id)) __result = true; }
    internal static void NormalizePerks() { if (Run != null) Perks.NormalizeMenu(); }
    internal static bool Remove(string __0) => Run == null || __0 != Identity.Streetwise || Native.Store == null || !Native.IsCareer(Native.Store);
    internal static void NoteName(GameItem __instance, ref string __result)
    {
        var run = Run; var note = run?.State?.Notes.FirstOrDefault(x => x.Paper?.UniqueId == __instance.uniqueId);
        if (note == null) return;
        string status = note.Invalidated ? Native.Localize("invalidated_by_debug_replacement") : note.Expired ? Native.Localize("expired")
            : run!.State?.Night is { } night && night.Day == note.Day && night.Npcs.Single(x => x.Key == note.Target).Items.All(x => x.UniqueId != note.ItemUniqueId)
                ? Native.Localize("already_taken") : Native.Localize("valid_tonight");
        __result += Native.Localize("day", note.Day, status);
    }
}

[HarmonyPatch(typeof(PlayerStore), "SaveGame")]
internal static class SavePatch
{
    private static void Prefix(PlayerStore __instance) => NativeHooks.Save(__instance);
}

[HarmonyPatch(typeof(PlayerStore), "InitialSave")]
internal static class InitialPatch
{
    private static void Prefix(PlayerStore __instance) { if (Plugin.Active?.Enabled == true) Plugin.Active.BeginNativeInitial(__instance); }
    private static void Postfix(PlayerStore __instance) => NativeHooks.Initial(__instance);
}

[HarmonyPatch(typeof(MapUIManager), "Return")]
internal static class CanReturnPatch
{
    private static bool Prefix(MapUIManager __instance) => NativeHooks.CanReturn(__instance);
}

[HarmonyPatch(typeof(MapUIManager), "ReturnNow")]
internal static class ReturnNowPatch
{
    private static bool Prefix(MapUIManager __instance) => NativeHooks.ReturnNow(__instance);
}

[HarmonyPatch(typeof(EmporiumEntry), "OnArriveStore")]
internal static class ArrivalPatch
{
    private static void Prefix(ref bool fromMap) => NativeHooks.Arrival(ref fromMap);
    private static Exception? Finalizer(Exception? __exception) => NativeHooks.Arrived(__exception);
}

[HarmonyPatch(typeof(StoreClientList), "CreateThief")]
internal static class ThiefPatch
{
    private static void Postfix(StoreClient __result) => NativeHooks.Thief(__result);
}

[HarmonyPatch(typeof(SlotMarker), "TryAcceptOnce", new[] { typeof(int) })]
internal static class AcceptPatch
{
    private static bool Prefix(SlotMarker __instance, ref int __result) => NativeHooks.Accept(__instance, ref __result);
}

[HarmonyPatch(typeof(GameItem), "MayStack", new[] { typeof(GameItem) })]
internal static class StackPatch
{
    private static bool Prefix(GameItem __instance, GameItem __0, ref bool __result) => NativeHooks.Stack(__instance, __0, ref __result);
}

[HarmonyPatch(typeof(NewGamePerksUI), "IsLocked", new[] { typeof(StartingPerk) })]
internal static class PerkLockedPatch
{
    private static void Postfix(StartingPerk perk, ref bool __result) => NativeHooks.PerkLocked(perk, ref __result);
}

[HarmonyPatch(typeof(NewGamePerksUI), "Rebuild")]
internal static class PerkChangedPatch
{
    private static void Prefix() => NativeHooks.NormalizePerks();
}

[HarmonyPatch(typeof(NewGamePerksUI), "Open")]
internal static class PerkOpenPatch
{
    private static void Prefix() => NativeHooks.NormalizePerks();
}

[HarmonyPatch(typeof(StartingPerk), "RemovePerk", new[] { typeof(string) })]
internal static class RemovePatch
{
    private static bool Prefix(string __0) => NativeHooks.Remove(__0);
}

[HarmonyPatch(typeof(GameItem), "GetDisplayName", new[] { typeof(bool) })]
internal static class NoteNamePatch
{
    private static void Postfix(GameItem __instance, ref string __result) => NativeHooks.NoteName(__instance, ref __result);
}

[HarmonyPatch(typeof(GameItem), "MayActivateSlotItem")]
internal static class MayActivateSlotItemPatch
{ private static bool Prefix(GameItem __instance, ref bool __result) => NativeHooks.Use(__instance, ref __result); }

[HarmonyPatch(typeof(GameItem), "CanActivateSlotItem")]
internal static class CanActivateSlotItemPatch
{ private static bool Prefix(GameItem __instance, ref bool __result) => NativeHooks.Use(__instance, ref __result); }

[HarmonyPatch(typeof(GameItem), "MaySelectSlotItem")]
internal static class MaySelectSlotItemPatch
{ private static bool Prefix(GameItem __instance, ref bool __result) => NativeHooks.Use(__instance, ref __result); }

[HarmonyPatch(typeof(GameItem), "MayToggleSlotItem")]
internal static class MayToggleSlotItemPatch
{ private static bool Prefix(GameItem __instance, ref bool __result) => NativeHooks.Use(__instance, ref __result); }

[HarmonyPatch(typeof(GameItem), "MayTarget", new[] { typeof(GameItem) })]
internal static class MayTargetPatch
{ private static bool Prefix(GameItem __instance, GameItem __0, ref bool __result) => NativeHooks.Target(__instance, __0, ref __result); }

[HarmonyPatch(typeof(GameItem), "CanTarget", new[] { typeof(GameItem) })]
internal static class CanTargetPatch
{ private static bool Prefix(GameItem __instance, GameItem __0, ref bool __result) => NativeHooks.Target(__instance, __0, ref __result); }
