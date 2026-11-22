using static PsModI18n.I18n;
using UnityEngine;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private NativeTransactionStep PlaceDeviceStep(string id, StateChange change, Action<GameItem> assign, Action restore)
    {
        GameItem? created = null; GameGridInventory? inventory = null;
        return new("create-" + id, () =>
        {
            if (!change.Current()) throw new InvalidOperationException(PsModI18n.I18n.Get("the_run_changed"));
            inventory = Warehouse() ?? throw new InvalidOperationException(PsModI18n.I18n.Get("warehouse_is_not_ready"));
            created = NativeItemApi.Create(id);
            var slot = inventory.TryFindOneValidInventorySlot(created, false);
            if (slot == null || !slot.IsValid() || slot.targetItem != null || slot.TryAcceptOnce(1) != 1 ||
                created.parentInventory?.Handle() != inventory.Handle()) throw new InvalidOperationException(PsModI18n.I18n.Get("warehouse_is_full_waiting_for_space"));
            assign(created);
        }, () =>
        {
            if (!change.Current()) return false;
            if (created != null)
            {
                if (NativeSaveGraphApi.Items(created).Count != 1 || (NativeLiquidApi.Capture(created)?.TotalParts ?? 0) != 0) return false;
                if (created.parentInventory != null && (created.parentInventory.Handle() != inventory?.Handle() || !inventory.Expel(created))) return false;
                if (created.parentInventory != null) return false;
                Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(created);
            }
            restore(); return true;
        });
    }

    private bool DeviceOwned(GameItem item) => Ready && IsCurrentRun() && item != null && item.parentInventory != null &&
        NativeInventoryApi.IsPlayerOwned(item) && !NativeInventoryFreezeApi.IsFrozen(item) && item.MayRemove();
    internal static bool PreferDeviceUpgrade(GameItem source, GameItem target, bool upgrade) =>
        source != null && target != null && source.Handle() != target.Handle() && Content.IsDevice(target.identifier) &&
        (upgrade || source.identifier == StorageExpansionHelper.IDENTIFIER && target.identifier == Content.ResourceDevice);
    internal bool HandlesDeviceTarget(GameItem source, GameItem target, bool upgrade) => source != null && target != null &&
        (upgrade && Content.IsDevice(target.identifier) || target.identifier is Content.ResourceDevice or Content.PowerDevice or Content.WaterDevice || source.identifier == Content.WaterDevice || source.identifier == Content.PowerDevice && target.identifier != Content.PowerDevice);

    internal bool CanDeviceTarget(GameItem source, GameItem target, bool upgrade)
    {
        if (!DeviceOwned(source) || source.Handle() == target.Handle()) return false;
        // The native drain is a scene fixture, not a player-owned vessel.
        if (!upgrade && source.identifier == Content.WaterDevice && WaterHelper.IsDrain(target))
            return _resourceTransfers == 0 && !HasResourceContainerCycle(source) && !source.parentInventory!.IsRemoveLocked() &&
                NativeLiquidApi.Capture(source)?.TotalParts > 0;
        // The aquaculture reservoir stays fixed in its slot while receiving water.
        if (!upgrade && source.identifier == Content.WaterDevice && target.identifier == "ras_water_tank")
        {
            var into = NativeLiquidApi.Capture(target);
            return WaterStorageReady && NativeInventoryApi.IsPlayerOwned(target) &&
                target.parentInventory is { } inventory && !inventory.IsInsertLocked() &&
                !NativeInventoryFreezeApi.IsFrozen(target) && !source.parentInventory!.IsRemoveLocked() &&
                NativeLiquidApi.Capture(source)?.TotalParts > 0 && into != null && into.TotalParts < into.CapacityParts;
        }
        if (!DeviceOwned(target)) return false;
        if (source.identifier == StorageExpansionHelper.IDENTIFIER && target.identifier == Content.ResourceDevice)
            return _resourceTransfers == 0 && !HasResourceContainerCycle(target) && StorageExpansionHelper.CanExpand(target);
        if (upgrade)
            return target.identifier switch
            {
                Content.ResourceDevice => !HasResourceContainerCycle(target) && !Content.IsDevice(source.identifier) &&
                    (State.Resources?.Rows ?? AutomatedLogisticsNumbers.ResourceInitialRows) < AutomatedLogisticsNumbers.ResourceMaxRows && NetworkStorage.EmptyUpgrade(source) && !NativeLiquidApi.IsContainer(source) && NetworkStorage.UpgradeCells(source) > 0,
                Content.WaterDevice => !HasResourceContainerCycle(target) && !Content.IsDevice(source.identifier) &&
                    (State.Resources?.WaterCapacityParts ?? AutomatedLogisticsNumbers.WaterInitialParts) < AutomatedLogisticsNumbers.WaterMaxParts && NetworkStorage.EmptyUpgrade(source) && NativeLiquidApi.Capture(source)?.CapacityParts > 0,
                Content.PowerDevice => target.contentWindow?.childElement?.TryCast<GameGridInventory>() != null && source.identifier == "metal_ingot" && Purity(source) >= 4 &&
                    State.Power.Height < AutomatedLogisticsNumbers.PowerMaxRows,
                _ => false
            };
        if (target.identifier == Content.ResourceDevice) return Grids(target).Any() && NetworkStorage.IsResourceItem(source);
        if (target.identifier == Content.PowerDevice) return target.contentWindow?.childElement?.TryCast<GameGridInventory>() != null && NetworkStorage.IsPowerRegionItem(source);
        if (target.identifier == Content.WaterDevice) return WaterStorageReady && NativeLiquidApi.Capture(source)?.TotalParts > 0;
        if (source.identifier == Content.WaterDevice) return WaterStorageReady && NativeLiquidApi.IsContainer(target);
        return source.identifier == Content.PowerDevice && ChargeTargets(target).Length > 0;
    }
    internal void DeviceTarget(GameItem source, GameItem target, bool upgrade)
    {
        if (!CanDeviceTarget(source, target, upgrade)) return;
        string message;
        if (source.identifier == StorageExpansionHelper.IDENTIFIER && target.identifier == Content.ResourceDevice)
        {
            StorageExpansionHelper.Expand(target, source);
            message = PsModI18n.I18n.Get("void_container_tabs", 1 + StorageExpansionHelper.GetExtraTabs(target), StorageExpansionHelper.MAX_TABS);
        }
        else if (upgrade)
        {
            if (target.identifier == Content.PowerDevice) message = ExpandPower(source, target);
            else { ProcessResourceUpgrade(source, target); message = ResourceUpgradeStatus; }
        }
        else if (target.identifier == Content.ResourceDevice)
            message = target.TryFindOneValidInventorySlot(source)?.inventory is GameGridInventory grid
                ? NativeItemTransferApi.Move(source, grid, source.unitCount).Status : "no-valid-slot";
        else if (target.identifier == Content.PowerDevice)
            message = NativeItemTransferApi.Move(source, target.contentWindow!.childElement!.Cast<GameGridInventory>(), source.unitCount).Status;
        else if (source.identifier == Content.PowerDevice) message = ChargeFromDevice(source, target);
        else if (source.identifier == Content.WaterDevice && WaterHelper.IsDrain(target))
        {
            WaterHelper.EmptyContainer(source);
            RefreshDeviceWater(source);
            if (NativeLiquidApi.Capture(source)?.TotalParts != 0)
                throw new InvalidOperationException(PsModI18n.I18n.Get("void_tank_emptying_could_not_be_verified"));
            message = PsModI18n.I18n.Get("void_tank_emptied");
        }
        else
        {
            message = NativeLiquidTransferApi.Pour(source, target, int.MaxValue).Status;
            RefreshDeviceWater(source); RefreshDeviceWater(target);
        }
        CollectGridEnergy();
        ResourceUpgradeStatus = message;
        NetworkPresentationChanged?.Invoke();
        info?.Invoke(message);
    }

    private string ExpandPower(GameItem material, GameItem target)
    {
        var parent = material.parentInventory!; int units = material.unitCount;
        var grid = target.contentWindow!.childElement!.Cast<GameGridInventory>();
        int width = Math.Max(State.Power.Width, AutomatedLogisticsNumbers.Parameters.PowerWidth), height = Math.Max(State.Power.Height, AutomatedLogisticsNumbers.Parameters.PowerHeight);
        var change = new StateChange(this);
        var result = NativeTransactionApi.Run([
            new("power-upgrade-ingot", () =>
            {
                if (!change.Current() || material.parentInventory?.Handle() != parent.Handle() || material.unitCount != units ||
                    material.identifier != "metal_ingot" || Purity(material) < 4 || !material.MayRemove())
                    throw new InvalidOperationException(PsModI18n.I18n.Get("the_upgrade_ingot_changed"));
                if (units > 1) material.SetUnitCount(units - 1);
                else if (!parent.Expel(material) || material.parentInventory != null) throw new InvalidOperationException(PsModI18n.I18n.Get("upgrade_material_could_not_be_removed"));
                if (units > 1 && material.unitCount != units - 1) throw new InvalidOperationException(PsModI18n.I18n.Get("upgrade_material_count_could_not_be_verified"));
            }, () =>
            {
                if (!change.Current()) return false;
                if (material.parentInventory == null && !parent.UncheckedAccept(material)) return false;
                if (material.parentInventory?.Handle() != parent.Handle()) return false;
                material.SetUnitCount(units); return material.unitCount == units;
            }),
            new("power-upgrade-row", () =>
            {
                change.Commit(() => { State.Power.Width = width; State.Power.Height = checked(height + 1); });
                foreach (var device in NetworkDevices(Content.PowerDevice))
                    device.contentWindow!.childElement!.Cast<GameGridInventory>().SetShape(width, State.Power.Height);
                if (!change.Verify() || grid.inventoryShape.height != State.Power.Height) throw new InvalidOperationException(PsModI18n.I18n.Get("power_storage_expansion_could_not_be_verified"));
            }, () =>
            {
                if (!change.Restore()) return false;
                foreach (var device in NetworkDevices(Content.PowerDevice))
                    device.contentWindow!.childElement!.Cast<GameGridInventory>().SetShape(width, height);
                return grid.inventoryShape.height == height && grid.inventoryShape.width == width;
            })
        ]);
        if (result.Committed && units == 1 && material.parentInventory == null) material.Destroy();
        if (!result.Committed && !result.Restored && change.Current()) NetworkError = result.Status;
        return result.Committed ? PsModI18n.I18n.Get("void_grid_expanded_by_one_row", width, height + 1) : PsModI18n.I18n.Get("expansion_was_not_completed") + result.Status;
    }

    private GameItem[] ChargeTargets(GameItem target)
    {
        var sources = PowerBatteries().Select(item => item.Handle()).ToHashSet();
        return NativeSaveGraphApi.Items(target).Where(item => !sources.Contains(item.Handle()) &&
            item.IsTag("power_source_item") && !PowerHelper.IsDisposablePowerSource(item) &&
            item.GetTagReadonly("power_source_item_max_energy")?.valueInt > NativePowerApi.GetEnergy(item) &&
            item.unitCount == 1 && DeviceOwned(item)).ToArray();
    }
    private string ChargeFromDevice(GameItem device, GameItem target)
    {
        var targets = ChargeTargets(target);
        if (targets.Length == 0) return PsModI18n.I18n.Get("no_battery_needs_charging");
        var receivers = targets.Select(item => new EnergySnapshot(item)).ToArray();
        long required = receivers.Sum(item => (long)item.Maximum - item.Energy);
        if (required <= 0 || required > int.MaxValue) return PsModI18n.I18n.Get("charge_amount_is_out_of_range");
        CollectGridEnergy();
        if (_powerLedgerBusy || State.Power.StoredEnergy < required) return PsModI18n.I18n.Get("insufficient_energy_none_was_spent");
        long before = State.Power.StoredEnergy;
        using var freeze = NativeInventoryFreezeApi.TryAcquire(receivers.Select(entry => entry.Item).Concat(new[] { device, target }));
        if (freeze == null) return PsModI18n.I18n.Get("the_item_is_in_use");
        var change = new StateChange(this);
        bool Current() => change.Current() && receivers.All(entry => entry.SameItem()) &&
            NativeInventoryApi.IsPlayerOwned(device) && NativeInventoryApi.IsPlayerOwned(target);
        _powerLedgerBusy = true;
        try
        {
            var result = NativeTransactionApi.Run([new("device-charge", () =>
            {
                if (!Current() || State.Power.StoredEnergy != before || receivers.Any(entry => NativePowerApi.GetEnergy(entry.Item) != entry.Energy)) throw new InvalidOperationException(PsModI18n.I18n.Get("the_charging_target_changed"));
                change.Commit(() => State.Power.StoredEnergy = before - required);
                if (!change.Verify()) throw new InvalidOperationException(PsModI18n.I18n.Get("charging_energy_balance_could_not_be_saved"));
                foreach (var receiver in receivers)
                {
                    if (!Current()) throw new InvalidOperationException(PsModI18n.I18n.Get("the_run_or_item_changed_during_charging"));
                    PowerHelper.SetPowerSourceAt(receiver.Item, receiver.Maximum);
                    PowerHelper.UpdateEnergyCreditSprite(receiver.Item);
                }
                if (!Current() || receivers.Any(entry => NativePowerApi.GetEnergy(entry.Item) != entry.Maximum) ||
                    State.Power.StoredEnergy != before - required || !change.Verify())
                    throw new InvalidOperationException(PsModI18n.I18n.Get("charging_energy_verification_failed"));
            }, () =>
            {
                if (!change.Current()) return false;
                bool restored = change.Restore();
                foreach (var entry in receivers) restored = entry.Restore() && restored;
                return restored;
            })]);
            if (!result.Committed && !result.Restored && change.Current()) NetworkError = PsModI18n.I18n.Get("charging_rollback_failed") + result.Status;
            return result.Committed ? PsModI18n.I18n.Get("charging_complete", required) : PsModI18n.I18n.Get("charging_was_not_completed") + result.Status;
        }
        finally { _powerLedgerBusy = false; }
    }
    private sealed class EnergySnapshot(GameItem item)
    {
        internal readonly GameItem Item = item;
        internal readonly int Energy = NativePowerApi.GetEnergy(item), Units = item.unitCount,
            Raw = item.GetTagReadonly("power_source_item_energy")!.valueInt,
            Maximum = item.GetTagReadonly("power_source_item_max_energy")!.valueInt;
        private readonly IntPtr? _parent = item.parentInventory?.Handle();
        private readonly int _id = item.GetUniqueID();
        internal bool SameItem() => Item.GetUniqueID() == _id && Item.parentInventory?.Handle() == _parent && NativeInventoryApi.IsPlayerOwned(Item);
        internal bool Restore()
        {
            if (!SameItem()) return false;
            Item.SetUnitCount(Units); PowerHelper.SetPowerSourceAt(Item, Raw); PowerHelper.UpdateEnergyCreditSprite(Item);
            return Item.unitCount == Units && NativePowerApi.GetEnergy(Item) == Energy;
        }
    }
}
