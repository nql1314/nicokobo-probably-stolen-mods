using System.Text;

namespace DefinitelyStolen.Integration;

internal static class InventoryLayout
{
    // Exact layout key: rearrangement matters even when the item count is unchanged.
    // UI preflight may be reused; actual theft still validates native admission again.
    internal static string Key(GameGridInventory grid)
    {
        var key = new StringBuilder().Append(grid.GetHashCode()).Append(':').Append(grid.IsInsertLocked());
        void Shape(GridShape shape)
        {
            key.Append('|').Append(shape.minX).Append(',').Append(shape.minY).Append(',')
                .Append(shape.width).Append(',').Append(shape.height).Append(',')
                .Append(shape.orientation).Append(',').Append(shape.flipped).Append(',').Append(shape.GetOutsideBounds()).Append(':');
            for (int y = 0; y < shape.height; y++)
                for (int x = 0; x < shape.width; x++) key.Append(shape.GetLocal(x, y)).Append(',');
        }
        Shape(grid.inventoryShape);
        foreach (var item in grid.childItems)
        { key.Append(';').Append(item.GetHashCode()).Append(':').Append(item.unitCount); Shape(item.modifiedShape); }
        return key.ToString();
    }
}
