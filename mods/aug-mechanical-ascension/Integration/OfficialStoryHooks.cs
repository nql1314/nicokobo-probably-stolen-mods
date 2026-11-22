namespace AugMechanicalAscension.Integration;
internal static class OfficialStoryTransfer
{
    internal sealed record Result(int Moved, bool Indeterminate, string Status);

    internal static Result Move(GameItem item, GameInventory target, int amount)
    {
        if (item.unitCount != 1 || amount != 1) return new(0, false, "Whole original evidence required");
        if (ReferenceEquals(item.parentInventory, target)) return new(1, false, "Already placed");
        var source = item.parentInventory;
        var sourceShape = new GridShapeBuilder(item.modifiedShape).Clone();
        bool changed = false;
        try
        {
            var slot = target.TryInventorySlot(item, amount, item.modifiedShape);
            if (slot == null || !slot.IsValid() || slot.targetItem != null || slot.numTransfer != 1)
                return new(0, false, "No whole-item space");
            changed = true;
            int moved = slot.TryAcceptOnce(amount);
            bool placed = ReferenceEquals(item.parentInventory, target) && target.childItems.Contains(item) && item.unitCount == 1;
            if (moved == 1 && placed) return new(1, false, "Placed and verified");
            if (ReferenceEquals(item.parentInventory, source) && item.unitCount == 1)
                return new(0, false, "Source retained");
            if (item.parentInventory == null && source != null)
            {
                var restore = source.TryInventorySlot(item, 1, sourceShape);
                if (restore != null && restore.targetItem == null && restore.IsValid() && restore.TryAcceptOnce(1) == 1 &&
                    ReferenceEquals(item.parentInventory, source) && source.childItems.Contains(item))
                    return new(0, false, "Source restored and verified");
            }
            return new(0, true, "Placement outcome could not be verified");
        }
        catch (Exception error)
        {
            bool retained = ReferenceEquals(item.parentInventory, source) && item.unitCount == 1;
            return new(0, changed && !retained, error.Message);
        }
    }
}

internal static class OfficialStoryGame { internal static PlayerStore? Store => PlayerStore.HasInstance ? PlayerStore.Instance : null; }
