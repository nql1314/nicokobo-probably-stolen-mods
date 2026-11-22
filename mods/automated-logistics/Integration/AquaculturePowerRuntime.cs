using static PsModI18n.I18n;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    internal sealed record AquaculturePowerObservation(GameItem Machine, GameInventory Tank, IntPtr Parent,
        int Day, int Generation, string Run, int Slot, IntPtr Store, int Cells, int Output);

    // Modified shapes already include placement and rotation. Count each
    // cardinal outline cell once, restricted to usable cells in this inventory.
    internal static int AquacultureGridCells(GameItem machine)
    {
        var grid = machine.parentInventory?.TryCast<GameGridInventory>();
        var own = machine.modifiedShape;
        var area = grid?.inventoryShape;
        if (grid == null || own == null || area == null) return 0;
        var cells = new HashSet<(int X, int Y)>();
        void Add(int x, int y)
        {
            if (x >= area.minX && x <= area.maxX && y >= area.minY && y <= area.maxY &&
                area.Get(x, y) == 0 && own.Get(x, y) != 1) cells.Add((x, y));
        }
        for (int y = own.minY; y <= own.maxY; y++)
        for (int x = own.minX; x <= own.maxX; x++)
            if (own.Get(x, y) == 1)
            { Add(x - 1, y); Add(x + 1, y); Add(x, y - 1); Add(x, y + 1); }
        foreach (var item in grid.childItems)
        {
            if (item == null || item.Handle() == machine.Handle() || item.parentInventory?.Handle() != grid.Handle() ||
                !(item.IsTag("power_source_item") || NetworkStorage.ChargingEquipmentIds.Contains(item.identifier)) ||
                item.modifiedShape is not { } shape) continue;
            cells.RemoveWhere(cell => shape.Get(cell.X, cell.Y) == 1);
        }
        return cells.Count;
    }

    private bool AquacultureGeneratorAvailable(GameItem machine, GameInventory tank) =>
        Ready && IsCurrentRun() && !_powerLedgerBusy && PowerInventory() != null &&
        machine != null && machine.Handle() != IntPtr.Zero && machine.identifier == "ras" && machine.GetUniqueID() > 0 &&
        machine.parentInventory is { } parent && !parent.IsRemoveLocked() && NativeInventoryApi.IsPlayerOwned(machine) &&
        !NativeInventoryFreezeApi.IsFrozen(machine) && tank != null && tank.Handle() != IntPtr.Zero &&
        tank.GetParentItem()?.Handle() == machine.Handle();

    internal AquaculturePowerObservation? BeforeAquaculturePower(GameItem machine, GameInventory tank)
    {
        if (!AquacultureGeneratorAvailable(machine, tank)) return null;
        int day = StoreStation.GetDayCounter(), id = machine.GetUniqueID();
        if (day < 0 || State.Power.AquacultureDay > day ||
            State.Power.AquacultureDay == day && State.Power.AquacultureGenerators.Contains(id)) return null;
        int output = 0;
        foreach (var stock in tank.childItems)
            output = checked(output + AquacultureHelper.GetPowerOutput(stock));
        output = Math.Max(0, output);
        return new(machine, tank, machine.parentInventory!.Handle(), day, _loadGeneration, _run!, _slot,
            PlayerStore.Instance!.Handle(), AquacultureGridCells(machine), output);
    }

    internal void AfterAquaculturePower(AquaculturePowerObservation? observation)
    {
        if (observation is not { } value || !AquacultureGeneratorAvailable(value.Machine, value.Tank) ||
            value.Generation != _loadGeneration || value.Run != _run || value.Slot != _slot ||
            value.Store != PlayerStore.Instance?.Handle() || value.Parent != value.Machine.parentInventory?.Handle() ||
            value.Day != StoreStation.GetDayCounter() || State.Power.AquacultureDay > value.Day ||
            State.Power.AquacultureDay == value.Day && State.Power.AquacultureGenerators.Contains(value.Machine.GetUniqueID())) return;
        // Native generation has already charged its real recipients. Snapshot
        // only now, so compensation cannot undo those completed native charges.
        CollectGridEnergy();
        long added = Math.Min(Math.Max(0, PowerCapacity() - PowerTotal()), checked((long)value.Cells * value.Output));
        var change = new StateChange(this);
        _powerLedgerBusy = true;
        try
        {
            var result = NativeTransactionApi.Run([new("aquaculture-grid-generation", () =>
            {
                change.Commit(() =>
                {
                    if (State.Power.AquacultureDay != value.Day)
                    { State.Power.AquacultureDay = value.Day; State.Power.AquacultureGenerators.Clear(); }
                    State.Power.AquacultureGenerators.Add(value.Machine.GetUniqueID());
                    State.Power.StoredEnergy = checked(State.Power.StoredEnergy + added);
                    State.Power.Generated = checked(State.Power.Generated + added);
                });
                if (!change.Verify()) throw new InvalidOperationException(PsModI18n.I18n.Get("aquaculture_generation_could_not_be_saved"));
            }, change.Restore)]);
            if (result.Committed) { _powerStatsDirty = true; NetworkPresentationChanged?.Invoke(); }
            else if (!result.Restored && change.Current())
            { NetworkError = PsModI18n.I18n.Get("aquaculture_generation_rollback_failed") + result.Status; }
        }
        finally { _powerLedgerBusy = false; }
    }
}
