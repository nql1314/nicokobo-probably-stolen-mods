using System.Reflection;
using DseCecil;
using StockReroll;
using UnityEngine;

int assertions = 0;
void Check(bool value, string message)
{ assertions++; if (!value) throw new InvalidOperationException(message); }
Action Supply(Action action) => new SupplyAction(action).Run;
GameItem Item(GameInventory inventory, bool owned = false)
{
    var item = new GameItem { Owned = owned };
    Check(inventory.UncheckedAccept(item), "Fixture placement failed."); return item;
}
GameItem Container(GameInventory inventory, bool owned = false)
{
    var item = Item(inventory, owned);
    var contents = new GameGridInventory();
    item.SetContentWindow(new PixelWindow { Child = contents });
    item.SetExamineWindow(new PixelWindow());
    Item(contents);
    return item;
}
object Patch(Type type, string method, params object[] args) => type.GetMethod(method,
    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
bool TradeWindowAllowed() => RerollTradeWindowPatch.Prefix();
StockRerollMod currentMod = null;
(StockRerollMod mod, StoreClient client, GameGridInventory back, GameGridInventory front) Visit(bool night = false)
{
    currentMod?.OnDisable();
    EmporiumEntry.Instance = new(); StoreUIManager.Instance = new();
    PlayerStore.instance = new(); MapUIManager.Instance = new(); DialogUIManager.Instance = new();
    ModHelper.NightShopStock = null;
    NegociationUIManager.Instance = new(); BargainUIManager.Instance = new();
    AfterhourEvents.Special = false; GraphHandler.windowGraphNodeRoots.Clear();
    StoreClientList.QuotaCharges = 0;
    StoreClientList.ScavSupplyCalls = 0; StoreClientList.StockMultiplier = 1f;
    StoreStation.Instance = new(); TableMaster.Rolls.Clear();
    RNG.Bounds.Clear(); RNG.Chances.Clear(); RNG.Integers.Clear(); RNG.RollResult = false;
    currentMod = new(); currentMod.Init(new()); currentMod.OnEnable();
    if (night) { MapUIManager.Instance.Current = new() { Id = AfterhourLocation.DR_JACKSON }; ModHook.Arrive(AfterhourLocation.DR_JACKSON); }
    return (currentMod, PlayerStore.instance.currentClientInstance.Client,
        EmporiumEntry.Instance.backInvinvElement, EmporiumEntry.Instance.frontInvinvElement);
}
void AllowTransactions(bool allow) => ((ConfigEntry<bool>)ModHelper.Config.Entries["BlockAfterTransaction"]).Value = !allow;

// Assert the fee requests at the native payment boundary, including failed rerolls.
{
    var (mod, client, _, front) = Visit();
    var store = PlayerStore.instance;
    var setting = (ConfigEntry<bool>)ModHelper.Config.Entries["ChargeRerollFee"];
    Check(!setting.Value, "Reroll fees were enabled by default.");
    setting.Value = true;
    store.HardOrAbove = true;
    store.Cash = 50;
    int generated = 0;
    bool generateStock = true, spendDuringSupply = false;
    Item(front);
    client.OnArrival = Supply(() =>
    {
        generated++;
        if (generateStock) Item(front);
        if (spendDuringSupply) store.Cash = 0;
    });
    mod.RerollCurrentStock();
    mod.RerollCurrentStock();
    Check(store.Payments.SequenceEqual(new[] { 10, 20 }), "Hard rerolls did not request the first two escalating fees.");
    Check(StoreUIManager.Instance.LastNotification.Contains("Spent 20 credits."), "Paid reroll did not show its fee.");

    var retained = front.childItems.Single();
    store.Cash = 29;
    mod.RerollCurrentStock();
    Check(generated == 2 && !retained.Destroyed && store.Payments.Count == 2
        && StoreUIManager.Instance.LastNotification.Contains("30"), "An unaffordable reroll changed stock or failed to show its fee.");

    store.Cash = 30;
    generateStock = false;
    mod.RerollCurrentStock();
    Check(front.childItems.Single() == retained && store.Payments.Count == 2, "An empty reroll charged a fee or lost stock.");

    generateStock = true;
    spendDuringSupply = true;
    mod.RerollCurrentStock();
    Check(front.childItems.Single() == retained && store.Payments.Count == 2, "A supply callback spending the balance left the reroll committed.");

    store.Cash = 30;
    spendDuringSupply = false;
    mod.RerollCurrentStock();
    Check(retained.Destroyed && store.Payments.SequenceEqual(new[] { 10, 20, 30 }), "A failed attempt advanced the fee or an exact balance could not pay it.");

    setting.Value = false;
    retained = front.childItems.Single();
    store.Cash = -1;
    mod.RerollCurrentStock();
    Check(retained.Destroyed && store.Payments.Count == 3, "Turning off fees did not restore free rerolls.");
    setting.Value = true;
    store.Cash = 50;
    mod.RerollCurrentStock();
    store.currentClientInstance = new() { Client = new() { identifier = client.identifier, OnArrival = client.OnArrival } };
    mod.RerollCurrentStock();
    Check(store.Payments.SequenceEqual(new[] { 10, 20, 30, 50, 10 }), "Changing settings or starting another visit miscounted the fee.");
}

// Preserve visitor identity, trade terms and purchased items on both counters;
// ignore unrelated native actions, future dialogue and duplicate delegates.
{
    var (mod, client, back, front) = Visit();
    ((ConfigEntry<bool>)ModHelper.Config.Entries["ChargeRerollFee"]).Value = true;
    PlayerStore.instance.Cash = -1;
    var instance = PlayerStore.instance.currentClientInstance;
    var oldBack = Item(back); var oldFront = Item(front); var owned = Container(front, true);
    int generated = 0, extras = 0;
    Action stock = Supply(() => { generated++; Item(back); Item(front); });
    client.OnArrival = stock + (() => extras++);
    client.mainDialogue = new() { endAction = stock, isEndActionExecuted = true,
        nextDialogue = new() { endAction = Supply(() => extras++) } };
    mod.RerollCurrentStock(); mod.RerollCurrentStock();
    Check(PlayerStore.instance.Payments.Count == 0, "Rerolls below Hard requested a fee.");
    Check(generated == 2 && extras == 0, "Replay repeated a hook, a future line or a duplicate callback.");
    Check(ReferenceEquals(instance, PlayerStore.instance.currentClientInstance) && instance.Client == client,
        "Reroll replaced the visitor.");
    Check(client.wholesaleDiscount == -35 && client.budget == 1234, "Reroll changed trade terms.");
    Check(oldBack.Destroyed && oldFront.Destroyed && !owned.Destroyed && front.childItems.Count == 2 && back.childItems.Count == 1,
        "Reroll lost owned goods or accumulated stock.");
    Check(StoreClientList.QuotaCharges == 0 && client.IntroducedCalls == 0, "Replay consumed quota or reintroduced the visitor.");
    client.OnIntroduced(); StoreClientList.RemoveCommissaryStock();
    Check(client.IntroducedCalls == 1 && StoreClientList.QuotaCharges == 1, "Normal supply side effects remained blocked.");
}

// Reached external callbacks are enabled by default and respect the setting.
// Future dialogue callbacks must remain untouched in both modes.
foreach (bool allowModCallbacks in new[] { false, true })
foreach (string source in new[] { "arrival", "start", "end", "future" })
{
    var (mod, client, back, front) = Visit();
    Check(ModHelper.Config.Entries.TryGetValue("AllowModCallbacks", out var setting)
        && setting is ConfigEntry<bool> { Value: true }, "Missing enabled-by-default Mod-callback setting.");
    ((ConfigEntry<bool>)setting).Value = allowModCallbacks;
    var vanilla = Item(front); var foreign = Container(back);
    int generated = 0, externalCalls = 0;
    // Task.RunSynchronously is declared in another assembly, like a Mod's
    // callback. Replaying the same Task twice would also expose failed deduplication.
    Action external = new Task(() => { externalCalls++; Item(back); }).RunSynchronously;
    client.OnArrival = Supply(() => { generated++; Item(front); });
    if (source == "arrival")
    {
        client.OnArrival += external;
        client.mainDialogue = new() { endAction = external, isEndActionExecuted = true };
    }
    else
    {
        var line = new Dialogue(); client.mainDialogue = line;
        if (source == "start")
        {
            line.startAction = external; DialogUIManager.Instance.currentDialog = line;
            Patch(typeof(DialogueStartedPatch), "Postfix", DialogUIManager.Instance, true);
        }
        else { line.endAction = external; line.isEndActionExecuted = source == "end"; }
    }
    mod.RerollCurrentStock();
    if (source == "future")
        Check(generated == 1 && externalCalls == 0, "A future callback blocked current stock or ran early.");
    else if (allowModCallbacks)
        Check(generated == 1 && externalCalls == 1 && vanilla.Destroyed && foreign.Destroyed
            && front.childItems.Count == 1 && back.childItems.Count == 1,
            "Opted-in Mod callback failed to regenerate stock exactly once: " + source);
    else
    {
        Check(!vanilla.Destroyed && !foreign.Destroyed && generated == 0 && externalCalls == 0,
            "Mixed native/foreign supply destroyed goods from an unreplayed callback: " + source);
        Check(StoreUIManager.Instance.LastNotification.Contains("another Mod"), "Mixed supply rejection did not explain why.");
    }
}

// A Mod may wrap the native supplier instead of appending a separate callback,
// as Mechcore's thief supply does. The default must work with no exposed native action.
{
    var (mod, client, back, front) = Visit();
    var old = Item(back); var owned = Item(front, true); int generated = 0;
    Action native = Supply(() => { generated++; Item(back); });
    client.mainDialogue = new() { endAction = native.Invoke, isEndActionExecuted = true };
    mod.RerollCurrentStock();
    Check(generated == 1 && old.Destroyed && !owned.Destroyed,
        "The default could not replay a wrapped supplier or lost purchased goods.");
    var setting = (ConfigEntry<bool>)ModHelper.Config.Entries["AllowModCallbacks"];
    var retained = back.childItems.Single();
    setting.Value = false; mod.RerollCurrentStock();
    Check(generated == 1 && !retained.Destroyed, "Disabling the opt-in did not immediately restore protection.");
    setting.Value = true;
    TransactionGuard.MarkTransaction(PlayerStore.instance); mod.RerollCurrentStock();
    Check(generated == 1 && !retained.Destroyed, "Mod-callback opt-in bypassed transaction protection.");
    AllowTransactions(true); mod.RerollCurrentStock();
    Check(generated == 2 && !owned.Destroyed, "Independent transaction opt-out failed for a wrapped supplier.");
}

// Wrapped suppliers can call the native wholesale-introduction code again.
// Exercise the real reroll and UI admission guard, without simulating OpenUI.
foreach (bool wrapped in new[] { false, true })
{
    var (mod, client, _, front) = Visit(); Item(front);
    ((ConfigEntry<bool>)ModHelper.Config.Entries["AllowModCallbacks"]).Value = true;
    int generated = 0; bool allowedDuringSupply = false;
    client.OnArrival = Supply(() =>
    {
        generated++;
        allowedDuringSupply |= TradeWindowAllowed();
        Item(front);
    });
    if (wrapped) client.OnArrival = client.OnArrival.Invoke;
    Check(TradeWindowAllowed(), "Normal interaction cannot open the trade window.");
    mod.RerollCurrentStock(); mod.RerollCurrentStock();
    Check(generated == 2 && !allowedDuringSupply,
        "Stock replay allowed the trade window to reopen and block repeated rerolls.");
    Check(TradeWindowAllowed(), "Trade-window suppression leaked after a successful reroll.");
}

// The full release supplies inventor stock from a dialogue start action.
// A rejected dialogue start must not make that supply replayable.
{
    var (mod, client, back, _) = Visit(); Item(back); int calls = 0;
    var line = new Dialogue { startAction = Supply(() => { calls++; Item(back); }) };
    client.mainDialogue = line; DialogUIManager.Instance.currentDialog = line;
    Patch(typeof(DialogueStartedPatch), "Postfix", DialogUIManager.Instance, false);
    mod.RerollCurrentStock(); Check(calls == 0, "Unstarted dialogue supplied stock.");
    Patch(typeof(DialogueStartedPatch), "Postfix", DialogUIManager.Instance, true);
    DialogUIManager.Instance.currentDialog = null;
    mod.RerollCurrentStock(); Check(calls == 1, "Completed dialogue lost its start-action stock.");
    ModHook.Load(); mod.RerollCurrentStock(); Check(calls == 1, "Dialogue history leaked across loads.");
}

// Empty/failed generation and partial/throwing detachment restore BOTH counters.
foreach (bool modCallback in new[] { false, true })
foreach (string failure in new[] { "empty", "generate", "detach", "throw detach" })
{
    var (mod, client, back, front) = Visit();
    ((ConfigEntry<bool>)ModHelper.Config.Entries["AllowModCallbacks"]).Value = modCallback;
    var oldBack = Container(back); var oldFront = Container(front); var owned = Item(front, true);
    oldFront.CannotDetach = failure == "detach"; oldFront.ThrowOnDetach = failure == "throw detach";
    GameItem partialBack = null, partialFront = null; int calls = 0;
    client.OnArrival = Supply(() =>
    {
        calls++;
        if (failure == "generate")
        {
            partialBack = Container(back); partialFront = Container(front, true);
            client.wholesaleDiscount = -99;
            throw new InvalidOperationException("fixture supply failure");
        }
    });
    if (modCallback) client.OnArrival = client.OnArrival.Invoke;
    int liveBefore = GameObject.LiveCount;
    mod.RerollCurrentStock();
    Check(oldBack.parentInventory == back && oldFront.parentInventory == front && !owned.Destroyed,
        failure + ": original goods were lost.");
    Check(TradeWindowAllowed(), failure + ": trade-window suppression leaked after rollback.");
    Check(!oldBack.Destroyed && !oldFront.Destroyed && !back.overrideLockRemove && !front.overrideLockRemove,
        failure + ": original goods or inventory locks changed.");
    Check(client.wholesaleDiscount == -35 && GameObject.LiveCount == liveBefore,
        failure + ": terms or window lifetime leaked.");
    Check(calls == (failure.Contains("detach") ? 0 : 1), "Supply ran after detach failed.");
    if (failure == "generate") Check(partialBack.Destroyed && partialFront.Destroyed, "Failed supply retained partial goods.");
}

// Repeated replacement frees nested contents and window roots. Shared windows
// remain alive while a purchased item owns them.
{
    var (mod, client, back, _) = Visit(); Container(back); var owned = Container(back, true);
    client.OnArrival = Supply(() => Container(back));
    int before = GameObject.LiveCount;
    for (int i = 0; i < 100; i++) mod.RerollCurrentStock();
    Check(GameObject.LiveCount == before && back.childItems.Count == 2 && !owned.Destroyed,
        "Repeated refresh leaked a container hierarchy or removed purchased goods.");
    Check(GraphHandler.windowGraphNodeRoots.Count == 0, "Destroyed windows remained in the graph root list.");
    var old = back.childItems.Single(item => !item.Owned);
    var shared = old.contentWindow; old.SetExamineWindow(shared);
    owned.SetContentWindow(shared);
    mod.RerollCurrentStock();
    Check(!shared.handler.gameObject.Destroyed && shared.parentItems.Count == 1 && shared.parentItems[0] == owned,
        "Cleanup destroyed or retained the wrong owner of a shared window.");
}

// Native location IDs, special-stock selection and both night-shop counters.
{
    var (mod, _, back, front) = Visit(true); var old = Container(back); var owned = Item(front, true);
    ((ConfigEntry<bool>)ModHelper.Config.Entries["ChargeRerollFee"]).Value = true;
    PlayerStore.instance.HardOrAbove = true;
    PlayerStore.instance.Cash = 100;
    int normal = 0, special = 0; bool allowedDuringSupply = false;
    StoreClientList.NightGenerator = () => { normal++; allowedDuringSupply |= TradeWindowAllowed(); Item(front); };
    StoreClientList.SpecialGenerator = () => { special++; allowedDuringSupply |= TradeWindowAllowed(); Item(front); };
    AfterhourEvents.Special = true; mod.RerollCurrentStock();
    Check(special == 1 && normal == 0 && old.Destroyed && !owned.Destroyed, "Night special stock or counter cleanup was wrong.");
    TransactionGuard.MarkTransaction(PlayerStore.instance); mod.RerollCurrentStock(); Check(special == 1, "Night transaction did not block reroll.");
    AllowTransactions(true); mod.RerollCurrentStock(); Check(special == 2, "Night transaction opt-out failed.");
    AllowTransactions(false); mod.RerollCurrentStock(); Check(special == 2, "Reroll erased its transaction history.");
    ModHook.Leave(AfterhourLocation.DR_JACKSON); ModHook.Arrive(AfterhourLocation.DR_JACKSON);
    PlayerStore.instance.currentClientInstance = new();
    AfterhourEvents.Special = false; mod.RerollCurrentStock(); Check(normal == 1, "A new night visit kept the old transaction lock.");
    Check(!allowedDuringSupply && TradeWindowAllowed(), "Night-shop UI suppression was missing or leaked after restocking.");
    var retained = front.childItems.Single(item => !item.Owned); int live = GameObject.LiveCount;
    StoreClientList.NightGenerator = () => { Container(back, true); Container(front); throw new InvalidOperationException("night failure"); };
    mod.RerollCurrentStock();
    Check(retained.parentInventory == front && !retained.Destroyed && GameObject.LiveCount == live,
        "Night failure lost stock or leaked a partial hierarchy.");
    Check(TradeWindowAllowed(), "Night-shop rollback kept normal trade-window opening disabled.");
    Check(PlayerStore.instance.Payments.SequenceEqual(new[] { 10, 20, 10 }), "Night-shop fees advanced on failure or carried into another visit.");
}

// A Better Merchants location has a client instance but uses the night-shop transaction.
{
    var (mod, _, back, front) = Visit();
    var store = PlayerStore.instance;
    var location = new AfterhourLocation { Id = "better_merchants.shop.water", Source = "better_merchants", ShowsStoreCounters = true };
    MapUIManager.Instance.Current = location;
    ModHook.Arrive(location.Id);
    var old = Item(front);
    var retained = Item(front);
    var owned = Item(back, true);
    ((ConfigEntry<bool>)ModHelper.Config.Entries["ChargeRerollFee"]).Value = true;
    store.HardOrAbove = true;
    store.Cash = 100;
    int rolls = 0, rollbacks = 0;
    bool placeStock = true;
    ModHelper.NightShopStock = (Func<AfterhourLocation, (Action, Action, Predicate<GameItem>)>)(activeLocation =>
    {
        Check(ReferenceEquals(activeLocation, location), "Night-stock provider received the wrong location.");
        return (
            () => { rolls++; if (placeStock) Item(front); },
            () => rollbacks++,
            item => ReferenceEquals(item, retained));
    });

    mod.RerollCurrentStock();
    Check(rolls == 1 && rollbacks == 0 && old.Destroyed && retained.parentInventory == front && owned.parentInventory == back,
        "Better Merchants reroll missed its provider or removed goods that should be kept.");
    var replaced = front.childItems.Single(item => !ReferenceEquals(item, retained));

    TransactionGuard.MarkTransaction(store);
    mod.RerollCurrentStock();
    Check(rolls == 1, "A completed Better Merchants trade did not lock the visit.");

    ModHook.Leave(location.Id);
    ModHook.Arrive(location.Id);
    placeStock = false;
    mod.RerollCurrentStock();
    Check(rolls == 2 && rollbacks == 1 && replaced.parentInventory == front && retained.parentInventory == front
        && store.Payments.SequenceEqual(new[] { 10 }), "Failed reroll counted retained offers as new stock or charged for a rollback.");
    placeStock = true;
    mod.RerollCurrentStock();
    Check(rolls == 3 && store.Payments.SequenceEqual(new[] { 10, 10 }), "Better Merchants fees did not reset for a new visit.");
}

// Transaction patches require observed completion, including native bulk buys.
{
    var (_, _, back, _) = Visit(); var item = Item(back);
    Patch(typeof(ItemBoughtTransactionPatch), "Postfix", PlayerStore.instance, item);
    Check(!TransactionGuard.HasCustomerTransaction(PlayerStore.instance.currentClientInstance), "Unowned item marked a completed purchase.");
    var state = new object[] { null }; Patch(typeof(BulkPurchaseTransactionPatch), "Prefix", state);
    item.Owned = true; Patch(typeof(BulkPurchaseTransactionPatch), "Postfix", PlayerStore.instance, state[0]);
    Check(TransactionGuard.HasCustomerTransaction(PlayerStore.instance.currentClientInstance), "Bulk purchase bypassed transaction protection.");
    AllowTransactions(true); AllowTransactions(false);
    Check(TransactionGuard.HasCustomerTransaction(PlayerStore.instance.currentClientInstance), "Configuration erased transaction state.");
}

// Appraisal fees bypass native buy/sell callbacks. Observe the completed fee
// boundary, then exercise the real reroll with both interaction panels closed.
{
    var (mod, client, back, front) = Visit();
    var stock = Container(front); var backStock = Item(back); var receipt = Item(front, true);
    int generated = 0;
    client.OnArrival = Supply(() => { generated++; Item(front); });
    Check(!TransactionGuard.HasCustomerTransaction(PlayerStore.instance.currentClientInstance),
        "An unpaid appraisal blocked the visitor.");
    Patch(typeof(AppraisalFeeTransactionPatch), "Postfix");
    Check(!NegociationUIManager.Instance.IsOpen && !BargainUIManager.Instance.IsOpen,
        "Appraisal fixture left an interaction panel open.");
    mod.RerollCurrentStock();
    Check(generated == 0 && !stock.Destroyed && !backStock.Destroyed && !receipt.Destroyed
        && stock.parentInventory == front && backStock.parentInventory == back,
        "A paid appraisal allowed remaining stock to reroll with no interaction panel open.");
    Check(StoreUIManager.Instance.LastNotification.Contains("transaction"),
        "Appraisal settlement did not explain the transaction lock.");
    AllowTransactions(true); mod.RerollCurrentStock();
    Check(generated == 1 && !receipt.Destroyed, "Appraisal transaction opt-out failed.");
    AllowTransactions(false); mod.RerollCurrentStock();
    Check(generated == 1, "Changing settings erased the appraisal settlement record.");
    PlayerStore.instance.currentClientInstance = new() { Client = client };
    mod.RerollCurrentStock();
    Check(generated == 2, "A different visitor inherited the appraisal settlement lock.");
}

// Ordinary sales are protected only after a real owned -> unowned transition.
// Barter is already protected by the native business-complete state.
{
    var (mod, client, back, front) = Visit();
    var sold = Item(back, true); var stock = Item(front); int generated = 0;
    client.OnArrival = Supply(() => { generated++; Item(front); });
    var state = new object[] { sold, null };
    Patch(typeof(ItemSoldTransactionPatch), "Prefix", state);
    Patch(typeof(ItemSoldTransactionPatch), "Postfix", PlayerStore.instance, sold, state[1]);
    Check(!TransactionGuard.HasCustomerTransaction(PlayerStore.instance.currentClientInstance),
        "A cancelled sale marked a transaction.");
    PlayerStore.instance.SellItem(sold);
    Patch(typeof(ItemSoldTransactionPatch), "Postfix", PlayerStore.instance, sold, state[1]);
    mod.RerollCurrentStock();
    Check(generated == 0 && !stock.Destroyed, "An ordinary sale did not block reroll.");
    AllowTransactions(true); mod.RerollCurrentStock();
    Check(generated == 1, "Sale protection opt-out did not work.");
    AllowTransactions(false); mod.RerollCurrentStock();
    Check(generated == 1, "Configuration changes erased the ordinary-sale record.");
}
{
    var (mod, client, _, front) = Visit(); Item(front); int generated = 0;
    client.OnArrival = Supply(() => { generated++; Item(front); });
    var alreadyUnowned = new GameItem();
    var state = new object[] { alreadyUnowned, null };
    Patch(typeof(ItemSoldTransactionPatch), "Prefix", state);
    Patch(typeof(ItemSoldTransactionPatch), "Postfix", PlayerStore.instance, alreadyUnowned, state[1]);
    Check(!TransactionGuard.HasCustomerTransaction(PlayerStore.instance.currentClientInstance),
        "An unowned argument marked a sale.");
    AllowTransactions(true); PlayerStore.instance.AcceptBarter(); mod.RerollCurrentStock();
    Check(generated == 0, "Completed barter rerolled without a redundant barter patch.");
}

// A selected weighing-counter item is not an open negotiation. Both native
// panels still protect their referenced items while the user is trading.
{
    var (mod, client, back, front) = Visit(); var old = Item(back); int generated = 0;
    client.OnArrival = Supply(() => { generated++; Item(front); });
    Check(PlayerStore.instance.currentNegociatedItem == old, "Fixture did not model weighing selection.");
    NegociationUIManager.Instance.IsOpen = true; mod.RerollCurrentStock();
    Check(generated == 0 && !old.Destroyed, "Open trade panel allowed stock replacement.");
    Check(TradeWindowAllowed(), "A rejected reroll kept normal trade-window opening disabled.");
    NegociationUIManager.Instance.IsOpen = false;
    BargainUIManager.Instance.IsOpen = true; mod.RerollCurrentStock();
    Check(generated == 0 && !old.Destroyed, "Open bargain panel allowed stock replacement.");
    BargainUIManager.Instance.IsOpen = false; mod.RerollCurrentStock();
    Check(generated == 1 && old.Destroyed && PlayerStore.instance.currentNegociatedItem == null,
        "Closed negotiation left a selected weighing item blocking reroll.");
}

// Mixed native callbacks must supply the same goods without repeating visit
// introduction, changing wholesale terms, or consuming commissary stock.
{
    var (mod, client, _, front) = Visit(); Item(front);
    client.OnArrival = Supply(StockSupply.GetReplacement("StoreClientList+<>c__DisplayClass20_0", "<CreateJunkerSellOnly>b__0", client.identifier));
    mod.RerollCurrentStock();
    Check(front.childItems.Select(i => i.identifier).SequenceEqual(new[]
        { "scrap_metal", "scrap_metal", "scrap_metal", "common_electronic", "wire", "wire" }),
        "Junker pure supply changed native stock.");
    Check(client.IntroducedCalls == 0 && client.wholesaleDiscount == -35,
        "Junker pure supply repeated visit side effects.");
    Check(StockSupply.GetReplacement("AnotherMod", "<CreateJunkerSellOnly>b__0", client.identifier) == null,
        "Replacement matched a similarly named foreign method.");
}
{
    var (mod, client, _, front) = Visit(); Item(front);
    client.OnArrival = Supply(StockSupply.GetReplacement("StoreClientList+<>c__DisplayClass49_0", "<CreateScavGeneral>b__0", client.identifier));
    mod.RerollCurrentStock();
    Check(StoreClientList.ScavSupplyCalls == 1 && front.childItems.Single().identifier == "scav_stock"
        && client.IntroducedCalls == 0, "Scavenger did not use the public supply method alone.");
}
foreach (int day in new[] { 0, 20, 60 })
{
    var (mod, client, _, front) = Visit();
    var owned = Item(front, true); owned.BaseValue = 10; Item(front);
    StoreStation.Instance.dayCounter = day;
    int baseCount = day < 20 ? 5 : 6;
    for (int i = 0; i < baseCount; i++) RNG.Integers.Enqueue(baseCount + 1);
    RNG.Integers.Enqueue(baseCount);
    client.OnArrival = Supply(StockSupply.GetReplacement("StoreClientList+<>c__DisplayClass51_0", "<CreateScavCrate>b__0", client.identifier));
    mod.RerollCurrentStock();
    Check(front.childItems.Count == baseCount + 3 && front.childItems.Contains(owned)
        && front.childItems.Where(i => i != owned).All(i => i.identifier == "packed_food"),
        "Crate supply changed quantities, goods or the retained owned item.");
    Check(RNG.Bounds.Count == baseCount + 1 && RNG.Bounds.All(b => b == (baseCount, baseCount + 1))
        && TableMaster.Rolls.Single() == "packedFoodTable" && client.IntroducedCalls == 0,
        "Crate supply changed native random draws or introduced the visitor again.");
}
foreach (bool roll in new[] { false, true })
{
    var (mod, client, _, front) = Visit(); Item(front);
    RNG.RollResult = roll; StoreClientList.StockMultiplier = 2f;
    client.OnArrival = Supply(StockSupply.GetReplacement("StoreClientList+<>c", "<CreateFoodThief>b__58_0", client.identifier));
    mod.RerollCurrentStock();
    Check(front.childItems.Count == (roll ? 21 : 1)
        && front.childItems.All(i => !i.Owned && i.Stolen && !i.IgnorePerk && i.ForcedHeat == 25)
        && front.childItems.Last().identifier == "nutrifruit" && StoreClientList.QuotaCharges == 0,
        "Food thief changed stolen-stock rules, guarantee, or commissary state.");
    Check(RNG.Chances.SequenceEqual(new float[] { 15, 10, 8, 8, 8, 12, 10, 10, 10, 10, 10,
        10, 10, 10, 10, 20, 10, 5, 5, 5 }.Select(c => c * 2)),
        "Food thief changed native random chances or their ordering.");
}

// The registered supplier IDs must select the native stock-only method rather
// than replaying the captured callback's OnIntroduced side effect.
foreach (var (id, family, helper) in new[]
{
    ("scavGenericFoodSeller", "scav", "AddScavFoodStock"),
    ("scavGenericHouseholdSeller", "scav", "AddScavHouseholdStock"),
    ("scavGenericMaterialSeller", "scav", "AddScavMaterialStock"),
    ("scavGenericMedSeller", "scav", "AddScavMedStock"),
    ("thiefGenericFoodSeller", "thief", "AddThiefFoodStock"),
    ("thiefGenericHouseholdSeller", "thief", "AddThiefHouseholdStock"),
    ("thiefGenericMaterialSeller", "thief", "AddThiefMaterialStock"),
    ("thiefGenericMedSeller", "thief", "AddThiefMedStock"),
})
{
    var replacement = family == "scav"
        ? StockSupply.GetReplacement("StoreClientListSupplier+<>c__DisplayClass16_0", "<CreateScavGenericSeller>b__0", id)
        : StockSupply.GetReplacement("StoreClientListSupplier+<>c__DisplayClass27_0", "<CreateThiefGenericSeller>b__0", id);
    Check(replacement?.Method.DeclaringType == typeof(StoreClientListSupplier) && replacement.Method.Name == helper,
        "Supplier selected a different stock family or retained visit side effects: " + id);
}

// First and subsequent lottery visits replace sale stock without duplicating
// the introduction leaflet. Purchased goods and visit terms remain intact.
foreach (string callback in new[] { "<CreateLotteryRep>b__86_2", "<CreateLotteryRep>b__86_3" })
{
    var (mod, client, _, front) = Visit(); var owned = Item(front, true); Item(front);
    client.OnArrival = Supply(StockSupply.GetReplacement("StoreClientList+<>c", callback, "lotteryRep"));
    mod.RerollCurrentStock(); mod.RerollCurrentStock();
    Check(front.childItems.Count == 16 && front.childItems.Contains(owned)
        && front.childItems.Count(item => item.identifier == "lottery_ticket") == 10
        && front.childItems.Count(item => item.identifier == "scratcher") == 5,
        "Lottery reroll duplicated a reward, changed sale quantities or lost purchased goods.");
    Check(client.IntroducedCalls == 0 && client.wholesaleDiscount == -35, "Lottery reroll changed visit state.");
}

// Keep the native inspection-family choice and day thresholds while avoiding
// another introduction. The item factories are API stand-ins, not generators.
foreach (var (day, count, choice, expected) in new[]
    { (0, 1, 0, "stamp"), (15, 2, 1, "cigarette"), (40, 3, 2, "injector"), (75, 4, 0, "stamp") })
{
    var (mod, client, _, front) = Visit(); var owned = Item(front, true); Item(front);
    PlayerStore.instance.isStampIntroduced = true;
    PlayerStore.instance.isVisualInspectionIntroduced = true;
    PlayerStore.instance.isInjectorIntroduced = true;
    StoreStation.Instance.dayCounter = day; RNG.Integers.Enqueue(choice);
    client.OnArrival = Supply(StockSupply.GetReplacement("StoreClientList+<>c__DisplayClass54_0", "<CreateInsScav>b__0", "scavInspectable"));
    mod.RerollCurrentStock();
    Check(front.childItems.Count == count + 1 && front.childItems.Contains(owned)
        && front.childItems.Where(item => item != owned).All(item => item.identifier == expected)
        && client.IntroducedCalls == 0 && client.wholesaleDiscount == -35,
        "Inspectable supplier changed its stock family, day threshold, goods ownership or visit state.");
}

// Main-menu shortcuts must never invoke the singleton getter that creates a run.
{
    var (mod, _, _, _) = Visit(); PlayerStore.instance = null; PlayerStore.SingletonCreations = 0;
    mod.RerollCurrentStock(); Check(PlayerStore.instance == null && PlayerStore.SingletonCreations == 0, "Main-menu key created a run.");
}

// The actual hotkey parser and focus guard run against input/UI stand-ins.
{
    Check(RerollHotkey.TryParse("Ctrl+R", out var hotkey), "Combination key failed to parse.");
    Input.Down = KeyCode.R; Input.Held.Add(KeyCode.LeftControl);
    Check(hotkey.WasPressedThisFrame(), "Combination key did not fire.");
    Input.Held.Add(KeyCode.LeftShift); Check(!hotkey.WasPressedThisFrame(), "Unexpected modifier activated a key.");
    Check(!RerollHotkey.TryParse("9999", out _) && !RerollHotkey.TryParse("F5+F6", out _), "Invalid shortcut was accepted.");
    Check(RerollHotkey.TryParse("None", out var none) && !none.WasPressedThisFrame(), "None activated a key.");
    Application.isFocused = false; Check(TextInputFocus.ShouldSuppressHotkeys(), "Unfocused game accepted shortcuts."); Application.isFocused = true;
    var selected = new GameObject(); selected.AddComponent<TMPro.TMP_InputField>().isFocused = true;
    UnityEngine.EventSystems.EventSystem.current = new() { currentSelectedGameObject = selected };
    Check(TextInputFocus.ShouldSuppressHotkeys(), "TMP editing did not suppress shortcuts.");
    selected.GetComponentInParent<TMPro.TMP_InputField>().isFocused = false;
    Check(TextInputFocus.ShouldSuppressHotkeys(), "Submit frame was not consumed.");
    Time.frameCount++; UnityEngine.Object.Destroy(selected);
    Check(!TextInputFocus.ShouldSuppressHotkeys(), "Destroyed input focus broke shortcuts.");
    CustomUIManager.Instance = new() { Open = true };
    Check(TextInputFocus.ShouldSuppressHotkeys(), "Binding capture activated another shortcut.");
}

// Exercise the real full-release Tick -> focus guard -> reroll transaction.
// SuppressHotkeys is the native API boundary; capture lifetime belongs to the game.
{
    var (mod, client, _, front) = Visit(); Item(front);
    int generated = 0;
    client.OnArrival = Supply(() => { generated++; Item(front); });
    CustomUIManager.Instance = null;
    UnityEngine.EventSystems.EventSystem.current = null;
    Input.Held.Clear(); Input.Down = KeyCode.F5; Time.frameCount++;
    KeyBindHelper.SuppressHotkeys = true;
    mod.Tick();
    Check(generated == 0, "Native binding capture rerolled actual stock.");
    // Native KeyBindHelper keeps this API true when capture ends this frame.
    Time.frameCount++;
    mod.Tick();
    Check(generated == 0, "Native capture-end frame rerolled actual stock.");
    KeyBindHelper.SuppressHotkeys = false; Time.frameCount++;
    mod.Tick();
    Check(generated == 1, "The frame after native capture did not restore the reroll hotkey.");

    var selected = new GameObject();
    var tmp = selected.AddComponent<TMPro.TMP_InputField>(); tmp.isFocused = true;
    UnityEngine.EventSystems.EventSystem.current = new() { currentSelectedGameObject = selected };
    Time.frameCount++; mod.Tick();
    Check(generated == 1, "TMP text editing rerolled actual stock.");
    tmp.isFocused = false; mod.Tick();
    Check(generated == 1, "TMP submit/blur frame rerolled actual stock.");
    Time.frameCount++; mod.Tick();
    Check(generated == 2, "Normal stock hotkey remained blocked after the text submit frame.");

    var legacy = selected.AddComponent<UnityEngine.UI.InputField>(); legacy.isFocused = true;
    Time.frameCount++; mod.Tick();
    Check(generated == 2, "Legacy text editing rerolled actual stock.");
    legacy.isFocused = false; mod.Tick();
    Check(generated == 2, "Legacy text submit frame rerolled actual stock.");
    Time.frameCount++; Input.compositionString = "拼"; mod.Tick();
    Check(generated == 2, "IME composition rerolled actual stock.");
    Input.compositionString = ""; mod.Tick();
    Check(generated == 2, "IME completion frame rerolled actual stock.");
    Time.frameCount++; StoreUIManager.Editing = true; mod.Tick();
    Check(generated == 2, "Native text focus rerolled actual stock.");
    StoreUIManager.Editing = false; mod.Tick();
    Check(generated == 2, "Native text submit frame rerolled actual stock.");
    Time.frameCount++; Application.isFocused = false; mod.Tick();
    Check(generated == 2, "Unfocused application rerolled actual stock.");
    Application.isFocused = true; Time.frameCount++; mod.Tick();
    Check(generated == 3, "Normal hotkey did not recover after input guards.");
    UnityEngine.Object.Destroy(selected);
    UnityEngine.EventSystems.EventSystem.current = null; Input.Down = KeyCode.None;
}
currentMod?.OnDisable();

if (args.Length != 2) throw new ArgumentException("Pass built StockReroll.dll and full-release Assembly-CSharp.dll.");
using (var binary = AssemblyDefinition.ReadAssembly(args[0]))
using (var game = AssemblyDefinition.ReadAssembly(args[1]))
{
    Check(!binary.MainModule.AssemblyReferences.Any(reference => reference.Name.Contains("Il2Cpp") || reference.Name == "MelonLoader"),
        "Official binary references the Demo loader.");
    Check(binary.MainModule.Types.Count(type => type.Interfaces.Any(i => i.InterfaceType.Name == "IMod")) == 1,
        "Package does not have exactly one official entry point.");
    var patchTargets = new HashSet<string>();
    foreach (var type in binary.MainModule.Types)
    foreach (var attribute in type.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
    {
        var target = (TypeReference)attribute.ConstructorArguments[0].Value;
        string method = (string)attribute.ConstructorArguments[1].Value;
        patchTargets.Add(target.FullName + "." + method);
        Check(game.MainModule.GetType(target.FullName)?.Methods.Any(m => m.Name == method) == true,
            "Patch target is missing: " + target.FullName + "." + method);
        bool uiGuard = target.FullName == "NegociationUIManager" && method == "OpenUI";
        Check(type.Methods.Where(m => m.Name == "Prefix").All(m => m.ReturnType.FullName == (uiGuard ? "System.Boolean" : "System.Void")),
            "Only the scoped trade-window guard may suppress a native method: " + type.Name);
    }
    Check(patchTargets.SetEquals(new[] { "DialogUIManager.StartDialogue", "DialogUIManager.ContinueConversation",
        "PlayerStore.OnItemBought", "PlayerStore.SellItem", "PlayerStore.BuyAllItemFromTable",
        "AppraisalServiceHelper.GiveAppraisalFee", "NegociationUIManager.OpenUI", "ModConfigWindow.Open" }),
        "Harmony scope must stay limited to the six observation targets, trade-window guard and config header localization.");
    var policy = Assembly.LoadFile(Path.GetFullPath(args[0])).GetType("StockReroll.StockCallbackPolicy", true);
    var callbacks = (HashSet<string>)policy.GetField("SupplyCallbacks", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    // These full-release suppliers were absent even though every old policy
    // entry still existed. Check coverage against the production policy.
    foreach (string callback in new[]
    {
        "StoreClientListSupplier+<>c.<CreateHypertechSmuggler>b__32_0",
        "StoreClientListSupplier+<>c.<CreateCourrierBeer>b__0_0",
        "StoreClientListSupplier+<>c.<CreateCourrierMining>b__2_0",
        "StoreClientListSupplier+<>c.<CreateCourrierChem>b__4_0",
        "StoreClientListSupplier+<>c.<CreateCourrierPharma>b__6_0",
        "StoreClientListSupplier+<>c__DisplayClass16_0.<CreateScavGenericSeller>b__0",
        "StoreClientListSupplier+<>c__DisplayClass27_0.<CreateThiefGenericSeller>b__0",
        "StoreClientListSpec+<>c.<CreateRetiredAquafarmer>b__6_0",
        "StoreClientListSpec+<>c.<CreateScalper>b__15_1",
        "StoreClientListAppraisal+<>c.<CreateAppraisalRandom>b__3_0",
        "StoreClientListWanted+<>c.<CreateWanted1Return>b__5_0",
        "StoreClientListWanted+<>c.<CreateWanted2Return>b__9_0",
        "StoreClientUniqueList+<>c.<CreateGeorgioLuckSale>b__8_0",
        "StoreClientUniqueList+<>c.<Nellis3>b__86_0",
        "StoreClientUniqueList+<>c.<GrandpaCheng3>b__92_0",
        "StoreClientList+<>c__DisplayClass54_0.<CreateInsScav>b__0",
        "StoreClientList+<>c.<CreateLotteryRep>b__86_2",
        "StoreClientList+<>c.<CreateLotteryRep>b__86_3",
    })
        Check(callbacks.Contains(callback), "Full-release supplier cannot reroll: " + callback);
    Check(callbacks.Count == 110, "The reviewed supply policy lost entries.");
    foreach (string callback in callbacks)
    {
        int separator = callback.LastIndexOf('.');
        var type = game.MainModule.GetType(callback[..separator].Replace('+', '/'));
        var method = type?.Methods.SingleOrDefault(m => m.Name == callback[(separator + 1)..]);
        Check(method != null && method.Parameters.Count == 0 && method.ReturnType.FullName == "System.Void",
            "Native callback is missing or changed: " + callback);
        var calls = method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToList();
        Check(calls.Any(m => m.Name is "AddDirectSellingItemToTable" or "PlaceSupplierInventory" or "PlaceInventorInventory" or "AddScavGeneralStock"
            || m.DeclaringType.Name == "StoreClientListSupplier" && m.Name is "AddScavFoodStock" or "AddThiefFoodStock"),
            "Approved action no longer supplies stock: " + callback);
        bool writesVisitState = method.Body.Instructions.Any(i => i.OpCode.Code == DseCecil.Cil.Code.Stfld && i.Operand is FieldReference field
            && field.DeclaringType.Name is "PlayerStore" or "StoreClientData" or "StoreStation");
        Check(!writesVisitState || StockSupply.GetReplacement(type.FullName.Replace('/', '+'), method.Name, "lotteryRep")?.Method.Name == "PlaceLotteryStock",
            "Approved action writes visit/story state: " + callback);
    }
}
Console.WriteLine($"Passed {assertions} official Stock Reroll checks (managed fixtures and binary metadata; no game process).");
