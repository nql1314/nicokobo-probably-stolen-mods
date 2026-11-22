
namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private bool GatherStorageAccessible(GameItem item)
    {
        var seen = new HashSet<IntPtr>();
        for (GameItem? current = item; current != null; current = current.parentInventory?.GetParentItem())
        {
            if (current.Handle() == IntPtr.Zero || !seen.Add(current.Handle()) || !NativeInventoryApi.IsPlayerOwned(current) ||
                NativeInventoryFreezeApi.IsFrozen(current)) return false;
            if (current.parentInventory is not { } parent) return false;
            if (parent.IsRemoveLocked() || !current.MayRemove()) return false;
            if (parent.GetParentItem() is { } container &&
                (container.IsGameItemType("MACHINE") || container.identifier == Content.PowerDevice)) return false;
        }
        return true;
    }

    private bool GatherStorageAvailable(GameGridInventory grid) => Ready && IsCurrentRun() &&
        !NativeInventoryApi.IsItemDragActive && grid != null && grid.Handle() != IntPtr.Zero && !grid.IsInsertLocked() &&
        grid.identifier == Content.ContentsId && grid.GetParentItem() is { } root && root.identifier == Content.ResourceDevice &&
        Grids(root).Any(candidate => candidate.Handle() == grid.Handle()) && GatherStorageAccessible(root);

    internal NativeItemTransferResult GatherResourceStorage(GameGridInventory grid)
    {
        if (_resourceTransfers != 0 || !GatherStorageAvailable(grid)) return new(0, false, "storage-unavailable");
        int generation = _resourceTransferGeneration, moved = 0;
        var store = PlayerStore.Instance!.Handle();
        var targetRoot = grid.GetParentItem()!;
        _resourceTransfers++;
        try
        {
            // Capture the seed IDs once. Variants keep their real native state
            // and only the inventory the player clicked is a destination.
            var identifiers = grid.childItems.ToArray().Where(item => item != null && item.unitCount > 0 && item.parentInventory?.Handle() == grid.Handle())
                .Select(item => item.identifier).ToHashSet(StringComparer.Ordinal);
            if (identifiers.Count == 0) return new(0, false, "storage-empty");
            var warehouse = Warehouse();
            var sources = Items().Where(item => identifiers.Contains(item.identifier) && item.parentInventory?.Handle() != grid.Handle())
                .DistinctBy(item => item.Handle()).OrderBy(item => item.GetUniqueID()).ToArray();
            foreach (var item in sources)
            {
                var parent = item.parentInventory?.TryCast<GameGridInventory>();
                if (parent == null || parent.Handle() == grid.Handle() ||
                    parent.GetParentItem() == null && parent.Handle() != warehouse?.Handle()) continue;
                string identifier = item.identifier;
                while (item.Handle() != IntPtr.Zero && item.unitCount > 0 && item.parentInventory?.Handle() == parent.Handle())
                {
                    if (generation != _resourceTransferGeneration || PlayerStore.Instance?.Handle() != store ||
                        grid.GetParentItem()?.Handle() != targetRoot.Handle() || !GatherStorageAvailable(grid))
                        return new(moved, false, "storage-condition-changed");
                    if (item.identifier != identifier || !identifiers.Contains(identifier) || !GatherStorageAccessible(item) ||
                        !NetworkStorage.IsResourceItem(item)) break;
                    int request = Math.Min(item.unitCount, item.MaxNumRemove());
                    if (request <= 0) break;
                    _resourceTransferItems.Clear(); _resourceTransferContainers.Clear();
                    _resourceTransferItems.Add(item.GetUniqueID());
                    _resourceTransferContainers.Add(targetRoot.GetUniqueID());
                    if (parent.GetParentItem() is { } sourceRoot) _resourceTransferContainers.Add(sourceRoot.GetUniqueID());
                    var result = NativeItemTransferApi.Move(item, grid, request);
                    moved = checked(moved + result.Moved);
                    if (result.Indeterminate)
                    {
                        return new(moved, true, result.Status);
                    }
                    // A rejected shape must not block smaller matching items.
                    // Native slots also allow merging into otherwise full grids.
                    if (result.Moved == 0) break;
                }
            }
            return new(moved, false, moved > 0 ? "gathered" : "no-matching-items-or-space");
        }
        finally
        {
            EndResourceTransfer(generation);
            if (generation == _resourceTransferGeneration && PlayerStore.Instance?.Handle() == store && moved > 0) NetworkPresentationChanged?.Invoke();
        }
    }
}
