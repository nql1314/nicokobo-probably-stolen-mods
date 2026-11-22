namespace MechcoreProtocol.ModuleMatrix.Definitions;

internal enum ModuleTier { Basic, Rare, Functional }
internal sealed record ModuleTierDefinition(string Id, ModuleTier Tier, int? UnitValue, bool IsMatrix)
{
    // For native items this is the historical basis of Matrix's retired price shift,
    // not the current game's selling price.
    internal int? PreviousUnitValue { get; init; }
}

// Explicit item IDs keep damaged modules, cybernetics and machine-specific
// modules out of upgrades. Native MODULE_TAG also covers ordinary nodes.
internal static class ModuleTierDefinitions
{
    internal const string UpgradeRecipeId = FixedModuleDefinitions.OwnerId + ".recipe.rare_module_upgrade";
    internal const string UpgradeMachineId = "nicokobo.mechcore.synthesis.synthesizer";
    internal const int UpgradeInputCount = 2;
    internal static IReadOnlyList<ModuleTierDefinition> All { get; } = Array.AsReadOnly(
        new[]
        {
            new ModuleTierDefinition("system_module_performance", ModuleTier.Basic, null, false) { PreviousUnitValue = 70 },
            new ModuleTierDefinition("system_module_efficiancy", ModuleTier.Basic, null, false) { PreviousUnitValue = 70 },
            new ModuleTierDefinition("system_module_quality", ModuleTier.Basic, null, false) { PreviousUnitValue = 70 },
            new ModuleTierDefinition("node_small", ModuleTier.Basic, null, false) { PreviousUnitValue = 50 },
            new ModuleTierDefinition("node_medium", ModuleTier.Basic, null, false) { PreviousUnitValue = 50 },
            new ModuleTierDefinition("system_module_overclock", ModuleTier.Rare, null, false) { PreviousUnitValue = 100 },
            new ModuleTierDefinition("system_module_eco", ModuleTier.Rare, null, false) { PreviousUnitValue = 100 },
            new ModuleTierDefinition("system_module_fineness", ModuleTier.Rare, null, false) { PreviousUnitValue = 100 },
            new ModuleTierDefinition("system_module_streamlining", ModuleTier.Rare, null, false) { PreviousUnitValue = 125 },
            new ModuleTierDefinition("alarm_module_transmitter", ModuleTier.Functional, null, false),
            new ModuleTierDefinition("furnace_module_junk", ModuleTier.Functional, null, false),
            new ModuleTierDefinition("furnace_module_blast", ModuleTier.Functional, null, false)
        }
        .Concat(FixedModuleDefinitions.All.Select(item => new ModuleTierDefinition(item.Id,
            item.Code == "M05" ? ModuleTier.Rare : ModuleTier.Basic, item.UnitValue, true)
            { PreviousUnitValue = item.Code switch { "M01" or "M04" => 100, "M02" => 110,
                "M03" => 130, "M06" => 90, _ => item.UnitValue } }))
        .Concat(ContinuousModuleDefinitions.All.Select(item =>
            new ModuleTierDefinition(item.Module.Id, ModuleTier.Rare, item.Module.UnitValue, true)))
        .Concat(NodeItemDefinitions.All.Select(item =>
            new ModuleTierDefinition(item.Id, ModuleTier.Rare, item.UnitValue, true)
            { PreviousUnitValue = item.Code switch { "N09" or "N10" => 90, "N11" => 110,
                "N12" => 100, _ => item.UnitValue } }))
        .Concat(MachineModuleDefinitions.All.Select(item =>
            new ModuleTierDefinition(item.Id, ModuleTier.Functional, null, true))).ToArray());

    private static readonly IReadOnlyDictionary<string, ModuleTierDefinition> ById =
        All.ToDictionary(item => item.Id, StringComparer.Ordinal);

    internal static ModuleTierDefinition? Find(string? id) =>
        id != null && ById.TryGetValue(id, out var item) ? item : null;

    internal static IReadOnlyList<string> RarePool(Func<ModuleTierDefinition, bool> available) =>
        All.Where(item => item.Tier == ModuleTier.Rare && available(item))
            .Select(item => item.Id).ToArray();

    internal static string PickRare(IReadOnlyList<string> pool, int index)
    {
        if (pool.Count == 0) throw new InvalidOperationException("No rare modules available");
        if ((uint)index >= (uint)pool.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return pool[index];
    }

    internal static string Label(ModuleTier tier) => Nicokobo.ModUtilities.ModText.Get(MechcorePackage.Id, tier switch
    {
        ModuleTier.Basic => "matrix.tier.basic",
        ModuleTier.Rare => "matrix.tier.rare",
        ModuleTier.Functional => "matrix.tier.functional",
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    });
}
