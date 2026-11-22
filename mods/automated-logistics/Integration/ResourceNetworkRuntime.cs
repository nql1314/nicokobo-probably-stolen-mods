using static PsModI18n.I18n;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    internal string PendingNumbers => AutomatedLogisticsNumbers.InternalTestPreset ? PsModI18n.I18n.Get("internal_test_preset_same_default_parameters") : PsModI18n.I18n.Get("production_parameters_configured");
    internal string ResourceUnlockInfo => PsModI18n.I18n.Get("free_after_the_first_showcase_expansion");
    internal string PowerUnlockInfo => PsModI18n.I18n.Get("free_after_the_commercial_power_unit_upgrade", AutomatedLogisticsNumbers.Parameters.PowerWidth, AutomatedLogisticsNumbers.Parameters.PowerHeight);
    internal static bool HasNetworkUpgrade(string id)
    {
        var store = PlayerStore.Instance;
        return store != null && store.networkUpgrade.TryGetValue(id, out var upgrade) && upgrade != null && upgrade.state == 2;
    }
    internal static bool HasReactorPerk() => PlayerStore.Instance != null && StartingPerk.IsMakeshiftReactor();
    internal static bool CanUnlockPowerGrid() => HasReactorPerk() || HasNetworkUpgrade(AutomatedLogisticsNumbers.CommercialPowerUpgrade);

    internal void UnlockAvailableNetworks()
    {
        if (!Ready || !IsCurrentRun() || NativeInventoryApi.IsItemDragActive || _resourceTransfers != 0) return;
        if (!State.ResourcesUnlocked && !_resourceUnlockAttempted && HasNetworkUpgrade(AutomatedLogisticsNumbers.ResourceUpgrade))
        { _resourceUnlockAttempted = true; UnlockResources(); }
        if (!Ready) return;
        if (!State.WaterUnlocked && HasNetworkUpgrade(AutomatedLogisticsNumbers.WaterUpgrade)) UnlockWater();
        if (!State.Power.Unlocked && !_powerUnlockAttempted && CanUnlockPowerGrid())
        { _powerUnlockAttempted = true; UnlockPower(); }
    }
    internal string UnlockResources()
    {
        var numbers = AutomatedLogisticsNumbers.Require();
        if (!Ready || !IsCurrentRun() || State.ResourcesUnlocked) throw new InvalidOperationException(PsModI18n.I18n.Get("resource_network_is_already_enabled_or_the"));
        if (!HasNetworkUpgrade(AutomatedLogisticsNumbers.ResourceUpgrade)) throw new InvalidOperationException(PsModI18n.I18n.Get("complete_the_first_showcase_expansion_first"));
        var change = new StateChange(this);
        var result = NativeTransactionApi.Run(ResourceCreationSteps(change));
        if (!result.Committed) _resourceUnlockAttempted = false;
        if (result.Committed) { _resourceMembersReady = true; NetworkPresentationChanged?.Invoke(); }
        if (!result.Committed && !result.Restored && change.Current()) { _resourceMembersReady = false; NetworkError = result.Status; }
        return result.Committed ? PsModI18n.I18n.Get("resource_network_enabled_20_5_item_grid") : PsModI18n.I18n.Get("network_was_not_enabled") + result.Status;
    }
    internal string UnlockWater()
    {
        if (!Ready || !IsCurrentRun() || State.WaterUnlocked || !HasNetworkUpgrade(AutomatedLogisticsNumbers.WaterUpgrade))
            throw new InvalidOperationException(PsModI18n.I18n.Get("unlock_rusty_water_supply_first_the_void"));
        var change = new StateChange(this); var previous = _waterRoot;
        var result = NativeTransactionApi.Run([
            PlaceDeviceStep(Content.WaterDevice, change, item => _waterRoot = item, () => _waterRoot = previous),
            new("water-settings", () =>
            {
                change.Commit(() => { State.WaterUnlocked = true; foreach (var history in State.WaterHistory) history.Baseline = 0; State.WaterInstanceId = _waterRoot!.GetUniqueID(); State.Resources ??= new(); });
                if (!change.Verify() || !ApplyResourceCapacity()) throw new InvalidOperationException(PsModI18n.I18n.Get("tank_settings_could_not_be_verified"));
            }, change.Restore)]);
        if (result.Committed) { BeginTotalStatistics(); NetworkPresentationChanged?.Invoke(); }
        if (!result.Committed && !result.Restored && change.Current()) { Ready = false; Error = result.Status; }
        return result.Committed ? PsModI18n.I18n.Get("void_tank_unlocked_10_l") : PsModI18n.I18n.Get("tank_was_not_granted") + result.Status;
    }
    internal string UnlockPower()
    {
        if (!Ready || !IsCurrentRun() || State.Power.Unlocked) throw new InvalidOperationException(PsModI18n.I18n.Get("power_grid_is_already_enabled_or_the"));
        if (!CanUnlockPowerGrid())
            throw new InvalidOperationException(PsModI18n.I18n.Get("unlock_the_commercial_power_unit_first"));
        var change = new StateChange(this);
        var previousRoot = _powerRoot;
        // A reactor save may already have a grid that could never bind to the power ledger.
        var existingGrid = HasReactorPerk() ? NetworkDevices(Content.PowerDevice).OrderBy(item => item.GetUniqueID()).FirstOrDefault() : null;
        var steps = new List<NativeTransactionStep>();
        if (existingGrid == null)
            steps.Add(PlaceDeviceStep(Content.PowerDevice, change, item => _powerRoot = item, () => _powerRoot = previousRoot));
        steps.Add(new("power-settings", () =>
        {
            change.Commit(() => State.Power = CreateUnlockedPowerState(State.Power, existingGrid ?? _powerRoot!));
            if (!change.Verify()) throw new InvalidOperationException(PsModI18n.I18n.Get("void_grid_settings_could_not_be_verified"));
            if (existingGrid != null) _powerRoot = existingGrid;
        }, change.Restore));
        var result = NativeTransactionApi.Run(steps);
        if (!result.Committed) _powerUnlockAttempted = false;
        if (result.Committed) { BeginTotalStatistics(); NetworkPresentationChanged?.Invoke(); }
        return result.Committed ? PsModI18n.I18n.Get("power_network_enabled") : PsModI18n.I18n.Get("grid_was_not_enabled") + result.Status;
    }

    private static PowerNetworkState CreateUnlockedPowerState(PowerNetworkState previous, GameItem grid)
    {
        var numbers = AutomatedLogisticsNumbers.Require();
        return new()
        {
            Unlocked = true,
            InstanceId = grid.GetUniqueID(),
            StoredEnergy = previous.StoredEnergy,
            Totals = new() { Baseline = 0 },
            Width = Math.Max(previous.Width, numbers.PowerWidth),
            Height = Math.Max(previous.Height, numbers.PowerHeight)
        };
    }
}
