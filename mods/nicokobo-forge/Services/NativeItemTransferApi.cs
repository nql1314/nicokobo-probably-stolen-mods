
namespace Nicokobo.ContentInterop;



/// <summary>One direct inventory move through the native slot acceptance path.
/// A rejected move restores its detached item/split to the original source.
/// No replacement factories or rollback of earlier independent moves.</summary>
public static class NativeItemTransferApi
{
    public static NativeItemTransferResult Move(GameItem item, GameGridInventory destination, int maximum)
        => MoveCore(item, destination, maximum, () => destination.TryFindOneValidInventorySlot(item, false));

    public static NativeItemTransferResult Move(GameItem item, GameSlotInventory destination, int maximum)
        => MoveCore(item, destination, maximum, () => destination.TryFindOneValidInventorySlot(item));

    private static NativeItemTransferResult MoveCore(GameItem item, GameInventory destination, int maximum,
        Func<SlotMarker?> findSlot)
    {
        if (maximum <= 0 ||
            item == null || item.Handle() == IntPtr.Zero || destination == null || destination.Handle() == IntPtr.Zero)
            return new(0, false, "unavailable");
        var source = item.parentInventory;
        if (source == null || source.Handle() == destination.Handle() || source.IsRemoveLocked() || NativeInventoryFreezeApi.IsFrozen(item) ||
            destination.IsInsertLocked() || !GeneralHelper.IsItemOwned(item) || !item.MayRemove()) return new(0, false, "locked-or-source");
        int before = item.unitCount;
        int requested = Math.Min(maximum, before);
        if (requested <= 0) return new(0, false, "empty");
        var slot = findSlot();
        if (slot == null || slot.Handle() == IntPtr.Zero || slot.item?.Handle() != item.Handle() ||
            slot.inventory?.Handle() != destination.Handle() || !slot.IsValid() || slot.numTransfer <= 0)
            return new(0, false, "no-slot");
        if (item.parentInventory?.Handle() != source.Handle() || item.unitCount != before)
            return new(0, false, "source-changed");
        requested = Math.Min(requested, slot.numTransfer);
        // Native acceptance can expel/split first and then reject or throw before
        // attachment. Keep the original placement and SlotMarker's split handle.
        string id = item.identifier;
        long sourceBefore = Count(source, id), targetBefore = Count(destination, id);
        var shape = item.modifiedShape;
        int reported = -1;
        string failure = "native-readback-mismatch";
        try
        {
            reported = slot.TryAcceptOnce(requested);
        }
        catch (Exception ex) { failure = ex.GetType().Name; }
        try
        {
            long removed = sourceBefore - Count(source, id), added = Count(destination, id) - targetBefore;
            // A callback may throw after attachment. Conserved, confirmed moves
            // have committed and must never be refunded or replayed.
            if (removed > 0 && removed <= requested && added == removed)
                return new((int)removed, false, reported == removed ? "moved" : "moved-readback");
            if (removed == 0 && added == 0)
            {
                if (item.parentInventory?.Handle() == source.Handle() && item.unitCount == before) item.modifiedShape = shape;
                return reported <= 0 ? new(0, false, "rejected") : new(0, true, failure);
            }
            if (added == 0 && removed > 0 && removed <= requested && slot.item is { } detached &&
                detached.parentInventory == null && detached.identifier == id && detached.unitCount == removed)
            {
                bool restored;
                if (detached.Handle() != item.Handle() && item.parentInventory?.Handle() == source.Handle() &&
                    item.unitCount == before - removed)
                {
                    // GetSplitItem copied the original stack. Recombine only this
                    // confirmed detached split, preserving the original identity.
                    item.SetUnitCount(before);
                    restored = item.unitCount == before;
                    if (restored) { item.modifiedShape = shape; detached.Destroy(); }
                }
                else if (detached.Handle() == item.Handle() && removed == before)
                {
                    detached.modifiedShape = shape;
                    RestoreDetached(source, detached);
                    restored = detached.parentInventory?.Handle() == source.Handle();
                }
                else restored = false;
                if (restored && Count(source, id) == sourceBefore && Count(destination, id) == targetBefore)
                    return new(0, false, "rejected-restored");
            }
            return new(0, true, failure);
        }
        catch (Exception ex) { return new(0, true, ex.GetType().Name); }
    }
    private static void RestoreDetached(GameInventory source, GameItem item)
    {
        // Restore custody into the vacated position. Player insertion callbacks
        // and admission locks must not veto recovery of the source's own item.
        // Preflight the original position before unchecked attachment; a native
        // callback may have changed source occupancy during the failed move.
        var admission = source.mayInventoryAddItemFunc;
        var onAdd = source.onSlotAddItemFunc;
        bool locked = source.overrideLockInsert;
        source.mayInventoryAddItemFunc = null;
        source.onSlotAddItemFunc = null;
        source.overrideLockInsert = false;
        try
        {
            var slot = source.TryInventorySlot(item, item.unitCount, item.modifiedShape);
            if (slot != null && slot.IsValid() && slot.targetItem == null && slot.numTransfer >= item.unitCount)
                source.UncheckedAccept(item);
        }
        finally
        {
            source.mayInventoryAddItemFunc = admission;
            source.onSlotAddItemFunc = onAdd;
            source.overrideLockInsert = locked;
        }
    }
    private static long Count(GameInventory inventory, string id)
    {
        long sum = 0;
        foreach (var child in inventory.childItems)
            if (child != null && child.identifier == id) sum = checked(sum + child.unitCount);
        return sum;
    }
}
