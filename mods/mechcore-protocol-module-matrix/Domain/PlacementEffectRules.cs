namespace MechcoreProtocol.ModuleMatrix.Domain;

internal readonly record struct GridCell(int X, int Y);

internal sealed record PlacementLayout(
    IReadOnlyCollection<GridCell> BayCells,
    IReadOnlyCollection<GridCell> OwnCells,
    IReadOnlyDictionary<GridCell, string> OtherOccupants);

internal sealed record PlacementMetrics(
    bool TouchesTop,
    bool TouchesBottom,
    bool TouchesLeft,
    bool TouchesRight,
    int DistinctEmptyOutlineCells,
    int DistinctAdjacentItems)
{
    internal bool TouchesCorner =>
        (TouchesTop || TouchesBottom) && (TouchesLeft || TouchesRight);

    internal bool Isolated => DistinctAdjacentItems == 0;
}

internal static class PlacementEffectRules
{
    private static readonly (int Dx, int Dy)[] Directions =
    {
        (0, -1),
        (0, 1),
        (-1, 0),
        (1, 0)
    };

    internal static PlacementMetrics Analyze(PlacementLayout layout)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (layout.BayCells.Count == 0)
            throw new ArgumentException("Module bay must contain at least one cell", nameof(layout));
        if (layout.OwnCells.Count == 0)
            throw new ArgumentException("Placed item must occupy at least one cell", nameof(layout));
        if (layout.OwnCells.Any(cell => !layout.BayCells.Contains(cell)))
            throw new ArgumentException("Placed item extends outside the module bay", nameof(layout));

        var empty = new HashSet<GridCell>();
        var adjacent = new HashSet<string>(StringComparer.Ordinal);
        var exterior = FindExterior(layout.BayCells);
        bool top = false;
        bool bottom = false;
        bool left = false;
        bool right = false;

        foreach (var cell in layout.OwnCells)
        {
            foreach (var (dx, dy) in Directions)
            {
                var neighbor = new GridCell(cell.X + dx, cell.Y + dy);
                if (!layout.BayCells.Contains(neighbor))
                {
                    if (exterior.Contains(neighbor))
                    {
                        top |= dy < 0;
                        bottom |= dy > 0;
                        left |= dx < 0;
                        right |= dx > 0;
                    }
                    continue;
                }
                if (layout.OwnCells.Contains(neighbor)) continue;
                if (layout.OtherOccupants.TryGetValue(neighbor, out var ownerId))
                {
                    if (!string.IsNullOrWhiteSpace(ownerId)) adjacent.Add(ownerId);
                    continue;
                }
                empty.Add(neighbor);
            }
        }

        return new(top, bottom, left, right, empty.Count, adjacent.Count);
    }

    private static HashSet<GridCell> FindExterior(IReadOnlyCollection<GridCell> bay)
    {
        int minX = bay.Min(cell => cell.X) - 1;
        int maxX = bay.Max(cell => cell.X) + 1;
        int minY = bay.Min(cell => cell.Y) - 1;
        int maxY = bay.Max(cell => cell.Y) + 1;
        var exterior = new HashSet<GridCell>();
        var queue = new Queue<GridCell>();
        var start = new GridCell(minX, minY);
        exterior.Add(start);
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var (dx, dy) in Directions)
            {
                var neighbor = new GridCell(cell.X + dx, cell.Y + dy);
                if (neighbor.X < minX || neighbor.X > maxX ||
                    neighbor.Y < minY || neighbor.Y > maxY ||
                    bay.Contains(neighbor) || !exterior.Add(neighbor))
                    continue;
                queue.Enqueue(neighbor);
            }
        }
        return exterior;
    }

    internal static int ResolvePercent(string effectCode, PlacementMetrics metrics) =>
        effectCode switch
        {
            "E01" => metrics.TouchesCorner ? ModuleMatrixNumbers.Placement.E01Percent : 0,
            "E03" => checked(metrics.DistinctEmptyOutlineCells * ModuleMatrixNumbers.Placement.E03Percent),
            "E04" => checked(metrics.DistinctAdjacentItems * ModuleMatrixNumbers.Placement.E04Percent),
            "E06" => metrics.Isolated ? ModuleMatrixNumbers.Placement.E06Percent : 0,
            "E07" => metrics.TouchesBottom ? ModuleMatrixNumbers.Placement.E07Percent : 0,
            "E08" => metrics.TouchesRight ? ModuleMatrixNumbers.Placement.E08Percent : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(effectCode), effectCode,
                "Unsupported placement effect")
        };

    // Both effect slots are dispatched separately by the game. The first
    // placement callback applies their combined percentage; the second does
    // nothing, so fractional bonuses are retained until the final division.
    internal static int ResolveCallbackPercent(string effectCode,
        string? firstPlacementCode, string? secondPlacementCode,
        PlacementMetrics metrics)
    {
        if (effectCode != (firstPlacementCode ?? secondPlacementCode)) return 0;
        int percent = ResolvePercent(effectCode, metrics);
        if (firstPlacementCode != null && secondPlacementCode != null)
            percent = checked(percent + ResolvePercent(secondPlacementCode, metrics));
        return percent;
    }

    internal static int PositiveBaseBonus(int baseValue, int percent)
    {
        if (baseValue <= 0 || percent <= 0) return 0;
        long bonus = (long)baseValue * percent / 100;
        return (int)Math.Min(int.MaxValue, bonus);
    }

}
