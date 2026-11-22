using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const int ProtectionLastObservationDay = 14;
    private const string ProtectionReceiptId = "nicokobo.aug.story.evidence.receipt";
    private const string ProtectionNeuralId = "nicokobo.aug.brain_computer_interface_module";
    private static readonly string[] ProtectionMatrixKeys =
        { "receipt.root", "neural.root", "receipt.backpack", "neural.backpack", "receipt.double", "neural.double" };
    private readonly JArray _protectionActions = new(), _protectionFixtures = new(), _protectionTrials = new(), _protectionCasesDone = new();
    private readonly HashSet<string> _protectionComplete = new(StringComparer.Ordinal);
    private readonly List<ProtectionCase> _protectionCases = new();
    private readonly List<GameItem> _protectionNightStores = new();
    private readonly JArray _protectionNightCustody = new(), _protectionRecoveries = new();
    private GameItem? _protectionReceipt, _protectionNeural;
    private sealed class ProtectionCase(string key, GameItem payload, int depth, GameItem root, GameItem? inner = null)
    {
        internal readonly string Key = key;
        internal readonly GameItem Payload = payload, Root = root;
        internal readonly GameItem? Inner = inner;
        internal readonly int Depth = depth;
        internal bool Done;
    }

    // Generate the receipt directly; retain the original opening neural UID.
    // Acquisition/calendar behavior is outside this guard regression.
    internal IEnumerable<object?> AugProtection()
    {
        R.environment["augProtectionScope"] = "Generated fixture regression: actual opening neural UID, factory-created receipt and containers, and the game's CreateDebugClient2 buyer through AddNextClient and native bell/dialogue. Buyer budget999 and ALL_TYPE are retained; contraband acceptance is an explicit input, removing the factory's random refusal so the Mod guard is tested independently. Actual six root/nested cells, single/bulk sales, positive controls and independent ES3 readback. No natural customer, story acquisition or economic progression claim.";
        R.environment["augProtectionActions"] = _protectionActions;
        R.environment["augProtectionFixtures"] = _protectionFixtures;
        R.environment["augProtectionCustomerTrials"] = _protectionTrials;
        R.environment["augProtectionCases"] = _protectionCasesDone;
        foreach (object? wait in ProtectionWait("protection.generated-fixture-ready", () => AugReady() && Reflect.Get(GraylineFlow, "State") != null)) yield return wait;
        _protectionNeural = ProtectionUniqueOwned(ProtectionNeuralId);
        _protectionReceipt = ProtectionFixture(ProtectionReceiptId, "Explicit generated receipt for guard input; not an acquired E01 receipt");
        ProtectionPrepareCases(); ProtectionRecordPatchOwners();
        JObject originalStory = GraylineStoryState();
        using var observation = new ProtectionObservation(this);
        R.environment["augProtectionObservation"] = new JObject
        {
            ["sales"] = observation.Sales, ["sellReturns"] = observation.SellReturns,
            ["bulkEntries"] = observation.BulkEntries, ["errors"] = observation.Errors
        };
        if (AdvCalendarUIManager.Instance.UIPanel.activeSelf) AdvCalendarUIManager.Instance.CloseUI();
        FixtureClearVisitors("six-cell protection input preparation");
        ProtectionRequire("protection.generated-open-native-eligibility", true, Store.CanStartDay(), Store.CanStartDay());
        StoreShutterButton.Instance.OnClick();
        foreach (object? wait in ProtectionWait("protection.generated-shutter-open", () => Store.storeState == PlayerStore.StoreState.OPEN && Store.lockShutterButtonFlag == 0)) yield return wait;
        FixtureClearVisitors("six-cell protection opened fixture");
        foreach (var cell in _protectionCases)
        {
            FixtureClearVisitors("next protection cell has an independent generated buyer");
            // A shipped test buyer supplies the precondition. The actual game
            // transaction and Mod guard remain the implementation under test.
            var buyer = StoreClientListUnused.CreateDebugClient2();
            buyer.AcceptingContrabandOverride = true;
            buyer.noAcceptingContraband = false;
            _protectionFixtures.Add(JObject.FromObject(new { purpose = "Native buyer input with explicit contraband acceptance, not natural availability", buyer.identifier, budget = buyer.GetBudget(), buyer.AcceptingContrabandOverride, buyer.noAcceptingContraband, preferences = buyer.clientBuyingTagList.ToArray() }));
            Store.storeClientManager.AddNextClient(buyer);
            foreach (object? wait in ProtectionWait("protection.generated-bell-ready." + cell.Key, () => Store.CanNextClient() && Store.lockNextBellFlag == 0 && ProtectionDialogueIdle())) yield return wait;
            GraylineRingBell();
            foreach (object? wait in ProtectionWait("protection.generated-client-arrived." + cell.Key, () => ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), buyer) && Store.isClientArrived)) yield return wait;
            float deadline = Time.realtimeSinceStartup + 60;
            while (!ProtectionDialogueIdle())
            {
                ProtectionCheckWaiting("protection.generated-native-dialogue." + cell.Key, deadline);
                GraylineAdvanceDialogue();
                if (GraylineChoicesVisible()) ProtectionBlock("protection.generated-unexpected-choice", "The native test buyer should have no choices; stop rather than choosing a fallback.");
                yield return null;
            }
            foreach (object? wait in ProtectionTryCell(cell, buyer, observation)) yield return wait;
            ProtectionRequire("protection.generated-cell-complete." + cell.Key, true, cell.Done, cell.Done);
        }
        ProtectionRequire("protection.complete-six-cell-business-matrix", ProtectionMatrixKeys, _protectionCasesDone,
            _protectionCasesDone.Count == 6 && ProtectionMatrixKeys.All(_protectionComplete.Contains) && observation.Errors.Count == 0);
        ProtectionRequire("protection.generated-story-unmodified", originalStory, GraylineStoryState(), JToken.DeepEquals(originalStory, GraylineStoryState()));
        FixtureClearVisitors("completed guard transactions before durable save fixture");
        GraylineStowOwnedCounter();
        var retained = ProtectionCreateNested("persistence.double", _protectionReceipt);
        ProtectionMove(_protectionReceipt, ProtectionInventory(retained.Inner!), "persist-generated-receipt");
        ProtectionMove(_protectionNeural, ProtectionInventory(retained.Inner!), "persist-original-opening-neural");
        Retain(retained.Root);
        R.persistence["augProtection"] = new JObject
        {
            ["fixtureMode"] = true, ["day"] = StoreStation.GetDayCounter(), ["cash"] = Store.playerCash,
            ["receiptUid"] = _protectionReceipt.uniqueId, ["neuralUid"] = _protectionNeural.uniqueId,
            ["outerUid"] = retained.Root.uniqueId, ["innerUid"] = retained.Inner!.uniqueId,
            ["story"] = GraylineStoryState(), ["aug"] = Reflect.Json(AugState()),
            ["graph"] = ProtectionGraph(retained.Root), ["matrix"] = _protectionCasesDone.DeepClone(),
            ["nativeSales"] = observation.Sales.DeepClone(), ["nativeSellReturns"] = observation.SellReturns.DeepClone(),
            ["nativeBulkEntries"] = observation.BulkEntries.DeepClone(), ["bulkPatchOrders"] = observation.BulkOrders.DeepClone(),
            ["soldTracker"] = ProtectionSoldTracker(),
            ["reloadScope"] = "Exact protected UIDs, native inventory graph, cash, unchanged story and real sale receipts. Generated buyer availability is not a gameplay claim."
        };
    }

    private IEnumerable<object?> ProtectionReceiveE01()
    {
        foreach (object? wait in GraylineServeUntil(() => Store.currentClientInstance?.GetClientBlueprint()?.identifier == GraylineStoryActor &&
            Store.isClientArrived && (string?)GraylineStoryState()["ActiveEventId"] == "E01" &&
            (int?)GraylineStoryState()["EncounterDay"] == 2, GraylineStoryActor, "protection.E01.natural-arrival")) yield return wait;
        foreach (object? wait in GraylineWaitChoice("[不接这笔退货]", "[Refuse the return]", "protection.E01.decline")) yield return wait;
        int cash = Store.playerCash;
        var before = GraylineStoryState();
        ProtectionAction("actual-native-dialog-choice", new { eventId = "E01", option = "decline" });
        GraylineClickChoice("[不接这笔退货]", "[Refuse the return]", "protection.E01.decline");
        foreach (object? wait in ProtectionWait("protection.E01.actual-commit", () => ((JArray)GraylineStoryState()["Choices"]!).Any(row =>
            (string?)row["EventId"] == "E01" && (string?)row["OptionId"] == "decline"))) yield return wait;
        var state = GraylineStoryState();
        int uid = (int)((JArray)state["Items"]!).Single(row => (string?)row["Key"] == "receipt")["Uid"]!;
        _protectionReceipt = Store.FindAllItem().Single(item => item.identifier == ProtectionReceiptId && item.uniqueId == uid && item.unitCount == 1 && GeneralHelper.IsItemOwned(item));
        ProtectionRequire("protection.E01.actual-free-receipt", new { cash, day = 2, choices = 1, receiptUid = uid }, new { cash = Store.playerCash, state, item = ProtectionItem(_protectionReceipt) },
            Store.playerCash == cash && ((JArray)before["Choices"]!).Count == 0 && ((JArray)state["Choices"]!).Count == 1 &&
            (string?)state["HiddenClosedReason"] == "no_prosthetic" && state["Pending"]!.Type == JTokenType.Null &&
            !((JArray)state["Items"]!).Any(row => (string?)row["Key"] == "broken"));
        _protectionComplete.Add("natural-e01");
        foreach (object? wait in GraylineWaitChoice("结束会面", "End the visit", "protection.E01.leave")) yield return wait;
        GraylineClickChoice("结束会面", "End the visit", "protection.E01.leave");
        foreach (object? wait in ProtectionWait("protection.E01.native-departure", () => Store.currentClientInstance?.GetClientBlueprint()?.identifier != GraylineStoryActor)) yield return wait;
        GraylineStowOwnedCounter();
    }

    private static bool ProtectionValidStoryRoute(JObject state, int day)
    {
        var choices = state["Choices"] as JArray;
        return choices != null && day >= 2 && day <= ProtectionLastObservationDay && choices.Count == (day >= 9 ? 2 : 1) &&
            (string?)choices[0]!["EventId"] == "E01" && (string?)choices[0]!["OptionId"] == "decline" && (int?)choices[0]!["Day"] == 2 &&
            (day < 9 || (string?)choices[1]!["EventId"] == "E02" && (string?)choices[1]!["OptionId"] == "accept" && (int?)choices[1]!["Day"] == 9);
    }

    private IEnumerable<object?> ProtectionReceiveE02()
    {
        ProtectionRequire("protection.E02.exact-natural-arrival", "Actual arrived E02 actor on D9; no unrecognized story route", GraylineUiState(),
            StoreStation.GetDayCounter() == 9 && Store.isClientArrived && Store.currentClientInstance?.GetClientBlueprint()?.identifier == GraylineStoryActor &&
            (string?)GraylineStoryState()["ActiveEventId"] == "E02" && (int?)GraylineStoryState()["EncounterDay"] == 9);
        foreach (object? wait in GraylineWaitChoice("[收下目录和钛合金]", "[Take the index and the titanium]", "protection.E02.accept")) yield return wait;
        JObject before = GraylineStoryState(); int cash = Store.playerCash;
        var beforeItems = Store.FindAllItem().Where(GeneralHelper.IsItemOwned).ToDictionary(item => item.uniqueId, item => (Id: item.identifier, Count: item.unitCount));
        JToken aug = Reflect.Json(AugState());
        ProtectionAction("actual-native-dialog-choice", new { eventId = "E02", option = "accept" });
        GraylineClickChoice("[收下目录和钛合金]", "[Take the index and the titanium]", "protection.E02.accept");
        foreach (object? wait in ProtectionWait("protection.E02.actual-commit", () => ((JArray)GraylineStoryState()["Choices"]!).Any(row =>
            (string?)row["EventId"] == "E02" && (string?)row["OptionId"] == "accept"))) yield return wait;
        JObject after = GraylineStoryState();
        var owned = Store.FindAllItem().Where(GeneralHelper.IsItemOwned).ToArray();
        var grants = owned.Where(item => !beforeItems.ContainsKey(item.uniqueId)).ToArray();
        int indexUid = (int)((JArray)after["Items"]!).Single(row => (string?)row["Key"] == "index")["Uid"]!;
        ProtectionRequire("protection.E02.full-actual-grant-invoice", "Cash unchanged, prior choice prefix/owned UIDs and stack counts intact, exactly one bound index and one real titanium alloy; original protected UIDs retained", new
        { cash = Store.playerCash, state = after, grants = grants.Select(ProtectionItem).ToArray() },
            Store.playerCash == cash && JToken.DeepEquals(aug, Reflect.Json(AugState())) && after["Pending"]!.Type == JTokenType.Null &&
            ((JArray)before["Choices"]!).Count == 1 && ProtectionValidStoryRoute(after, 9) &&
            JToken.DeepEquals(new JArray(((JArray)after["Choices"]!).Take(1)), before["Choices"]) &&
            beforeItems.All(pair => owned.Count(item => item.uniqueId == pair.Key && item.identifier == pair.Value.Id && item.unitCount == pair.Value.Count) == 1) &&
            grants.Length == 2 && grants.All(item => item.uniqueId > 0 && item.unitCount == 1) &&
            grants.Select(item => item.identifier).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(new[]
                { "nicokobo.aug.story.evidence.index", GraylineMaterial + "titanium_alloy" }.OrderBy(id => id, StringComparer.Ordinal)) &&
            grants.Count(item => item.uniqueId == indexUid && item.identifier == "nicokobo.aug.story.evidence.index") == 1 &&
            ProtectionOwnedUnits(_protectionReceipt!.uniqueId) == 1 && ProtectionOwnedUnits(_protectionNeural!.uniqueId) == 1);
        foreach (object? wait in GraylineWaitChoice("结束会面", "End the visit", "protection.E02.leave")) yield return wait;
        GraylineClickChoice("结束会面", "End the visit", "protection.E02.leave");
        foreach (object? wait in ProtectionWait("protection.E02.native-departure", () => Store.currentClientInstance?.GetClientBlueprint()?.identifier != GraylineStoryActor)) yield return wait;
        GraylineStowOwnedCounter();
    }

    private void ProtectionRecoverForNight(int day)
    {
        int health = Reflect.Int(AugState()!, "Health");
        if (health > 50) return;
        int cash = Store.playerCash, exposure = Reflect.Int(AugState()!, "Exposure");
        JObject story = GraylineStoryState();
        var medical = ProtectionFixture("common_medical", "Explicit longer-window survival input; actual recovery transaction, not purchase or mouse UI coverage");
        int uid = medical.uniqueId;
        var before = new JArray(Store.FindAllItem().Where(item => item.uniqueId != uid && GeneralHelper.IsItemOwned(item)).OrderBy(item => item.uniqueId).Select(ProtectionItem));
        ProtectionRequire("protection.recovery.native-fresh-material." + day, "One owned unmodified native common_medical with fixed native recovery basis 40", ProtectionItem(medical),
            medical.unitValue == 40 && medical.unitCount == 1 && ProtectionOwnedUnits(uid) == 1 &&
            medical.itemFeatures.All(feature => feature.isDisabled || feature.featureType == ItemFeature.FeatureType.Event || feature.GetActualValueModifier() == 0));
        object recovery = Reflect.Get(Reflect.Type("AugMechanicalAscension.Integration.ConsumableRuntimeFlow"), "_active")
            ?? throw new InvalidOperationException("Actual Aug recovery flow unavailable");
        ProtectionAction("actual-Aug-ConsumeRecovery-business-callback", new { uid, basis = 40, health, day });
        Reflect.Call(recovery, "ConsumeRecovery", medical);
        _retained.Remove(medical);
        JToken state = Reflect.Json(AugState());
        var remaining = new JArray(Store.FindAllItem().Where(GeneralHelper.IsItemOwned).OrderBy(item => item.uniqueId).Select(ProtectionItem));
        var invoice = new JObject { ["day"] = day, ["consumedUid"] = uid, ["healthBefore"] = health, ["healthAfter"] = Reflect.Int(AugState()!, "Health"),
            ["basis"] = 40, ["cash"] = Store.playerCash, ["receipt"] = state["LastCommittedOperation"]?.DeepClone(), ["scope"] = "Factory material; actual guarded business transaction, no mouse medical UI or economic acquisition claim" };
        _protectionRecoveries.Add(invoice);
        ProtectionRequire("protection.recovery.actual-consumption-and-complete-invoice." + day, new { health = Math.Min(100, health + 40), cash, exposure, consumedUid = uid, remainingUnits = 0 }, invoice,
            ProtectionOwnedUnits(uid) == 0 && !Store.FindAllItem().Any(item => item.uniqueId == uid) &&
            Reflect.Int(AugState()!, "Health") == Math.Min(100, health + 40) && Reflect.Int(AugState()!, "Exposure") == exposure &&
            Store.playerCash == cash && JToken.DeepEquals(story, GraylineStoryState()) && JToken.DeepEquals(before, remaining) &&
            (string?)state["LastCommittedOperation"]?["Kind"] == "medicalConsume" && (int?)state["LastCommittedOperation"]?["Day"] == day &&
            (string?)state["LastCommittedOperation"]?["ItemInstanceId"] == "native-item:" + uid &&
            (long?)state["LastCommittedOperation"]?["ExpectedValue"] == 40 && state["PendingOperation"]!.Type == JTokenType.Null);
    }

    private void ProtectionPackNight(int day)
    {
        // Real candidate selection and ordinary registered smuggler admission;
        // no theft flags, parent writes, replacement protected UIDs or grid edits.
        foreach (var item in Store.GetAllRepoEligibleItems().OrderByDescending(item => item.modifiedShape.width * item.modifiedShape.height).ToArray())
        {
            if (item.IsRootedInSmugglerBay() || _protectionNightStores.Contains(item)) continue;
            GameInventory? destination = _protectionNightStores.Select(store => ProtectionInventory(store))
                .FirstOrDefault(inventory => inventory.TryFindOneValidInventorySlot(item)?.IsValid() == true);
            if (destination == null)
            {
                ProtectionRequire("protection.night-storage.bound." + day, "At most four original registered smuggler_bay_mod fixture grids", _protectionNightStores.Count, _protectionNightStores.Count < 4);
                var bay = ProtectionFixture("smuggler_bay_mod", "Declared native night storage; preserves real theft rules and original protected UIDs");
                ProtectionRequire("protection.night-storage.native-main-floor-slot." + day, "Real native 8x8 smuggler grid and legal main-floor slot", ProtectionItem(bay),
                    bay.contentWindow?.childElement is GameGridInventory grid && grid.identifier == "smuggler_bay" &&
                    (ReferenceEquals(bay.parentInventory, EmporiumEntry.Instance.invElement) ||
                        EmporiumEntry.Instance.invElement.TryFindOneValidInventorySlot(bay)?.IsValid() == true));
                ProtectionMove(bay, EmporiumEntry.Instance.invElement, "night-storage-root-to-main-floor");
                _protectionNightStores.Add(bay); destination = ProtectionInventory(bay);
            }
            ProtectionRequire("protection.night-storage.actual-admission." + day, "Actual owned item removable with legal cargo SlotMarker", ProtectionItem(item),
                item.MayRemove() && destination.TryFindOneValidInventorySlot(item)?.IsValid() == true);
            ProtectionMove(item, destination, "native-night-custody");
        }
        GraylineStowOwnedCounter();
        ProtectionRequire("protection.night-storage.no-native-theft-candidate." + day, "Actual native candidate selector empty after legal player storage; theft remains enabled",
            Store.GetAllRepoEligibleItems().Select(ProtectionItem).ToArray(), Store.GetAllRepoEligibleItems().Count == 0);
        ProtectionCheckCustody(day, "before-real-night");
    }
    private void ProtectionCheckCustody(int day, string stage)
    {
        var payloads = new[] { _protectionNeural, _protectionReceipt }.Where(item => item != null).Cast<GameItem>().ToArray();
        var owned = Store.FindAllItem().Where(GeneralHelper.IsItemOwned).ToArray();
        var evidence = new JObject { ["day"] = day, ["stage"] = stage, ["items"] = new JArray(payloads.Select(ProtectionItem)),
            ["storageRoots"] = new JArray(_protectionNightStores.Select(ProtectionItem)) };
        _protectionNightCustody.Add(evidence);
        ProtectionRequire("protection.original-payload-night-custody." + stage + "." + day, "Same original protected UIDs remain uniquely owned and natively ineligible for theft/repo", evidence,
            payloads.Length == (day == 1 || day == 2 && stage == "after-real-night" ? 1 : 2) && payloads.All(item => item.uniqueId > 0 && item.unitCount == 1 &&
                owned.Count(current => current.uniqueId == item.uniqueId && current.identifier == item.identifier) == 1 && !Store.IsEligibleForRepo(item)));
    }

    private void ProtectionPrepareCases()
    {
        _protectionCases.Add(new("receipt.root", _protectionReceipt!, 0, _protectionReceipt!));
        _protectionCases.Add(new("neural.root", _protectionNeural!, 0, _protectionNeural!));
        _protectionCases.Add(new("receipt.backpack", _protectionReceipt!, 1, ProtectionFixture("fanny_pack", "native one-level receipt container")));
        _protectionCases.Add(new("neural.backpack", _protectionNeural!, 1, ProtectionFixture("fanny_pack", "native one-level opening neural container")));
        _protectionCases.Add(ProtectionCreateNested("receipt.double", _protectionReceipt!));
        _protectionCases.Add(ProtectionCreateNested("neural.double", _protectionNeural!));
    }

    private ProtectionCase ProtectionCreateNested(string key, GameItem payload)
    {
        var inner = ProtectionFixture("eng_box", "legal two-level inner container; starts locked, not a story acquisition");
        var card = ProtectionFixture("eng_keycard", "native engineering key consumed through Target to prepare the container");
        int cardUid = card.uniqueId, boxUid = inner.uniqueId, cash = Store.playerCash;
        ProtectionRequire("protection.native-key-eligible." + key, "Owned real engineering key, matching locked real box, native target guards", new
        { key = ProtectionItem(card), box = ProtectionItem(inner) }, LockHelper.IsCurrentlyLocked(inner) && LockHelper.CanOpenLock(inner, "LOCK_ID_ENG") && card.MayTarget(inner) && card.CanTarget(inner));
        ProtectionAction("GameItem.Target-native-engineering-key", new { cardUid, boxUid });
        card.Target(inner);
        _retained.Remove(card); // Consumed by the real key delegate, no stale retained UID.
        ProtectionRequire("protection.native-unlock-consumes-key." + key, "Same box unlocked, key actually consumed, cash unchanged, no hidden contents", new
        { box = ProtectionItem(inner), keyRemaining = ProtectionOwnedUnits(cardUid), cash = Store.playerCash },
            !LockHelper.IsCurrentlyLocked(inner) && ProtectionOwnedUnits(cardUid) == 0 && inner.uniqueId == boxUid &&
            Store.playerCash == cash && inner.FindAllChildItems().Count == 0);
        GameItem? outer = null;
        foreach (string id in new[] { "smuggler_bay_mini", "smuggler_bay", "smuggler_bay_mod" })
        {
            var candidate = ProtectionFixture(id, "explicit native two-level outer container; real admission, no backpack-in-backpack override");
            if (ProtectionInventory(candidate).TryFindOneValidInventorySlot(inner)?.IsValid() == true) { outer = candidate; break; }
            _protectionFixtures.Add(new JObject { ["uid"] = candidate.uniqueId, ["outcome"] = "native shape/admission rejected engineering box; candidate disposed" });
            ProtectionDisposeFixture(candidate, "Candidate cannot legally receive the engineering box.");
        }
        ProtectionRequire("protection.native-double-container-available." + key, "One normal registered outer container can legally receive the unlocked engineering box", outer?.identifier, outer != null);
        ProtectionMove(inner, ProtectionInventory(outer!), "unlocked-engineering-box-to-native-outer");
        return new(key, payload, 2, outer!, inner);
    }

    private GameItem ProtectionFixture(string id, string purpose)
    {
        var item = Spawn(id); Retain(item);
        _protectionFixtures.Add(new JObject { ["source"] = "actual factory fixture, not economic acquisition", ["purpose"] = purpose, ["item"] = ProtectionItem(item) });
        return item;
    }
    private void ProtectionDisposeFixture(GameItem item, string reason)
    {
        _protectionFixtures.Add(new JObject { ["uid"] = item.uniqueId, ["outcome"] = "unused explicit fixture disposed", ["reason"] = reason });
        _retained.Remove(item);
        Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
    }
    private static GameInventory ProtectionInventory(GameItem item) =>
        item.contentWindow?.childElement as GameInventory ?? throw new InvalidOperationException("Expected real direct inventory in " + item.identifier);
    private void ProtectionMove(GameItem item, GameInventory destination, string purpose)
    {
        ProtectionAction("native-admitted-GraphUtils-transfer", new { purpose, item.uniqueId, destination = destination.identifier });
        GraylineTransfer(item, destination, "protection." + purpose);
    }

    private IEnumerable<object?> ProtectionServeNaturalDay(ProtectionObservation observation)
    {
        float deadline = Time.realtimeSinceStartup + 480;
        while (Store.currentClientInstance != null || Store.CanNextClient() || !Store.CanEndDay() || !ProtectionDialogueIdle())
        {
            ProtectionCheckWaiting("protection.natural-customers", deadline);
            var client = Store.currentClientInstance?.GetClientBlueprint();
            if (client == null)
            {
                if (Store.CanNextClient() && Store.lockNextBellFlag == 0 && DialogUIManager.Instance.currentDialog == null) GraylineRingBell();
                else if (Store.CanEndDay() && ProtectionDialogueIdle()) break;
                yield return null;
            }
            else if (!Store.isClientArrived) yield return null;
            else foreach (object? wait in ProtectionHandleClient(client, observation)) yield return wait;
        }
        ProtectionRequire("protection.day-native-close-eligible", "Actual day completion without native game cheats", GraylineUiState(), Store.CanEndDay() && !Store.devMode);
    }

    private IEnumerable<object?> ProtectionHandleClient(StoreClient client, ProtectionObservation observation)
    {
        if (client.identifier == GraylineStoryActor)
        {
            foreach (object? wait in ProtectionReceiveE02()) yield return wait;
            yield break;
        }
        float deadline = Time.realtimeSinceStartup + 240;
        bool paidRent = false, declinedMortgage = false, declinedVending = false, tried = false, rang = false;
        while (ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client))
        {
            ProtectionCheckWaiting("protection.client." + client.identifier, deadline);
            GraylineAdvanceDialogue();
            if (GraylineChoicesVisible())
            {
                if (client.identifier == "landlord" && !paidRent)
                {
                    int cash = Store.playerCash, amount = Store.GetRentValue(); string label = LocHelper.GetLocalizedDialogue("dialog_landlord_player_pay", amount);
                    ProtectionRequire("protection.actual-rent-choice", "Affordable visible rent option", new { cash, amount }, Store.CanPayRent() && GraylineChoice(label, label) != null);
                    GraylineClickChoice(label, label, "protection.pay-real-rent"); paidRent = true;
                    ProtectionRequire("protection.actual-rent-charge", cash - amount, Store.playerCash, Store.playerCash == cash - amount);
                }
                else if (client.identifier == "loan_shark2" && !declinedMortgage)
                {
                    string label = LocHelper.GetLocalizedDialogue("dialog_loan_shark2_player_decline");
                    ProtectionRequire("protection.actual-mortgage-decline", "Exact final loan-shark offer and visible decline", GraylineUiState(),
                        DialogUIManager.Instance.currentDialog.plainText == LocHelper.GetLocalizedDialogue("dialog_loan_shark2_last_line") && GraylineChoice(label, label) != null);
                    GraylineClickChoice(label, label, "protection.decline-mortgage"); declinedMortgage = true;
                }
                else if (client.identifier == "landlordVending" && !declinedVending)
                {
                    string label = LocHelper.GetLocalizedDialogue("dialog_landlord_vending_player_decline");
                    ProtectionRequire("protection.actual-vending-decline", "Visible optional vending decline", GraylineUiState(), GraylineChoice(label, label) != null);
                    GraylineClickChoice(label, label, "protection.decline-vending"); declinedVending = true;
                }
                else ProtectionBlock("protection.unknown-choice", "An unrecognized native choice is visible; no fallback option or dismissal bypass is selected.");
            }
            if (!tried && ProtectionDialogueIdle() && Store.IsClientDimissable() && client.clientIntent is StoreClient.ClientIntent.BUY or StoreClient.ClientIntent.SELLNBUY)
            {
                tried = true;
                // Spend a rare qualifying buyer's real budget on the largest
                // pending graph first; all live on-counter gates remain below.
                foreach (var cell in _protectionCases.Where(cell => !cell.Done).OrderByDescending(cell => cell.Depth)
                             .ThenByDescending(cell => ReferenceEquals(cell.Payload, _protectionNeural)).ToArray())
                {
                    if (!ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client)) break;
                    foreach (object? wait in ProtectionTryCell(cell, client, observation)) yield return wait;
                }
            }
            if (!rang && Store.IsClientDimissable() && Store.lockNextBellFlag == 0 && ProtectionDialogueIdle())
            { GraylineRingBell(); rang = true; }
            yield return null;
        }
        GraylineStowOwnedCounter();
    }

    private IEnumerable<object?> ProtectionTryCell(ProtectionCase cell, StoreClient client, ProtectionObservation observation)
    {
        GraylineStowOwnedCounter();
        if (cell.Depth > 0)
            ProtectionMove(cell.Payload, ProtectionInventory(cell.Inner ?? cell.Root), cell.Key + ".insert-protected-payload");
        ProtectionMove(cell.Root, EmporiumEntry.Instance.backInvinvElement, cell.Key + ".actual-weighing-counter");
        foreach (object? wait in ProtectionIdle("protection.counter-response." + cell.Key)) yield return wait;
        GameItem? normal = null;
        // Read the native quotes; do not replace prices or a customer offer.
        // Keep at most two candidates live, then actually weigh/revalidate the
        // cheapest legal positive input against the current remaining budget.
        foreach (string id in new[] { "common_electronic", "scrap_metal", "toilet_paper", "soap", "water_ration_small", "blank_keycard" })
        {
            if (!DirectoryMaster.Has<GameItem>(id)) continue;
            var sample = ProtectionFixture(id, cell.Key + " ordinary positive-control candidate");
            long quote = sample.GetNegociatedValue();
            if (quote > 0 && client.IsClientBuyingThisItem(sample) && !client.CanClientExposeAnyFeatureDeep(sample) &&
                !ContrabandHelper.IsContraband(sample) && !StolenHelper.IsStolenItem(sample) &&
                (normal == null || quote < normal.GetNegociatedValue()))
            {
                if (normal != null) ProtectionDisposeFixture(normal, "A lower real positive native quote is available for this same client; avoid wasting its finite budget.");
                normal = sample;
            }
            else ProtectionDisposeFixture(sample, "Unused candidate: not the lowest native positive quote satisfying this customer's actual preference/exposure and ordinary legal-input conditions.");
        }
        if (normal != null)
        {
            ProtectionMove(normal, EmporiumEntry.Instance.backInvinvElement, cell.Key + ".normal-mixed-control");
            foreach (object? wait in ProtectionIdle("protection.normal-counter-response." + cell.Key)) yield return wait;
            // Actual counter removal/addition selects the protected root again.
            // Do not assign currentNegociatedItem or use an artificial quote.
            ProtectionMove(cell.Root, EmporiumEntry.Instance.invElement, cell.Key + ".native-reselection-out");
            ProtectionMove(cell.Root, EmporiumEntry.Instance.backInvinvElement, cell.Key + ".native-reselection-in");
            foreach (object? wait in ProtectionIdle("protection.protected-reselection-response." + cell.Key)) yield return wait;
        }
        JObject gate = ProtectionRawGate(cell.Root, client);
        JObject? normalGate = normal == null ? null : ProtectionRawGate(normal, client);
        long rootQuote = cell.Root.GetNegociatedValue(), normalQuote = normal?.GetNegociatedValue() ?? -1;
        // Reserve the actual loaded-root quote and one positive normal sale.
        // At least one budget unit must remain after the normal sale so that
        // a native all-done departure cannot become the reason for rejection.
        bool qualified = normal != null && (bool?)gate["vanillaEligible"] == true &&
            (bool?)normalGate?["vanillaEligible"] == true && Store.CanSellThisItem(normal) &&
            rootQuote >= 0 && rootQuote <= int.MaxValue && normalQuote > 0 && normalQuote <= int.MaxValue &&
            (!client.useClientBudget || Math.Max(1L, rootQuote) + normalQuote <= client.GetBudget());
        _protectionTrials.Add(new JObject { ["cell"] = cell.Key, ["day"] = StoreStation.GetDayCounter(), ["gate"] = gate,
            ["normalGate"] = normalGate, ["rootQuoteOnCounter"] = rootQuote, ["normalQuoteOnCounter"] = normalQuote, ["qualified"] = qualified });
        if (!qualified)
        {
            if (normal != null) ProtectionDisposeFixture(normal, "No full original native gate/budget qualification; cell remains unexecuted for the next natural client.");
            if (cell.Depth > 0) ProtectionMove(cell.Payload, EmporiumEntry.Instance.invElement, cell.Key + ".retrieve-unqualified-payload");
            ProtectionMove(cell.Root, EmporiumEntry.Instance.invElement, cell.Key + ".stow-unqualified-root");
            yield break;
        }
        ProtectionRequire("protection.raw-native-counterfactual." + cell.Key, "Live native preference/exposure/variety/actual budget permit this exact protected root", gate, (bool?)gate["vanillaEligible"] == true);
        ProtectionRequire("protection.CanSell-overloads-block." + cell.Key, new { normalBudget = false, ignoreBudget = false }, new
        { normalBudget = Store.CanSellThisItem(cell.Root), ignoreBudget = Store.CanSellThisItem(cell.Root, true) },
            !Store.CanSellThisItem(cell.Root) && !Store.CanSellThisItem(cell.Root, true));
        NegociationUIManager.Instance.OpenUI();
        ProtectionRequire("protection.actual-native-selection." + cell.Key, cell.Root.uniqueId, Store.currentNegociatedItem?.uniqueId,
            ReferenceEquals(Store.currentNegociatedItem, cell.Root));
        ProtectionReject(cell.Key + ".native-ui-single", cell, client, observation, () => NegociationUIManager.Instance.SellItem());
        int returns = observation.SellReturns.Count;
        ProtectionReject(cell.Key + ".direct-SellItem", cell, client, observation, () => Store.SellItem(cell.Root));
        ProtectionRequire("protection.SellItem-original-skipped." + cell.Key, "Actual guarded method entry reports original not run", observation.SellReturns.Skip(returns).ToArray(),
            observation.SellReturns.Count == returns + 1 && (bool?)observation.SellReturns.Last!["ranOriginal"] == false);
        ProtectionReject(cell.Key + ".direct-SellItem-repeat", cell, client, observation, () => Store.SellItem(cell.Root));
        var pure = new List<GameItem> { cell.Root, cell.Root };
        ProtectionReject(cell.Key + ".native-pure-bulk", cell, client, observation,
            () => ProtectionBulkObserved(cell.Key + ".native-pure-bulk", Array.Empty<int>(), false, observation,
                () => NegociationUIManager.Instance.SellMultiple(pure)));
        var ignore = new List<GameItem> { cell.Root };
        ProtectionReject(cell.Key + ".native-ignore-budget-bulk", cell, client, observation,
            () => ProtectionBulkObserved(cell.Key + ".native-ignore-budget-bulk", Array.Empty<int>(), true, observation,
                () => NegociationUIManager.Instance.SellMultiple(ignore, true)));

        ProtectionRequire("protection.normal-control-actually-eligible." + cell.Key, "Normal native sale guard accepts the ordinary input", ProtectionRawGate(normal!, client),
            Store.CanSellThisItem(normal!) && NegociationUIManager.Instance.GetSellableItem().Contains(normal!));
        JObject protectedBefore = ProtectionGraph(cell.Root); JObject storyBefore = GraylineStoryState(); JToken augBefore = Reflect.Json(AugState());
        var mixed = new List<GameItem> { cell.Root, normal!, cell.Root };
        JObject? mixedEntry = null;
        JObject mixedSale = ProtectionSellExpected(normal!, client, observation,
            () => mixedEntry = ProtectionBulkObserved(cell.Key + ".mixed-native-bulk", new[] { normal!.uniqueId }, false, observation,
                () => NegociationUIManager.Instance.SellMultiple(mixed)), cell.Key + ".mixed-native-bulk");
        ProtectionRequire("protection.mixed-partial-semantics." + cell.Key, "Only normal item sells once; final effective parameters exclude protected duplicates; graph and story unchanged", new
        {
            callerListUidsAfter = mixed.Select(item => item.uniqueId).ToArray(), // Diagnostic only: another Mod may rebind ref __0.
            effectiveEntry = mixedEntry, sale = mixedSale, graph = ProtectionGraph(cell.Root)
        }, mixedEntry != null && JToken.DeepEquals(protectedBefore, ProtectionGraph(cell.Root)) &&
            JToken.DeepEquals(storyBefore, GraylineStoryState()) && JToken.DeepEquals(augBefore, Reflect.Json(AugState())));
        foreach (object? wait in ProtectionIdle("protection.mixed-sale-reply." + cell.Key)) yield return wait;
        var uiReplay = NegociationUIManager.Instance.GetSellableItem();
        ProtectionRequire("protection.real-UI-replay-list." + cell.Key, new[] { cell.Root.uniqueId }, uiReplay.Select(item => item.uniqueId).ToArray(),
            uiReplay.Count == 1 && ReferenceEquals(uiReplay[0], cell.Root));
        ProtectionReject(cell.Key + ".actual-UI-list-repeat", cell, client, observation,
            () => ProtectionBulkObserved(cell.Key + ".actual-UI-list-repeat", Array.Empty<int>(), false, observation,
                () => NegociationUIManager.Instance.SellMultiple(uiReplay)));
        JObject? shellSale = null;
        if (cell.Depth > 0)
        {
            ProtectionMove(cell.Payload, EmporiumEntry.Instance.invElement, cell.Key + ".extract-only-protected-item");
            foreach (object? wait in ProtectionIdle("protection.empty-shell-response." + cell.Key)) yield return wait;
            ProtectionRequire("protection.same-shell-positive-eligible." + cell.Key, "Same root and normal remaining empty inner container now pass both native guards", ProtectionRawGate(cell.Root, client),
                Store.CanSellThisItem(cell.Root) && Store.CanSellThisItem(cell.Root, true) &&
                !cell.Root.FindAllChildItems().Any(item => item.identifier is ProtectionReceiptId or ProtectionNeuralId));
            shellSale = ProtectionSellExpected(cell.Root, client, observation,
                () => ProtectionBulkObserved(cell.Key + ".same-shell-positive", new[] { cell.Root.uniqueId }, false, observation,
                    () => NegociationUIManager.Instance.SellMultiple(new List<GameItem> { cell.Root })), cell.Key + ".same-shell-positive");
            foreach (object? wait in ProtectionIdle("protection.same-shell-sale-reply." + cell.Key)) yield return wait;
        }
        else ProtectionMove(cell.Payload, EmporiumEntry.Instance.invElement, cell.Key + ".retain-original-root");
        ProtectionRequire("protection.original-payload-retained." + cell.Key, "Original protected UID remains owned exactly once after all actual sales", ProtectionItem(cell.Payload),
            ProtectionOwnedUnits(cell.Payload.uniqueId) == 1 && Store.FindAllItem().Count(item => item.uniqueId == cell.Payload.uniqueId) == 1);
        cell.Done = true; _protectionComplete.Add(cell.Key);
        _protectionCasesDone.Add(new JObject { ["key"] = cell.Key, ["depth"] = cell.Depth, ["payloadUid"] = cell.Payload.uniqueId,
            ["rootUid"] = cell.Root.uniqueId, ["client"] = client.identifier, ["day"] = StoreStation.GetDayCounter(), ["nativeGate"] = gate,
            ["mixedSale"] = mixedSale, ["sameShellSale"] = shellSale, ["outcome"] = "passed" });
        GraylineStowOwnedCounter();
    }

    // Check the effective argument seen after every production prefix, not
    // whether the caller's original List object was mutated. Synthesis can
    // legally rebind ref __0 before Aug filters that replacement.
    private JObject ProtectionBulkObserved(string name, int[] expectedUids, bool ignoreBudget, ProtectionObservation observation, Action attempt)
    {
        int count = observation.BulkEntries.Count, errors = observation.Errors.Count;
        string previousLabel = observation.BulkLabel;
        observation.BulkLabel = name;
        try { attempt(); }
        finally { observation.BulkLabel = previousLabel; }
        var entries = observation.BulkEntries.Skip(count).ToArray();
        var entry = entries.Length == 1 ? entries[0] as JObject : null;
        var items = entry?["items"] as JArray;
        ProtectionRequire("protection.final-effective-bulk-parameters." + name,
            new { captures = 1, expectedUids, ignoreBudget, originalWillRun = true, observerIsLast = true, protectedCount = 0 },
            new { captures = entries, observerErrors = observation.Errors.Skip(errors).ToArray() },
            entry != null && items != null && observation.Errors.Count == errors &&
            (bool?)entry["originalWillRun"] == true && (bool?)entry["orderVerified"] == true &&
            (bool?)entry["ignoreBudget"] == ignoreBudget && (string?)entry["label"] == name &&
            items.Select(item => (int)item["uid"]!).SequenceEqual(expectedUids) &&
            items.All(item => (bool?)item["protectedByProductionGuard"] == false),
            "Readonly void prefix receives parameters by value after all actually installed prefixes. Missing capture/order proof or any protected effective element fails. Cash, UID, ownership and native TrackItem checks remain independent.");
        return entry!;
    }

    private JObject ProtectionSellExpected(GameItem item, StoreClient client, ProtectionObservation observation, Action sell, string name)
    {
        int uid = item.uniqueId, cash = Store.playerCash, budget = client.GetBudget(), events = observation.Sales.Count;
        int tracker = Store.soldItemsTracker.Count; long quote = item.GetNegociatedValue();
        ProtectionRequire("protection.positive-native-invoice." + name, "Actual native eligible owned input, positive quote and full real budget", new { uid, cash, budget, quote },
            ProtectionOwnedUnits(uid) == 1 && Store.CanSellThisItem(item) && quote > 0 && quote <= int.MaxValue && (!client.useClientBudget || quote <= budget));
        ProtectionAction("actual-positive-native-sale", new { name, uid, quote, cash, budget });
        int returns = observation.SellReturns.Count;
        sell();
        var actual = observation.Sales.Skip(events).ToArray();
        ProtectionRequire("protection.positive-payment-ownership-event." + name, new { cash = cash + quote, budget = client.useClientBudget ? budget - quote : budget, sales = 1, uid },
            new { cash = Store.playerCash, budget = client.GetBudget(), owned = ProtectionOwnedUnits(uid), events = actual, tracker = ProtectionSoldTracker() },
            Store.playerCash == cash + quote && (!client.useClientBudget || client.GetBudget() == budget - quote) &&
            ProtectionOwnedUnits(uid) == 0 && !GeneralHelper.IsItemOwned(item) && actual.Length == 1 &&
            (int?)actual[0]["uid"] == uid && (int?)actual[0]["price"] == quote && (string?)actual[0]["client"] == client.identifier &&
            observation.SellReturns.Count == returns + 1 && (bool?)observation.SellReturns.Last!["ranOriginal"] == true &&
            ReferenceEquals(item.parentInventory, EmporiumEntry.Instance.soldElement) &&
            Store.soldItemsTracker.Count == tracker + 1 && Store.soldItemsTracker.Count(row => row.itemUniqueId == uid && row.soldPrice == quote) == 1);
        _retained.Remove(item);
        return new JObject { ["uid"] = uid, ["quote"] = quote, ["cashBefore"] = cash, ["cashAfter"] = Store.playerCash,
            ["budgetBefore"] = budget, ["budgetAfter"] = client.GetBudget(), ["nativeSale"] = actual[0].DeepClone() };
    }

    private void ProtectionReject(string name, ProtectionCase cell, StoreClient client, ProtectionObservation observation, Action attempt)
    {
        JObject gate = ProtectionRawGate(cell.Root, client);
        ProtectionRequire("protection.rejection-independent-gates." + name, "All original native sale conditions are true; only protection is expected to reject", gate, (bool?)gate["vanillaEligible"] == true);
        JObject before = ProtectionLedger(client); int events = observation.Sales.Count;
        ProtectionAction("actual-protected-sale-attempt", new { name, cell.Key, rootUid = cell.Root.uniqueId });
        attempt();
        JObject after = ProtectionLedger(client);
        ProtectionRequire("protection.no-cash-uid-ownership-graph-receipt-change." + name, before, after,
            JToken.DeepEquals(before, after) && observation.Sales.Count == events,
            "Independent cash/budget, complete native owned item UID/parent graph, story, and actual sold tracker. No native SoldItem.TrackItem event may occur.");
    }

    // Direct observations of every original Build26317870 CanSellThisItem
    // condition, read separately from the patched method under test.
    private JObject ProtectionRawGate(GameItem item, StoreClient client)
    {
        bool current = ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client) && Store.isClientArrived;
        bool counter = ReferenceEquals(item.parentInventory, EmporiumEntry.Instance.backInvinvElement) && EmporiumEntry.Instance.backInvinvElement.childItems.Count > 0;
        bool willing = current && client.IsClientBuyingThisItem(item);
        bool exposes = current && client.CanClientExposeAnyFeatureDeep(item);
        bool budget = !client.useClientBudget || client.HasBudgetLeftToBuy(item);
        bool variety = !client.isRequireVariety || !client.IsItemInVarietyList(item);
        bool owned = GeneralHelper.IsItemOwned(item) && item.unitCount == 1 && ProtectionOwnedUnits(item.uniqueId) == 1;
        return new JObject { ["client"] = client.identifier, ["intent"] = client.clientIntent.ToString(), ["currentArrived"] = current,
            ["actualWeightedCounter"] = counter, ["clientWillingIncludingContents"] = willing, ["exposableFeature"] = exposes,
            ["budgetSufficient"] = budget, ["varietySufficient"] = variety, ["budget"] = client.GetBudget(), ["useBudget"] = client.useClientBudget,
            ["quote"] = item.GetNegociatedValue(), ["ownedUniqueUnit"] = owned, ["uid"] = item.uniqueId,
            ["vanillaEligible"] = current && counter && willing && !exposes && budget && variety && owned };
    }
    private JObject ProtectionLedger(StoreClient client) => new()
    {
        ["cash"] = Store.playerCash, ["budget"] = client.GetBudget(), ["story"] = GraylineStoryState(), ["aug"] = Reflect.Json(AugState()),
        ["items"] = new JArray(Store.FindAllItem().Where(item => item.uniqueId > 0 && GeneralHelper.IsItemOwned(item)).Distinct().OrderBy(item => item.uniqueId).Select(ProtectionItem)),
        ["soldTracker"] = ProtectionSoldTracker()
    };
    private static JObject ProtectionItem(GameItem item)
    {
        var result = (JObject)ItemState(item);
        result["x"] = item.modifiedShape.minX; result["y"] = item.modifiedShape.minY;
        result["orientation"] = item.modifiedShape.orientation; result["flipped"] = item.modifiedShape.flipped;
        result["locked"] = LockHelper.IsCurrentlyLocked(item);
        return result;
    }
    private static JObject ProtectionGraph(GameItem root) => new()
    {
        ["rootUid"] = root.uniqueId,
        ["items"] = new JArray(new[] { root }.Concat(root.FindAllChildItems()).Distinct().OrderBy(item => item.uniqueId).Select(ProtectionItem))
    };
    private JArray ProtectionSoldTracker() => new(Store.soldItemsTracker.Select(row => JObject.FromObject(new
    { uid = row.itemUniqueId, id = row.itemId, client = row.clientId, price = row.soldPrice })));
    private int ProtectionOwnedUnits(int uid) => Store.FindAllItem().Where(item => item.uniqueId == uid && GeneralHelper.IsItemOwned(item)).Sum(item => item.unitCount);
    private GameItem ProtectionUniqueOwned(string id) => Store.FindAllItem().Single(item => item.identifier == id && item.unitCount == 1 && item.uniqueId > 0 && GeneralHelper.IsItemOwned(item));
    private bool ProtectionDialogueIdle() => DialogUIManager.Instance.currentDialog == null &&
        Reflect.Get(DialogUIManager.Instance, "displayingUncompletedDialog") == null && !DialogUIManager.Instance.isWaitingForManualAdvance && !GraylineChoicesVisible();
    private IEnumerable<object?> ProtectionIdle(string name)
    {
        foreach (object? wait in ProtectionWait(name, ProtectionDialogueIdle, 120))
        {
            GraylineAdvanceDialogue();
            if (GraylineChoicesVisible()) ProtectionBlock(name, "Unexpected choice while waiting for actual native inspection/sale reply.");
            yield return wait;
        }
    }
    private IEnumerable<object?> ProtectionWait(string name, Func<bool> ready, float seconds = 40)
    {
        float deadline = Time.realtimeSinceStartup + seconds; R.phase = name;
        while (!ready()) { ProtectionCheckWaiting(name, deadline); yield return null; }
        R.Check(name, "Actual native condition reached", GraylineUiState(), true);
    }
    private void ProtectionCheckWaiting(string name, float deadline)
    {
        R.phase = name;
        if (GameOverUIManager.Instance?.IsShowing == true || Time.realtimeSinceStartup >= deadline)
            ProtectionBlock(name, "Native condition not reached before local timeout or native GameOver; no date/cash/health/client bypass.");
    }
    private void ProtectionBlock(string name, string reason)
    {
        R.environment["augProtectionBlocker"] = GraylineUiState(); R.Skip(name, reason);
        throw new InvalidOperationException(name + ": " + reason);
    }
    private void ProtectionRequire(string name, object? expected, object? actual, bool passed, string evidence = "")
    {
        R.Check(name, expected, actual, passed, evidence);
        if (!passed) { R.environment["augProtectionBlocker"] = GraylineUiState(); throw new InvalidOperationException(name + " failed; no successful save baseline"); }
    }
    private void ProtectionAction(string action, object? detail = null) => _protectionActions.Add(JObject.FromObject(new
    { action, day = StoreStation.GetDayCounter(), frame = Time.frameCount, cash = Store.playerCash, detail }));
    private void ProtectionRecordPatchOwners()
    {
        var methods = new[] { typeof(PlayerStore).GetMethod(nameof(PlayerStore.CanSellThisItem), new[] { typeof(GameItem), typeof(bool) })!,
            typeof(PlayerStore).GetMethod(nameof(PlayerStore.SellItem), new[] { typeof(GameItem) })!,
            typeof(NegociationUIManager).GetMethod(nameof(NegociationUIManager.SellMultiple), new[] { typeof(List<GameItem>), typeof(bool) })! };
        var info = new JArray(methods.Select(method => new JObject { ["method"] = method.DeclaringType!.Name + "." + method.Name,
            ["owners"] = new JArray(Harmony.GetPatchInfo(method)?.Owners.ToArray() ?? Array.Empty<string>()) }));
        R.environment["augProtectionInstalledOwners"] = info;
        ProtectionRequire("protection.real-production-guards-installed", "All three actual methods have the current Aug guard owner", info,
            info.All(row => ((JArray)row["owners"]!).Any(owner => (string?)owner == "nicokobo.aug.protected-items")));
    }

    internal IEnumerable<object?> AugProtectionReload(JObject baseline)
    {
        ProtectionRequire("protection.reload.same-slot-scope", "Original independent slot, readonly load; no copy rebind assertion", O.UseCopy, !O.UseCopy);
        var expected = baseline["persistence"]?["augProtection"] as JObject ?? throw new InvalidOperationException("Missing actual native protection baseline");
        foreach (object? wait in ProtectionWait("protection.reload.real-Aug-session", () => AugReady() && Reflect.Get(GraylineFlow, "State") != null)) yield return wait;
        int receiptUid = (int)expected["receiptUid"]!, neuralUid = (int)expected["neuralUid"]!, outerUid = (int)expected["outerUid"]!, innerUid = (int)expected["innerUid"]!;
        var items = Store.FindAllItem().ToArray();
        GameItem receipt = items.Single(item => item.uniqueId == receiptUid), neural = items.Single(item => item.uniqueId == neuralUid);
        GameItem outer = items.Single(item => item.uniqueId == outerUid), inner = items.Single(item => item.uniqueId == innerUid);
        ProtectionRequire("protection.reload.actual-original-UIDs-and-graph", expected["graph"], ProtectionGraph(outer),
            receipt.identifier == ProtectionReceiptId && neural.identifier == ProtectionNeuralId &&
            new[] { receipt, neural, outer, inner }.All(GeneralHelper.IsItemOwned) &&
            ReferenceEquals(inner.parentInventory, ProtectionInventory(outer)) && ReferenceEquals(receipt.parentInventory, ProtectionInventory(inner)) &&
            ReferenceEquals(neural.parentInventory, ProtectionInventory(inner)) && JToken.DeepEquals(expected["graph"], ProtectionGraph(outer)));
        ProtectionRequire("protection.reload.actual-cash-story-and-sales", new { expectedCash = expected["cash"], expectedDay = expected["day"], expectedStory = expected["story"], soldTracker = expected["soldTracker"] },
            new { cash = Store.playerCash, day = StoreStation.GetDayCounter(), story = GraylineStoryState(), soldTracker = ProtectionSoldTracker() },
            Store.playerCash == (int)expected["cash"]! && StoreStation.GetDayCounter() == (int)expected["day"]! &&
            JToken.DeepEquals(expected["story"], GraylineStoryState()) && JToken.DeepEquals(expected["soldTracker"], ProtectionSoldTracker()) &&
            ((JArray)expected["matrix"]!).Count == 6 && (bool?)expected["fixtureMode"] == true);
        JToken restoredAug = Reflect.Json(AugState());
        ProtectionRequire("protection.reload.opening-and-neural-grant-receipts", new { opening = expected["aug"]?["Opening"], neuralModules = expected["aug"]?["NeuralModules"] },
            new { opening = restoredAug["Opening"], neuralModules = restoredAug["NeuralModules"] },
            JToken.DeepEquals(expected["aug"]?["Opening"], restoredAug["Opening"]) &&
            JToken.DeepEquals(expected["aug"]?["NeuralModules"], restoredAug["NeuralModules"]),
            "The original opening grant remains the source of the same restored native neural UID; no factory protected item or receipt rewrite.");
        var guard = Reflect.Type("AugMechanicalAscension.Integration.ProtectedCyberneticItems");
        ProtectionRequire("protection.reload.current-production-classifier", "Installed current guard recognizes restored real window graph and original neural UID", new
        { installed = Reflect.Get(guard, "_installed"), root = Reflect.Call(guard, "ContainsStory", outer, true), neural = Reflect.Call(guard, "ContainsStory", neural, true) },
            (bool)Reflect.Get(guard, "_installed")! && (bool)Reflect.Call(guard, "ContainsStory", outer, true)! &&
            (bool)Reflect.Call(guard, "ContainsStory", neural, true)! && !(bool)Reflect.Call(guard, "ContainsStory", neural, false)!,
            "Readonly production classifier after actual LoadGame; not a no-client trade attempt or a claim of post-load native customer sale coverage.");
        JObject before = GraylineStoryState(); JObject graph = ProtectionGraph(outer); int cash = Store.playerCash;
        float until = Time.realtimeSinceStartup + 2.5f;
        while (Time.realtimeSinceStartup < until) yield return null;
        ProtectionRequire("protection.reload.no-duplicate-or-implicit-action", new { story = before, graph, cash }, new { story = GraylineStoryState(), graph = ProtectionGraph(outer), cash = Store.playerCash },
            JToken.DeepEquals(before, GraylineStoryState()) && JToken.DeepEquals(graph, ProtectionGraph(outer)) && Store.playerCash == cash &&
            Store.FindAllItem().Count(item => item.uniqueId == receiptUid) == 1 && Store.FindAllItem().Count(item => item.uniqueId == neuralUid) == 1);
        string file = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId));
        ProtectionRequire("protection.reload.source-file-remains-unchanged", baseline["persistence"]?["saveSha256"], Reflect.HashFile(file),
            (string?)baseline["persistence"]?["saveSha256"] == Reflect.HashFile(file));
        R.persistence["augProtection"] = expected.DeepClone();
    }

    // Passive method observations only. No return values, arguments or game
    // state are written. The native Harmony sorter proves the capture position.
    private sealed class ProtectionObservation : IDisposable
    {
        private static ProtectionObservation? Active;
        private readonly ProbeContext _context;
        private readonly Harmony _hooks;
        private readonly MethodInfo _sell, _track, _bulk, _beforeBulk;
        private readonly Type _guard;
        internal readonly JArray Sales = new(), SellReturns = new(), BulkEntries = new(), BulkOrders = new(), Errors = new();
        internal string BulkLabel = "";
        internal ProtectionObservation(ProbeContext context)
        {
            if (Active != null) throw new InvalidOperationException("Protection observer already active");
            _context = context; _hooks = new Harmony("nicokobo.content_native_probe.augprotection." + context.O.RunId);
            _sell = typeof(PlayerStore).GetMethod(nameof(PlayerStore.SellItem), new[] { typeof(GameItem) })!;
            _track = typeof(SoldItem).GetMethod(nameof(SoldItem.TrackItem), new[] { typeof(GameItem), typeof(StoreClient), typeof(int) })!;
            _bulk = typeof(NegociationUIManager).GetMethod(nameof(NegociationUIManager.SellMultiple), new[] { typeof(List<GameItem>), typeof(bool) })!;
            _beforeBulk = typeof(ProtectionObservation).GetMethod(nameof(BeforeBulk), BindingFlags.NonPublic | BindingFlags.Static)!;
            _guard = Reflect.Type("AugMechanicalAscension.Integration.ProtectedCyberneticItems");
            try
            {
                // The current 11-package set has normal-priority (400) Aug and
                // Synthesis prefixes. Read actual owners too, so registration
                // order is irrelevant; each call rechecks the real sorted list.
                var owners = Harmony.GetPatchInfo(_bulk)?.Prefixes.Select(patch => patch.owner)
                    .Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
                _hooks.Patch(_bulk, prefix: new HarmonyMethod(_beforeBulk) { priority = Priority.Last, after = owners });
                _hooks.Patch(_sell, postfix: new HarmonyMethod(typeof(ProtectionObservation), nameof(AfterSell)));
                _hooks.Patch(_track, postfix: new HarmonyMethod(typeof(ProtectionObservation), nameof(AfterTrack)));
                Active = this;
                JObject order = RecordBulkOrder("observer-installation");
                _context.ProtectionRequire("protection.bulk-observer-final-position", "Actual Aug and Synthesis prefixes precede the read-only observer; no inner prefix follows it",
                    order, (bool?)order["verified"] == true,
                    "Harmony 2.4.2 PatchProcessor.GetSortedPatchMethods is read on the actual loaded SellMultiple target, not a presumed load order.");
            }
            catch { Dispose(); throw; }
        }
        private JObject RecordBulkOrder(string stage)
        {
            var info = Harmony.GetPatchInfo(_bulk) ?? throw new InvalidOperationException("Actual bulk patch metadata is missing");
            var prefixes = info.Prefixes.ToArray();
            var sorted = PatchProcessor.GetSortedPatchMethods(_bulk, prefixes);
            bool aug = prefixes.Any(patch => patch.owner == "nicokobo.aug.protected-items" &&
                patch.PatchMethod.DeclaringType?.FullName == "AugMechanicalAscension.Integration.ProtectedCyberneticItems" &&
                patch.PatchMethod.Name == "SellMultiplePrefix");
            bool synthesis = prefixes.Any(patch => patch.owner == "mod.nicokobo.mechcore.synthesis" &&
                patch.PatchMethod.DeclaringType?.FullName == "MechcoreProtocol.SynthesisExpansion.CyberEyeBulkSalePatch" &&
                patch.PatchMethod.Name == "Prefix");
            bool final = sorted.Count == prefixes.Length && sorted.Count > 0 && sorted.Last().Equals(_beforeBulk) &&
                prefixes.Count(patch => patch.owner == _hooks.Id && patch.PatchMethod.Equals(_beforeBulk)) == 1;
            var entry = new JObject
            {
                ["stage"] = stage, ["frame"] = Time.frameCount, ["harmonyAssembly"] = typeof(Harmony).Assembly.FullName,
                ["augPresent"] = aug, ["synthesisPresent"] = synthesis, ["innerPrefixCount"] = info.InnerPrefixes.Count,
                ["observerLast"] = final, ["verified"] = aug && synthesis && final && info.InnerPrefixes.Count == 0,
                ["orderedPrefixMethods"] = new JArray(sorted.Select(method => method.DeclaringType!.FullName + "." + method.Name)),
                ["prefixMetadata"] = new JArray(prefixes.Select(patch => new JObject
                {
                    ["owner"] = patch.owner, ["priority"] = patch.priority, ["index"] = patch.index,
                    ["method"] = patch.PatchMethod.DeclaringType!.FullName + "." + patch.PatchMethod.Name,
                    ["assembly"] = patch.PatchMethod.DeclaringType.Assembly.FullName,
                    ["before"] = new JArray(patch.before), ["after"] = new JArray(patch.after)
                }))
            };
            BulkOrders.Add(entry);
            return entry;
        }
        // No ref/__args/__result/return override: Harmony treats this as a
        // readonly void prefix, including when another prefix skips original.
        private static void BeforeBulk(List<GameItem> __0, bool __1, bool __runOriginal)
        {
            if (Active is not { } self) return;
            try
            {
                JObject order = self.RecordBulkOrder(self.BulkLabel);
                var items = new JArray(__0.Select(item => new JObject
                {
                    ["uid"] = item.uniqueId, ["id"] = item.identifier, ["owned"] = GeneralHelper.IsItemOwned(item),
                    ["protectedByProductionGuard"] = (bool)Reflect.Call(self._guard, "ContainsStory", item, true)!,
                    ["descendantUids"] = new JArray(item.FindAllChildItems().Select(child => child.uniqueId))
                }));
                self.BulkEntries.Add(new JObject
                {
                    ["label"] = self.BulkLabel, ["frame"] = Time.frameCount, ["ignoreBudget"] = __1,
                    ["originalWillRun"] = __runOriginal, ["orderVerified"] = (bool?)order["verified"] == true,
                    ["orderIndex"] = self.BulkOrders.Count - 1, ["items"] = items
                });
            }
            // An observer must not throw into or short-circuit the native call.
            // The caller requires exactly one complete capture and zero errors.
            catch (Exception error) { self.Errors.Add(error.ToString()); }
        }
        private static void AfterSell(GameItem __0, bool __runOriginal)
        {
            if (Active is not { } self) return;
            try { self.SellReturns.Add(JObject.FromObject(new { uid = __0.uniqueId, ranOriginal = __runOriginal, cash = self._context.Store.playerCash, frame = Time.frameCount })); }
            catch (Exception error) { self.Errors.Add(error.ToString()); }
        }
        private static void AfterTrack(GameItem __0, StoreClient __1, int __2)
        {
            if (Active is not { } self) return;
            try { self.Sales.Add(JObject.FromObject(new { uid = __0.uniqueId, id = __0.identifier, client = __1.identifier, price = __2,
                ownedAfter = GeneralHelper.IsItemOwned(__0), parentStorage = __0.parentInventory?.identifier, frame = Time.frameCount })); }
            catch (Exception error) { self.Errors.Add(error.ToString()); }
        }
        public void Dispose()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            _hooks.Unpatch(_sell, HarmonyPatchType.All, _hooks.Id);
            _hooks.Unpatch(_track, HarmonyPatchType.All, _hooks.Id);
            _hooks.Unpatch(_bulk, HarmonyPatchType.All, _hooks.Id);
        }
    }
}
