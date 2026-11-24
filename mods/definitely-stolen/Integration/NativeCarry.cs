using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;
using Nicokobo.ContentInterop;
using Nicokobo.ModUtilities;

namespace DefinitelyStolen.Integration;

internal static class NativeCarry
{
    internal static IReadOnlyList<(string Key, GameInventory Inventory)> Inventories()
    {
        var entry = EmporiumEntry.Instance;
        if (entry == null) throw new InvalidOperationException("Carry inventories unavailable");
        return new (string, GameInventory)[] { ("backpack", entry.afterhourPocketSlotInvBackpack), ("left", entry.afterhourPocketSlotInvLeft),
            ("right", entry.afterhourPocketSlotInvRight), ("grid", entry.afterhourInventory) };
    }
    internal static IReadOnlyList<GameItem> Items() => Inventories().SelectMany(x => x.Inventory.childItems.ToArray()).Distinct().ToArray();
    internal static IReadOnlyList<GameItem> EquippedItems() => Inventories().Where(x => x.Key != "grid")
        .SelectMany(x => x.Inventory.childItems.ToArray()).Distinct().ToArray();
    internal static GameItem? FindEscapeSmoke() => Items().SelectMany(NativeGraph.Items)
        .FirstOrDefault(item => EscapeSmokeRules.IsEscapeSmoke(item.identifier) && item.unitCount == 1);
    internal static void DetachEscapeSmoke(GameItem item)
    {
        if (!EscapeSmokeRules.IsEscapeSmoke(item.identifier) || item.unitCount != 1 || !ContainsInventory(item.parentInventory))
            throw new InvalidOperationException("Escape smoke is no longer in carry");
        NativeItems.Detach(item);
    }
    // Run capture can include objects without a persistent ID. They cannot
    // match saved custody; duplicate positive IDs must still be rejected.
    internal static Dictionary<int, GameItem> IndexIdentifiedItems(IEnumerable<GameItem> items) =>
        items.Where(x => x.uniqueId > 0).ToDictionary(x => x.uniqueId);
    internal static IEnumerable<int> InventoryParents(GameInventory? inventory)
    {
        // Native container grids have an intermediate GraphNodeStorage parent.
        // GetParentItem resolves that node to the actual owning item.
        while (inventory?.GetParentItem() is { } item)
        {
            yield return item.uniqueId;
            inventory = item.parentInventory;
        }
    }
    internal static bool ContainsInventory(GameInventory? inventory)
    {
        var roots = Inventories();
        while (inventory != null)
        {
            if (roots.Any(x => ReferenceEquals(x.Inventory, inventory))) return true;
            inventory = inventory.GetParentItem()?.parentInventory;
        }
        return false;
    }
    internal static List<CarryState> Snapshot(Action<string>? warn = null)
    {
        var saved = new List<CarryState>();
        foreach (var pair in Inventories())
            foreach (var item in pair.Inventory.childItems.ToArray())
            {
                try
                {
                    saved.Add(new CarryState { UniqueId = item.GetUniqueID(), Graph = NativeGraph.Encode(item), Inventory = pair.Key });
                }
                catch (Exception ex) when (warn != null) { warn($"Could not capture carry item {item.uniqueId}: {ex.Message}"); }
            }
        return saved.DistinctBy(x => x.UniqueId).ToList();
    }
    internal static Action CaptureRollback()
    {
        // Keep live handles, whole stacks and exact slots. Native returning and
        // going outside are not inverse inventory transfers.
        var saved = Inventories().SelectMany(pair => pair.Inventory.childItems.ToArray().Select(item =>
            (Item: item, Inventory: pair.Inventory, Shape: item.modifiedShape.Clone(), Units: item.unitCount,
                Nodes: GraphIdentity.Nodes(NativeGraph.Encode(item))))).ToArray();
        var nested = saved.SelectMany(root => NativeGraph.Items(root.Item)
            .Where(item => item.uniqueId != root.Item.uniqueId && item.parentInventory != null)
            .Select(item => (Item: item, Inventory: item.parentInventory!, Shape: item.modifiedShape.Clone(), Units: item.unitCount))).ToArray();
        return () =>
        {
            // Loose goods may have been moved into the backpack by returning.
            // Detach every original root before restoring the backpack's shape.
            foreach (var entry in saved.AsEnumerable().Reverse())
            {
                if (!Native.Alive(entry.Item)) throw new InvalidDataException("Rollback carry item no longer exists");
                NativeItems.Detach(entry.Item);
            }
            // Capture removes stolen roots even when they were sorted into a
            // personal bag. Reattach these before validating that bag's graph.
            foreach (var entry in nested)
            {
                if (!Native.Alive(entry.Item)) throw new InvalidDataException("Rollback nested item no longer exists");
                if (ReferenceEquals(entry.Item.parentInventory, entry.Inventory)) continue;
                NativeItems.Detach(entry.Item);
                RestorePlacement(entry.Item, entry.Inventory, entry.Shape, entry.Units);
            }
            foreach (var entry in saved)
            {
                var nodes = GraphIdentity.Nodes(NativeGraph.Encode(entry.Item));
                if (nodes.Count != entry.Nodes.Count || nodes.Any(x => !entry.Nodes.TryGetValue(x.Key, out var old) || old != x.Value))
                    throw new InvalidDataException("Rollback carry graph changed");
                RestorePlacement(entry.Item, entry.Inventory, entry.Shape, entry.Units);
            }
        };
    }
    private static void RestorePlacement(GameItem item, GameInventory inventory, GridShape shape, int units)
    {
        item.modifiedShape = shape.Clone();
        var slot = inventory.TryInventorySlot(item, units, shape.Clone());
        if (slot == null || !slot.IsValid() || slot.targetItem != null || slot.numTransfer != units ||
            slot.TryAcceptOnce(units) != units || !ReferenceEquals(item.parentInventory, inventory) || item.unitCount != units)
            throw new InvalidOperationException("Original carry placement could not be restored");
    }
    internal static void Restore(IReadOnlyList<CarryState> carry, PlayerStore store)
    {
        var live = NativeGraph.RunItems(store).Concat(Items().SelectMany(NativeGraph.Items)).Distinct().ToArray();
        var ids = live.Where(x => x.uniqueId > 0).Select(x => x.uniqueId).ToHashSet();
        var inserted = new List<GameItem>();
        try
        {
            // The native backpack pocket callback establishes the carry grid's
            // shape. Restore it before placing loose goods into that grid.
            foreach (var pair in Inventories())
                foreach (var saved in carry.Where(x => x.Inventory == pair.Key))
                {
                    var nodes = GraphIdentity.Nodes(saved.Graph); var graphIds = nodes.Keys.ToHashSet();
                    if (ids.Contains(saved.UniqueId))
                    {
                        var existing = live.Single(x => x.uniqueId == saved.UniqueId);
                        var contents = NativeGraph.Items(existing);
                        if (existing.parentInventory == null || !ReferenceEquals(existing.parentInventory, pair.Inventory) ||
                            !graphIds.SetEquals(contents.Select(x => x.uniqueId)) ||
                            contents.Any(x => nodes[x.uniqueId] != (x.identifier, x.unitCount)))
                            throw new InvalidDataException("Partial/conflicting carry graph or wrong saved inventory");
                        continue;
                    }
                    var item = NativeGraph.Decode(saved.Graph, ids); inserted.Add(item);
                    // A saved carry graph already has an exact native placement.
                    // Reward planning would repack it and assumes a single unit.
                    RestorePlacement(item, pair.Inventory, item.modifiedShape, item.unitCount);
                    ids.UnionWith(graphIds);
                }
        }
        catch { foreach (var item in inserted.AsEnumerable().Reverse()) { NativeItems.Detach(item); ItemGraphCleanup.Destroy(item); } throw; }
    }

    internal static void RecoverToStore(List<CarryState> pending, PlayerStore store, Action<string> warn)
    {
        var entry = EmporiumEntry.Instance;
        var live = NativeGraph.RunItems(store).Where(item => item.uniqueId > 0).ToLookup(item => item.uniqueId);
        var ids = live.Select(group => group.Key).ToHashSet();
        foreach (var saved in pending.ToArray())
        {
            GameItem? item = null;
            IReadOnlyDictionary<int, (string Id, int Units)>? nodes = null;
            try
            {
                nodes = GraphIdentity.Nodes(saved.Graph);
                if (!nodes.ContainsKey(saved.UniqueId)) throw new InvalidDataException("Missing recovery carry root");
                int highest = nodes.Keys.Max();
                if (highest == int.MaxValue) throw new InvalidDataException("Carry UID leaves no room for native allocation");
                store.currentUniqueId = Math.Max(store.currentUniqueId, highest + 1);
                if (nodes.Keys.Any(ids.Contains))
                {
                    var existing = live[saved.UniqueId].Take(2).ToArray();
                    if (existing.Length == 1 && existing[0].identifier == nodes[saved.UniqueId].Id)
                        existing[0].state.DisableTag(Identity.CustodyTag);
                    else
                        warn($"Reset carry record {saved.UniqueId} because its UID overlaps live inventory");
                    pending.Remove(saved);
                    continue;
                }

                item = NativeGraph.Decode(saved.Graph, ids);
                if (item.uniqueId != saved.UniqueId) throw new InvalidDataException("Recovery carry root changed");
                foreach (var grid in new[] { entry.backInvinvElement, entry.invElement, entry.showcaseElement })
                {
                    if (InventoryPlacement.PlanWholeStacks(grid, new[] { item }) is not { Count: 1 } plan) continue;
                    RestorePlacement(item, grid, plan[0].Shape, item.unitCount);
                    item.state.DisableTag(Identity.CustodyTag);
                    pending.Remove(saved);
                    break;
                }
            }
            catch (Exception ex) { warn($"Could not restore carry item {saved.UniqueId}: {ex.Message}"); }
            finally
            {
                if (Native.Alive(item))
                {
                    if (item!.parentInventory == null) ItemGraphCleanup.Destroy(item);
                    else ids.UnionWith(nodes!.Keys);
                }
            }
        }
    }
}
