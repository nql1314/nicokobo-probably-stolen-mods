namespace AugMechanicalAscension.Integration;

internal static class WorkshopAchievementDelivery
{
    internal static bool Apply(GameGridInventory inventory, IReadOnlyList<OfficialInventoryPlacement> placements)
    {
        if (inventory == null || NativeObject.Handle(inventory) == IntPtr.Zero || placements == null)
            return false;
        var seen = new HashSet<IntPtr>();
        foreach (var placement in placements)
            if (placement?.Item == null || NativeObject.Handle(placement.Item) == IntPtr.Zero ||
                placement.Shape == null || !seen.Add(NativeObject.Handle(placement.Item)) ||
                placement.Item.parentInventory != null || placement.Item.unitCount != 1)
                return false;
        foreach (var placement in placements)
        {
            if (placement.Item.parentInventory != null || placement.Item.unitCount != 1)
                return false;
            var slot = inventory.TryInventorySlot(placement.Item, 1, placement.Shape);
            if (slot == null || NativeObject.Handle(slot) == IntPtr.Zero ||
                NativeObject.Handle(slot.item) != NativeObject.Handle(placement.Item) || NativeObject.Handle(slot.inventory) != NativeObject.Handle(inventory) ||
                slot.targetItem != null || slot.numTransfer != 1 || !slot.IsValid() ||
                placement.Item.parentInventory != null || placement.Item.unitCount != 1 ||
                NativeObject.Handle(slot.item) != NativeObject.Handle(placement.Item) || NativeObject.Handle(slot.inventory) != NativeObject.Handle(inventory) ||
                slot.targetItem != null || slot.numTransfer != 1 || slot.TryAcceptOnce(1) != 1 ||
                NativeObject.Handle(placement.Item.parentInventory) != NativeObject.Handle(inventory) || placement.Item.unitCount != 1)
                return false;
        }
        return placements.All(x => NativeObject.Handle(x.Item.parentInventory) == NativeObject.Handle(inventory) && OfficialInventoryApi.IsPlayerOwned(x.Item));
    }

    internal static bool Recall(IReadOnlyList<GameItem> products)
    {
        bool restored = true;
        foreach (var item in products)
            try
            {
                var parent = item.parentInventory;
                if (parent != null)
                {
                    bool locked = parent.overrideLockRemove; parent.overrideLockRemove = true;
                    try { if (!parent.Expel(item)) { restored = false; continue; } }
                    finally { parent.overrideLockRemove = locked; }
                }
                if (item.parentInventory != null) { restored = false; continue; }
                Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
            }
            catch { restored = false; }
        return restored;
    }
}
