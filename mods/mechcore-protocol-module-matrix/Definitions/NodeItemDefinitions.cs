namespace MechcoreProtocol.ModuleMatrix.Definitions;

internal sealed record NodeItemDefinition(string Code, string Id,
    string ChineseName, string EnglishName, int UnitValue,
    int Width, int Height, string ShapeData);

internal static class NodeItemDefinitions
{
    private const string Prefix = FixedModuleDefinitions.OwnerId + ".";

    internal static readonly NodeItemDefinition N09 = new("N09",
        Prefix + "performance_conversion_node",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.performance_conversion_node", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.performance_conversion_node", "en"),
        ModuleMatrixNumbers.Nodes.N09.UnitValue,
        ModuleMatrixNumbers.Nodes.N09.Width,
        ModuleMatrixNumbers.Nodes.N09.Height,
        "1");
    internal static readonly NodeItemDefinition N10 = new("N10",
        Prefix + "efficiency_conversion_node",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.efficiency_conversion_node", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.efficiency_conversion_node", "en"),
        ModuleMatrixNumbers.Nodes.N10.UnitValue,
        ModuleMatrixNumbers.Nodes.N10.Width,
        ModuleMatrixNumbers.Nodes.N10.Height,
        "1");
    internal static readonly NodeItemDefinition N11 = new("N11",
        Prefix + "quality_conversion_node",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.quality_conversion_node", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.quality_conversion_node", "en"),
        ModuleMatrixNumbers.Nodes.N11.UnitValue,
        ModuleMatrixNumbers.Nodes.N11.Width,
        ModuleMatrixNumbers.Nodes.N11.Height,
        "1");
    internal static readonly NodeItemDefinition N12 = new("N12",
        Prefix + "isolation_node",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.isolation_node", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.isolation_node", "en"),
        ModuleMatrixNumbers.Nodes.N12.UnitValue,
        ModuleMatrixNumbers.Nodes.N12.Width,
        ModuleMatrixNumbers.Nodes.N12.Height,
        "11");
    internal static readonly NodeItemDefinition N16 = new("N16",
        Prefix + "learning_acceleration_node",
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.accelerated_learning_node", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.accelerated_learning_node", "en"),
        ModuleMatrixNumbers.Nodes.N16.UnitValue,
        ModuleMatrixNumbers.Nodes.N16.Width,
        ModuleMatrixNumbers.Nodes.N16.Height,
        "11");

    internal static IReadOnlyList<NodeItemDefinition> All { get; } =
        Array.AsReadOnly(new[] { N09, N10, N11, N12, N16 });
}
