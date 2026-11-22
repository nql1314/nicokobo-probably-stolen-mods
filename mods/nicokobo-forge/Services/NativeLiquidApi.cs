
namespace Nicokobo.ContentInterop;

public static partial class NativeLiquidApi
{
    public static bool IsContainer(GameItem item) => item != null && item.Handle() != IntPtr.Zero &&
        item.unitCount == 1 && item.IsTag("LIQUID_CONTAINER_TAG") &&
        (long)WaterHelper.GetFreeCapacity(item) + WaterHelper.GetTotalVolume(item) > 0;

    public static NativeMachineLiquidSnapshot? Capture(GameItem container)
    {
        var raw = CaptureRaw(container);
        return raw == null ? null : NativeLiquidValueRuntime.Read(container, raw);
    }

    internal static NativeMachineLiquidSnapshot? CaptureRaw(GameItem container)
    {
        if (!IsContainer(container) || Liquid.Liquids == null) return null;
        var parts = new List<NativeMachineLiquidPart>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        for (int index = 0; index < Liquid.Liquids.Count; index++)
        {
            var liquid = Liquid.Liquids[index];
            if (liquid == null || string.IsNullOrWhiteSpace(liquid.identifier) ||
                string.IsNullOrWhiteSpace(liquid.currentPart) || !ids.Add(liquid.identifier)) return null;
            int amount = container.GetTagReadonly(liquid.currentPart)?.valueInt ?? 0;
            if (amount < 0 || (total += amount) > int.MaxValue) return null;
            parts.Add(new(liquid.identifier, amount));
        }
        // GetCurrentCapacity returns the fill percentage (e.g. 16),
        // not liquid parts. Free capacity and occupied volume use native parts.
        long capacity = (long)WaterHelper.GetFreeCapacity(container) + total;
        return capacity >= total && capacity <= int.MaxValue && WaterHelper.GetTotalVolume(container) == total
            ? new((int)capacity, Array.AsReadOnly(parts.ToArray())) : null;
    }

    /// <summary>0 unknown/unsafe, 1 base, 2 high quality, 3 pure.</summary>
    public static int WaterQuality(GameItem container)
    {
        var id = WaterFeatureHelper.GetConditionFromPurity(WaterHelper.GetWaterPurity(container))?.identifier;
        if (id == ItemConditionList.CreatePureWater().identifier) return 3;
        if (id == ItemConditionList.CreateHighQualityWater().identifier) return 2;
        return id == ItemConditionList.CreateBaseWater().identifier ? 1 : 0;
    }

    internal static bool Matches(NativeMachineLiquidSnapshot? current, NativeMachineLiquidSnapshot expected) =>
        current != null && current.CapacityParts == expected.CapacityParts &&
        current.Contents.SequenceEqual(expected.Contents);

}
