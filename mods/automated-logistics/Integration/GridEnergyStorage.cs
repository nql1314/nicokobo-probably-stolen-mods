using static PsModI18n.I18n;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private bool _powerLedgerBusy;
    private bool _powerCollectionPending = true;
    private string? _powerTransferFailure;
    internal void QueueGridEnergy(GameItem item)
    {
        var seen = new HashSet<IntPtr>();
        for (GameItem? current = item; current != null && seen.Add(current.Handle()); current = current.parentInventory?.GetParentItem())
            if (current.identifier == Content.PowerDevice) { _powerCollectionPending = true; return; }
    }
    private void CollectPendingGridEnergy()
    {
        if (!_powerCollectionPending || _powerLedgerBusy) return;
        _powerCollectionPending = false;
        CollectGridEnergy();
    }
    internal void CollectGridEnergy()
    {
        if (!Ready || !IsCurrentRun() || _powerLedgerBusy) return;
        long before = State.Power.StoredEnergy, capacity = PowerCapacity();
        if (before >= capacity) return;
        var batteries = PowerBatteries().Where(item => NativePowerApi.GetEnergy(item) > 0 &&
            !NativeInventoryFreezeApi.IsFrozen(item) && !item.parentInventory!.IsRemoveLocked()).ToArray();
        if (batteries.Length == 0) return;
        var transfers = new List<(EnergySnapshot Before, int Take)>();
        long added = 0;
        foreach (var battery in batteries)
        {
            var snapshot = new EnergySnapshot(battery);
            int take = (int)Math.Min(snapshot.Energy, capacity - before - added);
            transfers.Add((snapshot, take)); added += take;
            if (before + added == capacity) break;
        }
        // Keep slot positions only for this transfer. Native callbacks may
        // reorder a list, so a changed position falls back to live membership.
        var positions = new Dictionary<IntPtr, int>();
        foreach (var parent in transfers.Select(entry => entry.Before.Item.parentInventory!).DistinctBy(parent => parent.Handle()))
            for (int index = 0; index < parent.childItems.Count; index++)
                if (parent.childItems[index] is { } child) positions.TryAdd(child.Handle(), index);
        bool Linked(GameItem item)
        {
            var children = item.parentInventory!.childItems;
            if (positions.TryGetValue(item.Handle(), out int index) && index < children.Count && children[index]?.Handle() == item.Handle()) return true;
            for (index = 0; index < children.Count; index++)
                if (children[index]?.Handle() == item.Handle()) return true;
            return false;
        }
        using var lease = NativeInventoryFreezeApi.TryAcquire(transfers.Select(entry => entry.Before.Item));
        if (lease == null) return;
        var change = new StateChange(this);
        _powerLedgerBusy = true;
        try
        {
            var result = NativeTransactionApi.Run([new("store-grid-energy", () =>
            {
                var members = PowerBatteries().Select(item => item.Handle()).ToHashSet();
                var snapshots = transfers.Select(entry => entry.Before).ToArray();
                if (!change.Current() || State.Power.StoredEnergy != before || PowerCapacity() != capacity || snapshots.Any(entry => !entry.SameItem() ||
                    !members.Contains(entry.Item.Handle()) || NativePowerApi.GetEnergy(entry.Item) != entry.Energy))
                    throw new InvalidOperationException(PsModI18n.I18n.Get("the_grid_battery_changed"));
                foreach (var transfer in transfers)
                {
                    var entry = transfer.Before;
                    if (!change.Current() || !entry.SameItem() || !Linked(entry.Item))
                        throw new InvalidOperationException(PsModI18n.I18n.Get("the_battery_moved_during_energy_transfer"));
                    if (PowerHelper.TryRemoveEnergy(entry.Item, transfer.Take, false) != transfer.Take)
                        throw new InvalidOperationException(PsModI18n.I18n.Get("battery_energy_debit_was_incomplete"));
                    PowerHelper.UpdateEnergyCreditSprite(entry.Item);
                }
                members = PowerBatteries().Select(item => item.Handle()).ToHashSet();
                if (!change.Current() || PowerCapacity() != capacity || transfers.Any(transfer => !transfer.Before.SameItem() ||
                    !members.Contains(transfer.Before.Item.Handle()) || NativePowerApi.GetEnergy(transfer.Before.Item) != transfer.Before.Energy - transfer.Take))
                    throw new InvalidOperationException(PsModI18n.I18n.Get("remaining_battery_energy_could_not_be_verified"));
                change.Commit(() => State.Power.StoredEnergy = checked(before + added));
                if (!change.Verify()) throw new InvalidOperationException(PsModI18n.I18n.Get("grid_energy_balance_could_not_be_saved"));
            }, () =>
            {
                if (!change.Current()) return false;
                bool restored = change.Restore();
                foreach (var entry in transfers)
                    try { restored = entry.Before.Restore() && restored; } catch { restored = false; }
                return restored;
            })]);
            if (result.Committed)
            {
                if (_powerTransferFailure != null && NetworkError == _powerTransferFailure) NetworkError = "";
                _powerTransferFailure = null;
                NetworkPresentationChanged?.Invoke();
            }
            else if (change.Current())
            {
                string failure = (result.Restored ? PsModI18n.I18n.Get("energy_transfer_failed") :
                    PsModI18n.I18n.Get("energy_transfer_rollback_failed")) + result.Status;
                if (_powerTransferFailure != failure) log(failure);
                _powerTransferFailure = NetworkError = failure;
            }
        }
        finally { _powerLedgerBusy = false; }
    }
}
