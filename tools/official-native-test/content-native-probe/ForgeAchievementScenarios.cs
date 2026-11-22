using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const string ForgeAchievementKey = "nicokobo.forge.native_achievements";
    private const string ForgeAchievementChain = "nicokobo.forge.native_workshop";
    private Type ForgeWorkshop => Reflect.Type("Nicokobo.ContentInterop.NativeWorkshop");
    private Type ForgeWorkshopUi => Reflect.Type("Nicokobo.ContentInterop.OfficialWorkshopApi");
    private readonly JArray _forgeFixtures = new(), _forgeActions = new(), _forgeNights = new(), _forgeClaims = new();
    private readonly Dictionary<string, JObject> _forgeGoals = new(StringComparer.Ordinal);
    private readonly List<GameItem> _forgeBags = new();
    private readonly List<GameItem> _forgePoweredMachines = new();
    private readonly HashSet<int> _forgeReceipts = new(), _forgeProofUids = new();
    private GameItem? _forgeWine, _forgeRat, _forgeCage, _forgeProjector;
    private ForgeSteamObservation? _forgeSteam;

    private sealed class ForgeGoal(string id, string[] conditions, float x, float y, params (string Id, int Count)[] rewards)
    {
        internal readonly string Id = id;
        internal readonly string[] Conditions = conditions;
        internal readonly float X = x, Y = y;
        internal readonly (string Id, int Count)[] Rewards = rewards;
    }
    // Independent expectations. Do not obtain eligibility, rewards or node
    // coordinates by calling the Forge evaluator being tested.
    private static readonly ForgeGoal[] ForgeGoalInventory =
    {
        new("pure_water_jug", new[] { "identifier=water_jug", "LIQUID_CONTAINER_TAG", "capacity>0; volume>0", "abs(volume-capacity)<=1000 native parts", "WaterFeatureHelper purity index=0", "real native purification before observation" }, -.78f, -.68f, ("water_filter_adv", 1)),
        new("rat_800g", new[] { "identifier=rat", "alive", "finite ANIMAL_WEIGHT_TAG>=800g on one animal", "user-authorized generated 800g test fixture; qualification/reward/persistence coverage" }, -.28f, -.84f, ("serum_green", 5), ("serum_red", 5)),
        new("quality_overload", new[] { "identifier=metal_ingot", "native purity=INGOT_PURITY_PERFECT", "actual furnace consumes two ores with quality>=200" }, .28f, -.84f, ("system_capped_neural_core", 1)),
        new("master_brewer", new[] { "identifier=wine_bottle or wine_bloomberry", "WineHelper.IsFinishedWine", "no WINE_COUNTERFEIT_TAG", "WineHelper tier=5", "AgableHelper age>=10", "explicit generated finished age10 wine fixture" }, .78f, -.68f, ("wine_yeast_infinite", 1)),
        new("scavenger_toolset", new[] { "same owned snapshot contains flashlight,screwdriver,wire_cutter,welder,metal_scanner", "five separate valid owned identities" }, -.96f, 0, ("backpack_large", 1)),
        new("max_scav_scanner", new[] { "identifier=metal_scanner", "SCAV_SCANNER_DOUBLE_CHANCE>=100", "actual electronic target actions consume materials and add 5 per upgrade" }, -.68f, .72f, ("scav_token", 1)),
        new("wealthy_store", new[] { "native GetTotalEstimatedValue()>100000, strictly", "supported new-run InitialCredits fixture explicitly recorded; no cash writes" }, .96f, 0, ("machine_bay_ext", 1)),
        new("store_attractiveness", new[] { "native GetCurrentStoreAttractiveness()>=1000", "actual powered mirage_projector cycle with legitimate installed modules", "no baseStoreAttractiveness/projectorAttractivenessBonus writes" }, .68f, .72f, ("storage_bay_large", 1)),
        new("faction_max_rep", new[] { "valid LOWER_LEVEL/UPPER_LEVEL/SECURITY/BLACK_MARKET/REVOLUTION/CARTEL faction", "finite GetReputationExact()>=200 in one faction", "explicit native faction-cap input fixture" }, 0, .82f, ("smuggler_bay_mod", 1)),
        new("workshop_master", new[] { "direct native buyout input fixture and actual payment", "ExecuteGameOver(buyout or buyout_mortgage) from actual ending chain", "virtual badge, no inventory reward", "NPC budget x2 after claim" }, 0, 0)
    };
    private static readonly string[] ForgeFactionIds = { "FACTION_LOWER_LEVEL", "FACTION_UPPER_LEVEL", "FACTION_SECURITY", "FACTION_BLACK_MARKET", "FACTION_REVOLUTION", "FACTION_CARTEL" };

    internal IEnumerable<object?> ForgeAchievements()
    {
        R.phase = "achievements-real-native-qualification";
        ForgeInitializeRows();
        R.environment["forgeAchievementScope"] = "Build26317870. Independent ten-goal denominator; explicitly factory-prepared tools, machines, module cores, kits, batteries and ingredients. Factory fixtures do not prove economic acquisition. Real native target/slot/machine actions, generated rat, wine age, faction and direct-buyout inputs, actual Forge page/node/claim buttons, independent UID/count/cash ledgers and actual ES3 readback. No completion/claim or account-stat writes. Input fixtures include funds, wine age, rat weight and native reputation. Steam account unlocks remain not-tested.";
        R.environment["forgeAchievementFixtures"] = _forgeFixtures;
        R.environment["forgeAchievementActions"] = _forgeActions;
        R.environment["forgeAchievementNights"] = _forgeNights;
        R.environment["forgeAchievementClaims"] = _forgeClaims;
        R.environment["graylineActions"] = _graylineActions;
        ForgeRequire("achievements.isolated-steam-and-fresh-run", "Steam disabled; fresh D1 Aug run with supported InitialCredits=150000; game Developer cheats off", new
        {
            steam = SteamManager.Initialized, day = StoreStation.GetDayCounter(), Store.playerCash, Store.devMode,
            config = AugState() == null ? null : Reflect.Json(Reflect.Get(AugState()!, "ConfigSnapshot")), scenario = NewGameData.Instance.startScenarioId
        }, !SteamManager.Initialized && !O.Reload && !Store.devMode && StoreStation.GetDayCounter() == 1 && AugReady() &&
            Reflect.Int(Reflect.Get(AugState()!, "ConfigSnapshot")!, "InitialCredits") == 150000 && Store.playerCash == 150000);
        ForgeValidateDenominator();
        Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
        using var steam = new ForgeSteamObservation(this);
        _forgeSteam = steam;
        R.environment["steamAchievementNativeObservation"] = steam.Evidence;
        try
        {
            for (int i = 0; i < 2; i++) _forgeBags.Add(ForgeFixture("backpack_large", "native storage fixture; not reward acquisition"));
            foreach (object? wait in ForgeCase("scavenger_toolset", ForgePrepareTools)) yield return wait;
            foreach (object? wait in ForgeCase("quality_overload", ForgePrepareIngot)) yield return wait;
            foreach (object? wait in ForgeCase("pure_water_jug", ForgePrepareWater)) yield return wait;
            foreach (object? wait in ForgeCase("store_attractiveness", ForgePrepareProjector)) yield return wait;
            foreach (object? wait in ForgeCase("master_brewer", ForgePrepareWine)) yield return wait;
            foreach (var goal in ForgeGoalInventory.Where(goal => goal.Id is not ("master_brewer" or "rat_800g" or "workshop_master" or "faction_max_rep")))
                foreach (object? wait in ForgeCase(goal.Id, () => ForgeClaim(goal))) yield return wait;
            // Generate eligibility inputs directly; reward evaluation/claim/save stay real.
            foreach (object? wait in ForgeCase("calendar", ForgeQualificationFixtures)) yield return wait;
            foreach (var goal in ForgeGoalInventory.Where(goal => goal.Id is "master_brewer" or "rat_800g" or "workshop_master" or "faction_max_rep"))
                foreach (object? wait in ForgeCase(goal.Id, () => ForgeClaim(goal))) yield return wait;
            foreach (object? wait in ForgeCase("workshop_master", ForgeVictoryBudgetFixture)) yield return wait;
            ForgePack();
            foreach (object? wait in ForgeCase("persistence", ForgeSaveReadback)) yield return wait;
        }
        finally
        {
            steam.WriteRows();
            ForgeFinalizeMissing();
            _forgeSteam = null;
            Reflect.Call(ForgeWorkshopUi, "CloseWindow");
        }
    }

    private void ForgeInitializeRows()
    {
        foreach (var goal in ForgeGoalInventory)
            _forgeGoals[goal.Id] = new JObject
            {
                ["id"] = goal.Id, ["conditions"] = new JArray(goal.Conditions),
                ["sharedConditions"] = new JArray("Each item proof: GeneralHelper.IsItemOwned", "Each item proof: positive unique UID, present once in observation", "Each item proof: unitCount>0", "page-open observation; latched completion", "no prerequisite achievements"),
                ["expectedRewards"] = new JArray(goal.Rewards.Select(reward => JObject.FromObject(new { itemId = reward.Id, count = reward.Count }))),
                ["qualification"] = "not-tested", ["page"] = "not-tested", ["claim"] = "not-tested", ["save"] = "not-tested", ["reload"] = "not-tested",
                ["economicAcquisition"] = "not-tested", ["status"] = "not-tested", ["hadFailure"] = false, ["failedChecks"] = new JArray()
            };
        R.environment["forgeAchievementMatrix"] = new JArray(ForgeGoalInventory.Select(goal => _forgeGoals[goal.Id]));
    }
    private void ForgeValidateDenominator()
    {
        var definitions = Reflect.Each(Reflect.Get(Reflect.Type("Nicokobo.ContentInterop.AchievementRules"), "Definitions")).ToArray();
        bool matches = definitions.Length == ForgeGoalInventory.Length && definitions.All(definition =>
        {
            var expected = ForgeGoalInventory.SingleOrDefault(goal => goal.Id == Reflect.Str(definition, "Id"));
            var rewards = Reflect.Each(Reflect.Get(definition, "Rewards")).ToArray();
            return expected != null && rewards.Length == expected.Rewards.Length && expected.Rewards.All(reward =>
                rewards.Count(actual => Reflect.Str(actual, "ItemId") == reward.Id && Reflect.Int(actual, "Count") == reward.Count) == 1);
        });
        ForgeRequire("achievements.runtime-denominator-ten-and-independent-rewards", ForgeGoalInventory.Select(goal => goal.Id).ToArray(),
            definitions.Select(Reflect.Json).ToArray(), matches);
    }
    private IEnumerable<object?> ForgeCase(string id, Func<IEnumerable<object?>> action)
    {
        int firstCheck = R.checks.Count;
        IEnumerator<object?>? iterator = null;
        Exception? failure = null;
        try { iterator = action().GetEnumerator(); } catch (Exception error) { failure = error; }
        if (iterator != null)
        {
            try
            {
                while (failure == null)
                {
                    bool next = false; object? value = null;
                    try { next = iterator.MoveNext(); if (next) value = iterator.Current; } catch (Exception error) { failure = error; }
                    if (!next || failure != null) break;
                    yield return value;
                }
            }
            finally { iterator.Dispose(); }
        }
        if (failure is FixtureUnavailable unavailable) ForgeSkip(id, unavailable.Message);
        else if (failure != null)
        {
            if (_forgeGoals.TryGetValue(id, out var row)) { row["status"] = "failed"; row["hadFailure"] = true; row["reason"] = failure.Message; }
            R.Check("achievements.execution." + id, "Actual business path completes with independent readback", null, false, error: failure.ToString());
        }
        var failedChecks = R.checks.Skip(firstCheck).Where(check => check.status == "failed").Select(check => check.name).ToArray();
        if (_forgeGoals.TryGetValue(id, out var resultRow) && failedChecks.Length > 0)
        {
            resultRow["hadFailure"] = true; resultRow["status"] = "failed"; resultRow["claim"] = "failed";
            resultRow["failedChecks"] = new JArray(((JArray?)resultRow["failedChecks"] ?? new JArray()).Values<string>().Concat(failedChecks).Distinct(StringComparer.Ordinal));
        }
    }
    private void ForgeRequire(string name, object? expected, object? actual, bool valid, string evidence = "")
    {
        R.Check(name, expected, actual, valid, evidence);
        if (!valid) throw new InvalidOperationException(name + " failed");
    }
    private void ForgeSkip(string id, string reason)
    {
        if (_forgeGoals.TryGetValue(id, out var row)) { row["reason"] = reason; if ((string?)row["status"] != "failed") row["status"] = "not-tested"; }
        R.Skip("achievements.remaining." + id, reason, false);
    }
    private void ForgeFinalizeMissing()
    {
        foreach (var goal in ForgeGoalInventory)
        {
            var row = _forgeGoals[goal.Id];
            if ((string?)row["claim"] == "passed" && !ForgeGoalHasFailures(row)) continue;
            if (row["reason"] == null) ForgeSkip(goal.Id, "The complete real qualification/page/claim chain did not finish in this run. Continue from the recorded fixture, business actions and per-goal observation; do not synthesize its completion.");
        }
        R.environment["forgeAchievementSummary"] = new
        {
            denominator = 10, claimed = _forgeGoals.Values.Count(row => (string?)row["claim"] == "passed" && !ForgeGoalHasFailures(row)),
            qualification = _forgeGoals.Values.Count(row => (string?)row["qualification"] == "passed"),
            complete = _forgeGoals.Values.All(row => !ForgeGoalHasFailures(row) && (string?)row["claim"] == "passed" && (string?)row["save"] == "passed"),
            accountUnlocks = "not-tested; no account or Steam statistic writes"
        };
    }
    private static bool ForgeGoalHasFailures(JObject row) => (bool?)row["hadFailure"] == true || (string?)row["status"] == "failed" ||
        (row["failedChecks"] is JArray failures && failures.Count > 0);
    private void ForgeAction(string action, object? detail = null) => _forgeActions.Add(JObject.FromObject(new
    { action, day = StoreStation.GetDayCounter(), frame = Time.frameCount, cash = Store.playerCash, detail }));
    private GameItem ForgeFixture(string id, string purpose)
    {
        var item = Spawn(id); Retain(item);
        _forgeFixtures.Add(new JObject { ["purpose"] = purpose, ["source"] = "actual registered factory fixture; economic acquisition not tested", ["item"] = ForgeItem(item) });
        return item;
    }
    private void ForgePack()
    {
        foreach (var item in EmporiumEntry.Instance.invElement.childItems.Concat(EmporiumEntry.Instance.backInvinvElement.childItems).Where(GeneralHelper.IsItemOwned).ToArray())
        {
            if (_forgeBags.Contains(item) || item.IsGameItemType("MACHINE") || item.IsTag("CAGE_TAG") || item.identifier == "magnifier") continue;
            foreach (var bag in _forgeBags)
            {
                var inventory = bag.contentWindow?.childElement as GameInventory;
                if (inventory?.TryFindOneValidInventorySlot(item)?.IsValid() != true) continue;
                int uid = item.uniqueId; GraphUtils.TryAcceptOnce(inventory, item);
                ForgeRequire("achievements.pack." + uid, "Same owned UID moves through a real backpack slot", ItemState(item), item.uniqueId == uid && item.parentInventory == inventory && GeneralHelper.IsItemOwned(item));
                break;
            }
        }
    }

    private IEnumerable<object?> ForgePrepareTools()
    {
        GameItem? scanner = null;
        foreach (string id in new[] { "flashlight", "screwdriver", "wire_cutter", "welder", "metal_scanner" })
        {
            var item = ForgeFixture(id, "native toolset fixture");
            if (id == "metal_scanner") scanner = item;
            yield return null;
        }
        foreach (object? wait in ForgeClaim(ForgeGoalInventory.Single(goal => goal.Id == "scavenger_toolset"))) yield return wait;
        ForgeRequire("achievements.scanner.native-default", 15, scanner!.GetTagReadonly("SCAV_SCANNER_DOUBLE_CHANCE").GetInt(), scanner.GetTagReadonly("SCAV_SCANNER_DOUBLE_CHANCE").GetInt() == 15 && !StartingPerk.IsTinkerer());
        int scannerUid = scanner.uniqueId;
        for (int i = 0; i < 17; i++)
        {
            ForgePack();
            var material = ForgeFixture("common_electronic", "scanner upgrade material " + (i + 1));
            int uid = material.uniqueId, cash = Store.playerCash, before = 15 + i * 5;
            long value = scanner.unitValue;
            bool allowed = material.mayThisTargetItemFunc?.Invoke(material, scanner) == true && material.canThisTargetItemFunc?.Invoke(material, scanner) == true && material.onThisTargetItemFunc != null;
            ForgeRequire("achievements.scanner.target-eligible." + i, new { chance = before, allowed = true }, new { chance = scanner.GetTagReadonly("SCAV_SCANNER_DOUBLE_CHANCE").GetInt(), allowed }, allowed && scanner.GetTagReadonly("SCAV_SCANNER_DOUBLE_CHANCE").GetInt() == before);
            ForgeAction("native-electronic-onThisTargetItemFunc", new { materialUid = uid, scannerUid, chanceBefore = before });
            material.onThisTargetItemFunc!.Invoke(material, scanner);
            ForgeRequire("achievements.scanner.real-upgrade." + i, new { chance = before + 5, scannerUid, value = value + 15, materialRemaining = 0, cash },
                new { item = ForgeItem(scanner), materialRemaining = ForgeOwnedUnits(uid), cash = Store.playerCash },
                scanner.uniqueId == scannerUid && scanner.GetTagReadonly("SCAV_SCANNER_DOUBLE_CHANCE").GetInt() == before + 5 && scanner.unitValue == value + 15 && ForgeOwnedUnits(uid) == 0 && Store.playerCash == cash,
                "Actual registered common_electronic target guard and callback -> ScavHelper.UpgradeScavScanner -> ConsumeUpgradeMaterial; no scanner tag assignment");
            yield return null;
        }
        _forgeProofUids.Add(scannerUid); ForgePack();
    }

    private GameItem ForgePoweredMachine(string id, int qualityTarget)
    {
        ForgePack();
        var machine = ForgeFixture(id, "native working machine fixture");
        if (qualityTarget > 0)
        {
            for (int i = 0; i < 2; i++)
            {
                var kit = ForgeFixture("module_bay_expansion_kit", "legitimate native module bay expansion input");
                int uid = kit.uniqueId, stage = machine.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt();
                if (kit.mayItemTargetItemFunc?.Invoke(kit, machine) != true || kit.canItemTargetItemFunc?.Invoke(kit, machine) != true || kit.onItemTargetItemFunc == null)
                    throw new FixtureUnavailable("Native module-bay kit target guards reject " + id);
                ForgeAction("native-module-bay-kit-target", new { uid, machineUid = machine.uniqueId, stage });
                kit.onItemTargetItemFunc.Invoke(kit, machine);
                ForgeRequire("achievements.module-bay.real-consumption." + machine.uniqueId + "." + i, new { stage = stage + 1, kitRemaining = 0 },
                    new { stage = machine.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt(), kitRemaining = ForgeOwnedUnits(uid) },
                    machine.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt() == stage + 1 && ForgeOwnedUnits(uid) == 0);
            }
            var modules = MachineHelper.GetBodyStorage(machine, 1) as GameInventory ?? throw new FixtureUnavailable("Native module grid missing for " + id);
            for (int i = 0; i < 16 && MachineryHelper.GetCurrentQualityBonus(machine) < qualityTarget; i++)
            {
                var module = Spawn("system_uncapped_neural_core");
                var marker = machine.TryFindOneValidInventorySlot(module);
                if (marker?.IsValid() != true || !ReferenceEquals(marker.inventory, modules))
                { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(module); break; }
                _forgeFixtures.Add(new JObject { ["purpose"] = "rare legal factory input; no economic acquisition claimed", ["item"] = ForgeItem(module) });
                Place(modules, module, "achievement-native-core." + machine.uniqueId + "." + i, machine);
            }
            if (MachineryHelper.GetCurrentQualityBonus(machine) < qualityTarget)
                throw new FixtureUnavailable("Actual expanded native grid cannot fit enough unmodified modules: " + id + "; quality=" + MachineryHelper.GetCurrentQualityBonus(machine) + ", required=" + qualityTarget);
        }
        var power = MachineHelper.GetBodyStorage(machine, 0) as GameInventory ?? throw new FixtureUnavailable("Native power slot missing for " + id);
        var battery = ForgeLegalBattery(machine);
        Place(power, battery, "achievement-native-battery." + machine.uniqueId, machine);
        _forgePoweredMachines.Add(machine);
        return machine;
    }
    private GameItem ForgeLegalBattery(GameItem machine)
    {
        int cost = MachineryHelper.GetMachinePowerUsage(machine);
        // Native capacities: ordinary100, extended250. The helper does not
        // clamp arbitrary input, so never ask it to create excess energy.
        GameItem battery = cost <= 100 ? PreBuiltItemHelper.EnergyCredit(100) : PreBuiltItemHelper.EnergyCreditExt(250);
        int capacity = battery.GetTagReadonly("power_source_item_max_energy").GetInt();
        ForgeRequire("achievements.power.legal-full-fixture." + battery.uniqueId, new { capacity = cost <= 100 ? 100 : 250, full = true, sufficient = true },
            new { cost, capacity, energy = Energy(battery), battery.identifier }, capacity == (cost <= 100 ? 100 : 250) && Energy(battery) == capacity && Energy(battery) >= cost && Energy(battery) <= capacity);
        _forgeFixtures.Add(new JObject { ["purpose"] = "legal native full battery input, not money; ordinary100 or extended250 for actual cost above100", ["item"] = ForgeItem(battery) });
        return battery;
    }
    private void ForgeMaintainPowerInputs()
    {
        foreach (var machine in _forgePoweredMachines.Where(machine => machine.parentInventory != null && GeneralHelper.IsItemOwned(machine)))
        {
            var slot = MachineHelper.GetBodyStorage(machine, 0) as GameInventory ?? throw new FixtureUnavailable("Actual battery slot disappeared: " + machine.identifier);
            var old = slot.childItems.SingleOrDefault();
            int cost = MachineryHelper.GetMachinePowerUsage(machine);
            if (old != null)
            {
                int capacity = old.GetTagReadonly("power_source_item_max_energy").GetInt();
                ForgeRequire("achievements.power.pre-night-bounds." + machine.uniqueId + "." + StoreStation.GetDayCounter(), "Existing native battery remains within capacity", new { energy = Energy(old), capacity }, Energy(old) >= 0 && Energy(old) <= capacity);
                if (PowerHelper.CanDrawPowerSource(old, cost)) continue;
                GameInventory? target = new[] { (GameInventory)EmporiumEntry.Instance.invElement }.Concat(_forgeBags.Select(bag => bag.contentWindow?.childElement).OfType<GameInventory>())
                    .FirstOrDefault(inventory => inventory.TryFindOneValidInventorySlot(old)?.IsValid() == true);
                if (target == null) throw new FixtureUnavailable("No real legal storage slot to remove an exhausted battery before replacement");
                int uid = old.uniqueId, energy = Energy(old); GraphUtils.TryAcceptOnce(target, old);
                ForgeRequire("achievements.power.actual-battery-removal." + uid, new { uid, energy, detachedFromPowerSlot = true }, ItemState(old), old.uniqueId == uid && Energy(old) == energy && old.parentInventory == target && GeneralHelper.IsItemOwned(old));
            }
            var replacement = ForgeLegalBattery(machine);
            ForgeAction("real-native-battery-slot-replacement", new { machineUid = machine.uniqueId, oldUid = old?.uniqueId, newUid = replacement.uniqueId, cost });
            Place(slot, replacement, "achievement-pre-night-battery." + machine.uniqueId, machine);
        }
    }
    private IEnumerable<object?> ForgePrepareIngot()
    {
        var furnace = ForgePoweredMachine("furnace", 300);
        var input = MachineHelper.GetBodyStorage(furnace, 2) as GameInventory ?? throw new FixtureUnavailable("Native furnace input slot missing");
        var output = MachineHelper.GetBodyStorage(furnace, 3) as GameInventory ?? throw new FixtureUnavailable("Native furnace output slot missing");
        var battery = ((GameInventory)MachineHelper.GetBodyStorage(furnace, 0)).childItems.Single();
        var ores = new[] { Spawn("common_ore"), Spawn("common_ore") };
        foreach (var ore in ores) { _forgeFixtures.Add(new JObject { ["purpose"] = "actual furnace raw ore input", ["item"] = ForgeItem(ore) }); Place(input, ore, "achievement-furnace-ore." + ore.uniqueId, furnace); }
        int energy = Energy(battery), debit = MachineryHelper.GetMachinePowerUsage(furnace), cash = Store.playerCash;
        ForgeAction("native-furnace-cycle", new { uid = furnace.uniqueId, inputUids = ores.Select(item => item.uniqueId).ToArray(), debit });
        NativeCycle(furnace);
        var ingots = output.childItems.Where(item => item.identifier == "metal_ingot").ToArray();
        ForgeRequire("achievements.ingot.actual-furnace-output", new { output = 1, purity = "INGOT_PURITY_PERFECT", consumed = 2, energy = energy - debit, cash },
            new { output = ingots.Select(ForgeItem).ToArray(), input = input.childItems.Select(ForgeItem).ToArray(), energy = Energy(battery), cash = Store.playerCash },
            ingots.Length == 1 && IngotPurityHelper.GetPurity(ingots[0]) == "INGOT_PURITY_PERFECT" && ores.All(ore => ForgeOwnedUnits(ore.uniqueId) == 0) && Energy(battery) == energy - debit && Store.playerCash == cash,
            "Independent formula: LOW level 6 + two-ore bonus 2 + quality/100 >=3 clamps to PERFECT level10. Actual native callback consumes both ores and real power; output purity is never assigned.");
        foreach (var ingot in ingots) { _forgeProofUids.Add(ingot.uniqueId); Retain(ingot); GraphUtils.TryAcceptOnce(EmporiumEntry.Instance.invElement, ingot); }
        yield return null;
    }
    private IEnumerable<object?> ForgePrepareWater()
    {
        var purifier = ForgePoweredMachine("water_purifier", 200);
        var vessel = Spawn("water_jug"); WaterHelper.EmptyContainer(vessel);
        int capacity = vessel.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt();
        if (capacity <= 20000) throw new FixtureUnavailable("Actual native jug capacity no longer fits the contaminated-water input");
        WaterHelper.AddLiquid(vessel, "water", capacity - 20000); WaterHelper.AddLiquid(vessel, "physical_contaminant", 20000);
        Place((GameInventory)MachineHelper.GetBodyStorage(purifier, 2), vessel, "achievement-purifier-full-dirty-jug", purifier);
        _forgeFixtures.Add(new JObject { ["purpose"] = "explicit full-capacity water + 20000 physical-contaminant raw input", ["item"] = ForgeItem(vessel) });
        var battery = ((GameInventory)MachineHelper.GetBodyStorage(purifier, 0)).childItems.Single();
        int energy = Energy(battery), debit = MachineryHelper.GetMachinePowerUsage(purifier), cash = Store.playerCash;
        ForgeAction("native-purifier-cycle", new { uid = purifier.uniqueId, vesselUid = vessel.uniqueId, capacity, debit }); NativeCycle(purifier);
        ForgeRequire("achievements.water.actual-full-jug-purification", new { water = capacity, contaminant = 0, volume = capacity, removed = 20000, energy = energy - debit, cash },
            new { vessel = ForgeItem(vessel), energy = Energy(battery), removed = purifier.GetTagReadonly("TOTAL_CONTAMINANT_REMOVED_TAG").GetInt(), cash = Store.playerCash },
            vessel.GetTagReadonly(Liquid.GetCurrentPartFromId("water")).GetInt() == capacity && vessel.GetTagReadonly(Liquid.GetCurrentPartFromId("physical_contaminant")).GetInt() == 0 &&
            WaterHelper.GetTotalVolume(vessel) == capacity && WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(vessel)) == 0 &&
            purifier.GetTagReadonly("TOTAL_CONTAMINANT_REMOVED_TAG").GetInt() == 20000 && Energy(battery) == energy - debit && Store.playerCash == cash,
            "Actual native purifier callback, >=200 installed quality removes all input contaminant while preserving volume and jug UID; no AddPureWater result fixture.");
        _forgeProofUids.Add(vessel.uniqueId); yield return null;
    }
    private IEnumerable<object?> ForgePrepareProjector()
    {
        Store.baseStoreAttractiveness = 1000;
        _forgeFixtures.Add(JObject.FromObject(new { purpose = "Explicit base-attractiveness input for the Forge qualification test", Store.baseStoreAttractiveness }));
        _forgeProjector = ForgePoweredMachine("mirage_projector", 0);
        var machine = _forgeProjector;
        var battery = ((GameInventory)MachineHelper.GetBodyStorage(machine, 0)).childItems.Single();
        int energy = Energy(battery), debit = MachineryHelper.GetMachinePowerUsage(machine);
        int independentBonus = Math.Max(0, 100 + MachineryHelper.GetCurrentQualityBonus(machine) / 2 + MachineryHelper.GetCurrentPerformanceBonus(machine) / 4);
        ForgeAction("native-projector-cycle", new { uid = machine.uniqueId, debit, independentBonus }); NativeCycle(machine);
        ForgeRequire("achievements.attractiveness.actual-powered-projector", new { bonus = independentBonus, energy = energy - debit },
            new { Store.projectorAttractivenessBonus, attractiveness = Store.GetCurrentStoreAttractiveness(), energy = Energy(battery), machine = ForgeItem(machine) },
            machine.IsTag("IS_PROJECTOR_ACTIVE") && Store.projectorAttractivenessBonus == independentBonus && Energy(battery) == energy - debit,
            "The native powered cycle owns projectorAttractivenessBonus. The probe only reads the result; the fixed performance/quality divisors above are independent expectations.");
        yield return null;
    }
    private IEnumerable<object?> ForgePrepareWine()
    {
        var bottle = ForgeFixture("wine_bottle", "empty original wine-bottle ingredient fixture");
        WaterHelper.AddPureWater(bottle, 750000);
        var ingredients = GraphUtils.FindAllChildrenType<GameInventory>(bottle).FirstOrDefault() ?? throw new FixtureUnavailable("Native wine ingredient inventory missing");
        int selected = 0;
        for (int attempt = 0; attempt < 64 && selected < 2; attempt++)
        {
            var berry = Spawn("wine_berry"); berry.ModifyTag("BERRY_CONDITION_INT", tag => tag.Enable().SetInt(2)); int condition = berry.GetTagReadonly("BERRY_CONDITION_INT").GetInt();
            _forgeFixtures.Add(new JObject { ["purpose"] = "explicit condition2 berry fixture; not crop/merchant acquisition", ["condition"] = condition, ["selected"] = condition == 2, ["item"] = ForgeItem(berry) });
            if (condition != 2) { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(berry); continue; }
            Place(ingredients, berry, "achievement-wine-native-berry." + selected); selected++;
        }
        if (selected != 2) throw new FixtureUnavailable("No two unmodified condition2 berries within 64 actual factory draws; no berry condition was assigned");
        Place(ingredients, Spawn("wine_superyeast"), "achievement-wine-native-superyeast");
        int[] inputUids = ingredients.childItems.Select(item => item.uniqueId).ToArray();
        PixelWindow oldWindow = bottle.contentWindow;
        ForgeAction("native-WineHelper.TryStartFermentation", new { bottleUid = bottle.uniqueId, inputUids, independentScore = 7, yeastPower = 4 });
        bool started = WineHelper.TryStartFermentation(bottle);
        ForgeRequire("achievements.wine.real-fermentation-start", new { state = 1, score = 7, yeast = 4, remaining = 4, consumedInputs = inputUids.Length }, ForgeItem(bottle),
            started && bottle.GetTagReadonly("WINE_CONTAINER_STATE").GetInt() == 1 && bottle.GetTagReadonly("WINE_SCORE_INT").GetInt() == 7 &&
            bottle.GetTagReadonly("WINE_YEAST_POWER_INT").GetInt() == 4 && bottle.GetTagReadonly("FERMENTATION_TIME_REMAINING_INT").GetInt() == 4 &&
            !bottle.IsTag("WINE_COUNTERFEIT_TAG") && inputUids.All(uid => ForgeOwnedUnits(uid) == 0),
            "Independent score=2+2 berries +1 multiple berries +1 genuine yeast +1 pure water. Native business entry consumes ingredients. FinishFermentation/OnAge and wine/age tags are never invoked or assigned by the probe.");
        if (oldWindow != null && bottle.contentWindow != oldWindow && oldWindow.parentItems.Count == 0)
        { var carrier = ItemDirectory.CreateEmptyItem(""); carrier.SetContentWindow(oldWindow); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(carrier); }
        _forgeWine = bottle; _forgeProofUids.Add(bottle.uniqueId); ForgePack(); yield return null;
    }
    private IEnumerable<object?> ForgePrepareRat()
    {
        var cage = ForgeFixture("animal_cage", "native cage for the explicitly generated achievement test rat");
        var rat = Spawn("rat");
        rat.ModifyTag("ANIMAL_WEIGHT_TAG", tag => tag.Enable().SetFloat(800f));
        var grid = AnimalCage.GetCageInventory(cage);
        Place(grid, rat, "achievement-generated-800g-rat");
        _forgeFixtures.Add(new JObject { ["purpose"] = "User explicitly requested direct generation; only ANIMAL_WEIGHT_TAG is configured to 800g. This proves achievement behavior, not natural growth.", ["item"] = ForgeItem(rat) });
        ForgeRequire("achievements.rat.generated-live-800g-fixture", "Owned live rat at exactly 800g", ForgeItem(rat),
            GeneralHelper.IsItemOwned(rat) && !HusbandryHelper.IsAnimalDead(rat) && rat.GetTagReadonly("ANIMAL_WEIGHT_TAG").GetFloat() == 800f);
        _forgeCage = cage; _forgeRat = rat; _forgeProofUids.Add(rat.uniqueId); yield return null;
    }

    internal IEnumerable<object?> RatFixtureAchievement()
    {
        ForgeInitializeRows(); ForgeValidateDenominator();
        R.environment["forgeAchievementScope"] = "User-authorized generated live 800g rat. Only actual qualification, Forge UI claim, duplicate guard, native ES3 and independent-process reload are tested; natural breeding/growth is outside this scenario.";
        R.environment["forgeAchievementFixtures"] = _forgeFixtures;
        R.environment["forgeAchievementActions"] = _forgeActions;
        R.environment["forgeAchievementClaims"] = _forgeClaims;
        foreach (object? wait in ForgePrepareRat()) yield return wait;
        foreach (object? wait in ForgeClaim(ForgeGoalInventory.Single(goal => goal.Id == "rat_800g"))) yield return wait;
        ForgeRequire("ratfixture.real-claim-complete", "Actual rat_800g claim succeeded", _forgeGoals["rat_800g"],
            (string?)_forgeGoals["rat_800g"]["claim"] == "passed");
        foreach (object? wait in ForgeSaveReadback()) yield return wait;
        Reflect.Call(ForgeWorkshopUi, "CloseWindow");
    }

    private IEnumerable<object?> ForgeQualificationFixtures()
    {
        foreach (object? wait in ForgePrepareRat()) yield return wait;
        if (_forgeWine == null) throw new FixtureUnavailable("Wine ingredient fixture missing");
        WineHelper.FinishFermentation(_forgeWine);
        _forgeWine.ModifyTag("AGE_INT", tag => tag.Enable().SetInt(10));
        _forgeFixtures.Add(new JObject { ["purpose"] = "Explicit finished, age10 wine qualification fixture; no ten-night wait", ["item"] = ForgeItem(_forgeWine) });
        Store.storeReputations.First(faction => faction.factionId == "FACTION_BLACK_MARKET").ModReputationRaw(200);
        _forgeFixtures.Add(JObject.FromObject(new { purpose = "Explicit native faction-cap input for Forge eligibility", reputations = ReputationSnapshot() }));
        int quote = Store.GetStoreBuyPrice();
        if (Store.playerCash < quote) Store.ModCash(quote + 1000 - Store.playerCash, false);
        int before = Store.playerCash;
        Store.BuyOutStore();
        _forgeGoals["workshop_master"]["purchase"] = JObject.FromObject(new { purpose = "Direct native buyout fixture; no landlord wait", quote, before, after = Store.playerCash });
        ForgeRequire("achievements.fixture.actual-buyout-transaction", before - quote, Store.playerCash, Store.IsPropertyPaid && Store.playerCash == before - quote);
        Store.ExecuteGameOver("buyout");
        foreach (object? wait in ForgeContinueVictory()) yield return wait;
        ForgePack();
    }
    private void ForgeRecoveryInputs()
    {
        if (AugState() == null) return;
        object flow = Reflect.Get(Reflect.Type("AugMechanicalAscension.Integration.ConsumableRuntimeFlow"), "_active") ?? throw new FixtureUnavailable("Aug normal recovery business flow missing");
        foreach (string kind in new[] { "common_medical", "common_electronic" })
        {
            int before = Reflect.Int(AugState()!, kind == "common_medical" ? "Health" : "Exposure");
            if (kind == "common_medical" ? before >= 90 : before < 20) continue;
            var input = ForgeFixture(kind, "explicit actual consumable survival input for natural calendar"); int uid = input.uniqueId;
            long value = input.unitValue; ForgeAction("actual-Aug-ConsumeRecovery", new { kind, uid, before }); Reflect.Call(flow, "ConsumeRecovery", input);
            int after = Reflect.Int(AugState()!, kind == "common_medical" ? "Health" : "Exposure");
            int expected = kind == "common_medical" ? (int)Math.Min(100, before + value) : Math.Max(0, before - 25);
            ForgeRequire("achievements.calendar.actual-recovery." + uid, new { amount = expected, remaining = 0 }, new { amount = after, remaining = ForgeOwnedUnits(uid) }, after == expected && ForgeOwnedUnits(uid) == 0);
        }
    }
    private void ForgeRatCare()
    {
        if (_forgeRat == null || _forgeCage == null || HusbandryHelper.IsAnimalDead(_forgeRat)) return;
        var grid = AnimalCage.GetCageInventory(_forgeCage);
        // Native TryFeed eats noncanned calorie items in this same inventory.
        if (!grid.childItems.Any(item => item.IsTag("CALORIE_VALUE_TAG")))
        { var food = Spawn("wine_berry"); Place(grid, food, "achievement-rat-native-calorie-input"); _forgeFixtures.Add(new JObject { ["purpose"] = "native rat food input", ["item"] = ForgeItem(food) }); }
        foreach (string id in new[] { "serum_red", "serum_green" })
        {
            string liquid = id == "serum_red" ? "livestock_steroid" : "livestock_immunity";
            if (_forgeRat.GetTagReadonly(Liquid.GetCurrentPartFromId(liquid)).GetInt() >= 10) continue;
            var serum = ForgeFixture(id, "actual native rat-care input; neither weight nor genetic tags assigned");
            if (serum.mayThisTargetItemFunc?.Invoke(serum, _forgeRat) != true || serum.canThisTargetItemFunc?.Invoke(serum, _forgeRat) != true || serum.onThisTargetItemFunc == null)
                throw new FixtureUnavailable("Native serum target guards rejected actual rat");
            int uses = serum.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt(), before = _forgeRat.GetTagReadonly(Liquid.GetCurrentPartFromId(liquid)).GetInt();
            ForgeAction("native-serum-onThisTargetItemFunc", new { serumUid = serum.uniqueId, ratUid = _forgeRat.uniqueId, liquid }); serum.onThisTargetItemFunc.Invoke(serum, _forgeRat);
            ForgeRequire("achievements.rat.actual-serum." + serum.uniqueId, new { uses = uses - 1, concentration = before + 20 },
                new { uses = serum.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt(), concentration = _forgeRat.GetTagReadonly(Liquid.GetCurrentPartFromId(liquid)).GetInt() },
                serum.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt() == uses - 1 && _forgeRat.GetTagReadonly(Liquid.GetCurrentPartFromId(liquid)).GetInt() == before + 20);
        }
    }

    private IEnumerable<object?> ForgeServeRequired()
    {
        float deadline = Time.realtimeSinceStartup + 360;
        while (!Store.CanEndDay() || Store.currentClientInstance != null || DialogUIManager.Instance.currentDialog != null)
        {
            if (Time.realtimeSinceStartup >= deadline) throw new FixtureUnavailable("Required natural visitor did not finish within360s; no client queue/dismissability was edited");
            if (SteamManager.Initialized) throw new InvalidOperationException("Steam became initialized; refusing further scenario actions");
            if (GameOverUIManager.Instance?.IsShowing == true)
            { foreach (object? wait in ForgeContinueVictory()) yield return wait; continue; }
            var client = Store.currentClientInstance?.GetClientBlueprint();
            if (client == null)
            {
                if (Store.CanEndDay() && DialogUIManager.Instance.currentDialog == null) break;
                if (Store.CanNextClient() && Store.lockNextBellFlag == 0 && DialogUIManager.Instance.currentDialog == null) GraylineRingBell();
                yield return null;
            }
            else if (!Store.isClientArrived) yield return null;
            else if (client.identifier == "landlord") foreach (object? wait in ForgeLandlord(client)) yield return wait;
            else if (client.identifier == GraylineStoryActor) foreach (object? wait in ForgeStoryVisitor(client)) yield return wait;
            else foreach (object? wait in GraylineOrdinaryClient(client)) yield return wait;
        }
    }
    private IEnumerable<object?> ForgeLandlord(StoreClient client)
    {
        int cash = Store.playerCash, quote = Store.GetStoreBuyPrice(); bool chosen = false, rang = false;
        float deadline = Time.realtimeSinceStartup + 150;
        while (ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client))
        {
            if (Time.realtimeSinceStartup >= deadline) throw new FixtureUnavailable("Actual landlord dialogue did not finish; no payment or ending was forced");
            GraylineAdvanceDialogue();
            if (!chosen && GraylineChoicesVisible())
            {
                string label = LocHelper.GetLocalizedDialogue("dialog_landlord_player_buyout", quote);
                if (!Store.CanBuyOutStore() || GraylineChoice(label, label) == null) throw new FixtureUnavailable("Actual landlord cannot offer an affordable visible buyout choice; quote=" + quote + ", cash=" + cash);
                ForgeAction("visible-native-landlord-buyout-choice", new { quote, cash }); GraylineClickChoice(label, label, "achievements-buyout"); chosen = true;
                ForgeRequire("achievements.victory.actual-buyout-charge", new { cash = cash - quote, propertyPaid = true }, new { cash = Store.playerCash, Store.IsPropertyPaid }, Store.playerCash == cash - quote && Store.IsPropertyPaid);
                _forgeGoals["workshop_master"]["purchase"] = JObject.FromObject(new { day = StoreStation.GetDayCounter(), cashBefore = cash, cashAfter = Store.playerCash, quote, source = "actual arrived native landlord dialogue choice" });
            }
            if (!rang && chosen && Store.IsClientDimissable() && Store.lockNextBellFlag == 0 && DialogUIManager.Instance.currentDialog == null) { GraylineRingBell(); rang = true; }
            yield return null;
        }
        if (chosen)
        {
            float endingDeadline = Time.realtimeSinceStartup + 15;
            while (_forgeSteam?.Endings.Any(row => (string?)row["ending"] == "buyout") != true && Time.realtimeSinceStartup < endingDeadline) { GraylineAdvanceDialogue(); yield return null; }
            ForgeRequire("achievements.victory.actual-ending-and-deed", "Native dialogue end called ExecuteGameOver(buyout); one real owned deed", new { endings = _forgeSteam?.Endings, deeds = Store.FindAllItem().Where(item => item.identifier == "deed").Select(ForgeItem).ToArray() },
                _forgeSteam?.Endings.Any(row => (string?)row["ending"] == "buyout") == true && Store.FindAllItem().Count(item => item.identifier == "deed" && GeneralHelper.IsItemOwned(item)) == 1);
            foreach (object? wait in ForgeContinueVictory()) yield return wait;
        }
    }
    private IEnumerable<object?> ForgeContinueVictory()
    {
        if (_forgeSteam?.Endings.Any(row => (string?)row["ending"] is "buyout" or "buyout_mortgage") != true)
            throw new FixtureUnavailable("Unexpected native game-over; no survival/date/reputation bypass is used");
        var ui = GameOverUIManager.Instance;
        if (!ui.IsShowing) yield break;
        if (ui.continuePlayButton.activeInHierarchy)
        {
            var button = ui.continuePlayButton.GetComponent<Button>() ?? ui.continuePlayButton.GetComponentInChildren<Button>();
            if (button == null || !button.IsInteractable()) throw new FixtureUnavailable("Native victory Continue button is unavailable");
            ForgeAction("native-victory-continue-button"); button.onClick.Invoke();
        }
        else
        {
            ForgeAction("native-victory-run-summary-button"); ui.OnMenuButtonClick(); yield return null;
            var root = Reflect.Get(typeof(RunSummaryUI), "root") as GameObject;
            string label = LocHelper.GetLocalizedMainMenu("new_continue");
            var buttons = root?.GetComponentsInChildren<Button>().Where(button => button.gameObject.activeInHierarchy && button.IsInteractable() && button.GetComponentsInChildren<TextMeshProUGUI>().Any(text => text.text == label)).ToArray() ?? Array.Empty<Button>();
            if (buttons.Length != 1) throw new FixtureUnavailable("Native run summary has no unique actual Continue button");
            ForgeAction("native-run-summary-continue-button"); buttons[0].onClick.Invoke();
        }
        yield return null;
        ForgeRequire("achievements.victory.actual-continue-play", "Native victory overlay closed through its real button event", ui.IsShowing, !ui.IsShowing);
    }
    private IEnumerable<object?> ForgeStoryVisitor(StoreClient client)
    {
        bool selected = false; float deadline = Time.realtimeSinceStartup + 150;
        while (ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client))
        {
            if (Time.realtimeSinceStartup >= deadline) throw new FixtureUnavailable("Natural Aug story visitor did not finish; no story choice/queue was assigned");
            GraylineAdvanceDialogue();
            if (GraylineChoice("结束会面", "End the visit") != null) GraylineClickChoice("结束会面", "End the visit", "achievement-calendar-story-leave");
            else if (!selected && GraylineChoicesVisible())
            {
                string ev = (string?)GraylineStoryState()["ActiveEventId"] ?? "";
                (string zh, string en) = ev switch
                {
                    "E01" => ("[不接这笔退货]", "[Refuse the return]"),
                    "E02" => ("[收下目录和钛合金]", "[Take the index and the titanium]"),
                    _ => throw new FixtureUnavailable("Unrecognized required story event in achievement calendar: " + ev)
                };
                ForgeAction("actual-calendar-story-choice", new { ev, zh, en }); GraylineClickChoice(zh, en, "achievement-calendar-" + ev); selected = true;
            }
            yield return null;
        }
        ForgePack();
    }

    private JObject ForgeState() => (JObject)Reflect.Json(Reflect.Get(ForgeWorkshop, "_state"));
    private CustomUIWindow? ForgeWindow() => Reflect.Get(ForgeWorkshopUi, "_window") as CustomUIWindow;
    private IEnumerable<GameItem> ForgeOwned() => Store.FindAllItem().Where(item => GeneralHelper.IsItemOwned(item) && item.uniqueId > 0 && item.unitCount > 0)
        .GroupBy(item => item.uniqueId).Where(group => group.Count() == 1).Select(group => group.Single());
    private int ForgeOwnedUnits(int uid) => Store.FindAllItem().Where(item => item.uniqueId == uid && GeneralHelper.IsItemOwned(item)).Sum(item => item.unitCount);
    private JObject ForgeQualification(string id)
    {
        var owned = ForgeOwned().ToArray(); var proof = new List<GameItem>(); bool met = false; object? detail = null;
        switch (id)
        {
            case "pure_water_jug":
                proof.AddRange(owned.Where(item => item.identifier == "water_jug" && item.IsTag("LIQUID_CONTAINER_TAG") && item.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt() > 0 &&
                    WaterHelper.GetTotalVolume(item) > 0 && Math.Abs((long)WaterHelper.GetTotalVolume(item) - item.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt()) <= 1000 && WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(item)) == 0).Take(1)); met = proof.Count == 1; break;
            case "rat_800g":
                proof.AddRange(owned.Where(item => item.identifier == "rat" && !HusbandryHelper.IsAnimalDead(item) && !float.IsNaN(item.GetTagReadonly("ANIMAL_WEIGHT_TAG").GetFloat()) && !float.IsInfinity(item.GetTagReadonly("ANIMAL_WEIGHT_TAG").GetFloat()) && item.GetTagReadonly("ANIMAL_WEIGHT_TAG").GetFloat() >= 800).Take(1));
                met = proof.Count == 1; detail = owned.Where(item => item.identifier == "rat").Select(ForgeItem).ToArray(); break;
            case "quality_overload": proof.AddRange(owned.Where(item => item.identifier == "metal_ingot" && IngotPurityHelper.GetPurity(item) == "INGOT_PURITY_PERFECT").Take(1)); met = proof.Count == 1; break;
            case "master_brewer": proof.AddRange(owned.Where(item => item.identifier is "wine_bottle" or "wine_bloomberry").Where(item => WineHelper.IsFinishedWine(item) && !item.IsTag("WINE_COUNTERFEIT_TAG") && WineHelper.GetWineQualityTier(item) == 5 && AgableHelper.GetAge(item) >= 10).Take(1)); met = proof.Count == 1; detail = _forgeWine == null ? null : ForgeItem(_forgeWine); break;
            case "scavenger_toolset": foreach (string tool in new[] { "flashlight", "screwdriver", "wire_cutter", "welder", "metal_scanner" }) { var item = owned.FirstOrDefault(item => item.identifier == tool); if (item != null) proof.Add(item); } met = proof.Count == 5; break;
            case "max_scav_scanner": proof.AddRange(owned.Where(item => item.identifier == "metal_scanner" && item.GetTagReadonly("SCAV_SCANNER_DOUBLE_CHANCE").GetInt() >= 100).Take(1)); met = proof.Count == 1; break;
            case "wealthy_store": detail = Store.GetTotalEstimatedValue(); met = Store.GetTotalEstimatedValue() > 100000; break;
            case "store_attractiveness": detail = new { value = Store.GetCurrentStoreAttractiveness(), Store.baseStoreAttractiveness, Store.projectorAttractivenessBonus, Store.garbageLevel }; met = Store.GetCurrentStoreAttractiveness() >= 1000; break;
            case "faction_max_rep":
                var factions = Store.storeReputations.Where(faction => faction != null && ForgeFactionIds.Contains(faction.factionId) && StoreReputation.IsFactionValid(faction.factionId)).ToArray();
                detail = factions.Select(faction => new { faction.factionId, value = faction.GetReputationExact() }).ToArray();
                met = factions.Any(faction => !double.IsNaN(faction.GetReputationExact()) && !double.IsInfinity(faction.GetReputationExact()) && faction.GetReputationExact() >= 200); break;
            case "workshop_master": detail = _forgeSteam?.Endings; met = _forgeGoals["workshop_master"]["purchase"] != null && Store.IsPropertyPaid && _forgeSteam?.Endings.Any(row => (string?)row["ending"] is "buyout" or "buyout_mortgage") == true; break;
        }
        return new JObject { ["met"] = met, ["uids"] = new JArray(proof.Select(item => item.uniqueId)), ["items"] = new JArray(proof.Select(ForgeItem)), ["detail"] = detail == null ? null : Reflect.Json(detail), ["day"] = StoreStation.GetDayCounter() };
    }
    private IEnumerable<object?> ForgeClaim(ForgeGoal goal)
    {
        var row = _forgeGoals[goal.Id];
        int firstCheck = R.checks.Count;
        try
        {
        if (ForgeGoalHasFailures(row)) { ForgeSkip(goal.Id, "This goal already has failed evidence in the current run; later page/save stages cannot promote it to passed."); yield break; }
        if ((string?)row["claim"] == "passed") yield break;
        var qualification = ForgeQualification(goal.Id); row["observation"] = qualification;
        if ((bool?)qualification["met"] != true)
        {
            string next = goal.Id switch
            {
                "faction_max_rep" => "Reach200 in a named valid faction through actual customer/request/arrest/night-event transactions, preserving their payment/stock/UID and exact reputation ledger. Direct reputation writes or ModReputation fixtures are forbidden.",
                "rat_800g" => "Run the explicit generated-800g fixture in the ratfixture family, then inspect the actual page and reward transaction.",
                _ => "Continue the recorded actual preparation/business path until every listed qualification holds, then reopen the actual page."
            };
            ForgeSkip(goal.Id, "Independent current qualification is not met. Next step: " + next); yield break;
        }
        row["qualification"] = "passed";
        ForgePack();
        ForgeAction("actual-Forge-open-chain", new { goal.Id });
        bool opened = (bool)Reflect.Call(ForgeWorkshopUi, "OpenChain", ForgeAchievementChain)!; yield return null;
        var state = ForgeState();
        ForgeRequire("achievements.page.actual-latched-goal." + goal.Id, new { visible = true, completed = goal.Id, evidenceUids = qualification["uids"] }, new { opened, visible = ForgeWindow()?.IsVisible, state },
            opened && ForgeWindow()?.IsVisible == true && state["Completed"]?[goal.Id] != null &&
            ((JArray)state["Completed"]![goal.Id]!["Uids"]!).Values<int>().OrderBy(uid => uid).SequenceEqual(((JArray)qualification["uids"]!).Values<int>().OrderBy(uid => uid)));
        row["page"] = "passed";
        foreach (int uid in ((JArray)qualification["uids"]!).Values<int>()) _forgeProofUids.Add(uid);
        Button node = ForgeNode(goal); ForgeAction("actual-Forge-node-button", new { goal.Id }); node.onClick.Invoke(); yield return null;
        ForgeRequire("achievements.page.selected." + goal.Id, goal.Id, Reflect.Get(ForgeWorkshopUi, "_selected"), Reflect.Str(ForgeWorkshopUi, "_selected") == goal.Id);
        var claim = ForgeClaimButton(false);
        ForgeRequire("achievements.claim.actual-enabled-button." + goal.Id, "Visible enabled actual claim button", claim.IsInteractable(), claim.gameObject.activeInHierarchy && claim.IsInteractable());
        ForgeProveRewardRoom(goal);
        var beforeUids = Store.FindAllItem().Select(item => item.uniqueId).ToHashSet(); int cash = Store.playerCash;
        var counts = goal.Rewards.ToDictionary(reward => reward.Id, reward => ForgeOwned().Where(item => item.identifier == reward.Id).Sum(item => item.unitCount));
        ForgeAction("actual-Forge-claim-button", new { goal.Id }); claim.onClick.Invoke(); yield return null;
        state = ForgeState();
        var additions = Store.FindAllItem().Where(item => !beforeUids.Contains(item.uniqueId)).OrderBy(item => item.uniqueId).ToArray();
        bool isClaimed = ((JArray)state["Claimed"]!).Values<string>().Contains(goal.Id);
        bool rewards = additions.Length == goal.Rewards.Sum(reward => reward.Count) && additions.Select(item => item.uniqueId).Distinct().Count() == additions.Length &&
            goal.Rewards.All(reward => additions.Count(item => item.identifier == reward.Id && item.unitCount == 1 && GeneralHelper.IsItemOwned(item) && item.uniqueId > 0) == reward.Count &&
                ForgeOwned().Where(item => item.identifier == reward.Id).Sum(item => item.unitCount) - counts[reward.Id] == reward.Count);
        ForgeRequire("achievements.claim.independent-reward-ledger." + goal.Id, new { cash, expectedRewards = goal.Rewards.Select(reward => new { reward.Id, reward.Count }).ToArray(), uniqueOwnedNewUids = true, pending = "null" },
            new { cash = Store.playerCash, additions = additions.Select(ForgeItem).ToArray(), state }, isClaimed && state["Pending"]?.Type == JTokenType.Null && rewards && Store.playerCash == cash);
        var receipt = new JObject { ["goal"] = goal.Id, ["cashBefore"] = cash, ["cashAfter"] = Store.playerCash, ["items"] = new JArray(additions.Select(ForgeItem)), ["proofUids"] = qualification["uids"]!.DeepClone() };
        _forgeClaims.Add(receipt); row["receipt"] = receipt; row["delivery"] = "passed";
        foreach (var item in additions) { _forgeReceipts.Add(item.uniqueId); Retain(item); }
        ForgeRequire("achievements.claim.button-disabled." + goal.Id, "Claimed button disabled", ForgeClaimButton(true).IsInteractable(), !ForgeClaimButton(true).IsInteractable());
        var ledger = ForgeLedger(); ForgeAction("explicit-actual-claim-business-replay", new { goal.Id }); Reflect.Call(ForgeWorkshop, "Claim", goal.Id);
        ForgeRequire("achievements.claim.no-repeat." + goal.Id, ledger, ForgeLedger(), JToken.DeepEquals(ledger, ForgeLedger()), "Actual production Claim callback is invoked again only to verify the normal duplicate guard; no completion/claimed flags are written.");
        foreach (object? wait in CaptureEvidenceFrame("native-achievement-claimed-" + goal.Id)) yield return wait;
        Reflect.Call(ForgeWorkshopUi, "CloseWindow"); ForgePack();
        if (!ForgeGoalHasFailures(row)) { row["claim"] = "passed"; row["status"] = "claimed-awaiting-save"; }
        yield return null;
        }
        finally
        {
            var failures = R.checks.Skip(firstCheck).Where(check => check.status == "failed").Select(check => check.name).ToArray();
            if (failures.Length > 0)
            {
                row["hadFailure"] = true; row["status"] = "failed"; row["claim"] = "failed";
                row["failedChecks"] = new JArray(((JArray?)row["failedChecks"] ?? new JArray()).Values<string>().Concat(failures).Distinct(StringComparer.Ordinal));
            }
        }
    }
    private void ForgeProveRewardRoom(ForgeGoal goal)
    {
        var grid = EmporiumEntry.Instance.invElement;
        if (goal.Rewards.Length == 0)
        { _forgeGoals[goal.Id]["capacityProof"] = new JObject { ["virtualReward"] = true, ["requiredItemSlots"] = 0 }; return; }
        ForgeRequire("achievements.claim.main-grid-unlocked." + goal.Id, "Native main inventory accepts insertions", grid.IsInsertLocked(), !grid.IsInsertLocked());
        var before = SnapshotItems(); int cash = Store.playerCash;
        string data = Store.ModData().TryGetValue(ForgeAchievementKey, out string original) ? original : "";
        var samples = new List<GameItem>(); var placements = new JArray();
        try
        {
            foreach (var reward in goal.Rewards)
                for (int index = 0; index < reward.Count; index++)
                {
                    var item = Spawn(reward.Id); samples.Add(item); SlotMarker? accepted = null;
                    // Independent physical rehearsal through native admission,
                    // never the Forge planner under test. Keep every earlier
                    // sample placed until the complete reward batch fits.
                    for (int flip = 0; flip < 2 && accepted == null; flip++)
                        for (int turn = 0; turn < 4 && accepted == null; turn++)
                            for (int y = 0; y < grid.inventoryShape.globalHeight && accepted == null; y++)
                                for (int x = 0; x < grid.inventoryShape.globalWidth && accepted == null; x++)
                                {
                                    var shape = new GridShapeBuilder(item.shape).SetTransform(x, y, flip != 0, 4 - turn).Build();
                                    var marker = grid.TryInventorySlot(item, 1, shape);
                                    if (marker?.IsValid() != true || marker.targetItem != null || marker.numTransfer != 1) continue;
                                    accepted = marker;
                                }
                    if (accepted == null)
                    {
                        _forgeGoals[goal.Id]["capacityProof"] = new JObject { ["status"] = "native-rehearsal-no-space", ["blockedReward"] = reward.Id, ["index"] = index,
                            ["width"] = grid.inventoryShape.globalWidth, ["height"] = grid.inventoryShape.globalHeight, ["placedSamples"] = placements.DeepClone(), ["existingItems"] = before.DeepClone() };
                        throw new FixtureUnavailable("Actual independent native full-batch rehearsal exhausted all main-grid positions/orientations for " + reward.Id + " #" + index + "; earlier samples and original items occupied their real cells. Claim was not attempted; see capacityProof. This cannot apply to the zero-item victory reward.");
                    }
                    int transferred = accepted.TryAcceptOnce();
                    ForgeRequire("achievements.claim.capacity-native-placement." + goal.Id + "." + samples.Count, "Exactly one same-UID unstacked sample physically accepted by native grid", new { transferred, item = ItemState(item) },
                        transferred == 1 && item.parentInventory == grid && item.unitCount == 1 && GeneralHelper.IsItemOwned(item));
                    var placedShape = item.modifiedShape;
                    placements.Add(new JObject { ["id"] = item.identifier, ["uid"] = item.uniqueId,
                        ["shape"] = JObject.FromObject(new { placedShape.minX, placedShape.minY, placedShape.width, placedShape.height,
                            placedShape.globalWidth, placedShape.globalHeight, placedShape.orientation, placedShape.flipped }) });
                }
            _forgeGoals[goal.Id]["capacityProof"] = new JObject { ["status"] = "complete-native-rehearsal-passed", ["sampleCount"] = samples.Count, ["placements"] = placements.DeepClone() };
        }
        finally
        {
            foreach (var sample in samples) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(sample);
            string afterData = Store.ModData().TryGetValue(ForgeAchievementKey, out string next) ? next : "";
            ForgeRequire("achievements.claim.capacity-fixture-cleanup." + goal.Id, "All rehearsal samples removed; original inventory, cash and achievement JSON unchanged", new { items = SnapshotItems(), cash = Store.playerCash, achievementJson = afterData },
                JToken.DeepEquals(before, SnapshotItems()) && Store.playerCash == cash && data == afterData && samples.All(sample => !Store.FindAllItem().Any(item => ReferenceEquals(item, sample))));
        }
    }
    private Button ForgeNode(ForgeGoal goal)
    {
        var expected = new Vector2(324 + goal.X * 250, -(310 + goal.Y * 225)); float size = goal.Id == "workshop_master" ? 80 : 48;
        var nodes = ForgeWindow()!.root.GetComponentsInChildren<Button>().Where(button => button.gameObject.activeInHierarchy && button.IsInteractable() && button.transform is RectTransform rect &&
            Vector2.Distance(rect.anchoredPosition, expected) < .5f && Mathf.Abs(rect.sizeDelta.x - size) < .5f && Mathf.Abs(rect.sizeDelta.y - size) < .5f).ToArray();
        ForgeRequire("achievements.page.unique-node." + goal.Id, new { expected, size, count = 1 }, nodes.Length, nodes.Length == 1); return nodes[0];
    }
    private Button ForgeClaimButton(bool claimed)
    {
        string[] labels = claimed ? new[] { "已领取", "Claimed" } : new[] { "领取奖励", "Claim reward" };
        var buttons = ForgeWindow()!.root.GetComponentsInChildren<Button>().Where(button => button.gameObject.activeInHierarchy && button.GetComponentsInChildren<TextMeshProUGUI>(true).Any(label => labels.Contains(label.text))).ToArray();
        if (buttons.Length != 1) throw new FixtureUnavailable("Actual Forge page has no unique " + (claimed ? "claimed" : "claim") + " button"); return buttons[0];
    }
    private JObject ForgeLedger() => new() { ["cash"] = Store.playerCash, ["uidGenerator"] = Store.currentUniqueId, ["state"] = ForgeState(),
        ["items"] = new JArray(ForgeOwned().OrderBy(item => item.uniqueId).Select(ForgeItem)), ["graph"] = SnapshotItems() };
    private IEnumerable<object?> ForgeVictoryBudgetFixture()
    {
        if ((string?)_forgeGoals["workshop_master"]["claim"] != "passed") yield break;
        var client = new StoreClient { identifier = "native-achievement-budget-fixture" };
        client.SetClientBudget(123);
        ForgeRequire("achievements.victory.native-budget-setter", 246, client.GetBudget(), client.GetBudget() == 246,
            "Explicit detached stock StoreClient API fixture: the real installed SetClientBudget postfix doubles a newly assigned budget. This is not a naturally arrived NPC sale.");
        client.ModBudget(-7); int remaining = client.GetBudget();
        bool opened = (bool)Reflect.Call(ForgeWorkshopUi, "OpenChain", ForgeAchievementChain)!; yield return null;
        ForgeRequire("achievements.victory.reopen-no-budget-refill", 239, new { opened, remaining, budget = client.GetBudget() }, opened && remaining == 239 && client.GetBudget() == 239);
        Reflect.Call(ForgeWorkshopUi, "CloseWindow");
        R.Skip("achievements.victory.natural-npc-sales-and-mortgage-ending", "Virtual badge and actual stock budget setter are checked; a naturally arrived NPC's doubled budget spending/affordability and separate buyout_mortgage playthrough remain not-tested. Next step: real buyer arrival -> actual sale -> remaining-budget ledger, plus independent mortgage slot.", false);
    }

    private static readonly string[] ForgeSavedTags = { "ANIMAL_WEIGHT_TAG", "ANIMAL_AGE_TAG", "ANIMAL_IS_DEAD_TAG", "ANIMAL_HUNGER_TAG", "ANIMAL_HEALTH_TAG", "ANIMAL_GROWTH_RATE_TAG_ALLELE_A", "ANIMAL_GROWTH_RATE_TAG_ALLELE_B", "ANIMAL_GROWTH_RATE_TAG_BONUS_PERCENT", "WINE_CONTAINER_STATE", "WINE_SCORE_INT", "WINE_YEAST_POWER_INT", "FERMENTATION_TIME_REMAINING_INT", "WINE_COUNTERFEIT_TAG", "AGE_INT", "INGOT_PURITY_TAG", "SCAV_SCANNER_DOUBLE_CHANCE", "LIQUID_CONTAINER_CAPACITY", "power_source_item_energy", "MODULE_UPGRADE_STAGE_INT", "TOTAL_PERCENTAGE_QUALITY_BONUS_INT", "TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT" };
    private static JObject ForgeItem(GameItem item) => ForgeStoredItem(item.identifier, item.uniqueId, item.unitCount, item.unitValue, item.state);
    private static JObject ForgeStoredItem(string id, int uid, int count, long value, TagSystem state)
    {
        var tags = new JObject();
        foreach (string key in ForgeSavedTags.Concat(Liquid.Liquids.Select(liquid => liquid.currentPart)).Distinct().OrderBy(key => key, StringComparer.Ordinal))
        {
            state.dict.TryGetValue(key, out var tag);
            tags[key] = JObject.FromObject(new { enabled = tag?.valueEnabled ?? false, integer = tag?.valueInt ?? 0, number = (double)(tag?.valueFloat ?? 0), doubleNumber = tag?.valueDouble ?? 0, text = tag?.GetString() ?? "" });
        }
        return new JObject { ["id"] = id, ["uid"] = uid, ["count"] = count, ["value"] = value, ["tags"] = tags };
    }
    private IEnumerable<object?> ForgeSaveReadback()
    {
        ForgeAction("actual-native-SaveGame-for-achievements"); Store.SaveGame();
        string path = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId)); var settings = new ES3Settings(path, ES3.Location.File);
        var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", settings)!;
        bool hasLive = Store.ModData().TryGetValue(ForgeAchievementKey, out string liveJson), hasSaved = saved.ModData().TryGetValue(ForgeAchievementKey, out string savedJson);
        ForgeRequire("achievements.save.actual-es3-state-and-cash", "Exact actual achievement JSON/run/slot/cash in ES3 carrier", new { hasLive, hasSaved, saved.runID, saved.saveSlotId, saved.playerCash },
            hasLive && hasSaved && liveJson == savedJson && saved.runID == Store.runID && saved.saveSlotId == Store.saveSlotId && saved.playerCash == Store.playerCash);
        int[] targetUids = _forgeReceipts.Concat(_forgeProofUids).Distinct().OrderBy(uid => uid).Where(uid => ForgeOwnedUnits(uid) > 0).ToArray();
        var nodes = new[] { saved.mainInvJSON, saved.backInvJSON }.SelectMany(json => JsonUtility.FromJson<SaveState>(json).saveItems).ToArray();
        var live = new JArray(targetUids.Select(uid => ForgeItem(ForgeOwned().Single(item => item.uniqueId == uid)))); var disk = new JArray();
        foreach (int uid in targetUids)
        {
            var matches = nodes.Where(node => node.uniqueId == uid).ToArray();
            ForgeRequire("achievements.save.unique-reward-or-proof-uid." + uid, 1, matches.Length, matches.Length == 1);
            var node = matches[0]; disk.Add(ForgeStoredItem(node.identifier, node.uniqueId, node.unitCount, node.unitValue, node.itemState));
        }
        ForgeRequire("achievements.save.actual-es3-rewards-proof-tags", live, disk, JToken.DeepEquals(live, disk), "Actual native ES3 bytes, including weight/age/quality/scanner/liquid tags; no JSON-only serialization roundtrip.");
        foreach (var goal in ForgeGoalInventory)
            if (!ForgeGoalHasFailures(_forgeGoals[goal.Id]) && (string?)_forgeGoals[goal.Id]["claim"] == "passed")
            { _forgeGoals[goal.Id]["save"] = "passed"; _forgeGoals[goal.Id]["status"] = "passed-save-only"; }
        R.persistence["forgeAchievements"] = new JObject { ["state"] = JObject.Parse(savedJson), ["items"] = disk, ["claims"] = _forgeClaims.DeepClone(),
            ["matrix"] = new JArray(ForgeGoalInventory.Select(goal => _forgeGoals[goal.Id].DeepClone())), ["cash"] = saved.playerCash,
            ["day"] = ES3.Load<StoreStation>("storeStation", settings).dayCounter, ["es3Path"] = path, ["es3Sha256AtReadback"] = Reflect.HashFile(path) };
        yield return null;
    }
    internal IEnumerable<object?> ForgeAchievementsReload(JObject baseline)
    {
        ForgeInitializeRows(); R.phase = "achievements-independent-process-readback";
        ForgeRequire("achievements.reload.steam-still-disabled", false, SteamManager.Initialized, !SteamManager.Initialized);
        var evidence = baseline["persistence"]?["forgeAchievements"] as JObject ?? throw new InvalidOperationException("Actual saved Forge achievement evidence missing");
        var expectedState = (JObject)evidence["state"]!.DeepClone();
        if (O.UseCopy) expectedState["SlotId"] = Store.saveSlotId;
        ForgeRequire("achievements.reload.exact-original-state", expectedState, ForgeState(), JToken.DeepEquals(expectedState, ForgeState()));
        ForgeRequire("achievements.reload.cash-and-day", new { cash = (int)evidence["cash"]!, day = (int)evidence["day"]! }, new { cash = Store.playerCash, day = StoreStation.GetDayCounter() }, Store.playerCash == (int)evidence["cash"]! && StoreStation.GetDayCounter() == (int)evidence["day"]!);
        var expectedItems = (JArray)evidence["items"]!; var actualItems = new JArray();
        foreach (var token in expectedItems)
        {
            int uid = (int)token["uid"]!; var matches = ForgeOwned().Where(item => item.uniqueId == uid).ToArray();
            ForgeRequire("achievements.reload.unique-native-owned-uid." + uid, 1, matches.Length, matches.Length == 1); actualItems.Add(ForgeItem(matches[0]));
        }
        ForgeRequire("achievements.reload.exact-rewards-and-qualifications", expectedItems, actualItems, JToken.DeepEquals(expectedItems, actualItems), "New process normal LoadGame restored the same real owned reward and proof UIDs with actual native tags.");
        foreach (var goal in ForgeGoalInventory)
        {
            var previous = ((JArray)evidence["matrix"]!).Single(row => (string?)row["id"] == goal.Id);
            _forgeGoals[goal.Id]["save"] = previous["save"]!.DeepClone();
            if ((string?)previous["claim"] != "passed") { ForgeSkip(goal.Id, "Save run did not execute this complete qualification/claim. Next step: " + ((string?)previous["reason"] ?? "complete the documented actual business path")); continue; }
            _forgeGoals[goal.Id]["qualification"] = previous["qualification"]!.DeepClone(); _forgeGoals[goal.Id]["claim"] = "passed";
            var pageBefore = ForgeLedger();
            bool opened = (bool)Reflect.Call(ForgeWorkshopUi, "OpenChain", ForgeAchievementChain)!; yield return null;
            var node = ForgeNode(goal); node.onClick.Invoke(); yield return null;
            ForgeRequire("achievements.reload.claimed-page-disabled." + goal.Id, "Visible page, same claimed record and disabled actual button", new { opened, state = ForgeState() },
                opened && ForgeWindow()?.IsVisible == true && !ForgeClaimButton(true).IsInteractable() && ((JArray)ForgeState()["Claimed"]!).Values<string>().Contains(goal.Id));
            var before = ForgeLedger();
            ForgeRequire("achievements.reload.claimed-page-preserves-rewards." + goal.Id, pageBefore, before,
                (int?)pageBefore["cash"] == (int?)before["cash"] && JToken.DeepEquals(pageBefore["state"], before["state"]) && JToken.DeepEquals(pageBefore["items"], before["items"]),
                "Page icons call the real ReadItem factory and may allocate a temporary prototype UID. Cash, achievement state and every owned item must remain exact; the duplicate Claim below separately compares the full ledger including the UID allocator in one frame.");
            Reflect.Call(ForgeWorkshop, "Claim", goal.Id);
            ForgeRequire("achievements.reload.no-repeat-reward." + goal.Id, before, ForgeLedger(), JToken.DeepEquals(before, ForgeLedger()));
            _forgeGoals[goal.Id]["page"] = "passed"; _forgeGoals[goal.Id]["reload"] = "passed"; _forgeGoals[goal.Id]["status"] = "passed-save-and-reload";
            foreach (object? wait in CaptureEvidenceFrame("native-achievement-reloaded-" + goal.Id)) yield return wait;
            Reflect.Call(ForgeWorkshopUi, "CloseWindow");
        }
        R.persistence["forgeAchievementsReload"] = new JObject { ["state"] = ForgeState(), ["items"] = actualItems, ["matrix"] = new JArray(ForgeGoalInventory.Select(goal => _forgeGoals[goal.Id])) };
        foreach (var spec in ForgeSteamSpecs) R.Skip("steam.account-unlock." + spec.Id, "Steam remains disabled. Save/reload validates Forge run achievements only; actual Steam account unlock/threshold statistics are not tested.", false);
    }

    private sealed class ForgeSteamSpec(string id, string check, string trigger)
    { internal readonly string Id = id, Check = check, Trigger = trigger; }
    private static readonly ForgeSteamSpec[] ForgeSteamSpecs =
    {
        new("ACH_TUTORIAL", "CheckTutorialComplete", "mentor tutorial final dialogue endAction"), new("ACH_BUYOUT", "CheckBuyout", "actual native BuyOutStore -> OnStoreOwned"),
        new("ACH_HONEST_MERCHANT", "CheckHonestMerchant", "OnStoreOwned, soldStolenOrContrabandThisRun=false"), new("ACH_AUTHORIZED", "CheckAuthorized", "actual paid business-permit dialogue transaction"),
        new("ACH_RATS", "CheckRats", "native RANCHER start initialization"), new("ACH_RAG_TO_RICHES", "CheckRagToRiches", "actual property acquisition after RockBottom start"),
        new("ACH_NOTHING_TO_RICHES", "CheckNothingToRiches", "actual property acquisition after Hard+ RockBottom start"), new("ACH_PERFECT_MEDIATOR", "CheckPerfectMediator", "actual native reputation change reaches one faction200"),
        new("ACH_LUCKY", "CheckLucky", "real ScratcherHelper.UpdateValue jackpot"), new("ACH_SETTLED", "CheckSettled", "actual start-of-day UI with wealth>10000"), new("ACH_RICH", "CheckRich", "actual start-of-day UI with wealth>100000"),
        new("ACH_LAB_PURE", "CheckLabPure", "real MachineChemFinisher finished-output button, lab_pure product"), new("ACH_HUGE_NUTRIFRUIT", "CheckHugeNutrifruit", "actual mature plant HarvestNutrifruit huge-output branch"),
        new("ACH_PRODIGIOUS", "CheckProdigious", "actual property acquisition before day50"), new("ACH_SHINY", "CheckShiny", "actual start-of-day/open-shutter with attractiveness>=1000"),
        new("ACH_OVERLOAD", "CheckOverload", "actual native module grid installation and recalculated quality>=250"), new("ACH_MODEL_CITIZEN", "CheckModelCitizen", "ten real native reported-criminal arrest/departure callbacks; account counter guarded"),
        new("ACH_AUG_HUNTER", "CheckAugHunter", "ten real StoreUIManager.Shoot calls on actual isAug customers; account counter guarded"), new("ACH_LOWER_LEVELS_SPECIAL", "CheckLowerLevelsSpecial", "actual native labeler dropdown fakeCondition/exposure/waterlabel transaction"),
        new("ACH_MASTER_APPRAISER", "CheckMasterAppraiser", "ten actual appraisal-service perfect submissions; account counter guarded"), new("ACH_INDUSTRIOUS", "CheckIndustrious", "actual start-of-day UI with >=10 owned MACHINE items")
    };
    // Read-only Harmony observers. Every prefix is void; no return value,
    // statsReady, guard, counter, flags, Steam function or original arguments
    // are replaced. The probe never invokes a Check or Unlock itself.
    private sealed class ForgeSteamObservation : IDisposable
    {
        private static ForgeSteamObservation? Active;
        private readonly ProbeContext _context;
        private readonly Harmony _hooks;
        private readonly List<MethodInfo> _patched = new();
        internal readonly JArray Calls = new(), Unlocks = new(), Counters = new(), Endings = new(), Errors = new();
        internal readonly JObject Evidence;
        internal ForgeSteamObservation(ProbeContext context)
        {
            if (Active != null) throw new InvalidOperationException("Steam observation already active");
            _context = context; _hooks = new Harmony("nicokobo.content_native_probe.achievements." + context.O.RunId);
            Evidence = new JObject { ["checkCalls"] = Calls, ["unlockEntries"] = Unlocks, ["guardedCounterEntries"] = Counters, ["endings"] = Endings, ["errors"] = Errors };
            try
            {
                foreach (var spec in ForgeSteamSpecs) Patch(typeof(AchievementTracker).GetMethod(spec.Check, Reflect.All)!, nameof(CheckPrefix));
                Patch(typeof(AchievementTracker).GetMethod("Unlock", Reflect.All)!, nameof(UnlockPrefix));
                Patch(typeof(AchievementTracker).GetMethod("IncrementCountAndUnlock", Reflect.All)!, nameof(CounterPrefix));
                Patch(typeof(PlayerStore).GetMethod(nameof(PlayerStore.ExecuteGameOver), new[] { typeof(string) })!, nameof(EndingPrefix));
                Active = this;
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodInfo method, string prefix)
        { if (method == null) throw new InvalidOperationException("Expected native Steam observation target missing"); _hooks.Patch(method, prefix: new HarmonyMethod(typeof(ForgeSteamObservation), prefix)); _patched.Add(method); }
        private void Record(JArray rows, string method, object? value)
        {
            try
            {
                var trace = new System.Diagnostics.StackTrace();
                string[] callers = trace.GetFrames().Select(frame => frame.GetMethod()).Where(caller => caller != null && caller.DeclaringType != typeof(ForgeSteamObservation)).Take(8)
                    .Select(caller => caller!.DeclaringType?.FullName + "." + caller.Name).ToArray();
                rows.Add(JObject.FromObject(new { method, frame = Time.frameCount, day = StoreStation.GetDayCounter(), steamInitialized = SteamManager.Initialized,
                    statsReady = AchievementTracker.Instance == null ? null : Reflect.Get(AchievementTracker.Instance, "statsReady"), value, callers }));
            }
            catch (Exception error) { Errors.Add(error.ToString()); }
        }
        private static void CheckPrefix(MethodBase __originalMethod, object[] __args)
        {
            if (Active is not { } observer) return;
            observer.Record(observer.Calls, __originalMethod.Name, __args.Select(arg => arg is GameItem item ? (object)new { item.identifier, item.uniqueId } : arg).ToArray());
        }
        private static void UnlockPrefix(string __0) { if (Active is { } observer) observer.Record(observer.Unlocks, "Unlock", new { id = __0 }); }
        private static void CounterPrefix(string __0, int __1, string __2) { if (Active is { } observer) observer.Record(observer.Counters, "IncrementCountAndUnlock", new { stat = __0, threshold = __1, id = __2 }); }
        private static void EndingPrefix(string __0)
        { if (Active is { } observer) observer.Endings.Add(JObject.FromObject(new { ending = __0, day = StoreStation.GetDayCounter(), cash = observer._context.Store.playerCash, propertyPaid = observer._context.Store.IsPropertyPaid })); }
        internal void WriteRows()
        {
            var rows = new JArray();
            foreach (var spec in ForgeSteamSpecs)
            {
                var checks = Calls.Where(row => (string?)row["method"] == spec.Check).ToArray();
                var unlocks = Unlocks.Where(row => (string?)row["value"]?["id"] == spec.Id).ToArray();
                var counters = Counters.Where(row => (string?)row["value"]?["id"] == spec.Id).ToArray();
                rows.Add(new JObject { ["id"] = spec.Id, ["check"] = spec.Check, ["requiredActualBusinessTrigger"] = spec.Trigger,
                    ["actualCheckCalls"] = new JArray(checks), ["actualUnlockEntries"] = new JArray(unlocks), ["guardedCounterEntries"] = new JArray(counters),
                    ["businessObservation"] = checks.Length == 0 ? "not-tested" : "observed-real-business-call", ["eligibilityObservation"] = unlocks.Length > 0 ? "actual-Check-called-Unlock" : "not-observed-or-account-stat-guard", ["accountUnlock"] = "not-tested" });
                if (checks.Length == 0) _context.R.Skip("steam.business-trigger." + spec.Id, "Not reached in this exact native scenario. Next step: " + spec.Trigger + ". Do not call its Check/Unlock directly.", false);
                _context.R.Skip("steam.account-unlock." + spec.Id, "Steam disabled; actual Check/Unlock entries above are passive business observations only. No account login, SetAchievement, StoreStats or stat-counter writes are performed by the probe.", false);
            }
            _context.R.environment["steamAchievementMatrix"] = rows;
            _context.R.Check("steam.observer-guard-preserved", "Steam remains uninitialized; passive observer has no errors", new { initialized = SteamManager.Initialized, errors = Errors, steamEnabledCalls = Calls.Concat(Unlocks).Count(row => (bool?)row["steamInitialized"] == true) },
                !SteamManager.Initialized && Errors.Count == 0 && !Calls.Concat(Unlocks).Any(row => (bool?)row["steamInitialized"] == true));
        }
        public void Dispose()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            foreach (var method in _patched) _hooks.Unpatch(method, HarmonyPatchType.All, _hooks.Id);
        }
    }
}
