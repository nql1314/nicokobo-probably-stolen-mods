namespace Nicokobo.ContentInterop;

/// <summary>A detached placement proposal. Shape is a clone, not the item's
/// current shape. Revalidate the native slot immediately before accepting.</summary>

/// <summary>Plans a whole batch of detached single items without changing their
/// IDs, shapes, parents or quantities, or constructing a native inventory.
/// Does not accept items, reserve live slots, grant rewards or save the game.</summary>
internal static class InventoryPlacement
{
    public static bool IsAvailable => true;

    /// <summary>Null means unavailable, invalid items, rejected native admission
    /// or insufficient room for the whole batch. Stacking and splitting are
    /// excluded. Searches in the native row/orientation order. Returned proposals
    /// do not prove that subsequent acceptance, rollback or saving will succeed.</summary>
    public static IReadOnlyList<InventoryPlacementEntry>? PlanWholeGrid(GameGridInventory inventory,
        IReadOnlyList<GameItem> products) => PlanGrid(inventory, products, singleUnits: true);

    // Save recovery preserves a whole saved stack and its identity. It must
    // never merge into another item or split while the persisted receipt lives.
    internal static IReadOnlyList<InventoryPlacementEntry>? PlanWholeStacks(GameGridInventory inventory,
        IReadOnlyList<GameItem> products) => PlanGrid(inventory, products, singleUnits: false);

    private static IReadOnlyList<InventoryPlacementEntry>? PlanGrid(GameGridInventory inventory,
        IReadOnlyList<GameItem> products, bool singleUnits)
    {
        if (!IsAvailable || inventory == null || NativeObject.Handle(inventory) == IntPtr.Zero || products == null ||
            products.Count > 2048) return null;
        try
        {
            if (inventory.IsInsertLocked()) return null;
            var seen = new HashSet<IntPtr>();
            foreach (var product in products)
                if (product == null || NativeObject.Handle(product) == IntPtr.Zero || !seen.Add(NativeObject.Handle(product)) ||
                    product.parentInventory != null || product.unitCount <= 0 || singleUnits && product.unitCount != 1) return null;
            if (products.Count == 0) return Array.Empty<InventoryPlacementEntry>();
            var shape = inventory.inventoryShape;
            var children = inventory.childItems;
            if (shape == null || children == null) return null;
            int width = shape.globalWidth, height = shape.globalHeight;
            if (width <= 0 || height <= 0 || width > 64 || height > 64) return null;
            var blocked = new bool[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    blocked[y * width + x] = shape.Get(x, y) != 0;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child == null || NativeObject.Handle(child) == IntPtr.Zero || NativeObject.Handle(child.parentInventory) != NativeObject.Handle(inventory) ||
                    child.modifiedShape == null) return null;
                BlockOccupied(blocked, width, height, child.modifiedShape);
            }
            var grid = new WorkshopGridPlanner(width, height, blocked);
            var result = new List<InventoryPlacementEntry>(products.Count);
            foreach (var product in products)
            {
                var planned = ReserveShape(grid, product.shape);
                if (planned == null) return null;
                int units = product.unitCount;
                var slot = inventory.TryInventorySlot(product, units, planned);
                if (slot == null || NativeObject.Handle(slot) == IntPtr.Zero || NativeObject.Handle(slot.item) != NativeObject.Handle(product) ||
                    NativeObject.Handle(slot.inventory) != NativeObject.Handle(inventory) || slot.targetItem != null || slot.numTransfer != units ||
                    !slot.IsValid() || product.parentInventory != null || product.unitCount != units) return null;
                result.Add(new(product, planned));
            }
            return result.AsReadOnly();
        }
        catch { return null; }
    }

    private static void BlockOccupied(bool[] blocked, int width, int height, GridShape shape)
    {
        int left = shape.minX, top = shape.minY, right = shape.maxX, bottom = shape.maxY;
        if (shape.GetOutsideBounds() == 1)
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (x < left || x > right || y < top || y > bottom) blocked[y * width + x] = true;
        for (int y = Math.Max(0, top); y <= Math.Min(height - 1, bottom); y++)
            for (int x = Math.Max(0, left); x <= Math.Min(width - 1, right); x++)
                if (!blocked[y * width + x] && shape.Get(x, y) == 1) blocked[y * width + x] = true;
    }

    private static GridShape? ReserveShape(WorkshopGridPlanner grid, GridShape? source)
    {
        if (source == null) return null;
        var candidate = new GridShapeBuilder(source);
        // Build 25382790: orientation 4,3,2,1 (4 normalizes to 0),
        // first unflipped and then flipped, for keepOrientation=false.
        for (int flip = 0; flip < 2; flip++)
            for (int turn = 0; turn < 4; turn++)
            {
                candidate.SetTransform(0, 0, flip != 0, 4 - turn);
                var oriented = candidate.shape;
                if (oriented == null) return null;
                int width = oriented.globalWidth, height = oriented.globalHeight;
                if (width <= 0 || height <= 0 || width > 64 || height > 64) return null;
                var footprint = new List<WorkshopGridCell>();
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        if (oriented.Get(x, y) == 1) footprint.Add(new(x, y));
                if (!grid.TryReserve(footprint, out var position)) continue;
                candidate.SetPosition(position.X, position.Y);
                return candidate.Clone();
            }
        return null;
    }
}
