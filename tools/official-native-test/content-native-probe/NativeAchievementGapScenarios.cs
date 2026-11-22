using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private static readonly string[] GapGoalIds = { "wealthy_store", "store_attractiveness" };
    private static readonly string[] GapPriorNotTested =
    {
        "achievements.remaining.store_attractiveness: Actual expanded native grid cannot fit enough unmodified modules: mirage_projector; quality=600, required=700",
        "achievements.remaining.wealthy_store: Actual independent native full-batch rehearsal exhausted all main-grid positions/orientations for machine_bay_ext #0; earlier samples and original items occupied their real cells. Claim was not attempted; see capacityProof. This cannot apply to the zero-item victory reward.",
        "achievements.remaining.store_attractiveness: Independent current qualification is not met. Next step: Continue the recorded actual preparation/business path until every listed qualification holds, then reopen the actual page."
    };
    private readonly Dictionary<int, (string Id, int Count, string Purpose)> _gapCommitments = new();
    private readonly Dictionary<int, string> _gapConsumed = new();
    private readonly JArray _gapActions = new();
    private GameItem _gapWealthReward = null!, _gapProjector = null!, _gapAttractionReward = null!;
    private static readonly string[] GapTags =
    {
        "IS_OWNED_TAG", "MODULE_UPGRADE_STAGE_INT", "power_source_item_energy", "power_source_item_max_energy",
        "MACHINERY_DRAWN_POWER_BASE", "machinery_drawn_power", "MACHINE_PERFORMANCE_RATIO_INT", "MACHINE_QUALITY_RATIO_INT",
        "TOTAL_PERCENTAGE_QUALITY_BONUS_INT", "TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT", "TOTAL_PERCENTAGE_POWER_BONUS_INT",
        "BONUS_PERCENTAGE_PERFORMANCE_INT", "BONUS_PERCENTAGE_EFFICIENCY_INT", "BONUS_PERCENTAGE_QUALITY_INT",
        "TEMP_PERCENTAGE_PERFORMANCE_INT", "TEMP_PERCENTAGE_EFFICIENCY_INT", "TEMP_PERCENTAGE_QUALITY_INT",
        "IS_PROJECTOR_ACTIVE", "LOW_POWER_MODE_BOOL", "MODULE_STUCK_TAG"
    };

    internal IEnumerable<object?> NativeAchievementGaps()
    {
        GapInitialize();
        R.phase = "achievementsgaps.native-Aug-fresh-opening";
        R.environment["nativeAchievementGapPriorRun"] = "achievements-save-97ac5449-e9bd-47c6-87fe-023dfb696175 slot72; prior reasons remain historical not-tested, not rewritten as passed";
        R.environment["nativeAchievementGapPriorNotTested"] = new JArray(GapPriorNotTested);
        R.environment["nativeAchievementGapScope"] = "Exactly wealthy_store and store_attractiveness. Supported Aug InitialCredits=150000 fixture, not earned wealth. Wealth claim first through existing real Forge node/claim UI plus whole native reward-placement rehearsal; then real RENOVATION1 UI purchase500, legal two-kit projector expansion and six native uncapped cores, 250kJ battery. One complete natural D1->D2 night with actual paid janitorial checkbox and native powered projector cycle; no manual native cycle invocation. Fixed UID/ID/count commitments include original rewards, proof trees and genuinely consumed kits. Independent bonus700/power112/base400/penalty formula. Actual ES3 and independent original-slot reload. No cash/date/attractiveness/projector/completion/claim/upgrade state assignments, customer mocks or Steam account writes.";
        ForgeRequire("achievementsgaps.fresh-supported-150000", "Fresh Aug D1, supported InitialCredits150000, normal rules, no perks/cheats or Steam", new
        {
            day = StoreStation.GetDayCounter(), Store.playerCash, Store.devMode, steam = SteamManager.Initialized,
            scenario = NewGameData.Instance.startScenarioId, config = AugState() == null ? null : Reflect.Json(Reflect.Get(AugState()!, "ConfigSnapshot")),
            startingPerks = NewGameData.Instance.startingPerks.Select(perk => perk.id).ToArray(), Time.timeScale, priceMultiplier = Difficulty.Rules.upgradePriceMultiplier
        }, !O.Reload && !SteamManager.Initialized && !Store.devMode && AugReady() && StoreStation.GetDayCounter() == 1 &&
            Reflect.Int(Reflect.Get(AugState()!, "ConfigSnapshot")!, "InitialCredits") == 150000 && Store.playerCash == 150000 &&
            NewGameData.Instance.startingPerks.Select(perk => perk.id).SequenceEqual(new[] { "nicokobo.aug.neural_network" }) && Difficulty.Rules.upgradePriceMultiplier == 1 &&
            !StoreReputation.IsPerkUnlocked("LLPILLAR"));
        Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
        ForgeValidateDenominator();
        try
        {
            ForgeRequire("achievementsgaps.wealth-first-before-hardware", "Native asset threshold strictly>100000 before any fixture hardware or prior claim", new
            { wealth = Store.GetTotalEstimatedValue(), fixtures = _forgeFixtures.Count, claims = _forgeClaims.Count },
                Store.GetTotalEstimatedValue() > 100000 && _forgeFixtures.Count == 0 && _forgeClaims.Count == 0);
            foreach (object? wait in ForgeClaim(GapGoal("wealthy_store"))) yield return wait;
            _gapWealthReward = GapClaimedReward("wealthy_store", "machine_bay_ext");
            GapCommit(_gapWealthReward, "original whole wealthy_store reward actually delivered by the real Forge claim");
            GapAudit("achievementsgaps.wealth-original-reward-fixed");
            // The actual reward's native11x14 interior clears later main-grid
            // room. Rejected admissions stay in place; no parent/lock changes.
            GapClearMainIntoWealthReward();
            foreach (object? wait in GapBuyRenovation()) yield return wait;
            foreach (object? wait in GapPrepareProjector()) yield return wait;
            GapAudit("achievementsgaps.before-complete-native-night");
            int day = StoreStation.GetDayCounter();
            var battery = MachineHelper.GetBatterySlot(_gapProjector).childItem;
            int energy = Energy(battery);
            ForgeRequire("achievementsgaps.independent-powered-input", new { quality = 600, performance = 1200, efficiency = -600, power = 112, capacity = 250, bonus = 700 },
                GapProjectorEvidence(), MachineryHelper.GetCurrentQualityBonus(_gapProjector) == 600 &&
                MachineryHelper.GetCurrentPerformanceBonus(_gapProjector) == 1200 &&
                _gapProjector.GetTagReadonly("TOTAL_PERCENTAGE_POWER_BONUS_INT").GetInt() == -600 &&
                MachineryHelper.GetMachinePowerUsage(_gapProjector) == 16 - 16 * (-600) / 100 &&
                Energy(battery) == 250 && battery.GetTagReadonly("power_source_item_max_energy").GetInt() == 250 &&
                Store.baseStoreAttractiveness == 400 && Store.projectorAttractivenessBonus == 0);
            using (var night = new NightObservation(this, Array.Empty<RecipeFixture>(), day))
            {
                R.environment["nativeAchievementGapNightObservation"] = night.Evidence;
                foreach (object? wait in GraylineOpen()) yield return wait;
                foreach (object? wait in GraylineServeUntil(Store.CanEndDay, "", "achievementsgaps.D1.actual-required-clients")) yield return wait;
                // This existing native EndNight helper only handles genuine
                // shutter/bedroll/checkbox/End Day flow and read-only observers.
                // It does not require or prepare a FENCE/reputation state.
                foreach (object? wait in ReputationEndNight(day, night)) yield return wait;
            }
            int trash = Store.garbageLevel;
            int penalty = trash <= 2 ? 0 : (int)(400 * (trash * 5.0 / 100.0));
            int independentAttractiveness = 400 - penalty + (100 + 6 * 100 / 2 + 6 * 200 / 4);
            ForgeRequire("achievementsgaps.real-powered-night-independent-formula", new
            { day = day + 1, bonus = 700, powerConsumed = 112, energy = energy - 112, baseAttractiveness = 400, penalty, independentAttractiveness, threshold = 1000 }, new
            {
                day = StoreStation.GetDayCounter(), Store.baseStoreAttractiveness, Store.projectorAttractivenessBonus, Store.garbageLevel,
                penalty = Store.GetTrashAttPenalty(), actual = Store.GetCurrentStoreAttractiveness(), projector = GapProjectorEvidence()
            }, StoreStation.GetDayCounter() == day + 1 && _gapProjector.IsTag("IS_PROJECTOR_ACTIVE") && Energy(battery) == energy - 112 &&
                Store.baseStoreAttractiveness == 400 && Store.projectorAttractivenessBonus == 700 && Store.GetTrashAttPenalty() == penalty &&
                Store.GetCurrentStoreAttractiveness() == independentAttractiveness && independentAttractiveness >= 1000 &&
                MachineryHelper.GetCurrentQualityBonus(_gapProjector) == 600 && MachineryHelper.GetCurrentPerformanceBonus(_gapProjector) == 1200);
            GapAudit("achievementsgaps.after-complete-native-powered-night");
            GapClearMainIntoWealthReward();
            foreach (object? wait in ForgeClaim(GapGoal("store_attractiveness"))) yield return wait;
            _gapAttractionReward = GapClaimedReward("store_attractiveness", "storage_bay_large");
            GapCommit(_gapAttractionReward, "original whole store_attractiveness reward actually delivered by the real Forge claim");
            GapAudit("achievementsgaps.both-original-rewards-fixed");
            ForgeRequire("achievementsgaps.exact-two-required-claims", GapGoalIds, _forgeGoals.Values.Select(r => new { id = (string?)r["id"], claim = (string?)r["claim"], failure = ForgeGoalHasFailures(r) }).ToArray(),
                _forgeGoals.Count == 2 && GapGoalIds.All(id => (string?)_forgeGoals[id]["claim"] == "passed" && !ForgeGoalHasFailures(_forgeGoals[id])));
            foreach (object? wait in GapSaveReadback()) yield return wait;
            R.Skip("achievementsgaps.economic-acquisition-and-steam-account", "150000 starting credits, machine, rare cores, kits and battery are explicitly prepared supported/factory inputs; no earned-wealth or complete hardware acquisition chain is claimed. Native Forge run goals, not Steam account unlocks, are verified. Steam is disabled.", false);
        }
        finally { Reflect.Call(ForgeWorkshopUi, "CloseWindow"); WildNetworkUI.Close(); }
    }

    internal IEnumerable<object?> NativeAchievementGapsReload(JObject baseline)
    {
        GapInitialize();
        R.phase = "achievementsgaps.independent-original-slot-readback";
        ForgeRequire("achievementsgaps.reload-original-slot-and-disabled-steam", "Actual independent original-slot native load, Steam disabled", new { O.Reload, O.UseCopy, steam = SteamManager.Initialized },
            O.Reload && !O.UseCopy && !SteamManager.Initialized);
        var expected = baseline["persistence"]?["nativeAchievementGaps"] as JObject ?? throw new InvalidOperationException("Missing native achievement-gap baseline");
        foreach (var row in (JArray)expected["commitments"]!)
        {
            int uid = (int)row["uid"]!;
            if (_gapCommitments.ContainsKey(uid)) throw new InvalidOperationException("Repeated UID in fixed achievement-gap baseline");
            _gapCommitments.Add(uid, ((string)row["id"]!, (int)row["count"]!, (string)row["purpose"]!));
            string reason = (string?)row["consumedByVerifiedNativeAction"] ?? "";
            if (reason.Length > 0) _gapConsumed.Add(uid, reason);
        }
        _gapWealthReward = GapFind((int)expected["wealthRewardUid"]!, "machine_bay_ext");
        _gapProjector = GapFind((int)expected["projectorUid"]!, "mirage_projector");
        _gapAttractionReward = GapFind((int)expected["attractionRewardUid"]!, "storage_bay_large");
        _forgeProjector = _gapProjector;
        GapAudit("achievementsgaps.reload-fixed-denominator");
        var actual = GapSnapshot();
        ForgeRequire("achievementsgaps.reload-exact-goals-upgrade-UID-power-attraction", expected["snapshot"], actual,
            JToken.DeepEquals(GapReadbackProjection(expected["snapshot"]!), GapReadbackProjection(actual)), "Normal native LoadGame/ES3; only wholly default disabled tag omission follows SaveItemNode. No fixture, cycle, upgrade or claim replay.");
        foreach (string id in GapGoalIds)
        {
            var previous = ((JArray)expected["matrix"]!).Single(row => (string?)row["id"] == id);
            ForgeRequire("achievementsgaps.reload-source-goal-complete." + id, "Source qualification/claim/save genuinely passed", previous,
                (string?)previous["qualification"] == "passed" && (string?)previous["claim"] == "passed" && (string?)previous["save"] == "passed" && !ForgeGoalHasFailures((JObject)previous));
            foreach (var property in ((JObject)previous).Properties()) _forgeGoals[id][property.Name] = property.Value.DeepClone();
            var pageBefore = ForgeLedger(); string path = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId)), hash = Reflect.HashFile(path);
            bool opened = (bool)Reflect.Call(ForgeWorkshopUi, "OpenChain", ForgeAchievementChain)!;
            yield return null;
            Button node = ForgeNode(GapGoal(id));
            ForgeRequire("achievementsgaps.reload-active-node." + id, "Actual active native achievement node", node.IsInteractable(),
                node.gameObject.activeInHierarchy && node.IsInteractable());
            node.onClick.Invoke(); yield return null;
            var claimed = ForgeClaimButton(true);
            ForgeRequire("achievementsgaps.reload-disabled-claimed-page." + id, "Visible real page, selected same goal, same claimed record and disabled claim", new
            { opened, visible = ForgeWindow()?.IsVisible, selected = Reflect.Get(ForgeWorkshopUi, "_selected"), state = ForgeState(), interactable = claimed.IsInteractable() },
                opened && ForgeWindow()?.IsVisible == true && Reflect.Str(ForgeWorkshopUi, "_selected") == id && claimed.gameObject.activeInHierarchy &&
                !claimed.IsInteractable() && ((JArray)ForgeState()["Claimed"]!).Values<string>().Contains(id));
            var before = ForgeLedger();
            ForgeRequire("achievementsgaps.reload-claimed-page-preserves-owned-rewards." + id, pageBefore, before,
                (int?)pageBefore["cash"] == (int?)before["cash"] && JToken.DeepEquals(pageBefore["state"], before["state"]) && JToken.DeepEquals(pageBefore["items"], before["items"]),
                "Native icon prototypes may allocate temporary UIDs while the page renders; every owned item, cash and achievement record remains exact. The actual duplicate Claim below compares the complete same-frame ledger.");
            Reflect.Call(ForgeWorkshop, "Claim", id);
            ForgeRequire("achievementsgaps.reload-actual-duplicate-guard." + id, before, ForgeLedger(), JToken.DeepEquals(before, ForgeLedger()));
            ForgeRequire("achievementsgaps.reload-no-save." + id, hash, Reflect.HashFile(path), Reflect.HashFile(path) == hash);
            GapAudit("achievementsgaps.reload-no-reward-substitution." + id);
            _forgeGoals[id]["reload"] = "passed"; _forgeGoals[id]["status"] = "passed-save-and-reload";
            foreach (object? wait in CaptureEvidenceFrame("native-achievement-gap-reloaded-" + id)) yield return wait;
            Reflect.Call(ForgeWorkshopUi, "CloseWindow");
        }
        R.persistence["nativeAchievementGapsReload"] = new JObject
        { ["snapshot"] = GapSnapshot(), ["commitments"] = GapCommitmentSnapshot(), ["matrix"] = new JArray(GapGoalIds.Select(id => _forgeGoals[id].DeepClone())) };
    }

    private void GapInitialize()
    {
        GapCheckTagProjectionLong();
        _forgeGoals.Clear();
        foreach (string id in GapGoalIds)
        {
            ForgeGoal goal = GapGoal(id);
            _forgeGoals.Add(id, new JObject
            {
                ["id"] = id, ["conditions"] = new JArray(goal.Conditions),
                ["expectedRewards"] = new JArray(goal.Rewards.Select(r => new JObject { ["itemId"] = r.Id, ["count"] = r.Count })),
                ["qualification"] = "not-tested", ["page"] = "not-tested", ["claim"] = "not-tested", ["save"] = "not-tested", ["reload"] = "not-tested",
                ["economicAcquisition"] = "not-tested", ["status"] = "not-tested", ["hadFailure"] = false, ["failedChecks"] = new JArray()
            });
        }
        R.environment["nativeAchievementGapGoalIds"] = new JArray(GapGoalIds);
        R.environment["nativeAchievementGapMatrix"] = new JArray(GapGoalIds.Select(id => _forgeGoals[id]));
        R.environment["forgeAchievementFixtures"] = _forgeFixtures;
        R.environment["forgeAchievementActions"] = _forgeActions;
        R.environment["forgeAchievementClaims"] = _forgeClaims;
        R.environment["nativeAchievementGapActions"] = _gapActions;
    }
    private static ForgeGoal GapGoal(string id) => ForgeGoalInventory.Single(goal => goal.Id == id);
    private GameItem GapClaimedReward(string goalId, string itemId)
    {
        ForgeRequire("achievementsgaps.required-actual-claim." + goalId, "Required real Forge claim and physical whole-item rehearsal passed", _forgeGoals[goalId],
            (string?)_forgeGoals[goalId]["claim"] == "passed" && !ForgeGoalHasFailures(_forgeGoals[goalId]) &&
            (string?)_forgeGoals[goalId]["capacityProof"]?["status"] == "complete-native-rehearsal-passed");
        var receipt = _forgeClaims.Single(row => (string?)row["goal"] == goalId);
        var rewards = ((JArray)receipt["items"]!).ToArray();
        ForgeRequire("achievementsgaps.exact-reward-receipt." + goalId, new { itemId, count = 1, positiveOriginalUid = true }, rewards,
            rewards.Length == 1 && (string?)rewards[0]["id"] == itemId && (int?)rewards[0]["count"] == 1 && (int?)rewards[0]["uid"] > 0);
        return GapFind((int)rewards[0]["uid"]!, itemId);
    }
    private void GapClearMainIntoWealthReward()
    {
        var inventory = (GameInventory)_gapWealthReward.contentWindow.childElement;
        foreach (var item in EmporiumEntry.Instance.invElement.childItems.Where(i => !ReferenceEquals(i, _gapWealthReward) && GeneralHelper.IsItemOwned(i)).ToArray())
        {
            var slot = inventory.TryFindOneValidInventorySlot(item);
            if (slot?.IsValid() != true)
            {
                GapAction("native-main-clearing-rejected-item-remains", new { item.uniqueId, item.identifier, destination = inventory.identifier });
                continue;
            }
            int uid = item.uniqueId, count = item.unitCount;
            Place(inventory, item, "achievementsgaps-main-clearing-" + uid);
            ForgeRequire("achievementsgaps.original-main-clearing." + uid, new { uid, count, owned = true }, GapItem(item),
                item.uniqueId == uid && item.unitCount == count && GeneralHelper.IsItemOwned(item) && ReferenceEquals(item.parentInventory, inventory));
            GapCommit(item, "original legally moved main-grid item retained in actual wealthy_store reward interior");
        }
    }

    private IEnumerable<object?> GapBuyRenovation()
    {
        R.phase = "achievementsgaps.native-paid-renovation";
        var upgrade = Store.networkUpgrade["RENOVATION1"];
        ForgeRequire("achievementsgaps.independent-renovation-contract", new { id = "RENOVATION1", cost = 500, prerequisiteCount = 0, baseAttractiveness = 250 }, new
        { upgrade.id, upgrade.cost, actualCost = upgrade.GetCost(), prerequisite = upgrade.prerequisite, ready = upgrade.IsReady(), unlocked = upgrade.IsUnlocked(), Store.baseStoreAttractiveness },
            upgrade.id == "RENOVATION1" && upgrade.cost == 500 && upgrade.GetCost() == 500 && upgrade.prerequisite.Count == 0 &&
            upgrade.IsReady() && !upgrade.IsUnlocked() && Store.baseStoreAttractiveness == 250);
        WildNetworkUI.Open();
        yield return null;
        var window = CustomUIManager.Instance.GetWindow(WildNetworkUI.WINDOW_ID);
        ForgeRequire("achievementsgaps.real-wild-network-window", "Actual visible native Wild Network UI", WildNetworkUI.IsOpen,
            WildNetworkUI.IsOpen && window?.IsVisible == true && window.IsAlive);
        WildNetworkSlot? selected = null;
        var tabButtons = Reflect.Each(Reflect.Get(typeof(WildNetworkUI), "tabViews")).Select(v => (Button)Reflect.Get(v, "button")!).ToArray();
        for (int index = -1; index < tabButtons.Length && selected == null; index++)
        {
            if (index >= 0)
            {
                var tab = tabButtons[index];
                ForgeRequire("achievementsgaps.active-native-network-tab." + index, "Active enabled native tab button", tab.IsInteractable(),
                    tab.gameObject.activeInHierarchy && tab.IsInteractable());
                tab.onClick.Invoke(); yield return null;
            }
            var matches = window!.root.GetComponentsInChildren<WildNetworkSlot>(false).Where(s => s.id == "RENOVATION1" && s.gameObject.activeInHierarchy).ToArray();
            ForgeRequire("achievementsgaps.unique-visible-renovation-slot." + index, "At most one actual visible matching slot", matches.Length, matches.Length <= 1);
            selected = matches.SingleOrDefault();
        }
        ForgeRequire("achievementsgaps.native-renovation-slot-reached", "Visible original RENOVATION1 slot through actual tab buttons", selected?.id,
            selected != null && selected.gameObject.activeInHierarchy && selected.onClick != null && EventSystem.current != null);
        GapAction("native-WildNetworkSlot-left-click", new { id = selected!.id });
        selected!.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
        yield return null;
        var frame = (WildNetworkFrame)Reflect.Get(typeof(WildNetworkUI), "frame")!;
        var buy = frame.useButton;
        ForgeRequire("achievementsgaps.actual-renovation-payment-button", "Selected original upgrade and actual active enabled purchase button", new { selected = Reflect.Get(typeof(WildNetworkUI), "selectedId"), buy.interactable },
            Reflect.Str(typeof(WildNetworkUI), "selectedId") == "RENOVATION1" && WildNetworkUI.IsOpen && buy.gameObject.activeInHierarchy && buy.IsInteractable() &&
            upgrade.IsReady() && !upgrade.IsUnlocked() && Store.playerCash + Store.wildFavor >= 500 && Store.wildFavor >= 0);
        int cash = Store.playerCash, favor = Store.wildFavor, expectedCash = cash - Math.Max(0, 500 - favor), expectedFavor = Math.Max(0, favor - 500);
        GapAction("native-renovation-purchase-button", new { cash, favor, independentCost = 500, expectedCash, expectedFavor });
        buy.onClick.Invoke(); yield return null;
        ForgeRequire("achievementsgaps.real-renovation-payment-and-effect", new { cash = expectedCash, favor = expectedFavor, baseAttractiveness = 400, unlocked = true }, new
        { cash = Store.playerCash, favor = Store.wildFavor, Store.baseStoreAttractiveness, unlocked = upgrade.IsUnlocked() },
            Store.playerCash == expectedCash && Store.wildFavor == expectedFavor && Store.baseStoreAttractiveness == 250 + 150 && upgrade.IsUnlocked());
        WildNetworkUI.Close();
    }

    private IEnumerable<object?> GapPrepareProjector()
    {
        R.phase = "achievementsgaps.legal-six-core-projector";
        var storage = (GameInventory)_gapWealthReward.contentWindow.childElement;
        _gapProjector = ForgeFixture("mirage_projector", "one actual native projector input; no synthetic quality/performance or completed-cycle tags");
        Place(storage, _gapProjector, "achievementsgaps-original-projector-in-real-reward");
        _forgeProjector = _gapProjector;
        GapCommit(_gapProjector, "original legally admitted projector proof");
        for (int index = 0; index < 2; index++)
        {
            var kit = ForgeFixture("module_bay_expansion_kit", "one legitimate registered expansion-kit input; actual target action consumes this original UID");
            Place(storage, kit, "achievementsgaps-original-expansion-kit-" + index);
            GapCommit(kit, "original legal expansion-kit material before actual target consumption");
            int uid = kit.uniqueId, stage = _gapProjector.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt(), cash = Store.playerCash;
            ForgeRequire("achievementsgaps.real-expansion-target-guards." + index, "One owned admitted original kit and actual native target guards", GapItem(kit),
                ForgeOwnedUnits(uid) == 1 && kit.mayItemTargetItemFunc?.Invoke(kit, _gapProjector) == true &&
                kit.canItemTargetItemFunc?.Invoke(kit, _gapProjector) == true && kit.onItemTargetItemFunc != null);
            GapAction("native-module-bay-kit-target", new { uid, projector = _gapProjector.uniqueId, stage });
            kit.onItemTargetItemFunc!.Invoke(kit, _gapProjector);
            ForgeRequire("achievementsgaps.real-expansion-consumption." + index, new { stage = stage + 1, originalKitOwned = 0, cash }, new
            { stage = _gapProjector.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt(), originalKitOwned = ForgeOwnedUnits(uid), cash = Store.playerCash },
                _gapProjector.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt() == stage + 1 && ForgeOwnedUnits(uid) == 0 && Store.playerCash == cash);
            GapRetire(uid, "verified original module_bay_expansion_kit consumed by native target, stage" + stage + "->" + (stage + 1));
        }
        var modules = (GameGridInventory)MachineHelper.GetBodyStorage(_gapProjector, 1);
        ForgeRequire("achievementsgaps.real-expanded-module-grid", "Actual stage2 native7x5 grid, no fabricated seventh core", new
        { stage = _gapProjector.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt(), modules.inventoryShape.globalWidth, modules.inventoryShape.globalHeight },
            _gapProjector.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt() == 2 && modules.inventoryShape.globalWidth == 7 && modules.inventoryShape.globalHeight == 5);
        for (int index = 0; index < 6; index++)
        {
            var core = ForgeFixture("system_uncapped_neural_core", "rare native hardware fixture, performance200/efficiency-100/quality100; economic acquisition and contraband legality are not claimed");
            var slot = _gapProjector.TryFindOneValidInventorySlot(core);
            ForgeRequire("achievementsgaps.actual-core-admission." + index, "Actual original native module is accepted by the real expanded module grid", new { core = GapItem(core), destination = slot?.inventory?.identifier, valid = slot?.IsValid() },
                slot?.IsValid() == true && ReferenceEquals(slot.inventory, modules) &&
                core.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt() == 200 &&
                core.GetTagReadonly("BONUS_PERCENTAGE_EFFICIENCY_INT").GetInt() == -100 && core.GetTagReadonly("BONUS_PERCENTAGE_QUALITY_INT").GetInt() == 100);
            Place(modules, core, "achievementsgaps-real-six-core-" + index, _gapProjector);
            GapCommit(core, "original legally admitted native uncapped module proof #" + index);
            ForgeRequire("achievementsgaps.independent-module-sums." + index, new { performance = (index + 1) * 200, efficiency = (index + 1) * -100, quality = (index + 1) * 100 }, GapProjectorEvidence(),
                MachineryHelper.GetCurrentPerformanceBonus(_gapProjector) == (index + 1) * 200 &&
                _gapProjector.GetTagReadonly("TOTAL_PERCENTAGE_POWER_BONUS_INT").GetInt() == (index + 1) * -100 &&
                MachineryHelper.GetCurrentQualityBonus(_gapProjector) == (index + 1) * 100);
        }
        ForgeRequire("achievementsgaps.independent-power-cost", 16 - 16 * (-600) / 100, MachineryHelper.GetMachinePowerUsage(_gapProjector),
            MachineryHelper.GetMachinePowerUsage(_gapProjector) == 112);
        var battery = ForgeLegalBattery(_gapProjector);
        Place(MachineHelper.GetBatterySlot(_gapProjector), battery, "achievementsgaps-actual-legal-250kJ-battery", _gapProjector);
        GapCommit(battery, "original legally admitted250kJ native battery proof, expected112kJ real-night debit");
        _forgeProofUids.Add(_gapProjector.uniqueId);
        yield return null;
    }

    private IEnumerable<object?> GapSaveReadback()
    {
        GapAudit("achievementsgaps.pre-save-fixed-UIDs");
        Store.SaveGame();
        string path = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId));
        var settings = new ES3Settings(path, ES3.Location.File);
        var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", settings)!;
        bool livePresent = Store.ModData().TryGetValue(ForgeAchievementKey, out string liveJson), diskPresent = saved.ModData().TryGetValue(ForgeAchievementKey, out string diskJson);
        ForgeRequire("achievementsgaps.actual-es3-state-identity-cash", "Same actual achievement JSON, run/slot/cash and native next day", new
        { livePresent, diskPresent, saved.runID, saved.saveSlotId, saved.playerCash, day = ES3.Load<StoreStation>("storeStation", settings).dayCounter },
            livePresent && diskPresent && liveJson == diskJson && saved.runID == Store.runID && saved.saveSlotId == Store.saveSlotId &&
            saved.playerCash == Store.playerCash && ES3.Load<StoreStation>("storeStation", settings).dayCounter == StoreStation.GetDayCounter());
        var nodes = new[] { saved.mainInvJSON, saved.backInvJSON }.SelectMany(json => JsonUtility.FromJson<SaveState>(json).saveItems).ToArray();
        var diskItems = new JArray();
        foreach (var pair in _gapCommitments.OrderBy(p => p.Key))
        {
            var matches = nodes.Where(node => node.uniqueId == pair.Key).ToArray();
            bool consumed = _gapConsumed.ContainsKey(pair.Key);
            ForgeRequire("achievementsgaps.actual-es3-fixed-UID." + pair.Key, new { uid = pair.Key, expected = consumed ? 0 : 1, pair.Value.Id, pair.Value.Count }, new
            { count = matches.Length, matches = matches.Select(n => new { n.identifier, n.uniqueId, n.unitCount }).ToArray(), consumed },
                consumed ? matches.Length == 0 : matches.Length == 1 && matches[0].identifier == pair.Value.Id && matches[0].unitCount == pair.Value.Count);
            if (!consumed) diskItems.Add(GapStoredItem(matches[0].identifier, matches[0].uniqueId, matches[0].unitCount, matches[0].unitValue, matches[0].itemState));
        }
        var liveItems = GapLiveProofs();
        var decodedItems = GapProveSerializedTemperatures(liveItems, diskItems, saved.mainInvJSON, saved.backInvJSON);
        ForgeRequire("achievementsgaps.actual-es3-all-fixed-proof-tags", liveItems, diskItems, JToken.DeepEquals(GapReadbackProjection(decodedItems), GapReadbackProjection(diskItems)),
            "Every original reward/proof UID, count, module/power/projector tag is read from actual native ES3 bytes. No Where(stillOwned) filter may shrink the saved denominator.");
        foreach (string id in GapGoalIds) { _forgeGoals[id]["save"] = "passed"; _forgeGoals[id]["status"] = "passed-save-only"; }
        var snapshot = GapSnapshot();
        snapshot["proofItems"] = diskItems.DeepClone();
        R.persistence["nativeAchievementGaps"] = new JObject
        {
            ["snapshot"] = snapshot, ["commitments"] = GapCommitmentSnapshot(), ["matrix"] = new JArray(GapGoalIds.Select(id => _forgeGoals[id].DeepClone())),
            ["claims"] = _forgeClaims.DeepClone(), ["wealthRewardUid"] = _gapWealthReward.uniqueId, ["projectorUid"] = _gapProjector.uniqueId,
            ["attractionRewardUid"] = _gapAttractionReward.uniqueId, ["nativeEs3Path"] = path, ["nativeEs3Sha256AtReadback"] = Reflect.HashFile(path)
        };
        yield return null;
    }

    private void GapAction(string name, object? value = null) => _gapActions.Add(new JObject
    { ["name"] = name, ["day"] = StoreStation.GetDayCounter(), ["frame"] = Time.frameCount, ["cash"] = Store.playerCash, ["detail"] = value == null ? null : (JToken?)EvidenceJson.Snapshot(value) });
    private void GapCommit(GameItem root, string purpose)
    {
        foreach (var item in new[] { root }.Concat(root.FindAllChildItems()).Distinct())
        {
            ForgeRequire("achievementsgaps.commit-real-admitted-UID." + item.uniqueId, "Admitted owned original UID, positive native count", GapItem(item),
                item.uniqueId > 0 && item.unitCount > 0 && item.parentInventory != null && GeneralHelper.IsItemOwned(item));
            if (_gapCommitments.TryGetValue(item.uniqueId, out var before))
                ForgeRequire("achievementsgaps.no-commitment-redefinition." + item.uniqueId, new { before.Id, before.Count }, GapItem(item),
                    !_gapConsumed.ContainsKey(item.uniqueId) && before.Id == item.identifier && before.Count == item.unitCount);
            else _gapCommitments.Add(item.uniqueId, (item.identifier, item.unitCount, purpose));
        }
    }
    private void GapRetire(int uid, string reason)
    {
        ForgeRequire("achievementsgaps.retire-only-proved-native-consumption." + uid, "Original previously committed UID now absent after independent native consumption assertion", new
        { uid, reason, committed = _gapCommitments.ContainsKey(uid), previouslyRetired = _gapConsumed.ContainsKey(uid), ownedUnits = ForgeOwnedUnits(uid) },
            _gapCommitments.ContainsKey(uid) && !_gapConsumed.ContainsKey(uid) && ForgeOwnedUnits(uid) == 0);
        _gapConsumed.Add(uid, reason);
    }
    private JArray GapCommitmentSnapshot() => new(_gapCommitments.OrderBy(p => p.Key).Select(p => new JObject
    {
        ["uid"] = p.Key, ["id"] = p.Value.Id, ["count"] = p.Value.Count, ["purpose"] = p.Value.Purpose,
        ["consumedByVerifiedNativeAction"] = _gapConsumed.TryGetValue(p.Key, out string reason) ? reason : ""
    }));
    private void GapAudit(string name)
    {
        var owned = Store.FindAllItem().Where(GeneralHelper.IsItemOwned).ToArray();
        var rows = new JArray(); bool valid = _gapCommitments.Count > 0;
        foreach (var pair in _gapCommitments.OrderBy(p => p.Key))
        {
            var matches = owned.Where(i => i.uniqueId == pair.Key).ToArray(); bool consumed = _gapConsumed.ContainsKey(pair.Key);
            bool itemValid = consumed ? matches.Length == 0 : matches.Length == 1 && matches[0].identifier == pair.Value.Id && matches[0].unitCount == pair.Value.Count;
            valid &= itemValid;
            rows.Add(new JObject { ["uid"] = pair.Key, ["valid"] = itemValid, ["consumed"] = consumed, ["ownedInstances"] = new JArray(matches.Select(GapItem)) });
        }
        ForgeRequire(name, GapCommitmentSnapshot(), rows, valid, "Fixed original UID/ID/count denominator; consumed materials retain explicit verified-native-action retirement receipts. Missing proof/reward or same-energy replacement cannot silently shrink the expected graph.");
    }
    private GameItem GapFind(int uid, string id)
    {
        var matches = Store.FindAllItem().Where(i => i.uniqueId == uid).ToArray();
        ForgeRequire("achievementsgaps.original-owned-identity." + uid, new { uid, id, count = 1 }, matches.Select(GapItem).ToArray(),
            matches.Length == 1 && matches[0].identifier == id && matches[0].unitCount == 1 && GeneralHelper.IsItemOwned(matches[0]));
        return matches[0];
    }
    private static JObject GapItem(GameItem item) => GapStoredItem(item.identifier, item.uniqueId, item.unitCount, item.unitValue, item.state);
    // Readback only. Runtime transactions retain every raw tag and bit field.
    private static JToken GapReadbackProjection(JToken source)
    {
        var copy = source.DeepClone();
        void Visit(JToken node)
        {
            if (node is JObject item && item["tags"] is JObject tags)
                foreach (var property in tags.Properties().ToArray())
                {
                    if (property.Value is not JObject tag || tag.Count != 9) continue;
                    bool numericZero = new[] { "integer", "longInteger", "number", "doubleNumber" }.All(key => tag[key]?.Type is JTokenType.Integer or JTokenType.Float && (double)tag[key]! == 0.0);
                    if (tag["enabled"]?.Type == JTokenType.Boolean && (bool)tag["enabled"]! == false &&
                        tag["boolean"]?.Type == JTokenType.Boolean && (bool)tag["boolean"]! == false && numericZero &&
                        (string?)tag["floatBits"] is "00000000" or "80000000" &&
                        (string?)tag["doubleBits"] is "0000000000000000" or "8000000000000000" &&
                        (tag["text"]?.Type == JTokenType.Null || tag["text"]?.Type == JTokenType.String && (string?)tag["text"] == "")) property.Remove();
                }
            foreach (var child in node.Children().ToArray()) Visit(child);
        }
        Visit(copy); return copy;
    }
    private JArray GapProveSerializedTemperatures(JArray live, JArray disk, params string[] inventories)
    {
        var projected = (JArray)live.DeepClone();
        var rawNodes = new JArray(inventories.SelectMany(json => (JArray)JObject.Parse(json)["saveItems"]!));
        var nativeNodes = inventories.SelectMany(json => JsonUtility.FromJson<SaveState>(json).saveItems).ToArray();
        foreach (JObject item in projected)
        {
            if (item["tags"]?["CONTAINER_TEMP_TAG"] is not JObject tag || (bool?)tag["enabled"] != true) continue;
            int uid = (int)item["uid"]!; double original = (double)tag["doubleNumber"]!;
            var native = nativeNodes.Where(node => node.uniqueId == uid).ToArray();
            double decoded = native.Length == 1 && native[0].itemState.dict.TryGetValue("CONTAINER_TEMP_TAG", out var decodedTag) ? decodedTag.valueDouble : double.NaN;
            bool scalarExact = FlReloadOriginalTemperature(rawNodes, uid, original, decoded, out JToken? rawTag);
            var observed = disk.OfType<JObject>().Single(row => (int?)row["uid"] == uid)["tags"]?["CONTAINER_TEMP_TAG"];
            string bits = BitConverter.DoubleToInt64Bits(decoded).ToString("X16");
            ForgeRequire("native-readback.original-disk-scalar-and-unity-decoder." + uid, "Original disk temperature bits exact, then exact actual Unity-decoded scalar/bits", new
            { uid, original, decoded, rawTag, observed }, scalarExact && rawTag != null &&
                BitConverter.DoubleToInt64Bits((double)rawTag["valueDouble"]!) == BitConverter.DoubleToInt64Bits(original) &&
                (double?)observed?["doubleNumber"] == decoded && (string?)observed?["doubleBits"] == bits);
            tag["doubleNumber"] = decoded; tag["doubleBits"] = bits;
        }
        return projected;
    }
    private static JObject GapStoredItem(string id, int uid, int count, long value, TagSystem state)
    {
        var tags = new JObject();
        foreach (string key in GapTags.Concat(state.dict.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            state.dict.TryGetValue(key, out var tag);
            float single = tag?.valueFloat ?? 0; double wide = tag?.valueDouble ?? 0;
            tags[key] = JObject.FromObject(new { enabled = tag?.valueEnabled ?? false, boolean = tag?.GetBool() ?? false, integer = tag?.valueInt ?? 0, longInteger = tag?.valueLong ?? 0L,
                number = (double)single, floatBits = BitConverter.SingleToInt32Bits(single).ToString("X8"),
                doubleNumber = wide, doubleBits = BitConverter.DoubleToInt64Bits(wide).ToString("X16"), text = tag?.GetString() ?? "" });
        }
        return new JObject { ["id"] = id, ["uid"] = uid, ["count"] = count, ["value"] = value, ["tags"] = tags };
    }
    private void GapCheckTagProjectionLong()
    {
        const string key = "native_probe_detached_long_projection_control";
        const long lower = 9007199254740992L, upper = 9007199254740993L;
        var original = new TagSystem(); var changed = new TagSystem();
        original.dict.Add(key, new TagState(key, key) { valueLong = lower });
        changed.dict.Add(key, new TagState(key, key) { valueLong = upper });
        JObject before = GapStoredItem("detached-native-tag-projection", 1, 1, 0, original);
        JObject after = GapStoredItem("detached-native-tag-projection", 1, 1, 0, changed);
        JObject encoded = JObject.Parse(before.ToString());
        ForgeRequire("achievementsgaps.raw-nonzero-long-change-rejected", "Only native TagState.valueLong differs by1 above double exact-integer range; exact evidence comparison rejects it", new
        { lower, upper, original = before, changed = after },
            (long?)before["tags"]?[key]?["longInteger"] == lower && (long?)after["tags"]?[key]?["longInteger"] == upper &&
            !JToken.DeepEquals(before, after) && JToken.DeepEquals(before, encoded) && !JToken.DeepEquals(GapReadbackProjection(before), GapReadbackProjection(after)),
            "Detached real TagSystem/TagState evidence-only fixture. No live item, gameplay tag, UID allocation, cash, achievement flag or save is changed. Disabled nonzero longs must remain visible, matching native SaveItemNode.OnBeforeSerialize.");
        var zero = JObject.FromObject(new { enabled = false, boolean = false, integer = 0, longInteger = 0L, number = 0.0, floatBits = "00000000", doubleNumber = 0.0, doubleBits = "0000000000000000", text = "" });
        var absent = new JObject { ["uid"] = 1, ["tags"] = new JObject() };
        var defaults = new JObject { ["uid"] = 1, ["tags"] = new JObject { ["sample"] = zero.DeepClone() } };
        bool omitted = JToken.DeepEquals(GapReadbackProjection(absent), GapReadbackProjection(defaults));
        var preserved = new JObject();
        foreach (string field in zero.Properties().Select(p => p.Name))
        {
            var nondefault = (JObject)zero.DeepClone();
            nondefault[field] = field is "enabled" or "boolean" ? new JValue(true) : field is "floatBits" or "doubleBits" or "text" ? new JValue("nondefault") : new JValue(1);
            var sample = new JObject { ["uid"] = 1, ["tags"] = new JObject { ["sample"] = nondefault } };
            preserved[field] = !JToken.DeepEquals(GapReadbackProjection(absent), GapReadbackProjection(sample));
        }
        var future = (JObject)defaults.DeepClone(); future["tags"]!["sample"]!["futurePayload"] = 0;
        bool futurePreserved = !JToken.DeepEquals(GapReadbackProjection(absent), GapReadbackProjection(future));
        ForgeRequire("native-readback.default-disabled-nine-field-negative-controls", "Only exact default-disabled tags may equal absence; every payload and unknown field survives", new { omitted, preserved, futurePreserved },
            omitted && preserved.Properties().All(p => (bool)p.Value) && futurePreserved);
    }
    private JArray GapLiveProofs() => new(_gapCommitments.Where(p => !_gapConsumed.ContainsKey(p.Key)).OrderBy(p => p.Key)
        .Select(p => GapItem(Store.FindAllItem().Single(i => i.uniqueId == p.Key && GeneralHelper.IsItemOwned(i)))));
    private JObject GapProjectorEvidence() => new()
    {
        ["uid"] = _gapProjector.uniqueId, ["quality"] = MachineryHelper.GetCurrentQualityBonus(_gapProjector),
        ["performance"] = MachineryHelper.GetCurrentPerformanceBonus(_gapProjector),
        ["efficiency"] = _gapProjector.GetTagReadonly("TOTAL_PERCENTAGE_POWER_BONUS_INT").GetInt(),
        ["power"] = MachineryHelper.GetMachinePowerUsage(_gapProjector), ["active"] = _gapProjector.IsTag("IS_PROJECTOR_ACTIVE"),
        ["battery"] = MachineHelper.GetBatterySlot(_gapProjector).childItem == null ? null : GapItem(MachineHelper.GetBatterySlot(_gapProjector).childItem)
    };
    private JObject GapSnapshot() => new()
    {
        ["runId"] = Store.runID, ["slot"] = Store.saveSlotId, ["cash"] = Store.playerCash, ["favor"] = Store.wildFavor,
        ["day"] = StoreStation.GetDayCounter(), ["achievementState"] = ForgeState(), ["baseAttractiveness"] = Store.baseStoreAttractiveness,
        ["projectorAttractiveness"] = Store.projectorAttractivenessBonus, ["garbageLevel"] = Store.garbageLevel,
        ["actualAttractiveness"] = Store.GetCurrentStoreAttractiveness(), ["renovation1Unlocked"] = Store.networkUpgrade["RENOVATION1"].IsUnlocked(),
        ["projector"] = GapProjectorEvidence(), ["proofItems"] = GapLiveProofs(), ["commitments"] = GapCommitmentSnapshot()
    };
}

