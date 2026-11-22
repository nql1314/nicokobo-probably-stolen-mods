namespace MechcoreProtocol.ModuleMatrix.Domain;

internal static class OsmosisRules
{
    internal static int ExtraRemoval(int remainingAfterNative, int stacks)
    {
        if (remainingAfterNative <= 0 || stacks <= 0) return 0;
        decimal remainingFraction = 1m;
        for (int i = 0; i < stacks; i++)
            remainingFraction *= ModuleMatrixNumbers.Effects.OsmosisRemainingFraction;
        int remaining = decimal.ToInt32(decimal.Floor(
            remainingAfterNative * remainingFraction));
        return remainingAfterNative - remaining;
    }
}
