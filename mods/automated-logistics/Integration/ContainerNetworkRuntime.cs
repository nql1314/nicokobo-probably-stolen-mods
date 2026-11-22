using static PsModI18n.I18n;

namespace Nicokobo.AutomatedLogistics.Integration;

public sealed record ResourceContainerView(GameItem? Item, int InstanceId, bool Member, bool Online,
    string Status, IReadOnlyList<GameGridInventory> Inventories, bool Liquid);
public sealed record ResourceItemView(GameItem Item, int ContainerId, GameGridInventory? Inventory, bool LiquidEndpoint);

internal sealed partial class AutomatedLogisticsRuntime
{
    private readonly HashSet<int> _resourceTransferItems = new();
    private readonly HashSet<int> _resourceTransferContainers = new();
    private bool _resourceMembersReady;
    private int _resourceTransfers, _resourceTransferGeneration;
    internal bool ResourceStorageReady => Ready && IsCurrentRun() && _resourceMembersReady &&
        State.ResourcesUnlocked && State.Resources != null && ResourceInventory() != null && _resourceRoot != null;
    internal bool WaterStorageReady => Ready && IsCurrentRun() && State.WaterUnlocked && State.Resources != null && ResourceWaterTank() != null;
    internal bool ResourceNetworkOnline => (State.ResourcesUnlocked ? ResourceStorageReady : WaterStorageReady) && State.ResourcesEnabled;
    private static bool HasResourceContainerCycle(GameItem item)
    {
        var seen = new HashSet<IntPtr>();
        for (GameItem? current = item; current != null; current = current.parentInventory?.GetParentItem())
            if (!seen.Add(current.Handle())) return true;
        return false;
    }

    internal string SetResourceNetworkEnabled(bool enabled)
    {
        if (!Ready || !IsCurrentRun() || !State.ResourcesUnlocked || !_resourceMembersReady || _resourceTransfers != 0)
            throw new InvalidOperationException(PsModI18n.I18n.Get("resource_network_is_unavailable_or_a_transfer"));
        var change = new StateChange(this); int generation = _resourceTransferGeneration; _resourceTransfers++;
        try
        {
            var result = NativeTransactionApi.Run([new("resource-network-switch", () =>
            {
                change.Commit(() => State.ResourcesEnabled = enabled);
                if (!change.Verify()) throw new InvalidOperationException(PsModI18n.I18n.Get("resource_network_toggle_could_not_be_verified"));
            }, change.Restore)]);
            if (!result.Committed && !result.Restored && generation == _resourceTransferGeneration) _resourceMembersReady = false;
            return result.Committed ? (enabled ? PsModI18n.I18n.Get("resource_network_enabled") : PsModI18n.I18n.Get("resource_network_paused")) : PsModI18n.I18n.Get("toggle_was_not_saved") + result.Status;
        }
        finally { EndResourceTransfer(generation); }
    }

    private string ResourceAccessReason(GameItem item)
    {
        var seen = new HashSet<IntPtr>();
        for (GameItem? current = item; current != null; current = current.parentInventory?.GetParentItem())
        {
            if (current.Handle() == IntPtr.Zero || !seen.Add(current.Handle())) return PsModI18n.I18n.Get("invalid_container_hierarchy");
            if (!NativeInventoryApi.IsPlayerOwned(current)) return PsModI18n.I18n.Get("the_container_is_no_longer_player_owned");
            if (current.parentInventory == null) return PsModI18n.I18n.Get("container_is_unavailable");
            bool ownTransfer = (_resourceTransferItems.Contains(current.GetUniqueID()) ||
                _resourceTransferContainers.Contains(current.GetUniqueID())) && NativeInventoryFreezeApi.IsFrozen(current);
            if (current.parentInventory.IsRemoveLocked() || !ownTransfer && (!current.MayRemove() || NativeInventoryFreezeApi.IsFrozen(current)))
                return PsModI18n.I18n.Get("container_is_locked_or_in_use");
            if (current.IsGameItemType("MACHINE") || current.identifier == Content.PowerDevice ||
                current.identifier.Contains("backpack", StringComparison.OrdinalIgnoreCase)) return PsModI18n.I18n.Get("endpoints_inside_machines_backpacks_or_special_inventories");
        }
        return NativeInventoryApi.IsItemDragActive ? PsModI18n.I18n.Get("access_deferred_while_an_item_is_being") : "";
    }

    internal IReadOnlyList<ResourceContainerView> ResourceContainers() => ResourceEndpoints(false);

    private IReadOnlyList<ResourceContainerView> ResourceEndpoints(bool requireOnline = true)
    {
        if (!Ready || !IsCurrentRun() || (!State.ResourcesUnlocked && !State.WaterUnlocked) || requireOnline && !State.ResourcesEnabled)
            return [];
        var result = new List<ResourceContainerView>();
        var devices = Items();
        foreach (var root in devices.Where(item => OwnedRoot(item, Content.ResourceDevice)).DistinctBy(item => item.GetUniqueID()))
            if (State.ResourcesUnlocked && Grids(root).ToArray() is { Length: > 0 } grids)
                result.Add(new(root, root.GetUniqueID(), true, true, PsModI18n.I18n.Get("void_container"), grids, false));
        foreach (var tank in devices.Where(item => OwnedRoot(item, Content.WaterDevice)).DistinctBy(item => item.GetUniqueID()))
            if (State.WaterUnlocked)
                result.Add(new(tank, tank.GetUniqueID(), true, true, PsModI18n.I18n.Get("void_tank"), [], true));
        return result;
    }

    internal IReadOnlyList<ResourceItemView> ResourceContents() => ResourceContents(ResourceEndpoints(false));
    private IReadOnlyList<ResourceItemView> ResourceContents(IReadOnlyList<ResourceContainerView> endpoints)
    {
        var result = new List<ResourceItemView>(); var seen = new HashSet<int>();
        foreach (var endpoint in endpoints)
        {
            if (endpoint.Liquid && endpoint.Item is { } vessel && seen.Add(vessel.GetUniqueID()))
                result.Add(new(vessel, endpoint.InstanceId, null, true));
            foreach (var grid in endpoint.Inventories)
                foreach (var item in grid.childItems.ToArray().Where(item => item != null).OrderBy(item => item.GetUniqueID()))
                    if (item.Handle() != IntPtr.Zero && item.unitCount > 0 && item.parentInventory?.Handle() == grid.Handle() &&
                        NativeInventoryApi.IsPlayerOwned(item) && seen.Add(item.GetUniqueID()))
                        result.Add(new(item, endpoint.InstanceId, grid, false));
        }
        return result;
    }

    private static bool ContainsResourceMember(GameItem item) => NativeSaveGraphApi.Items(item).Any(node =>
        Content.IsDevice(node.identifier));

    internal bool ResourceSourceCurrent(GameItem item, bool liquid = false) => ResourceSourceCurrent(item, liquid, true);
    internal bool ResourceMemberSourceCurrent(GameItem item, bool liquid = false) => ResourceSourceCurrent(item, liquid, false);

    private bool ResourceSourceCurrent(GameItem item, bool liquid, bool requireOnline, IReadOnlyList<ResourceContainerView>? endpoints = null)
    {
        if (requireOnline && !ResourceNetworkOnline || item == null || item.Handle() == IntPtr.Zero || item.unitCount <= 0 ||
            !NativeInventoryApi.IsPlayerOwned(item) || item.parentInventory == null || item.parentInventory.IsRemoveLocked() ||
            NativeInventoryApi.IsItemDragActive) return false;
        bool ownFreeze = _resourceTransferItems.Contains(item.GetUniqueID()) && NativeInventoryFreezeApi.IsFrozen(item);
        if (!ownFreeze && (!item.MayRemove() || NativeInventoryFreezeApi.IsFrozen(item))) return false;
        if (liquid && (!NativeLiquidApi.IsContainer(item) || WaterHelper.GetFilterItem(item) != null)) return false;
        foreach (var endpoint in endpoints ?? ResourceEndpoints(requireOnline))
        {
            if (endpoint.Item is { } root && ResourceAccessReason(root).Length != 0) continue;
            if (liquid && endpoint.Liquid && endpoint.Item?.Handle() == item.Handle()) return true;
            if (!ContainsResourceMember(item) && endpoint.Inventories.Any(grid => grid.Handle() == item.parentInventory.Handle() &&
                grid.childItems.ToArray().Count(child => child != null && child.GetUniqueID() == item.GetUniqueID() && child.Handle() == item.Handle()) == 1)) return true;
        }
        return false;
    }

    internal IDisposable? BeginResourceSourceTransfer(GameItem item, bool liquid = false, bool requireOnline = true)
    {
        if (_resourceTransfers != 0 || !ResourceSourceCurrent(item, liquid, requireOnline)) return null;
        foreach (var endpoint in ResourceEndpoints(requireOnline).Where(endpoint => endpoint.Item?.Handle() == item.Handle() ||
            endpoint.Inventories.Any(grid => grid.Handle() == item.parentInventory?.Handle()))) _resourceTransferContainers.Add(endpoint.InstanceId);
        _resourceTransferItems.Add(item.GetUniqueID());
        _resourceTransfers++;
        return new ResourceTransferLease(this, _resourceTransferGeneration);
    }

    private void EndResourceTransfer(int generation)
    {
        if (generation != _resourceTransferGeneration) return;
        _resourceTransfers--; _resourceTransferItems.Clear(); _resourceTransferContainers.Clear();
    }

    private sealed class ResourceTransferLease(AutomatedLogisticsRuntime runtime, int generation) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; runtime.EndResourceTransfer(generation);
        }
    }

    internal NativeItemTransferResult DepositResource(GameItem item, int maximum, Func<bool>? stillAllowed = null)
    {
        if (!ResourceNetworkOnline || _resourceTransfers != 0 || maximum <= 0 || item == null || item.parentInventory == null ||
            ContainsResourceMember(item) || !NetworkStorage.IsResourceItem(item) || stillAllowed?.Invoke() == false)
            return new(0, false, "resource-unavailable-or-member");
        int moved = 0; int sourceId = item.GetUniqueID(); var parent = item.parentInventory;
        int generation = _resourceTransferGeneration; _resourceTransfers++;
        _resourceTransferItems.Add(sourceId);
        foreach (var endpoint in ResourceEndpoints().Where(endpoint => endpoint.Inventories.Any(grid => grid.Handle() == parent.Handle())))
            _resourceTransferContainers.Add(endpoint.InstanceId);
        try
        {
            // Native slot preflight decides merge/shape/admission; neither cell
            // totals nor a shadow item-count capacity are used for storage.
            var rejected = new HashSet<IntPtr>();
            while (moved < maximum && item.Handle() != IntPtr.Zero && item.unitCount > 0 && item.parentInventory?.Handle() == parent.Handle())
            {
                if (generation != _resourceTransferGeneration || !ResourceNetworkOnline || stillAllowed?.Invoke() == false ||
                    item.GetUniqueID() != sourceId || ContainsResourceMember(item)) return new(moved, false, "resource-condition-changed");
                var targets = ResourceEndpoints().Where(endpoint => endpoint.Item is { } root && ResourceAccessReason(root).Length == 0)
                    .SelectMany(endpoint => endpoint.Inventories.Select((grid, index) =>
                    (endpoint.InstanceId, Grid: grid, Index: index, Slot: !rejected.Contains(grid.Handle()) && grid.Handle() != parent.Handle() && !grid.IsInsertLocked()
                        ? grid.TryFindOneValidInventorySlot(item, false) : null)))
                    .Where(target => target.Slot != null && target.Slot.IsValid() && target.Slot.item?.Handle() == item.Handle() &&
                        target.Slot.inventory?.Handle() == target.Grid.Handle() && target.Slot.numTransfer > 0)
                    .Select(target => (target.InstanceId, target.Grid, target.Index, Merge: target.Slot!.targetItem != null))
                    .OrderBy(target => target.Merge ? 0 : 1).ThenBy(target => target.InstanceId).ThenBy(target => target.Index).ToArray();
                if (targets.Length == 0) break;
                var target = targets[0];
                if (generation != _resourceTransferGeneration || !ResourceNetworkOnline || stillAllowed?.Invoke() == false ||
                    item.parentInventory?.Handle() != parent.Handle() || !ResourceEndpoints().Any(endpoint => endpoint.InstanceId == target.InstanceId &&
                        endpoint.Item is { } root && ResourceAccessReason(root).Length == 0 &&
                        endpoint.Inventories.Any(grid => grid.Handle() == target.Grid.Handle()))) return new(moved, false, "resource-condition-changed");
                var result = NativeItemTransferApi.Move(item, target.Grid, maximum - moved);
                moved = checked(moved + result.Moved);
                if (result.Indeterminate)
                {
                    return new(moved, true, result.Status);
                }
                if (result.Moved == 0) rejected.Add(target.Grid.Handle()); else rejected.Clear();
            }
            return new(moved, false, moved > 0 ? "stored-in-resource-region" : "resource-region-full-or-rejected");
        }
        finally
        {
            EndResourceTransfer(generation);
            if (moved > 0 && generation == _resourceTransferGeneration) NetworkPresentationChanged?.Invoke();
        }
    }

    internal NativeItemTransferResult WithdrawResource(int instanceId, int count, GameGridInventory? destination = null)
    {
        var view = ResourceContents().SingleOrDefault(view => !view.LiquidEndpoint && view.Item.GetUniqueID() == instanceId);
        destination ??= Warehouse();
        if (view == null || destination == null || count <= 0 || ContainsResourceMember(view.Item)) return new(0, false, "actual-instance-unavailable");
        using var transfer = BeginResourceSourceTransfer(view.Item);
        if (transfer == null) return new(0, false, "resource-source-unavailable");
        var result = NativeItemTransferApi.Move(view.Item, destination, count);
        if (result.Moved > 0) NetworkPresentationChanged?.Invoke();
        return result;
    }

    internal NativeLiquidTransferResult PourResource(GameItem source, int maximumParts, Func<bool>? stillAllowed = null, bool exactComposition = false)
    {
        if (!ResourceNetworkOnline || _resourceTransfers != 0 || maximumParts <= 0 || source == null ||
            source.parentInventory == null || source.parentInventory.IsRemoveLocked() || !source.MayRemove() ||
            !NativeInventoryApi.IsPlayerOwned(source) || NativeInventoryFreezeApi.IsFrozen(source) || NativeInventoryApi.IsItemDragActive ||
            stillAllowed?.Invoke() == false || NativeLiquidApi.Capture(source) is not { } water ||
            water.TotalParts <= 0 || !exactComposition && !water.Contents.Any(part => part.LiquidId == "water" && part.Parts > 0)) return new(0, false, "resource-water-unavailable");
        var original = source.parentInventory; int moved = 0; int generation = _resourceTransferGeneration; _resourceTransfers++;
        _resourceTransferItems.Add(source.GetUniqueID());
        foreach (var endpoint in ResourceEndpoints().Where(endpoint => endpoint.Item?.Handle() == source.Handle() ||
            endpoint.Inventories.Any(grid => grid.Handle() == original.Handle()))) _resourceTransferContainers.Add(endpoint.InstanceId);
        try
        {
            var targets = ResourceContents().Where(view => NativeLiquidApi.IsContainer(view.Item))
                .OrderBy(view => NativeLiquidApi.Capture(view.Item)?.TotalParts > 0 ? 0 : 1).ThenBy(view => view.ContainerId)
                .ThenBy(view => view.Item.GetUniqueID()).ToArray();
            foreach (var target in targets)
            {
                if (generation != _resourceTransferGeneration || !ResourceNetworkOnline || source.parentInventory?.Handle() != original.Handle() ||
                    stillAllowed?.Invoke() == false || NativeInventoryFreezeApi.IsFrozen(source) || !source.MayRemove() || !NativeInventoryApi.IsPlayerOwned(source)) break;
                bool Current() => generation == _resourceTransferGeneration && ResourceNetworkOnline && source.parentInventory?.Handle() == original.Handle() &&
                    NativeInventoryApi.IsPlayerOwned(source) && stillAllowed?.Invoke() != false &&
                    ResourceSourceCurrent(target.Item, true) && LiquidFeeding.SameOutputQuality(source, target.Item, exactComposition: exactComposition);
                if (!Current()) continue;
                _resourceTransferItems.Add(target.Item.GetUniqueID());
                var result = NativeLiquidTransferApi.Pour(source, target.Item, maximumParts - moved, Current);
                moved = checked(moved + result.PartsMoved);
                if (result.Indeterminate)
                {
                    return new(moved, true, result.Status);
                }
                if (moved >= maximumParts || NativeLiquidApi.Capture(source)?.TotalParts == 0) break;
            }
            return new(moved, false, moved > 0 ? "poured-into-resource-tank" : "resource-liquid-full-or-incompatible");
        }
        finally { EndResourceTransfer(generation); }
    }

    internal NativeLiquidTransferResult WithdrawResourceLiquid(int instanceId, GameItem target, int maximumParts, Func<bool>? stillAllowed = null)
    {
        var view = ResourceContents().SingleOrDefault(view => view.Item.GetUniqueID() == instanceId && NativeLiquidApi.IsContainer(view.Item));
        if (view == null || maximumParts <= 0 || target == null || !NativeInventoryApi.IsPlayerOwned(target) ||
            target.parentInventory == null || target.parentInventory.IsInsertLocked() || NativeInventoryFreezeApi.IsFrozen(target))
            return new(0, false, "actual-liquid-source-unavailable");
        int generation = _resourceTransferGeneration; var targetParent = target.parentInventory;
        using var transfer = BeginResourceSourceTransfer(view.Item, true);
        bool Current() => generation == _resourceTransferGeneration && ResourceSourceCurrent(view.Item, true) &&
            target.parentInventory?.Handle() == targetParent.Handle() && stillAllowed?.Invoke() != false && LiquidFeeding.SameOutputQuality(view.Item, target);
        if (transfer == null || !Current()) return new(0, false, "resource-liquid-unavailable-or-incompatible");
        var result = NativeLiquidTransferApi.Pour(view.Item, target, maximumParts, Current);
        return result;
    }
}
