using System;
using System.Collections.Generic;
using HarmonyLib;
using Nicokobo.ModUtilities;
using NightShopStockProvider = System.Func<AfterhourLocation, (System.Action Generate, System.Action Rollback, System.Predicate<GameItem> Keep)>;

namespace BetterMerchantsStockReroll
{
    public sealed class BetterMerchantsStock : IMod
    {
        const string BetterMerchantsId = "better_merchants";
        const string StashId = "better_merchants.shop.stash";
        const string StashStockKey = "stash_stock";
        ModManifest manifest;
        Harmony harmony;
        Type shopType;
        Type priceEffects;
        Type stashStock;
        static readonly HashSet<GameItem> redirected = new HashSet<GameItem>();
        static bool arriving;

        public void Init(ModManifest modManifest) => manifest = modManifest;

        public void OnEnable()
        {
            ModHook.OnModAssetsLoaded += Bind;
            ModHook.OnAfterhourArriveEarly += BeginArrival;
            ModHook.OnAfterhourArriveLate += EndArrival;
            ModHook.OnAfterhourLeaveLate += ClearRedirected;
        }

        public void OnDisable()
        {
            ModHelper.Expose(manifest.ID, "stock", null);
            ModHook.OnModAssetsLoaded -= Bind;
            ModHook.OnAfterhourArriveEarly -= BeginArrival;
            ModHook.OnAfterhourArriveLate -= EndArrival;
            ModHook.OnAfterhourLeaveLate -= ClearRedirected;
            harmony?.UnpatchAll(manifest.ID);
            harmony = null;
            shopType = priceEffects = stashStock = null;
            ClearRedirected(null);
        }

        void Bind(LoadedMod mod)
        {
            if (mod.Manifest.ID != manifest.ID)
                return;

            shopType = ModReflect.FindType("BetterMerchants.ShopLocation");
            priceEffects = ModReflect.FindType("BetterMerchants.EventEffects");
            stashStock = ModReflect.FindType("BetterMerchants.StashStock");
            var redirect = ModReflect.FindType("BetterMerchants.JacksonRedirect");
            if (shopType == null || priceEffects == null || stashStock == null || redirect == null)
            {
                new ModLog(manifest).Warning(ModText.Get(manifest.ID, "compat.unsupported_version"));
                return;
            }

            // Redirected offers are consumed on arrival. Keep the actual items
            // placed by that call so a reroll does not erase a one-time offer.
            harmony = new Harmony(manifest.ID);
            harmony.Patch(ModReflect.Target(redirect, "PlaceAtShop"),
                prefix: new HarmonyMethod(typeof(BetterMerchantsStock), nameof(BeforeRedirect)),
                postfix: new HarmonyMethod(typeof(BetterMerchantsStock), nameof(AfterRedirect)));
            ModHelper.Expose(manifest.ID, "stock", (NightShopStockProvider)GetStock);
        }

        static void BeginArrival(string locationId)
        {
            redirected.Clear();
            arriving = true;
        }

        static void EndArrival(string locationId) => arriving = false;

        static void ClearRedirected(string locationId)
        {
            arriving = false;
            redirected.Clear();
        }

        static void BeforeRedirect(out HashSet<GameItem> __state)
        {
            __state = null;
            if (!arriving)
                return;

            __state = new HashSet<GameItem>();
            __state.UnionWith(EmporiumEntry.Instance.frontInvinvElement.childItems);
            __state.UnionWith(EmporiumEntry.Instance.backInvinvElement.childItems);
        }

        static void AfterRedirect(HashSet<GameItem> __state)
        {
            if (__state == null)
                return;

            foreach (GameItem item in EmporiumEntry.Instance.frontInvinvElement.childItems)
                if (!__state.Contains(item)) redirected.Add(item);
            foreach (GameItem item in EmporiumEntry.Instance.backInvinvElement.childItems)
                if (!__state.Contains(item)) redirected.Add(item);
        }

        (Action Generate, Action Rollback, Predicate<GameItem> Keep) GetStock(AfterhourLocation location)
        {
            if (!shopType.IsInstanceOfType(location))
                return default;

            object definition = ModReflect.Get(location, "Def");
            string slot = ModReflect.Get<string>(definition, "Slot");
            Action roll = ModReflect.Method<Action>(location, "RollStock");
            bool stash = location.Id == StashId;
            string[] keys = stash
                ? new[] { StashStockKey, "stash_shown" }
                : new[] { "stock_carried." + slot, "auto_stock_shown." + slot, "jackson_redirect." + slot };
            string[] previousData = null;
            object previousStashOffers = null;
            var previousPositions = new Dictionary<GameItem, GridShape>();
            return (
                Generate: () =>
                {
                    // Better Merchants can repack unowned items already on the counter.
                    var front = EmporiumEntry.Instance.frontInvinvElement;
                    foreach (GameItem item in redirected)
                    {
                        if (ReferenceEquals(item.parentInventory, front) && !GeneralHelper.IsItemOwned(item))
                            previousPositions.Add(item, item.modifiedShape);
                    }
                    if (stash)
                        previousStashOffers = ModReflect.GetStatic(stashStock, "tonight");
                    var snapshot = new string[keys.Length];
                    for (int i = 0; i < keys.Length; i++)
                        snapshot[i] = ModHelper.GetModData(BetterMerchantsId, keys[i]);
                    previousData = snapshot;

                    // The stall normally reloads the same saved offers on arrival.
                    if (stash)
                        ModHelper.RemoveModData(BetterMerchantsId, StashStockKey);
                    roll();
                    ModReflect.CallStatic(priceEffects, "ApplyPrices", definition);
                },
                Rollback: () =>
                {
                    if (previousData == null)
                        return;

                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (previousData[i] == null)
                            ModHelper.RemoveModData(BetterMerchantsId, keys[i]);
                        else
                            ModHelper.SetModData(BetterMerchantsId, keys[i], previousData[i]);
                    }
                    if (stash)
                        ModReflect.SetStatic(stashStock, "tonight", previousStashOffers);
                    foreach (var entry in previousPositions)
                    {
                        entry.Key.modifiedShape = entry.Value;
                        entry.Key.SyncModifiedState();
                    }
                },
                Keep: redirected.Contains);
        }
    }
}
