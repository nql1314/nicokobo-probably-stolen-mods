using AugMechanicalAscension.Domain;
using HarmonyLib;
namespace AugMechanicalAscension.Integration;

internal sealed partial class StoryRuntimeFlow
{
    private sealed record CounterOffer(GameItem Item, string Operation);
    private readonly Dictionary<IntPtr, CounterOffer> _graylineCounter = new();
    private Dialogue? _graylineCounterDialogue;
    private string? _graylineCounterRequest;
    private bool _graylineCounterHooks, _graylinePurchaseFailed;

    private bool InstallGraylineCounterHooks()
    {
        var hooks = new HarmonyLib.Harmony("nicokobo.aug.grayline-counter");
        try
        {
            void PurchaseHook(string method, Type[] args, string prefix) => hooks.Patch(
                AccessTools.Method(typeof(PlayerStore), method, args) ?? throw new MissingMethodException(typeof(PlayerStore).FullName, method),
                prefix: new HarmonyMethod(typeof(StoryRuntimeFlow), prefix),
                postfix: new HarmonyMethod(typeof(StoryRuntimeFlow), nameof(CounterBuyPostfix)),
                finalizer: new HarmonyMethod(typeof(StoryRuntimeFlow), nameof(CounterBuyFinalizer)));
            PurchaseHook(nameof(PlayerStore.BuyItem), [typeof(GameItem)], nameof(CounterBuyPrefix));
            hooks.Patch(AccessTools.Method(typeof(NegociationUIManager), nameof(NegociationUIManager.BuyMultipleItem),
                    [typeof(System.Collections.Generic.List<GameItem>)]) ??
                throw new MissingMethodException(typeof(NegociationUIManager).FullName, nameof(NegociationUIManager.BuyMultipleItem)),
                prefix: new HarmonyMethod(typeof(StoryRuntimeFlow), nameof(CounterBulkBuyPrefix)));
            _graylineCounterHooks = true;
            return true;
        }
        catch (Exception ex)
        {
            hooks.UnpatchSelf();
            log("[ERROR] [Grayline] Counter hooks unavailable: " + ex.Message);
            return false;
        }
    }

    private void ForgetGraylineCounter()
    {
        // Native dismiss/load owns removal of unowned counter goods.
        _graylineCounter.Clear(); _graylineCounterDialogue = null; _graylineCounterRequest = null;
        _graylinePurchaseFailed = false;
    }

    private void ConfigureGraylineCounterClient()
    {
        var client = _graylineStore!.storeClientManager.clientStack.ToArray().Single(x => x.identifier == GraylineActorId);
        StoreClientFactionSetup.InitLowerLevelCitizen(client);
        client.clientIntent = StoreClient.ClientIntent.SELL;
        // Forge story visitors disable regular sales. Native CanBuyItemFromTable
        // rejects that flag before the purchase hook can ever be reached.
        client.isRegularSellDisable = false;
        client.isMultiBuyDisabled = false;
        client.isBargainAvailable = true;
        // OnAddedToClientArea turns this native supplier modifier into the
        // visible +25% buying feature; native refresh and purchase own its life.
        client.buyPriceModifier = 25;
        var unavailable = _graylineState!.Stock.Where(row => !GraylineRegistered(row.ItemId))
            .Select(row => GraylineName(row.ItemId) + AugText.Get("aug.provider_unavailable_or_category_disabled")).ToArray();
        if (unavailable.Length != 0)
            _graylineUnavailable = AugText.Get("aug.unavailable_stock") + string.Join(AugText.Get("aug.format"), unavailable);
        _graylineCounterDialogue = new Dialogue().SetText(AugText.Get("aug.grayline"),
            AugText.Get("aug.the_supplies_are_on_the_counter_take") +
            (string.IsNullOrEmpty(_graylineUnavailable) ? "" : "\n" + _graylineUnavailable));
        _graylineCounterDialogue.isMainDialog = true;
        // A regular supplier's line ends without sending him away. Native item
        // selection and negotiation then control purchases and dismissal.
        client.mainDialogue = _graylineCounterDialogue;
        client.AddBasicDialog();
        client.CompleteClientCreation();
    }

    private void PlaceGraylineCounter()
    {
        if (!GraylineReady || !_graylineCounterHooks || _graylineRequest == null ||
            _graylineState?.VisitState != "arrived") return;
        if (_graylineStore!.currentClientInstance?.GetClientBlueprint()?.identifier != GraylineActorId) return;
        var counter = EmporiumEntry.Instance?.frontInvinvElement;
        if (counter == null) return;
        if (_graylineCounterRequest == _graylineRequest) return;
        _graylineCounterRequest = _graylineRequest;
        var made = new List<GameItem>();
        try
        {
            var newspapers = counter.childItems.ToArray().Where(x => x.identifier == "newspaper").ToArray();
            if (!WorkshopAchievementDelivery.Recall(newspapers))
                throw new InvalidOperationException("Counter newspaper cleanup unconfirmed");
            foreach (var row in _graylineState.Stock)
            {
                // Historical catalogs remain readable, but native nodes are no
                // longer offered even when an old shipment still records them.
                if (!GraylineRegistered(row.ItemId) ||
                    GraylineSupplyRules.Product(row.ItemId, _graylineState.CatalogVersion)?.Kind == GraylineProductKind.BasicNode) continue;
                for (int n = 0; n < row.Remaining; n++)
                {
                    var item = OfficialItemApi.Create(row.ItemId, true);
                    made.Add(item);
                    if (item.identifier != row.ItemId || item.unitCount != 1 || item.parentInventory != null || item.unitValue is < 0 or > int.MaxValue)
                        throw new InvalidOperationException("Counter product identity/value mismatch");
                    item.mayStackItemFunc = null; // retain each offered instance and its purchase receipt
                    GeneralHelper.SetItemOwned(item, false);
                    _graylineCounter.Add(NativeObject.Handle(item), new(item, Guid.NewGuid().ToString("D")));
                }
            }
            // The native counter is 18x4. Reserve full-height columns for 1x4
            // modules before machines and ores fragment the remaining rows.
            made = made.OrderByDescending(x => x.modifiedShape.globalHeight)
                .ThenByDescending(x => x.modifiedShape.globalWidth * x.modifiedShape.globalHeight).ToList();
            foreach (var item in made)
            {
                if (OfficialInventoryPlacementApi.PlanWholeGrid(counter, [item]) == null)
                {
                    if (!WorkshopAchievementDelivery.Recall([item]))
                        throw new InvalidOperationException("Skipped counter product cleanup unconfirmed");
                    _graylineCounter.Remove(NativeObject.Handle(item));
                    continue;
                }
                _graylineStore.AddDirectSellingItemToTable(item, false);
                if (item.parentInventory == null || OfficialInventoryApi.IsPlayerOwned(item))
                    throw new InvalidOperationException($"Unowned counter placement unconfirmed: item={item.identifier}; uid={item.uniqueId}; parent={item.parentInventory?.identifier ?? "none"}; owned={OfficialInventoryApi.IsPlayerOwned(item)}");
            }
        }
        catch (Exception ex)
        {
            WorkshopAchievementDelivery.Recall(made);
            BlockGrayline("Counter placement failed: " + ex.Message);
        }
    }

    private static bool CounterBuyPrefix(PlayerStore __instance, GameItem? __0, out NativeCounterPurchase? __state)
    {
        __state = null;
        return _active?.BeginNativeCounterPurchase(__instance, __0, out __state) ?? true;
    }
    private static void CounterBuyPostfix(NativeCounterPurchase? __state) =>
        __state?.Flow.FinishNativeCounterPurchase(__state, null);
    private static Exception? CounterBuyFinalizer(Exception? __exception, NativeCounterPurchase? __state)
    {
        __state?.Flow.FinishNativeCounterPurchase(__state, __exception);
        return __exception;
    }
    private static bool CounterBulkBuyPrefix(System.Collections.Generic.List<GameItem> __0)
    {
        if (_active is not { } flow || __0 == null || !__0.ToArray().Any(x => x != null && flow._graylineCounter.ContainsKey(NativeObject.Handle(x)))) return true;
        // Native BuyMultipleItem calls PlayerStore.BuyItem for each selected
        // instance, so the same receipt guard covers both purchase entries.
        return flow.CanBuyNativeCounterItems(__0.ToArray());
    }
}
