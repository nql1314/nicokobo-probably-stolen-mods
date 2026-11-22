using static PsModI18n.I18n;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private sealed record RestockSession(int Generation, string? Run, int Slot, IntPtr Store, IntPtr Input);
    private sealed record ObservedItem(string ItemId, int Count, int Purity, int Quality, string MaterialState, bool Manual);
    private sealed record ObservedLiquid(string SlotId, int VesselId, int WaterQuality, NativeMachineLiquidSnapshot Contents);
    private sealed record ConsumptionSnapshot(RestockSession Session, ObservedItem[] Items, ObservedLiquid[] Liquids);
    private readonly Dictionary<int, ConsumptionSnapshot> _consumption = new();

    private static int ComponentGrade(GameItem item) =>
        item.IsTag("nicokobo.mechcore.synthesis.component_grade") &&
        item.GetTagReadonly("nicokobo.mechcore.synthesis.component_grade")?.valueInt is int value && value is >= 0 and <= 3 ? value + 1 : 0;
    private static string ConsumedMaterialState(GameItem item)
    {
        // Record the consumed native state; never run recipe predicates to
        // choose a substitute. Public authenticity labels can conceal fakes.
        if (item.identifier == "neuroactive_perfume")
            return $"uses:{item.IsTag(UseCountHelper.USE_COUNT_ITEM_TAG)}:{item.GetTagReadonly(UseCountHelper.MAX_USE_COUNT_INT)?.valueInt}:{item.GetTagReadonly(UseCountHelper.CURRENT_USE_COUNT_INT)?.valueInt}";
        if (item.identifier == "wine_berry")
            return $"berry:{item.IsTag("BERRY_TAG")}:{item.GetTagReadonly("BERRY_CONDITION_INT")?.valueInt}";
        if (item.IsTag("SCAV_SCANNER_DOUBLE_CHANCE") && item.GetTagReadonly("SCAV_SCANNER_DOUBLE_CHANCE") is { } scanner)
            return "scanner:" + scanner.valueInt;
        if (item.identifier != "large_purple_injector") return "";
        var conditions = new List<string>();
        if (item.itemFeatures != null)
            foreach (var feature in item.itemFeatures)
            {
                if (feature == null || !feature.useCondition || feature.realCondition is not { } real ||
                    real.Handle() == IntPtr.Zero || real.category != "CATEGORY_GENUINE_INJECTOR") continue;
                conditions.Add(real.identifier.Length + ":" + real.identifier);
            }
        return "injector:" + string.Concat(conditions.OrderBy(value => value, StringComparer.Ordinal));
    }
    private static bool NeedsManualRestock(GameItem item) => item.IsGameItemType("MACHINE") ||
        item.IsGameItemType("STORAGE") || item.IsTag("CONTAINER_TAG") || item.IsTag("LIQUID_CONTAINER_TAG") || NativeLiquidApi.IsContainer(item);
    private static bool MatchesConsumed(RestockItem demand, GameItem item)
    {
        if (item.identifier != demand.ItemId || Purity(item) != demand.Purity || ComponentGrade(item) != demand.Quality) return false;
        // Existing v6 perfume debts have no use-count signature. Its recipe
        // requires a full bottle; preserve the debt without inventing old counts.
        if (demand.ItemId == "neuroactive_perfume" && demand.MaterialState == "")
            return item.IsTag(UseCountHelper.USE_COUNT_ITEM_TAG) &&
                item.GetTagReadonly(UseCountHelper.MAX_USE_COUNT_INT)?.valueInt is int maximum && maximum > 0 &&
                item.GetTagReadonly(UseCountHelper.CURRENT_USE_COUNT_INT)?.valueInt == maximum;
        return ConsumedMaterialState(item) == demand.MaterialState;
    }
    private static int PresentUnits(GameInventory? input, RestockItem demand) => input == null ? 0 : input.childItems.ToArray()
        .Where(item => item != null && item.parentInventory?.Handle() == input.Handle() && item.unitCount > 0 && MatchesConsumed(demand, item))
        .Sum(item => item.unitCount);
    private RestockSession Session(NativeMachineInventory inventory) =>
        new(_loadGeneration, _run, _slot, PlayerStore.Instance!.Handle(), inventory.ItemInput?.Handle() ?? IntPtr.Zero);
    private bool RestockCurrent(GameItem machine, RestockSession session, out NativeMachineInventory? inventory)
    {
        inventory = null;
        return Ready && _loadGeneration == session.Generation && IsCurrentRun() && _run == session.Run && _slot == session.Slot &&
            PlayerStore.Instance?.Handle() == session.Store && MachineAccess.Eligible(machine) && FillEnabled(machine) &&
            NativeModuleApi.FindInstalled(machine, Content.Feeder).Count > 0 &&
            MachineAccess.TryInventory(machine, out inventory) && inventory != null &&
            (inventory.ItemInput?.Handle() ?? IntPtr.Zero) == session.Input;
    }

    private void BeforeBatch(NativeMachineAutomationContext context)
    {
        EnsureLoaded();
        if (!Ready || MachineAccess.IsNative(context.Machine.identifier)) return;
        FillOnce(context.Machine, context.Inventory, context, "forge-before-batch");
        CaptureConsumption(context.Machine, context.Inventory);
    }
    private void AfterBatch(NativeMachineAutomationContext context)
    {
        if (MachineAccess.IsNative(context.Machine.identifier)) return;
        bool produced = context.Result?.StartsWith("produced:", StringComparison.Ordinal) == true;
        CompleteConsumption(context.Machine, produced || context.Work?.Consumed.Count > 0 || context.Work?.WaterPartsConsumed > 0);
        if (produced) EjectOnce(context.Machine);
    }
    internal bool BeforeFurnaceBatch(GameItem machine, GameInventory input, out long before)
    {
        before = -1; EnsureLoaded();
        if (!Ready || !IsCurrentRun() || !MachineAccess.Eligible(machine)) return true;
        if (!MachineAccess.TryInventory(machine, out var inventory) || inventory?.ItemInput?.Handle() != input.Handle()) return true;
        FillOnce(machine, inventory, trigger: "furnace-before-batch");
        if (!Ready || !IsCurrentRun() ||
            !MachineAccess.TryInventory(machine, out inventory) || inventory?.ItemInput?.Handle() != input.Handle()) return false;
        CaptureConsumption(machine, inventory);
        before = FurnaceInputUnits(input); return true;
    }
    internal void AfterFurnaceBatch(GameItem machine, GameInventory input, long before)
    {
        bool consumed = before >= 0 && FurnaceInputUnits(input) < before;
        CompleteConsumption(machine, consumed);
        if (consumed) EjectOnce(machine);
        // Matrix observes the input decrease in its later postfix. Refill at
        // the next cycle's prefix or background pass, after that observation.
    }
    private static long FurnaceInputUnits(GameInventory input) => input.childItems.ToArray()
        .Where(item => item != null && item.Handle() != IntPtr.Zero && item.unitCount > 0).Sum(item => (long)item.unitCount);

    private void CaptureConsumption(GameItem machine, NativeMachineInventory inventory)
    {
        int id = machine.GetUniqueID(); _consumption.Remove(id);
        if (!Ready || !IsCurrentRun()) return;
        var session = Session(inventory);
        if (!RestockCurrent(machine, session, out _)) return;
        _consumption[id] = new(session, ReadInputItems(inventory.ItemInput), ReadInputLiquids(inventory));
    }

    private static ObservedItem[] ReadInputItems(GameInventory? input) =>
        input?.childItems.ToArray().Where(item => item != null && item.unitCount > 0 &&
            item.parentInventory?.Handle() == input.Handle() && NativeInventoryApi.IsPlayerOwned(item))
            .Select(item => new ObservedItem(item.identifier, item.unitCount, Purity(item), ComponentGrade(item), ConsumedMaterialState(item), NeedsManualRestock(item)))
            .ToArray() ?? Array.Empty<ObservedItem>();

    private static ObservedLiquid[] ReadInputLiquids(NativeMachineInventory inventory) =>
        inventory.LiquidInputs.Select(pair => (pair.Key, Vessel: pair.Value.childItem))
            .Where(pair => pair.Vessel != null && NativeInventoryApi.IsPlayerOwned(pair.Vessel))
            .Select(pair => new ObservedLiquid(pair.Key, pair.Vessel!.GetUniqueID(), Math.Clamp(NativeLiquidApi.WaterQuality(pair.Vessel), 0, 3),
                NativeLiquidApi.Capture(pair.Vessel)!)).Where(liquid => liquid.Contents is { TotalParts: > 0 }).ToArray();

    private void CompleteConsumption(GameItem machine, bool produced)
    {
        int id = machine.GetUniqueID();
        if (!_consumption.Remove(id, out var before) || !produced || !RestockCurrent(machine, before.Session, out var inventory)) return;
        try
        {
            var settings = State.Machines.GetValueOrDefault(id) ?? new MachineSettings { MachineId = id };
            int consumedUnits = 0, consumedParts = 0;
            foreach (var group in before.Items.GroupBy(item => (item.ItemId, item.Purity, item.Quality, item.MaterialState, item.Manual)))
            {
                var key = group.Key; int previous = group.Sum(item => item.Count);
                var demand = settings.PendingItems.FirstOrDefault(item => item.ItemId == key.ItemId && item.Purity == key.Purity &&
                    item.Quality == key.Quality && item.MaterialState == key.MaterialState && item.Manual == key.Manual) ??
                    new RestockItem { ItemId = key.ItemId, Purity = key.Purity, Quality = key.Quality, MaterialState = key.MaterialState, Manual = key.Manual };
                int current = PresentUnits(inventory!.ItemInput, demand), consumed = Math.Max(0, previous - current);
                if (consumed == 0) continue;
                int pending = Math.Min(demand.Remaining, Math.Max(0, demand.TargetCount - previous));
                demand.Remaining = checked(pending + consumed); demand.TargetCount = checked(current + demand.Remaining);
                if (!settings.PendingItems.Contains(demand)) settings.PendingItems.Add(demand);
                consumedUnits = checked(consumedUnits + consumed);
            }
            foreach (var liquid in before.Liquids)
            {
                if (!inventory!.LiquidInputs.TryGetValue(liquid.SlotId, out var slot) || slot.childItem is not { } vessel ||
                    vessel.GetUniqueID() != liquid.VesselId || NativeLiquidApi.Capture(vessel) is not { } current) continue;
                int consumed = liquid.Contents.TotalParts - current.TotalParts;
                if (consumed <= 0) continue;
                var demand = settings.PendingLiquids.FirstOrDefault(item => item.SlotId == liquid.SlotId && item.VesselId == liquid.VesselId &&
                    SameConsumedLiquid(item, liquid));
                if (demand == null)
                {
                    demand = new() { SlotId = liquid.SlotId, VesselId = liquid.VesselId, WaterQuality = liquid.WaterQuality,
                        Basis = liquid.Contents.Contents.Where(part => part.Parts > 0).Select(part => new RestockLiquidPart
                        { LiquidId = part.LiquidId, Parts = part.Parts, Value = part.Value, QualityBasis = part.QualityBasis }).ToList() };
                }
                int pending = Math.Min(demand.Remaining, Math.Max(0, demand.TargetParts - liquid.Contents.TotalParts));
                demand.Remaining = checked(pending + consumed); demand.TargetParts = checked(current.TotalParts + demand.Remaining);
                // Prefer the material actually consumed most recently when an
                // empty vessel could accept several incompatible old demands.
                settings.PendingLiquids.Remove(demand); settings.PendingLiquids.Insert(0, demand);
                consumedParts = checked(consumedParts + consumed);
            }
            if (consumedUnits == 0 && consumedParts == 0) return;
            settings.HasConsumption = true; State.Machines[id] = settings; Save();
            NotifyMachinePresentation(machine);
            info?.Invoke($"[AutomatedLogistics/Restock] machine={machine.identifier}#{id}; phase=consumed; units={consumedUnits}; liquidParts={consumedParts}; pending=recorded");
        }
        catch (Exception ex) { log(PsModI18n.I18n.Get("consumption_tracking_failed") + ex.Message); }
    }

    private static bool MatchesConsumedLiquid(RestockLiquid demand, GameItem source)
    {
        var snapshot = NativeLiquidApi.Capture(source);
        if (snapshot == null || snapshot.TotalParts <= 0 || WaterHelper.GetFilterItem(source) != null) return false;
        return CompatibleConsumedLiquid(demand, snapshot, NativeLiquidApi.WaterQuality(source));
    }
    private static bool SameConsumedLiquid(RestockLiquid demand, ObservedLiquid observed) =>
        demand.WaterQuality == observed.WaterQuality && CompatibleConsumedLiquid(demand, observed.Contents, observed.WaterQuality, identity: true);
    private static bool CompatibleConsumedLiquid(RestockLiquid demand, NativeMachineLiquidSnapshot snapshot, int waterQuality, bool identity = false)
    {
        if (demand.SlotId == MachineAccess.HydroponicWaterSlot)
            return NativeLiquidCompositionMath.SameSpecification(new(demand.TargetParts,
                demand.Basis.Select(part => new NativeMachineLiquidPart(part.LiquidId, part.Parts)
                { Value = part.Value, QualityBasis = part.QualityBasis }).ToArray()), snapshot);
        var expected = demand.Basis.ToDictionary(part => part.LiquidId, part => part.Parts);
        var actual = snapshot.Contents.Where(part => part.Parts > 0).ToDictionary(part => part.LiquidId, part => part.Parts);
        if (expected.ContainsKey("water"))
        {
            if (demand.WaterQuality is >= 1 and <= 3) return LiquidRules.CompatibleWater(expected, actual, demand.WaterQuality, waterQuality);
            // Unknown-grade water remains manual-only, but identical observed
            // debt must still coalesce instead of growing one entry per batch.
            if (!identity && (demand.SlotId != MachineAccess.PurifierWaterSlot || waterQuality != 0)) return false;
        }
        if (!LiquidRules.SameMixture(expected, actual)) return false;
        return demand.Basis.All(part => snapshot.Contents.Any(other => other.LiquidId == part.LiquidId &&
            part.Value.HasValue == other.Value.HasValue && part.QualityBasis.HasValue == other.QualityBasis.HasValue &&
            (part.Value == null || part.Value.Value * other.Parts == other.Value!.Value * part.Parts) &&
            (part.QualityBasis == null || part.QualityBasis.Value * other.Parts == other.QualityBasis!.Value * part.Parts)));
    }

    internal string Preview(GameItem machine)
    {
        if (!Ready || !MachineAccess.Eligible(machine)) return PsModI18n.I18n.Get("machine_is_unavailable");
        if (MachineAccess.IsFeedDispenser(machine)) return FeedDispenserPlan(machine);
        if (!MachineAccess.HasRestockInput(machine)) return PsModI18n.I18n.Get("the_moisture_farm_needs_no_ingredients_insert");
        if (NativeModuleApi.FindInstalled(machine, Content.Feeder).Count == 0) return PsModI18n.I18n.Get("restocking_module_not_installed");
        var settings = Settings(machine);
        if (!settings.FillEnabled) return PsModI18n.I18n.Get("restocking_paused");
        var lines = new List<string> { PsModI18n.I18n.Get("restocks_materials_actually_consumed_by_processing"), PsModI18n.I18n.Get("sources_resource_network_nearby_containers_warehouse") };
        lines.AddRange(settings.PendingItems.Select(item => PsModI18n.I18n.Get("pending", MachineAccess.Name(item.ItemId), item.Remaining) + (item.Manual ? PsModI18n.I18n.Get("load_manually") : "")));
        lines.AddRange(settings.PendingLiquids.Select(item => PsModI18n.I18n.Get("pending_liquid_ml", item.SlotId, item.Remaining / 1000m)));
        if (settings.PendingItems.Count == 0 && settings.PendingLiquids.Count == 0)
            lines.Add(settings.HasConsumption ? PsModI18n.I18n.Get("no_consumed_materials_await_restocking") : PsModI18n.I18n.Get("no_consumption_recorded_load_ingredients_manually_and"));
        return string.Join("\n", lines);
    }

    internal void FillOnce(GameItem machine, NativeMachineInventory? inventory = null, NativeMachineAutomationContext? context = null,
        string trigger = "manual")
    {
        if (!Ready || !IsCurrentRun() || !MachineAccess.Eligible(machine)) return;
        int id = machine.GetUniqueID(); bool installed = MachineAccess.IsFeedDispenser(machine) || NativeModuleApi.FindInstalled(machine, Content.Feeder).Count > 0;
        int generation = _loadGeneration;
        bool trace = debug != null && trigger != "scan", report = info != null && trigger != "scan" && installed;
        if (!_fillBusy.Add(id)) return;
        int movedUnits = 0, movedParts = 0;
        try
        {
            SynchronizePrinterRecipe(machine);
            if (!MachineAccess.HasRestockInput(machine))
            {
                Status[id] = machine.identifier == "evaporator" ? PsModI18n.I18n.Get("load_the_source_liquid_manually") :
                    machine.identifier == "plant_dna_manipulator" ? PsModI18n.I18n.Get("working_disks_and_seeds_remain_under_native") : PsModI18n.I18n.Get("no_ingredients_to_restock");
                return;
            }
            if (!installed) { Status[id] = PsModI18n.I18n.Get("restocking_module_not_installed"); return; }
            if (!FillEnabled(machine)) { Status[id] = PsModI18n.I18n.Get("restocking_paused"); return; }
            if (context?.Blocked == true) { Status[id] = PsModI18n.I18n.Get("this_batch_was_blocked"); return; }
            if (MachineAccess.IsFeedDispenser(machine)) { movedUnits = FillFeedDispenser(machine); return; }
            if (!MachineAccess.TryInventory(machine, out var actual) || actual == null ||
                inventory != null && inventory.ItemInput?.Handle() != actual.ItemInput?.Handle()) { Status[id] = PsModI18n.I18n.Get("machine_slots_are_unavailable"); return; }
            inventory = actual; var session = Session(inventory);
            if (machine.identifier == "hydroponic") { FillHydroponic(machine, inventory); return; }
            if (trace) TraceRestock(machine, trigger, "begin", WarehouseDiagnostic());
            var settings = State.Machines.GetValueOrDefault(id);
            if (settings == null || settings.PendingItems.Count == 0 && settings.PendingLiquids.Count == 0)
            {
                Status[id] = settings?.HasConsumption == true ? PsModI18n.I18n.Get("no_consumed_materials_await_restocking") :
                    inventory.ItemInput?.childItems.Count > 0 || inventory.LiquidInputs.Values.Any(slot =>
                        slot.childItem is { } vessel && NativeLiquidApi.Capture(vessel)?.TotalParts > 0)
                        ? PsModI18n.I18n.Get("ingredients_loaded_waiting_for_processing_to_record") : PsModI18n.I18n.Get("no_consumption_recorded_load_ingredients_manually_and");
                return;
            }
            if (trace) TraceSources(machine, inventory.ItemInput?.childItems.ToArray() ?? Array.Empty<GameItem>(),
                RestockSources(machine, false), RestockSources(machine, true));
            bool Current() => RestockCurrent(machine, session, out _) && ReferenceEquals(State.Machines.GetValueOrDefault(id), settings);
            void Persist()
            {
                settings.PendingItems.RemoveAll(item => item.Remaining == 0);
                settings.PendingLiquids.RemoveAll(item => item.Remaining == 0); Save();
            }
            foreach (var demand in settings.PendingLiquids.ToArray())
            {
                if (!Current()) { context?.Block(); return; }
                if (!inventory.LiquidInputs.TryGetValue(demand.SlotId, out var slot) || slot.childItem is not { } target ||
                    target.GetUniqueID() != demand.VesselId || NativeLiquidApi.Capture(target) is not { } liquid) continue;
                // A manually seeded different liquid does not repay this debt.
                // Keep that record, but never top up or reconcile it against an
                // incompatible current receiver.
                if (liquid.TotalParts > 0 && !MatchesConsumedLiquid(demand, target)) continue;
                int deficit = Math.Min(demand.Remaining, Math.Max(0, demand.TargetParts - liquid.TotalParts));
                if (deficit != demand.Remaining) { demand.Remaining = deficit; Persist(); }
                var rejected = new HashSet<IntPtr>();
                while (demand.Remaining > 0)
                {
                    if (!Current()) { context?.Block(); return; }
                    if (slot.childItem?.Handle() != target.Handle() || target.parentInventory?.Handle() != slot.Handle())
                    { context?.Block(); Status[id] = PsModI18n.I18n.Get("receiving_vessel_changed_pending_consumption_remains_recorded"); return; }
                    var source = RestockSources(machine, true).FirstOrDefault(source => !rejected.Contains(source.Item.Handle()) &&
                        MatchesConsumedLiquid(demand, source.Item) && LiquidFeeding.SameOutputQuality(source.Item, target, demand.SlotId == MachineAccess.PurifierWaterSlot));
                    if (source == null) break;
                    int request = Math.Min(demand.Remaining, Math.Min(NativeLiquidApi.Capture(source.Item)!.TotalParts,
                        NativeLiquidApi.Capture(target)!.CapacityParts - NativeLiquidApi.Capture(target)!.TotalParts));
                    if (request <= 0 || !SourceCurrent(machine, source, true)) break;
                    using var lease = source.Member ? BeginResourceSourceTransfer(source.Item, true, source.Tier == 0) : null;
                    if (source.Member && lease == null) break;
                    bool Allowed() => Current() && slot.childItem?.Handle() == target.Handle() && target.parentInventory?.Handle() == slot.Handle() &&
                        SourceCurrent(machine, source, true) && MatchesConsumedLiquid(demand, source.Item) && LiquidFeeding.SameOutputQuality(source.Item, target, demand.SlotId == MachineAccess.PurifierWaterSlot);
                    NativeLiquidTransferResult result; _restockLiquidTransfers.Add(source.Item.Handle());
                    try { result = NativeLiquidTransferApi.Pour(source.Item, target, request, Allowed); }
                    finally { _restockLiquidTransfers.Remove(source.Item.Handle()); }
                    if (!Current()) { context?.Block(); return; }
                    if (result.Indeterminate)
                    { context?.Block(); Status[id] = PsModI18n.I18n.Get("restocking_could_not_be_verified") + result.Status; return; }
                    movedParts = checked(movedParts + result.PartsMoved); demand.Remaining -= result.PartsMoved;
                    if (trace) TraceRestock(machine, trigger, "liquid", $"tier={source.Tier}; requested={request}; moved={result.PartsMoved}; status={result.Status}");
                    if (result.PartsMoved == 0) rejected.Add(source.Item.Handle()); else Persist();
                    if (result.PartsMoved > 0 && result.PartsMoved < request) break;
                }
            }
            foreach (var demand in settings.PendingItems.ToArray())
            {
                if (!Current()) { context?.Block(); return; }
                if (demand.Manual || inventory.ItemInput is not { } input) continue;
                int deficit = Math.Min(demand.Remaining, Math.Max(0, demand.TargetCount - PresentUnits(input, demand)));
                if (deficit != demand.Remaining) { demand.Remaining = deficit; Persist(); }
                var rejected = new HashSet<IntPtr>();
                while (demand.Remaining > 0)
                {
                    if (!Current()) { context?.Block(); return; }
                    var source = RestockSources(machine, false).FirstOrDefault(source => !rejected.Contains(source.Item.Handle()) && MatchesConsumed(demand, source.Item));
                    if (source == null || input.IsInsertLocked() || !SourceCurrent(machine, source, false)) break;
                    using var lease = source.Member ? BeginResourceSourceTransfer(source.Item, false, source.Tier == 0) : null;
                    if (source.Member && lease == null) break;
                    int request = Math.Min(demand.Remaining, source.Item.MaxNumRemove());
                    var result = input.TryCast<GameGridInventory>() is { } grid ? NativeItemTransferApi.Move(source.Item, grid, request) :
                        input.TryCast<GameSlotInventory>() is { } itemSlot ? NativeItemTransferApi.Move(source.Item, itemSlot, request) :
                        new NativeItemTransferResult(0, false, "unsupported-input");
                    if (!Current()) { context?.Block(); return; }
                    if (result.Indeterminate)
                    { context?.Block(); Status[id] = PsModI18n.I18n.Get("restocking_could_not_be_verified") + result.Status; return; }
                    movedUnits = checked(movedUnits + result.Moved); demand.Remaining -= result.Moved;
                    if (trace) TraceRestock(machine, trigger, "solid", $"tier={source.Tier}; source={source.Item.identifier}#{source.Item.GetUniqueID()}; requested={request}; moved={result.Moved}; status={result.Status}");
                    if (result.Moved == 0) rejected.Add(source.Item.Handle()); else Persist();
                    if (result.Moved > 0 && result.Moved < request) break;
                }
            }
            Status[id] = settings.PendingItems.Count == 0 && settings.PendingLiquids.Count == 0 ? PsModI18n.I18n.Get("materials_restocked_to_replace_actual_consumption") :
                settings.PendingItems.Any(item => item.Manual) ? PsModI18n.I18n.Get("partly_restocked_containers_or_machine_ingredients_must") : PsModI18n.I18n.Get("consumed_materials_still_pending_waiting_for_matching");
        }
        catch (Exception ex)
        {
            context?.Block();
            if (_loadGeneration == generation) Status[id] = PsModI18n.I18n.Get("restocking_failed") + ex.Message;
        }
        finally
        {
            _fillBusy.Remove(id);
            if (_loadGeneration == generation)
            {
                FillStatus[id] = Status.GetValueOrDefault(id) ?? ""; NotifyMachinePresentation(machine);
                if (movedUnits > 0) NetworkPresentationChanged?.Invoke();
            }
            if (trace) TraceRestock(machine, trigger, "end", "status=" + Status.GetValueOrDefault(id));
            if (report) info?.Invoke($"[AutomatedLogistics/Restock] machine={machine.identifier}#{id}; trigger={trigger}; phase=result; solidsMoved={movedUnits}; liquidPartsMoved={movedParts}; status={Status.GetValueOrDefault(id)}");
        }
    }
}
