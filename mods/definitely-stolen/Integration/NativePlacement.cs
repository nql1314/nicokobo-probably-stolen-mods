namespace DefinitelyStolen.Integration;
internal sealed record NativePlacement(GameItem Item, GridShape Shape);
internal static class NativePlacementApi
{
    internal static IReadOnlyList<NativePlacement>? PlanWholeGrid(GameGridInventory grid, IReadOnlyList<GameItem> items) =>
        Nicokobo.ContentInterop.InventoryPlacement.PlanWholeGrid(grid, items)?.Select(p => new NativePlacement(p.Item, p.Shape)).ToArray();
}
