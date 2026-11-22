namespace Nicokobo.AutomatedLogistics.Domain;

public static class LiquidRules
{
    public static bool CompatibleWater(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b, int gradeA, int gradeB) =>
        gradeA is >= 1 and <= 3 && gradeA == gradeB && a.GetValueOrDefault("water") > 0 && b.GetValueOrDefault("water") > 0 &&
        a.All(p => p.Value >= 0) && b.All(p => p.Value >= 0);
    public static bool SameMixture(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
    {
        long totalA = a.Values.Sum(v => (long)v), totalB = b.Values.Sum(v => (long)v);
        if (a.Values.Any(v => v < 0) || b.Values.Any(v => v < 0) || totalA <= 0 || totalB <= 0) return false;
        return a.Keys.Concat(b.Keys).Distinct().All(id =>
            (decimal)a.GetValueOrDefault(id) * totalB == (decimal)b.GetValueOrDefault(id) * totalA);
    }
}
