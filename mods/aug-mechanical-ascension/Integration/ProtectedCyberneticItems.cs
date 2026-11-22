using HarmonyLib;
using AugMechanicalAscension.Domain;
namespace AugMechanicalAscension.Integration;

internal static class ProtectedCyberneticItems
{
    private static bool _installed;

    internal static bool Install(HarmonyLib.Harmony harmony, Action<string> log)
    {
        _installed = false;
        var guards = new HarmonyLib.Harmony("nicokobo.aug.protected-items");
        try
        {
            var list = typeof(System.Collections.Generic.List<GameItem>);
            (Type Type, string Method, Type[] Args, string Callback, bool Postfix)[] hooks =
            [
                (typeof(PlayerStore), nameof(PlayerStore.CanSellThisItem), [typeof(GameItem), typeof(bool)], nameof(CanSellPostfix), true),
                (typeof(PlayerStore), nameof(PlayerStore.SellItem), [typeof(GameItem)], nameof(SellPrefix), false),
                (typeof(NegociationUIManager), nameof(NegociationUIManager.SellMultiple), [list, typeof(bool)], nameof(SellMultiplePrefix), false),
                (typeof(PlayerStore), nameof(PlayerStore.StealItem), [typeof(GameItem)], nameof(TheftPrefix), false),
                (typeof(StealPixelElement), nameof(StealPixelElement.StealItem), [typeof(GameItem)], nameof(TheftBoolPrefix), false),
                // Bind the generated predicate at compile time so game updates cannot silently disable the story.
                (typeof(PlayerStore), nameof(PlayerStore.IsEligibleForRepo), [typeof(GameItem)], nameof(BurglaryPostfix), true),
                (typeof(GameItem), nameof(GameItem.CheckIsStateValid), [typeof(GameInventory), typeof(SlotMarker), typeof(GridShape), typeof(TagSystem)], nameof(TrashStatePrefix), false),
                (typeof(GameGridInventory), nameof(GameGridInventory.UncheckedAccept), [typeof(GameItem)], nameof(TrashGridPrefix), false),
                (typeof(GameSlotInventory), nameof(GameSlotInventory.UncheckedAccept), [typeof(GameItem)], nameof(TrashSlotPrefix), false),
                // GameItem.Destroy is abstract in IL2CPP despite its concrete-looking
                // generated wrapper. Recycling dispatches to this implementation.
                (typeof(GameItemElement), nameof(GameItemElement.Destroy), [], nameof(TrashDestroyPrefix), false)
            ];
            var resolved = hooks.Select(hook =>
            {
                var target = NativeHookSet.Find(hook.Type, hook.Method, hook.Args, declaredOnly:true);
                if (!OfficialHookApi.TryValidateNativeTarget(target, out var reason))
                    throw new InvalidOperationException(reason);
                var callback = new HarmonyMethod(AccessTools.DeclaredMethod(typeof(ProtectedCyberneticItems), hook.Callback)
                    ?? throw new MissingMethodException(nameof(ProtectedCyberneticItems), hook.Callback));
                return (Target: target, Callback: callback, hook.Postfix);
            }).ToArray(); // Preflight every native address before installing any guard.
            foreach (var hook in resolved)
                guards.Patch(hook.Target, prefix: hook.Postfix ? null : hook.Callback,
                    postfix: hook.Postfix ? hook.Callback : null);
            _installed = true;
            log("[P3] Protected item guards installed: sales, theft candidates, direct theft, trash insertion and recycling");
            return true;
        }
        catch (Exception ex)
        {
            guards.UnpatchSelf();
            log($"[ERROR] [P3] Protected item guards unavailable; {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void CanSellPostfix(GameItem gameItem, ref bool __result)
    {
        if (_installed && Protected(gameItem)) __result = false;
    }

    private static bool SellPrefix(GameItem item) => !_installed || !Protected(item);

    private static bool StoryItem(GameItem? item) => item != null && NativeObject.Handle(item) != IntPtr.Zero &&
        (item.identifier == StoryState.CoreId || StoryPhysicalItems.IsEvidence(item.identifier));

    // A bag carrying evidence must be protected along with its contents.
    private static bool ContainsStory(GameItem? item, bool includeNeural = false)
    {
        if (item == null || NativeObject.Handle(item) == IntPtr.Zero) return false;
        bool Matches(GameItem candidate) => StoryItem(candidate) ||
            includeNeural && candidate.identifier == NeuralModuleRules.ItemId;
        // Native traversal includes PixelWindow and intermediate layout nodes.
        // Casting only the item's immediate children to inventories skips bags.
        return Matches(item) || item.FindAllChildItems().Any(Matches);
    }

    private static bool Trash(GameInventory? inventory)
    {
        if (inventory == null) return false;
        var parent = inventory.GetParentItem();
        var entry = EmporiumEntry.Instance;
        return inventory.identifier == "trashcan" || entry != null &&
            (NativeObject.Handle(inventory) == NativeObject.Handle(entry.trashInvElement) || NativeObject.Handle(inventory) == NativeObject.Handle(entry.trashcanInvElement)) ||
            parent != null && (parent.identifier == "trashcan" || Trash(parent.parentInventory));
    }

    private static bool TheftPrefix(GameItem __0) => !_installed || !ContainsStory(__0);
    private static bool TheftBoolPrefix(GameItem __0, ref bool __result)
    { if (TheftPrefix(__0)) return true; __result = false; return false; }
    private static void BurglaryPostfix(GameItem __0, ref bool __result)
    { if (_installed && ContainsStory(__0)) __result = false; }
    private static bool TrashStatePrefix(GameItem __instance, GameInventory __0, ref bool __result)
    { if (!_installed || !Trash(__0) || !ContainsStory(__instance)) return true; __result = false; return false; }
    private static bool TrashGridPrefix(GameGridInventory __instance, GameItem __0, ref bool __result) =>
        TrashAccept(__instance, __0, ref __result);
    private static bool TrashSlotPrefix(GameSlotInventory __instance, GameItem __0, ref bool __result) =>
        TrashAccept(__instance, __0, ref __result);
    private static bool TrashAccept(GameInventory inventory, GameItem item, ref bool result)
    { if (!_installed || !Trash(inventory) || !ContainsStory(item)) return true; result = false; return false; }
    private static bool TrashDestroyPrefix(GameItem __instance) =>
        !_installed || !Trash(__instance.parentInventory) || !ContainsStory(__instance);

    private static void SellMultiplePrefix(
        System.Collections.Generic.List<GameItem> items)
    {
        if (!_installed || items == null) return;
        for (int index = items.Count - 1; index >= 0; index--)
            if (Protected(items[index])) items.RemoveAt(index);
    }

    private static bool Protected(GameItem? item)
    {
        try
        {
            return ContainsStory(item, includeNeural: true);
        }
        catch { return false; }
    }
}
