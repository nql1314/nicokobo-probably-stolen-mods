using Nicokobo.ContentInterop;
using DseCecil;
using Newtonsoft.Json;
using Nicokobo.AutomatedLogistics.Domain;
using Nicokobo.AutomatedLogistics.Native;
using Nicokobo.AutomatedLogistics.Integration;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }

// Exercise the actual read/write boundary: a v6 runtime must load and stage
// its own receipt, while an older payload remains untouched.
var runStore = PlayerStore.Instance;
const string logisticsOwner = "nicokobo.automated_logistics";
const string logisticsKey = logisticsOwner + ".runtime_v6";
runStore.modData[logisticsOwner + ".runtime_v5"] = "{\"Version\":5}";
Check(NativeRunDataApi.Read(runStore, logisticsOwner, logisticsKey).Status == RunDataStatus.Missing,
    "The terminal's v6 run key is rejected before its state can load");
const string receipt = "{\"Version\":6,\"TerminalGranted\":true}";
Check(NativeRunDataApi.Stage(runStore, logisticsOwner, logisticsKey, runStore.runID, runStore.saveSlotId, null, receipt).Status == RunDataStatus.StagedInMemory &&
    NativeRunDataApi.Read(runStore, logisticsOwner, logisticsKey).Json == receipt,
    "A new terminal receipt must stage and read back through the real state adapter");
Check(NativeRunDataApi.Stage(runStore, logisticsOwner, logisticsKey, runStore.runID, runStore.saveSlotId, null, "{}").Status == RunDataStatus.Conflict &&
    runStore.modData[logisticsKey] == receipt, "A stale write must preserve the current terminal receipt");
Check(NativeRunDataApi.Read(runStore, "other.owner", logisticsKey).Status == RunDataStatus.Invalid &&
    runStore.modData[logisticsOwner + ".runtime_v5"] == "{\"Version\":5}", "Reading v6 must not claim another owner's data or overwrite v5");

// The threshold belongs to the installed physical battery and each payment
// comes from one source. A pooled total must never authorize a split debit.
Check(PowerPaymentPlan.Select(26, 100, 500, 20) == PowerPaymentSource.Local, "Above 25% must use an adequate local battery");
Check(PowerPaymentPlan.Select(25, 100, 500, 20) == PowerPaymentSource.Grid, "Exactly 25% must prefer the whole grid payment");
Check(PowerPaymentPlan.Select(25, 100, 19, 20) == PowerPaymentSource.Local, "An inadequate grid must fall back to an adequate low battery");
Check(PowerPaymentPlan.Select(2, 100, 3, 4) == PowerPaymentSource.None, "Independent insufficiency must not pool resources");
Check(PowerPaymentPlan.Select(100, 100, 1000, 101) == PowerPaymentSource.None, "The grid must not evade physical battery capacity");
for (int local = 0; local <= 100; local++)
for (int cost = 1; cost <= 110; cost++)
foreach (long grid in new long[] { 0, 1, 24, 25, 60, 5000 })
{
    var source = PowerPaymentPlan.Select(local, 100, grid, cost);
    int afterLocal = local - (source == PowerPaymentSource.Local ? cost : 0);
    long afterGrid = grid - (source == PowerPaymentSource.Grid ? cost : 0);
    Check(afterLocal >= 0 && afterGrid >= 0, "Payment overdraws a real source");
    Check((local - afterLocal) + (grid - afterGrid) == (source == PowerPaymentSource.None ? 0 : cost), "Energy payment is not conserved");
}

// Run the production ledger rule: generation follows live output, and its
// per-cell daily receipt must survive reload even when the grid was full.
var hypertech = new PowerNetworkState();
Check(hypertech.AddHypertechGeneration(3, 71, 12, 5000) == 12 && hypertech.StoredEnergy == 12 && hypertech.Generated == 12,
    "A Hypertech cell must generate its current output once per night");
Check(hypertech.AddHypertechGeneration(3, 71, 22, 5000) == 0 && hypertech.StoredEnergy == 12,
    "Upgrading or reinserting the same cell must not award a second generation in one day");
Check(hypertech.AddHypertechGeneration(3, 72, 22, 5000) == 22 && hypertech.StoredEnergy == 34,
    "Separate Hypertech cells must each contribute their own output");
hypertech = JsonConvert.DeserializeObject<PowerNetworkState>(JsonConvert.SerializeObject(hypertech))!;
Check(hypertech.Valid() && hypertech.AddHypertechGeneration(3, 71, 22, 5000) == 0 && hypertech.StoredEnergy == 34,
    "Reloading the shared power ledger must retain the daily generator receipts");
Check(hypertech.AddHypertechGeneration(4, 71, 22, 5000) == 22 && hypertech.StoredEnergy == 56 && hypertech.Generated == 56,
    "The following night must use the upgraded output");
Check(hypertech.AddHypertechGeneration(3, 73, 12, 5000) == 0 && hypertech.StoredEnergy == 56,
    "An older night notification must not reset current generator receipts");
hypertech.StoredEnergy = 4995;
Check(hypertech.AddHypertechGeneration(4, 72, 22, 5000) == 5 && hypertech.StoredEnergy == 5000 && hypertech.Generated == 61,
    "Hypertech generation must cap storage and count only energy actually accepted");
Check(hypertech.AddHypertechGeneration(4, 73, 12, 5000) == 0, "A full grid must reject excess energy");
hypertech.StoredEnergy -= 100;
Check(hypertech.AddHypertechGeneration(4, 73, 12, 5000) == 0 && hypertech.StoredEnergy == 4900,
    "Spending after a full-grid settlement must not recover discarded generation");
Check(hypertech.SettleDay(4) && hypertech.Days.Single().Generated == 61,
    "Nightly statistics must include accepted Hypertech generation");
var oldPower = JsonConvert.DeserializeObject<PowerNetworkState>("{\"StoredEnergy\":123}")!;
Check(oldPower.Valid() && oldPower.HypertechDay == -1 && oldPower.HypertechGenerators.Count == 0,
    "Existing saves without Hypertech receipts must remain valid");
oldPower.HypertechGenerators.Add(71);
Check(!oldPower.Valid(), "Saved generator receipts require an associated day");

// Collection must follow the selected target, rather than leave a completed
// low-quality purifier waiting for an unreachable pure-water grade.
Check(PurifierCollectionRules.Evaluate(9900, 9900, false, PurifierCollectionTarget.MachineLimit) == PurifierCollectionState.Ready,
    "A quality-0 purifier remains stuck at its completed 99% limit");
Check(PurifierCollectionRules.Evaluate(9980, 9980, false, PurifierCollectionTarget.MachineLimit) == PurifierCollectionState.Ready,
    "A quality-75 purifier cannot collect its completed batch");
Check(PurifierCollectionRules.Evaluate(9990, 9990, false, PurifierCollectionTarget.MachineLimit) == PurifierCollectionState.Ready,
    "A quality-100 purifier cannot collect its completed batch");
Check(PurifierCollectionRules.Evaluate(10000, 10000, false, PurifierCollectionTarget.MachineLimit) == PurifierCollectionState.Ready,
    "A quality-200 purifier cannot collect its completed batch");
Check(PurifierCollectionRules.Evaluate(9800, 9900, true, PurifierCollectionTarget.MachineLimit) == PurifierCollectionState.Purifying,
    "An unfinished purifier batch was collected");
Check(PurifierCollectionRules.Evaluate(9900, 9900, true, PurifierCollectionTarget.PureWater) == PurifierCollectionState.Unreachable,
    "A pure-water target above machine capability is not explained");
Check(PurifierCollectionRules.Evaluate(9800, 9900, false, PurifierCollectionTarget.MachineLimit) == PurifierCollectionState.Unremovable,
    "Unremovable residual components were treated as a completed batch");
Check(PurifierCollectionRules.Evaluate(9980, 10000, false, PurifierCollectionTarget.PureWater) == PurifierCollectionState.Unremovable,
    "An unremovable batch below the selected pure-water target was collected");
Check(PurifierCollectionRules.Evaluate(9990, 10000, false, PurifierCollectionTarget.PureWater) == PurifierCollectionState.Ready,
    "The selected pure-water grade incorrectly waits for 100% purity");

var feedSample = new NativeMachineLiquidSnapshot(10000, new[] {
    new NativeMachineLiquidPart("water", 9980) { Value = 9.98m, QualityBasis = 19.96m },
    new NativeMachineLiquidPart("mineral", 20) { Value = .20m, QualityBasis = .40m } });
var smallerFeed = new NativeMachineLiquidSnapshot(20000, new[] {
    new NativeMachineLiquidPart("water", 4990) { Value = 4.99m, QualityBasis = 9.98m },
    new NativeMachineLiquidPart("mineral", 10) { Value = .10m, QualityBasis = .20m } });
Check(NativeLiquidCompositionMath.SameSpecification(feedSample, smallerFeed), "An equal feed specification changes with source volume or capacity");
Check(!NativeLiquidCompositionMath.SameSpecification(feedSample, feedSample with {
    Contents = feedSample.Contents.Select(part => part.LiquidId == "mineral" ? part with { LiquidId = "salt" } : part).ToArray() }),
    "Liquids with equal water purity but different components are interchangeable");
Check(!NativeLiquidCompositionMath.SameSpecification(feedSample, feedSample with {
    Contents = feedSample.Contents.Select(part => part.LiquidId == "mineral" ? part with { Value = .21m } : part).ToArray() }),
    "A different per-part value matches the recorded feed specification");
Check(!NativeLiquidCompositionMath.SameSpecification(feedSample, feedSample with {
    Contents = feedSample.Contents.Select(part => part.LiquidId == "mineral" ? part with { QualityBasis = .41m } : part).ToArray() }),
    "A different per-part quality basis matches the recorded feed specification");
Check(!NativeLiquidCompositionMath.SameSpecification(feedSample, new NativeMachineLiquidSnapshot(10000, Array.Empty<NativeMachineLiquidPart>())),
    "An empty source matches a recorded feed specification");

// Repeated odd-sized draws exercise largest-remainder volume allocation and
// retain per-component prices/quality bases across a real JSON ledger.
var water = new NativeMachineLiquidSnapshot(1000, new[] {
    new NativeMachineLiquidPart("water", 331) { Value = 9.93m, QualityBasis = 6.62m },
    new NativeMachineLiquidPart("salt", 167) { Value = 3.34m, QualityBasis = 1.67m },
    new NativeMachineLiquidPart("acid", 5) { Value = .15m, QualityBasis = .1m } });
for (int draw = 1; draw <= water.TotalParts; draw++)
{
    var remaining = MachineBatchMath.Consume(water, draw, null);
    var drawn = NativeLiquidCompositionMath.Consumed(water, remaining);
    var empty = new NativeMachineLiquidSnapshot(1000, water.Contents.Select(part => new NativeMachineLiquidPart(part.LiquidId, 0)).ToArray());
    var receiver = MachineBatchMath.Add(empty, drawn);
    Check(remaining.TotalParts + receiver.TotalParts == water.TotalParts, "A bounded pour creates/loses volume");
    foreach (var part in water.Contents)
    {
        var left = remaining.Contents.Single(value => value.LiquidId == part.LiquidId);
        var moved = receiver.Contents.Single(value => value.LiquidId == part.LiquidId);
        Check(left.Parts + moved.Parts == part.Parts, "A pour changes composition");
        Check((left.Value ?? 0) + (moved.Value ?? 0) == part.Value, "A pour creates/loses component value");
        Check((left.QualityBasis ?? 0) + (moved.QualityBasis ?? 0) == part.QualityBasis, "A pour changes the quality ledger");
    }
    var restored = LiquidValueLedger.Decode(LiquidValueLedger.Encode(receiver), receiver with { Contents = receiver.Contents.Select(part => part with { Value = null, QualityBasis = null }).ToArray() });
    Check(restored.Contents.SequenceEqual(receiver.Contents), "Legacy per-liquid ledger does not round-trip");
}

// A failed middle write must compensate that partially attempted write and
// every earlier write, even if an independent compensation fails.
var rollbackOrder = new List<int>();
int physical = 20, balance = 30;
var transaction = MachineTransaction.Run(new[] {
    new MachineTransactionStep("physical", () => physical -= 7, () => { physical += 7; rollbackOrder.Add(1); return true; }),
    new MachineTransactionStep("balance", () => { balance -= 7; throw new InvalidOperationException("write-after-mutation"); }, () => { balance += 7; rollbackOrder.Add(2); return true; }) });
Check(!transaction.Committed && transaction.Restored && physical == 20 && balance == 30 && rollbackOrder.SequenceEqual(new[] { 2, 1 }), "Partial transaction was not compensated in reverse order");
rollbackOrder.Clear();
transaction = MachineTransaction.Run(new[] {
    new MachineTransactionStep("first", () => { }, () => { rollbackOrder.Add(1); return true; }),
    new MachineTransactionStep("second", () => throw new InvalidOperationException("failure"), () => { rollbackOrder.Add(2); throw new InvalidOperationException("restore-failure"); }) });
Check(!transaction.Restored && rollbackOrder.SequenceEqual(new[] { 2, 1 }), "An uncertain compensation skipped an earlier restore");

var state = new AutomatedLogisticsState { TerminalGranted = true, ResourceInstanceId = 41, ResourcesUnlocked = true, Resources = new() };
state.Machines[51] = new MachineSettings {
    MachineId = 51, HasConsumption = true, PurifierTarget = PurifierCollectionTarget.PureWater, PrinterRecipe = "printer_chip_pistol",
    PendingItems = new() { new() { ItemId = "metal_ingot", Purity = 6, Quality = 2, Remaining = 2, TargetCount = 5 } } };
state.Machines[52] = new MachineSettings { MachineId = 52, HasConsumption = true, Hydroponic = new HydroponicFeed {
    Supply = new RestockLiquid { SlotId = "hydroponic_water", VesselId = 61, WaterQuality = 2, TargetParts = feedSample.TotalParts, Remaining = 1250,
        Basis = feedSample.Contents.Select(part => new RestockLiquidPart {
            LiquidId = part.LiquidId, Parts = part.Parts, Value = part.Value, QualityBasis = part.QualityBasis }).ToList() },
    ObservedContents = new() {
        new() { LiquidId = "water", Parts = 7984, Value = 7.984m, QualityBasis = 15.968m },
        new() { LiquidId = "mineral", Parts = 8, Value = .08m, QualityBasis = .16m } },
    RequiresRecording = true } };
state.Machines[53] = new MachineSettings { MachineId = 53, FeedItemIds = new() { "corn", "meat" } };
state.Power = new() { Unlocked = true, InstanceId = 42, Width = 16, Height = 5, StoredEnergy = 4321, AquacultureDay = 8, AquacultureGenerators = new() { 88, 89 } };
string json = JsonConvert.SerializeObject(state);
var loaded = JsonConvert.DeserializeObject<AutomatedLogisticsState>(json)!;
Check(loaded.Version == 6 && loaded.IsValid() && loaded.TerminalGranted && loaded.ResourceInstanceId == 41 && loaded.Power.StoredEnergy == 4321 && loaded.Power.AquacultureGenerators.SetEquals(new[] { 88, 89 }), "Schema 6 loses rewards, UIDs or real stored energy");
Check(loaded.Machines[51].PendingItems[0].Purity == 6 && loaded.Machines[51].PendingItems[0].Remaining == 2, "Consumption debt or material identity changed on reload");
Check(loaded.Machines[51].PurifierTarget == PurifierCollectionTarget.PureWater && loaded.Machines[51].PrinterRecipe == "printer_chip_pistol",
    "The collection target or selected printer recipe changed on reload");
Check(loaded.Machines[53].FeedItemIds.SequenceEqual(new[] { "corn", "meat" }) && loaded.Machines[51].FeedItemIds.Count == 0,
    "A dispenser must retain multiple selected foods without sharing them with another machine");
var oldMachine = JsonConvert.DeserializeObject<MachineSettings>("{\"MachineId\":54}")!;
Check(oldMachine.FeedItemIds.Count == 0, "An existing save must start with no authorized food types");
loaded.Machines[53].FeedItemIds.Add("corn");
Check(!loaded.IsValid(), "Duplicate food selections must be rejected at the saved-state boundary");
loaded.Machines[53].FeedItemIds.RemoveAt(2);
var selectedFoods = loaded.Machines[53].FeedItemIds;
loaded.Machines[53].FeedItemIds = null!;
Check(!loaded.IsValid(), "A null food selection must be rejected without losing the other machine settings");
loaded.Machines[53].FeedItemIds = selectedFoods;
var hydroponic = loaded.Machines[52].Hydroponic!;
Check(hydroponic.RequiresRecording && hydroponic.Supply.SlotId == "hydroponic_water" && hydroponic.Supply.VesselId == 61 &&
    hydroponic.Supply.WaterQuality == 2 && hydroponic.Supply.TargetParts == 10000 && hydroponic.Supply.Remaining == 1250,
    "The hydroponic sample binding or pending refill volume changed on reload");
Check(hydroponic.Supply.Basis.Select(part => new NativeMachineLiquidPart(part.LiquidId, part.Parts) {
    Value = part.Value, QualityBasis = part.QualityBasis }).SequenceEqual(feedSample.Contents),
    "The hydroponic sample lost component quantities, value or quality basis on reload");
Check(hydroponic.ObservedContents.Count == 2 && hydroponic.ObservedContents[0].LiquidId == "water" && hydroponic.ObservedContents[0].Parts == 7984 &&
    hydroponic.ObservedContents[0].Value == 7.984m && hydroponic.ObservedContents[0].QualityBasis == 15.968m &&
    hydroponic.ObservedContents[1].LiquidId == "mineral" && hydroponic.ObservedContents[1].Parts == 8 &&
    hydroponic.ObservedContents[1].Value == .08m && hydroponic.ObservedContents[1].QualityBasis == .16m,
    "The observed hydroponic mixture changed on reload");
hydroponic.Supply.Remaining = 10001;
Check(!loaded.IsValid(), "Hydroponic refill debt can exceed its recorded target volume");
hydroponic.Supply.Remaining = 1250; hydroponic.Supply.TargetParts = 10001;
Check(!loaded.IsValid(), "A hydroponic sample target can disagree with its actual component total");
hydroponic.Supply.TargetParts = 10000; loaded.Version = 7;
Check(!loaded.IsValid(), "A future schema is silently accepted");
var history = new TotalHistory { Baseline = 100 };
Check(history.Settle(3, 130) && !history.Settle(3, 999) && !history.Settle(2, 999) && history.Baseline == 130, "Same-day/history-fork settlement rewrites its baseline");
for (int day = 4; day <= 20; day++) history.Settle(day, day * 10);
Check(history.Days.Count == 10 && history.Days[0].Day == 11, "Statistics do not retain exactly the last ten settlements");

// Keep state identity checks, then exercise the actual production dispatcher.
var priorStore = PlayerStore.Instance;
PlayerStore.Instance = new PlayerStore { runID = priorStore.runID, saveSlotId = priorStore.saveSlotId };
Check(NativeRunDataApi.Stage(priorStore, logisticsOwner, logisticsKey, priorStore.runID, priorStore.saveSlotId,
    priorStore.modData[logisticsKey], "{}").Status == RunDataStatus.Conflict, "A stale PlayerStore instance was accepted for staging");
PlayerStore.Instance = priorStore;
NativeProductionChecks.Run(Check);
if (args.Length >= 4) NativeProductionChecks.InspectGame(args[2], args[3], Check);

var capture = new WindowInputCapture();
capture.Open(1, false); Check(capture.BlocksPointer(2, true, true), "An inside gesture did not stay captured");
Check(capture.BlocksPointer(3, false, false), "Release frame escaped the terminal guard");
Check(!capture.BlocksPointer(4, false, true) && !capture.BlocksPointer(5, true, false), "A native drag begun outside was stolen by the terminal");

if (args.Length >= 2)
{
    string source = Path.GetFullPath(args[0]), dll = Path.GetFullPath(args[1]);
    using var mod = AssemblyDefinition.ReadAssembly(dll);
    using var game = AssemblyDefinition.ReadAssembly(Path.Combine(args.Length >= 3 ? args[2] : @"F:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed", "Assembly-CSharp.dll"));
    Check(mod.MainModule.Types.Count(type => type.Interfaces.Any(iface => iface.InterfaceType.FullName == "IMod")) == 1, "Package must contain exactly one IMod");
    Check(!mod.MainModule.AssemblyReferences.Any(reference => reference.Name.Contains("Forge", StringComparison.OrdinalIgnoreCase) || reference.Name.Contains("Il2Cpp", StringComparison.OrdinalIgnoreCase) || reference.Name.Contains("Melon", StringComparison.OrdinalIgnoreCase)), "Legacy loader/runtime dependency remains");
    Check(mod.MainModule.Resources.Count(resource => resource.Name.EndsWith(".png", StringComparison.Ordinal)) == 6, "The six device icons are not embedded");
    foreach (var (type, name, count) in new[] {
        ("PlayerStore", "EndNight", 0), ("PlayerStore", "InitialSave", 0), ("PlayerStore", "BeginDay", 0),
        ("GameItemElement", "GetTooltipBasic", 0), ("GameItemElement", "ResolveItemSprite", 0),
        ("GameItem", "MayActivateSlotItem", 0), ("GameItem", "CanActivateSlotItem", 0), ("GameItem", "ActivateSlotItem", 0),
        ("GameItem", "DisplayManufacturer", 2), ("GameItem", "MayStack", 1), ("GameItem", "MayTarget", 1), ("GameItem", "CanTarget", 1), ("GameItem", "Target", 1),
        ("InputActionManager", "Update", 0), ("CustomEventHandler", "Update", 0),
        ("StoreUIManager", "CheckRaycast", 0), ("StoreUIManager", "Update", 0), ("StoreUIManager", "ResolveEscape", 0), ("StoreUIManager", "CloseAllUI", 0),
        ("ItemContextHandler", "EqualizeButtonWidths", 0), ("ItemContextHandler", "EqualizeSortButtonWidths", 0), ("ItemContextHandler", "TriggerButton", 1),
        ("MachineHelper", "GetBodyStorage", 2), ("PowerHelper", "DrawPowerSource", 2), ("PowerHelper", "CanDrawPowerSource", 2), ("PowerHelper", "GetAvailableEnergyFromItem", 2),
        ("MachineFeedDispenser", "GetStorageGrid", 1), ("MachineFeedDispenser", "IsAutoBlendOn", 1), ("MachineFeedDispenser", "TryBlendItem", 2),
        ("PowerHelper", "GetAllPowerSourceFourniture", 1), ("PowerHelper", "ChargeAllRechargable", 1), ("PowerHelper", "ChargeBattery", 1), ("PowerHelper", "ChargeBattery", 3),
        ("SpriteHelper", "UpdateWaterContainerSprite", 1), ("WaterHelper", "TransferLiquid", 2), ("WaterHelper", "EmptyContainer", 1), ("WaterHelper", "AddLiquid", 3),
        ("AquacultureHelper", "GeneratePower", 2), ("DirectoryMaster", "Item", 1),
        ("StorageExpansionHelper", "GetKitTabs", 1), ("StorageExpansionHelper", "Expand", 2), ("StorageExpansionHelper", "RestoreTabs", 1),
        ("MachineFurnace", "Furnace", 0), ("MachinePurifier", "WaterPurifier", 0), ("MachineMoistureFarm", "MoistureFarm", 0), ("MachineDesequencer", "Desequencer", 0),
        ("MachinePrinter", "Printer", 0), ("MachineEvaporator", "Evaporator", 0), ("MachineHydroponic", "Hydroponic", 0), ("MachineDnaManipulator", "DnaManipulator", 0) })
        Check(game.MainModule.GetType(type).Methods.Any(method => method.Name == name && method.Parameters.Count == count && method.HasBody), "Required official hook/member changed: " + type + "." + name);
    var comparer = game.MainModule.GetType("InventorySortHelper").NestedTypes.SelectMany(type => type.Methods).Where(method => !method.IsStatic && method.ReturnType.FullName == "System.Int32" && method.Parameters.Count == 2 && method.Parameters.All(parameter => parameter.ParameterType.FullName == "GameItem")).ToArray();
    Check(comparer.Length == 1, "The native custom-storage comparer cannot be selected unambiguously");
    foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories).Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
        Check(!File.ReadAllText(file).Contains("PatchAll("), "Entry assembly repeats loader-owned PatchAll: " + file);
}
Console.WriteLine($"{checks} logistics checks passed (pure rules, transaction/queue boundaries, JSON and optional actual assembly metadata; no Unity/native execution).");
