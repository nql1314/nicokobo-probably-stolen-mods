using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion.Domain;

public static class RefinerMetalRules
{
    public const string BlastModuleId = "furnace_module_blast";

    public static bool IsSmelting(OutputRule rule) =>
        rule is OutputRule.MetalSmelting or OutputRule.BlastMetalSmelting;

    public static bool MatchesMode(OutputRule rule, bool blastInstalled) =>
        rule == (blastInstalled ? OutputRule.BlastMetalSmelting : OutputRule.MetalSmelting);

    // Spread scrap across the available batches so a scrap-heavy first batch
    // does not produce a lower grade when the same mix can serve every batch.
    public static int BalancedOrePieces(long orePieces, long scrapPieces, int batchSize)
    {
        if (orePieces < 0) throw new ArgumentOutOfRangeException(nameof(orePieces));
        if (scrapPieces < 0) throw new ArgumentOutOfRangeException(nameof(scrapPieces));
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        long batches = checked(orePieces + scrapPieces) / batchSize;
        if (batches == 0 || orePieces == 0) return 0;
        return (int)Math.Min(orePieces, Math.Max(1L, batchSize - scrapPieces / batches));
    }

    // The native furnace sorts scrap before ore and reads its last two pieces.
    // Count selected units, including stacked inputs, independently of grid order.
    public static string BaselineTag(int orePieces) => orePieces switch
    {
        <= 0 => "INGOT_PURITY_LOW",
        1 => "INGOT_PURITY_FAIR",
        _ => "INGOT_PURITY_HIGH"
    };

    public static int PurityLevels(string baseline, int quality, bool blast)
    {
        var grade = IngotPurityRules.FromNativeTag(baseline);
        if (grade == IngotPurityGrade.Unknown) return 0;
        int qualityLevels = Math.Max(0, RefinerPurityRules.QualityLevels(quality) - (blast ? 1 : 0));
        return Math.Min(SynthesisNumbers.Quality.MetalSmeltingBasePurityLevels + qualityLevels,
            (int)IngotPurityGrade.Perfect - (int)grade);
    }
}
