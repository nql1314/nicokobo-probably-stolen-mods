using System.Globalization;

namespace MechcoreProtocol.ModuleMatrix.Definitions;

internal sealed record NodeEffectDefinition(string Code, string Id,
    string ChineseName, string EnglishName,
    string ChineseDescription, string EnglishDescription);

internal static class NodeEffectDefinitions
{
    private const string Prefix = FixedModuleDefinitions.OwnerId + ".effect.";
    internal const string NativeSupport = "NODE_EFFECT_SUPPORT";
    internal const string NativeLearning = "MODULE_EFFECT_LEARNING_ALGORITHM";

    private static string Percent(double fraction) =>
        (100 * fraction).ToString("0.###", CultureInfo.InvariantCulture);

    internal static readonly NodeEffectDefinition N09 = new("N09",
        Prefix + "convert_performance",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.performance_conversion", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.performance_conversion", "en"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_eligible_adjacent_module_gives_this_node_3", "zh", Percent(1 / ModuleMatrixNumbers.Effects.ConversionSecondaryDivisor), Percent(1 / ModuleMatrixNumbers.Effects.ConversionQualityDivisor)),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_eligible_adjacent_module_gives_this_node_3", "en", Percent(1 / ModuleMatrixNumbers.Effects.ConversionSecondaryDivisor), Percent(1 / ModuleMatrixNumbers.Effects.ConversionQualityDivisor)));
    internal static readonly NodeEffectDefinition N10 = new("N10",
        Prefix + "convert_efficiency",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.efficiency_conversion", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.efficiency_conversion", "en"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_eligible_adjacent_module_gives_this_node_2", "zh", Percent(1 / ModuleMatrixNumbers.Effects.ConversionSecondaryDivisor), Percent(1 / ModuleMatrixNumbers.Effects.ConversionQualityDivisor)),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_eligible_adjacent_module_gives_this_node_2", "en", Percent(1 / ModuleMatrixNumbers.Effects.ConversionSecondaryDivisor), Percent(1 / ModuleMatrixNumbers.Effects.ConversionQualityDivisor)));
    internal static readonly NodeEffectDefinition N11 = new("N11",
        Prefix + "convert_quality",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.quality_conversion", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.quality_conversion", "en"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_eligible_adjacent_module_gives_this_node", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_eligible_adjacent_module_gives_this_node", "en"));
    internal static readonly NodeEffectDefinition N12 = new("N12",
        Prefix + "isolation",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.isolation", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.isolation", "en"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.weakens_adjacent_modules_negative_stats_by_and", "zh", Percent(1 - 1.0 / ModuleMatrixNumbers.Effects.IsolationDivisor), Percent(1 - (double)ModuleMatrixNumbers.Effects.IsolationPositiveNumerator / ModuleMatrixNumbers.Effects.IsolationDivisor)),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.weakens_adjacent_modules_negative_stats_by_and", "en", Percent(1 - 1.0 / ModuleMatrixNumbers.Effects.IsolationDivisor), Percent(1 - (double)ModuleMatrixNumbers.Effects.IsolationPositiveNumerator / ModuleMatrixNumbers.Effects.IsolationDivisor)));
    internal static readonly NodeEffectDefinition N16 = new("N16",
        Prefix + "learning_acceleration",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.accelerated_learning", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.accelerated_learning", "en"),
        Nicokobo.ModUtilities.ModText.GetFor(MechcorePackage.Id, Prefix + "learning_acceleration.description", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor(MechcorePackage.Id, Prefix + "learning_acceleration.description", "en"));

    internal static IReadOnlyList<NodeEffectDefinition> All { get; } =
        Array.AsReadOnly(new[] { N09, N10, N11, N12, N16 });

    internal static IReadOnlyDictionary<string, NodeEffectDefinition> ByCode { get; } =
        All.ToDictionary(x => x.Code, StringComparer.Ordinal);

    internal static IReadOnlyList<string> IncompatibleWith(string effectId) =>
        All.Where(x => x.Id != effectId).Select(x => x.Id)
            .Append(NativeSupport).ToArray();
}
