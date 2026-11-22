namespace MechcoreProtocol.ModuleMatrix.Definitions;

internal sealed record MachineModuleDefinition(string Code, string Id,
    string ChineseName, string EnglishName, string ModuleType,
    string MachineId, int UnitValue, int Width, int Height,
    string ShapeData, string ChineseAbility, string EnglishAbility, bool Retired = false);

internal static class MachineModuleDefinitions
{
    private const string Owner = FixedModuleDefinitions.OwnerId;

    internal static IReadOnlyList<MachineModuleDefinition> All { get; } =
        Array.AsReadOnly(new[]
        {
            new MachineModuleDefinition("S24",
                Owner + ".osmosis_module",
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.osmosis_module", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.osmosis_module", "en"),
                Owner + ".type.purifier",
                "water_purifier",
                ModuleMatrixNumbers.MachineModules.S24.UnitValue,
                ModuleMatrixNumbers.MachineModules.S24.Width,
                ModuleMatrixNumbers.MachineModules.S24.Height,
                "1111",
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_module_filters_an_extra_of_each", "zh", 100m * (1m - ModuleMatrixNumbers.Effects.OsmosisRemainingFraction)),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.each_module_filters_an_extra_of_each", "en", 100m * (1m - ModuleMatrixNumbers.Effects.OsmosisRemainingFraction))),
            new MachineModuleDefinition("S06",
                Owner + ".auto_water_supply_module",
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.automatic_water_supply_module", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.automatic_water_supply_module", "en"),
                Owner + ".type.moisture_farm",
                "moisture_farm",
                ModuleMatrixNumbers.MachineModules.S06.UnitValue,
                ModuleMatrixNumbers.MachineModules.S06.Width,
                ModuleMatrixNumbers.MachineModules.S06.Height,
                "1",
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.automatically_supplies_water_to_containers_in_adjacent", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.automatically_supplies_water_to_containers_in_adjacent", "en")),
            new MachineModuleDefinition("S25",
                Owner + ".market_tracking_module",
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.market_tracking_module", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.market_tracking_module", "en"),
                Owner + ".type.alarm",
                "security_alarm",
                ModuleMatrixNumbers.MachineModules.S25.UnitValue,
                ModuleMatrixNumbers.MachineModules.S25.Width,
                ModuleMatrixNumbers.MachineModules.S25.Height,
                "1111",
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.an_intact_alarm_can_gather_market_information", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.an_intact_alarm_can_gather_market_information", "en")),
            new MachineModuleDefinition("S09",
                Owner + ".decoding_cache_module",
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.decoding_cache_module", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.decoding_cache_module", "en"),
                Owner + ".type.desequencer",
                "desequencer",
                ModuleMatrixNumbers.MachineModules.S09.UnitValue,
                ModuleMatrixNumbers.MachineModules.S09.Width,
                ModuleMatrixNumbers.MachineModules.S09.Height,
                "1111",
                Nicokobo.ModUtilities.ModText.GetFor(MechcorePackage.Id, Owner + ".decoding_cache_module.ability", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor(MechcorePackage.Id, Owner + ".decoding_cache_module.ability", "en")),
            new MachineModuleDefinition("S26",
                Owner + ".batch_smelting_module",
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.batch_smelting_module", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.batch_smelting_module", "en"),
                Owner + ".type.furnace",
                "furnace",
                ModuleMatrixNumbers.MachineModules.S26.UnitValue,
                ModuleMatrixNumbers.MachineModules.S26.Width,
                ModuleMatrixNumbers.MachineModules.S26.Height,
                "1111",
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.retired_no_processing_effect_and_no_longer", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.retired_no_processing_effect_and_no_longer", "en"),
                Retired: true)
        });

    // Keep the stable identifier reserved while the feature is hidden.
    internal static MachineModuleDefinition S11Reserved { get; } =
        new MachineModuleDefinition("S11",
            Owner + ".targeted_projection_module",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.targeted_projection_module", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.targeted_projection_module", "en"),
            Owner + ".type.projector",
            "mirage_projector",
            ModuleMatrixNumbers.MachineModules.S11.UnitValue,
            ModuleMatrixNumbers.MachineModules.S11.Width,
            ModuleMatrixNumbers.MachineModules.S11.Height,
            "1111",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.selects_one_additional_customer_type_to_attract", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.selects_one_additional_customer_type_to_attract", "en"));
}
