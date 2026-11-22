namespace MechcoreProtocol.ModuleMatrix.Definitions;

internal sealed record FixedModuleDefinition(
    string Code,
    string Id,
    string ChineseName,
    string EnglishName,
    int UnitValue,
    int Performance,
    int Efficiency,
    int Quality,
    int Width,
    int Height,
    string ShapeData)
{
    internal int OccupiedCells => ShapeData.Count(cell => cell == '1');
}

internal static class FixedModuleDefinitions
{
    internal const string OwnerId = "nicokobo.mechcore_protocol.module_matrix";
    internal const string TemplateId = "blank_module";
    internal const string UniversalModuleType = "MODULE_TYPE_UNIVERSAL";

    internal static readonly FixedModuleDefinition M01 = new("M01",
        OwnerId + ".balanced_module",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.balanced_module", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.balanced_module", "en"),
        ModuleMatrixNumbers.FixedModules.M01.UnitValue,
        ModuleMatrixNumbers.FixedModules.M01.Performance,
        ModuleMatrixNumbers.FixedModules.M01.Efficiency,
        ModuleMatrixNumbers.FixedModules.M01.Quality,
        ModuleMatrixNumbers.FixedModules.M01.Width,
        ModuleMatrixNumbers.FixedModules.M01.Height,
        "1111");

    internal static readonly FixedModuleDefinition M02 = new("M02",
        OwnerId + ".assembly_line_module",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.assembly_line_module", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.assembly_line_module", "en"),
        ModuleMatrixNumbers.FixedModules.M02.UnitValue,
        ModuleMatrixNumbers.FixedModules.M02.Performance,
        ModuleMatrixNumbers.FixedModules.M02.Efficiency,
        ModuleMatrixNumbers.FixedModules.M02.Quality,
        ModuleMatrixNumbers.FixedModules.M02.Width,
        ModuleMatrixNumbers.FixedModules.M02.Height,
        "1111");

    internal static readonly FixedModuleDefinition M03 = new("M03",
        OwnerId + ".precision_module",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.precision_module", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.precision_module", "en"),
        ModuleMatrixNumbers.FixedModules.M03.UnitValue,
        ModuleMatrixNumbers.FixedModules.M03.Performance,
        ModuleMatrixNumbers.FixedModules.M03.Efficiency,
        ModuleMatrixNumbers.FixedModules.M03.Quality,
        ModuleMatrixNumbers.FixedModules.M03.Width,
        ModuleMatrixNumbers.FixedModules.M03.Height,
        "100111");

    internal static readonly FixedModuleDefinition M04 = new("M04",
        OwnerId + ".steady_state_module",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.steady_state_module", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.steady_state_module", "en"),
        ModuleMatrixNumbers.FixedModules.M04.UnitValue,
        ModuleMatrixNumbers.FixedModules.M04.Performance,
        ModuleMatrixNumbers.FixedModules.M04.Efficiency,
        ModuleMatrixNumbers.FixedModules.M04.Quality,
        ModuleMatrixNumbers.FixedModules.M04.Width,
        ModuleMatrixNumbers.FixedModules.M04.Height,
        "111010");

    internal static readonly FixedModuleDefinition M05 = new("M05",
        OwnerId + ".high_power_module",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.high_power_module", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.high_power_module", "en"),
        ModuleMatrixNumbers.FixedModules.M05.UnitValue,
        ModuleMatrixNumbers.FixedModules.M05.Performance,
        ModuleMatrixNumbers.FixedModules.M05.Efficiency,
        ModuleMatrixNumbers.FixedModules.M05.Quality,
        ModuleMatrixNumbers.FixedModules.M05.Width,
        ModuleMatrixNumbers.FixedModules.M05.Height,
        "111111");

    internal static readonly FixedModuleDefinition M06 = new("M06",
        OwnerId + ".inspection_module",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.inspection_module", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.inspection_module", "en"),
        ModuleMatrixNumbers.FixedModules.M06.UnitValue,
        ModuleMatrixNumbers.FixedModules.M06.Performance,
        ModuleMatrixNumbers.FixedModules.M06.Efficiency,
        ModuleMatrixNumbers.FixedModules.M06.Quality,
        ModuleMatrixNumbers.FixedModules.M06.Width,
        ModuleMatrixNumbers.FixedModules.M06.Height,
        "11");

    internal static IReadOnlyList<FixedModuleDefinition> All { get; } =
        Array.AsReadOnly(new[] { M01, M02, M03, M04, M05, M06 });

    internal static IReadOnlyList<FixedModuleDefinition> Scavenging { get; } =
        Array.AsReadOnly(new[] { M01, M02, M03, M04, M06 });
}
