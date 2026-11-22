namespace MechcoreProtocol.ModuleMatrix.Definitions;

internal sealed record ContinuousModuleDefinition(
    FixedModuleDefinition Module, string Attribute, int Step, int Cap,
    string ChineseDescription, string EnglishDescription)
{
    internal int Initial => Attribute switch
    {
        "PERFORMANCE" => Module.Performance,
        "EFFICIENCY" => Module.Efficiency,
        _ => Module.Quality
    };
    internal int MaximumGrowth => Cap - Initial;
}

internal static class ContinuousModuleDefinitions
{
    private const string Owner = FixedModuleDefinitions.OwnerId;

    internal static readonly ContinuousModuleDefinition M08 = new(new("M08",
            Owner + ".continuous_acceleration_module",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.continuous_acceleration_module", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.continuous_acceleration_module", "en"),
            ModuleMatrixNumbers.ContinuousModules.M08.UnitValue,
            ModuleMatrixNumbers.ContinuousModules.M08.Performance,
            ModuleMatrixNumbers.ContinuousModules.M08.Efficiency,
            ModuleMatrixNumbers.ContinuousModules.M08.Quality,
            ModuleMatrixNumbers.ContinuousModules.M08.Width,
            ModuleMatrixNumbers.ContinuousModules.M08.Height,
            "1111"),
        "PERFORMANCE",
        ModuleMatrixNumbers.ContinuousModules.M08.Step,
        ModuleMatrixNumbers.ContinuousModules.M08.Cap,
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_completed_operation_adds_percentage_points_of_3", "zh", ModuleMatrixNumbers.ContinuousModules.M08.Step, ModuleMatrixNumbers.ContinuousModules.M08.Cap),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_completed_operation_adds_percentage_points_of_3", "en", ModuleMatrixNumbers.ContinuousModules.M08.Step, ModuleMatrixNumbers.ContinuousModules.M08.Cap));

    internal static readonly ContinuousModuleDefinition M10 = new(new("M10",
            Owner + ".continuous_efficiency_module",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.continuous_efficiency_module", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.continuous_efficiency_module", "en"),
            ModuleMatrixNumbers.ContinuousModules.M10.UnitValue,
            ModuleMatrixNumbers.ContinuousModules.M10.Performance,
            ModuleMatrixNumbers.ContinuousModules.M10.Efficiency,
            ModuleMatrixNumbers.ContinuousModules.M10.Quality,
            ModuleMatrixNumbers.ContinuousModules.M10.Width,
            ModuleMatrixNumbers.ContinuousModules.M10.Height,
            "1111"),
        "EFFICIENCY",
        ModuleMatrixNumbers.ContinuousModules.M10.Step,
        ModuleMatrixNumbers.ContinuousModules.M10.Cap,
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_completed_operation_adds_percentage_points_of_2", "zh", ModuleMatrixNumbers.ContinuousModules.M10.Step, ModuleMatrixNumbers.ContinuousModules.M10.Cap),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_completed_operation_adds_percentage_points_of_2", "en", ModuleMatrixNumbers.ContinuousModules.M10.Step, ModuleMatrixNumbers.ContinuousModules.M10.Cap));

    internal static readonly ContinuousModuleDefinition M11 = new(new("M11",
            Owner + ".continuous_calibration_module",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.continuous_calibration_module", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.continuous_calibration_module", "en"),
            ModuleMatrixNumbers.ContinuousModules.M11.UnitValue,
            ModuleMatrixNumbers.ContinuousModules.M11.Performance,
            ModuleMatrixNumbers.ContinuousModules.M11.Efficiency,
            ModuleMatrixNumbers.ContinuousModules.M11.Quality,
            ModuleMatrixNumbers.ContinuousModules.M11.Width,
            ModuleMatrixNumbers.ContinuousModules.M11.Height,
            "100111"),
        "QUALITY",
        ModuleMatrixNumbers.ContinuousModules.M11.Step,
        ModuleMatrixNumbers.ContinuousModules.M11.Cap,
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_completed_operation_adds_percentage_points_of", "zh", ModuleMatrixNumbers.ContinuousModules.M11.Step, ModuleMatrixNumbers.ContinuousModules.M11.Cap),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_completed_operation_adds_percentage_points_of", "en", ModuleMatrixNumbers.ContinuousModules.M11.Step, ModuleMatrixNumbers.ContinuousModules.M11.Cap));

    internal static IReadOnlyList<ContinuousModuleDefinition> All { get; } =
        Array.AsReadOnly(new[] { M08, M10, M11 });

    internal static IReadOnlyDictionary<string, ContinuousModuleDefinition> ById
        { get; } = All.ToDictionary(x => x.Module.Id, StringComparer.Ordinal);
}
