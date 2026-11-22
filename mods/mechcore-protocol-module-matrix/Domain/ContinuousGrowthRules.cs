namespace MechcoreProtocol.ModuleMatrix.Domain;

internal static class ContinuousGrowthRules
{
    internal static int Advance(int growth, int step, int maximumGrowth,
        int multiplier = 1)
    {
        if (step <= 0 || maximumGrowth < 0 || multiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(step));
        return (int)Math.Min(maximumGrowth,
            Math.Max(0L, growth) + (long)step * multiplier);
    }

    internal static int Decay(int growth, int step, int elapsedDays)
    {
        if (step <= 0 || elapsedDays < 0)
            throw new ArgumentOutOfRangeException(nameof(step));
        return (int)Math.Max(0L,
            (long)growth - (long)step * elapsedDays);
    }

}
