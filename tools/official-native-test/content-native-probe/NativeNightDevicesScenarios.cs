using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private static readonly string[] NdxDeviceIds = { "powerblock", "makeshift_reactor", "security_alarm", "mirage_projector", "wine_rack", "fridge" };
    private readonly List<GameItem> _ndxRoots = new();
    private readonly Dictionary<string, GameItem> _ndxItems = new(StringComparer.Ordinal);
    private readonly HashSet<int> _ndxPromised = new();
    private readonly Dictionary<int, (string Id, int Count)> _ndxIdentity = new();
    private readonly Dictionary<int, JObject> _ndxLiquidBefore = new();
    private readonly JArray _ndxFixtures = new(), _ndxNights = new(), _ndxDevices = new();

    // This is a separate, bounded family. Routing, candidate construction and
    // process supervision belong to the runner, not to this partial class.
    internal IEnumerable<object?> NativeNightDevices()
    {
        R.phase = "nightdevices-native-fixture";
        R.environment["nativeNightDevicesScope"] = "Build26317870; six native device types, five ordinary APPRENTICE complete nights at timeScale 1, native inventory geometry and naturally fermented wine. Registered factories, pure water, initial battery energy and NetworkUpgrade.Unlock are declared preparation grants, not economic or achievement acquisition. Harmony observers never change arguments, results, state or callbacks. No day, temperature, age, integrity, fermentation completion or output is assigned. Programmatic native UI/API actions do not prove physical keyboard/mouse operation.";
        R.environment["nativeNightDevicesFixtures"] = _ndxFixtures;
        R.environment["nativeNightDevicesNights"] = _ndxNights;
        R.environment["nativeNightDevicesDevices"] = _ndxDevices;
        foreach (string id in NdxDeviceIds) _ndxDevices.Add(new JObject { ["id"] = id, ["activeWork"] = "not-tested" });
        NdxRequire("nightdevices.factory-denominator", NdxDeviceIds, NdxDeviceIds.Where(id => !DirectoryMaster.Has<GameItem>(id)).ToArray(),
            NdxDeviceIds.Length == 6 && NdxDeviceIds.All(DirectoryMaster.Has<GameItem>), "Registration is a fixture prerequisite; activeWork remains not-tested until actual native nights finish.");
        NdxRequire("nightdevices.ordinary-start", "APPRENTICE without starting perks, normal difficulty and devMode false",
            new { NewGameData.Instance.startType, perks = NewGameData.Instance.startingPerks.ToArray(), Store.devMode, difficulty = NewGameData.Instance.difficultyId },
            NewGameData.Instance.startType == NewGameData.StartType.APPRENTICE && NewGameData.Instance.startingPerks.Count == 0 && !Store.devMode && NewGameData.Instance.difficultyId == "normal");
        int initialCash = Store.playerCash;
        Store.ModCash(20000 - initialCash, false);
        R.environment["nativeNightDevicesFixtureFunds"] = new { initialCash, fixtureCash = Store.playerCash,
            reason = "Explicit test funds for actual nightly service invoices; no customer/economic acquisition claim" };
        NdxSetup();
        // Native berry/yeast consumption was proved during setup. Promise all
        // remaining original fixtures now, before the first night can lose one.
        foreach (GameItem root in _ndxRoots)
            foreach (GameItem item in new[] { root }.Concat(root.FindAllChildItems()).Distinct())
            { _ndxPromised.Add(item.uniqueId); _ndxIdentity[item.uniqueId] = (item.identifier, item.unitCount); }
        UnlockFixtureUpgrade("POWER_HIJACK");
        R.environment["nativeNightDevicesUpgradeGrants"] = new JArray("POWER_HIJACK before night 1", "COMMERCIAL_POWER before night 2 (including its native prerequisites)");
        if (NetworkUpgrade.IsCommercialPowerUnlocked())
            R.Skip("nightdevices.powerblock-basic-two-rate", "COMMERCIAL_POWER was already legitimately unlocked before the first tested night. The observed four-per-source commercial branch is tested; the two-per-source branch is not forced by changing upgrade state.", false);
        Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
        NdxRequire("nightdevices.normal-speed", 1f, Time.timeScale, Time.timeScale == 1f);
        int firstDay = StoreStation.GetDayCounter();
        var thermalUids = new HashSet<int>(new[] { _ndxItems["chillLiquid"].uniqueId, _ndxItems["freezeLiquid"].uniqueId, _ndxItems["offLiquid"].uniqueId });
        using (var observer = new NdxObservation(this, thermalUids))
        {
            R.environment["nativeNightDevicesObserver"] = observer.Evidence;
            foreach (object? wait in NdxWaitTicks(observer, "initial-two-actual-seconds", 2)) yield return wait;
            NdxRequire("nightdevices.fridge-real-one-second-cooling", "At least two independently checked genuine native cooling calls for each powered zone, none for unpowered fridge",
                observer.Evidence, observer.ValidCoolCount(_ndxItems["chillLiquid"].uniqueId) >= 2 && observer.ValidCoolCount(_ndxItems["freezeLiquid"].uniqueId) >= 2 &&
                observer.CoolCount(_ndxItems["offLiquid"].uniqueId) == 0 && observer.Errors.Count == 0,
                "EmporiumEntry.OneSecPrefixFunction supplies a real Update/Time.deltaTime tick witness. CoolZoneContainers prefix/postfix observes the original method. Expected clamp is independently +/-0.5 toward 3 C and +/-1 toward -18 C; no direct method or callback invocation.");
            for (int night = 1; night <= 5; night++)
            {
                if (night == 2)
                {
                    UnlockFixtureUpgrade("COMMERCIAL_POWER");
                    Place(NdxPort(_ndxItems["rackOn"], 2), _ndxItems["wineOn"], "nightdevices-natural-wine-to-powered-rack", _ndxItems["rackOn"]);
                    Place(NdxPort(_ndxItems["rackOff"], 2), _ndxItems["wineOff"], "nightdevices-natural-wine-to-unpowered-rack", _ndxItems["rackOff"]);
                }
                // A real zone move exercises spirit iceCold/chilled replacement.
                if (night == 2) Place(MachineFridge.GetFreezeZone(_ndxItems["fridgeOn"]), _ndxItems["spirit"], "nightdevices-spirit-to-freezer");
                if (night == 3) Place(MachineFridge.GetChillZone(_ndxItems["fridgeOn"]), _ndxItems["spirit"], "nightdevices-spirit-back-to-chiller");
                int day = StoreStation.GetDayCounter();
                NdxRequire("nightdevices.day-before." + night, firstDay + night - 1, day, day == firstDay + night - 1);
                var before = NdxLive(_ndxPromised, "night-" + night + "-before");
                int sharedBefore = Energy(_ndxItems["sharedCell"]), reactorBefore = Energy(_ndxItems["reactorCell"]);
                int[] energies = NdxEnergy();
                bool commercial = NetworkUpgrade.IsCommercialPowerUnlocked();
                int alarmCallsBefore = observer.AlarmCalls.Count;
                observer.Night = night;
                var row = new JObject { ["number"] = night, ["dayBefore"] = day, ["commercialPower"] = commercial,
                    ["itemsBefore"] = before, ["energyBefore"] = new JArray(energies), ["alarmOperateBefore"] = alarmCallsBefore };
                _ndxNights.Add(row);
                using (var observation = new NightObservation(this, Array.Empty<RecipeFixture>(), day))
                {
                    row["nativeNight"] = observation.Evidence;
                    foreach (object? wait in NightDay(day, "nightdevices.night" + night, observation, ensureInsurance: true)) yield return wait;
                }
                row["dayAfter"] = StoreStation.GetDayCounter(); row["energyAfter"] = new JArray(NdxEnergy());
                row["itemsAfter"] = NdxLive(_ndxPromised, "night-" + night + "-after");
                NdxCheckNight(night, sharedBefore, reactorBefore, energies, commercial, observer, alarmCallsBefore);
                observer.Night = 0;
                foreach (object? wait in CaptureEvidenceFrame("nightdevices-real-night-" + night)) yield return wait;
            }
            NdxRequire("nightdevices.five-complete-days", firstDay + 5, StoreStation.GetDayCounter(), StoreStation.GetDayCounter() == firstDay + 5 && _ndxNights.Count == 5);
            // Stop refrigeration by a genuine battery-slot move. Allow the
            // native chemical tick to clear HEATING_TAG before committing the
            // final immutable UID denominator. Liquids remain saved and owned.
            Place(NdxPage(_ndxItems["fridgeStage"], 1), _ndxItems["fridgeOnCell"], "nightdevices-remove-fridge-battery-for-persistence");
            int coolingBefore = observer.TotalCoolingCalls;
            foreach (object? wait in NdxWaitTicks(observer, "battery-removed-two-actual-seconds", 2)) yield return wait;
            NdxRequire("nightdevices.fridge-battery-removal-stops-cooling", new { newCoolingCalls = 0, heating = false },
                new { calls = observer.TotalCoolingCalls - coolingBefore, liquids = thermalUids.Select(uid => NdxItem(_ndxItems.Values.Single(item => item.uniqueId == uid))).ToArray() },
                observer.TotalCoolingCalls == coolingBefore && thermalUids.All(uid => !_ndxItems.Values.Single(item => item.uniqueId == uid).IsTag("HEATING_TAG")),
                "No timer, temperature or heating flag is assigned; the actual native one-second callback clears the prior heating marker with the battery physically outside its port.");
            NdxRequire("nightdevices.observers-have-no-errors", 0, observer.Errors, observer.Errors.Count == 0);
            if (observer.AlarmCalls.Count == 0)
                R.Skip("nightdevices.alarm-transmitter-insurance-effect", "Native HandleInsurance never called MachineAlarm.Operate within these five nights (insurance locked/grace/protection branches are recorded). Per-device real early-cycle power/POWERED checks still execute. No direct Operate call or invented reputation result is substituted.", false);
            else
                NdxRequire("nightdevices.alarm-transmitter-insurance-effect", "At least one actual insurance-triggered Operate with exact normal-difficulty +1 faction reputation (clamped at 200)", observer.AlarmCalls,
                    observer.AlarmCalls.All(call => (bool?)call["valid"] == true));
            R.Skip("nightdevices.reactor-radiation", "Not applicable in Build26317870: ReactorHelper.Create/OnNight/ChargeAdjacent/repair/upgrade implement integrity, waste, power and explosion only. ReactorHelper contains no radiation/health damage call and PlayerStore's reactor call sites do not add one. No health value is fabricated.", false);
            R.Skip("nightdevices.reactor-natural-waste-output-160", "Five unmodified base-reactor nights accumulate 18*5=90 damage/waste progress. The native 160 threshold is not reached; no nuclear_waste production pass is claimed and no extra nights or repairs are added solely to manufacture it.", false);
            R.Skip("nightdevices.random-burglary-outcome", "POWERED and native Operate effects are observed; a particular RNG burglary/prevention outcome is not guaranteed or forced. Full geometry/upgrades/blackout/difficulty/random-event combinations remain not-tested.", false);
            foreach (JObject device in _ndxDevices) device["activeWork"] = "five-real-native-nights";
            // No yield may be added between this snapshot and the runner's
            // Save(): temperature is deliberately still live and is bound to
            // the original encoded ES3 node during the separate reload.
            var items = NdxLive(_ndxPromised, "before-save");
            R.persistence["nativeNightDevices"] = new JObject { ["schema"] = 1, ["completedNights"] = 5, ["dayBefore"] = firstDay,
                ["dayAfter"] = StoreStation.GetDayCounter(), ["gameRunId"] = Store.runID, ["uids"] = new JArray(_ndxPromised.OrderBy(uid => uid)),
                ["items"] = items, ["thermalUids"] = new JArray(thermalUids.OrderBy(uid => uid)), ["roles"] = JObject.FromObject(_ndxItems.ToDictionary(pair => pair.Key, pair => pair.Value.uniqueId)),
                ["devices"] = _ndxDevices.DeepClone(), ["nights"] = _ndxNights.DeepClone(), ["coverageComplete"] = false };
        }
    }

    private void NdxSetup()
    {
        foreach (string group in new[] { "power", "reactor", "alarm", "projector", "rack", "fridge" })
        {
            GameItem stage = Spawn("hypertech_bay"); Retain(stage); _ndxRoots.Add(stage); _ndxItems[group + "Stage"] = stage;
            _ndxFixtures.Add(new JObject { ["role"] = group + "Stage", ["grant"] = "unmodified native factory; legal ordinary retained inventory", ["item"] = NdxItem(stage) });
        }
        GameGridInventory power = NdxPage(_ndxItems["powerStage"], 0);
        GameItem a = NdxBorn("powerblock", "powerA", power), b = NdxBorn("powerblock", "powerB", NdxPage(_ndxItems["powerStage"], 1));
        GameItem cell = NdxCell("sharedCell", 0, NdxPage(_ndxItems["powerStage"], 1));
        NdxRequire("nightdevices.powerblock-legal-A-position", true, NdxItem(a), NdxAt(power, a, 0, 0));
        NdxRequire("nightdevices.powerblock-legal-shared-cell-position", true, NdxItem(cell), NdxAt(power, cell, a.modifiedShape.width, 0));
        NdxRequire("nightdevices.powerblock-legal-B-position", true, NdxItem(b), NdxAt(power, b, a.modifiedShape.width + cell.modifiedShape.width, 0));
        NdxRequire("nightdevices.two-real-adjacencies", "Both native powerblocks find the same actual battery UID", new { a = a.FindAdjacentType<GameItem>().Select(item => item.uniqueId).ToArray(), b = b.FindAdjacentType<GameItem>().Select(item => item.uniqueId).ToArray(), cell = cell.uniqueId },
            a.FindAdjacentType<GameItem>().Contains(cell) && b.FindAdjacentType<GameItem>().Contains(cell));
        NdxCell("farCell", 0, NdxPage(_ndxItems["powerStage"], 1));
        GameGridInventory reactorGrid = NdxPage(_ndxItems["reactorStage"], 0);
        GameItem reactor = NdxBorn("makeshift_reactor", "reactor", reactorGrid);
        GameItem reactorCell = NdxCell("reactorCell", 0, NdxPage(_ndxItems["reactorStage"], 1));
        NdxRequire("nightdevices.reactor-legal-position", true, NdxItem(reactor), NdxAt(reactorGrid, reactor, 0, 0));
        NdxRequire("nightdevices.reactor-cell-legal-position", true, NdxItem(reactorCell), NdxAt(reactorGrid, reactorCell, reactor.modifiedShape.width, 0));
        NdxRequire("nightdevices.reactor-real-adjacency-and-factory-state", new { integrity = 100, max = 100, upgrades = 0, waste = 0, adjacent = true }, NdxItem(reactor),
            NdxInt(reactor, "REACTOR_INTEGRITY_INT") == 100 && NdxInt(reactor, "REACTOR_MAX_INTEGRITY_INT") == 100 && NdxInt(reactor, "REACTOR_UPGRADES_INT") == 0 && NdxInt(reactor, "REACTOR_WASTE_PROGRESS_INT") == 0 && reactor.FindAdjacentType<GameItem>().Contains(reactorCell));
        foreach (var device in new[] { (Id: "security_alarm", Group: "alarm", Cost: 4, Energy: 100), (Id: "mirage_projector", Group: "projector", Cost: 16, Energy: 16), (Id: "wine_rack", Group: "rack", Cost: 24, Energy: 100), (Id: "fridge", Group: "fridge", Cost: 2, Energy: 100) })
        {
            foreach (string suffix in new[] { "On", "Off" })
            {
                GameItem machine = NdxBorn(device.Id, device.Group + suffix, NdxPage(_ndxItems[device.Group + "Stage"], 0));
                NdxCell(device.Group + suffix + "Cell", suffix == "On" ? device.Energy : 0, NdxPort(machine, 0), machine);
                NdxRequire("nightdevices.factory-power-cost." + device.Group + suffix, device.Cost, MachineryHelper.GetMachinePowerUsage(machine), MachineryHelper.GetMachinePowerUsage(machine) == device.Cost);
            }
        }
        GameItem transmitter = NdxBorn("alarm_module_transmitter", "transmitter", NdxPort(_ndxItems["alarmOn"], 1), _ndxItems["alarmOn"]);
        NdxRequire("nightdevices.alarm-transmitter-native-target", "FACTION_SECURITY", NdxString(transmitter, "TRANSMITTER_TARGET_TAG"), NdxString(transmitter, "TRANSMITTER_TARGET_TAG") == "FACTION_SECURITY");
        NdxRequire("nightdevices.alarm-first-selection", _ndxItems["alarmOn"].uniqueId, MachineAlarm.FindAlarmeItem()?.uniqueId, MachineAlarm.FindAlarmeItem() == _ndxItems["alarmOn"],
            "Native insurance selects the first owned non-broken alarm. An unexpected pre-existing alarm is a required fixture failure, not an assumed transmitter effect.");
        NdxRequire("nightdevices.alarm-transmitter-native-efficiency-cost", 6, MachineryHelper.GetMachinePowerUsage(_ndxItems["alarmOn"]), MachineryHelper.GetMachinePowerUsage(_ndxItems["alarmOn"]) == 6,
            "Native transmitter has performance 0, efficiency -50, quality 0: independent 4-4*(-50)/100=6 energy; stop rate remains 50. Its default target is Security.");
        GameItem emptyRack = NdxBorn("wine_rack", "rackEmpty", NdxPage(_ndxItems["rackStage"], 1));
        NdxCell("rackEmptyCell", 60, NdxPort(emptyRack, 0), emptyRack);
        foreach (string role in new[] { "wineOn", "wineOff" }) NdxStartWine(role);
        GameItem invalidWine = NdxBorn("wine_bottle", "invalidWine", NdxPage(_ndxItems["rackStage"], 1));
        JObject invalidBefore = NdxItem(invalidWine);
        SlotMarker? invalidSlot = NdxPort(_ndxItems["rackEmpty"], 2).TryFindOneValidInventorySlot(invalidWine);
        NdxRequire("nightdevices.rack-rejects-unfermented-wine", "No legal slot; unchanged native empty-bottle UID/state", new { slotValid = invalidSlot?.IsValid() == true, item = NdxItem(invalidWine) },
            (invalidSlot == null || !invalidSlot.IsValid()) && JToken.DeepEquals(invalidBefore, NdxItem(invalidWine)));
        NdxUpgradeFridgeForFixtures();
        NdxBorn("energy_drink", "chillFood", MachineFridge.GetChillZone(_ndxItems["fridgeOn"]));
        NdxBorn("nudka", "spirit", MachineFridge.GetChillZone(_ndxItems["fridgeOn"]));
        NdxBorn("beis_icecream", "icecream", MachineFridge.GetFreezeZone(_ndxItems["fridgeOn"]));
        foreach (var entry in new[] { (Role: "chillLiquid", Zone: MachineFridge.GetChillZone(_ndxItems["fridgeOn"])), (Role: "freezeLiquid", Zone: MachineFridge.GetFreezeZone(_ndxItems["fridgeOn"])), (Role: "offLiquid", Zone: MachineFridge.GetChillZone(_ndxItems["fridgeOff"])) })
        {
            GameItem liquid = NdxBorn("1ml_dropper", entry.Role, entry.Zone);
            WaterHelper.AddLiquid(liquid, "water", 1000);
            NdxRequire("nightdevices.pure-water-grant." + entry.Role, "1000 water and zero of every other native liquid species", FlLiquid(liquid), WaterHelper.GetTotalVolume(liquid) == 1000 &&
                Liquid.Liquids.All(part => NdxInt(liquid, part.currentPart) == (part.identifier == "water" ? 1000 : 0)),
                "Explicit liquid ingredient fixture through native AddLiquid. AddPureWater delegates to randomized AddWater and is deliberately not used as a deterministic composition oracle.");
            _ndxLiquidBefore[liquid.uniqueId] = FlLiquid(liquid);
        }
        NdxBorn("energy_drink", "offFood", MachineFridge.GetChillZone(_ndxItems["fridgeOff"]));
        GameItem emptyFridge = NdxBorn("fridge", "fridgeEmpty", NdxPage(_ndxItems["fridgeStage"], 1));
        NdxCell("fridgeEmptyCell", 60, NdxPort(emptyFridge, 0), emptyFridge);
        // Use another native page for the icecream after its first cold night,
        // leaving space for the spirit's legal freezer move on night two.
        NdxRequire("nightdevices.native-fridge-classifications", "Native beverage, spirit and freezable factory representatives", new { chill = _ndxItems["chillFood"].identifier, spirit = _ndxItems["spirit"].identifier, freeze = _ndxItems["icecream"].identifier },
            MachineFridge.IsChillable(_ndxItems["chillFood"]) && MachineFridge.IsSpirit(_ndxItems["spirit"]) && MachineFridge.IsFreezable(_ndxItems["icecream"]));
    }

    private void NdxUpgradeFridgeForFixtures()
    {
        GameItem fridge = _ndxItems["fridgeOn"];
        foreach (var upgrade in new[] { (Id: "scrap_metal", Tag: "FRIDGE_STORAGE_UPGRADE_COUNT_TAG"), (Id: "common_electronic", Tag: "FRIDGE_POWER_UPGRADE_COUNT_TAG") })
        {
            for (int level = 1; level <= 2; level++)
            {
                GameItem material = Spawn(upgrade.Id); Retain(material);
                int uid = material.uniqueId, cash = Store.playerCash;
                long value = fridge.unitValue, materialValue = material.unitValue;
                JObject before = NdxItem(material);
                bool may = material.MayTarget(fridge), can = material.CanTarget(fridge);
                NdxRequire("nightdevices.fridge-upgrade-admission." + upgrade.Id + "." + level,
                    "Original owned one-unit material has actual native MayTarget and CanTarget", new { may, can, item = before },
                    uid > 0 && material.unitCount == 1 && GeneralHelper.IsItemOwned(material) && may && can);
                material.Target(fridge);
                int storage = NdxInt(fridge, "FRIDGE_STORAGE_UPGRADE_COUNT_TAG"), efficiency = NdxInt(fridge, "FRIDGE_POWER_UPGRADE_COUNT_TAG");
                int expectedCost = Math.Max(2, 2 + 2 * storage - 2 * efficiency);
                bool consumed = !Store.FindAllItem(false).Any(item => item.uniqueId == uid);
                NdxRequire("nightdevices.fridge-real-material-upgrade." + upgrade.Id + "." + level,
                    new { level, consumed = true, cash, value = value + materialValue, expectedCost },
                    new { level = NdxInt(fridge, upgrade.Tag), consumed, cash = Store.playerCash, value = fridge.unitValue, power = MachineryHelper.GetMachinePowerUsage(fridge) },
                    NdxInt(fridge, upgrade.Tag) == level && consumed && Store.playerCash == cash && fridge.unitValue == value + materialValue && MachineryHelper.GetMachinePowerUsage(fridge) == expectedCost);
                _ndxFixtures.Add(new JObject { ["purpose"] = "Actual consumed native material target; no upgrade counter or geometry assignment", ["material"] = before,
                    ["consumedUid"] = uid, ["after"] = NdxItem(fridge) });
            }
        }
        var chill = MachineFridge.GetChillZone(fridge); var freeze = MachineFridge.GetFreezeZone(fridge);
        NdxRequire("nightdevices.fridge-upgraded-legal-fixture-space-and-cost", new { storage = 2, efficiency = 2, cost = 2, chillWidth = 5, freezeWidth = 3, height = 4 },
            new { storage = NdxInt(fridge, "FRIDGE_STORAGE_UPGRADE_COUNT_TAG"), efficiency = NdxInt(fridge, "FRIDGE_POWER_UPGRADE_COUNT_TAG"), cost = MachineryHelper.GetMachinePowerUsage(fridge), chillWidth = chill.inventoryShape.width, freezeWidth = freeze.inventoryShape.width, chillHeight = chill.inventoryShape.height, freezeHeight = freeze.inventoryShape.height },
            NdxInt(fridge, "FRIDGE_STORAGE_UPGRADE_COUNT_TAG") == 2 && NdxInt(fridge, "FRIDGE_POWER_UPGRADE_COUNT_TAG") == 2 && MachineryHelper.GetMachinePowerUsage(fridge) == 2 && chill.inventoryShape.width == 5 && chill.inventoryShape.height == 4 && freeze.inventoryShape.width == 3 && freeze.inventoryShape.height == 4);
    }

    private void NdxStartWine(string role)
    {
        GameItem bottle = NdxBorn("wine_bottle", role, NdxPage(_ndxItems["rackStage"], 1));
        GameInventory ingredients = GraphUtils.FindAllChildrenType<GameInventory>(bottle).First();
        GameItem berry = NdxBorn("wine_berry", role + "Berry", ingredients, bottle);
        GameItem yeast = NdxBorn("wine_superyeast", role + "Yeast", ingredients, bottle);
        int condition = NdxInt(berry, "BERRY_CONDITION_INT");
        int expectedScore = (condition < 0 ? condition + 1 : condition) + 1 + 1;
        WaterHelper.AddLiquid(bottle, "water", 750000);
        NdxRequire("nightdevices.wine-raw-input." + role, new { pureWaterParts = 750000, yeastPower = 4 }, new { bottle = NdxItem(bottle), berry = NdxItem(berry), yeast = NdxItem(yeast) },
            WaterHelper.GetTotalVolume(bottle) == 750000 && Liquid.Liquids.All(part => NdxInt(bottle, part.currentPart) == (part.identifier == "water" ? 750000 : 0)) &&
            NdxInt(yeast, "WINE_YEAST_POWER_INT") == 4 && NdxInt(bottle, "WINE_CONTAINER_STATE") == 0);
        bool started = WineHelper.TryStartFermentation(bottle);
        bool consumed = !Store.FindAllItem(false).Any(item => item.uniqueId == berry.uniqueId || item.uniqueId == yeast.uniqueId);
        NdxRequire("nightdevices.wine-native-start-consumption." + role, new { state = 1, remaining = 4, yeastPower = 4, expectedScore, consumed = true, water = 0 }, NdxItem(bottle),
            started && consumed && NdxInt(bottle, "WINE_CONTAINER_STATE") == 1 && NdxInt(bottle, "FERMENTATION_TIME_REMAINING_INT") == 4 &&
            NdxInt(bottle, "WINE_YEAST_POWER_INT") == 4 && NdxInt(bottle, "WINE_SCORE_INT") == expectedScore && WaterHelper.GetTotalVolume(bottle) == 0 && !bottle.IsTag("WINE_COUNTERFEIT_TAG"),
            "Same public API as the native Start Fermenting button. Actual factories supply one random-condition berry and superyeast; independent score is adjusted observed berry condition + yeast 1 + pure-water 1. Inputs are consumed by native StartFermentation, never by the probe. FinishFermentation is not called here.");
    }

    private int[] NdxEnergy() => new[] { "alarmOnCell", "alarmOffCell", "projectorOnCell", "projectorOffCell", "rackOnCell", "rackOffCell", "rackEmptyCell", "fridgeOnCell", "fridgeOffCell", "fridgeEmptyCell" }.Select(role => Energy(_ndxItems[role])).ToArray();

    private void NdxCheckNight(int night, int sharedBefore, int reactorCellBefore, int[] energyBefore, bool commercial, NdxObservation observation, int alarmCallsBefore)
    {
        string p = "nightdevices.night" + night + ".";
        int amount = commercial ? 4 : 2;
        var charges = observation.Charges.OfType<JObject>().Where(row => (int?)row["night"] == night).ToArray();
        var shared = charges.Where(row => (int?)row["uid"] == _ndxItems["sharedCell"].uniqueId).ToArray();
        NdxRequire(p + "powerblock-two-sources-one-adjacent-cell", new { calls = 2, amountEach = amount, after = sharedBefore + amount * 2, far = 0 }, new { shared, actual = Energy(_ndxItems["sharedCell"]), far = Energy(_ndxItems["farCell"]), Store.isPowerOn },
            Store.isPowerOn && shared.Length == 2 && shared.All(row => (int?)row["amount"] == amount && (int?)row["after"] - (int?)row["before"] == amount) &&
            Energy(_ndxItems["sharedCell"]) == sharedBefore + amount * 2 && Energy(_ndxItems["farCell"]) == 0,
            "Both original early-cycle closures execute through EndNight against native same-page adjacency. A second-page battery is a real nonadjacent control. Recharge sums instead of being inferred from registration.");
        int integrity = 100 - 18 * night, charge = integrity > 25 ? 1 : 0;
        var reactorCharges = charges.Where(row => (int?)row["uid"] == _ndxItems["reactorCell"].uniqueId).ToArray();
        NdxRequire(p + "reactor-integrity-waste-and-adjacent-energy", new { integrity, waste = 18 * night, charge, energy = reactorCellBefore + charge }, new { reactor = NdxItem(_ndxItems["reactor"]), actualEnergy = Energy(_ndxItems["reactorCell"]), reactorCharges },
            NdxInt(_ndxItems["reactor"], "REACTOR_INTEGRITY_INT") == integrity && NdxInt(_ndxItems["reactor"], "REACTOR_WASTE_PROGRESS_INT") == 18 * night &&
            Energy(_ndxItems["reactorCell"]) == reactorCellBefore + charge && reactorCharges.Length == charge && reactorCharges.All(row => (int?)row["amount"] == 1),
            "Independent base constants 100 integrity, 18 decay, strict >25 power threshold, one adjacent charge and 160 waste-output threshold. Fifth natural night verifies 28->10 stops charging. No health/radiation assertion exists for this Build.");
        int[] expected = { energyBefore[0] - 6, 0, night == 1 ? 0 : energyBefore[2], 0, energyBefore[4] - (night == 1 ? 0 : 24), 0, 60, energyBefore[7] - 2, 0, 60 };
        NdxRequire(p + "native-exact-energy-and-idle-controls", expected, NdxEnergy(), expected.SequenceEqual(NdxEnergy()),
            "Alarm with native transmitter 6 (base4, efficiency-50), projector one-shot16, genuine wines only after night1 then rack24, populated fridge2. Unpowered counterparts and physically empty powered rack/fridge debit zero. Batteries are unchanged original UIDs.");
        int[] nightlyCooling = new[] { "chillLiquid", "freezeLiquid", "offLiquid" }.Select(role => observation.SourceCount(night, "native-nightly-chemical-360", _ndxItems[role].uniqueId)).ToArray();
        NdxRequire(p + "fridge-exact-360-native-nightly-cooling-iterations", new[] { 360, 360, 0 }, nightlyCooling,
            nightlyCooling.SequenceEqual(new[] { 360, 360, 0 }),
            "Independent Build26317870 RunNightlyChemicalSimulation loop bound is exactly 360. Both original powered-zone liquid witnesses must receive 360 checked original CoolZoneContainers executions in this actual night; the powerless witness must receive none. Initial real-time ticks and end-of-night features cannot substitute for missing nightly work.");
        NdxRequire(p + "alarm-real-cycle-and-no-power-control", new { on = true, off = false, stopRate = 50 }, new { on = _ndxItems["alarmOn"].IsTag("POWERED"), off = _ndxItems["alarmOff"].IsTag("POWERED"), rate = MachineAlarm.GetAlarmeStopRate(_ndxItems["alarmOn"]), operateCalls = observation.AlarmCalls.Count - alarmCallsBefore },
            _ndxItems["alarmOn"].IsTag("POWERED") && !_ndxItems["alarmOff"].IsTag("POWERED") && MachineAlarm.GetAlarmeStopRate(_ndxItems["alarmOn"]) == 50);
        JObject[] early = observation.EarlyServices.OfType<JObject>().Where(row => (int?)row["night"] == night).ToArray();
        NdxRequire(p + "alarm-actual-global-effect-before-insurance", "Exactly one native post-early-cycle services event with global alarm active, powered source true, no-power control false", new JArray(early),
            early.Length == 1 && (bool?)early[0]["globalAlarm"] == true && (bool?)early[0]["on"] == true && (bool?)early[0]["off"] == false,
            "Observe the official OnHandlingNightlyServicesEarly event before HandleInsurance clears or preserves the global alarm flag. This is the native early-cycle effect, not a manually assigned store flag.");
        int bonus = night == 1 ? 100 : 0;
        int trashPenalty = Store.garbageLevel <= 2 ? 0 : (int)(Store.baseStoreAttractiveness * (Store.garbageLevel * 5.0 / 100.0));
        int attraction = Store.baseStoreAttractiveness - trashPenalty + bonus;
        NdxRequire(p + "projector-real-attractiveness-and-empty-cell-control", new { bonus, attraction, active = night == 1, off = false }, new { Store.projectorAttractivenessBonus, actual = Store.GetCurrentStoreAttractiveness(), on = _ndxItems["projectorOn"].IsTag("IS_PROJECTOR_ACTIVE"), off = _ndxItems["projectorOff"].IsTag("IS_PROJECTOR_ACTIVE") },
            Store.projectorAttractivenessBonus == bonus && Store.GetCurrentStoreAttractiveness() == attraction && _ndxItems["projectorOn"].IsTag("IS_PROJECTOR_ACTIVE") == (night == 1) && !_ndxItems["projectorOff"].IsTag("IS_PROJECTOR_ACTIVE"),
            "Independent unmodified projector boost 100 and native base-minus-trash formula; real EndNight resets the global bonus before the next powerless cycle.");
        foreach (string role in new[] { "wineOn", "wineOff" })
        {
            GameItem wine = _ndxItems[role];
            int age = (night - 1) * (role == "wineOn" ? 2 : 1), value = 100 + age * 3;
            int ageCalls = observation.Ages.OfType<JObject>().Count(row => (int?)row["night"] == night && (int?)row["uid"] == wine.uniqueId);
            int finishCalls = observation.Finishes.OfType<JObject>().Count(row => (int?)row["night"] == night && (int?)row["uid"] == wine.uniqueId);
            NdxRequire(p + "natural-fermentation-and-aging." + role, new { state = 2, remaining = 0, age, value, finishCalls = night == 1 ? 1 : 0, ageCalls = night == 1 ? 0 : role == "wineOn" ? 2 : 1 }, new { wine = NdxItem(wine), finishCalls, ageCalls },
                NdxInt(wine, "WINE_CONTAINER_STATE") == 2 && NdxInt(wine, "FERMENTATION_TIME_REMAINING_INT") == 0 && NdxInt(wine, "AGE_INT") == age && wine.unitValue == value &&
                !wine.IsTag("WINE_COUNTERFEIT_TAG") && finishCalls == (night == 1 ? 1 : 0) && ageCalls == (night == 1 ? 0 : role == "wineOn" ? 2 : 1),
                "Native superyeast consumes four remaining fermentation units in the first actual night. Subsequent wine self-cycle ages once; powered rack adds one more for 24 energy, powerless rack adds none. No FinishFermentation/OnAgeWine direct call is made by this family.");
        }
        NdxRequire(p + "fridge-beverage-and-spirit-features", new { beverage = "chilled:20", spirit = night == 2 ? "iceCold:25" : "chilled:20", noPower = "no chilled", unitCounts = 1 },
            new { beverage = NdxItem(_ndxItems["chillFood"]), spirit = NdxItem(_ndxItems["spirit"]), off = NdxItem(_ndxItems["offFood"]) },
            NdxFeature(_ndxItems["chillFood"], "chilled", 20) && NdxFeature(_ndxItems["spirit"], night == 2 ? "iceCold" : "chilled", night == 2 ? 25 : 20) &&
            _ndxItems["spirit"].FindItemFeatureByID(night == 2 ? "chilled" : "iceCold") == null && _ndxItems["offFood"].FindItemFeatureByID("chilled") == null &&
            new[] { "chillFood", "spirit", "offFood", "icecream" }.All(role => _ndxItems[role].unitCount == 1) &&
            _ndxItems["chillFood"].unitValue == 18 && _ndxItems["offFood"].unitValue == 18 && _ndxItems["spirit"].unitValue == 85 && _ndxItems["icecream"].unitValue == 40,
            "Cold modifiers are native Innate pre-exposure values (current/fake20 or25, actual exposed payload0). Independent factory raw values remain beverage18, spirit85 and icecream40; no food is consumed or substituted.");
        if (night == 1)
        {
            NdxRequire(p + "fridge-freezable-real-night-feature", "frozen +25", NdxItem(_ndxItems["icecream"]), NdxFeature(_ndxItems["icecream"], "frozen", 25));
            Place(NdxPage(_ndxItems["fridgeStage"], 1), _ndxItems["icecream"], "nightdevices-retain-icecream-after-real-freeze");
        }
        else
            NdxRequire(p + "fridge-feature-naturally-expires-outside", "No frozen feature after the next real night outside fridge", NdxItem(_ndxItems["icecream"]), _ndxItems["icecream"].FindItemFeatureByID("frozen") == null);
        NdxRequire(p + "liquid-input-ledger-unchanged", "All three genuine pure-water witnesses retain 1000 parts and every species/value/basis ledger", new JArray(new[] { "chillLiquid", "freezeLiquid", "offLiquid" }.Select(role => NdxItem(_ndxItems[role]))),
            new[] { "chillLiquid", "freezeLiquid", "offLiquid" }.All(role => WaterHelper.GetTotalVolume(_ndxItems[role]) == 1000 && NdxInt(_ndxItems[role], "CURRENT_PART_WATER") == 1000 &&
                Liquid.Liquids.All(liquid => NdxInt(_ndxItems[role], liquid.currentPart) == (liquid.identifier == "water" ? 1000 : 0)) && JToken.DeepEquals(_ndxLiquidBefore[_ndxItems[role].uniqueId], FlLiquid(_ndxItems[role]))));
    }

    private static GameInventory NdxPort(GameItem machine, int index) => MachineHelper.GetBodyStorage(machine, index) as GameInventory
        ?? throw new InvalidOperationException("Native night-device port missing: " + machine.identifier + ":" + index);
    private static GameGridInventory NdxPage(GameItem stage, int index) => (GameGridInventory)DfTabs(stage).GetTabInventory(index);
    private GameItem NdxBorn(string id, string role, GameInventory destination, GameItem? through = null)
    {
        GameItem item = Spawn(id); Place(destination, item, "nightdevices-fixture-" + role, through); _ndxItems.Add(role, item);
        _ndxFixtures.Add(new JObject { ["role"] = role, ["grant"] = "unmodified registered native factory + actual SlotMarker", ["item"] = NdxItem(item) });
        return item;
    }
    private GameItem NdxCell(string role, int amount, GameInventory destination, GameItem? through = null)
    {
        GameItem item = PreBuiltItemHelper.EnergyCredit(amount); Place(destination, item, "nightdevices-energy-grant-" + role, through); _ndxItems.Add(role, item);
        _ndxFixtures.Add(new JObject { ["role"] = role, ["grant"] = "native PreBuiltItemHelper.EnergyCredit initial fixture energy only", ["energy"] = amount, ["item"] = NdxItem(item) });
        NdxRequire("nightdevices.initial-energy." + role, amount, Energy(item), Energy(item) == amount && item.uniqueId > 0 && GeneralHelper.IsItemOwned(item));
        return item;
    }
    private static bool NdxAt(GameGridInventory grid, GameItem item, int x, int y)
    {
        if (item.parentInventory == grid && item.modifiedShape.minX == x && item.modifiedShape.minY == y && item.modifiedShape.orientation == 0) return true;
        var shape = new GridShapeBuilder(item.shape).SetOrientation(0).SetPosition(x, y).Build();
        SlotMarker marker = grid.TryInventorySlot(item, item.unitCount, shape);
        return marker != null && marker.IsValid() && marker.inventory == grid && marker.targetItem == null && marker.TryAcceptOnce(item.unitCount) == item.unitCount && item.parentInventory == grid;
    }
    private static int NdxInt(GameItem item, string key) => item.state.dict.TryGetValue(key, out var tag) ? tag.valueInt : 0;
    private static string NdxString(GameItem item, string key) => item.state.dict.TryGetValue(key, out var tag) ? tag.valueString ?? "" : "";
    private static double NdxTemperature(GameItem item) => item.state.dict.TryGetValue("CONTAINER_TEMP_TAG", out var tag) ? tag.valueDouble : double.NaN;
    private static bool NdxFeature(GameItem item, string id, int value) => item.itemFeatures.Count(feature => feature.identifier == id && feature.GetCurrentValueMod() == value && feature.GetFakeValueModifier() == value && feature.GetActualValueModifier() == 0 && !feature.isDisabled) == 1;
    private JObject NdxItem(GameItem item)
    {
        JObject state = FlItem(item);
        state["features"] = new JArray(item.itemFeatures.OrderBy(feature => feature.identifier, StringComparer.Ordinal).Select(feature => JObject.FromObject(new
        { feature.identifier, feature.category, feature.featureType, feature.valueStage, actual = feature.GetActualValueModifier(), fake = feature.GetFakeValueModifier(), feature.isDisabled, feature.customIntValue1 })));
        if (item.IsTag("LIQUID_CONTAINER_TAG")) state["liquid"] = FlLiquid(item);
        if (item.identifier == "hypertech_bay") state["pages"] = new JArray(Enumerable.Range(0, DfTabs(item).count).Select(index =>
            new JObject { ["index"] = index, ["uids"] = new JArray(DfTabs(item).GetTabInventory(index).childItems.Select(child => child.uniqueId).OrderBy(uid => uid)) }));
        return state;
    }
    private JArray NdxLive(IEnumerable<int> denominator, string phase)
    {
        int[] promised = denominator.OrderBy(uid => uid).ToArray();
        GameItem[] live = Store.FindAllItem(false).ToArray();
        NdxRequire("nightdevices.uid-denominator." + phase, "Positive nonempty fixed UID set without duplicates", promised, promised.Length > 0 && promised.All(uid => uid > 0) && promised.Distinct().Count() == promised.Length);
        var rows = new JArray();
        foreach (int uid in promised)
        {
            GameItem[] matches = live.Where(item => item.uniqueId == uid).ToArray();
            bool valid = matches.Length == 1 && matches[0].unitCount > 0 && GeneralHelper.IsItemOwned(matches[0]) && matches[0].parentInventory != null &&
                _ndxIdentity.TryGetValue(uid, out var identity) && matches[0].identifier == identity.Id && matches[0].unitCount == identity.Count;
            NdxRequire("nightdevices.uid-custody." + phase + "." + uid, "Exactly one original owned positive-count item in a real inventory", new { uid, matches = matches.Select(NdxItem).ToArray() }, valid,
                "Resolve promised UIDs in the full graph including unowned items. A disappeared/stolen/duplicate item remains a failure; no reduced live intersection defines the denominator.");
            rows.Add(NdxItem(matches[0]));
        }
        return rows;
    }
    private void NdxRequire(string name, object? expected, object? observed, bool passed, string evidence = "")
    {
        R.Check(name, expected, observed, passed, evidence);
        if (!passed) throw new InvalidOperationException(name + " failed; no successful nightdevices baseline may be emitted");
    }
    private IEnumerable<object?> NdxWaitTicks(NdxObservation observation, string name, int count)
    {
        int start = observation.Tick;
        float deadline = Time.realtimeSinceStartup + 12f;
        while (observation.Tick - start < count)
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Genuine native one-second ticks did not arrive: " + name);
            if (Time.timeScale != 1f) NdxRequire("nightdevices.clock." + name, 1f, Time.timeScale, false);
            yield return null;
        }
        NdxRequire("nightdevices.actual-timer." + name, count, observation.Tick - start, observation.Tick - start >= count);
    }

    internal IEnumerable<object?> NativeNightDevicesReload(JObject baseline)
    {
        R.phase = "nightdevices-new-process-native-es3-readback";
        JObject expected = baseline["persistence"]?["nativeNightDevices"] as JObject ?? throw new InvalidOperationException("Night-device save baseline is missing");
        int[] promised = (expected["uids"] as JArray)?.Values<int>().ToArray() ?? Array.Empty<int>();
        JArray before = expected["items"] as JArray ?? throw new InvalidOperationException("Night-device UID snapshots are missing");
        int[] recorded = before.Select(item => (int?)item["uid"] ?? 0).ToArray();
        NdxRequire("nightdevices.reload-fixed-denominator-and-five-nights", "Five complete normal nights and one exact positive unique baseline row per promised UID", new { promised, recorded, expectedNights = expected["completedNights"] },
            (int?)expected["completedNights"] == 5 && promised.Length > 0 && promised.All(uid => uid > 0) && promised.Distinct().Count() == promised.Length && promised.OrderBy(uid => uid).SequenceEqual(recorded.OrderBy(uid => uid)));
        NdxRequire("nightdevices.reload-same-day-run-and-morning", new { run = expected["gameRunId"], day = expected["dayAfter"], state = "MORNING" }, new { Store.runID, day = StoreStation.GetDayCounter(), state = Store.storeState.ToString() },
            (string?)expected["gameRunId"] == Store.runID && (int?)expected["dayAfter"] == StoreStation.GetDayCounter() && Store.storeState == PlayerStore.StoreState.MORNING);
        foreach (int uid in promised)
        {
            _ndxPromised.Add(uid);
            JToken item = before.Single(row => (int?)row["uid"] == uid);
            _ndxIdentity[uid] = ((string)item["id"]!, (int)item["count"]!);
        }
        JArray actual = NdxLive(promised, "reload");
        bool same = NdxReloadContract(baseline, expected, before, actual);
        NdxRequire("nightdevices.reload-all-original-uids-content-shapes-features-tags-pages-and-liquid-ledgers", before, actual, same,
            "Common Reload requires a different process/run token, passed baseline, matching candidate hashes and original slot hash before real LoadGame. This family also proves every encoded nondefault native tag and narrowly bounded native thermal evolution. Factories, cycle callbacks and native DecodeNodes are never replayed by the reader.");
        R.persistence["nativeNightDevices"] = expected.DeepClone();
        R.environment["nativeNightDevicesReloadRawItems"] = actual;
        R.Skip("nightdevices.reload-additional-night-replay", "This separate process reads the saved five-night state and actual native timer behavior. It does not execute a sixth night (base reactor would naturally reach zero/explode), economic acquisition, arbitrary upgrade matrices or Steam achievements.", false);
        yield return null;
    }

    private bool NdxReloadContract(JObject baseline, JObject fixture, JArray expected, JArray actual)
    {
        var evidence = new JObject { ["rawExpected"] = expected.DeepClone(), ["rawObserved"] = actual.DeepClone(), ["encoded"] = new JArray(), ["thermal"] = new JArray() };
        R.environment["nativeNightDevicesReloadContract"] = evidence;
        string file = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId));
        string hash = Reflect.HashFile(file);
        string savedHash = (string?)(O.UseCopy ? baseline["persistence"]?["duplicateSaveSha256"] : baseline["persistence"]?["saveSha256"]) ?? "";
        NdxRequire("nightdevices.reload-original-es3-bytes", savedHash, new { file, hash }, savedHash.Length == 64 && hash == savedHash);
        var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", new ES3Settings(file, ES3.Location.File))!;
        string[] fields = { "mainInvJSON", "backInvJSON", "trashcanInvJSON", "docInvJSON", "cassettePlayerInvJSON", "showcaseInvJSON", "soldInvJSON", "hirelingInvJSON", "hiddenInvJSON", "vendingMachineInvJSON", "vendingFountainInvJSON" };
        SaveItemNode[] nodes = fields.Select(field => (string?)Reflect.Get(saved, field)).Where(json => !string.IsNullOrEmpty(json)).SelectMany(json => JsonUtility.FromJson<SaveState>(json!).saveItems).ToArray();
        bool encodedValid = saved.runID == Store.runID && saved.saveSlotId == (int?)baseline["persistence"]?["slotId"];
        foreach (JObject item in expected)
        {
            int uid = (int)item["uid"]!;
            SaveItemNode[] matches = nodes.Where(node => node.uniqueId == uid).ToArray();
            JObject? tags = matches.Length == 1 ? FlReloadNativeTags(matches[0].itemState) : null;
            bool valid = matches.Length == 1 && matches[0].identifier == (string?)item["id"] && matches[0].unitCount == (int?)item["count"] && matches[0].unitValue == (long?)item["value"] && matches[0].unitBaseValue == (long?)item["baseValue"] &&
                JToken.DeepEquals(FlReloadProjection(new JObject { ["tags"] = item["tags"]!.DeepClone() }), FlReloadProjection(new JObject { ["tags"] = tags!.DeepClone() }));
            var row = new JObject { ["uid"] = uid, ["matches"] = matches.Length, ["tags"] = tags, ["valid"] = valid };
            ((JArray)evidence["encoded"]!).Add(row);
            NdxRequire("nightdevices.reload-original-node-full-nondefault-tag-payload." + uid, "Exact identifier/count/raw values and every nondefault seven-field tag payload", row, valid,
                "Uses native read-only ES3 carrier + JsonUtility SaveState/SaveItemNode.OnAfterDeserialize. SaveItemNode.OnBeforeSerialize deliberately omits only wholly default disabled tags. Raw snapshots are retained; no other tag is discarded.");
            encodedValid &= valid;
        }
        int[] thermal = (fixture["thermalUids"] as JArray)?.Values<int>().ToArray() ?? Array.Empty<int>();
        NdxRequire("nightdevices.reload-thermal-denominator", "Exactly three original pure-water dropper UIDs", thermal, thermal.Length == 3 && thermal.Distinct().Count() == 3 && thermal.All(uid => _ndxPromised.Contains(uid)));
        var approved = new HashSet<int>();
        var comparisonExpected = (JArray)expected.DeepClone();
        var tick = (Action<GameItem, GameInventory, SlotMarker>)Reflect.Get(typeof(ChemicalHelper), "containerTempTick")!;
        float levelTime = Time.timeSinceLevelLoad;
        int maximum = !float.IsNaN(levelTime) && !float.IsInfinity(levelTime) && levelTime >= 0 && levelTime < int.MaxValue - 2 ? (int)Math.Ceiling(levelTime) + 1 : -1;
        evidence["maximumAmbientTicks"] = maximum; evidence["scaledLevelTime"] = levelTime;
        var rawMainNodes = (JArray)JObject.Parse(saved.mainInvJSON)["saveItems"]!;
        foreach (int uid in thermal)
        {
            JObject old = expected.OfType<JObject>().Single(item => (int?)item["uid"] == uid), current = actual.OfType<JObject>().Single(item => (int?)item["uid"] == uid);
            GameItem[] live = Store.FindAllItem(false).Where(item => item.uniqueId == uid).ToArray();
            SaveItemNode[] raw = nodes.Where(node => node.uniqueId == uid).ToArray();
            double initial = (double?)old["tags"]?["CONTAINER_TEMP_TAG"]?["valueDouble"] ?? double.NaN;
            double observed = (double?)current["tags"]?["CONTAINER_TEMP_TAG"]?["valueDouble"] ?? double.NaN;
            double decodedInitial = raw.Length == 1 && raw[0].itemState.dict.TryGetValue("CONTAINER_TEMP_TAG", out var decodedTag) ? decodedTag.valueDouble : double.NaN;
            bool originalScalarExact = FlReloadOriginalTemperature(rawMainNodes, uid, initial, decodedInitial, out JToken? rawTag);
            bool initialHeatingMarker = raw.Length == 1 && raw[0].itemState.IsTag("HEATING_TAG");
            int heatingClearTicks = initialHeatingMarker ? 1 : 0;
            var expectedHeating = old["tags"]?["HEATING_TAG"]?.DeepClone();
            if (initialHeatingMarker && expectedHeating is JObject heating) heating["valueEnabled"] = false;
            var beforeHeating = new JObject { ["tags"] = new JObject { ["HEATING_TAG"] = expectedHeating ?? JValue.CreateNull() } };
            var afterHeating = new JObject { ["tags"] = new JObject { ["HEATING_TAG"] = current["tags"]?["HEATING_TAG"]?.DeepClone() ?? JValue.CreateNull() } };
            bool heatingFieldsMatch = JToken.DeepEquals(FlReloadProjection(beforeHeating), FlReloadProjection(afterHeating));
            int steps = -1;
            bool valid = encodedValid && maximum >= 0 && Time.timeScale == 1f && live.Length == 1 && raw.Length == 1 && live[0].identifier == "1ml_dropper" && live[0].onOneSecondItemFunc == tick &&
                !live[0].IsTag("HEATING_TAG") && heatingFieldsMatch && WaterHelper.GetTotalVolume(live[0]) == 1000 &&
                originalScalarExact && raw[0].itemState.dict.TryGetValue("CONTAINER_TEMP_TAG", out var temperature) && temperature.valueEnabled && temperature.valueDouble == decodedInitial &&
                JToken.DeepEquals(old["liquid"], current["liquid"]) && FlReloadExactAmbientTicks(decodedInitial, observed, 1000, maximum - heatingClearTicks, out steps);
            var row = new JObject { ["uid"] = uid, ["initial"] = initial, ["observed"] = observed, ["exactNativeSteps"] = steps, ["maximum"] = maximum, ["valid"] = valid,
                ["rawDiskTag"] = rawTag?.DeepClone(), ["originalScalarExact"] = originalScalarExact, ["decodedInitial"] = decodedInitial,
                ["initialHeatingMarker"] = initialHeatingMarker, ["nativeHeatingClearTicks"] = heatingClearTicks, ["heatingFieldsMatch"] = heatingFieldsMatch };
            ((JArray)evidence["thermal"]!).Add(row);
            NdxRequire("nightdevices.reload-temperature-is-exact-clock-bounded-native-evolution." + uid, "Exact saved temperature followed only by a finite number of native 26 C ambient ticks, unchanged complete liquid ledger", row, valid,
                "The fridge battery was actually removed. A serialized HEATING_TAG, if retained by native base-state serialization, must clear on the first native ambient callback without changing temperature; only subsequent callbacks advance the exact temperature trajectory. All three original 1000-part pure-water droppers remain in their original zones. Expected arithmetic is 26+(T-26)*exp(-(0.05/(sqrt(1000)+11))*125), bounded by ceil(Time.timeSinceLevelLoad)+1; no epsilon, reset temperature or neighboring numeric tolerance is accepted.");
            if (valid)
            {
                approved.Add(uid);
                if (initialHeatingMarker)
                    ((JObject)comparisonExpected.OfType<JObject>().Single(item => (int?)item["uid"] == uid)["tags"]!["HEATING_TAG"]!)["valueEnabled"] = false;
            }
        }
        // Existing audited helpers implement only the native seven-field zero
        // omission and approved UID temperature-double projection. They never
        // run on runtime transfer/production comparisons.
        FlReloadProjectionNegativeControls();
        JToken projectedBefore = FlReloadProjection(comparisonExpected, approved), projectedAfter = FlReloadProjection(actual, approved);
        evidence["projectedExpected"] = projectedBefore; evidence["projectedObserved"] = projectedAfter;
        return approved.Count == 3 && encodedValid && JToken.DeepEquals(projectedBefore, projectedAfter);
    }

    private sealed class NdxObservation : IDisposable
    {
        private static NdxObservation? Active;
        private readonly ProbeContext _context;
        private readonly HashSet<int> _thermal;
        private readonly Dictionary<int, (double Target, double Rate)> _thermalContract;
        private readonly Harmony _harmony;
        private readonly List<MethodInfo> _patched = new();
        private int _tickFrame = -1, _insuranceDepth, _chemicalNightDepth;
        private readonly Dictionary<int, int> _coolCount = new(), _validCoolCount = new();
        private readonly Dictionary<string, int> _sourceCounts = new(StringComparer.Ordinal);
        internal int Tick, Night;
        internal int TotalCoolingCalls;
        internal readonly JArray Cooling = new(), Charges = new(), AlarmCalls = new(), Ages = new(), Finishes = new(), EarlyServices = new();
        internal readonly List<string> Errors = new();
        internal readonly JObject Evidence = new();
        internal NdxObservation(ProbeContext context, HashSet<int> thermal)
        {
            if (Active != null) throw new InvalidOperationException("A night-device observer is already active");
            _context = context; _thermal = thermal;
            _thermalContract = new Dictionary<int, (double Target, double Rate)>
            {
                [context._ndxItems["chillLiquid"].uniqueId] = (3.0, 0.5),
                [context._ndxItems["freezeLiquid"].uniqueId] = (-18.0, 1.0),
                [context._ndxItems["offLiquid"].uniqueId] = (3.0, 0.5)
            };
            _harmony = new Harmony("nicokobo.content_native_probe.nightdevices." + context.O.RunId);
            try
            {
                Patch(typeof(EmporiumEntry).GetMethod(nameof(EmporiumEntry.OneSecPrefixFunction))!, null, nameof(TimerAfter));
                Patch(typeof(MachineFridge).GetMethod("CoolZoneContainers", Reflect.All)!, nameof(CoolBefore), nameof(CoolAfter));
                Patch(typeof(ChemicalHelper).GetMethod(nameof(ChemicalHelper.RunNightlyChemicalSimulation))!, nameof(ChemicalNightBefore), nameof(ChemicalNightAfter));
                Patch(typeof(PowerHelper).GetMethod(nameof(PowerHelper.ChargeBattery), new[] { typeof(GameItem), typeof(int), typeof(bool) })!, nameof(ChargeBefore), nameof(ChargeAfter));
                Patch(typeof(PlayerStore).GetMethod(nameof(PlayerStore.HandleInsurance))!, nameof(InsuranceBefore), nameof(InsuranceAfter));
                Patch(typeof(MachineAlarm).GetMethod(nameof(MachineAlarm.Operate))!, nameof(AlarmBefore), nameof(AlarmAfter));
                Patch(typeof(WineHelper).GetMethod(nameof(WineHelper.OnAgeWine))!, null, nameof(AgeAfter));
                Patch(typeof(WineHelper).GetMethod(nameof(WineHelper.FinishFermentation))!, null, nameof(FinishAfter));
                ModHook.OnHandlingNightlyServicesEarly += ServicesEarly;
                Active = this; Refresh();
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodInfo method, string? before, string? after)
        {
            if (method == null) throw new MissingMethodException("Build26317870 night-device observer target missing");
            _harmony.Patch(method, prefix: before == null ? null : new HarmonyMethod(typeof(NdxObservation).GetMethod(before, Reflect.All)),
                postfix: after == null ? null : new HarmonyMethod(typeof(NdxObservation).GetMethod(after, Reflect.All)));
            _patched.Add(method);
        }
        private void Observe(Action action) { try { action(); } catch (Exception error) { Errors.Add(error.ToString()); } Refresh(); }
        private void Refresh()
        {
            Evidence["nativeTimerTicks"] = Tick; Evidence["cooling"] = Cooling; Evidence["charges"] = Charges;
            Evidence["alarmOperate"] = AlarmCalls; Evidence["nativeWineAge"] = Ages; Evidence["nativeWineFinish"] = Finishes; Evidence["errors"] = new JArray(Errors);
            Evidence["nativeEarlyServices"] = EarlyServices;
            Evidence["totalCoolingMethodCalls"] = TotalCoolingCalls; Evidence["coolingPerUid"] = JObject.FromObject(_coolCount);
            Evidence["validCoolingPerUid"] = JObject.FromObject(_validCoolCount); Evidence["coolingSourceCounts"] = JObject.FromObject(_sourceCounts);
            Evidence["coolingSamplePolicy"] = "All calls checked/counted; first two complete raw-tag/ledger samples per original witness UID, night and native source retained. Actual EndNight.RunNightlyChemicalSimulation invokes 360 native callbacks; these are separately labelled from real Update one-second ticks.";
        }
        internal int CoolCount(int uid) => _coolCount.GetValueOrDefault(uid);
        internal int ValidCoolCount(int uid) => _validCoolCount.GetValueOrDefault(uid);
        internal int SourceCount(int night, string source, int uid) => _sourceCounts.GetValueOrDefault(night + ":" + source + ":" + uid);
        private static void TimerAfter() { var o = Active; if (o != null) o.Observe(() => { o.Tick++; o._tickFrame = Time.frameCount; }); }
        private static void ChemicalNightBefore() { var o = Active; if (o != null) o._chemicalNightDepth++; }
        private static void ChemicalNightAfter() { var o = Active; if (o != null) o._chemicalNightDepth--; }
        private static void CoolBefore(GameGridInventory __0, double __1, double __2, out JObject? __state)
        {
            __state = null; var o = Active; if (o == null) return;
            try
            {
                GameItem[] items = __0.childItems.Where(item => o._thermal.Contains(item.uniqueId)).ToArray();
                if (items.Length == 0) return;
                string source = o._chemicalNightDepth > 0 ? "native-nightly-chemical-360" : o._tickFrame == Time.frameCount ? "native-update-one-second" : "unknown";
                __state = new JObject { ["night"] = o.Night, ["tick"] = o.Tick, ["frame"] = Time.frameCount, ["genuineTimerFrame"] = o._tickFrame == Time.frameCount,
                    ["source"] = source, ["target"] = __1, ["rate"] = __2, ["items"] = new JArray(items.Select(item =>
                    {
                        string key = o.Night + ":" + source + ":" + item.uniqueId;
                        bool sample = o._sourceCounts.GetValueOrDefault(key) < 2;
                        return new JObject { ["uid"] = item.uniqueId, ["before"] = NdxTemperature(item), ["sourceKey"] = key, ["sample"] = sample,
                            ["countBefore"] = item.unitCount, ["valueBefore"] = item.unitValue, ["baseValueBefore"] = item.unitBaseValue,
                            ["snapshotBefore"] = sample ? o._context.NdxItem(item) : null };
                    })) };
            }
            catch (Exception error) { o.Errors.Add(error.ToString()); o.Refresh(); }
        }
        private static void CoolAfter(GameGridInventory __0, JObject? __state)
        {
            var o = Active; if (o == null || __state == null) return;
            o.Observe(() =>
            {
                double target = (double)__state["target"]!, rate = (double)__state["rate"]!;
                bool valid = (string?)__state["source"] != "unknown";
                foreach (JObject row in (JArray)__state["items"]!)
                {
                    GameItem item = __0.childItems.Single(child => child.uniqueId == (int)row["uid"]!);
                    var contract = o._thermalContract[item.uniqueId];
                    double before = (double)row["before"]!, expected = before > contract.Target ? Math.Max(before - contract.Rate, contract.Target) : Math.Min(before + contract.Rate, contract.Target);
                    bool sample = (bool)row["sample"]!;
                    row["expectedTarget"] = contract.Target; row["expectedRate"] = contract.Rate;
                    row["expected"] = expected; row["after"] = NdxTemperature(item); row["snapshotAfter"] = sample ? o._context.NdxItem(item) : null;
                    bool preserved = item.uniqueId > 0 && item.unitCount == 1 && item.unitCount == (int?)row["countBefore"] && item.unitValue == (long?)row["valueBefore"] && item.unitBaseValue == (long?)row["baseValueBefore"] &&
                        GeneralHelper.IsItemOwned(item) && WaterHelper.GetTotalVolume(item) == 1000 && Liquid.Liquids.All(part => NdxInt(item, part.currentPart) == (part.identifier == "water" ? 1000 : 0)) &&
                        (!sample || JToken.DeepEquals(row["snapshotBefore"]?["liquid"], row["snapshotAfter"]?["liquid"]));
                    // CoolZoneContainers writes with skipValidate=true; its
                    // postfix observes raw state before modifiedState refresh.
                    row["valid"] = target == contract.Target && rate == contract.Rate && preserved && NdxTemperature(item) == expected && item.state.IsTag("HEATING_TAG"); valid &= (bool)row["valid"]!;
                    o._coolCount[item.uniqueId] = o._coolCount.GetValueOrDefault(item.uniqueId) + 1;
                    if ((bool)row["valid"]! && valid) o._validCoolCount[item.uniqueId] = o._validCoolCount.GetValueOrDefault(item.uniqueId) + 1;
                    string key = (string)row["sourceKey"]!; o._sourceCounts[key] = o._sourceCounts.GetValueOrDefault(key) + 1;
                }
                __state["valid"] = valid; o.TotalCoolingCalls++;
                if (!valid || ((JArray)__state["items"]!).Any(item => (bool?)item["sample"] == true)) o.Cooling.Add(__state);
                if (!valid) o.Errors.Add("Original CoolZoneContainers did not match its independent exact thermal/ledger contract");
            });
        }
        private static void ChargeBefore(GameItem __0, int __1, out JObject? __state)
        {
            __state = null; var o = Active; if (o == null || o.Night <= 0) return;
            try
            {
                if (__0 != o._context._ndxItems["sharedCell"] && __0 != o._context._ndxItems["reactorCell"]) return;
                __state = new JObject { ["night"] = o.Night, ["uid"] = __0.uniqueId, ["amount"] = __1, ["before"] = Energy(__0), ["frame"] = Time.frameCount };
            }
            catch (Exception error) { o.Errors.Add(error.ToString()); o.Refresh(); }
        }
        private static void ChargeAfter(GameItem __0, JObject? __state)
        { var o = Active; if (o != null && __state != null) o.Observe(() => { __state["after"] = Energy(__0); o.Charges.Add(__state); }); }
        private static void InsuranceBefore() { var o = Active; if (o != null) o._insuranceDepth++; }
        private static void InsuranceAfter() { var o = Active; if (o != null) o._insuranceDepth--; }
        private static void ServicesEarly()
        {
            var o = Active; if (o == null || o.Night <= 0) return;
            o.Observe(() => o.EarlyServices.Add(new JObject { ["night"] = o.Night, ["globalAlarm"] = o._context.Store.isSecurityAlarmActive,
                ["on"] = o._context._ndxItems["alarmOn"].IsTag("POWERED"), ["off"] = o._context._ndxItems["alarmOff"].IsTag("POWERED"),
                ["insuranceUnlocked"] = StoreService.GetInsuranceService().unlocked, ["insuranceUsing"] = StoreService.GetInsuranceService().isUsing,
                ["day"] = StoreStation.GetDayCounter(), ["startingDay"] = o._context.Store.startingDay, ["skipIntro"] = o._context.Store.skipIntro }));
        }
        private static JObject Reputation() => JObject.FromObject(new { rev = Reflect.Get(StoreReputation.GetRevFaction(), "amount"), sec = Reflect.Get(StoreReputation.GetSecFaction(), "amount"), bm = Reflect.Get(StoreReputation.GetBMFaction(), "amount"), ul = Reflect.Get(StoreReputation.GetULFaction(), "amount") });
        private static void AlarmBefore(GameItem __0, out JObject? __state)
        {
            __state = null; var o = Active; if (o == null || o.Night <= 0) return;
            try { __state = new JObject { ["night"] = o.Night, ["uid"] = __0?.uniqueId ?? 0, ["insuranceDepth"] = o._insuranceDepth, ["before"] = Reputation(), ["frame"] = Time.frameCount }; }
            catch (Exception error) { o.Errors.Add(error.ToString()); o.Refresh(); }
        }
        private static void AlarmAfter(JObject? __state)
        {
            var o = Active; if (o == null || __state == null) return;
            o.Observe(() =>
            {
                JObject before = (JObject)__state["before"]!, after = Reputation();
                double expected = Math.Min(200.0, (double)before["sec"]! + 1.0);
                bool valid = (int?)__state["uid"] == o._context._ndxItems["alarmOn"].uniqueId && (int?)__state["insuranceDepth"] == 1 &&
                    (double)after["sec"]! == expected && new[] { "rev", "bm", "ul" }.All(faction => JToken.DeepEquals(before[faction], after[faction]));
                __state["expectedSec"] = expected; __state["after"] = after; __state["valid"] = valid; o.AlarmCalls.Add(__state);
                if (!valid) o.Errors.Add("Native insurance-triggered alarm transmitter effect differs from declared unmodified APPRENTICE/normal +1 contract");
            });
        }
        private static void AgeAfter(GameItem __0)
        { var o = Active; if (o != null && o.Night > 0 && (__0 == o._context._ndxItems["wineOn"] || __0 == o._context._ndxItems["wineOff"])) o.Observe(() => o.Ages.Add(new JObject { ["night"] = o.Night, ["uid"] = __0.uniqueId, ["age"] = NdxInt(__0, "AGE_INT"), ["item"] = o._context.NdxItem(__0) })); }
        private static void FinishAfter(GameItem __0)
        { var o = Active; if (o != null && o.Night > 0 && (__0 == o._context._ndxItems["wineOn"] || __0 == o._context._ndxItems["wineOff"])) o.Observe(() => o.Finishes.Add(new JObject { ["night"] = o.Night, ["uid"] = __0.uniqueId, ["item"] = o._context.NdxItem(__0) })); }
        public void Dispose()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            ModHook.OnHandlingNightlyServicesEarly -= ServicesEarly;
            foreach (MethodInfo method in _patched) _harmony.Unpatch(method, HarmonyPatchType.All, _harmony.Id);
            Refresh();
        }
    }
}
