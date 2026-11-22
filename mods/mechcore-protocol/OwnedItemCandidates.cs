using System.Reflection;
using HarmonyLib;

namespace MechcoreProtocol;

// Only stable item identities are indexed. Quantity and ownership are read live
// by each caller, so selling or consuming the last copy takes effect immediately.
internal sealed class OwnedItemCandidates(
    Func<PlayerStore, IEnumerable<GameItem>> capture, Func<GameItem, bool> relevant)
{
    private WeakReference<PlayerStore>? _store;
    private string? _run;
    private int _slot;
    private long _version = -1;
    private WeakReference<GameItem>[] _items = [];

    internal IEnumerable<GameItem> Read(PlayerStore store)
    {
        if (_version != ItemGraphChanges.Version || _store == null ||
            !_store.TryGetTarget(out var previous) || !ReferenceEquals(previous, store) ||
            _run != store.runID || _slot != store.saveSlotId)
        {
            // Include unowned candidates: a purchase may change ownership without
            // moving the item, and must not require another graph traversal.
            var items = capture(store).Where(relevant).Select(item => new WeakReference<GameItem>(item)).ToArray();
            _items = items;
            _store = new(store); _run = store.runID; _slot = store.saveSlotId;
            _version = ItemGraphChanges.Version;
        }
        foreach (var reference in _items)
            if (reference.TryGetTarget(out var item) && relevant(item)) yield return item;
    }

    internal void Clear()
    {
        _store = null; _run = null; _items = []; _version = -1;
    }
}

[HarmonyPatch]
internal static class ItemGraphChanges
{
    internal static long Version { get; private set; }
    internal static void Invalidate() => Version++;

    // These are the native attachment points for items, inventories and nested
    // windows. Invalidate after a real change, before slot callbacks can query effects.
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "AddParent", "RemoveParent" })
        {
            yield return AccessTools.DeclaredMethod(typeof(GameItem), name, [typeof(GraphNodeStorage)])
                ?? throw new MissingMethodException(nameof(GameItem), name);
            yield return AccessTools.DeclaredMethod(typeof(GameGridInventory), name, [typeof(GraphNodeStorage)])
                ?? throw new MissingMethodException(nameof(GameGridInventory), name);
            yield return AccessTools.DeclaredMethod(typeof(GameSlotInventory), name, [typeof(GraphNodeStorage)])
                ?? throw new MissingMethodException(nameof(GameSlotInventory), name);
            yield return AccessTools.DeclaredMethod(typeof(GameGridScrollableInventory), name, [typeof(GraphNodeStorage)])
                ?? throw new MissingMethodException(nameof(GameGridScrollableInventory), name);
            yield return AccessTools.DeclaredMethod(typeof(GameCharacterRaidInventory), name, [typeof(GraphNodeStorage)])
                ?? throw new MissingMethodException(nameof(GameCharacterRaidInventory), name);
            yield return AccessTools.DeclaredMethod(typeof(PixelWindow), name, [typeof(GraphNodeStorage)])
                ?? throw new MissingMethodException(nameof(PixelWindow), name);
        }
    }

    internal static void Postfix(bool __result) { if (__result) Invalidate(); }
}
