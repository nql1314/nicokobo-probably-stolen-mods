namespace Nicokobo.ContentInterop;

public sealed record NativeMachineLiquidPart(string LiquidId, int Parts)
{ public decimal? Value { get; init; } public decimal? QualityBasis { get; init; } }
public sealed record NativeMachineLiquidSnapshot(int CapacityParts, IReadOnlyList<NativeMachineLiquidPart> Contents)
{ public int TotalParts => checked((int)Contents.Sum(part => (long)part.Parts)); }

internal static class NativeProductionValueMath
{
    internal static decimal NativeLiquidValue(int parts) => parts >= 0 ? parts / 25000m : throw new ArgumentOutOfRangeException(nameof(parts));
    internal static decimal RemainingLiquidValue(decimal value, int before, int after) =>
        value >= 0 && before > 0 && after >= 0 && after <= before ? value * after / before : throw new ArgumentOutOfRangeException(nameof(after));
}
