
namespace Nicokobo.AutomatedLogistics.Integration;

internal static class NetworkStorage
{
    internal static bool IsResourceItem(GameItem item) => NativeInventoryApi.IsPlayerOwned(item) && Supported(item) &&
        NativeSaveGraphApi.Items(item).All(node => !node.IsGameItemType("MACHINE") && !node.IsTag("CONTAINER_TAG") &&
            !node.IsTag("LIQUID_CONTAINER_TAG") && !node.IsGameItemType("STORAGE") && !NativeLiquidApi.IsContainer(node) && !AutomatedLogisticsRuntime.Grids(node).Any() &&
            !Content.IsDevice(node.identifier));

    internal static int UpgradeCells(GameItem item)
    {
        if (!EmptyUpgrade(item) || NativeLiquidApi.IsContainer(item)) return 0;
        long cells = 0;
        foreach (var grid in AutomatedLogisticsRuntime.Grids(item).DistinctBy(grid => grid.Handle()))
            for (int y = 0; y < grid.inventoryShape.height; y++)
                for (int x = 0; x < grid.inventoryShape.width; x++)
                    if (grid.inventoryShape.GetLocal(x, y) == 0) cells++;
        return checked((int)cells);
    }
    internal static bool EmptyUpgrade(GameItem item) => item != null && item.Handle() != IntPtr.Zero && item.unitCount == 1 &&
        NativeInventoryApi.IsPlayerOwned(item) && !item.IsGameItemType("MACHINE") && item.TryCast<GameCharacterItem>() == null &&
        (item.IsTag("CONTAINER_TAG") || NativeLiquidApi.IsContainer(item)) &&
        !Content.IsDevice(item.identifier) &&
        NativeSaveGraphApi.Items(item).Count == 1 && (NativeLiquidApi.Capture(item)?.TotalParts ?? 0) == 0;
    internal static readonly HashSet<string> ChargingEquipmentIds = ["powerblock", "recharger", "recharger_base"];
    internal static bool IsBattery(GameItem item) => item != null && item.Handle() != IntPtr.Zero && item.unitCount > 0 &&
        item.IsGameItemType("ENERGY_CELL") && item.IsTag("power_source_item") &&
        item.GetTagReadonly("power_source_item_energy")?.valueInt >= 0 &&
        item.GetTagReadonly("power_source_item_max_energy")?.valueInt > 0;
    internal static bool IsPowerRegionItem(GameItem item) => NativeInventoryApi.IsPlayerOwned(item) &&
        (IsBattery(item) || HypertechBatteryHelper.IsHypertechBattery(item) || ChargingEquipmentIds.Contains(item.identifier)) &&
        Supported(item) && !ContainsAquacultureSystem(item);
    private static bool ContainsAquacultureSystem(GameItem item) =>
        NativeSaveGraphApi.Items(item).Any(node => node.identifier == "ras");
    internal static bool AllowsPowerInventory(GameItem item, GameInventory inventory)
    {
        var seen = new HashSet<IntPtr>();
        for (GameItem? owner = inventory.GetParentItem(); owner != null && seen.Add(owner.Handle()); owner = owner.parentInventory?.GetParentItem())
            if (owner.identifier == Content.PowerDevice) return !ContainsAquacultureSystem(item);
        return true;
    }
    private static bool Supported(GameItem item)
    {
        try
        {
            var nodes = NativeSaveGraphApi.Items(item);
            if (nodes.Any(node => !NativeItemApi.Exists(node.identifier) || node.TryCast<GameCharacterItem>() != null)) return false;
            NativeSaveGraphApi.Validate(item);
            return true;
        }
        catch { return false; }
    }
    internal static string LiquidKind(GameItem item)
    {
        var liquid = NativeLiquidApi.Capture(item);
        if (liquid == null || liquid.TotalParts <= 0) return "";
        int grade = NativeLiquidApi.WaterQuality(item);
        if (grade >= 1) return "water:" + grade;
        var components = liquid.Contents.Where(part => part.Parts > 0).ToArray();
        return components.Length == 1 && components[0].LiquidId != "water" ? components[0].LiquidId : "";
    }
}
