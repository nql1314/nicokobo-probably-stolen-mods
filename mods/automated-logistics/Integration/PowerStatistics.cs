
namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private bool _powerStatsDirty;
    private readonly HashSet<IntPtr> _chargeObservations = new();
    internal sealed record ChargeObservation(GameItem Item, int Before, int Generation, string? Run, int Slot);
    private void RecordProductionPower(int cost, int gridDebit)
    {
        State.Power.Consumed = checked(State.Power.Consumed + cost);
        State.Power.Transferred = checked(State.Power.Transferred + gridDebit);
        _powerStatsDirty = true; NetworkPresentationChanged?.Invoke();
    }
    internal ChargeObservation? BeforeNativeCharge(GameItem item)
    {
        if (!Ready || !IsCurrentRun() || item == null || !PowerBatteries().Any(battery => battery.Handle() == item.Handle()) ||
            !_chargeObservations.Add(item.Handle())) return null;
        return new(item, NativePowerApi.GetEnergy(item), _loadGeneration, _run, _slot);
    }
    internal void AfterNativeCharge(ChargeObservation? observation)
    {
        if (observation == null) return;
        _chargeObservations.Remove(observation.Item.Handle());
        if (!Ready || !IsCurrentRun() || observation.Generation != _loadGeneration || observation.Run != _run || observation.Slot != _slot) return;
        // Leave the charge in the battery until the native callback and its Mod patches have returned.
        QueueGridEnergy(observation.Item);
        long added = Math.Max(0, NativePowerApi.GetEnergy(observation.Item) - (long)observation.Before);
        if (added == 0) return;
        State.Power.Generated = checked(State.Power.Generated + added);
        _powerStatsDirty = true; NetworkPresentationChanged?.Invoke();
    }
    private void FlushPowerStatistics()
    {
        if (!_powerStatsDirty || !Ready || !IsCurrentRun()) return;
        Save(); _powerStatsDirty = false;
    }
    internal ISet<IntPtr> GridMembers() => !Ready || !IsCurrentRun()
        ? new HashSet<IntPtr>() : NetworkDevices(Content.PowerDevice).SelectMany(NativeSaveGraphApi.Items).Select(item => item.Handle()).ToHashSet();
    internal long[] WaterTotals()
    {
        var totals = new long[4];
        foreach (var tank in NetworkDevices(Content.WaterDevice))
        {
            var snapshot = NativeLiquidApi.Capture(tank);
            if (snapshot == null || !snapshot.Contents.Any(part => part.LiquidId == "water" && part.Parts > 0)) continue;
            int quality = Math.Clamp(NativeLiquidApi.WaterQuality(tank), 0, 3);
            totals[quality] = checked(totals[quality] + snapshot.TotalParts);
        }
        return totals;
    }
    internal void RefreshDeviceWater(GameItem item)
    {
        if (item == null || item.identifier != Content.WaterDevice) return;
        item.SyncModifiedState(); ItemArtwork.Apply(item);
        NetworkPresentationChanged?.Invoke();
    }
    private void BeginTotalStatistics()
    {
        if (!Ready || !IsCurrentRun()) return;
        if (State.Power.Unlocked) State.Power.Totals.Baseline ??= PowerTotal();
        if (State.WaterUnlocked)
        {
            var totals = WaterTotals();
            for (int quality = 1; quality <= 3; quality++) State.WaterHistory[quality - 1].Baseline ??= totals[quality];
        }
    }
    internal void ObserveMorning(int day)
    {
        EnsureLoaded();
        if (!Ready || !IsCurrentRun() || day <= 0) return;
        // BeginDay has advanced the day and completed OnDayStart supplies.
        // Compare with the preceding morning and attribute the delta to yesterday.
        CollectGridEnergy();
        SettleTotalStatistics(day - 1);
        FlushPowerStatistics();
        NetworkPresentationChanged?.Invoke();
    }
    private void SettleTotalStatistics(int day)
    {
        if (PowerInventory() != null) _powerStatsDirty |= State.Power.Totals.Settle(day, PowerTotal());
        if (WaterStorageReady)
        {
            var totals = WaterTotals();
            for (int quality = 1; quality <= 3; quality++) _powerStatsDirty |= State.WaterHistory[quality - 1].Settle(day, totals[quality]);
        }
    }
}
