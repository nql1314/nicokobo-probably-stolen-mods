using System.Numerics;

namespace MechcoreProtocol.SynthesisExpansion.Domain;

public enum ComponentGrade { Ordinary, Fine, Excellent, Perfect }

/// <summary>Component grade is independent of native metal purity. Values are
/// derived from the production basis or factory default, never from an earlier bonus.</summary>
public static class ComponentQualityRules
{
    public const int SchemaVersion = 1;
    public const string GradeTag = "nicokobo.mechcore.synthesis.component_grade";
    public const string SchemaTag = "nicokobo.mechcore.synthesis.component_grade_schema";
    public const int QualityPerGrade = SynthesisNumbers.Quality.PointsPerComponentGrade;

    public static ComponentGrade FromQuality(int quality) =>
        (ComponentGrade)Math.Clamp(quality / QualityPerGrade, 0, (int)ComponentGrade.Perfect);

    public static bool IsValid(int grade) => grade is >= 0 and <= (int)ComponentGrade.Perfect;

    public static int ValueBonusPercent(ComponentGrade grade) => grade switch
    {
        ComponentGrade.Ordinary => SynthesisNumbers.Quality.OrdinaryValueBonusPercent,
        ComponentGrade.Fine => SynthesisNumbers.Quality.FineValueBonusPercent,
        ComponentGrade.Excellent => SynthesisNumbers.Quality.ExcellentValueBonusPercent,
        ComponentGrade.Perfect => SynthesisNumbers.Quality.PerfectValueBonusPercent,
        _ => throw new ArgumentOutOfRangeException(nameof(grade))
    };

    public static long Value(long baseValue, ComponentGrade grade)
    {
        if (baseValue < 0) throw new ArgumentOutOfRangeException(nameof(baseValue));
        BigInteger value = BigInteger.DivRem(new BigInteger(baseValue) * (100 + ValueBonusPercent(grade)),
            100, out var remainder);
        if (remainder > 50 || remainder == 50 && !value.IsEven) value++;
        if (value > long.MaxValue) throw new OverflowException("Component value is too large");
        return (long)value;
    }

    public static string Name(ComponentGrade grade, bool? english = null)
    {
        if (grade is < ComponentGrade.Ordinary or > ComponentGrade.Perfect)
            throw new ArgumentOutOfRangeException(nameof(grade));
        return SynthesisText.GetFor("synthesis.component_grade." + (int)grade, english);
    }

    public static string Description(ComponentGrade grade, bool english) => english
        ? $"Grade: {Name(grade, true)} (value +{ValueBonusPercent(grade)}%)"
        : $"品级：{Name(grade, false)}（价值 +{ValueBonusPercent(grade)}%）";
}
