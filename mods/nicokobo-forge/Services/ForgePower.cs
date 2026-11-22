using HarmonyLib;
using Nicokobo.ContentInterop;


namespace Nicokobo.ContentInterop;

internal static class ForgePower
{
    private static Func<GameItem, int>? _energy;
    private static Func<GameItem, int, object[]?>? _plan;
    [ThreadStatic] internal static int RawReads;
    [ThreadStatic] internal static int Executing;
    [ThreadStatic] private static Operation? _operation;
    internal static bool IsAvailable => true;
    internal static IDisposable RegisterMachine(string owner, string key, Func<GameItem, int> energy, Func<GameItem, int, object[]?> plan)
    {
        if (_energy != null) throw new InvalidOperationException("Machine power provider already registered");
        _energy = energy; _plan = plan; DevicePower.Register(owner, Query, Debit);
        return new CallbackLease(() => { _energy = null; _plan = null; });
    }
    internal static GameItem? GetDevicePowerSource(GameItem machine) =>
        PowerConsumers.Source(machine) ?? MachineCatalog.Ports(machine)?.Battery?.childItem ?? (MachineHelper.GetBodyStorage(machine, 0) is GameSlotInventory slot ? slot.childItem : null);
    internal static bool CanSupplyDevice(GameItem device) => MachineCatalog.Contains(device.identifier) || PowerConsumers.Contains(device.identifier) || device.identifier is
        "furnace" or "water_purifier" or "moisture_farm" or "desequencer" or "printer" or "evaporator" or "plant_dna_manipulator" or
        "ras" or "hydroponic" or "powerblock" or "recharger" or "recharger_base" or "wine_rack" or "mirage_projector" or
        "security_alarm" or "bottle_printer" or "box_dispenser" or "fridge" or "vacuum_robot" or "aquascan" or "aug_scanner" or "turbo_booster" or "turbo_booster_adv";
    internal static int RawEnergy(GameItem item)
    { RawReads++; try { return PowerHelper.GetAvailableEnergyFromItem(item, false); } finally { RawReads--; } }
    internal sealed class PowerReceipt(object[] callbacks) : IDisposable
    {
        internal bool Validate() => ((Func<bool>)callbacks[0])();
        internal void Debit() => ((Action)callbacks[1])();
        internal bool Verify() => ((Func<bool>)callbacks[2])();
        internal bool Restore() => ((Func<bool>)callbacks[3])();
        internal void Fault(string reason) => ((Action<string>)callbacks[4])(reason);
        public void Dispose() => ((Action)callbacks[5])();
    }
    private static GameItem? Device(GameItem source)
    {
        var device = source.parentInventory?.GetParentItem();
        if (device == null || !CanSupplyDevice(device) || !GeneralHelper.IsItemOwned(device) ||
            !ReferenceEquals(GetDevicePowerSource(device), source) || !source.IsTag("power_source_item") ||
            source.GetTagReadonly("power_source_item_max_energy")?.valueInt <= 0) return null;
        return device;
    }
    private static int? Query(GameItem source)
    {
        if (Executing != 0 || RawReads != 0 || _energy == null || Device(source) is not { } device) return null;
        RawReads++;
        try { int value = _energy(device); return value < 0 ? null : value; }
        finally { RawReads--; }
    }
    private static bool? Debit(GameItem source, int cost)
    {
        if (Executing != 0 || _plan == null || Device(source) is not { } device) return null;
        if (cost == 0) return true;
        PowerReceipt? receipt = null;
        Executing++;
        try
        {
            var handle = _plan(device, cost);
            receipt = handle == null ? null : new PowerReceipt(handle);
            if (receipt == null) return false;
            if (!receipt.Validate()) return false;
            receipt.Debit();
            if (!receipt.Verify()) throw new InvalidOperationException("Grid power receipt failed verification");
            if (_operation != null) { _operation.Receipts.Add(receipt); receipt = null; }
            return true;
        }
        catch (Exception error)
        {
            if (receipt != null && !receipt.Restore()) receipt.Fault(error.Message);
            UnityEngine.Debug.LogWarning("[Forge] Power debit stopped: " + error.Message);
            return false;
        }
        finally { receipt?.Dispose(); Executing--; }
    }
    internal static Operation BeginOperation() => new();
    internal sealed class Operation : IDisposable
    {
        private readonly Operation? _previous = _operation;
        internal readonly List<PowerReceipt> Receipts = new();
        private bool _complete;
        internal Operation() => _operation = this;
        internal void Complete() => _complete = true;
        public void Dispose()
        {
            _operation = _previous; Executing++;
            try
            {
                foreach (var receipt in Receipts.AsEnumerable().Reverse())
                {
                    try { if (!_complete && !receipt.Restore()) receipt.Fault("Native operation failed after drawing energy"); }
                    finally { receipt.Dispose(); }
                }
            }
            finally { Executing--; }
        }
    }
}

[HarmonyPatch(typeof(PowerHelper), nameof(PowerHelper.CanDrawPowerSource), new[] { typeof(GameItem), typeof(int) })]
internal static class ExternalPowerCanDraw
{
    private static bool Prefix(GameItem __0, int __1, ref bool __result)
    {
        if (__0 == null || ForgePower.Executing != 0 || !DevicePower.TryEnergy(__0, out int energy)) return true;
        __result = __1 >= 0 && energy >= __1; return false;
    }
}
[HarmonyPatch(typeof(PowerHelper), nameof(PowerHelper.DrawPowerSource), new[] { typeof(GameItem), typeof(int) })]
internal static class ExternalPowerDraw
{
    private static bool Prefix(GameItem __0, int __1, ref bool __result)
    {
        if (__0 == null || ForgePower.Executing != 0 || !DevicePower.TryDebit(__0, __1, out bool result)) return true;
        __result = result; return false;
    }
}
[HarmonyPatch(typeof(PowerHelper), nameof(PowerHelper.GetAvailableEnergyFromItem), new[] { typeof(GameItem), typeof(bool) })]
internal static class ExternalPowerRead
{
    private static bool Prefix(GameItem __0, ref int __result)
    {
        if (__0 == null || ForgePower.Executing != 0 || ForgePower.RawReads != 0 || !DevicePower.TryEnergy(__0, out int energy)) return true;
        __result = energy; return false;
    }
}
