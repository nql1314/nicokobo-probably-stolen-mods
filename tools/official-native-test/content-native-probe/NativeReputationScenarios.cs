using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private readonly JArray _reputationDays = new(), _reputationTrades = new(), _reputationFundingDecisions = new();
    private readonly HashSet<int> _reputationTokenUids = new();
    private readonly HashSet<int> _reputationPaidRentDays = new();
    private static readonly string[] ReputationLegalTradeInputs =
    { "common_electronic", "common_medical", "scrap_metal", "common_ore", "wine_berry", "water_ration", "water_ration_small", "toilet_paper", "kitchen_knife", "flashlight", "screwdriver", "wire_cutter", "flux_agent", "wine_yeast", "cloth", "soap" };

    // This family is a real native FENCE run. It deliberately has no Aug
    // scenario, AugReady/FullStory guard, injected opening money or day edits.
    internal IEnumerable<object?> NativeReputation()
    {
        R.phase = "reputation-native-FENCE-opening";
        ForgeInitializeRows();
        R.environment["nativeReputationScope"] = "Build26317870 original FENCE start, default native450 cash/BM30/exchange unlocked. Generate the registered bm_rep_barter promotion as an explicit input fixture; no wait for a weekly draw. One actual scav_token fixture is admitted into the real trash inventory per eligible night. Real shutter/bedroll/End Day -> EndNight/HandleJanitorial/HandleExchange/Accept; seven actual exchanges, independent cash/token/reputation ledgers; Forge faction_max_rep real page and reward button; actual ES3 and independent-process reload. Fixture setup clears unrelated customer queues and supplies payment funds. Seven actual native exchange transactions, token consumption, reputation ledger, Forge reward and save/reload remain under assertion. No completion/claim flags are assigned.";
        R.environment["nativeReputationDays"] = _reputationDays;
        R.environment["nativeReputationTrades"] = _reputationTrades;
        R.environment["nativeReputationFundingDecisions"] = _reputationFundingDecisions;
        R.environment["forgeAchievementFixtures"] = _forgeFixtures;
        R.environment["forgeAchievementActions"] = _forgeActions;
        R.environment["forgeAchievementClaims"] = _forgeClaims;
        R.environment["graylineActions"] = _graylineActions;
        ForgeRequire("reputation.native-fresh-FENCE", "Native FENCE D1: cash450, BM30, SEC-30, unlocked exchange; normal difficulty, no perks, Steam disabled, no game cheats", new
        {
            start = NewGameData.Instance.startType.ToString(), scenario = NewGameData.Instance.startScenarioId,
            day = StoreStation.GetDayCounter(), Store.playerCash, Store.devMode, steam = SteamManager.Initialized,
            fence = Store.IsFenceStart(), Store.skipIntro, exchange = ReputationExchange(), reputations = ReputationSnapshot(), gain = Difficulty.Rules.reputationGainMultiplier
        }, !O.Reload && NewGameData.Instance.startType == NewGameData.StartType.FENCE && string.IsNullOrEmpty(NewGameData.Instance.startScenarioId) && Store.IsFenceStart() &&
            StoreStation.GetDayCounter() == 1 && Store.playerCash == 450 && ReputationAmount() == 30 && StoreReputation.GetSecFaction().GetReputationExact() == -30 &&
            Store.exchangeManager.isExchangeUnlocked && Store.skipIntro && StoreService.GetJanitorialService().introduced && StoreService.GetJanitorialService().unlocked &&
            Difficulty.Rules.reputationGainMultiplier == 1 && !StartingPerk.IsBlabbermouth() && !StartingPerk.IsTinkerer() &&
            !Store.devMode && !SteamManager.Initialized);
        ForgeValidateDenominator();
        ForgeRequire("reputation.native-initial-promotions", "Three unique native promotions drawn from seven registered offers", ReputationExchange(),
            Store.exchangeManager.promotionalBarter.Count == 7 && Store.exchangeManager.activePromotionalBarterIds.Count == 3 &&
            Store.exchangeManager.activePromotionalBarterIds.Distinct().Count() == 3 && Store.exchangeManager.activePromotionalBarterIds.All(id => Store.exchangeManager.promotionalBarter.Count(offer => offer.identifier == id) == 1));
        Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
        using var steam = new ForgeSteamObservation(this);
        using var exchange = new ReputationObservation(this);
        _forgeSteam = steam;
        R.environment["steamAchievementNativeObservation"] = steam.Evidence;
        R.environment["nativeReputationExchangeObservation"] = exchange.Evidence;
        try
        {
            _forgeBags.Add(ForgeFixture("backpack_large", "explicit legal storage input for FENCE trading and reward clearance"));
            ForgePack();
            // No arbitrary15-night impossibility claim. Continue ordinary
            // calendar until seven real promotional nights succeed. The runner
            // owns its explicit wall-clock limit; each real night saves itself.
            while (exchange.Accepts.Count < 7 || ReputationAmount() < 200)
            {
                int day = StoreStation.GetDayCounter();
                R.phase = "reputation-generated-promotion-fixture-D" + day;
                if (!Store.exchangeManager.activePromotionalBarterIds.Contains("bm_rep_barter"))
                    Store.exchangeManager.activePromotionalBarterIds.Add("bm_rep_barter");
                if (SteamManager.Initialized) throw new InvalidOperationException("Steam initialized during reputation scenario; refusing further actions");
                using var night = new NightObservation(this, Array.Empty<RecipeFixture>(), day);
                foreach (object? wait in GraylineOpen()) yield return wait;
                foreach (object? wait in ReputationServeDay()) { night.SampleClock(); yield return wait; }
                ForgePack();
                bool active = Store.exchangeManager.activePromotionalBarterIds.Contains("bm_rep_barter");
                bool eligible = active && Store.exchangeManager.isExchangeUnlocked && StoreService.GetJanitorialService().unlocked && !StoreService.GetJanitorialService().IsOnCooldown() && !StoreReputation.IsPerkUnlocked("BM_DISTRUSTED");
                GameItem? token = null;
                if (eligible)
                {
                    token = ForgeFixture("scav_token", "one explicit native barter input; not an economically earned token");
                    Place(EmporiumEntry.Instance.trashcanInvElement, token, "reputation-real-trash-token-D" + day);
                    _reputationTokenUids.Add(token.uniqueId); exchange.ExpectedTokenUids.Add(token.uniqueId);
                }
                double before = ReputationAmount(); int acceptStart = exchange.Accepts.Count, handleStart = exchange.Handles.Count, cash = Store.playerCash;
                var input = new JObject { ["day"] = day, ["promotionActive"] = active, ["eligible"] = eligible, ["exchangeBefore"] = ReputationExchange(),
                    ["reputationBefore"] = before, ["cashBeforeNight"] = cash, ["tokenUid"] = token?.uniqueId ?? 0, ["token"] = token == null ? null : ForgeItem(token) };
                _reputationDays.Add(input);
                exchange.NightArmed = true;
                try { foreach (object? wait in ReputationEndNight(day, night)) yield return wait; }
                finally { exchange.NightArmed = false; }
                var accepts = exchange.Accepts.Skip(acceptStart).ToArray(); var handles = exchange.Handles.Skip(handleStart).ToArray();
                double expected = eligible ? Math.Min(200, Math.Min(200, before + 25) + .1875) : before;
                input["dayAfter"] = StoreStation.GetDayCounter(); input["reputationAfter"] = ReputationAmount(); input["expectedReputationAfter"] = expected;
                input["exchangeAfter"] = ReputationExchange(); input["actualAccepts"] = new JArray(accepts); input["actualHandleExchange"] = new JArray(handles);
                input["night"] = night.Evidence.DeepClone();
                ForgeRequire("reputation.actual-night-barter-ledger.D" + day, new { accepts = eligible ? 1 : 0, bm = expected, tokenConsumed = eligible, actualHandleCalls = 1 }, input,
                    accepts.Length == (eligible ? 1 : 0) && handles.Length == 1 && Math.Abs(ReputationAmount() - expected) < 1e-9 &&
                    (!eligible || token != null && accepts.Length == 1 && (int?)accepts[0]["tokenUid"] == token.uniqueId && ForgeOwnedUnits(token.uniqueId) == 0 &&
                        !GraphUtils.FindAllChildrenType<GameItem>(EmporiumEntry.Instance.trashcan).Any(item => item.uniqueId == token.uniqueId)),
                    "Normal native HandleJanitorial resets barter counters and invokes HandleExchange during actual EndNight. Promotion selection is read before this night; weekly ResetPromotional occurs later in the native night. BM reward25 plus progress75/400, each independently clamped to200.");
                ForgeRequire("reputation.actual-night-observer-errors.D" + day, "No passive exchange observer errors", exchange.Errors, exchange.Errors.Count == 0);
                R.persistence["nativeReputationProgress"] = new JObject { ["lastDay"] = StoreStation.GetDayCounter(), ["actualAcceptCount"] = exchange.Accepts.Count,
                    ["bm"] = ReputationAmount(), ["days"] = _reputationDays.DeepClone(), ["lastAutomaticSave"] = night.AutoSave?.DeepClone(), ["scope"] = "progress only; not a passed completion baseline" };
                yield return null;
            }
            ForgeRequire("reputation.seven-real-exchanges-reach-cap", new { exchanges = 7, bm = 200, gainPerUncappedExchange = 25.1875 },
                new { exchanges = exchange.Accepts.Count, bm = ReputationAmount(), tokens = _reputationTokenUids.OrderBy(uid => uid).ToArray(), observer = exchange.Evidence },
                exchange.Accepts.Count == 7 && ReputationAmount() == 200 && exchange.Accepts.All(row => (bool?)row["registeredPromotion"] == true && (bool?)row["insideNativeNight"] == true && (bool?)row["valid"] == true));
            var goal = ForgeGoalInventory.Single(goal => goal.Id == "faction_max_rep");
            _forgeGoals[goal.Id]["businessEvidence"] = new JObject { ["nativeFENCE"] = true, ["actualExchanges"] = exchange.Accepts.DeepClone(), ["days"] = _reputationDays.DeepClone(), ["reputations"] = ReputationSnapshot() };
            foreach (object? wait in ForgeClaim(goal)) yield return wait;
            ForgeRequire("reputation.faction-achievement-actually-claimed", "Real native Forge faction_max_rep button granted exactly one smuggler_bay_mod", _forgeGoals[goal.Id], (string?)_forgeGoals[goal.Id]["claim"] == "passed");
            foreach (object? wait in ForgeSaveReadback()) yield return wait;
            ReputationSaveReadback(exchange);
            foreach (var other in ForgeGoalInventory.Where(other => other.Id != "faction_max_rep"))
                R.Skip("reputation.other-forge-goal." + other.Id, "This independent original FENCE family covers faction_max_rep. Use the documented actual qualification family for this other goal; no success is inferred.", false);
        }
        finally
        {
            steam.WriteRows(); _forgeSteam = null;
            Reflect.Call(ForgeWorkshopUi, "CloseWindow");
        }
    }

    private static double ReputationAmount() => StoreReputation.GetBMFaction().GetReputationExact();
    private JArray ReputationSnapshot() => new(Store.storeReputations.OrderBy(faction => faction.factionId, StringComparer.Ordinal)
        .Select(faction => JObject.FromObject(new { id = faction.factionId, amount = faction.GetReputationExact() })));
    private JObject ReputationExchange() => new()
    {
        ["unlocked"] = Store.exchangeManager.isExchangeUnlocked,
        ["janitorial"] = JObject.FromObject(new { StoreService.GetJanitorialService().introduced, StoreService.GetJanitorialService().unlocked,
            StoreService.GetJanitorialService().isUsing, StoreService.GetJanitorialService().cost, StoreService.GetJanitorialService().currentCooldown }),
        ["activePromotions"] = new JArray(Store.exchangeManager.activePromotionalBarterIds.OrderBy(id => id, StringComparer.Ordinal)),
        ["registeredPromotions"] = new JArray(Store.exchangeManager.promotionalBarter.Select(offer => offer.identifier).OrderBy(id => id, StringComparer.Ordinal)),
        ["bmOffer"] = new JObject { ["counter"] = Store.exchangeManager.promotionalBarter.Single(offer => offer.identifier == "bm_rep_barter").currentBarterCounter,
            ["perNight"] = Store.exchangeManager.promotionalBarter.Single(offer => offer.identifier == "bm_rep_barter").barterCounterPerNight }
    };
    private int ReputationCashReserve() => Math.Max(450, 2 * Store.GetRentValue() + 7 * (Store.storeService.Where(service => service.unlocked && service.isUsing).Sum(service => service.cost) +
        (StoreService.GetJanitorialService().unlocked && !StoreService.GetJanitorialService().isUsing ? StoreService.GetJanitorialService().cost : 0)) + 50);

    // A target for sales to actual queued buyers, not a prerequisite for a day
    // that has no more customers. The native End Day tablet rechecks its real
    // invoice after the ordinary janitorial checkbox is selected below.
    private int ReputationTonightInvoice() => Store.storeService.Where(service => service.unlocked && service.isUsing).Sum(service => service.cost) +
        (StoreService.GetJanitorialService().unlocked && !StoreService.GetJanitorialService().isUsing ? StoreService.GetJanitorialService().cost : 0);
    // GetRentDayCounter is calendar-only: it remains zero on D7 after paying.
    // Only a verified actual rent debit may settle this day's rent obligation;
    // it cannot settle a mortgage or any later rent day.
    private static int ReputationRequiredDuePayment(bool propertyPaid, int calendarRentCounter, bool payingMortgage,
        bool verifiedRentPaidToday, int rent, int mortgage) =>
        propertyPaid || calendarRentCounter != 0 || !payingMortgage && verifiedRentPaidToday ? 0 : payingMortgage ? mortgage : rent;
    private int ReputationDuePayment() => ReputationRequiredDuePayment(Store.IsPropertyPaid, Store.GetRentDayCounter(), Store.isPayingMortgage,
        _reputationPaidRentDays.Contains(StoreStation.GetDayCounter()), Store.GetRentValue(), Store.mortgageWeeklyPayment);
    private static bool ReputationMayCloseDay(int cash, int reserveTarget, int nightInvoice, int duePayment, int queueCount,
        bool canEndDay, bool currentClientPresent, bool dialoguePresent) =>
        canEndDay && !currentClientPresent && !dialoguePresent && queueCount >= 0 && nightInvoice >= 0 && duePayment >= 0 &&
        cash >= (long)nightInvoice + duePayment && (cash >= reserveTarget || queueCount == 0);
    private JObject ReputationFundingState()
    {
        int cash = Store.playerCash, reserve = ReputationCashReserve(), invoice = ReputationTonightInvoice(), due = ReputationDuePayment();
        int queued = Store.storeClientManager.clientStack.Count;
        bool canEnd = Store.CanEndDay(), client = Store.currentClientInstance != null, dialog = DialogUIManager.Instance.currentDialog != null;
        return new JObject { ["day"] = StoreStation.GetDayCounter(), ["cash"] = cash, ["reserveTarget"] = reserve, ["nightInvoice"] = invoice,
            ["duePayment"] = due, ["calendarRentCounter"] = Store.GetRentDayCounter(), ["nativeDaysUntilRent"] = Store.dayUntilRent,
            ["verifiedRentPaidToday"] = _reputationPaidRentDays.Contains(StoreStation.GetDayCounter()), ["propertyPaid"] = Store.IsPropertyPaid, ["payingMortgage"] = Store.isPayingMortgage,
            ["queueCount"] = queued, ["queue"] = new JArray(Store.storeClientManager.clientStack.Select(item => new JObject
                { ["id"] = item.identifier, ["dismissable"] = item.dismissable, ["intent"] = item.clientIntent.ToString() })),
            ["canEndDay"] = canEnd, ["canNextClient"] = Store.CanNextClient(), ["currentClientPresent"] = client,
            ["dialoguePresent"] = dialog, ["lockNextBellFlag"] = Store.lockNextBellFlag, ["lockShutterButtonFlag"] = Store.lockShutterButtonFlag,
            ["ready"] = ReputationMayCloseDay(cash, reserve, invoice, due, queued, canEnd, client, dialog) };
    }

    private IEnumerable<object?> ReputationServeDay()
    {
        foreach (object? wait in FixturePrepareNight()) yield return wait;
    }
    private IEnumerable<object?> ReputationOrdinaryClient(StoreClient client)
    {
        float deadline = Time.realtimeSinceStartup + 180; bool paidRent = false, mortgageDeclined = false, tradeAttempted = false, rang = false, vendingDeclined = false;
        JObject? mortgageBefore = null;
        while (ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client))
        {
            if (Time.realtimeSinceStartup >= deadline || GameOverUIManager.Instance?.IsShowing == true) throw new InvalidOperationException("Actual native client did not finish: " + client.identifier);
            GraylineAdvanceDialogue();
            if (GraylineChoicesVisible())
            {
                if (client.identifier == "landlord" && !paidRent)
                {
                    int amount = Store.GetRentValue(), cash = Store.playerCash; string label = LocHelper.GetLocalizedDialogue("dialog_landlord_player_pay", amount);
                    ForgeRequire("reputation.landlord.real-pay-option", "Actual affordable visible rent choice", new { cash, amount, client = client.identifier }, Store.CanPayRent() && GraylineChoice(label, label) != null);
                    ForgeAction("reputation-native-rent-choice", new { cash, amount }); GraylineClickChoice(label, label, "reputation-pay-rent"); paidRent = true;
                    ForgeRequire("reputation.landlord.actual-charge-D" + StoreStation.GetDayCounter(), cash - amount, Store.playerCash, Store.playerCash == cash - amount);
                    _reputationPaidRentDays.Add(StoreStation.GetDayCounter());
                }
                else if (client.identifier == "loan_shark2" && !mortgageDeclined)
                {
                    string label = LocHelper.GetLocalizedDialogue("dialog_loan_shark2_player_decline");
                    ForgeRequire("reputation.mortgage.exact-decline-option", "Known loan_shark2 final offer with visible native decline", DialogUIManager.Instance.currentDialog.plainText,
                        DialogUIManager.Instance.currentDialog.plainText == LocHelper.GetLocalizedDialogue("dialog_loan_shark2_last_line") && GraylineChoice(label, label) != null);
                    mortgageBefore = ReputationMortgage(); ForgeAction("reputation-explicit-decline-loan", mortgageBefore); GraylineClickChoice(label, label, "reputation-decline-mortgage"); mortgageDeclined = true;
                }
                else if (client.identifier == "landlordVending" && !vendingDeclined)
                {
                    string label = LocHelper.GetLocalizedDialogue("dialog_landlord_vending_player_decline");
                    ForgeRequire("reputation.vending.exact-decline-option", "Known optional vending offer's visible decline choice", DialogUIManager.Instance.currentDialog.plainText, GraylineChoice(label, label) != null);
                    ForgeAction("reputation-explicit-decline-optional-vending"); GraylineClickChoice(label, label, "reputation-decline-vending"); vendingDeclined = true;
                }
                else if (!Store.IsClientDimissable()) throw new InvalidOperationException("Unknown mandatory native choice: " + client.identifier + "; " + DialogUIManager.Instance.currentDialog.plainText + ". No fallback option selected.");
            }
            bool dialogueIdle = Reflect.Get(DialogUIManager.Instance, "displayingUncompletedDialog") == null && !DialogUIManager.Instance.isWaitingForManualAdvance && !GraylineChoicesVisible();
            if (!tradeAttempted && dialogueIdle && Store.IsClientDimissable() && client.identifier != "landlord" && Store.playerCash < ReputationCashReserve())
            { tradeAttempted = true; foreach (object? wait in ReputationFundWithActualSales(client)) yield return wait; }
            if (!rang && Store.IsClientDimissable() && Store.lockNextBellFlag == 0 && Reflect.Get(DialogUIManager.Instance, "displayingUncompletedDialog") == null && !DialogUIManager.Instance.isWaitingForManualAdvance && !GraylineChoicesVisible())
            { GraylineRingBell(); rang = true; }
            yield return null;
        }
        if (mortgageBefore != null)
        {
            var expected = (JObject)mortgageBefore.DeepClone(); expected["declined"] = true;
            ForgeRequire("reputation.mortgage.actual-decline-only", expected, ReputationMortgage(), JToken.DeepEquals(expected, ReputationMortgage()), "Actual reply endAction/departure sets only decline flag; no loan cash, mortgage terms or ownership writes by probe.");
        }
        ForgePack();
    }
    private JObject ReputationMortgage() => JObject.FromObject(new { cash = Store.playerCash, declined = Store.IsMortgageOfferDeclined,
        paying = Store.isPayingMortgage, paid = Store.IsPropertyPaid, Store.mortgageWeekLeft, Store.mortgageWeeklyPayment, Store.mortgageDownPaid,
        Store.mortgageTotalWeeks, Store.mortgageInterestRateTaken, Store.propertyBoughtAt });

    private IEnumerable<object?> ReputationFundWithActualSales(StoreClient client)
    {
        int attempts = 0;
        // A willing buyer can have budget left after one unit (the observed
        // D4 buyer retained 279). Offer more default inputs through the same
        // native guards, with at most eight real sale attempts per customer.
        for (int round = 0; round < 8; round++)
        foreach (string id in ReputationLegalTradeInputs)
        {
            if (Store.playerCash >= ReputationCashReserve() || attempts >= 8 || !ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client)) yield break;
            if (!DirectoryMaster.Has<GameItem>(id)) continue;
            var item = Spawn(id);
            bool willing = client.IsClientBuyingThisItem(item) && !client.CanClientExposeAnyFeatureDeep(item) &&
                (!client.useClientBudget || client.HasBudgetLeftToBuy(item)) && !ContrabandHelper.IsContraband(item) && !StolenHelper.IsStolenItem(item);
            if (!willing) { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); continue; }
            attempts++; Retain(item);
            _forgeFixtures.Add(new JObject { ["purpose"] = "explicit default legal factory input for a real naturally arrived customer's guarded sale; not economic acquisition", ["item"] = ForgeItem(item) });
            var marker = EmporiumEntry.Instance.backInvinvElement.TryFindOneValidInventorySlot(item);
            if (marker?.IsValid() != true) throw new InvalidOperationException("Real weighted counter cannot legally receive funding fixture " + id);
            GraphUtils.TryAcceptOnce(EmporiumEntry.Instance.backInvinvElement, item); yield return null;
            float responseDeadline = Time.realtimeSinceStartup + 60;
            while (Reflect.Get(DialogUIManager.Instance, "displayingUncompletedDialog") != null || DialogUIManager.Instance.isWaitingForManualAdvance)
            {
                if (Time.realtimeSinceStartup >= responseDeadline) throw new InvalidOperationException("Native customer did not finish inspecting a legitimate sale fixture");
                GraylineAdvanceDialogue(); if (GraylineChoicesVisible()) throw new InvalidOperationException("Unexpected mandatory choice during actual sale inspection"); yield return null;
            }
            bool eligible = ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client) && Store.isClientArrived && item.parentInventory == EmporiumEntry.Instance.backInvinvElement && GeneralHelper.IsItemOwned(item) &&
                Store.CanSellThisItem(item) && NegociationUIManager.Instance.GetSellableItem().Contains(item);
            if (!eligible)
            {
                GraphUtils.TryAcceptOnce(EmporiumEntry.Instance.invElement, item); ForgePack(); continue;
            }
            int uid = item.uniqueId, cash = Store.playerCash, budget = client.GetBudget(); long quote = item.GetNegociatedValue();
            ForgeRequire("reputation.sale.native-guard-and-affordable-quote." + uid, "Owned unique default input; actual customer accepts, quote positive and fully within budget", new { uid, cash, budget, quote, id }, quote > 0 && quote <= int.MaxValue && (!client.useClientBudget || quote <= budget));
            var beforeRep = ReputationSnapshot();
            ForgeAction("native-NegociationUIManager.SellMultiple", new { uid, id, client = client.identifier, quote });
            NegociationUIManager.Instance.SellMultiple(new List<GameItem> { item }); yield return null;
            ForgeRequire("reputation.sale.actual-payment-and-ownership." + uid, new { cash = cash + quote, budget = client.useClientBudget ? budget - quote : budget, ownedUnits = 0 },
                new { cash = Store.playerCash, budget = client.GetBudget(), ownedUnits = ForgeOwnedUnits(uid), itemOwned = GeneralHelper.IsItemOwned(item) },
                Store.playerCash == cash + quote && (!client.useClientBudget || client.GetBudget() == budget - quote) && ForgeOwnedUnits(uid) == 0 && !GeneralHelper.IsItemOwned(item),
                "The real bulk-sale UI callback calls native CanSellThisItem/TryReinspectBeforeSale/SellItem; customer OnItemBought, ModBudget and cash transfer run normally. No SellItem direct call, quote assignment or cash write.");
            _reputationTrades.Add(new JObject { ["day"] = StoreStation.GetDayCounter(), ["client"] = client.identifier, ["uid"] = uid, ["id"] = id, ["quote"] = quote,
                ["cashBefore"] = cash, ["cashAfter"] = Store.playerCash, ["budgetBefore"] = budget, ["budgetAfter"] = client.GetBudget(), ["reputationsBefore"] = beforeRep, ["reputationsAfter"] = ReputationSnapshot() });
            responseDeadline = Time.realtimeSinceStartup + 60;
            while (Reflect.Get(DialogUIManager.Instance, "displayingUncompletedDialog") != null || DialogUIManager.Instance.isWaitingForManualAdvance)
            {
                if (Time.realtimeSinceStartup >= responseDeadline) throw new InvalidOperationException("Actual sale reply did not finish");
                GraylineAdvanceDialogue(); if (GraylineChoicesVisible()) throw new InvalidOperationException("Unexpected choice after funding sale"); yield return null;
            }
        }
    }

    private IEnumerable<object?> ReputationEndNight(int day, NightObservation observation)
    {
        ForgeRequire("reputation.close-native-eligibility.D" + day, "Native CanEndDay, no locks/arrest/shootout/cheats", GraylineUiState(), Store.CanEndDay() && Store.lockShutterButtonFlag == 0 && !Store.isClientBeingArrested && !Store.isHandlingShootout && !Store.devMode);
        ForgeAction("reputation-real-shutter-close"); StoreShutterButton.Instance.OnClick();
        if (StoreUIManager.Instance.closeShutterConfirmationPanel.activeSelf) StoreShutterButton.Instance.EndAndClose();
        foreach (object? wait in NightWait("reputation.closed.D" + day, () => Store.storeState == PlayerStore.StoreState.CLOSED && Store.currentClientInstance == null &&
            StoreShutterButton.Instance.shutterGameObject.transform.position == StoreShutterButton.Instance.startPosition &&
            StoreShutterButton.Instance.blockerGameObject.transform.localPosition == StoreShutterButton.Instance.blockerStartPosition, observation)) yield return wait;
        var beds = UnityEngine.Object.FindObjectsOfType<StoreBedroll>().Where(bed => bed.gameObject.activeInHierarchy).ToArray();
        ForgeRequire("reputation.actual-bedroll.D" + day, 1, beds.Length, beds.Length == 1); beds[0].OnClick();
        foreach (object? wait in NightWait("reputation.real-end-day-ui.D" + day, () => EndOfDayUIManager.Instance.IsOpen && Reflect.Get(EndOfDayUIManager.Instance, "endDayButton") is TabletButton, observation)) yield return wait;
        // Exchange is provided by paid native janitorial service. Select its
        // actual tablet checkbox; never assign introduced/unlocked/isUsing.
        var janitorial = StoreService.GetJanitorialService();
        var rows = Reflect.Each(Reflect.Get(EndOfDayUIManager.Instance, "rows")).Where(row => ReferenceEquals(Reflect.Get(row, "Item1"), janitorial)).ToArray();
        ForgeRequire("reputation.actual-janitorial-row.D" + day, "One native unlocked, noncooldown janitorial checkbox", new { count = rows.Length, janitorial.unlocked, cooldown = janitorial.IsOnCooldown() },
            janitorial.unlocked && !janitorial.IsOnCooldown() && rows.Length == 1);
        var checkbox = (TabletCheckbox)Reflect.Get(rows[0], "Item2")!;
        if (!checkbox.IsOn)
        {
            ForgeRequire("reputation.actual-janitorial-checkbox-enabled.D" + day, "Visible interactable native checkbox button", checkbox.button?.IsInteractable(), checkbox.gameObject.activeInHierarchy && checkbox.button != null && checkbox.button.IsInteractable());
            ForgeAction("native-janitorial-checkbox-button"); checkbox.button!.onClick.Invoke(); yield return null;
        }
        ForgeRequire("reputation.actual-janitorial-selected.D" + day, "Native toggle callback selected paid cleaning before EndNight", new { checkbox.IsOn, janitorial.isUsing }, checkbox.IsOn && janitorial.isUsing);
        var end = (TabletButton)Reflect.Get(EndOfDayUIManager.Instance, "endDayButton")!;
        observation.Invoice = new JArray(Store.storeService.Where(service => service.unlocked && service.isUsing).Select(service => JObject.FromObject(new { service.id, service.cost, service.totalCooldown })));
        observation.InvoiceTotal = Store.storeService.Where(service => service.unlocked && service.isUsing).Sum(service => service.cost);
        observation.Evidence["invoice"] = observation.Invoice;
        ForgeRequire("reputation.affordable-real-end-day.D" + day, "Actual enabled End Day button and affordable invoice", new { cash = Store.playerCash, observation.InvoiceTotal, end.Interactable }, end.gameObject.activeInHierarchy && end.Interactable && end.button != null && end.button.IsInteractable() && observation.InvoiceTotal <= Store.playerCash);
        observation.Armed = true; ForgeAction("reputation-real-end-day-button", new { observation.InvoiceTotal }); end.button!.onClick.Invoke();
        foreach (object? wait in NightWait("reputation.actual-next-morning.D" + day, () => StoreStation.GetDayCounter() == day + 1 && Store.storeState == PlayerStore.StoreState.MORNING &&
            !StoreUIManager.Instance.blackscreen.gameObject.activeSelf && !StoreUIManager.Instance.savingTMP.gameObject.activeSelf && !EndOfDayUIManager.Instance.IsOpen &&
            AdvCalendarUIManager.Instance.UIPanel.activeSelf && AdvCalendarUIManager.Instance.nightlyReportSection.activeSelf && observation.Events.Count(row => (string?)row["name"] == "OnWakingUpLate") == 1, observation, 60)) yield return wait;
        observation.Armed = false;
        ForgeRequire("reputation.exact-native-night-and-auto-save.D" + day, "One actual EndNight/BeginDay/SaveGame, ten ordered lifecycle events, actual ES3 readback before waking", observation.Evidence,
            new[] { "EndNight", "BeginDay", "SaveGame" }.All(name => observation.Entered.GetValueOrDefault(name) == 1 && observation.Returned.GetValueOrDefault(name) == 1) &&
            observation.Events.Select(row => (string)row["name"]!).SequenceEqual(NightEvents) &&
            observation.Events.All(row => (int)row["day"]! == ((string)row["name"]! is "OnWakingUpEarly" or "OnWakingUpLate" ? day + 1 : day)) &&
            observation.AutoSave != null && (bool?)observation.AutoSave["valid"] == true && observation.ObserverErrors.Count == 0 && observation.Errors.Count == 0 &&
            observation.MinimumTimeScale == 1 && observation.MaximumTimeScale == 1);
        var sleep = observation.Events.Single(row => (string?)row["name"] == "OnGoingSleepEarly");
        var charged = observation.Events.Single(row => (string?)row["name"] == "OnHandlingNightlyServicesEarly");
        ForgeRequire("reputation.actual-service-payment.D" + day, observation.InvoiceTotal, (int)sleep["cash"]! - (int)charged["cash"]!, (int)sleep["cash"]! - (int)charged["cash"]! == observation.InvoiceTotal);
    }

    private void ReputationSaveReadback(ReputationObservation observation)
    {
        string path = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId)); var settings = new ES3Settings(path, ES3.Location.File);
        var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", settings)!;
        var savedRep = new JArray(saved.storeReputations.OrderBy(faction => faction.factionId, StringComparer.Ordinal).Select(faction => JObject.FromObject(new { id = faction.factionId, amount = faction.GetReputationExact() })));
        ForgeRequire("reputation.save.actual-es3-factions-and-exchange", new { factions = ReputationSnapshot(), unlocked = true, active = Store.exchangeManager.activePromotionalBarterIds.OrderBy(id => id, StringComparer.Ordinal).ToArray() },
            new { factions = savedRep, unlocked = saved.exchangeManager.isExchangeUnlocked, active = saved.exchangeManager.activePromotionalBarterIds.OrderBy(id => id, StringComparer.Ordinal).ToArray() },
            JToken.DeepEquals(savedRep, ReputationSnapshot()) && savedRep.Any(row => (string?)row["id"] == "FACTION_BLACK_MARKET" && (double?)row["amount"] == 200) &&
            saved.exchangeManager.isExchangeUnlocked && Store.exchangeManager.activePromotionalBarterIds.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(saved.exchangeManager.activePromotionalBarterIds.OrderBy(id => id, StringComparer.Ordinal)));
        R.persistence["nativeReputation"] = new JObject { ["source"] = "original FENCE, real promotional barter and actual EndNight; no reputation/date/flag writes", ["factions"] = savedRep,
            ["day"] = ES3.Load<StoreStation>("storeStation", settings).dayCounter, ["cash"] = saved.playerCash, ["exchangeUnlocked"] = true,
            ["activePromotions"] = new JArray(saved.exchangeManager.activePromotionalBarterIds.OrderBy(id => id, StringComparer.Ordinal)), ["actualAccepts"] = observation.Accepts.DeepClone(),
            ["days"] = _reputationDays.DeepClone(), ["trades"] = _reputationTrades.DeepClone(), ["tokenUids"] = new JArray(_reputationTokenUids.OrderBy(uid => uid)), ["es3Path"] = path, ["es3Sha256AtReadback"] = Reflect.HashFile(path) };
    }
    internal IEnumerable<object?> NativeReputationReload(JObject baseline)
    {
        R.phase = "reputation-independent-native-reload";
        var evidence = baseline["persistence"]?["nativeReputation"] as JObject ?? throw new InvalidOperationException("Native reputation ES3 baseline missing");
        ForgeRequire("reputation.reload.original-FENCE-and-seven-real-exchanges", "Normal LoadGame of native FENCE with seven completed native promotional exchanges", evidence,
            (string?)baseline["mode"] == "reputation-save" && Store.IsFenceStart() && ((JArray)evidence["actualAccepts"]!).Count == 7 &&
            ((JArray)evidence["actualAccepts"]!).All(row => (bool?)row["valid"] == true) && !SteamManager.Initialized && !Store.devMode);
        ForgeRequire("reputation.reload.exact-factions-cash-calendar", new { factions = evidence["factions"], cash = evidence["cash"], day = evidence["day"] },
            new { factions = ReputationSnapshot(), cash = Store.playerCash, day = StoreStation.GetDayCounter() },
            JToken.DeepEquals(evidence["factions"], ReputationSnapshot()) && ReputationAmount() == 200 && Store.playerCash == (int)evidence["cash"]! && StoreStation.GetDayCounter() == (int)evidence["day"]!);
        ForgeRequire("reputation.reload.serialized-promotion-and-unlock", new { active = evidence["activePromotions"], unlocked = true }, ReputationExchange(),
            Store.exchangeManager.isExchangeUnlocked && JToken.DeepEquals(evidence["activePromotions"], new JArray(Store.exchangeManager.activePromotionalBarterIds.OrderBy(id => id, StringComparer.Ordinal))));
        foreach (int uid in ((JArray)evidence["tokenUids"]!).Values<int>())
            ForgeRequire("reputation.reload.consumed-token-not-recreated." + uid, 0, ForgeOwnedUnits(uid), ForgeOwnedUnits(uid) == 0 && !GraphUtils.FindAllChildrenType<GameItem>(EmporiumEntry.Instance.trashcan).Any(item => item.uniqueId == uid));
        foreach (object? wait in ForgeAchievementsReload(baseline)) yield return wait;
        R.persistence["nativeReputationReload"] = evidence.DeepClone();
    }

    private sealed class ReputationObservation : IDisposable
    {
        private static ReputationObservation? Active;
        private readonly ProbeContext _context;
        private readonly Harmony _hooks;
        private readonly List<MethodInfo> _patched = new();
        private readonly Dictionary<ExchangeBarter, JObject> _pending = new();
        internal readonly HashSet<int> ExpectedTokenUids = new();
        internal readonly JArray Accepts = new(), Handles = new(), Errors = new();
        internal readonly JObject Evidence;
        internal bool NightArmed;
        internal ReputationObservation(ProbeContext context)
        {
            if (Active != null) throw new InvalidOperationException("Reputation observer already active");
            _context = context; _hooks = new Harmony("nicokobo.content_native_probe.reputation." + context.O.RunId);
            Evidence = new JObject { ["accepts"] = Accepts, ["handles"] = Handles, ["errors"] = Errors };
            try
            {
                Patch(typeof(ExchangeBarter).GetMethod(nameof(ExchangeBarter.Accept), Type.EmptyTypes)!, nameof(BeforeAccept), nameof(AfterAccept));
                Patch(typeof(ExchangeManager).GetMethod(nameof(ExchangeManager.HandleExchange), Type.EmptyTypes)!, null, nameof(AfterHandle)); Active = this;
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodInfo method, string? before, string after)
        { _hooks.Patch(method, prefix: before == null ? null : new HarmonyMethod(typeof(ReputationObservation), before), postfix: new HarmonyMethod(typeof(ReputationObservation), after)); _patched.Add(method); }
        private void Observe(Action action) { try { action(); } catch (Exception error) { Errors.Add(error.ToString()); } }
        private static void BeforeAccept(ExchangeBarter __instance)
        {
            if (Active is not { } observer || __instance.identifier != "bm_rep_barter") return;
            observer.Observe(() =>
            {
                var inputs = __instance.GetRequiredItems();
                var manager = observer._context.Store.exchangeManager;
                bool registered = manager.promotionalBarter.Any(offer => ReferenceEquals(offer, __instance));
                observer._pending[__instance] = new JObject { ["day"] = StoreStation.GetDayCounter(), ["frame"] = Time.frameCount,
                    ["counterBefore"] = __instance.currentBarterCounter, ["eligible"] = __instance.CanAccept(), ["reputationBefore"] = ReputationAmount(),
                    ["tokenUid"] = inputs.Count == 1 ? inputs[0].uniqueId : 0, ["inputs"] = new JArray(inputs.Select(ForgeItem)), ["registeredPromotion"] = registered,
                    ["promotionActive"] = manager.activePromotionalBarterIds.Contains("bm_rep_barter"), ["insideNativeNight"] = observer.NightArmed };
            });
        }
        private static void AfterAccept(ExchangeBarter __instance)
        {
            if (Active is not { } observer || __instance.identifier != "bm_rep_barter") return;
            observer.Observe(() =>
            {
                if (!observer._pending.TryGetValue(__instance, out var row)) throw new InvalidOperationException("Native barter return without its passive before snapshot");
                observer._pending.Remove(__instance); int uid = (int)row["tokenUid"]!; double before = (double)row["reputationBefore"]!;
                row["counterAfter"] = __instance.currentBarterCounter; row["reputationAfterAcceptBeforeProgress"] = ReputationAmount();
                row["valid"] = (bool?)row["eligible"] == true && (bool?)row["registeredPromotion"] == true && (bool?)row["promotionActive"] == true && (bool?)row["insideNativeNight"] == true &&
                    (int?)row["counterBefore"] == 1 && __instance.currentBarterCounter == 0 && observer.ExpectedTokenUids.Contains(uid) &&
                    ((JArray)row["inputs"]!).Count == 1 && (string?)row["inputs"]![0]!["id"] == "scav_token" &&
                    !GraphUtils.FindAllChildrenType<GameItem>(EmporiumEntry.Instance.trashcan).Any(item => item.uniqueId == uid) &&
                    Math.Abs(ReputationAmount() - Math.Min(200, before + 25)) < 1e-9;
                observer.Accepts.Add(row);
            });
        }
        private static void AfterHandle()
        {
            if (Active is not { } observer) return;
            observer.Observe(() => observer.Handles.Add(new JObject { ["day"] = StoreStation.GetDayCounter(), ["frame"] = Time.frameCount,
                ["insideNativeNight"] = observer.NightArmed, ["reputationAfterProgress"] = ReputationAmount(), ["exchange"] = observer._context.ReputationExchange() }));
        }
        public void Dispose()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            foreach (var method in _patched) _hooks.Unpatch(method, HarmonyPatchType.All, _hooks.Id);
        }
    }
}
