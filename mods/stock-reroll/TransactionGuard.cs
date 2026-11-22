using System;
using System.Collections.Generic;
using HarmonyLib;

namespace StockReroll
{
    internal static class TransactionGuard
    {
        static StoreClientInstance observedCustomer;
        static bool customerTransaction;
        static bool nightShopTransaction;

        internal static bool IsNightShop(AfterhourLocation location) =>
            location != null && (location.Id == AfterhourLocation.DR_JACKSON
                || (location.Source == "better_merchants" && location.ShowsStoreCounters));

        static bool IsInNightShop => IsNightShop(MapUIManager.Instance?.Current);

        internal static void Reset()
        {
            observedCustomer = null;
            customerTransaction = false;
            nightShopTransaction = false;
        }

        internal static void ResetNightVisit() => nightShopTransaction = false;

        static void ObserveCustomer(StoreClientInstance customer)
        {
            if (!ReferenceEquals(customer, observedCustomer))
            {
                observedCustomer = customer;
                customerTransaction = false;
            }
        }

        internal static void MarkTransaction(PlayerStore store)
        {
            StoreClientInstance customer = store?.currentClientInstance;
            ObserveCustomer(customer);
            if (IsInNightShop)
                nightShopTransaction = true;
            else if (customer != null)
                customerTransaction = true;
        }

        internal static bool HasCustomerTransaction(StoreClientInstance customer)
        {
            ObserveCustomer(customer);
            return customer != null && customerTransaction;
        }

        internal static bool HasNightShopTransaction() => IsInNightShop && nightShopTransaction;
    }

    // Accepted appraisals pay directly without OnItemBought or SellItem.
    // Observe the payout, so rejected estimates do not count as transactions.
    [HarmonyPatch(typeof(AppraisalServiceHelper), nameof(AppraisalServiceHelper.GiveAppraisalFee))]
    internal static class AppraisalFeeTransactionPatch
    {
        static void Postfix()
        {
            TransactionGuard.MarkTransaction(StoreAccess.Current);
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.OnItemBought))]
    internal static class ItemBoughtTransactionPatch
    {
        static void Postfix(PlayerStore __instance, GameItem gameItem)
        {
            if (gameItem != null && GeneralHelper.IsItemOwned(gameItem))
                TransactionGuard.MarkTransaction(__instance);
        }
    }

    // OnItemsSold is a barter-only path. Ordinary and multi-item sales call
    // SellItem; observe the ownership transition so a cancelled sale is ignored.
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SellItem))]
    internal static class ItemSoldTransactionPatch
    {
        static void Prefix(GameItem item, out bool __state)
        {
            __state = item != null && GeneralHelper.IsItemOwned(item);
        }
        static void Postfix(PlayerStore __instance, GameItem item, bool __state)
        {
            if (__state && item != null && !GeneralHelper.IsItemOwned(item))
                TransactionGuard.MarkTransaction(__instance);
        }
    }

    // The native bulk purchase bypasses OnItemBought. Only mark a transaction
    // when a previously unowned item actually changed ownership.
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.BuyAllItemFromTable))]
    internal static class BulkPurchaseTransactionPatch
    {
        static void Prefix(out List<GameItem> __state)
        {
            __state = new List<GameItem>();
            GameInventory inventory = EmporiumEntry.Instance?.backInvinvElement;
            if (inventory == null) return;
            foreach (GameItem item in inventory.childItems)
                if (item != null && !GeneralHelper.IsItemOwned(item)) __state.Add(item);
        }

        static void Postfix(PlayerStore __instance, List<GameItem> __state)
        {
            foreach (GameItem item in __state)
                if (GeneralHelper.IsItemOwned(item))
                {
                    TransactionGuard.MarkTransaction(__instance);
                    return;
                }
        }
    }
}
