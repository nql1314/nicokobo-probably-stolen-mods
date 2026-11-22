using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const string GraylineActor = "nicokobo.aug.grayline.supplier";
    private const string GraylineStoryActor = "nicokobo.aug.story.visitor";
    private const string GraylineMaterial = "nicokobo.mechcore.synthesis.";
    private readonly JArray _graylineActions = new();
    private object GraylineFlow => Reflect.Get(AugMod, "Story") ?? throw new InvalidOperationException("Aug story flow is absent");
    private JObject GraylineStoryState() => (JObject)Reflect.Json(Reflect.Get(GraylineFlow, "State"));
    private JObject GraylineSupplyState() => (JObject)Reflect.Json(Reflect.Get(GraylineFlow, "_graylineState"));

    // This mode starts a disposable MODDED Aug run with InitialCredits=10000
    // loaded from the Mod's supported scoped config.json before InitialSave.
    // Buttons and GraphUtils transfers below are programmatic, not mouse input.
    internal IEnumerable<object?> Grayline()
    {
        R.environment["graylineScope"] = "Generated D1 live E01/E02 visitors and immediate current supply appointment using shipped test entries; no nine-day calendar or customer wait. Programmatic native UI buttons and GraphUtils.TryAcceptOnce; explicit real-API duplicate attempts. Actual stock factories, native purchase guards, prices, receipts and ES3 remain under test. Test visitors and queue preparation are explicit input fixtures; no completed choices or purchase receipts are assigned.";
        R.environment["graylineActions"] = _graylineActions;
        foreach (object? wait in GraylineWait("grayline.opening-ready", () => AugReady() && Reflect.Get(GraylineFlow, "State") != null)) yield return wait;
        object run = AugState()!, opening = Reflect.Get(run, "Opening")!;
        JObject story = GraylineStoryState();
        GraylineRequire("grayline.isolated-natural-opening", "D1 committed mechanical-ascension opening; configured and actual 10000 credits; ordinary story calendar", new
        {
            day = StoreStation.GetDayCounter(), Store.playerCash, config = Reflect.Json(Reflect.Get(run, "ConfigSnapshot")), story,
            start = Reflect.Str(run, "StartId"), startDay = Reflect.Int(run, "StartDay"), openingStartDay = Reflect.Int(opening, "StartDay"), Store.runID, Store.saveSlotId
        }, StoreStation.GetDayCounter() == 1 && Reflect.Int(run, "StartDay") == 1 && Reflect.Int(opening, "StartDay") == 1 &&
            Reflect.Str(run, "StartId") == "nicokobo.aug.mechanical_ascension.start" &&
            Reflect.Int(Reflect.Get(run, "ConfigSnapshot")!, "InitialCredits") == 10000 && Store.playerCash == 10000 &&
            (bool?)story["CatchUpOpening"] == false && ((JArray)story["Choices"]!).Count == 0);
        var speed = Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp");
        Reflect.Call(speed, "RestoreNormalSpeed");
        yield return null;
        GraylineRequire("grayline.normal-speed", "Normal preset, no instant dialogue/global acceleration", Reflect.Json(new
        {
            Time.timeScale, instant = Reflect.Get(speed, "Instant"), global = Reflect.Get(speed, "GlobalEnabled"),
            dialogue = Reflect.Get(speed, "Multiplier"), queue = Reflect.Get(speed, "QueueMultiplier")
        }), Time.timeScale == 1f && !(bool)Reflect.Get(speed, "Instant")! && !(bool)Reflect.Get(speed, "GlobalEnabled")! &&
            new[] { "Multiplier", "QueueMultiplier", "ClientArriveSpeed", "ClientLeaveSpeed" }.All(name => Convert.ToSingle(Reflect.Get(speed, name)) == 1f));

        using var observation = new GraylineObservation(this);
        R.environment["graylineObservation"] = observation.Evidence;
        foreach (object? wait in GraylineOpen()) yield return wait;
        foreach (var stage in new[] { (Event: "E01", Choice: "investigate", Delta: -60), (Event: "E02", Choice: "accept", Delta: 0) })
        {
            bool requested = (bool)Reflect.Call(GraylineFlow, "RequestTestVisit", stage.Event, true)!;
            GraylineRequire("grayline.fixture.live-visit." + stage.Event, true, requested, requested);
            var label = FullStoryChoiceLabels(stage.Event, stage.Choice, GraylineStoryState());
            foreach (object? wait in GraylineStoryVisit(stage.Event, stage.Choice, label.Zh, label.En, stage.Delta)) yield return wait;
        }
        bool supplyRequested = (bool)Reflect.Call(GraylineFlow, "RequestGraylineDebugVisit")!;
        GraylineRequire("grayline.fixture.immediate-current-appointment", true, supplyRequested, supplyRequested);
        foreach (object? wait in GraylineServeUntil(() => Store.currentClientInstance?.GetClientBlueprint()?.identifier == GraylineActor &&
            Store.isClientArrived && Reflect.Get(GraylineFlow, "_graylineState") != null &&
            (string?)GraylineSupplyState()["VisitState"] == "arrived", GraylineActor, "grayline.fixture.supplier-arrival")) yield return wait;
        JObject initialSupply = GraylineSupplyState();
        GraylineRequire("grayline.generated-current-appointment", "E02 on D1 unlocks original Tuesday D2 stock; explicit immediate D1 visit; no purchase receipts", initialSupply,
            (bool?)initialSupply["Unlocked"] == true && (int?)initialSupply["BookedDay"] == 1 && (int?)initialSupply["DueDay"] == 2 &&
            (int?)initialSupply["DebugVisitDay"] == StoreStation.GetDayCounter() && ((JArray)initialSupply["Receipts"]!).Count == 0 &&
            (string?)initialSupply["GameRunId"] == Store.runID && (int?)initialSupply["SaveSlotId"] == Store.saveSlotId);
        foreach (object? wait in GraylineFinishLine("grayline.supplier-introduction")) yield return wait;
        GraylineStowOwnedCounter();
        object[] offers = Reflect.Each(Reflect.Get(GraylineFlow, "_graylineCounter")).Select(pair => Reflect.Get(pair, "Value")!).ToArray();
        var selected = offers.Select(offer => (Item: (GameItem)Reflect.Get(offer, "Item")!, Operation: Reflect.Str(offer, "Operation")))
            .Where(x => x.Item.identifier == GraylineMaterial + "quartz" || x.Item.identifier == GraylineMaterial + "titanium_ore")
            .Where(x => ReferenceEquals(x.Item.parentInventory, EmporiumEntry.Instance.frontInvinvElement) && !GeneralHelper.IsItemOwned(x.Item))
            .OrderBy(x => x.Item.GetNegociatedValue()).ThenBy(x => x.Item.uniqueId).Take(3).ToArray();
        GraylineRequire("grayline.three-natural-offers", "Three real unowned ore units with unique UIDs/operations, enough actual cash", selected.Select(x => new { item = ItemState(x.Item), x.Operation }).ToArray(),
            selected.Length == 3 && selected.All(x => x.Item.unitCount == 1 && x.Item.uniqueId > 0 && Guid.TryParse(x.Operation, out _) && x.Item.GetNegociatedValue() is >= 0 and <= int.MaxValue) &&
            selected.Select(x => x.Item.uniqueId).Distinct().Count() == 3 && selected.Select(x => x.Operation).Distinct().Count() == 3 &&
            selected.Sum(x => x.Item.GetNegociatedValue()) <= Store.playerCash);
        GameItem a = selected[0].Item, b = selected[1].Item, c = selected[2].Item;
        var operations = selected.ToDictionary(x => x.Item.uniqueId, x => x.Operation);
        foreach (var item in selected) observation.PurchaseUids.Add(item.Item.uniqueId);

        GraylineTransfer(a, EmporiumEntry.Instance.backInvinvElement, "single-to-weighing-counter");
        GraylineRequire("grayline.from-table-real-selection", "Native transfer selected A; native CanBuyItemFromTable accepts", ItemState(a),
            ReferenceEquals(Store.currentNegociatedItem, a) && Store.CanBuyItemFromTable() && !NegociationUIManager.IsTradeLocked());
        GraylinePurchase("grayline.from-table-purchase", new[] { a }, operations, () => Store.BuyItemFromTable(), observation);
        GraylineReject("grayline.repeat-BuyItem", new[] { a }, () => Store.BuyItem(a), observation);
        GraylineTransfer(a, EmporiumEntry.Instance.backInvinvElement, "owned-A-back-to-weighing-counter");
        GraylineRequire("grayline.repeat-from-table-reaches-guard", "Owned A remains eligible for the native table gate; Aug must reject payment", ItemState(a),
            ReferenceEquals(Store.currentNegociatedItem, a) && Store.CanBuyItemFromTable() && GeneralHelper.IsItemOwned(a));
        GraylineReject("grayline.repeat-BuyItemFromTable", new[] { a }, () => Store.BuyItemFromTable(), observation);
        GraylineTransfer(a, EmporiumEntry.Instance.invElement, "owned-A-to-storage");

        GraylineTransfer(b, EmporiumEntry.Instance.backInvinvElement, "bulk-B-to-weighing-counter");
        GraylineTransfer(c, EmporiumEntry.Instance.backInvinvElement, "bulk-C-to-weighing-counter");
        GraylineReject("grayline.duplicate-reference-bulk", new[] { b, c }, () => NegociationUIManager.Instance.BuyMultipleItem(new List<GameItem> { b, b }), observation);
        NegociationUIManager.Instance.OpenUI();
        string bulkLabel = LocHelper.GetLocalizedUI("ui_nego_buy_items", 2);
        foreach (object? wait in GraylineWait("grayline.actual-bulk-button", () => Reflect.Get(NegociationUIManager.Instance, "primaryButton") is TabletButton button &&
            button.gameObject.activeInHierarchy && button.Interactable && button.button != null && button.button.IsInteractable() && button.label.text == bulkLabel)) yield return wait;
        var bulkButton = (TabletButton)Reflect.Get(NegociationUIManager.Instance, "primaryButton")!;
        GraylineRequire("grayline.only-B-C-in-native-buy-list", new[] { b.uniqueId, c.uniqueId }.OrderBy(x => x).ToArray(),
            EmporiumEntry.Instance.GetBackInvItems().Where(item => !GeneralHelper.IsItemOwned(item)).Select(item => item.uniqueId).OrderBy(x => x).ToArray(),
            EmporiumEntry.Instance.GetBackInvItems().Where(item => !GeneralHelper.IsItemOwned(item)).Select(item => item.uniqueId).OrderBy(x => x)
                .SequenceEqual(new[] { b.uniqueId, c.uniqueId }.OrderBy(x => x)));
        GraylinePurchase("grayline.native-bulk-button-purchase", new[] { b, c }, operations, () => bulkButton.button.onClick.Invoke(), observation);
        GraylineReject("grayline.repeat-purchased-bulk", new[] { b, c }, () => NegociationUIManager.Instance.BuyMultipleItem(new List<GameItem> { b, c }), observation);
        foreach (GameItem item in new[] { a, b, c }) { GraylineTransfer(item, EmporiumEntry.Instance.invElement, "purchased-to-storage"); Retain(item); }
        GraylineRequire("grayline.fixture-three-native-purchases", new { nights = 0, purchases = 3 }, observation.Evidence,
            observation.Nights.Count == 0 && observation.Buys.Count == 3 && observation.ObserverErrors.Count == 0 &&
            observation.PurchaseUids.SetEquals(observation.Buys.Select(x => (int)x["uid"]!)));
        var purchased = new List<GameItem> { a, b, c };
        foreach (object? wait in GraylineFailureRecovery(observation, purchased)) yield return wait;
        R.persistence["grayline"] = new JObject
        {
            ["fixtureMode"] = true, ["recoveryTest"] = true, ["day"] = StoreStation.GetDayCounter(), ["cash"] = Store.playerCash, ["story"] = GraylineStoryState(),
            ["supply"] = GraylineSupplyState(), ["purchasedItems"] = new JArray(purchased.OrderBy(x => x.uniqueId).Select(ItemState)),
            ["naturalNights"] = observation.Nights.DeepClone(), ["nativePurchases"] = observation.Buys.DeepClone()
        };
    }

    internal IEnumerable<object?> GraylineReload(JObject baseline)
    {
        var expected = baseline["persistence"]?["grayline"] as JObject ?? throw new InvalidOperationException("Missing Grayline save baseline");
        foreach (object? wait in GraylineWait("grayline.reload-state", () => AugReady() && Reflect.Get(GraylineFlow, "State") != null && Reflect.Get(GraylineFlow, "_graylineState") != null)) yield return wait;
        var expectedSupply = (JObject)expected["supply"]!.DeepClone(); var expectedStory = (JObject)expected["story"]!.DeepClone();
        if (O.UseCopy) { expectedSupply["SaveSlotId"] = Store.saveSlotId; expectedStory["SaveSlotId"] = Store.saveSlotId; }
        int[] uids = ((JArray)expected["purchasedItems"]!).Select(x => (int)x["uid"]!).ToArray();
        var items = Store.FindAllItem().Where(item => uids.Contains(item.uniqueId)).Distinct().OrderBy(item => item.uniqueId).ToArray();
        GraylineRequire("grayline.reload-actual-purchases-and-journals", expected, new
        {
            day = StoreStation.GetDayCounter(), cash = Store.playerCash, story = GraylineStoryState(), supply = GraylineSupplyState(), items = items.Select(ItemState).ToArray()
        }, (bool?)expected["fixtureMode"] == true && (bool?)expected["recoveryTest"] == true &&
            (int?)expected["day"] == 3 && StoreStation.GetDayCounter() == 3 && Store.playerCash == (int)expected["cash"]! &&
            ((JArray)expected["naturalNights"]!).Count == 2 && ((JArray)expected["nativePurchases"]!).Count == 4 && items.Length == 4 &&
            items.All(GeneralHelper.IsItemOwned) && JToken.DeepEquals(expected["purchasedItems"], new JArray(items.Select(ItemState))) &&
            JToken.DeepEquals(expectedSupply, GraylineSupplyState()) && JToken.DeepEquals(expectedStory, GraylineStoryState()),
            "New-process normal LoadGame/ES3 readback; copied slots may rebind only SaveSlotId. No story/supply replay is invoked.");
    }

    private IEnumerable<object?> GraylineOpen(bool clearAfterOpen = true)
    {
        FixtureClearVisitors("prepare-open-store");
        foreach (object? wait in GraylineWait("grayline.morning-ready", () => Store.storeState == PlayerStore.StoreState.MORNING &&
            Store.lockShutterButtonFlag == 0 && !StoreUIManager.Instance.blackscreen.gameObject.activeSelf)) yield return wait;
        if (AdvCalendarUIManager.Instance.UIPanel.activeSelf) AdvCalendarUIManager.Instance.CloseUI();
        if (HandnoteUIManager.Instance.panel.activeSelf) HandnoteUIManager.Instance.ClosePanel();
        GraylineRequire("grayline.can-open", "Actual native open eligibility", GraylineUiState(), Store.CanStartDay() && !Store.isClientBeingArrested && !Store.isHandlingShootout);
        GraylineAction("shutter-button-open"); StoreShutterButton.Instance.OnClick();
        foreach (object? wait in GraylineWait("grayline.open", () => Store.storeState == PlayerStore.StoreState.OPEN && Store.lockShutterButtonFlag == 0)) yield return wait;
        if (clearAfterOpen) FixtureClearVisitors("opened-store-before-explicit-test-customer");
    }

    private IEnumerable<object?> GraylineStoryVisit(string eventId, string option, string chinese, string english, int delta)
    {
        foreach (object? wait in GraylineServeUntil(() => Store.currentClientInstance?.GetClientBlueprint()?.identifier == GraylineStoryActor && Store.isClientArrived &&
            (string?)GraylineStoryState()["ActiveEventId"] == eventId && (int?)GraylineStoryState()["EncounterDay"] == StoreStation.GetDayCounter(),
            GraylineStoryActor, "grayline." + eventId + ".natural-arrival")) yield return wait;
        foreach (object? wait in GraylineWaitChoice(chinese, english, eventId)) yield return wait;
        int cash = Store.playerCash;
        int titanium = Store.FindAllItem().Where(item => item.identifier == GraylineMaterial + "titanium_alloy" && GeneralHelper.IsItemOwned(item)).Sum(item => item.unitCount);
        GraylineClickChoice(chinese, english, eventId);
        foreach (object? wait in GraylineWait("grayline." + eventId + ".real-commit", () => ((JArray)GraylineStoryState()["Choices"]!).Any(x => (string?)x["EventId"] == eventId && (string?)x["OptionId"] == option))) yield return wait;
        JObject state = GraylineStoryState(); string[] keys = eventId == "E01" ? new[] { "receipt", "broken" } : new[] { "index" };
        GraylineRequire("grayline." + eventId + ".payment-and-evidence", new { cash = cash + delta, keys, titanium = titanium + (eventId == "E02" ? 1 : 0) }, state,
            Store.playerCash == cash + delta && state["Pending"]?.Type == JTokenType.Null && keys.All(key =>
            {
                var receipts = ((JArray)state["Items"]!).Where(x => (string?)x["Key"] == key).ToArray();
                return receipts.Length == 1 && Store.FindAllItem().Count(item => item.uniqueId == (int)receipts[0]["Uid"]! &&
                    item.identifier == "nicokobo.aug.story.evidence." + key && item.unitCount == 1 && GeneralHelper.IsItemOwned(item)) == 1;
            }) && Store.FindAllItem().Where(item => item.identifier == GraylineMaterial + "titanium_alloy" && GeneralHelper.IsItemOwned(item)).Sum(item => item.unitCount) == titanium + (eventId == "E02" ? 1 : 0));
        foreach (object? wait in GraylineWaitChoice("结束会面", "End the visit", eventId + ".leave")) yield return wait;
        GraylineClickChoice("结束会面", "End the visit", eventId + ".leave");
        foreach (object? wait in GraylineWait("grayline." + eventId + ".departed", () => Store.currentClientInstance?.GetClientBlueprint()?.identifier != GraylineStoryActor)) yield return wait;
        GraylineStowOwnedCounter();
    }

    private IEnumerable<object?> GraylineServeUntil(Func<bool> ready, string targetActor, string name)
    {
        R.phase = name; float deadline = Time.realtimeSinceStartup + 240f;
        while (!ready())
        {
            GraylineCheckWaiting(name, deadline);
            var client = Store.currentClientInstance?.GetClientBlueprint();
            if (client == null)
            {
                if (Store.CanNextClient() && Store.lockNextBellFlag == 0 && DialogUIManager.Instance.currentDialog == null) GraylineRingBell();
                yield return null;
            }
            else if (!Store.isClientArrived || client.identifier == targetActor) yield return null;
            else foreach (object? wait in GraylineOrdinaryClient(client)) yield return wait;
        }
        R.Check(name, "Actual visitor/calendar condition", GraylineUiState(), true);
    }

    private IEnumerable<object?> GraylineOrdinaryClient(StoreClient client)
    {
        float deadline = Time.realtimeSinceStartup + 120f; bool rang = false, paidRent = false;
        JObject? mortgageBefore = null;
        JObject MortgageState() => JObject.FromObject(new
        {
            cash = Store.playerCash, Store.IsMortgageOfferDeclined, Store.isPayingMortgage, Store.IsPropertyPaid,
            Store.mortgageWeekLeft, Store.mortgageWeeklyPayment, Store.mortgageDownPaid,
            Store.mortgageTotalWeeks, Store.mortgageInterestRateTaken, Store.propertyBoughtAt
        });
        while (ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client))
        {
            GraylineCheckWaiting("grayline.ordinary." + client.identifier, deadline);
            GraylineAdvanceDialogue();
            if (GraylineChoicesVisible())
            {
                if (client.identifier == "landlord" && !paidRent)
                {
                    int rent = Store.GetRentValue(), cash = Store.playerCash;
                    string label = LocHelper.GetLocalizedDialogue("dialog_landlord_player_pay", rent);
                    if (!Store.CanPayRent() || GraylineChoice(label, label) == null) GraylineBlock("grayline.landlord", "Actual affordable pay-rent choice is unavailable; no rent/cash/choice is assigned");
                    GraylineClickChoice(label, label, "landlord-pay-rent"); paidRent = true;
                    GraylineRequire("grayline.landlord-paid-real-charge", cash - rent, Store.playerCash, Store.playerCash == cash - rent);
                }
                else if (client.identifier == "loan_shark2" && mortgageBefore == null)
                {
                    string label = LocHelper.GetLocalizedDialogue("dialog_loan_shark2_player_decline");
                    if (client.clientIntent != StoreClient.ClientIntent.DIALOGUE ||
                        DialogUIManager.Instance.currentDialog.plainText != LocHelper.GetLocalizedDialogue("dialog_loan_shark2_last_line") ||
                        GraylineChoice(label, label) == null)
                        GraylineBlock("grayline.mortgage-decline-choice", "Known loan_shark2 confirmation and exact visible decline option are required; no fallback choice is selected");
                    mortgageBefore = MortgageState();
                    GraylineRequire("grayline.mortgage-offer-before-decline", "No existing mortgage or store ownership; offer not previously declined", mortgageBefore,
                        !Store.isPayingMortgage && !Store.IsPropertyPaid && !Store.IsMortgageOfferDeclined);
                    GraylineAction("test-branch.decline-mortgage-save-up-independently", mortgageBefore);
                    GraylineClickChoice(label, label, "loan_shark2-decline-mortgage");
                    // Native CreateLoanShark2 sets the decline flag at the end
                    // of its reply, then schedules departure. Wait for that flow.
                }
                else if (!Store.IsClientDimissable()) GraylineBlock("grayline.unknown-mandatory-choice", "Unrecognized nondismissable native client choice requires a real player decision");
            }
            if (!rang && Store.IsClientDimissable() && Store.lockNextBellFlag == 0 && Reflect.Get(DialogUIManager.Instance, "displayingUncompletedDialog") == null && !DialogUIManager.Instance.isWaitingForManualAdvance)
            { GraylineRingBell(); rang = true; }
            yield return null;
        }
        if (mortgageBefore != null)
        {
            var expected = (JObject)mortgageBefore.DeepClone(); expected["IsMortgageOfferDeclined"] = true;
            JObject after = MortgageState();
            GraylineRequire("grayline.mortgage-declined-without-payment-or-loan", expected, after, JToken.DeepEquals(expected, after),
                "Actual loan_shark2 decline bubble -> native reply end action -> native departure. Only IsMortgageOfferDeclined changes; cash, mortgage terms and property ownership stay unchanged.");
        }
    }

    private IEnumerable<object?> GraylineNight(int day, int eventStart, GraylineObservation observation)
    {
        GraylineRequire("grayline.close-eligible." + day, "Native CanEndDay without Developer bypass", GraylineUiState(),
            Store.CanEndDay() && Store.lockShutterButtonFlag == 0 && !Store.isClientBeingArrested && !Store.isHandlingShootout);
        GraylineAction("shutter-button-close"); StoreShutterButton.Instance.OnClick();
        if (StoreUIManager.Instance.closeShutterConfirmationPanel.activeSelf)
        {
            GraylineRequire("grayline.close-confirmation." + day, "Visible close confirmation and native eligibility", GraylineUiState(), Store.CanEndDay());
            GraylineAction("visible-close-confirmation"); StoreShutterButton.Instance.EndAndClose();
        }
        foreach (object? wait in GraylineWait("grayline.closed." + day, () => Store.storeState == PlayerStore.StoreState.CLOSED && Store.currentClientInstance == null &&
            StoreShutterButton.Instance.shutterGameObject.transform.position == StoreShutterButton.Instance.startPosition &&
            StoreShutterButton.Instance.blockerGameObject.transform.localPosition == StoreShutterButton.Instance.blockerStartPosition)) yield return wait;
        var beds = UnityEngine.Object.FindObjectsOfType<StoreBedroll>().Where(bed => bed.gameObject.activeInHierarchy).ToArray();
        GraylineRequire("grayline.bedroll." + day, "One active native bedroll", beds.Length, beds.Length == 1);
        GraylineAction("bedroll-button"); beds[0].OnClick();
        foreach (object? wait in GraylineWait("grayline.end-day-ui." + day, () => EndOfDayUIManager.Instance.IsOpen && Reflect.Get(EndOfDayUIManager.Instance, "endDayButton") is TabletButton)) yield return wait;
        var end = (TabletButton)Reflect.Get(EndOfDayUIManager.Instance, "endDayButton")!;
        var invoice = Store.storeService.Where(service => service.unlocked && service.isUsing).Select(service => new { service.id, service.cost, service.totalCooldown }).ToArray();
        int bill = invoice.Sum(service => service.cost); int nativeStart = observation.NativeCalls.Count;
        GraylineRequire("grayline.affordable-real-end-day." + day, invoice, new { bill, Store.playerCash, end.Interactable },
            end.gameObject.activeInHierarchy && end.Interactable && end.button != null && end.button.IsInteractable() && bill <= Store.playerCash);
        observation.NightArmed = true; GraylineAction("end-day-button", new { invoice }); end.button!.onClick.Invoke();
        foreach (object? wait in GraylineWait("grayline.actual-next-morning." + day, () => StoreStation.GetDayCounter() == day + 1 &&
            Store.storeState == PlayerStore.StoreState.MORNING && !StoreUIManager.Instance.blackscreen.gameObject.activeSelf && !StoreUIManager.Instance.savingTMP.gameObject.activeSelf &&
            !EndOfDayUIManager.Instance.IsOpen && AdvCalendarUIManager.Instance.UIPanel.activeSelf && AdvCalendarUIManager.Instance.nightlyReportSection.activeSelf &&
            observation.Events.Skip(eventStart).Any(e => (string?)e["name"] == "OnWakingUpLate"), 60f)) yield return wait;
        observation.NightArmed = false;
        var events = observation.Events.Skip(eventStart).ToArray(); var calls = observation.NativeCalls.Skip(nativeStart).ToArray();
        string file = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId)); var settings = new ES3Settings(file, ES3.Location.File);
        var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", settings)!;
        int savedDay = ES3.Load<StoreStation>("storeStation", settings).dayCounter;
        var evidence = JObject.FromObject(new { from = day, to = StoreStation.GetDayCounter(), savedDay, saved.runID, saved.saveSlotId, events, calls, invoice, aug = Reflect.Json(AugState()) });
        observation.Nights.Add(evidence);
        GraylineRequire("grayline.native-night-and-auto-save." + day, "One actual EndNight/BeginDay/SaveGame; ten events in order; actual ES3 next day", evidence,
            events.Select(e => (string)e["name"]!).SequenceEqual(NightEvents) &&
            events.All(e => (int)e["day"]! == ((string)e["name"]! is "OnWakingUpEarly" or "OnWakingUpLate" ? day + 1 : day)) &&
            new[] { "EndNight", "BeginDay", "SaveGame" }.All(name => calls.Count(c => (string?)c["name"] == name && (string?)c["stage"] == "enter") == 1 &&
                calls.Count(c => (string?)c["name"] == name && (string?)c["stage"] == "return") == 1) && savedDay == day + 1 &&
            saved.runID == Store.runID && saved.saveSlotId == Store.saveSlotId && Reflect.Int(AugState()!, "LastObservedDay") == day + 1 && AugReady());
        JToken sleep = events.Single(e => (string?)e["name"] == "OnGoingSleepEarly"), charged = events.Single(e => (string?)e["name"] == "OnHandlingNightlyServicesEarly");
        GraylineRequire("grayline.real-night-bill." + day, invoice, new { sleep, charged }, (int)sleep["cash"]! - (int)charged["cash"]! == bill &&
            invoice.All(row => ((JArray)charged["services"]!).Any(service => (string?)service["id"] == row.id && (int?)service["currentCooldown"] == row.totalCooldown)));
    }

    private void GraylineTransfer(GameItem item, GameInventory destination, string action)
    {
        if (ReferenceEquals(item.parentInventory, destination)) return;
        int uid = item.uniqueId, count = item.unitCount; bool owned = GeneralHelper.IsItemOwned(item);
        GraylineRequire("grayline.transfer-eligible." + action, "Native MayRemove and valid destination slot", ItemState(item), item.MayRemove() && destination.TryFindOneValidInventorySlot(item)?.IsValid() == true);
        GraylineAction("GraphUtils.TryAcceptOnce." + action, new { uid, destination = destination.identifier });
        GraphUtils.TryAcceptOnce(destination, item);
        GraylineRequire("grayline.transfer-observed." + action, new { uid, count, owned, destination = destination.identifier }, ItemState(item),
            item.uniqueId == uid && item.unitCount == count && GeneralHelper.IsItemOwned(item) == owned && ReferenceEquals(item.parentInventory, destination),
            "Programmatic real graph transfer and native callbacks; no currentNegociatedItem/ownership assignment. GraphUtils returns remaining amount, so actual attachment is checked.");
    }
    private void GraylineStowOwnedCounter()
    {
        // Native SaveGame serializes the main/back inventory, but not the NPC
        // front counter. The opening newspaper is owned there and must be
        // admitted to durable storage before taking an exact reload baseline.
        foreach (var inventory in new[] { EmporiumEntry.Instance.frontInvinvElement,
            EmporiumEntry.Instance.backInvinvElementCounter, EmporiumEntry.Instance.backInvinvElement }.Distinct())
            foreach (var item in inventory.childItems.ToArray().Where(GeneralHelper.IsItemOwned))
                GraylineTransfer(item, EmporiumEntry.Instance.invElement, "owned-counter-to-durable-storage");
    }
    private JObject GraylineLedger(GameItem[] items, GraylineObservation observation) => new()
    {
        ["cash"] = Store.playerCash, ["supply"] = GraylineSupplyState(), ["items"] = new JArray(items.Select(ItemState)), ["nativeBuyCount"] = observation.Buys.Count
    };
    private void GraylineReject(string name, GameItem[] items, Action attempt, GraylineObservation observation)
    {
        JObject before = GraylineLedger(items, observation); GraylineAction(name, new { kind = "explicit-native-API-replay", before });
        attempt(); JObject after = GraylineLedger(items, observation);
        GraylineRequire(name, before, after, JToken.DeepEquals(before, after), "Same naturally arrived offer objects; no native OnItemBought, cash, stock, receipt, UID, ownership or graph change");
    }
    private void GraylinePurchase(string name, GameItem[] items, Dictionary<int, string> operations, Action buy, GraylineObservation observation)
    {
        int cash = Store.playerCash, eventCount = observation.Buys.Count;
        JObject before = GraylineSupplyState(); JArray oldReceipts = (JArray)before["Receipts"]!;
        var quotes = items.ToDictionary(item => item.uniqueId, item => checked((int)item.GetNegociatedValue()));
        var identities = items.ToDictionary(item => item.uniqueId, GraylineIdentity);
        GraylineRequire(name + ".eligible", "Live supplier, distinct unowned offers and affordable actual native quotes", new { cash, quotes },
            Store.currentClientInstance?.GetClientBlueprint()?.identifier == GraylineActor && Store.isClientArrived &&
            (string?)before["VisitState"] == "arrived" && items.All(item => !GeneralHelper.IsItemOwned(item)) && quotes.Values.All(price => price >= 0) && quotes.Values.Sum() <= cash);
        GraylineAction(name, new { quotes, before, entry = name.Contains("button") ? "actual TabletButton.onClick -> native OnBuyAllClick" : "actual PlayerStore.BuyItemFromTable" });
        buy(); JObject after = GraylineSupplyState(); var receipts = ((JArray)after["Receipts"]!).Skip(oldReceipts.Count).ToArray();
        var events = observation.Buys.Skip(eventCount).ToArray(); int runningCash = cash;
        bool receiptCash = true;
        foreach (JToken receipt in receipts)
        {
            var output = (JArray)receipt["OutputUids"]!; int uid = output.Count == 1 ? (int)output[0] : -1;
            if (!quotes.TryGetValue(uid, out int price)) { receiptCash = false; continue; }
            receiptCash &= (int?)receipt["CashBefore"] == runningCash && (int?)receipt["CashAfter"] == runningCash - price &&
                (int?)receipt["UnitPrice"] == price && (int?)receipt["Quantity"] == 1 && (bool?)receipt["NativeTrade"] == true &&
                (string?)receipt["OperationId"] == operations[uid] && (string?)receipt["ItemId"] == items.Single(item => item.uniqueId == uid).identifier &&
                (int?)receipt["Day"] == StoreStation.GetDayCounter(); runningCash -= price;
        }
        bool stock = ((JArray)before["Stock"]!).All(row =>
        {
            var matches = ((JArray)after["Stock"]!).Where(next => (string?)next["ItemId"] == (string?)row["ItemId"]).ToArray();
            return matches.Length == 1 && (int?)matches[0]["Remaining"] == (int)row["Remaining"]! - items.Count(item => item.identifier == (string?)row["ItemId"]) &&
                (int?)matches[0]["InitialQuantity"] == (int?)row["InitialQuantity"];
        });
        GraylineRequire(name, new { cash = cash - quotes.Values.Sum(), uids = quotes.Keys.ToArray(), quotes }, new { cash = Store.playerCash, receipts, events, supply = after, items = items.Select(ItemState).ToArray() },
            Store.playerCash == cash - quotes.Values.Sum() && runningCash == Store.playerCash && receiptCash && receipts.Length == items.Length &&
            receipts.SelectMany(receipt => ((JArray)receipt["OutputUids"]!).Select(uid => (int)uid)).OrderBy(uid => uid).SequenceEqual(quotes.Keys.OrderBy(uid => uid)) &&
            JToken.DeepEquals(oldReceipts, new JArray(((JArray)after["Receipts"]!).Take(oldReceipts.Count))) && after["Pending"]?.Type == JTokenType.Null && stock &&
            events.Length == items.Length && items.All(item => events.Count(e => (int?)e["uid"] == item.uniqueId && (int?)e["cost"] == quotes[item.uniqueId]) == 1 &&
                item.parentInventory != null && GeneralHelper.IsItemOwned(item) && JToken.DeepEquals(identities[item.uniqueId], GraylineIdentity(item)) &&
                Store.FindAllItem().Count(owned => owned.uniqueId == item.uniqueId) == 1),
            "Expected payment comes from pre-action actual quotes; native OnItemBought, independent before/after stock counts, real receipt chain and same physical UID are all required");
    }
    private static JObject GraylineIdentity(GameItem item)
    {
        var state = (JObject)ItemState(item); state.Remove("owned"); state.Remove("parentUid"); state.Remove("parentStorage"); state["unitValue"] = item.unitValue; return state;
    }

    private IEnumerable<object?> GraylineFinishLine(string name)
    {
        foreach (object? wait in GraylineWait(name, () => DialogUIManager.Instance.currentDialog == null))
        {
            GraylineAdvanceDialogue();
            if (GraylineChoicesVisible()) GraylineBlock(name, "Unexpected choice while waiting for the actual supplier dialogue to finish");
            yield return wait;
        }
    }
    private IEnumerable<object?> GraylineWaitChoice(string chinese, string english, string name)
    {
        foreach (object? wait in GraylineWait("grayline.choice." + name, () => GraylineChoice(chinese, english) != null, 120f))
        { GraylineAdvanceDialogue(); yield return wait; }
    }
    private bool GraylineChoicesVisible() => DialogUIManager.Instance.currentDialog != null && Reflect.Get(DialogUIManager.Instance, "displayingUncompletedDialog") == null &&
        DialogUIManager.Instance.dialogChoiceBubbles.Any(bubble => bubble.gameObject.activeInHierarchy);
    private DialogChoiceBubble? GraylineChoice(string chinese, string english)
    {
        if (!GraylineChoicesVisible()) return null;
        var dialog = DialogUIManager.Instance;
        var choices = dialog.dialogChoiceBubbles.Where(bubble => bubble.gameObject.activeInHierarchy && bubble.IsInteractable() && bubble.type == 0 &&
            bubble.index >= 0 && bubble.index < dialog.currentDialog.choices.Count && dialog.currentDialog.IsChoiceEnabledAt(bubble.index) &&
            (dialog.currentDialog.choices[bubble.index].Item2.plainText == chinese || dialog.currentDialog.choices[bubble.index].Item2.plainText == english)).ToArray();
        return choices.Length == 1 ? choices[0] : null;
    }
    private void GraylineClickChoice(string chinese, string english, string name)
    {
        var choice = GraylineChoice(chinese, english) ?? throw new InvalidOperationException("Actual visible enabled choice missing: " + name);
        GraylineAction("dialogue-button." + name, new { choice.index, label = choice.TMP.text }); choice.OnDialogChoiceClick();
    }
    private void GraylineAdvanceDialogue()
    {
        var dialog = DialogUIManager.Instance;
        // The native Advance button finishes text and its guarded end actions.
        // Choice callbacks remain separate; no typewriter/calendar wait is needed.
        if (dialog.currentDialog != null && !dialog.IsChoice()) dialog.OnAdvancePressed();
    }
    private void GraylineRingBell()
    {
        GraylineRequire("grayline.bell-native-eligibility", "OPEN, bell unlocked, empty client or genuine dismissability", GraylineUiState(),
            Store.storeState == PlayerStore.StoreState.OPEN && Store.lockNextBellFlag == 0 &&
            (Store.currentClientInstance == null ? Store.CanNextClient() : Store.IsClientDimissable()));
        var bells = UnityEngine.Object.FindObjectsOfType<StoreBell>().Where(bell => bell.gameObject.activeInHierarchy).ToArray();
        GraylineRequire("grayline.actual-bell", "One active native bell", bells.Length, bells.Length == 1);
        GraylineAction("store-bell-button"); bells[0].OnClick();
    }
    private IEnumerable<object?> GraylineWait(string name, Func<bool> ready, float seconds = 30f)
    {
        R.phase = name; float deadline = Time.realtimeSinceStartup + seconds;
        while (!ready()) { GraylineCheckWaiting(name, deadline); yield return null; }
        R.Check(name, "Actual native condition reached", GraylineUiState(), true);
    }
    private void GraylineCheckWaiting(string name, float deadline)
    {
        if (GameOverUIManager.Instance?.IsShowing == true) GraylineBlock(name, "Native game-over reached; no health/rent/date bypass");
        if (Time.realtimeSinceStartup >= deadline) GraylineBlock(name, "Bounded wait expired; the native UI/visitor condition was not observed");
    }
    private void GraylineRequire(string name, object? expected, object? actual, bool passed, string evidence = "")
    {
        R.Check(name, expected, actual, passed, evidence);
        if (!passed) { R.environment["graylineBlocker"] = GraylineUiState(); throw new InvalidOperationException(name + " failed; no successful Grayline baseline may be saved"); }
    }
    private void GraylineBlock(string name, string reason)
    {
        R.environment["graylineBlocker"] = GraylineUiState(); R.Skip(name, reason);
        throw new InvalidOperationException(name + ": " + reason);
    }
    private object GraylineUiState()
    {
        object? aug = AugState();
        return new
        {
            day = StoreStation.GetDayCounter(), cash = Store.playerCash, state = Store.storeState.ToString(), Time.timeScale, Store.devMode,
            Store.lockNextBellFlag, Store.lockShutterButtonFlag, Store.isClientArrived, Store.isClientBeingArrested, Store.isHandlingShootout,
            client = Store.currentClientInstance?.GetClientBlueprint()?.identifier, dismissable = Store.IsClientDimissable(), canEndDay = Store.CanEndDay(),
            queue = Store.storeClientManager.clientStack.Select(client => new { client.identifier, client.dismissable, intent = client.clientIntent.ToString() }).ToArray(),
            dialogue = DialogUIManager.Instance?.currentDialog?.plainText,
            choices = DialogUIManager.Instance?.currentDialog?.choices.Select(choice => choice.Item2.plainText).ToArray(),
            health = aug == null ? (int?)null : Reflect.Int(aug, "Health"), exposure = aug == null ? (int?)null : Reflect.Int(aug, "Exposure"),
            lastObservedDay = aug == null ? (int?)null : Reflect.Int(aug, "LastObservedDay"), failure = aug == null ? null : Reflect.Str(aug, "FailureReason")
        };
    }
    private void GraylineAction(string action, object? detail = null) => _graylineActions.Add(JObject.FromObject(new
    { action, day = StoreStation.GetDayCounter(), frame = Time.frameCount, cash = Store.playerCash, client = Store.currentClientInstance?.GetClientBlueprint()?.identifier, detail }));

    // Passive observers are scoped to this iterator and never replace behavior,
    // suppress a failure or manufacture a game-side completion receipt.
    private sealed class GraylineObservation : IDisposable
    {
        private static GraylineObservation? Active;
        private readonly ProbeContext _context;
        private readonly Harmony _harmony;
        private readonly List<MethodInfo> _patched = new();
        private readonly List<(EventInfo Event, Action Handler)> _handlers = new();
        internal readonly HashSet<int> PurchaseUids = new();
        internal readonly JArray Events = new(), NativeCalls = new(), Nights = new(), Buys = new(), ObserverErrors = new();
        internal readonly JObject Evidence;
        internal bool NightArmed;
        internal GraylineObservation(ProbeContext context)
        {
            if (Active != null) throw new InvalidOperationException("Grayline observation already active");
            _context = context; _harmony = new Harmony("nicokobo.content_native_probe.grayline." + context.O.RunId);
            Evidence = new JObject { ["events"] = Events, ["nativeCalls"] = NativeCalls, ["nights"] = Nights, ["buys"] = Buys, ["errors"] = ObserverErrors };
            try
            {
                foreach (string name in NightEvents)
                {
                    EventInfo hook = typeof(ModHook).GetEvent(name, Reflect.All)!;
                    Action handler = () => Observe(() => Events.Add(JObject.FromObject(new
                    {
                        name, day = StoreStation.GetDayCounter(), frame = Time.frameCount, cash = context.Store.playerCash,
                        services = context.Store.storeService.Where(service => service.unlocked && service.isUsing).Select(service => new { service.id, service.currentCooldown }).ToArray()
                    })));
                    hook.AddEventHandler(null, handler); _handlers.Add((hook, handler));
                }
                foreach (string name in new[] { nameof(PlayerStore.EndNight), nameof(PlayerStore.BeginDay), nameof(PlayerStore.SaveGame) })
                    Patch(typeof(PlayerStore).GetMethod(name, Type.EmptyTypes)!, nameof(BeforeNative), nameof(AfterNative));
                Patch(typeof(PlayerStore).GetMethod(nameof(PlayerStore.OnItemBought), new[] { typeof(GameItem), typeof(int) })!, null, nameof(AfterItemBought));
                Active = this;
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodInfo method, string? before, string after)
        {
            _harmony.Patch(method, prefix: before == null ? null : new HarmonyMethod(typeof(GraylineObservation), before), postfix: new HarmonyMethod(typeof(GraylineObservation), after));
            _patched.Add(method);
        }
        private void Observe(Action action) { try { action(); } catch (Exception error) { ObserverErrors.Add(error.ToString()); } }
        private static void BeforeNative(MethodBase __originalMethod) => Native(__originalMethod, "enter");
        private static void AfterNative(MethodBase __originalMethod) => Native(__originalMethod, "return");
        private static void Native(MethodBase method, string stage)
        {
            if (Active is not { NightArmed: true } o) return;
            o.Observe(() => o.NativeCalls.Add(JObject.FromObject(new { name = method.Name, stage, day = StoreStation.GetDayCounter(), frame = Time.frameCount })));
        }
        private static void AfterItemBought(GameItem __0, int __1)
        {
            if (Active is not { } o || !o.PurchaseUids.Contains(__0.uniqueId)) return;
            o.Observe(() => o.Buys.Add(JObject.FromObject(new { uid = __0.uniqueId, id = __0.identifier, cost = __1, day = StoreStation.GetDayCounter(), frame = Time.frameCount, cashAtNativeEvent = o._context.Store.playerCash })));
        }
        public void Dispose()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            foreach (var handler in _handlers) handler.Event.RemoveEventHandler(null, handler.Handler);
            foreach (var method in _patched) _harmony.Unpatch(method, HarmonyPatchType.All, _harmony.Id);
        }
    }
}
