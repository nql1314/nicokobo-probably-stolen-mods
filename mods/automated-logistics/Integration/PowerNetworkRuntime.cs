using static PsModI18n.I18n;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private int? _powerNightDay;
    private void ObservePowerBeforeNight(int day)
    {
        EnsureLoaded(); BeginTotalStatistics(); _powerNightDay = Ready && IsCurrentRun() ? day : null;
    }
    private void ObservePowerAfterNight()
    {
        CollectGridEnergy();
        if (_powerNightDay is int generationDay) GenerateHypertechPower(generationDay);
        if (Ready && IsCurrentRun() && _powerNightDay is int day && State.Power.SettleDay(day))
        { _powerStatsDirty = true; NetworkPresentationChanged?.Invoke(); }
        _powerNightDay = null;
        FlushPowerStatistics();
    }
    internal IReadOnlyList<GameItem> PowerBatteries()
    {
        if (!Ready || !IsCurrentRun()) return [];
        var items = NetworkDevices(Content.PowerDevice).SelectMany(NativeSaveGraphApi.Items).DistinctBy(item => item.Handle()).Where(NetworkStorage.IsBattery).Where(NativeInventoryApi.IsPlayerOwned).ToArray();
        if (items.Select(item => item.GetUniqueID()).Distinct().Count() != items.Length)
            throw new InvalidDataException(PsModI18n.I18n.Get("duplicate_grid_battery_instance_id"));
        return items.OrderByDescending(NativePowerApi.GetEnergy).ThenBy(item => item.GetUniqueID()).ToArray();
    }
    internal long PowerTotal() => State.Power.StoredEnergy;
    internal long PowerCapacity() => (long)Math.Max(State.Power.Height, AutomatedLogisticsNumbers.Parameters.PowerHeight) * AutomatedLogisticsNumbers.PowerEnergyPerRow;
    private bool ClaimsGrid(GameItem machine) => IsCurrentRun() && State.Power.Unlocked &&
        machine != null && machine.Handle() != IntPtr.Zero && NativeExternalPowerApi.CanSupplyDevice(machine) &&
        machine.parentInventory != null && NativeInventoryApi.IsPlayerOwned(machine);
    private bool GridAvailable(GameItem machine) => Ready && ClaimsGrid(machine) &&
        !machine.parentInventory!.IsRemoveLocked() && PowerInventory() != null;
    private static GameItem? LocalBattery(GameItem machine) => NativeExternalPowerApi.GetDevicePowerSource(machine);
    private static bool ValidMachinePowerSource(GameItem source) => source.Handle() != IntPtr.Zero && source.unitCount > 0 &&
        source.IsTag("power_source_item") && source.GetTagReadonly("power_source_item_energy")?.valueInt >= 0 &&
        source.GetTagReadonly("power_source_item_max_energy")?.valueInt > 0;
    private static bool AboveBatteryReserve(int energy, int capacity) => (long)energy * 4 > capacity;
    private int MachineGridEnergy(GameItem machine)
    {
        if (!ClaimsGrid(machine) || !GridAvailable(machine)) return -1;
        var battery = LocalBattery(machine);
        if (battery == null || !ValidMachinePowerSource(battery)) return 0;
        int local = NativePowerApi.GetEnergy(battery);
        int capacity = battery.GetTagReadonly("power_source_item_max_energy")?.valueInt ?? 0;
        return (int)Math.Min(capacity, Math.Max(local, PowerTotal()));
    }
    private INativePowerTransaction? PlanPower(GameItem machine, int cost)
    {
        if (cost <= 0 || !GridAvailable(machine) || LocalBattery(machine) is not { } local || !ValidMachinePowerSource(local)) return null;
        int before = NativePowerApi.GetEnergy(local);
        int capacity = local.GetTagReadonly("power_source_item_max_energy")!.valueInt;
        if (capacity < cost) return null;
        CollectGridEnergy();
        var payment = PowerPaymentPlan.Select(before, capacity, PowerTotal(), cost);
        int gridDebit = payment == PowerPaymentSource.Grid ? cost : 0;
        if (_powerLedgerBusy || payment == PowerPaymentSource.None) return null;
        var lease = NativeInventoryFreezeApi.TryAcquire(new[] { local, machine });
        if (lease == null) return null;
        _powerLedgerBusy = true;
        return new PowerDebit(this, machine, cost, local, before, gridDebit, lease);
    }
    private sealed class PowerDebit(AutomatedLogisticsRuntime runtime, GameItem machine,
        int cost, GameItem local, int localBefore, int gridDebit, IDisposable lease) : INativePowerTransaction
    {
        private readonly StateChange _change = new(runtime);
        private readonly EnergySnapshot _localSnapshot = new(local);
        private readonly long _stored = runtime.State.Power.StoredEnergy;
        private readonly int _localDebit = cost - gridDebit;
        private bool _attempted, _debited, _verified, _disposed;
        private bool Current() => _change.Current() && runtime.GridAvailable(machine) &&
            LocalBattery(machine)?.Handle() == local.Handle() && _localSnapshot.SameItem();
        public bool Validate() => !_attempted && Current() && NativePowerApi.GetEnergy(local) == localBefore && runtime.State.Power.StoredEnergy == _stored;
        public void Debit()
        {
            if (!Validate()) throw new InvalidOperationException(PsModI18n.I18n.Get("the_grid_energy_debit_plan_changed"));
            _attempted = true;
            _change.Commit(() => runtime.State.Power.StoredEnergy = checked(_stored - gridDebit));
            if (!_change.Verify()) throw new InvalidOperationException(PsModI18n.I18n.Get("grid_energy_balance_could_not_be_saved"));
            if (!Current() || (_localDebit > 0 && !PowerHelper.DrawPowerSource(local, _localDebit)) || NativePowerApi.GetEnergy(local) != localBefore - _localDebit)
                throw new InvalidOperationException(PsModI18n.I18n.Get("machine_battery_debit_could_not_be_verified"));
            if (_localDebit > 0) PowerHelper.UpdateEnergyCreditSprite(local);
            _debited = true;
        }
        public bool Verify() => _verified = _debited && Current() && _change.Verify() &&
            runtime.State.Power.StoredEnergy == _stored - gridDebit && NativePowerApi.GetEnergy(local) == localBefore - _localDebit;
        public bool Restore()
        {
            _verified = false;
            if (!_change.Current()) return false;
            if (!_attempted) return NativePowerApi.GetEnergy(local) == localBefore && runtime.State.Power.StoredEnergy == _stored;
            bool restored = _change.Restore();
            try { restored = _localSnapshot.Restore() && restored; } catch { restored = false; }
            _debited = false; return restored;
        }
        public void Fault(string reason) { _verified = false; runtime.NetworkError = PsModI18n.I18n.Get("grid_energy_debit_failed") + reason; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (_debited && _verified && _change.Current()) runtime.RecordProductionPower(cost, gridDebit); }
            finally { runtime._powerLedgerBusy = false; lease.Dispose(); }
        }
    }
    internal string PowerStatus(GameItem machine)
    {
        if (MachineAccess.IsFeedDispenser(machine)) return PsModI18n.I18n.Get("the_feed_dispenser_needs_no_battery");
        var battery = LocalBattery(machine);
        if (battery == null) return PsModI18n.I18n.Get("no_battery_installed");
        int local = NativePowerApi.GetEnergy(battery), cost = NativePowerApi.GetMachineCost(machine);
        int available = MachineGridEnergy(machine);
        if (available < 0 && local >= cost)
            return PsModI18n.I18n.Get("local_battery_batch_cost", local, cost);
        if (available >= cost)
        {
            int capacity = battery.GetTagReadonly("power_source_item_max_energy")?.valueInt ?? 0;
            bool useLocal = AboveBatteryReserve(local, capacity) && local >= cost;
            int gridDebit = !useLocal && PowerTotal() >= cost ? cost : 0;
            return PsModI18n.I18n.Get("batch_cost_grid_local_battery", cost, gridDebit, cost - gridDebit);
        }
        return PsModI18n.I18n.Get("low_power_local_batch", local, cost);
    }
}
