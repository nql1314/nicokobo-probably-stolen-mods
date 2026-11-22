namespace AugMechanicalAscension.Official;
internal sealed record OfficialInventoryPlacement(GameItem Item, GridShape Shape);
internal static class OfficialInventoryPlacementApi
{
    public static bool IsAvailable => true;
    public static IReadOnlyList<OfficialInventoryPlacement>? PlanWholeGrid(GameGridInventory grid, IReadOnlyList<GameItem> items) =>
        Nicokobo.ContentInterop.InventoryPlacement.PlanWholeGrid(grid, items)?.Select(p => new OfficialInventoryPlacement(p.Item, p.Shape)).ToArray();
    internal static IReadOnlyList<OfficialInventoryPlacement>? PlanWholeStacks(GameGridInventory grid, IReadOnlyList<GameItem> items) =>
        Nicokobo.ContentInterop.InventoryPlacement.PlanWholeStacks(grid, items)?.Select(p => new OfficialInventoryPlacement(p.Item, p.Shape)).ToArray();
}
