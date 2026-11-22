using Nicokobo.ModUtilities;
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using StockCleanup = Nicokobo.ModUtilities.ItemGraphCleanup;
using NightShopStockProvider = System.Func<AfterhourLocation, (System.Action Generate, System.Action Rollback, System.Predicate<GameItem> Keep)>;

namespace StockReroll
{
    public sealed class StockRerollMod : IMod
    {
        const string ConfigSection = "reroll.config.stock_reroll";
        internal static bool IsRefreshingStock { get; private set; }

        ConfigEntry<string> hotkey;
        ConfigEntry<bool> blockAfterTransaction;
        ConfigEntry<bool> allowModCallbacks;
        ConfigEntry<bool> chargeRerollFee;
        StoreClientInstance rerollCustomer;
        int successfulRerolls;
        ModLog log;
        string lastHotkeyValue;
        RerollHotkey parsedHotkey;
        GameObject host;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            ModConfig config = ModHelper.GetConfig(manifest.ID);
            hotkey = config.Define("Hotkey", "F5", ConfigSection,
                () => Text.Get("hotkey"), tooltip: () => Text.Get("hotkey_help"));
            blockAfterTransaction = config.Define("BlockAfterTransaction", true, ConfigSection,
                () => Text.Get("block_after_transaction"), tooltip: () => Text.Get("block_after_transaction_help"));
            allowModCallbacks = config.Define("AllowModCallbacks", true, ConfigSection,
                () => Text.Get("allow_callbacks"), tooltip: () => Text.Get("allow_callbacks_help"));
            chargeRerollFee = config.Define("ChargeRerollFee", false, ConfigSection,
                () => Text.Get("charge_fee"), tooltip: () => Text.Get("charge_fee_help"));
            RefreshHotkey();
        }

        public void OnEnable()
        {
            ResetVisitState();
            try
            {
                ModHook.OnGameLoadedInit += ResetVisitState;
                ModHook.OnAfterhourArriveEarly += ResetNightVisit;
                ModHook.OnAfterhourLeaveLate += ResetNightVisit;
                host = new GameObject("Nicokobo.StockReroll");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<StockRerollRuntime>().Owner = this;
                log.Log($"Reroll hotkey={hotkey.Value}; block after transaction={blockAfterTransaction.Value}; allow Mod callbacks={allowModCallbacks.Value}.");
            }
            catch
            {
                OnDisable();
                throw;
            }
        }

        public void OnDisable()
        {
            ModHook.OnGameLoadedInit -= ResetVisitState;
            ModHook.OnAfterhourArriveEarly -= ResetNightVisit;
            ModHook.OnAfterhourLeaveLate -= ResetNightVisit;
            if (host != null)
            {
                host.SetActive(false);
                UnityEngine.Object.Destroy(host);
                host = null;
            }
            ResetVisitState();
        }

        void ResetVisitState()
        {
            TransactionGuard.Reset();
            StockReplay.Reset();
            ResetNightVisit(null);
        }

        void ResetNightVisit(string locationId)
        {
            TransactionGuard.ResetNightVisit();
            rerollCustomer = null;
            successfulRerolls = 0;
        }

        internal void Tick()
        {
            if (TextInputFocus.ShouldSuppressHotkeys()) return;
            RefreshHotkey();
            if (parsedHotkey.WasPressedThisFrame()) RerollCurrentStock();
        }

        void RefreshHotkey()
        {
            string value = hotkey.Value;
            if (string.Equals(value, lastHotkeyValue, StringComparison.Ordinal)) return;
            lastHotkeyValue = value;
            if (!RerollHotkey.TryParse(value, out parsedHotkey))
                log.Warning($"Invalid reroll hotkey '{value}'; reroll hotkey is disabled.");
        }

        internal void RerollCurrentStock()
        {
            try
            {
                // Instance creates a new store in the main menu. Read only the
                // existing native field; pressing a hotkey must not create a run.
                PlayerStore store = StoreAccess.Current;
                EmporiumEntry emporium = EmporiumEntry.Instance;
                StoreUIManager ui = StoreUIManager.Instance;
                if (store == null || emporium == null || ui == null)
                {
                    Reject(S.SceneNotReady);
                    return;
                }

                string blockingState = GetBlockingState(store, ui);
                if (blockingState != null)
                {
                    log.Warning($"Reroll blocked by active state: {blockingState}.");
                    Reject(S.FinishTransactionFirst);
                    return;
                }

                // Wrapped suppliers can replay OnIntroduced and reopen the
                // wholesale panel. Keep UI suppression active through rollback.
                IsRefreshingStock = true;
                MapUIManager map = MapUIManager.Instance;
                if (map != null && map.IsInTransit)
                    return;

                AfterhourLocation location = map?.Current;
                if (TransactionGuard.IsNightShop(location))
                {
                    if (ShouldBlockAfterTransaction() && TransactionGuard.HasNightShopTransaction())
                    {
                        Reject(S.TransactionAlreadyOccurred);
                        return;
                    }

                    if (location.Id == AfterhourLocation.DR_JACKSON)
                    {
                        RerollNightShop(store, emporium, GenerateJacksonStock);
                    }
                    else
                    {
                        var provider = ModHelper.Lookup<NightShopStockProvider>("nicokobo.stock-reroll-better-merchants", "stock");
                        var stock = provider == null ? default : provider(location);
                        if (stock.Generate == null)
                            Reject(S.UnsupportedNightShop);
                        else
                            RerollNightShop(store, emporium, stock.Generate, stock.Rollback, stock.Keep);
                    }
                    return;
                }

                if (map != null && map.IsOutside)
                {
                    Reject(S.NoCustomer);
                    return;
                }

                if (!store.isClientArrived)
                {
                    Reject(S.NoCustomer);
                    return;
                }

                StoreClientInstance oldInstance = store.currentClientInstance;
                StoreClient client = oldInstance?.GetClientBlueprint();
                if (oldInstance == null || client == null)
                {
                    Reject(S.NoCustomer);
                    return;
                }

                if (ShouldBlockAfterTransaction() && TransactionGuard.HasCustomerTransaction(oldInstance))
                {
                    Reject(S.TransactionAlreadyOccurred);
                    return;
                }

                if (client.isBusinessComplete || client.reported)
                {
                    Reject(S.BusinessFinished);
                    return;
                }

                GameGridInventory customerInventory = emporium.backInvinvElement;
                GameGridInventory frontInventory = emporium.frontInvinvElement;
                if (customerInventory == null || frontInventory == null)
                {
                    Reject(S.SceneNotReady);
                    return;
                }

                var oldGridStock = CollectUnowned(customerInventory);
                int oldGridCount = oldGridStock.Count;
                var oldFrontStock = CollectUnowned(frontInventory);
                int oldFrontCount = oldFrontStock.Count;
                var originalGridItems = CollectItemReferences(customerInventory);
                var originalFrontItems = CollectItemReferences(frontInventory);
                int oldCount = oldGridCount + oldFrontCount;
                if (oldCount == 0)
                {
                    Reject(S.NoCustomerStock);
                    return;
                }

                // Native replays only run reviewed supply actions. Replaying
                // reached Mod callbacks follows the configured setting.
                if (!StockReplay.TryGetActions(client, allowModCallbacks.Value, out var stockActions))
                {
                    Reject(S.MixedStockActions);
                    return;
                }
                if (stockActions.Count == 0)
                {
                    Reject(S.NoSafeStockAction);
                    return;
                }

                int fee = GetRerollFee(oldInstance);
                if (!CanAffordReroll(store, fee))
                    return;

                emporium.CloseItemWindowUIWindow();
                bool committed = false;
                int newGridCount = 0;
                int newFrontCount = 0;
                bool wasWholesale = client.isOfferWholesale;
                int wholesaleDiscount = client.wholesaleDiscount;
                try
                {
                    if (!DetachItems(customerInventory, oldGridStock)
                        || !DetachItems(frontInventory, oldFrontStock))
                    {
                        Reject(S.CouldNotDetachStock);
                        return;
                    }

                    foreach (Action action in stockActions)
                        action.Invoke();

                    newGridCount = CountUnowned(customerInventory);
                    newFrontCount = CountUnowned(frontInventory);
                    if (newGridCount + newFrontCount == 0)
                    {
                        Reject(S.EmptyResult);
                        return;
                    }

                    committed = TryCommitReroll(store, fee);
                    if (!committed)
                        return;
                }
                finally
                {
                    // Retain this visit's terms even when generation fails.
                    client.isOfferWholesale = wasWholesale;
                    client.wholesaleDiscount = wholesaleDiscount;
                    if (!committed)
                    {
                        try { RollbackStock(customerInventory, oldGridStock, originalGridItems); }
                        finally { RollbackStock(frontInventory, oldFrontStock, originalFrontItems); }
                    }
                }

                StockCleanup.Destroy(oldFrontStock);
                StockCleanup.Destroy(oldGridStock);

                store.RefreshCounterItem();
                emporium.Validate(true);

                log.Log(
                    $"Customer reroll generated: id={client.identifier ?? "<null>"}; "
                    + $"newGrid={newGridCount}; newFront={newFrontCount}.");

                string clientName = string.IsNullOrWhiteSpace(client.displayName)
                    ? client.identifier ?? S.UnknownCustomer
                    : client.displayName;
                ReportReroll(S.Success(clientName, oldCount, newGridCount + newFrontCount), fee);
            }
            catch (Exception exception)
            {
                log.Error($"Customer stock reroll failed: {exception}");
                Notify(S.Failed);
            }
            finally
            {
                IsRefreshingStock = false;
            }
        }

        bool ShouldBlockAfterTransaction()
        {
            return blockAfterTransaction == null || blockAfterTransaction.Value;
        }

        int GetRerollFee(StoreClientInstance customer)
        {
            if (!ReferenceEquals(rerollCustomer, customer))
            {
                rerollCustomer = customer;
                successfulRerolls = 0;
            }

            return chargeRerollFee.Value && Difficulty.IsHardOrAbove() ? 10 * (successfulRerolls + 1) : 0;
        }

        bool CanAffordReroll(PlayerStore store, int fee)
        {
            if (fee == 0 || store.GetCash() >= fee)
                return true;

            Reject(S.InsufficientCredits(fee));
            return false;
        }

        bool TryCommitReroll(PlayerStore store, int fee)
        {
            // A supply callback can spend credits after the first balance check.
            if (!CanAffordReroll(store, fee))
                return false;

            if (fee > 0)
                store.PayMoney(fee);
            successfulRerolls++;
            return true;
        }

        static List<GameItem> CollectUnowned(GameInventory inventory, Predicate<GameItem> keep = null)
        {
            var items = new List<GameItem>();
            if (inventory?.childItems == null)
                return items;

            for (int index = 0; index < inventory.childItems.Count; index++)
            {
                GameItem item = inventory.childItems[index];
                if (item != null && !GeneralHelper.IsItemOwned(item) && (keep == null || !keep(item)))
                    items.Add(item);
            }

            return items;
        }

        static HashSet<GameItem> CollectItemReferences(GameInventory inventory)
        {
            var items = new HashSet<GameItem>();
            if (inventory?.childItems == null)
                return items;
            for (int index = 0; index < inventory.childItems.Count; index++)
            {
                GameItem item = inventory.childItems[index];
                if (item != null)
                    items.Add(item);
            }
            return items;
        }

        static bool DetachItems(GameInventory inventory, List<GameItem> items)
        {
            if (inventory == null || items == null)
                return false;

            bool oldOverride = inventory.overrideLockRemove;
            inventory.overrideLockRemove = true;
            int detached = 0;
            try
            {
                for (int index = 0; index < items.Count; index++)
                {
                    GameItem item = items[index];
                    if (item != null && inventory.Expel(item))
                        detached++;
                }
            }
            finally
            {
                inventory.overrideLockRemove = oldOverride;
            }

            return detached == items.Count;
        }

        static void RestoreItems(GameInventory inventory, List<GameItem> items)
        {
            if (inventory == null || items == null)
                return;

            for (int index = 0; index < items.Count; index++)
            {
                GameItem item = items[index];
                if (item != null && item.parentInventory == null)
                {
                    if (!inventory.UncheckedAccept(item))
                        throw new InvalidOperationException("Could not restore detached stock to its original inventory.");
                }
            }
        }

        static void RollbackStock(
            GameInventory inventory,
            List<GameItem> oldStock,
            HashSet<GameItem> originalItems)
        {
            // Remove any partially generated replacement stock before restoring
            // the detached originals, otherwise a failed reroll could duplicate items.
            for (int index = inventory.childItems.Count - 1; index >= 0; index--)
            {
                GameItem partial = inventory.childItems[index];
                // A partial detach can leave original items in the inventory.
                // Also remove new items marked owned by a supply hook on failure.
                if (partial != null && !originalItems.Contains(partial))
                    StockCleanup.Destroy(partial);
            }
            RestoreItems(inventory, oldStock);
        }

        static void GenerateJacksonStock()
        {
            // Match DrJacksonLocation.Arrive without repeating travel or events.
            if (AfterhourEvents.IsActive(VanillaAfterhourEvents.SPECIAL_STOCK))
                StoreClientList.PlaceInventorSpecialStock();
            else
                StoreClientList.PlaceInventorInventory(false);
        }

        void RerollNightShop(PlayerStore store, EmporiumEntry emporium, Action generate,
            Action rollback = null, Predicate<GameItem> keep = null)
        {
            GameGridInventory inventory = emporium.frontInvinvElement;
            GameGridInventory back = emporium.backInvinvElement;
            if (inventory?.childItems == null || back?.childItems == null)
            {
                Reject(S.SceneNotReady);
                return;
            }

            int fee = GetRerollFee(store.currentClientInstance);
            if (!CanAffordReroll(store, fee))
                return;

            var oldStock = CollectUnowned(inventory, keep);
            var oldBackStock = CollectUnowned(back, keep);
            var originalItems = CollectItemReferences(inventory);
            var originalBackItems = CollectItemReferences(back);
            int oldCount = oldStock.Count + oldBackStock.Count;
            emporium.CloseItemWindowUIWindow();
            bool committed = false;
            int newCount = 0;
            try
            {
                if (!DetachItems(inventory, oldStock) || !DetachItems(back, oldBackStock))
                {
                    Reject(S.CouldNotDetachStock);
                    return;
                }

                generate();

                newCount = CountUnowned(inventory, keep) + CountUnowned(back, keep);
                if (newCount == 0)
                {
                    Reject(S.EmptyNightResult);
                    return;
                }
                committed = TryCommitReroll(store, fee);
                if (!committed)
                    return;
            }
            finally
            {
                if (!committed)
                {
                    try
                    {
                        try { RollbackStock(inventory, oldStock, originalItems); }
                        finally { RollbackStock(back, oldBackStock, originalBackItems); }
                    }
                    finally { rollback?.Invoke(); }
                }
            }

            StockCleanup.Destroy(oldStock);
            StockCleanup.Destroy(oldBackStock);
            store.RefreshCounterItem();
            emporium.Validate(true);
            ReportReroll(S.NightShopSuccess(oldCount, newCount), fee);
        }

        static string GetBlockingState(PlayerStore store, StoreUIManager ui)
        {
            // Normal dialogue/typing and item magnification are presentation
            // states, not transactions. Blocking them made ordinary interaction
            // permanently prevent the hotkey from working.
            // A selected weighing-counter item remains set after closing the
            // UI. Only an open trade/bargain panel blocks replacement.
            if (NegociationUIManager.Instance != null && NegociationUIManager.Instance.IsOpen
                || BargainUIManager.Instance != null && BargainUIManager.Instance.IsOpen)
                return "negotiation";
            if (store.isClientBeingArrested)
                return "client-arrest";
            if (store.isHandlingShootout || ui.isShootingMode)
                return "shootout";
            return null;
        }

        static int CountUnowned(GameInventory inventory, Predicate<GameItem> keep = null)
        {
            int count = 0;
            if (inventory?.childItems == null)
                return count;

            for (int index = 0; index < inventory.childItems.Count; index++)
            {
                GameItem item = inventory.childItems[index];
                if (item != null && !GeneralHelper.IsItemOwned(item) && (keep == null || !keep(item)))
                    count++;
            }
            return count;
        }

        void Reject(string message)
        {
            log.Warning(message);
            Notify(message);
        }

        void ReportReroll(string message, int fee)
        {
            if (fee > 0)
                message += " " + S.FeePaid(fee);
            log.Log(message);
            Notify(message);
        }

        static void Notify(string message)
        {
            try
            {
                StoreUIManager.Instance?.Notify(message);
            }
            catch
            {
                // Every result is also written through ModLog to the F8 console.
            }
        }
    }
    // The official IMod interface has no Update callback.
    [DefaultExecutionOrder(10000)]
    public sealed class StockRerollRuntime : MonoBehaviour
    {
        internal StockRerollMod Owner;
        void Update() => Owner?.Tick();
    }

    [HarmonyPatch(typeof(NegociationUIManager), nameof(NegociationUIManager.OpenUI))]
    internal static class RerollTradeWindowPatch
    {
        internal static bool Prefix() => !StockRerollMod.IsRefreshingStock;
    }

    internal static class Text
    {
        internal static string Get(string key, params object[] args) =>
            ModText.Get("nicokobo.stock-reroll", "reroll." + key, args);
    }

    internal static class S
    {
        public static string NoCustomer => Text.Get("no_customer");
        public static string NoCustomerStock => Text.Get("no_customer_stock");
        public static string NoSafeStockAction => Text.Get("no_safe_stock_action");
        public static string MixedStockActions => Text.Get("mixed_stock_actions");
        public static string BusinessFinished => Text.Get("business_finished");
        public static string SceneNotReady => Text.Get("scene_not_ready");
        public static string FinishTransactionFirst => Text.Get("finish_transaction");
        public static string TransactionAlreadyOccurred => Text.Get("transaction_occurred");
        public static string CouldNotDetachStock => Text.Get("detach_failed");
        public static string EmptyResult => Text.Get("empty_result");
        public static string EmptyNightResult => Text.Get("empty_night_result");
        public static string UnsupportedNightShop => Text.Get("unsupported_night_shop");
        public static string Failed => Text.Get("failed");
        public static string UnknownCustomer => Text.Get("unknown_customer");
        public static string InsufficientCredits(int fee) => Text.Get("insufficient_credits", fee);
        public static string FeePaid(int fee) => Text.Get("fee_paid", fee);
        public static string Success(string customer, int oldCount, int newCount) =>
            Text.Get("success", customer, oldCount, newCount);
        public static string NightShopSuccess(int oldCount, int newCount) =>
            Text.Get("night_success", oldCount, newCount);
    }
}
