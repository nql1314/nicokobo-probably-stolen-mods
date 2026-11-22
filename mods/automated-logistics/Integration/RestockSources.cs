
namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private sealed record RestockSource(GameItem Item, int Tier, IntPtr Parent, bool Member);
    private readonly HashSet<IntPtr> _restockLiquidTransfers = new();

    private static bool RemovableSource(GameItem item, bool ownTransfer = false) => item != null && item.Handle() != IntPtr.Zero &&
        item.GetUniqueID() > 0 && item.unitCount > 0 && NativeInventoryApi.IsPlayerOwned(item) &&
        item.parentInventory is { } parent && !parent.IsRemoveLocked() &&
        (ownTransfer || item.MayRemove() && !NativeInventoryFreezeApi.IsFrozen(item));

    private IReadOnlyList<RestockSource> RestockSources(GameItem machine, bool liquid, GameItem? transferring = null)
    {
        if (NativeInventoryApi.IsItemDragActive) return Array.Empty<RestockSource>();
        var result = new List<RestockSource>();
        var instances = new HashSet<int>();
        var pointers = new HashSet<IntPtr>();
        // Reuse only within this enumeration. Transfer admission below still
        // captures fresh endpoints after any native callback or inventory move.
        var endpoints = ResourceEndpoints(false);
        void Add(GameItem item, int tier)
        {
            if (!RemovableSource(item, item.Handle() == transferring?.Handle()) || HasResourceContainerCycle(item) ||
                item.Handle() == machine.Handle() || (liquid ? !NativeLiquidApi.IsContainer(item) : NeedsManualRestock(item))) return;
            if (!instances.Add(item.GetUniqueID()) || !pointers.Add(item.Handle())) return;
            result.Add(new(item, tier, item.parentInventory!.Handle(), ResourceSourceCurrent(item, liquid, false, endpoints)));
        }
        if (ResourceNetworkOnline)
            foreach (var view in ResourceContents(endpoints))
                if ((liquid ? NativeLiquidApi.IsContainer(view.Item) : !view.LiquidEndpoint) && ResourceSourceCurrent(view.Item, liquid, true, endpoints)) Add(view.Item, 0);
        foreach (var container in Containers(machine, transferring).OrderBy(item => item.GetUniqueID()))
        {
            if (liquid && NativeLiquidApi.IsContainer(container)) Add(container, 1);
            foreach (var grid in Grids(container).DistinctBy(grid => grid.Handle()))
            {
                if (grid.IsRemoveLocked()) continue;
                foreach (var item in grid.childItems)
                    if (item != null && item.parentInventory?.Handle() == grid.Handle()) Add(item, 1);
            }
        }
        if (Warehouse() is { } warehouse && !warehouse.IsRemoveLocked())
            foreach (var item in warehouse.childItems)
                if (item != null && item.parentInventory?.Handle() == warehouse.Handle()) Add(item, 2);
        return result;
    }

    private bool SourceCurrent(GameItem machine, RestockSource source, bool liquid) =>
        source.Item.parentInventory?.Handle() == source.Parent &&
        RemovableSource(source.Item, liquid && _restockLiquidTransfers.Contains(source.Item.Handle())) &&
        (!source.Member || ResourceMemberSourceCurrent(source.Item, liquid)) &&
        (source.Tier == 0 ? ResourceSourceCurrent(source.Item, liquid) :
            RestockSources(machine, liquid, liquid && _restockLiquidTransfers.Contains(source.Item.Handle()) ? source.Item : null).Any(current => current.Tier == source.Tier &&
                current.Item.Handle() == source.Item.Handle() && current.Parent == source.Parent));

}
