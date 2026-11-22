using static PsModI18n.I18n;
using Newtonsoft.Json;
using Nicokobo.AutomatedLogistics.Domain;
using Nicokobo.AutomatedLogistics.Integration;

namespace Nicokobo.AutomatedLogistics;

/// <summary>The same resource storage and power operations used by the terminal.
/// Read methods never transfer, charge, manufacture or consume an item.</summary>
public static class AutomatedLogisticsApi
{
    private static AutomatedLogisticsRuntime Runtime
    {
        get
        {
            var runtime = NativeHooks.Runtime ?? throw new InvalidOperationException(PsModI18n.I18n.Get("automation_runtime_is_not_installed"));
            runtime.EnsureLoaded(); if (!runtime.Ready) throw new InvalidOperationException(runtime.Error); return runtime;
        }
    }
    public static bool InternalTestPreset => AutomatedLogisticsNumbers.InternalTestPreset;
    public static string ConfigurationStatus => Runtime.PendingNumbers;
    public static string StateJson() => JsonConvert.SerializeObject(Runtime.State);
    public static string Manufacture(GameItem machine) => NativeMachineRuntimeApi.ProcessBatch(machine);
    public static void OpenTerminal(GameItem terminal) => NativeHooks.Open?.Invoke(terminal);
    public static NativeMachineInventory Inventory(GameItem machine) => MachineAccess.TryInventory(machine, out var inventory) && inventory != null
        ? inventory : throw new InvalidOperationException(PsModI18n.I18n.Get("machine_inventory_is_unavailable"));
    public static string UnlockResources() => Runtime.UnlockResources();
    public static bool ResourceNetworkOnline => Runtime.ResourceNetworkOnline;
    public static string SetResourceNetworkEnabled(bool enabled) => Runtime.SetResourceNetworkEnabled(enabled);
    public static IReadOnlyList<ResourceContainerView> ResourceContainers() => Runtime.ResourceContainers();
    public static IReadOnlyList<ResourceItemView> ResourceContents() => Runtime.ResourceContents();
    public static GameGridInventory? ResourceRegion() => Runtime.ResourceInventory();
    public static GameItem? ResourceWaterTank() => Runtime.ResourceWaterTank();
    public static NativeItemTransferResult DepositResource(GameItem item, int maximum) => Runtime.DepositResource(item, maximum);
    public static NativeItemTransferResult WithdrawResource(int instanceId, int count, GameGridInventory? destination = null) => Runtime.WithdrawResource(instanceId, count, destination);
    public static NativeLiquidTransferResult PourResource(GameItem source, int maximumParts) => Runtime.PourResource(source, maximumParts);
    public static NativeLiquidTransferResult WithdrawResourceLiquid(int instanceId, GameItem target, int maximumParts) => Runtime.WithdrawResourceLiquid(instanceId, target, maximumParts);
    public static string UnlockPower() => Runtime.UnlockPower();
    public static GameGridInventory? PowerRegion() => Runtime.PowerInventory();
    public static IReadOnlyList<GameItem> PowerBatteries() => Runtime.PowerBatteries();
    public static long PowerTotal() => Runtime.PowerTotal();
    public static string PowerStatus(GameItem machine) => Runtime.PowerStatus(machine);
    public static void ConfigureMachine(GameItem machine, bool fillEnabled, bool ejectEnabled)
    {
        var settings = Runtime.Settings(machine); settings.FillEnabled = fillEnabled; settings.EjectEnabled = ejectEnabled;
        Runtime.ChangeSettings(settings);
    }
    public static void FillOnce(GameItem machine) => Runtime.FillOnce(machine);
    public static void EjectOnce(GameItem machine) => Runtime.EjectOnce(machine);
    public static string PreviewFill(GameItem machine) => Runtime.Preview(machine);
    public static string Tooltip(GameItem item) => NativeItemInspectionApi.BuildTooltip(item)?.Build().nakedText ?? throw new InvalidOperationException(PsModI18n.I18n.Get("native_tooltip_is_unavailable"));
}
