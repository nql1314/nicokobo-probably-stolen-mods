using System.Collections;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private sealed class PurifierFixture : IDisposable
    {
        internal GameItem Machine = null!, Battery = null!, Vessel = null!;
        internal GameSlotInventory PowerSlot = null!, WaterSlot = null!, NoteSlot = null!;
        internal GameInventory Modules = null!;
        internal GameItem? GrowthModule;
        internal bool HasFeeder, Retained;
        public void Dispose() { if (!Retained) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(Machine); }
    }

    private IEnumerable<object?> NativePurifierScenarios()
    {
        // Independent single-litre ledger for Build 26284217 and the current
        // S24 contract: native treatment reaches 99%, so the shared purity cap
        // leaves no allowance for extra removal of the remaining 10,000 parts.
        (string Name, int Water, int Dirt, int FinalWater, int FinalDirt, int Removed, int UnmodifiedDebit, bool Matrix)[] cases =
        {
            ("pure-no-work", 1000000, 0, 1000000, 0, 0, 0, false),
            ("native-contaminated", 980000, 20000, 990000, 10000, 10000, 4, false),
            ("matrix-shared-cap-and-logistics", 980000, 20000, 990000, 10000, 10000, 4, true)
        };
        R.environment["purifierAcceptanceLedger"] = new { steamBuild = 26284217, units = "1000 parts = 1 ml", totalParts = 1000000,
            source = "Explicit precomputed one-litre cases: native and S24 share the 99% purity cap at zero quality bonus (docs/design/MODULE_MATRIX.md, S24). S24 must not turn 99% into 99.25%; positive extra treatment below the cap is outside this fixture. Native base cost is 4; active valid bonuses use the official pre-action cost query.",
            cases = cases.Select(c => new { c.Name, c.Water, c.Dirt, c.FinalWater, c.FinalDirt, c.Removed, c.UnmodifiedDebit, c.Matrix }).ToArray() };
        foreach (var c in cases)
        {
            R.phase = "native-purifier:" + c.Name;
            PurifierFixture? f = null;
            try
            {
                f = CreatePurifierFixture(c.Name, c.Water, c.Dirt, c.Matrix);
                int vesselUid = f.Vessel.uniqueId, batteryUid = f.Battery.uniqueId;
                int volumeBefore = WaterHelper.GetTotalVolume(f.Vessel), energyBefore = Energy(f.Battery);
                int powerBefore = MachineryHelper.GetMachinePowerUsage(f.Machine);
                int expectedDebit = c.Removed == 0 ? 0 : powerBefore;
                long gridBefore = GridTotal;
                JToken componentsBefore = PositiveLiquidParts(f.Vessel);
                int? growthBefore = f.GrowthModule?.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt();
                int? workDayBefore = f.GrowthModule?.GetTagReadonly("nicokobo.mechcore_protocol.module_matrix.continuous_last_work_day.v1").GetInt();
                NativeCycle(f.Machine);
                JToken componentsAfter = PositiveLiquidParts(f.Vessel);
                var expectedParts = new Dictionary<string, int>(StringComparer.Ordinal) { ["water"] = c.FinalWater };
                if (c.FinalDirt != 0) expectedParts["physical_contaminant"] = c.FinalDirt;
                int removedTag = f.Machine.GetTagReadonly("TOTAL_CONTAMINANT_REMOVED_TAG").GetInt();
                int energyAfter = Energy(f.Battery);
                R.Check("purifier.native-cycle." + c.Name,
                    new { totalParts = 1000000, components = expectedParts, totalContaminantRemoved = c.Removed, energyDebit = expectedDebit,
                        officialPreActionPowerUsage = powerBefore, unmodifiedBasePowerUsage = 4, preserveVesselUid = vesselUid, preserveBatteryUid = batteryUid, grid = gridBefore },
                    new { volumeBefore, volumeAfter = WaterHelper.GetTotalVolume(f.Vessel), componentsBefore, componentsAfter,
                        totalContaminantRemoved = removedTag, energyBefore, energyAfter, energyDebit = energyBefore - energyAfter,
                        nativePowerUsage = powerBefore, vesselUid = f.Vessel.uniqueId, batteryUid = f.Battery.uniqueId, grid = GridTotal },
                    volumeBefore == 1000000 && WaterHelper.GetTotalVolume(f.Vessel) == 1000000 &&
                        JToken.DeepEquals(JObject.FromObject(expectedParts), componentsAfter) && removedTag == c.Removed &&
                        powerBefore >= 0 && energyBefore - energyAfter == expectedDebit && GridTotal == gridBefore &&
                        f.Vessel.uniqueId == vesselUid && f.Vessel.parentInventory == f.WaterSlot &&
                        f.Battery.uniqueId == batteryUid && f.Battery.parentInventory == f.PowerSlot,
                    "Actual native water_purifier factory -> native slots 0 battery/1 modules/2 vessel/3 note -> real wrapped onCycleEndSlotItemFunc. Active valid bonuses use the official pre-action cost; no PurifyContainer substitute, resource assignment or synthetic receipt.");
                if (f.GrowthModule != null)
                {
                    int after = f.GrowthModule.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt();
                    int workDay = f.GrowthModule.GetTagReadonly("nicokobo.mechcore_protocol.module_matrix.continuous_last_work_day.v1").GetInt();
                    R.Check("purifier.native-machine-action." + c.Name,
                        new { before = 8, after = c.Removed == 0 ? 8 : 12, workDay = c.Removed == 0 ? workDayBefore : StoreStation.GetDayCounter() },
                        new { before = growthBefore, after, workDay },
                        growthBefore == 8 && after == (c.Removed == 0 ? 8 : 12) &&
                            workDay == (c.Removed == 0 ? workDayBefore : StoreStation.GetDayCounter()),
                        "Real installed M08 growth observes actual OnMachineActioned -> ActivateOnUseAction; pure water must not action the module");
                }
                if (c.Matrix && f.HasFeeder) AssertPurifierLogisticsObservation(f, c.Name, c.Water, c.Dirt, expectedParts);
                if (c.Matrix)
                {
                    R.persistence["nativePurifierMatrixFixture"] = new { machineUid = f.Machine.uniqueId, vesselUid, batteryUid,
                        finalComponents = componentsAfter, totalContaminantRemoved = removedTag, energy = energyAfter };
                    Retain(f.Machine); f.Retained = true;
                }
            }
            catch (FixtureUnavailable error) { R.Skip("purifier.fixture." + c.Name, error.Message); }
            catch (Exception error) { R.Check("purifier.native-cycle." + c.Name, "Actual native purification and resource ledger", null, false, "Build26284217 bounded native-cycle scenario", error.ToString()); }
            finally { f?.Dispose(); }
            yield return null;
        }
    }

    private PurifierFixture CreatePurifierFixture(string name, int water, int dirt, bool matrix)
    {
        var f = new PurifierFixture { Machine = Spawn("water_purifier") };
        try
        {
            ModHelper.MoveItemToInventory(f.Machine);
            if (f.Machine.parentInventory == null) ModHelper.MoveItemToBackCounter(f.Machine);
            if (f.Machine.parentInventory == null) throw new FixtureUnavailable("Native main/back inventory cannot fit a disposable water_purifier");
            f.PowerSlot = MachineHelper.GetBodyStorage(f.Machine, 0) as GameSlotInventory ?? throw new FixtureUnavailable("Native purifier battery slot 0 missing");
            f.Modules = MachineHelper.GetBodyStorage(f.Machine, 1) as GameInventory ?? throw new FixtureUnavailable("Native purifier module grid 1 missing");
            f.WaterSlot = MachineHelper.GetBodyStorage(f.Machine, 2) as GameSlotInventory ?? throw new FixtureUnavailable("Native purifier water vessel slot 2 missing");
            f.NoteSlot = MachineHelper.GetBodyStorage(f.Machine, 3) as GameSlotInventory ?? throw new FixtureUnavailable("Native purifier note slot 3 missing");
            f.Battery = PreBuiltItemHelper.EnergyCredit(100); Place(f.PowerSlot, f.Battery, "purifier-battery:" + name, f.Machine);
            f.Vessel = Spawn("water_jug"); WaterHelper.EmptyContainer(f.Vessel);
            if (WaterHelper.GetFreeCapacity(f.Vessel) < water + dirt) { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(f.Vessel); throw new FixtureUnavailable("Actual water_jug cannot fit the 1,000,000-part acceptance fixture"); }
            WaterHelper.AddLiquid(f.Vessel, "water", water);
            if (dirt > 0) WaterHelper.AddLiquid(f.Vessel, "physical_contaminant", dirt);
            Place(f.WaterSlot, f.Vessel, "purifier-vessel:" + name, f.Machine);
            var original = new Dictionary<string, int>(StringComparer.Ordinal) { ["water"] = water };
            if (dirt != 0) original["physical_contaminant"] = dirt;
            R.Check("purifier.real-fixture." + name, original, PositiveLiquidParts(f.Vessel),
                JToken.DeepEquals(JObject.FromObject(original), PositiveLiquidParts(f.Vessel)), "Fixture composition created only through native liquid helpers before the tested cycle");
            if (TryInstallPurifierModule(f, "nicokobo.mechcore_protocol.module_matrix.continuous_acceleration_module", name + ":M08", false, out var growth)) f.GrowthModule = growth;
            if (matrix)
            {
                if (!TryInstallPurifierModule(f, "nicokobo.mechcore_protocol.module_matrix.osmosis_module", name + ":S24", true, out _))
                    throw new FixtureUnavailable("Actual purifier module grid/admission cannot accept the required S24; shared-cap scenario cannot be claimed");
                f.HasFeeder = TryInstallPurifierModule(f, "nicokobo.automated_logistics.auto_feeder", name + ":feeder", true, out _);
            }
            Reflect.Call(Logistics, "ConfigureMachine", f.Machine, matrix && f.HasFeeder, false);
            object inventory = Reflect.Call(Logistics, "Inventory", f.Machine)!;
            var liquidInputs = Reflect.Get(inventory, "LiquidInputs") as IDictionary;
            bool portsMatch = ReferenceEquals(Reflect.Get(inventory, "Battery"), f.PowerSlot) && ReferenceEquals(Reflect.Get(inventory, "Modules"), f.Modules) &&
                ReferenceEquals(Reflect.Get(inventory, "Output"), f.WaterSlot) && Reflect.Get(inventory, "ItemInput") == null &&
                liquidInputs != null && ReferenceEquals(liquidInputs["purifier_water"], f.WaterSlot);
            R.Check("purifier.logistics-real-native-ports." + name, "0 battery / 1 modules / 2 output-water and purifier_water / 3 note", new { portsMatch, waterUid = f.Vessel.uniqueId }, portsMatch);
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private bool TryInstallPurifierModule(PurifierFixture f, string itemId, string name, bool required, out GameItem? item)
    {
        item = null;
        if (!DirectoryMaster.Has<GameItem>(itemId)) { R.Skip("purifier.module." + name, "Actual factory unavailable: " + itemId, required); return false; }
        var module = Spawn(itemId);
        SlotMarker marker = f.Machine.TryFindOneValidInventorySlot(module);
        if (marker == null || !marker.IsValid() || !ReferenceEquals(marker.inventory, f.Modules))
        {
            R.Skip("purifier.module." + name, "Legal native machine admission/shape rejected " + itemId + "; module=" + module.modifiedShape.width + "x" + module.modifiedShape.height + "; actual destination=" + marker?.inventory?.identifier, required);
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(module); return false;
        }
        try { Place(f.Modules, module, "purifier-module:" + name, f.Machine); item = module; return true; }
        catch { if (module.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(module); throw; }
    }

    private static JObject PositiveLiquidParts(GameItem vessel) => new(Liquid.Liquids.Select(liquid => new
        { liquid.identifier, amount = vessel.GetTagReadonly(liquid.currentPart).GetInt() }).Where(part => part.amount > 0)
        .OrderBy(part => part.identifier, StringComparer.Ordinal).Select(part => new JProperty(part.identifier, part.amount)));

    private void AssertPurifierLogisticsObservation(PurifierFixture f, string name, int originalWater, int originalDirt, Dictionary<string, int> expectedFinal)
    {
        object state = Reflect.Get(LogisticsRuntime, "State")!;
        var machines = (IDictionary)Reflect.Get(state, "Machines")!;
        object? settings = machines[f.Machine.uniqueId];
        object? cycle = settings == null ? null : Reflect.Get(settings, "Purifier");
        if (cycle == null)
        {
            R.Check("purifier.logistics-final-observation." + name, "Real before/after native wrapper captured a Purifier cycle", null, false,
                "Required real feeder installed; no synthetic PreparePurifierCycle/AfterNativeMachineBatch invocation or fabricated receipt");
            return;
        }
        object feed = Reflect.Get(cycle, "Feed")!;
        static Dictionary<string, int> Parts(object? value) => Reflect.Each(value).Where(part => Reflect.Int(part, "Parts") > 0)
            .ToDictionary(part => Reflect.Str(part, "LiquidId"), part => Reflect.Int(part, "Parts"), StringComparer.Ordinal);
        var expectedFeed = new Dictionary<string, int>(StringComparer.Ordinal) { ["water"] = originalWater, ["physical_contaminant"] = originalDirt };
        var feedParts = Parts(Reflect.Get(feed, "Basis"));
        var observedParts = Parts(Reflect.Get(cycle, "ObservedContents"));
        int pending = Reflect.Each(Reflect.Get(settings!, "PendingLiquids")).Count();
        R.Check("purifier.logistics-final-observation." + name,
            new { vesselUid = f.Vessel.uniqueId, targetParts = 1000000, originalFeed = expectedFeed, finalObserved = expectedFinal, remaining = 0, pendingLiquidDebts = 0 },
            new { cycle = Reflect.Json(cycle), feedParts, observedParts, pendingLiquidDebts = pending },
            Reflect.Int(feed, "VesselId") == f.Vessel.uniqueId && Reflect.Int(feed, "TargetParts") == 1000000 && Reflect.Int(feed, "Remaining") == 0 &&
                JToken.DeepEquals(JObject.FromObject(expectedFeed), JObject.FromObject(feedParts)) &&
                JToken.DeepEquals(JObject.FromObject(expectedFinal), JObject.FromObject(observedParts)) && pending == 0,
            "Real loaded factory wrapper observed the final native+Matrix liquid state. Ejection is disabled; in-place purification does not earn a fabricated withdrawal/restock debt.");
    }
}
