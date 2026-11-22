using static PsModI18n.I18n;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    internal bool BeforeNativeMachineBatch(GameItem machine, out int generation)
    {
        generation = -1; EnsureLoaded();
        if (!Ready || !IsCurrentRun() || !MachineAccess.Eligible(machine)) return true;
        // Clear last night's completed output before the native callback checks
        // capacity. This does not call the machine or grant another cycle.
        EjectOnce(machine);
        FillOnce(machine, trigger: "native-before-batch");
        if (!Ready || !IsCurrentRun() ||
            !MachineAccess.TryInventory(machine, out var inventory) || inventory == null) return false;
        if (machine.identifier == "water_purifier") PreparePurifierCycle(machine, inventory);
        else if (machine.identifier is "desequencer" or "printer") CaptureConsumption(machine, inventory);
        else if (machine.identifier == "hydroponic" && HydroponicFeedReady(machine, inventory)) CaptureConsumption(machine, inventory);
        generation = _loadGeneration;
        return true;
    }

    internal void AfterNativeMachineBatch(GameItem machine, int generation, bool ran)
    {
        if (generation < 0 || generation != _loadGeneration || !Ready || !IsCurrentRun()) return;
        if (!ran) { _consumption.Remove(machine.GetUniqueID()); return; }
        if (machine.identifier is "desequencer" or "printer") CompleteConsumption(machine, true);
        else if (machine.identifier == "hydroponic") CompleteHydroponicConsumption(machine);
        else if (machine.identifier == "water_purifier" && MachineAccess.TryInventory(machine, out var inventory) &&
            inventory?.LiquidInputs.GetValueOrDefault(MachineAccess.PurifierWaterSlot)?.childItem is { } vessel &&
            State.Machines.GetValueOrDefault(machine.GetUniqueID())?.Purifier is { } cycle && cycle.Feed.VesselId == vessel.GetUniqueID() &&
            NativeLiquidApi.Capture(vessel) is { } liquid && !SameObservedContents(cycle.ObservedContents, liquid))
        {
            cycle.ObservedContents = LiquidParts(liquid); Save();
        }
        EjectOnce(machine);
        NotifyMachinePresentation(machine);
    }

    internal void CancelNativeMachineBatch(GameItem machine, int generation)
    {
        if (generation == _loadGeneration) _consumption.Remove(machine.GetUniqueID());
    }

    private static List<RestockLiquidPart> LiquidParts(NativeMachineLiquidSnapshot liquid) => liquid.Contents
        .Where(part => part.Parts > 0).Select(part => new RestockLiquidPart
        { LiquidId = part.LiquidId, Parts = part.Parts, Value = part.Value, QualityBasis = part.QualityBasis }).ToList();

    private static bool SameObservedContents(IReadOnlyList<RestockLiquidPart> observed, NativeMachineLiquidSnapshot liquid) =>
        observed.Count == liquid.Contents.Count(part => part.Parts > 0) && observed.All(part => liquid.Contents.Any(actual =>
            part.LiquidId == actual.LiquidId && part.Parts == actual.Parts && part.Value == actual.Value && part.QualityBasis == actual.QualityBasis));

    private void PreparePurifierCycle(GameItem machine, NativeMachineInventory inventory)
    {
        int id = machine.GetUniqueID(); var settings = State.Machines.GetValueOrDefault(id);
        var previous = settings?.Purifier;
        var vessel = inventory.LiquidInputs.GetValueOrDefault(MachineAccess.PurifierWaterSlot)?.childItem;
        var liquid = vessel == null ? null : NativeLiquidApi.Capture(vessel);
        bool enabled = FillEnabled(machine) && NativeModuleApi.FindInstalled(machine, Content.Feeder).Count > 0;
        if (enabled && vessel != null && liquid != null && previous?.Feed.VesselId == vessel.GetUniqueID() &&
            SameObservedContents(previous.ObservedContents, liquid)) return;
        PurifierCycle? next = null;
        if (enabled && vessel != null && liquid is { TotalParts: > 0 } &&
            liquid.Contents.Any(part => part.LiquidId == "water" && part.Parts > 0))
        {
            next = new()
            {
                Feed = new() { SlotId = MachineAccess.PurifierWaterSlot, VesselId = vessel.GetUniqueID(),
                    WaterQuality = Math.Clamp(NativeLiquidApi.WaterQuality(vessel), 0, 3), TargetParts = liquid.TotalParts, Basis = LiquidParts(liquid) },
                ObservedContents = LiquidParts(liquid)
            };
        }
        if (previous == null && next == null) return;
        settings ??= new() { MachineId = id }; settings.Purifier = next; State.Machines[id] = settings; Save();
    }

    private PurifierCycle? PurifierEjection(GameItem machine, GameItem vessel, NativeMachineLiquidSnapshot liquid)
    {
        if (machine.identifier != "water_purifier" || !FillEnabled(machine) ||
            NativeModuleApi.FindInstalled(machine, Content.Feeder).Count == 0) return null;
        var cycle = State.Machines.GetValueOrDefault(machine.GetUniqueID())?.Purifier;
        return cycle?.Feed.VesselId == vessel.GetUniqueID() && SameObservedContents(cycle.ObservedContents, liquid) ? cycle : null;
    }

    private void RecordPurifierEjection(GameItem machine, GameItem vessel, NativeMachineLiquidSnapshot before,
        PurifierCycle? cycle, int moved)
    {
        if (cycle == null || moved <= 0 || !Ready || !IsCurrentRun() ||
            !ReferenceEquals(State.Machines.GetValueOrDefault(machine.GetUniqueID())?.Purifier, cycle)) return;
        var settings = State.Machines[machine.GetUniqueID()]; var feed = cycle.Feed;
        if (moved > before.TotalParts) throw new InvalidOperationException(PsModI18n.I18n.Get("purifier_water_ejection_exceeded_the_observed_batch"));
        // Native purification replaces contaminants with water at equal volume.
        // Only confirmed automatic withdrawal earns raw-feed repayment.
        int replacement = moved;
        var source = new ObservedLiquid(feed.SlotId, feed.VesselId, feed.WaterQuality,
            new(feed.TargetParts, feed.Basis.Select(part => new NativeMachineLiquidPart(part.LiquidId, part.Parts)
            { Value = part.Value, QualityBasis = part.QualityBasis }).ToArray()));
        var demand = settings.PendingLiquids.FirstOrDefault(item => item.SlotId == feed.SlotId && item.VesselId == feed.VesselId && SameConsumedLiquid(item, source));
        if (replacement > 0)
        {
            if (demand == null)
            {
                demand = new() { SlotId = feed.SlotId, VesselId = feed.VesselId, WaterQuality = feed.WaterQuality, Basis = feed.Basis.ToList() };
                settings.PendingLiquids.Insert(0, demand);
            }
            demand.TargetParts = Math.Max(demand.TargetParts, feed.TargetParts);
            demand.Remaining = (int)Math.Min(demand.TargetParts, (long)demand.Remaining + replacement);
            settings.HasConsumption = true;
        }
        cycle.ObservedContents = LiquidParts(NativeLiquidApi.Capture(vessel) ?? throw new InvalidOperationException(PsModI18n.I18n.Get("the_purifier_receiving_vessel_changed")));
        Save(); NotifyMachinePresentation(machine);
    }
}
