using System;
using System.Collections.Generic;

namespace NicoItemManager
{
    internal enum SpawnDestination { DesktopFirst, Backpack }

    internal static class ItemSpawner
    {
        internal struct SpawnResult
        {
            internal bool Success;
            internal int SpawnedCount;
            internal string Message;
        }

        internal static bool IsStoreReady() => EmporiumEntry.Instance != null && EmporiumEntry.Instance.invElement != null;

        internal static GameItem CreateItem(string id, bool spawn = false)
        {
            // The native trashcan factory overwrites the store endpoint even
            // when creating a detached catalog/editor prototype or extra item.
            var store = EmporiumEntry.Instance;
            bool restoreTrash = id == "trashcan" && store != null;
            var originalTrash = restoreTrash ? store.trashcanInvElement : null;
            try { return spawn ? DirectoryMaster.Item(id, true) : DirectoryMaster.Create<GameItem>(id); }
            finally { if (restoreTrash) store.trashcanInvElement = originalTrash; }
        }

        internal static SpawnResult Spawn(string id, int count, IReadOnlyList<TagEdit> edits = null,
            Action<GameItem> configure = null, SpawnDestination destination = SpawnDestination.DesktopFirst)
        {
            if (string.IsNullOrWhiteSpace(id)) return Fail(Text.Get("invalid_item_id"));
            if (!IsStoreReady()) return Fail(Text.Get("enter_the_store_first"));
            if (!DirectoryMaster.Has<GameItem>(id.Trim())) return Fail(Text.Get("item_not_found", id));
            if (count < 1 || count > 100) return Fail(Text.Get("choose_1_100_spawns"));
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                SpawnResult one = SpawnOne(id.Trim(), edits, configure, destination);
                spawned += one.SpawnedCount;
                if (!one.Success)
                    return new SpawnResult { SpawnedCount = spawned,
                        Message = Text.Get("spawned_2", spawned, count, one.Message) };
            }
            return new SpawnResult { Success = true, SpawnedCount = spawned,
                Message = Text.Get("spawned", spawned, id) };
        }

        static SpawnResult SpawnOne(string id, IReadOnlyList<TagEdit> edits, Action<GameItem> configure,
            SpawnDestination destination)
        {
            GameItem item = null;
            SlotMarker receipt = null;
            GameItem stack = null;
            int units = 0, stackBefore = 0;
            bool delivered = false;
            try
            {
                item = CreateItem(id, spawn: true);
                if (item == null || string.IsNullOrWhiteSpace(item.identifier))
                    return Fail(Text.Get("item_not_found", id));
                InitializeFoodStamp(item);
                string tagError = ItemTags.ApplyTo(item, edits);
                if (tagError != null) return Fail(tagError);
                configure?.Invoke(item);
                units = item.unitCount;
                if (units <= 0) return Fail(Text.Get("the_item_factory_returned_an_invalid_quantity"));
                if (destination == SpawnDestination.DesktopFirst)
                    receipt = FindSlot(EmporiumEntry.Instance.backInvinvElement, item);
                if (receipt == null) receipt = FindSlot(EmporiumEntry.Instance.invElement, item);
                if (receipt == null)
                    return Fail(destination == SpawnDestination.DesktopFirst
                        ? Text.Get("neither_the_desktop_nor_the_backpack_has")
                        : Text.Get("the_backpack_has_no_room_for_the"));
                stack = receipt.targetItem;
                stackBefore = stack?.unitCount ?? 0;
                // In this release AcceptUnchecked does not merge stacks. TryAcceptOnce
                // handles both stacks and empty slots using the native receiving rules.
                receipt.TryAcceptOnce(units);
                delivered = WasDelivered(item, receipt, stack, stackBefore, units);
                if (!delivered) return Fail(Text.Get("inventory_did_not_confirm_receipt"));
                return new SpawnResult { Success = true, SpawnedCount = 1 };
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning($"Spawn '{id}': {error}");
                delivered = WasDelivered(item, receipt, stack, stackBefore, units);
                return new SpawnResult { SpawnedCount = delivered ? 1 : 0,
                    Message = Text.Get("spawn_failed", error.Message) };
            }
            finally
            {
                // A stack merge leaves an empty source instance; it also needs releasing.
                if (!delivered || stack != null) Release(item);
            }
        }

        static SlotMarker FindSlot(GameGridInventory inventory, GameItem item)
        {
            SlotMarker slot = inventory?.TryFindOneValidInventorySlot(item, false);
            return slot != null && slot.IsValid() && slot.numTransfer >= item.unitCount ? slot : null;
        }

        static bool WasDelivered(GameItem item, SlotMarker receipt, GameItem stack, int stackBefore, int units) =>
            receipt != null && units > 0 && (stack != null
                ? stack.unitCount - stackBefore >= units
                : receipt.inventory != null && ReferenceEquals(item?.parentInventory, receipt.inventory));

        internal static void InitializeFoodStamp(GameItem item)
        {
            // The base factory builds the stamp face only. Native PreBuiltItemHelper
            // adds this feature separately, and redemption dereferences it directly.
            if (item.identifier == "food_stamp" && item.FindItemFeatureByCategory("CATEGORY_GENUINE_STAMP") == null)
                item.AddItemFeature(ItemFeatureList.StampAuthenticity());
        }

        internal static bool IsAlive(GameItem item) =>
            item != null && (!(item is GameItemElement element) || !element.IsDestroyed());

        internal static void Release(GameItem item)
        {
            if (!IsAlive(item) || item.parentInventory != null) return;
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
        }

        static SpawnResult Fail(string message) => new SpawnResult { Message = message };
    }
}
