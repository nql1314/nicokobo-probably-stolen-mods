using System.Numerics;
using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion.Domain;

public static class IngotPurityRules
{
    // Strip the zero-Quality output factor from manufacturing value. The
    // native purity feature then applies the final factor to this integer base.
    public static long BaseValue(long processedValue, string baselineTag)
    {
        if (processedValue < 0) throw new ArgumentOutOfRangeException(nameof(processedValue));
        var baseline = FromNativeTag(baselineTag);
        if (baseline == IngotPurityGrade.Unknown)
            throw new InvalidOperationException("Production purity baseline unavailable");
        return RoundedValue(new BigInteger(processedValue) * 100, ValueFactorPercent(baseline));
    }

    public static long QualityValue(long processedValue, string baselineTag, string finalTag)
    {
        if (processedValue < 0) throw new ArgumentOutOfRangeException(nameof(processedValue));
        var baseline = FromNativeTag(baselineTag);
        var final = FromNativeTag(finalTag);
        if (baseline == IngotPurityGrade.Unknown || final == IngotPurityGrade.Unknown || final < baseline)
            throw new InvalidOperationException("Invalid production purity progression");
        return RoundedValue(new BigInteger(BaseValue(processedValue, baselineTag)) * ValueFactorPercent(final), 100);
    }

    private static long RoundedValue(BigInteger numerator, int denominator)
    {
        BigInteger value = BigInteger.DivRem(numerator, denominator, out var remainder);
        int comparison = (remainder * 2).CompareTo(denominator);
        if (comparison > 0 || comparison == 0 && !value.IsEven) value++;
        return value <= long.MaxValue ? (long)value
            : throw new OverflowException("Purity production value is too large");
    }

    public static int ValueFactorPercent(IngotPurityGrade grade) => grade switch
    {
        IngotPurityGrade.VeryLow => 75,
        IngotPurityGrade.Low => 85,
        IngotPurityGrade.Fair => 100,
        IngotPurityGrade.High => 125,
        IngotPurityGrade.VeryHigh => 150,
        // Formal 0.46D Build 26274380 uses native ValueMod +200%.
        IngotPurityGrade.Perfect => 300,
        _ => throw new ArgumentOutOfRangeException(nameof(grade))
    };

    public static string MachinedTag(IEnumerable<string?> tags, int quality)
    {
        var highest = IngotPurityGrade.Unknown;
        foreach (string? tag in tags)
        {
            var grade = FromNativeTag(tag);
            if (grade == IngotPurityGrade.Unknown)
                throw new InvalidOperationException("Input purity unavailable");
            if (grade > highest) highest = grade;
        }
        if (highest == IngotPurityGrade.Unknown)
            throw new InvalidOperationException("Purity input missing");
        int baseline = Math.Max((int)IngotPurityGrade.Fair, (int)highest - 1);
        int output = Math.Min((int)IngotPurityGrade.Perfect,
            baseline + RefinerPurityRules.QualityLevels(quality));
        return ToNativeTag((IngotPurityGrade)output);
    }

    public static string MachinedGradeName(string tag, bool? english = null) =>
        ComponentQualityRules.Name(FromNativeTag(tag) switch
        {
            IngotPurityGrade.VeryLow or IngotPurityGrade.Low or IngotPurityGrade.Fair => ComponentGrade.Ordinary,
            IngotPurityGrade.High => ComponentGrade.Fine,
            IngotPurityGrade.VeryHigh => ComponentGrade.Excellent,
            IngotPurityGrade.Perfect => ComponentGrade.Perfect,
            _ => throw new InvalidOperationException("Component purity unavailable")
        }, english);

    public static string ToNativeTag(IngotPurityGrade grade) => grade switch
    {
        IngotPurityGrade.VeryLow => "INGOT_PURITY_VERY_LOW",
        IngotPurityGrade.Low => "INGOT_PURITY_LOW",
        IngotPurityGrade.Fair => "INGOT_PURITY_FAIR",
        IngotPurityGrade.High => "INGOT_PURITY_HIGH",
        IngotPurityGrade.VeryHigh => "INGOT_PURITY_VERY_HIGH",
        IngotPurityGrade.Perfect => "INGOT_PURITY_PERFECT",
        _ => throw new ArgumentOutOfRangeException(nameof(grade))
    };

    public static IngotPurityGrade FromNativeTag(string? value) => value switch
    {
        "INGOT_PURITY_VERY_LOW" => IngotPurityGrade.VeryLow,
        "INGOT_PURITY_LOW" => IngotPurityGrade.Low,
        "INGOT_PURITY_FAIR" => IngotPurityGrade.Fair,
        "INGOT_PURITY_HIGH" => IngotPurityGrade.High,
        "INGOT_PURITY_VERY_HIGH" => IngotPurityGrade.VeryHigh,
        "INGOT_PURITY_PERFECT" => IngotPurityGrade.Perfect,
        _ => IngotPurityGrade.Unknown
    };
}
