using static PsModI18n.I18n;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    internal string PurifierCollectionStatus(GameItem machine)
    {
        if (machine.identifier != "water_purifier") return "";
        if (!MachineAccess.TryInventory(machine, out var inventory) || inventory?.Output is not GameSlotInventory slot || slot.childItem is not { } vessel)
            return PsModI18n.I18n.Get("insert_a_purification_vessel_first");
        if (NativeLiquidApi.Capture(vessel) is not { TotalParts: > 0 }) return "";
        int purity = WaterHelper.GetWaterPurity(vessel);
        int limit = MachinePurifier.GetMaxPurityCap(MachineryHelper.GetCurrentQualityBonus(machine));
        bool removable = Liquid.Liquids.Any(liquid => liquid.identifier != "water" && liquid.canBeRemovedByPurifier &&
            vessel.GetTagReadonly(liquid.currentPart)?.valueInt > 0);
        var target = State.Machines.GetValueOrDefault(machine.GetUniqueID())?.PurifierTarget ?? PurifierCollectionTarget.MachineLimit;
        return PurifierCollectionRules.Evaluate(purity, limit, removable, target) switch
        {
            PurifierCollectionState.Ready => "",
            PurifierCollectionState.Unremovable => PsModI18n.I18n.Get("remaining_components_cannot_be_purified_the_vessel"),
            PurifierCollectionState.Unreachable => PsModI18n.I18n.Get("current_limit_pure_water_target_99_9", limit / 100m),
            _ => PsModI18n.I18n.Get("purifying_target", purity / 100m, (target == PurifierCollectionTarget.PureWater ? 9990 : limit) / 100m)
        };
    }

    private void SynchronizePrinterRecipe(GameItem machine)
    {
        if (machine.identifier != "printer") return;
        int id = machine.GetUniqueID(); var settings = State.Machines.GetValueOrDefault(id);
        if (settings == null && NativeModuleApi.FindInstalled(machine, Content.Feeder).Count == 0) return;
        var chip = (MachineHelper.GetBodyStorage(machine, 3) as GameSlotInventory)?.childItem;
        var modules = (MachineHelper.GetBodyStorage(machine, 1) as GameInventory)?.childItems
            .Where(item => item.identifier is not (Content.Feeder or Content.Ejector)).Select(item => item.GetUniqueID()).OrderBy(uid => uid);
        string recipe = chip == null ? "" : $"{chip.GetUniqueID()}:{chip.identifier}:{chip.GetTagReadonly("PROGRESS_ITEM_TARGET_ITEM_ID_TAG")?.valueString}:{string.Join(",", modules ?? Enumerable.Empty<int>())}";
        if (recipe == (settings?.PrinterRecipe ?? "")) return;
        settings ??= new() { MachineId = id };
        settings.PrinterRecipe = recipe; settings.PendingItems.Clear(); settings.HasConsumption = false;
        State.Machines[id] = settings; Save();
    }

    internal string RecordHydroponicFeed(GameItem machine)
    {
        EnsureLoaded();
        if (!Ready || !IsCurrentRun() || machine.identifier != "hydroponic" || !MachineAccess.Eligible(machine) ||
            NativeInventoryApi.IsItemDragActive || !MachineAccess.TryInventory(machine, out var inventory) || inventory == null ||
            inventory.LiquidInputs.GetValueOrDefault(MachineAccess.HydroponicWaterSlot)?.childItem is not { } vessel ||
            !NativeInventoryApi.IsPlayerOwned(vessel) || WaterHelper.GetFilterItem(vessel) != null ||
            NativeLiquidApi.Capture(vessel) is not { TotalParts: > 0 } liquid)
            return PsModI18n.I18n.Get("insert_a_vessel_containing_the_desired_nutrient");
        var settings = Settings(machine);
        settings.Hydroponic = new()
        {
            Supply = new() { SlotId = MachineAccess.HydroponicWaterSlot, VesselId = vessel.GetUniqueID(),
                WaterQuality = NativeLiquidApi.WaterQuality(vessel), TargetParts = liquid.TotalParts, Basis = LiquidParts(liquid) },
            ObservedContents = LiquidParts(liquid)
        };
        var change = new StateChange(this);
        var result = NativeTransactionApi.Run([new("hydroponic-feed", () =>
        { change.Commit(() => State.Machines[settings.MachineId] = settings); if (!change.Verify()) throw new InvalidOperationException("Hydroponic feed readback failed"); }, change.Restore)]);
        if (!result.Committed) return PsModI18n.I18n.Get("feed_specification_was_not_saved") + result.Status;
        FillStatus[machine.GetUniqueID()] = PsModI18n.I18n.Get("feed_specification_recorded_waiting_for_native_growth");
        NotifyMachinePresentation(machine);
        return FillStatus[machine.GetUniqueID()];
    }

    internal string HydroponicFeedStatus(GameItem machine)
    {
        if (machine.identifier != "hydroponic") return "";
        var feed = State.Machines.GetValueOrDefault(machine.GetUniqueID())?.Hydroponic;
        if (feed == null) return PsModI18n.I18n.Get("load_solution_manually_and_record_its_feed");
        if (!MachineAccess.TryInventory(machine, out var inventory) || inventory == null ||
            inventory.LiquidInputs.GetValueOrDefault(MachineAccess.HydroponicWaterSlot)?.childItem is not { } vessel ||
            vessel.GetUniqueID() != feed.Supply.VesselId)
            return PsModI18n.I18n.Get("the_original_feed_vessel_changed_record_its");
        var liquid = NativeLiquidApi.Capture(vessel);
        if (feed.RequiresRecording || liquid == null || !SameObservedContents(feed.ObservedContents, liquid))
            return PsModI18n.I18n.Get("solution_changed_outside_the_observed_growth_cycle");
        return feed.Supply.Remaining > 0
            ? PsModI18n.I18n.Get("ml_pending_waiting_for_matching_solution_or", feed.Supply.Remaining / 1000m) : "";
    }

    private bool HydroponicFeedReady(GameItem machine, NativeMachineInventory inventory) =>
        TryHydroponicFeed(machine, inventory, out _, out _, out _);

    private bool TryHydroponicFeed(GameItem machine, NativeMachineInventory inventory,
        out HydroponicFeed? feed, out GameItem? vessel, out NativeMachineLiquidSnapshot? liquid)
    {
        feed = State.Machines.GetValueOrDefault(machine.GetUniqueID())?.Hydroponic;
        vessel = inventory.LiquidInputs.GetValueOrDefault(MachineAccess.HydroponicWaterSlot)?.childItem;
        liquid = vessel == null ? null : NativeLiquidApi.Capture(vessel);
        return feed != null && !feed.RequiresRecording && vessel != null && liquid != null &&
            vessel.GetUniqueID() == feed.Supply.VesselId && NativeInventoryApi.IsPlayerOwned(vessel) &&
            WaterHelper.GetFilterItem(vessel) == null && SameObservedContents(feed.ObservedContents, liquid);
    }

    private void CompleteHydroponicConsumption(GameItem machine)
    {
        int id = machine.GetUniqueID();
        if (!_consumption.Remove(id, out var before) || !RestockCurrent(machine, before.Session, out var inventory)) return;
        var feed = State.Machines.GetValueOrDefault(id)?.Hydroponic;
        var observed = before.Liquids.FirstOrDefault(value => value.SlotId == MachineAccess.HydroponicWaterSlot);
        var vessel = inventory!.LiquidInputs.GetValueOrDefault(MachineAccess.HydroponicWaterSlot)?.childItem;
        if (feed == null || feed.RequiresRecording || observed == null || vessel == null ||
            vessel.GetUniqueID() != feed.Supply.VesselId || observed.VesselId != vessel.GetUniqueID() ||
            !SameObservedContents(feed.ObservedContents, observed.Contents) || NativeLiquidApi.Capture(vessel) is not { } liquid) return;
        if (SameObservedContents(feed.ObservedContents, liquid)) return;
        int consumed = Math.Max(0, observed.Contents.TotalParts - liquid.TotalParts);
        feed.Supply.Remaining = (int)Math.Min(feed.Supply.TargetParts, (long)feed.Supply.Remaining + consumed);
        // Native growth also consumes minerals and transforms contaminants.
        // Accept that post-cycle composition, retaining the player's feed sample.
        feed.ObservedContents = LiquidParts(liquid);
        State.Machines[id].HasConsumption |= consumed > 0; Save();
    }

    private void FillHydroponic(GameItem machine, NativeMachineInventory inventory)
    {
        int id = machine.GetUniqueID();
        if (!TryHydroponicFeed(machine, inventory, out var feed, out var vessel, out var liquid))
        {
            if (feed != null && !feed.RequiresRecording) { feed.RequiresRecording = true; Save(); }
            Status[id] = HydroponicFeedStatus(machine); return;
        }
        var supply = feed!.Supply; var target = vessel!; var session = Session(inventory);
        int deficit = Math.Min(supply.Remaining, Math.Max(0, supply.TargetParts - liquid!.TotalParts));
        if (deficit != supply.Remaining) { supply.Remaining = deficit; Save(); }
        var rejected = new HashSet<IntPtr>();
        while (supply.Remaining > 0)
        {
            bool Current() => RestockCurrent(machine, session, out var actual) && actual != null &&
                ReferenceEquals(State.Machines.GetValueOrDefault(id)?.Hydroponic, feed) &&
                TryHydroponicFeed(machine, actual, out _, out _, out _);
            if (!Current()) return;
            var source = RestockSources(machine, true).FirstOrDefault(value => value.Item.Handle() != target.Handle() &&
                !rejected.Contains(value.Item.Handle()) && MatchesConsumedLiquid(supply, value.Item));
            if (source == null || target.parentInventory?.IsInsertLocked() != false || !SourceCurrent(machine, source, true)) break;
            int request = Math.Min(supply.Remaining, Math.Min(NativeLiquidApi.Capture(source.Item)!.TotalParts,
                NativeLiquidApi.Capture(target)!.CapacityParts - NativeLiquidApi.Capture(target)!.TotalParts));
            if (request <= 0) break;
            using var lease = source.Member ? BeginResourceSourceTransfer(source.Item, true, source.Tier == 0) : null;
            if (source.Member && lease == null) break;
            bool Allowed() => Current() && SourceCurrent(machine, source, true) && MatchesConsumedLiquid(supply, source.Item);
            NativeLiquidTransferResult result; _restockLiquidTransfers.Add(source.Item.Handle());
            try { result = NativeLiquidTransferApi.Pour(source.Item, target, request, Allowed); }
            finally { _restockLiquidTransfers.Remove(source.Item.Handle()); }
            if (!RestockCurrent(machine, session, out _) || !ReferenceEquals(State.Machines.GetValueOrDefault(id)?.Hydroponic, feed)) return;
            if (result.Indeterminate)
            {
                feed.RequiresRecording = true; Save();
                Status[id] = PsModI18n.I18n.Get("feed_transfer_could_not_be_verified_check_2"); return;
            }
            supply.Remaining -= result.PartsMoved;
            feed.ObservedContents = LiquidParts(NativeLiquidApi.Capture(target) ?? throw new InvalidOperationException("Hydroponic vessel unavailable after transfer"));
            if (result.PartsMoved == 0) rejected.Add(source.Item.Handle()); else Save();
        }
        Status[id] = supply.Remaining == 0
            ? PsModI18n.I18n.Get("feed_specification_recorded_actual_consumption_replenished") : HydroponicFeedStatus(machine);
    }
}
