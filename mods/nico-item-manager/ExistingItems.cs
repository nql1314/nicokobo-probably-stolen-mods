using System;

namespace NicoItemManager
{
    internal static class ExistingItems
    {
        internal static string UnavailableReason(GameItem item)
        {
            if (!ItemSpawner.IsStoreReady() || !ItemSpawner.IsAlive(item) || item.unitCount <= 0)
                return Text.Get("this_item_is_no_longer_available");
            if (!GeneralHelper.IsItemOwned(item))
                return Text.Get("only_your_own_items_can_be_managed");
            var store = EmporiumEntry.Instance;
            if (item.parentInventory == null || !ReferenceEquals(item.parentInventory, store.invElement)
                && !ReferenceEquals(item.parentInventory, store.backInvinvElement))
                return Text.Get("move_the_item_to_your_backpack_or");
            if (GeneralHelper.IsItemLocked(item) || !item.MayRemove())
                return Text.Get("this_item_is_locked_and_cannot_be");
            return null;
        }

        internal static GameItem RequireEditable(GameItem item)
        {
            string reason = UnavailableReason(item);
            if (reason != null) throw new InvalidOperationException(reason);
            return item;
        }

        internal static ItemSpawner.SpawnResult Copy(GameItem item, int amount,
            SpawnDestination destination = SpawnDestination.DesktopFirst)
        {
            RequireEditable(item);
            if (amount < 1 || amount > item.unitCount)
                throw new InvalidOperationException(Text.Get("choose_1_items_to_copy", item.unitCount));
            if (item.FindAllChildItems().Count > 0)
                throw new InvalidOperationException(Text.Get("empty_the_container_before_copying_it"));

            var result = ItemSpawner.Spawn(item.identifier, 1, configure: copy =>
            {
                ItemEditor.CopyState(item, copy);
                copy.unitCount = amount;
            }, destination: destination);
            if (result.Success)
                result.Message = Text.Get("copied", item.GetDisplayName(), amount);
            OverlayHandler.current?.UpdateInventoryStateUI();
            return result;
        }

        internal static void Delete(GameItem item, int amount)
        {
            RequireEditable(item);
            if (amount < 1 || amount > item.unitCount)
                throw new InvalidOperationException(Text.Get("choose_1_items_to_delete", item.unitCount));
            if (item.FindAllChildItems().Count > 0)
                throw new InvalidOperationException(Text.Get("empty_the_container_before_deleting_it"));
            if (item.MaxNumRemove() < amount)
                throw new InvalidOperationException(Text.Get("the_inventory_does_not_allow_removing_that"));

            GameInventory inventory = item.parentInventory;
            if (amount == item.unitCount)
            {
                // Expel checks and invokes the native removal callbacks before we
                // release the item and its exclusive, already-empty windows.
                if (!inventory.Expel(item) || item.parentInventory != null)
                    throw new InvalidOperationException(Text.Get("the_inventory_did_not_confirm_removal"));
                Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
            }
            else
            {
                item.SetUnitCount(item.unitCount - amount);
                item.onChangeAmountItemFunc?.Invoke(item);
                SlotMarker slot = inventory.GetIncompleteItemInventorySlot(item);
                item.onSlotChangeAmountItemFunc?.Invoke(item, inventory, slot);
                inventory.onSlotChangeAmountItemFunc?.Invoke(item, inventory, slot);
                inventory.Validate();
            }
            OverlayHandler.current?.UpdateInventoryStateUI();
        }
    }
}
