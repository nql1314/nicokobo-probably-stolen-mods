using static PsModI18n.I18n;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private GameItem? _resourceRoot, _waterRoot;
    private string _resourceUpgradeStatus = PsModI18n.I18n.Get("hold_alt_and_drag_in_a_matching");
    internal string ResourceUpgradeStatus
    {
        get => _resourceUpgradeStatus;
        private set { if (_resourceUpgradeStatus != value) { _resourceUpgradeStatus = value; NetworkPresentationChanged?.Invoke(); } }
    }
    internal GameGridInventory? ResourceInventory() => OwnedRoot(_resourceRoot, Content.ResourceDevice)
        ? Grids(_resourceRoot!).FirstOrDefault() : null;
    internal GameItem? ResourceWaterTank() => OwnedRoot(_waterRoot, Content.WaterDevice) ? _waterRoot : null;
    internal void RefreshResourceTabs(GameItem item)
    {
        // Native expansion creates fresh grids without the device inventory ID.
        // Restore it before saved contents or admission rules access those pages.
        foreach (var grid in Grids(item)) grid.identifier = Content.ContentsId;
        if (Ready && IsCurrentRun()) NetworkPresentationChanged?.Invoke();
    }
    private IReadOnlyList<NativeTransactionStep> ResourceCreationSteps(StateChange change)
    {
        var previousResource = _resourceRoot;
        return [
            PlaceDeviceStep(Content.ResourceDevice, change, item => _resourceRoot = item, () => _resourceRoot = previousResource),
            new("resource-capacity-settings", () =>
            {
                change.Commit(() =>
                {
                    State.ResourcesUnlocked = true; State.Resources ??= new();
                    State.ResourceInstanceId = _resourceRoot!.GetUniqueID();
                });
                if (!change.Verify() || !ApplyResourceCapacity()) throw new InvalidOperationException(PsModI18n.I18n.Get("void_container_capacity_could_not_be_verified"));
            }, change.Restore)
        ];
    }
    private bool ApplyResourceCapacity()
    {
        var storage = State.Resources ?? new ResourceStorageState();
        foreach (var item in NetworkDevices(Content.ResourceDevice))
        {
            var grids = Grids(item).ToArray();
            if (grids.Length == 0) return false;
            foreach (var grid in grids)
            {
                grid.SetShape(AutomatedLogisticsNumbers.ResourceWidth, storage.Rows);
                if (grid.inventoryShape.width != AutomatedLogisticsNumbers.ResourceWidth || grid.inventoryShape.height != storage.Rows) return false;
            }
            item.contentWindow!.childElement!.Validate();
            item.contentWindow.Validate();
        }
        foreach (var tank in NetworkDevices(Content.WaterDevice))
        {
            var tag = tank.state.GetTag(Content.CapacityTag); tag.Enable(); tag.SetInt(storage.WaterCapacityParts);
            tank.SyncModifiedState(); ItemArtwork.Apply(tank);
            if (NativeLiquidApi.Capture(tank)?.CapacityParts != storage.WaterCapacityParts) return false;
        }
        return true;
    }
    internal void ProcessResourceUpgrade(GameItem item, GameItem? target = null)
    {
        if (target != null ? !CanDeviceTarget(item, target, true) : !(NativeLiquidApi.IsContainer(item) ? WaterStorageReady : ResourceStorageReady)) return;
        if (_resourceTransfers != 0 || item.parentInventory is not { } slot) return;
        int id = item.GetUniqueID();
        if (!NetworkStorage.EmptyUpgrade(item)) { ResourceUpgradeStatus = PsModI18n.I18n.Get("empty_the_container_first_only_empty_storage"); return; }
        if (State.Resources?.LastUpgradeItemId == id)
        {
            // A committed receipt can outlive a failed native detach/destroy.
            // Finish paying this shell without granting its capacity again.
            if (item.MayRemove() && slot!.Expel(item) && item.parentInventory == null)
            { item.Destroy(); ResourceUpgradeStatus = PsModI18n.I18n.Get("the_previous_upgrade_container_has_now_been"); }
            else ResourceUpgradeStatus = PsModI18n.I18n.Get("previous_capacity_upgrade_applied_container_consumption_is");
            return;
        }
        int water = NativeLiquidApi.Capture(item)?.CapacityParts ?? 0;
        int rows = water > 0 ? 0 : ResourceStorageState.AddedRows(NetworkStorage.UpgradeCells(item));
        if (water == 0 && rows == 0) { ResourceUpgradeStatus = PsModI18n.I18n.Get("this_container_adds_no_capacity"); return; }
        var previous = State.Resources ?? new ResourceStorageState();
        int nextRows, nextWater;
        try
        {
            if (water > 0 && previous.WaterCapacityParts >= AutomatedLogisticsNumbers.WaterMaxParts ||
                rows > 0 && previous.Rows >= AutomatedLogisticsNumbers.ResourceMaxRows)
            { ResourceUpgradeStatus = PsModI18n.I18n.Get("capacity_limit_reached_the_container_remains"); return; }
            nextRows = water > 0 ? previous.Rows : (int)Math.Min(AutomatedLogisticsNumbers.ResourceMaxRows, (long)previous.Rows + rows);
            nextWater = water > 0 ? (int)Math.Min(AutomatedLogisticsNumbers.WaterMaxParts, (long)previous.WaterCapacityParts + water) : previous.WaterCapacityParts;
            rows = nextRows - previous.Rows; water = nextWater - previous.WaterCapacityParts;
            if (nextRows > int.MaxValue / AutomatedLogisticsNumbers.ResourceWidth) throw new OverflowException();
        }
        catch (OverflowException) { ResourceUpgradeStatus = PsModI18n.I18n.Get("capacity_exceeds_native_inventory_limits_the_container"); return; }
        var change = new StateChange(this); int generation = _resourceTransferGeneration;
        _resourceTransfers++;
        try
        {
            var result = NativeTransactionApi.Run([
                new("resource-upgrade-container", () =>
                {
                    if (!change.Current() || !slot!.childItems.ToArray().Any(child => child.Handle() == item.Handle()) || !NetworkStorage.EmptyUpgrade(item) ||
                        !item.MayRemove() || !slot.Expel(item) || item.parentInventory != null)
                        throw new InvalidOperationException(PsModI18n.I18n.Get("the_upgrade_container_changed"));
                }, () =>
                {
                    if (!change.Current() || item.unitCount != 1) return false;
                    if (item.parentInventory?.Handle() == slot!.Handle()) return true;
                    if (item.parentInventory != null) return false;
                    var restore = slot.TryInventorySlot(item, 1, item.modifiedShape);
                    return restore != null && restore.IsValid() && restore.targetItem == null && slot.UncheckedAccept(item) && item.parentInventory?.Handle() == slot.Handle();
                }),
                new("resource-upgrade-capacity", () =>
                {
                    change.Commit(() => State.Resources = new() { Rows = nextRows, WaterCapacityParts = nextWater, LastUpgradeItemId = id });
                    if (!ApplyResourceCapacity() || !change.Verify()) throw new InvalidOperationException(PsModI18n.I18n.Get("capacity_expansion_could_not_be_verified"));
                }, () => change.Restore() && ApplyResourceCapacity())
            ]);
            if (!change.Current()) return;
            if (result.Committed)
            {
                _resourceUpgradeStatus = water > 0 ? PsModI18n.I18n.Get("tank_capacity_increased_by_l", water / 1_000_000m) : PsModI18n.I18n.Get("item_grid_expanded_by_rows_cells", rows, rows * AutomatedLogisticsNumbers.ResourceWidth);
                // Identical consecutive upgrades still change the native layout.
                NetworkPresentationChanged?.Invoke();
                // Only the detached, empty paid shell is destroyed after commit.
                try { item.Destroy(); } catch (Exception ex) { log(PsModI18n.I18n.Get("upgrade_committed_empty_container_destruction_callback_failed") + ex.Message); }
                log(PsModI18n.I18n.Get("resource_network_upgrade") + ResourceUpgradeStatus);
            }
            else
            {
                ResourceUpgradeStatus = PsModI18n.I18n.Get("upgrade_was_not_completed") + result.Status;
                if (!result.Restored) { _resourceMembersReady = false; NetworkError = ResourceUpgradeStatus; }
            }
        }
        finally { EndResourceTransfer(generation); }
    }
}
