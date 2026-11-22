using System.Text;
using MechcoreProtocol.SynthesisExpansion.Domain;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class StorageShape
{
    internal static readonly IReadOnlyList<string> SupportedBayIds = Array.AsReadOnly(new[] { "storage_bay", "storage_bay_large" });

    internal static bool TryCapture(GameItem item, out int width, out string data)
    {
        width = 0; data = "";
        if (!SupportedBayIds.Contains(item.identifier) || !item.IsGameItemType("STORAGE") ||
            !item.IsTag("CONTAINER_TAG") || item.IsTag("LIQUID_CONTAINER_TAG") ||
            item.contentWindow?.childElement is not GameGridInventory grid || grid.GetParentItem() != item) return false;
        var shape = grid.inventoryShape;
        if (shape == null || shape.width <= 0 || shape.height <= 0 || shape.width > int.MaxValue / shape.height) return false;
        width = shape.width;
        var cells = new StringBuilder(checked(width * shape.height));
        for (int y = 0; y < shape.height; y++)
        for (int x = 0; x < width; x++)
        {
            byte cell = shape.GetLocal(x, y);
            if (cell is not (0 or 1)) return false;
            cells.Append(cell == 0 ? '0' : '1');
        }
        data = cells.ToString();
        return ScavengingRobotRules.IsValidStorageShape(width, data);
    }
}
