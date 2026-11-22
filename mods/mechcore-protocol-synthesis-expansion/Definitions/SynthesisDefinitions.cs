using Newtonsoft.Json;
using Nicokobo.ModUtilities;

namespace MechcoreProtocol.SynthesisExpansion.Definitions;

public enum ItemKind { Material, Cybernetic, Machine, Guide, Household, Drink, Storage, Food, Robot, Luxury, Tool }
public enum TriggerMode { Nightly = 1 }
public enum QualityMode { None, Purity, ComponentGrade }
public enum OutputRule { Fixed, MeatValue, MetalSmelting, BlastMetalSmelting }
public enum RecipeResourceKind { Item, Liquid, TaggedItem }
public enum RecipeAmountBasis { PerBatch }
public enum RecipeProperty { MetalPurity, GenuineInjector, UsableGlowberry, ScavScannerLevel, EmptyStorage }
public enum IngotPurityGrade
{
    Unknown = 0, VeryLow = 5, Low = 6, Fair = 7,
    High = 8, VeryHigh = 9, Perfect = 10
}

public sealed record ItemDefinition(
    string Code, string Id, ItemKind Kind, int Width, int Height, int BaseValue,
    string Source, string NativeTemplateId, int ContrabandLevel = 0)
{
    private string TextOwner => Id.StartsWith("nicokobo.forge.", StringComparison.Ordinal) ? "nicokobo.forge" : MechcorePackage.Id;
    public string ChineseName => ModText.GetFor(TextOwner, Id + ".name", "zh");
    public string EnglishName => ModText.GetFor(TextOwner, Id + ".name", "en");
}

public sealed record RecipeCondition(RecipeProperty Property, int MinimumValue);

/// <summary>One item rule, with piece counts resolved per batch.</summary>
public sealed record RecipeRequirement(
    RecipeResourceKind Kind, string ResourceId, int Amount,
    RecipeAmountBasis Basis, IReadOnlyList<RecipeCondition> Conditions);

public sealed record RecipeDefinition(
    string Code, string Id, string MachineId,
    IReadOnlyList<RecipeRequirement> Requirements, string OutputItemId,
    int BaseOutputCount, OutputRule OutputRule)
{
    [JsonIgnore]
    public IReadOnlyList<RecipeRequirement> ItemInputs =>
        Requirements.Where(rule => rule.Kind is RecipeResourceKind.Item or RecipeResourceKind.TaggedItem).ToArray();

}

public sealed record GuideDefinition(
    string ItemId, IReadOnlyList<RecipeDefinition> Recipes);

public sealed record MachineDefinition(
    string Code, string ItemId, string GuideId, TriggerMode Trigger,
    QualityMode Quality, int QualityStep, bool SupportsEfficiency,
    bool SupportsQuality, int Width, int Height, int InputWidth,
    int InputHeight, int OutputWidth, int OutputHeight,
    int ModuleWidth, int ModuleHeight, int WaterSourceSlots = SynthesisNumbers.Machines.DefaultWaterSourceSlots,
    int BasePowerCost = SynthesisNumbers.Machines.DefaultPowerCost);

/// <summary>
/// Native factories and work requests use the recipe catalog. Guides select
/// from that catalog; one recipe may appear in more than one handbook.
/// </summary>
public static class SynthesisDefinitions
{
    public const string OwnerId = "nicokobo.mechcore.synthesis";
    /// <summary>Version 28 adds the portable mining drill and its terminal recipe.</summary>
    public const string DefinitionVersion = "28";
    public const string BloodBagId = "blood_bag";
    public const string BlueBloodBagId = "blue_blood_bag";
    public const string ImmunivaxId = "large_purple_injector";
    public const string ProcessedMeatId = "processed_meat";
    public const string CommonElectronicId = "common_electronic";
    public const string MouseTrapId = "mouse_trap";
    public static string ModuleMatrixId(string suffix) => "nicokobo.mechcore_protocol.module_matrix." + suffix;

    public static int ProductionMarkupPercent(string machineId) =>
        RecipeConfiguration.ProductionMarkupPercent(machineId);

    private static string Id(string suffix) => OwnerId + "." + suffix;
    private static string RecipeId(string suffix) => Id("recipe." + suffix);
    private static RecipeCondition Minimum(RecipeProperty property, int value) =>
        new(property, value);
    private static RecipeRequirement Need(string id, int count,
        params RecipeCondition[] conditions) =>
        new(RecipeResourceKind.Item, id, count, RecipeAmountBasis.PerBatch,
            Array.AsReadOnly(conditions));
    private static IReadOnlyList<RecipeRequirement> Needs(
        params RecipeRequirement[] requirements) =>
        Array.AsReadOnly(requirements);

    public static readonly ItemDefinition Quartz =
        new("R01", Id("quartz"), ItemKind.Material, SynthesisNumbers.Items.Quartz.Width, SynthesisNumbers.Items.Quartz.Height, SynthesisNumbers.Items.Quartz.BaseValue, "refiner_glass_recycling+night_shop+scavenging", "common_ore");
    public static readonly ItemDefinition Silicon =
        new("R02", Id("silicon"), ItemKind.Material, SynthesisNumbers.Items.Silicon.Width, SynthesisNumbers.Items.Silicon.Height, SynthesisNumbers.Items.Silicon.BaseValue, "refining_and_night_shop", "metal_ingot");
    public static readonly ItemDefinition TitaniumOre =
        new("R04", Id("titanium_ore"), ItemKind.Material, SynthesisNumbers.Items.TitaniumOre.Width, SynthesisNumbers.Items.TitaniumOre.Height, SynthesisNumbers.Items.TitaniumOre.BaseValue, "night_shop+scavenging", "rare_ore");
    public static readonly ItemDefinition TitaniumAlloy =
        new("R05", Id("titanium_alloy"), ItemKind.Material, SynthesisNumbers.Items.TitaniumAlloy.Width, SynthesisNumbers.Items.TitaniumAlloy.Height, SynthesisNumbers.Items.TitaniumAlloy.BaseValue, "refining", "metal_ingot");
    public static readonly ItemDefinition PrecisionParts =
        new("C01", Id("precision_parts"), ItemKind.Material, SynthesisNumbers.Items.PrecisionParts.Width, SynthesisNumbers.Items.PrecisionParts.Height, SynthesisNumbers.Items.PrecisionParts.BaseValue, "synthesis", "metal_ingot");
    public static readonly ItemDefinition AdvancedCircuit =
        new("C02", Id("advanced_circuit"), ItemKind.Material, SynthesisNumbers.Items.AdvancedCircuit.Width, SynthesisNumbers.Items.AdvancedCircuit.Height, SynthesisNumbers.Items.AdvancedCircuit.BaseValue, "synthesis", "common_electronic");
    public static readonly ItemDefinition ControlChip =
        new("C03", Id("control_chip"), ItemKind.Material, SynthesisNumbers.Items.ControlChip.Width, SynthesisNumbers.Items.ControlChip.Height, SynthesisNumbers.Items.ControlChip.BaseValue, "assembly", "metal_ingot");
    public static readonly ItemDefinition PowerRegulator =
        new("C06", Id("power_regulator"), ItemKind.Material, SynthesisNumbers.Items.PowerRegulator.Width, SynthesisNumbers.Items.PowerRegulator.Height, SynthesisNumbers.Items.PowerRegulator.BaseValue, "assembly", "metal_ingot");
    public static readonly ItemDefinition MechanicalCore =
        new("C11", Id("mechanical_core"), ItemKind.Material, SynthesisNumbers.Items.MechanicalCore.Width, SynthesisNumbers.Items.MechanicalCore.Height, SynthesisNumbers.Items.MechanicalCore.BaseValue, "assembly", "metal_ingot");
    public static readonly ItemDefinition AiCore =
        new("Y01", Id("ai_core"), ItemKind.Cybernetic, SynthesisNumbers.Items.AiCore.Width, SynthesisNumbers.Items.AiCore.Height, SynthesisNumbers.Items.AiCore.BaseValue, "universal_manufacturing", "rare_electronic", SynthesisNumbers.Items.AiCore.ContrabandLevel);
    public static readonly ItemDefinition SleepCompressionModule =
        new("Y02", Id("sleep_compression_module"), ItemKind.Cybernetic, SynthesisNumbers.Items.SleepCompressionModule.Width, SynthesisNumbers.Items.SleepCompressionModule.Height, SynthesisNumbers.Items.SleepCompressionModule.BaseValue, "universal_manufacturing", "rare_electronic", SynthesisNumbers.Items.SleepCompressionModule.ContrabandLevel);
    public static readonly ItemDefinition KineticRecovery =
        new("Y03", Id("kinetic_recovery"), ItemKind.Cybernetic, SynthesisNumbers.Items.KineticRecovery.Width, SynthesisNumbers.Items.KineticRecovery.Height, SynthesisNumbers.Items.KineticRecovery.BaseValue, "universal_manufacturing", "rare_electronic", SynthesisNumbers.Items.KineticRecovery.ContrabandLevel);
    public static readonly ItemDefinition TitaniumExoskeleton =
        new("Y04", Id("titanium_exoskeleton"), ItemKind.Cybernetic, SynthesisNumbers.Items.TitaniumExoskeleton.Width, SynthesisNumbers.Items.TitaniumExoskeleton.Height, SynthesisNumbers.Items.TitaniumExoskeleton.BaseValue, "universal_manufacturing", "rare_electronic", SynthesisNumbers.Items.TitaniumExoskeleton.ContrabandLevel);
    public static readonly ItemDefinition FineMotorAssistant =
        new("Y05", Id("fine_motor_assistant"), ItemKind.Cybernetic, SynthesisNumbers.Items.FineMotorAssistant.Width, SynthesisNumbers.Items.FineMotorAssistant.Height, SynthesisNumbers.Items.FineMotorAssistant.BaseValue, "universal_manufacturing", "rare_electronic", SynthesisNumbers.Items.FineMotorAssistant.ContrabandLevel);

    public static readonly ItemDefinition CyberEye =
        new("Y06", Id("cyber_eye"), ItemKind.Cybernetic, SynthesisNumbers.Items.CyberEye.Width, SynthesisNumbers.Items.CyberEye.Height, SynthesisNumbers.Items.CyberEye.BaseValue, "universal_manufacturing", "aug_scanner");

    public static readonly ItemDefinition RefinerItem =
        new("K01", Id("material_refiner"), ItemKind.Machine, SynthesisNumbers.Items.RefinerItem.Width, SynthesisNumbers.Items.RefinerItem.Height, SynthesisNumbers.Items.RefinerItem.BaseValue, "night_shop+scavenging", "furnace");
    public static readonly ItemDefinition SynthesizerItem =
        new("K02", Id("synthesizer"), ItemKind.Machine, SynthesisNumbers.Items.SynthesizerItem.Width, SynthesisNumbers.Items.SynthesizerItem.Height, SynthesisNumbers.Items.SynthesizerItem.BaseValue, "night_shop+scavenging", "furnace");
    public static readonly ItemDefinition AssemblerItem =
        new("K03", Id("precision_assembler"), ItemKind.Machine, SynthesisNumbers.Items.AssemblerItem.Width, SynthesisNumbers.Items.AssemblerItem.Height, SynthesisNumbers.Items.AssemblerItem.BaseValue, "night_shop+scavenging", "furnace");
    // External machine metadata for recipes and guides; Forge registers its item.
    public static ItemDefinition UniversalManufacturerItem { get; } =
        new("K05", "nicokobo.forge.machines.universal_manufacturing_terminal", ItemKind.Machine, SynthesisNumbers.Items.UniversalManufacturerItem.Width, SynthesisNumbers.Items.UniversalManufacturerItem.Height, SynthesisNumbers.Items.UniversalManufacturerItem.BaseValue, "night_shop+scavenging", "furnace");

    public static readonly ItemDefinition RefiningGuideItem =
        new("G01", Id("refining_guide"), ItemKind.Guide, SynthesisNumbers.Items.RefiningGuideItem.Width, SynthesisNumbers.Items.RefiningGuideItem.Height, SynthesisNumbers.Items.RefiningGuideItem.BaseValue, "all_starts+machine_bundle_or_night_shop+scavenging", "water_guide");
    public static readonly ItemDefinition SynthesisGuideItem =
        new("G02", Id("synthesis_guide"), ItemKind.Guide, SynthesisNumbers.Items.SynthesisGuideItem.Width, SynthesisNumbers.Items.SynthesisGuideItem.Height, SynthesisNumbers.Items.SynthesisGuideItem.BaseValue, "all_starts+machine_bundle_or_night_shop+scavenging", "water_guide");
    public static readonly ItemDefinition CyberneticCatalogItem =
        new("G06", Id("cybernetic_catalog"), ItemKind.Guide, SynthesisNumbers.Items.CyberneticCatalogItem.Width, SynthesisNumbers.Items.CyberneticCatalogItem.Height, SynthesisNumbers.Items.CyberneticCatalogItem.BaseValue, "all_starts+night_shop+scavenging", "water_guide");

    public static readonly ItemDefinition RobotCat =
        new("Y07", Id("robot_cat"), ItemKind.Luxury, SynthesisNumbers.Items.RobotCat.Width, SynthesisNumbers.Items.RobotCat.Height, SynthesisNumbers.Items.RobotCat.BaseValue, "manufacturing", "common_electronic");





    public static readonly ItemDefinition CyberneticStorageBox =
        new("S01", Id("cybernetic_storage_box"), ItemKind.Storage, SynthesisNumbers.Items.CyberneticStorageBox.Width, SynthesisNumbers.Items.CyberneticStorageBox.Height, SynthesisNumbers.Items.CyberneticStorageBox.BaseValue, "manufacturing", "smuggler_bay");

    public static readonly ItemDefinition ScavengingRobot =
        new("A01", Id("scavenging_robot"), ItemKind.Robot, SynthesisNumbers.Items.ScavengingRobot.Width, SynthesisNumbers.Items.ScavengingRobot.Height, SynthesisNumbers.Items.ScavengingRobot.BaseValue, "manufacturing", "satchel");

    public static readonly ItemDefinition MiningDrill =
        new("T01", Id("mining_drill"), ItemKind.Tool, SynthesisNumbers.Items.MiningDrill.Width, SynthesisNumbers.Items.MiningDrill.Height, SynthesisNumbers.Items.MiningDrill.BaseValue, "manufacturing", "screwdriver");

    public static IReadOnlyList<ItemDefinition> Items { get; } = Array.AsReadOnly(new[]
    {
        Quartz, Silicon, TitaniumOre, TitaniumAlloy, PrecisionParts,
        AdvancedCircuit, ControlChip, PowerRegulator,
        MechanicalCore,
        RefinerItem, SynthesizerItem, AssemblerItem, RefiningGuideItem, SynthesisGuideItem,
        CyberneticCatalogItem, CyberEye, AiCore,
        SleepCompressionModule, KineticRecovery, TitaniumExoskeleton,
        FineMotorAssistant, RobotCat, CyberneticStorageBox, ScavengingRobot, MiningDrill,
        });

    public static IReadOnlyList<MachineDefinition> Machines { get; } = Array.AsReadOnly(new[]
    {
        new MachineDefinition(
            "K01",
            RefinerItem.Id,
            RefiningGuideItem.Id,
            TriggerMode.Nightly,
            QualityMode.Purity,
            SynthesisNumbers.Machines.K01.QualityStep,
            true,
            true,
            SynthesisNumbers.Machines.K01.Width,
            SynthesisNumbers.Machines.K01.Height,
            SynthesisNumbers.Machines.K01.InputWidth,
            SynthesisNumbers.Machines.K01.InputHeight,
            SynthesisNumbers.Machines.K01.OutputWidth,
            SynthesisNumbers.Machines.K01.OutputHeight,
            SynthesisNumbers.Machines.K01.ModuleWidth,
            SynthesisNumbers.Machines.K01.ModuleHeight,
            SynthesisNumbers.Machines.K01.WaterSourceSlots,
            SynthesisNumbers.Machines.K01.BasePowerCost),
        new MachineDefinition(
            "K02",
            SynthesizerItem.Id,
            SynthesisGuideItem.Id,
            TriggerMode.Nightly,
            QualityMode.ComponentGrade,
            SynthesisNumbers.Machines.K02.QualityStep,
            true,
            true,
            SynthesisNumbers.Machines.K02.Width,
            SynthesisNumbers.Machines.K02.Height,
            SynthesisNumbers.Machines.K02.InputWidth,
            SynthesisNumbers.Machines.K02.InputHeight,
            SynthesisNumbers.Machines.K02.OutputWidth,
            SynthesisNumbers.Machines.K02.OutputHeight,
            SynthesisNumbers.Machines.K02.ModuleWidth,
            SynthesisNumbers.Machines.K02.ModuleHeight,
            SynthesisNumbers.Machines.K02.WaterSourceSlots,
            SynthesisNumbers.Machines.K02.BasePowerCost),
        new MachineDefinition(
            "K03",
            AssemblerItem.Id,
            SynthesisGuideItem.Id,
            TriggerMode.Nightly,
            QualityMode.Purity,
            SynthesisNumbers.Machines.K03.QualityStep,
            true,
            true,
            SynthesisNumbers.Machines.K03.Width,
            SynthesisNumbers.Machines.K03.Height,
            SynthesisNumbers.Machines.K03.InputWidth,
            SynthesisNumbers.Machines.K03.InputHeight,
            SynthesisNumbers.Machines.K03.OutputWidth,
            SynthesisNumbers.Machines.K03.OutputHeight,
            SynthesisNumbers.Machines.K03.ModuleWidth,
            SynthesisNumbers.Machines.K03.ModuleHeight,
            SynthesisNumbers.Machines.K03.WaterSourceSlots,
            SynthesisNumbers.Machines.K03.BasePowerCost),
        new MachineDefinition(
            "K05",
            UniversalManufacturerItem.Id,
            SynthesisGuideItem.Id,
            TriggerMode.Nightly,
            QualityMode.None,
            SynthesisNumbers.Machines.K05.QualityStep,
            true,
            false,
            SynthesisNumbers.Machines.K05.Width,
            SynthesisNumbers.Machines.K05.Height,
            SynthesisNumbers.Machines.K05.InputWidth,
            SynthesisNumbers.Machines.K05.InputHeight,
            SynthesisNumbers.Machines.K05.OutputWidth,
            SynthesisNumbers.Machines.K05.OutputHeight,
            SynthesisNumbers.Machines.K05.ModuleWidth,
            SynthesisNumbers.Machines.K05.ModuleHeight,
            SynthesisNumbers.Machines.K05.WaterSourceSlots,
            SynthesisNumbers.Machines.K05.BasePowerCost)
    });

    internal static IReadOnlyList<RecipeDefinition> DefaultRecipes { get; } = Array.AsReadOnly(new[]
    {
        MetalRecipe("P17", "metal_from_ore", 2, 0, false),
        MetalRecipe("P18", "metal_from_mixed", 1, 1, false),
        MetalRecipe("P19", "metal_from_scrap", 0, 2, false),
        MetalRecipe("P20", "blast_metal_from_ore", 3, 0, true),
        MetalRecipe("P21", "blast_metal_from_ore_mixed", 2, 1, true),
        MetalRecipe("P22", "blast_metal_from_scrap_mixed", 1, 2, true),
        MetalRecipe("P23", "blast_metal_from_scrap", 0, 3, true),
        new RecipeDefinition(
            "P01",
            RecipeId("silicon"),
            RefinerItem.Id,
            Needs(Need(Quartz.Id, SynthesisNumbers.Recipes.P01.QuartzCount)),
            Silicon.Id,
            SynthesisNumbers.Recipes.P01.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "P02",
            RecipeId("titanium_alloy"),
            RefinerItem.Id,
            Needs(
            Need(TitaniumOre.Id, SynthesisNumbers.Recipes.P02.TitaniumOreCount),
            Need(
            "metal_ingot",
            SynthesisNumbers.Recipes.P02.MetalIngotCount,
            Minimum(
            RecipeProperty.MetalPurity,
            SynthesisNumbers.Recipes.P02.MinimumMetalPurity))),
            TitaniumAlloy.Id,
            SynthesisNumbers.Recipes.P02.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "P03",
            RecipeId("precision_parts"),
            SynthesizerItem.Id,
            Needs(Need(
            "metal_ingot",
            SynthesisNumbers.Recipes.P03.MetalIngotCount,
            Minimum(
            RecipeProperty.MetalPurity,
            SynthesisNumbers.Recipes.P03.MinimumMetalPurity))),
            PrecisionParts.Id,
            SynthesisNumbers.Recipes.P03.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "P04",
            RecipeId("advanced_circuit"),
            SynthesizerItem.Id,
            Needs(
            Need(
            Silicon.Id,
            SynthesisNumbers.Recipes.P04.SiliconCount,
            Minimum(
            RecipeProperty.MetalPurity,
            SynthesisNumbers.Recipes.P04.MinimumMetalPurity)),
            Need("common_electronic", SynthesisNumbers.Recipes.P04.CommonElectronicCount)),
            AdvancedCircuit.Id,
            SynthesisNumbers.Recipes.P04.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "P15", RecipeId("common_electronic"), SynthesizerItem.Id,
            Needs(Need("wire", SynthesisNumbers.Recipes.P15.WireCount),
                Need("printer_plastic", SynthesisNumbers.Recipes.P15.PrinterPlasticCount)),
            CommonElectronicId, SynthesisNumbers.Recipes.P15.OutputCount, OutputRule.Fixed),
        new RecipeDefinition(
            "P16", RecipeId("mouse_trap"), SynthesizerItem.Id,
            Needs(Need("metal_ingot", SynthesisNumbers.Recipes.P16.MetalIngotCount),
                Need("wire", SynthesisNumbers.Recipes.P16.WireCount)),
            MouseTrapId, SynthesisNumbers.Recipes.P16.OutputCount, OutputRule.Fixed),
        new RecipeDefinition(
            "P06",
            RecipeId("control_chip"),
            AssemblerItem.Id,
            Needs(
            Need(AdvancedCircuit.Id, SynthesisNumbers.Recipes.P06.AdvancedCircuitCount),
            Need(PrecisionParts.Id, SynthesisNumbers.Recipes.P06.PrecisionPartsCount)),
            ControlChip.Id,
            SynthesisNumbers.Recipes.P06.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "P09",
            RecipeId("power_regulator"),
            AssemblerItem.Id,
            Needs(
            Need(PrecisionParts.Id, SynthesisNumbers.Recipes.P09.PrecisionPartsCount),
            Need(TitaniumAlloy.Id, SynthesisNumbers.Recipes.P09.TitaniumAlloyCount),
            Need("wire", SynthesisNumbers.Recipes.P09.WireCount)),
            PowerRegulator.Id,
            SynthesisNumbers.Recipes.P09.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "P14",
            RecipeId("mechanical_core"),
            AssemblerItem.Id,
            Needs(
            Need(ControlChip.Id, SynthesisNumbers.Recipes.P14.ControlChipCount),
            Need(PowerRegulator.Id, SynthesisNumbers.Recipes.P14.PowerRegulatorCount),
            Need(TitaniumAlloy.Id, SynthesisNumbers.Recipes.P14.TitaniumAlloyCount)),
            MechanicalCore.Id,
            SynthesisNumbers.Recipes.P14.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "M01",
            RecipeId("cyber_eye"),
            UniversalManufacturerItem.Id,
            Needs(
            Need(MechanicalCore.Id, SynthesisNumbers.Recipes.M01.MechanicalCoreCount),
            Need(PowerRegulator.Id, SynthesisNumbers.Recipes.M01.PowerRegulatorCount),
            Need("magnifier", SynthesisNumbers.Recipes.M01.MagnifierCount)),
            CyberEye.Id,
            SynthesisNumbers.Recipes.M01.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "M02",
            RecipeId("ai_core"),
            UniversalManufacturerItem.Id,
            Needs(
            Need(MechanicalCore.Id, SynthesisNumbers.Recipes.M02.MechanicalCoreCount),
            Need(AdvancedCircuit.Id, SynthesisNumbers.Recipes.M02.AdvancedCircuitCount),
            Need("system_capped_neural_core", SynthesisNumbers.Recipes.M02.SystemCappedNeuralCoreCount)),
            AiCore.Id,
            SynthesisNumbers.Recipes.M02.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "M03",
            RecipeId("sleep_compression_module"),
            UniversalManufacturerItem.Id,
            Needs(
            Need(ControlChip.Id, SynthesisNumbers.Recipes.M03.ControlChipCount),
            Need(AdvancedCircuit.Id, SynthesisNumbers.Recipes.M03.AdvancedCircuitCount),
            Need("caffeine_pill", SynthesisNumbers.Recipes.M03.CaffeinePillCount)),
            SleepCompressionModule.Id,
            SynthesisNumbers.Recipes.M03.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "M04",
            RecipeId("kinetic_recovery"),
            UniversalManufacturerItem.Id,
            Needs(
            Need(PowerRegulator.Id, SynthesisNumbers.Recipes.M04.PowerRegulatorCount),
            Need("energy_credit_ext", SynthesisNumbers.Recipes.M04.EnergyCreditExtCount),
            Need(
                    ModuleMatrixId("continuous_efficiency_module"),
                    SynthesisNumbers.Recipes.M04.ContinuousEfficiencyModuleCount)),
            KineticRecovery.Id,
            SynthesisNumbers.Recipes.M04.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "M05",
            RecipeId("titanium_exoskeleton"),
            UniversalManufacturerItem.Id,
            Needs(
            Need(TitaniumAlloy.Id, SynthesisNumbers.Recipes.M05.TitaniumAlloyCount),
            Need(PrecisionParts.Id, SynthesisNumbers.Recipes.M05.PrecisionPartsCount),
            Need(
                    ModuleMatrixId("continuous_acceleration_module"),
                    SynthesisNumbers.Recipes.M05.ContinuousAccelerationModuleCount)),
            TitaniumExoskeleton.Id,
            SynthesisNumbers.Recipes.M05.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "M06",
            RecipeId("fine_motor_assistant"),
            UniversalManufacturerItem.Id,
            Needs(
            Need(MechanicalCore.Id, SynthesisNumbers.Recipes.M06.MechanicalCoreCount),
            Need(PrecisionParts.Id, SynthesisNumbers.Recipes.M06.PrecisionPartsCount),
            Need(
                    ModuleMatrixId("continuous_calibration_module"),
                    SynthesisNumbers.Recipes.M06.ContinuousCalibrationModuleCount)),
            FineMotorAssistant.Id,
            SynthesisNumbers.Recipes.M06.OutputCount,
            OutputRule.Fixed),
        new RecipeDefinition(
            "M07", RecipeId("robot_cat"), UniversalManufacturerItem.Id,
            Needs(Need(AiCore.Id, SynthesisNumbers.Recipes.M07.AiCoreCount),
                Need(PrecisionParts.Id, SynthesisNumbers.Recipes.M07.PrecisionPartsCount),
                Need(TitaniumAlloy.Id, SynthesisNumbers.Recipes.M07.TitaniumAlloyCount)),
            RobotCat.Id, SynthesisNumbers.Recipes.M07.OutputCount, OutputRule.Fixed),
        new RecipeDefinition(
            "M08", RecipeId("cybernetic_storage_box"), UniversalManufacturerItem.Id,
            Needs(Need(TitaniumAlloy.Id, SynthesisNumbers.Recipes.M08.TitaniumAlloyCount),
                Need(PrecisionParts.Id, SynthesisNumbers.Recipes.M08.PrecisionPartsCount),
                Need("metal_ingot", SynthesisNumbers.Recipes.M08.MetalIngotCount)),
            CyberneticStorageBox.Id, SynthesisNumbers.Recipes.M08.OutputCount, OutputRule.Fixed),
        new RecipeDefinition(
            "M09", RecipeId("scavenging_robot"), UniversalManufacturerItem.Id,
            Needs(Need(MechanicalCore.Id, SynthesisNumbers.Recipes.M09.MechanicalCoreCount),
                Need("metal_scanner", SynthesisNumbers.Recipes.M09.ScannerCount,
                    Minimum(RecipeProperty.ScavScannerLevel, SynthesisNumbers.ScavengingRobot.InitialScannerChance)),
                new(RecipeResourceKind.TaggedItem, "CONTAINER_TAG",
                    SynthesisNumbers.Recipes.M09.StorageAreaCount, RecipeAmountBasis.PerBatch,
                    [Minimum(RecipeProperty.EmptyStorage, 1)])),
            ScavengingRobot.Id, SynthesisNumbers.Recipes.M09.OutputCount, OutputRule.Fixed),
        new RecipeDefinition(
            "M10", RecipeId("mining_drill"), UniversalManufacturerItem.Id,
            Needs(Need("metal_ingot", SynthesisNumbers.Recipes.M10.MetalIngotCount,
                    Minimum(RecipeProperty.MetalPurity, SynthesisNumbers.Recipes.M10.MinimumMetalPurity)),
                Need(PrecisionParts.Id, SynthesisNumbers.Recipes.M10.PrecisionPartsCount),
                Need(CommonElectronicId, SynthesisNumbers.Recipes.M10.CommonElectronicCount)),
            MiningDrill.Id, SynthesisNumbers.Recipes.M10.OutputCount, OutputRule.Fixed)
    });

    internal static IReadOnlyList<GuideDefinition> DefaultGuides { get; } = SelectGuides(DefaultRecipes);

    private static RecipeDefinition MetalRecipe(string code, string id, int ore, int scrap, bool blast) =>
        new(code, RecipeId(id), RefinerItem.Id,
            Array.AsReadOnly(new[] { Need("common_ore", ore), Need("scrap_metal", scrap) }
                .Where(input => input.Amount > 0).ToArray()),
            "metal_ingot", blast ? 2 : 1,
            blast ? OutputRule.BlastMetalSmelting : OutputRule.MetalSmelting);

    public static IReadOnlyList<GuideDefinition> Guides { get; private set; } =
        DefaultGuides;

    public static IReadOnlyList<RecipeDefinition> Recipes { get; private set; } =
        DefaultRecipes;

    private static IReadOnlyList<GuideDefinition> SelectGuides(IReadOnlyList<RecipeDefinition> recipes) =>
        Array.AsReadOnly(new[]
        {
            new GuideDefinition(RefiningGuideItem.Id, Array.AsReadOnly(recipes.Where(recipe =>
                recipe.MachineId == RefinerItem.Id).ToArray())),
            new GuideDefinition(SynthesisGuideItem.Id, Array.AsReadOnly(recipes.Where(recipe =>
                recipe.MachineId != RefinerItem.Id).ToArray())),
            new GuideDefinition(CyberneticCatalogItem.Id, Array.Empty<RecipeDefinition>())
        });

    internal static void UseRecipes(IReadOnlyList<RecipeDefinition> recipes)
    {
        Guides = SelectGuides(recipes);
        Recipes = Array.AsReadOnly(recipes.ToArray());
    }

    /// <summary>Items that carry the native INGOT_PURITY_TAG: the original
    /// ingot, refined materials and machined components. A MetalPurity condition is only
    /// meaningful for these.</summary>
    public static bool CarriesPurity(string? itemId) =>
        itemId == "metal_ingot" || itemId == Silicon.Id ||
        itemId == TitaniumAlloy.Id || CarriesMachinedPurity(itemId);

    public static bool CarriesMachinedPurity(string? itemId) =>
        itemId == PrecisionParts.Id || itemId == ControlChip.Id ||
        itemId == PowerRegulator.Id || itemId == MechanicalCore.Id;

    public static bool CarriesComponentGrade(string? itemId) =>
        itemId == AdvancedCircuit.Id;

    public static bool IsMachineItemInput(string? machineId, string? itemId) =>
        machineId != null && itemId != null &&
        Recipes.Any(recipe => recipe.MachineId == machineId &&
            recipe.ItemInputs.Any(input => input.ResourceId == itemId));

    public static ItemDefinition Item(string id) =>
        id == UniversalManufacturerItem.Id ? UniversalManufacturerItem : Items.Single(item => item.Id == id);

    public static MachineDefinition Machine(string id) =>
        Machines.Single(machine => machine.ItemId == id);

    public static RecipeDefinition Recipe(string id) =>
        Recipes.Single(recipe => recipe.Id == id);
}
