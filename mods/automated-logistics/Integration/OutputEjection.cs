
namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private IEnumerable<GameGridInventory> LocalOutputGrids(GameItem machine)
    {
        var seen = new HashSet<IntPtr>();
        foreach (var container in Containers(machine).OrderBy(item => item.GetUniqueID()))
        {
            if (HasResourceContainerCycle(container) || ResourceAccessReason(container).Length != 0) continue;
            foreach (var grid in Grids(container))
                if (!grid.IsInsertLocked() && seen.Add(grid.Handle())) yield return grid;
        }
        if (Warehouse() is { } warehouse && !warehouse.IsInsertLocked() && seen.Add(warehouse.Handle())) yield return warehouse;
    }

    private IReadOnlyList<(GameItem Item, int Tier)> LocalOutputLiquids(GameItem machine, GameItem? ownTransfer = null)
    {
        var targets = new List<(GameItem Item, int Tier)>();
        var seen = new HashSet<IntPtr>();
        void Add(GameItem item, int tier)
        {
            if (!NativeLiquidApi.IsContainer(item) || !RemovableSource(item, item.Handle() == ownTransfer?.Handle()) ||
                HasResourceContainerCycle(item) || ResourceNetworkOnline && ResourceMemberSourceCurrent(item, true) || !seen.Add(item.Handle())) return;
            targets.Add((item, tier));
        }
        foreach (var container in Containers(machine, ownTransfer).OrderBy(item => item.GetUniqueID()))
        {
            if (HasResourceContainerCycle(container) || ResourceAccessReason(container).Length != 0) continue;
            Add(container, 1);
            foreach (var grid in Grids(container).Where(grid => !grid.IsInsertLocked()))
                foreach (var item in grid.childItems)
                    if (item != null && item.parentInventory?.Handle() == grid.Handle()) Add(item, 1);
        }
        if (Warehouse() is { } warehouse && !warehouse.IsInsertLocked())
            foreach (var item in warehouse.childItems)
                if (item != null && item.parentInventory?.Handle() == warehouse.Handle()) Add(item, 2);
        return targets.OrderBy(target => target.Tier)
            .ThenBy(target => NativeLiquidApi.Capture(target.Item)?.TotalParts > 0 ? 0 : 1)
            .ThenBy(target => target.Item.GetUniqueID()).ToArray();
    }

    private IDisposable? BeginLocalOutputTransfer(GameItem source, GameInventory target, GameItem? vessel = null)
    {
        if (_resourceTransfers != 0 || NativeInventoryApi.IsItemDragActive) return null;
        int generation = _resourceTransferGeneration;
        _resourceTransfers++;
        try
        {
            _resourceTransferItems.Add(source.GetUniqueID());
            if (vessel != null) _resourceTransferItems.Add(vessel.GetUniqueID());
            foreach (var endpoint in ResourceEndpoints(false).Where(endpoint => vessel != null && endpoint.Item?.Handle() == vessel.Handle() ||
                endpoint.Inventories.Any(grid => grid.Handle() == target.Handle()))) _resourceTransferContainers.Add(endpoint.InstanceId);
            return new ResourceTransferLease(this, generation);
        }
        catch { EndResourceTransfer(generation); throw; }
    }

    private NativeItemTransferResult EjectItem(GameItem machine, GameItem item, Func<bool> current)
    {
        var parent = item.parentInventory;
        int count = item.unitCount, moved = 0;
        if (ResourceNetworkOnline)
        {
            var result = DepositResource(item, count, current);
            moved = result.Moved;
            if (result.Indeterminate || moved >= count) return result;
        }
        foreach (var grid in LocalOutputGrids(machine).ToArray())
        {
            if (!current() || moved >= count || item.parentInventory?.Handle() != parent?.Handle() || !RemovableSource(item)) break;
            if (grid.Handle() == parent?.Handle() || !LocalOutputGrids(machine).Any(target => target.Handle() == grid.Handle())) continue;
            using var transfer = BeginLocalOutputTransfer(item, grid);
            if (transfer == null) break;
            var result = NativeItemTransferApi.Move(item, grid, count - moved);
            moved = checked(moved + result.Moved);
            if (result.Indeterminate)
            {
                if (moved > 0) NetworkPresentationChanged?.Invoke();
                return new(moved, true, result.Status);
            }
        }
        if (moved > 0) NetworkPresentationChanged?.Invoke();
        return new(moved, false, moved > 0 ? "ejected" : "destinations-full-or-rejected");
    }

    private NativeLiquidTransferResult EjectLiquid(GameItem machine, GameItem source, int maximum, Func<bool> current)
    {
        int moved = 0;
        if (ResourceNetworkOnline)
        {
            var result = PourResource(source, maximum, current, exactComposition: machine.identifier == "evaporator");
            moved = result.PartsMoved;
            if (result.Indeterminate || moved >= maximum) return result;
        }
        foreach (var target in LocalOutputLiquids(machine))
        {
            if (!current() || moved >= maximum || NativeLiquidApi.Capture(source)?.TotalParts == 0) break;
            var parent = target.Item.parentInventory;
            bool TargetCurrent() => current() && target.Item.parentInventory?.Handle() == parent?.Handle() &&
                LocalOutputLiquids(machine, target.Item).Any(live => live.Item.Handle() == target.Item.Handle() && live.Tier == target.Tier) &&
                LiquidFeeding.SameOutputQuality(source, target.Item, exactComposition: machine.identifier == "evaporator");
            if (parent == null || !TargetCurrent()) continue;
            using var transfer = BeginLocalOutputTransfer(source, parent, target.Item);
            if (transfer == null) break;
            var result = NativeLiquidTransferApi.Pour(source, target.Item, maximum - moved, TargetCurrent);
            moved = checked(moved + result.PartsMoved);
            if (result.Indeterminate)
            {
                return new(moved, true, result.Status);
            }
        }
        return new(moved, false, moved > 0 ? "ejected-liquid" : "vessels-full-or-incompatible");
    }
}
