using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion.Domain;

/// <summary>
/// Purity the material refiner writes to silicon and titanium alloy. These
/// products gain grades from machine Quality, without flux.
/// </summary>
public static class RefinerPurityRules
{
    /// <summary>Silicon inherits no purity from quartz, so it starts at the
    /// ore-grade baseline and climbs from there.</summary>
    public const string SiliconBaselineTag = "INGOT_PURITY_FAIR";

    /// <summary>Grades gained from Quality, one per full 100 points.</summary>
    public static int QualityLevels(int qualityPoints) =>
        Math.Clamp(Math.Max(0, qualityPoints) / SynthesisNumbers.Quality.PointsPerPurityGrade,
            0, (int)IngotPurityGrade.Perfect);

    /// <summary>Grades the product may gain above its baseline. Capped at the
    /// native top grade, so the purity call never reads out of range.</summary>
    public static int PurityLevels(string baselineTag, int qualityPoints)
    {
        int baseline = (int)IngotPurityRules.FromNativeTag(baselineTag);
        if (baseline == (int)IngotPurityGrade.Unknown) return 0;
        int ceiling = (int)IngotPurityGrade.Perfect - baseline;
        return Math.Min(QualityLevels(qualityPoints), Math.Max(0, ceiling));
    }
}
