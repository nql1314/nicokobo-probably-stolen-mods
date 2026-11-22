using MechcoreProtocol.ModuleMatrix.Definitions;
using MechcoreProtocol.ModuleMatrix.Domain;

namespace MechcoreProtocol.ModuleMatrix;

internal static class MatrixGeometry
{
    internal static bool HasEffect(GameItem item, string id) =>
        new[] { "MODULE_EFFECT1_TAG", "MODULE_EFFECT2_TAG" }.Any(tag => item.IsTag(tag) && item.GetTagReadonly(tag).GetString() == id);

    internal static HashSet<GridCell> ItemCells(GameItem item)
    {
        var result = new HashSet<GridCell>();
        var shape = item.modifiedShape;
        if (shape == null) return result;
        for (int y = shape.minY; y <= shape.maxY; y++)
        for (int x = shape.minX; x <= shape.maxX; x++)
            if (shape.Get(x, y) == 1) result.Add(new(x, y));
        return result;
    }
    internal static bool Adjacent(GameItem a, GameItem b)
    {
        if (a == b || a.parentInventory == null || a.parentInventory != b.parentInventory) return false;
        var first = ItemCells(a); var second = ItemCells(b);
        return first.Any(cell => second.Contains(new(cell.X - 1, cell.Y)) || second.Contains(new(cell.X + 1, cell.Y)) ||
            second.Contains(new(cell.X, cell.Y - 1)) || second.Contains(new(cell.X, cell.Y + 1)));
    }
    internal static bool HasLearningNode(GameItem item) => MatrixMod.Settings.Nodes && item.parentInventory is GameGridInventory grid &&
        grid.childItems.Any(node => node.unitCount > 0 && GeneralHelper.IsItemOwned(node) && node.identifier == NodeItemDefinitions.N16.Id &&
            HasEffect(node, NodeEffectDefinitions.N16.Id) && Adjacent(item, node));

    internal static PlacementLayout? Layout(GameItem item)
    {
        if (item.parentInventory is not GameGridInventory grid || !grid.childItems.Contains(item)) return null;
        var bay = new HashSet<GridCell>(); var shape = grid.inventoryShape;
        for (int y = shape.minY; y <= shape.maxY; y++)
        for (int x = shape.minX; x <= shape.maxX; x++)
            if (shape.Get(x, y) == 0) bay.Add(new(x, y));
        var others = new Dictionary<GridCell, string>();
        int index = 0;
        foreach (var other in grid.childItems)
        {
            if (other == item) continue;
            string identity = (++index).ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var cell in ItemCells(other)) others.TryAdd(cell, identity);
        }
        var own = ItemCells(item);
        return own.Count == 0 || own.Any(cell => !bay.Contains(cell)) ? null : new(bay, own, others);
    }
}
