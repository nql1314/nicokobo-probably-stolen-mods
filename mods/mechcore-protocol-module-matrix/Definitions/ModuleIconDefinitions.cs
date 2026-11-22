namespace MechcoreProtocol.ModuleMatrix.Definitions;

internal sealed record ModuleIconDefinition(string ItemId, string SpriteKey,
    string ResourceName, int PixelWidth, int PixelHeight);

internal static class ModuleIconDefinitions
{
    internal const string AtlasKey =
        "Nicokobo/MechcoreProtocol/ModuleMatrix";
    private const string ResourcePrefix =
        "MechcoreProtocol.ModuleMatrix.Assets.Icons.";
    private const string SpritePrefix = "nicokobo_mechcore_";

    internal static IReadOnlyList<ModuleIconDefinition> All { get; } =
        Array.AsReadOnly(new[]
        {
            Fixed(FixedModuleDefinitions.M01, "m01_balanced"),
            Fixed(FixedModuleDefinitions.M02, "m02_assembly_line"),
            Fixed(FixedModuleDefinitions.M03, "m03_precision"),
            Fixed(FixedModuleDefinitions.M04, "m04_steady_state"),
            Fixed(FixedModuleDefinitions.M05, "m05_high_power"),
            Fixed(FixedModuleDefinitions.M06, "m06_inspection"),
            Continuous(ContinuousModuleDefinitions.M08,
                "continuous_acceleration_module", "m08_continuous_acceleration"),
            Continuous(ContinuousModuleDefinitions.M10,
                "continuous_efficiency_module", "m10_continuous_efficiency"),
            Continuous(ContinuousModuleDefinitions.M11,
                "continuous_calibration_module", "m11_continuous_calibration"),
            Node(NodeItemDefinitions.N09, "n09_performance_conversion"),
            Node(NodeItemDefinitions.N10, "n10_efficiency_conversion"),
            Node(NodeItemDefinitions.N11, "n11_quality_conversion"),
            Node(NodeItemDefinitions.N12, "n12_isolation"),
            Node(NodeItemDefinitions.N16, "n16_learning_acceleration"),
            Machine(MachineModuleDefinitions.All[0], "s24_osmosis"),
            Machine(MachineModuleDefinitions.All[1], "s06_auto_water_supply"),
            Machine(MachineModuleDefinitions.All[2], "s25_market_tracking"),
            Machine(MachineModuleDefinitions.All[3], "s09_decoding_cache"),
            Machine(MachineModuleDefinitions.All[4], "s26_batch_smelting")
        });

    internal static IReadOnlyDictionary<string, ModuleIconDefinition> ByItemId
        { get; } = All.ToDictionary(icon => icon.ItemId, StringComparer.Ordinal);

    private static ModuleIconDefinition Fixed(FixedModuleDefinition definition,
        string name) => Icon(definition.Id, name, definition.Width,
            definition.Height);

    private static ModuleIconDefinition Continuous(
        ContinuousModuleDefinition definition, string spriteKey, string fileName) =>
        new(definition.Module.Id, spriteKey,
            ResourcePrefix + fileName + ".png",
            definition.Module.Width * 16, definition.Module.Height * 16);

    private static ModuleIconDefinition Node(NodeItemDefinition definition,
        string name) => Icon(definition.Id, name, definition.Width,
            definition.Height);

    private static ModuleIconDefinition Machine(MachineModuleDefinition definition,
        string name) => Icon(definition.Id, name, definition.Width,
            definition.Height);

    private static ModuleIconDefinition Icon(string itemId, string name,
        int width, int height) => new(itemId, SpritePrefix + name,
            ResourcePrefix + name + ".png", width * 16, height * 16);
}
