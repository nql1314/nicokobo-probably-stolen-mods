namespace ProbablyStolen.ModSupport;

internal static class NativeContrabandStatus
{
    internal static void Apply(GameItem item, int level)
    {
        if (level is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(level));
        if (level == 0) return;
        if (item == null || NativeObject.Handle(item) == IntPtr.Zero)
            throw new InvalidOperationException("Contraband item is unavailable");

        // Use the native initializer so inspection, tooltips and pricing all
        // read the same type, enabled tag and level. Production basis is separate.
        ContrabandHelper.InitContrabandItem(item, level);
        if (!item.IsGameItemType("CONTRABAND") || !item.IsTag("CONTRABAND_ITEM_TAG") ||
            ContrabandHelper.GetContrabandLevel(item) != level)
            throw new InvalidOperationException($"Contraband initialization failed: {item.identifier}, level={level}");
    }
}
