namespace MechcoreProtocol.ModuleMatrix.Definitions;

internal sealed record PlacementEffectDefinition(
    string Code,
    string Id,
    string ChineseName,
    string EnglishName,
    string ChineseDescription,
    string EnglishDescription,
    int Percent,
    IReadOnlyList<string> IncompatibleIds);

internal static class PlacementEffectDefinitions
{
    internal const string NativeModuleEffectId = "MODULE_EFFECT_TOP_MOUNT";

    internal static readonly PlacementEffectDefinition E01 = new("E01",
        Id("corner_mount"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.corner_mount", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.corner_mount", "en"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes_3", "zh", ModuleMatrixNumbers.Placement.E01Percent),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes_3", "en", ModuleMatrixNumbers.Placement.E01Percent),
        ModuleMatrixNumbers.Placement.E01Percent,
        Array.Empty<string>());

    internal static readonly PlacementEffectDefinition E03 = new("E03",
        Id("clearance_bonus"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.clearance_bonus", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.clearance_bonus", "en"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes_4", "zh", ModuleMatrixNumbers.Placement.E03Percent),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes_4", "en", ModuleMatrixNumbers.Placement.E03Percent),
        ModuleMatrixNumbers.Placement.E03Percent,
        Array.Empty<string>());

    internal static readonly PlacementEffectDefinition E04 = new("E04",
        Id("adjacency_bonus"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.adjacency_bonus", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.adjacency_bonus", "en"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes_6", "zh", ModuleMatrixNumbers.Placement.E04Percent),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes_6", "en", ModuleMatrixNumbers.Placement.E04Percent),
        ModuleMatrixNumbers.Placement.E04Percent,
        new[] { Id("isolated_installation") });

    internal static readonly PlacementEffectDefinition E06 = new("E06",
        Id("isolated_installation"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.isolated_installation", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.isolated_installation", "en"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes_5", "zh", ModuleMatrixNumbers.Placement.E06Percent),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes_5", "en", ModuleMatrixNumbers.Placement.E06Percent),
        ModuleMatrixNumbers.Placement.E06Percent,
        new[] { Id("adjacency_bonus") });

    internal static readonly PlacementEffectDefinition E07 = new("E07",
        Id("bottom_mount"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.bottom_mount", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.bottom_mount", "en"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes_2", "zh", ModuleMatrixNumbers.Placement.E07Percent),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes_2", "en", ModuleMatrixNumbers.Placement.E07Percent),
        ModuleMatrixNumbers.Placement.E07Percent,
        Array.Empty<string>());

    internal static readonly PlacementEffectDefinition E08 = new("E08",
        Id("right_mount"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.right_mount", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.right_mount", "en"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes", "zh", ModuleMatrixNumbers.Placement.E08Percent),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.increases_this_module_s_positive_base_attributes", "en", ModuleMatrixNumbers.Placement.E08Percent),
        ModuleMatrixNumbers.Placement.E08Percent,
        Array.Empty<string>());

    internal static IReadOnlyList<PlacementEffectDefinition> FirstSlice { get; } =
        Array.AsReadOnly(new[] { E01, E03, E04, E06 });

    internal static IReadOnlyList<PlacementEffectDefinition> SecondSlice { get; } =
        Array.AsReadOnly(new[] { E07, E08 });

    internal static IReadOnlyList<PlacementEffectDefinition> All { get; } =
        Array.AsReadOnly(new[] { E01, E03, E04, E06, E07, E08 });

    private static string Id(string suffix) =>
        $"{FixedModuleDefinitions.OwnerId}.effect.{suffix}";
}
