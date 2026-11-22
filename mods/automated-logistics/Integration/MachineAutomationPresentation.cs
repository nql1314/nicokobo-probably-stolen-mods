using static PsModI18n.I18n;

namespace Nicokobo.AutomatedLogistics.Integration;

internal enum MachineAutomationSignal { Enabled, Shortage, Disabled }
internal sealed record MachineAutomationStatus(MachineAutomationSignal Signal, string Description);
internal sealed record MachineAutomationView(GameItem Machine, MachineAutomationStatus Status);

internal sealed partial class AutomatedLogisticsRuntime
{
    private readonly Dictionary<int, MachineAutomationStatus> _machinePresentation = new();
    private readonly Dictionary<int, (string? Fill, string? Eject)> _machineResultPresentation = new();
    internal event Action? MachinePresentationChanged;

    internal MachineAutomationStatus MachineAutomationStatus(GameItem machine)
    {
        if (MachineAccess.IsFeedDispenser(machine)) return FeedDispenserAutomationStatus(machine);
        int id = machine.GetUniqueID(); var settings = State.Machines.GetValueOrDefault(id);
        bool fill = MachineAccess.HasRestockInput(machine) && (settings?.FillEnabled ?? true) && NativeModuleApi.FindInstalled(machine, Content.Feeder).Count > 0;
        bool eject = (settings?.EjectEnabled ?? true) && NativeModuleApi.FindInstalled(machine, Content.Ejector).Count > 0;
        if (!fill && !eject) return new(MachineAutomationSignal.Disabled, machine.identifier switch
        {
            "evaporator" => PsModI18n.I18n.Get("install_and_enable_an_ejector_module_to"),
            "plant_dna_manipulator" => PsModI18n.I18n.Get("install_and_enable_an_ejector_module_to_2"),
            _ => PsModI18n.I18n.Get("automation_disabled")
        });
        if (!MachineAccess.TryInventory(machine, out var inventory) || inventory == null)
            return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("machine_slots_are_unavailable_2"));
        if (eject && machine.identifier == "water_purifier" && PurifierCollectionStatus(machine) is { Length: > 0 } purifierStatus)
            return new(MachineAutomationSignal.Shortage, purifierStatus);
        if (fill)
        {
            if (machine.identifier == "hydroponic" && HydroponicFeedStatus(machine) is { Length: > 0 } hydroponicStatus)
                return new(MachineAutomationSignal.Shortage, hydroponicStatus);
            if (settings?.PendingItems.Any(demand => Math.Min(demand.Remaining,
                Math.Max(0, demand.TargetCount - PresentUnits(inventory.ItemInput, demand))) > 0) == true)
                return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("consumed_materials_pending_waiting_for_materials_or"));
            // Hydroponic growth changes the remaining nutrient concentration;
            // its recorded feed and observed remainder are checked separately.
            foreach (var demand in machine.identifier == "hydroponic" ? [] : settings?.PendingLiquids ?? [])
            {
                if (demand.Remaining == 0) continue;
                if (!inventory.LiquidInputs.TryGetValue(demand.SlotId, out var slot) || slot.childItem is not { } vessel ||
                    vessel.GetUniqueID() != demand.VesselId || NativeLiquidApi.Capture(vessel) is not { } liquid)
                    return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("the_original_receiving_vessel_for_pending_liquid"));
                if (liquid.TotalParts > 0 && !MatchesConsumedLiquid(demand, vessel))
                    return new(MachineAutomationSignal.Shortage, machine.identifier == "water_purifier"
                        ? PsModI18n.I18n.Get("purified_water_remains_raw_water_restocking_is") : PsModI18n.I18n.Get("pending_liquid_differs_from_the_current_ingredients"));
                if (Math.Min(demand.Remaining, Math.Max(0, demand.TargetParts - liquid.TotalParts)) > 0)
                    return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("consumed_liquid_awaits_restocking"));
            }
            if (machine.identifier != "hydroponic" && machine.GetTagReadonly("MACHINE_STATE_TAG")?.valueString != "STATE_WORKING" &&
                ReadInputItems(inventory.ItemInput).Length == 0 && ReadInputLiquids(inventory).Length == 0)
                return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("no_ingredients_loaded_add_them_manually_first"));
        }
        if (machine.identifier == "hydroponic" && (MachineHelper.GetBodyStorage(machine, 3) as GameSlotInventory)?.childItem == null)
            return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("waiting_for_manual_planting_returned_seeds_are"));
        if (machine.identifier == "evaporator" && ((MachineHelper.GetBodyStorage(machine, 2) as GameSlotInventory)?.childItem is not { } feed ||
            NativeLiquidApi.Capture(feed) is not { TotalParts: > 0 }))
            return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("load_feed_manually_existing_distillate_can_still"));
        if (inventory.Battery == null || !MachineHelper.CanPower(machine, inventory.Battery))
            return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("native_processing_lacks_power_existing_outputs_can"));
        return new(MachineAutomationSignal.Enabled, machine.identifier switch
        {
            "plant_dna_manipulator" => PsModI18n.I18n.Get("collecting_native_ejected_disks_waiting_for_native"),
            "evaporator" => PsModI18n.I18n.Get("distillate_collection_enabled_feed_remains_under_manual"),
            _ => fill && eject ? PsModI18n.I18n.Get("restocking_and_collection_enabled_waiting_for_native") :
                fill ? PsModI18n.I18n.Get("restocking_enabled_waiting_for_native_processing") : PsModI18n.I18n.Get("collection_enabled")
        });
    }

    internal IReadOnlyList<MachineAutomationView> LogisticsMachines()
    {
        var views = Machines().Select(machine => new MachineAutomationView(machine, MachineAutomationStatus(machine)))
            .OrderBy(view => view.Status.Signal).ThenBy(view => view.Machine.GetUniqueID()).ToArray();
        foreach (var view in views)
        {
            int id = view.Machine.GetUniqueID();
            _machinePresentation[id] = view.Status;
            _machineResultPresentation[id] = (FillStatus.GetValueOrDefault(id), EjectStatus.GetValueOrDefault(id));
        }
        return views;
    }

    private void NotifyMachinePresentation(GameItem machine)
    {
        if (!Ready || !IsCurrentRun()) return;
        try
        {
            int id = machine.GetUniqueID(); var status = MachineAutomationStatus(machine);
            var results = (FillStatus.GetValueOrDefault(id), EjectStatus.GetValueOrDefault(id));
            if (_machinePresentation.TryGetValue(id, out var previous) && previous == status &&
                _machineResultPresentation.TryGetValue(id, out var previousResults) && previousResults == results) return;
            _machinePresentation[id] = status; _machineResultPresentation[id] = results; MachinePresentationChanged?.Invoke();
        }
        catch (Exception ex) { debug?.Invoke(PsModI18n.I18n.Get("automation_status_could_not_be_read") + ex.Message); }
    }
}
