using Newtonsoft.Json;
using Nicokobo.ModUtilities;

namespace Nicokobo.FoodProcessing.Definitions;

public enum ItemKind { Material, Cybernetic, Machine, Guide, Household, Drink, Storage, Food, Robot, Luxury, Medical }
public enum TriggerMode { Nightly = 1 }
public enum QualityMode { None, Purity, ComponentGrade }
public enum OutputRule { Fixed }
public enum RecipeResourceKind { Item, Liquid, TaggedItem }
public enum RecipeAmountBasis { PerBatch }
public enum RecipeProperty
{
    MetalPurity, GenuineInjector, UsableGlowberry, ScavScannerLevel, EmptyStorage, RawMeat, WaterQuality, WineScore,
    SmallOrStandardNutrifruit, LargeOrHugeNutrifruit, SmallFishFillet, StandardFishFillet, RipeGlowberry, SingleItem
}
public enum IngotPurityGrade
{
    Unknown = 0, VeryLow = 5, Low = 6, Fair = 7,
    High = 8, VeryHigh = 9, Perfect = 10
}

public sealed record ItemDefinition(
    string Code, string Id, ItemKind Kind, int Width, int Height, int BaseValue,
    string Source, string NativeTemplateId, int ContrabandLevel = 0, bool IsLuxury = false, string Brand = "", string Category = "", int Calories = -1,
    bool IsMeat = false, bool IsAlcohol = false, bool IsTreat = false)
{
    public string ChineseName => ModText.GetFor(FoodDefinitions.ModId, Id + ".name", "zh");
    public string EnglishName => ModText.GetFor(FoodDefinitions.ModId, Id + ".name", "en");
}

public sealed record RecipeCondition(RecipeProperty Property, int MinimumValue);

/// <summary>One item rule, with piece counts resolved per batch.</summary>
public sealed record RecipeRequirement(
    RecipeResourceKind Kind, string ResourceId, int Amount,
    RecipeAmountBasis Basis, IReadOnlyList<RecipeCondition> Conditions);

public sealed record RecipeDefinition(
    string Code, string Id, string MachineId,
    IReadOnlyList<RecipeRequirement> Requirements, string OutputItemId,
    int BaseOutputCount, OutputRule OutputRule, int? MarkupPercent = null, int Stars = 1)
{
    [JsonIgnore]
    public int RequiredQuality => (Stars - 1) * 50;
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
    int ModuleWidth, int ModuleHeight, int WaterSourceSlots = FoodNumbers.Machines.DefaultWaterSourceSlots,
    int BasePowerCost = FoodNumbers.Machines.DefaultPowerCost);

/// <summary>Release food catalog. Recipe IDs also identify machine selections.</summary>
public static class FoodDefinitions
{
    public const string ModId = "nicokobo.food_processing";
    public const string OwnerId = "nicokobo.mechcore";
    public const string IdPrefix = "nicokobo.mechcore.synthesis";
    public const string BloodBagId = "blood_bag", BlueBloodBagId = "blue_blood_bag";
    public const string ImmunivaxId = "large_purple_injector", ProcessedMeatId = "processed_meat";
    public const string LivestockImmuneSerumId = "serum_green", LivestockSteroidSerumId = "serum_red";
    public const string RubbingAlcoholId = "rubbing_alcohol", RawMeatTag = "BIO_PRINTABLE_TAG";
    public const string NativeCheeseId = "processed_cheese", IndustrialFishFeedId = "industrial_fish_feed";
    public const string NativeAnimalFatId = "fat_meat", NativeInstantRamenId = "cup_noodle";
    public const string NativeSodaId = "soda_red", NativeEnergyDrinkId = "energy_drink";
    public const string NomoreCareKitId = IdPrefix + ".nomore_care_kit";
    internal const string BasicMedicalKitId = IdPrefix + ".basic_medical_kit";
    internal const string AdvancedMedicalKitId = IdPrefix + ".advanced_medical_kit";
    internal const string RestrictedMedicalKitId = IdPrefix + ".restricted_medical_kit";
    internal const string BasicMedicalPool = "basic_medical_pool";
    internal const string AdvancedMedicalPool = "advanced_medical_pool";
    internal const string RestrictedMedicalPool = "restricted_medical_pool";
    internal const int MedicalKitMinimum = 3;
    private static string Id(string suffix) => IdPrefix + "." + suffix;
    private static RecipeCondition Minimum(RecipeProperty property, int value) => new(property, value);
    private static RecipeRequirement Need(string id, int count, params RecipeCondition[] conditions) =>
        new(RecipeResourceKind.Item, id, count, RecipeAmountBasis.PerBatch, Array.AsReadOnly(conditions));
    private static IReadOnlyList<RecipeRequirement> Needs(params RecipeRequirement[] requirements) => Array.AsReadOnly(requirements);
    private static RecipeRequirement RawMeat(int count) => new(RecipeResourceKind.TaggedItem, RawMeatTag, count,
        RecipeAmountBasis.PerBatch, [Minimum(RecipeProperty.RawMeat, 1)]);
    private static RecipeRequirement Water(int ml, int quality = 1) => new(RecipeResourceKind.Liquid, "water", ml,
        RecipeAmountBasis.PerBatch, [Minimum(RecipeProperty.WaterQuality, quality)]);
    private static RecipeDefinition Recipe(string code, string suffix, ItemDefinition machine, IReadOnlyList<RecipeRequirement> inputs,
        string output, int count = 1, int stars = 1) =>
        new(code, Id("recipe." + suffix), machine.Id, inputs, output, count, OutputRule.Fixed, FoodExpansion.StarMarkup(stars), stars);

    public static readonly ItemDefinition InfusionTowerItem = new("K04", Id("liquid_infusion_tower"), ItemKind.Machine, 3, 3, 150, "night_shop+scavenging", "furnace");
    public static readonly ItemDefinition FoodProcessorItem = new("K06", Id("food_processor"), ItemKind.Machine, 3, 3, 150, "night_shop+scavenging", "furnace");
    public static readonly ItemDefinition FoodProcessingGuideItem = new("G07", Id("food_processing_guide"), ItemKind.Guide, 2, 3, 25, "all_starts+machine_bundle_or_night_shop", "water_guide");
    public static readonly ItemDefinition CompressedNutrientLiquid = new("D02", Id("compressed_nutrient_liquid"), ItemKind.Drink, 1, 2, 85, "infusion", "processed_juice");
    public static readonly ItemDefinition NutrifruitJuice = new("D04", Id("nutrifruit_juice"), ItemKind.Drink, 2, 3, 80, "infusion", "processed_juice");
    public static readonly ItemDefinition CannedCoffee = new("D05", Id("canned_coffee"), ItemKind.Drink, 1, 2, 58, "infusion", "energy_drink");
    public static readonly ItemDefinition MeatSauceNoodles = new("E01", Id("meat_sauce_noodles"), ItemKind.Food, 2, 2, 71, "processing", "cup_noodle");
    public static readonly ItemDefinition LargeJerky = new("E05", Id("large_jerky"), ItemKind.Food, 2, 2, 173, "processing", "processed_meat");
    public static readonly ItemDefinition ExpeditionRation = new("E07", Id("expedition_ration"), ItemKind.Food, 2, 1, 60, "processing", "li_eat_snackbar");
    public static readonly ItemDefinition FishFloss = new("E08", Id("fish_floss"), ItemKind.Food, 2, 1, 153, "processing", "fish_fillet");
    public static readonly ItemDefinition CaviarEelPlatter = new("E14", Id("caviar_eel_platter"), ItemKind.Food, 3, 2, 604, "processing", "eel_fillet", IsLuxury: true);
    public static readonly ItemDefinition MincedFishCan = new("E15", Id("minced_fish_can"), ItemKind.Food, 2, 2, 60, "processing", "processed_meat");
    public static readonly ItemDefinition MushroomBake = new("E17", Id("mushroom_bake"), ItemKind.Food, 3, 2, 137, "processing", "processed_meat");
    public static readonly ItemDefinition SpicyMeatBites = new("E18", Id("spicy_meat_bites"), ItemKind.Food, 2, 1, 159, "processing", "processed_meat");
    public static readonly ItemDefinition GalaxySundae = new("E19", Id("galaxy_sundae"), ItemKind.Food, 2, 2, 130, "infusion", "beis_icecream");
    public static readonly ItemDefinition RoeFiberPatty = new("E20", Id("roe_fiber_patty"), ItemKind.Food, 2, 1, 107, "processing", "processed_meat");
    public static readonly ItemDefinition SupplyJelly = new("E21", Id("supply_jelly"), ItemKind.Food, 1, 2, 39, "infusion", "li_eat_snackbar");
    public static readonly ItemDefinition StarportFizz = new("D09", Id("starport_fizz"), ItemKind.Drink, 2, 2, 74, "infusion", "red_beer");
    public static readonly ItemDefinition FishFiberSalad = new("E22", Id("fish_fiber_salad"), ItemKind.Food, 2, 2, 121, "processing", "fish_fillet");

    public static IReadOnlyList<ItemDefinition> Items { get; } = Array.AsReadOnly(new[]
    {
        InfusionTowerItem, FoodProcessorItem, FoodProcessingGuideItem, CompressedNutrientLiquid, NutrifruitJuice,
        CannedCoffee, MeatSauceNoodles, LargeJerky, ExpeditionRation, FishFloss, CaviarEelPlatter, MincedFishCan,
        MushroomBake, SpicyMeatBites, GalaxySundae, RoeFiberPatty, SupplyJelly, StarportFizz, FishFiberSalad
    }.Select(FoodExpansion.Decorate).Concat(FoodExpansion.AllProducts.Select(product => product.Item)).ToArray());
    public static IReadOnlyList<MachineDefinition> Machines { get; } = Array.AsReadOnly(new[]
    {
        new MachineDefinition("K04", InfusionTowerItem.Id, FoodProcessingGuideItem.Id, TriggerMode.Nightly,
            QualityMode.ComponentGrade, 100, true, true, 3, 3, 9, 6, 9, 6, 7, 5, 1, 10),
        new MachineDefinition("K06", FoodProcessorItem.Id, FoodProcessingGuideItem.Id, TriggerMode.Nightly,
            QualityMode.ComponentGrade, 100, true, true, 3, 3, 9, 6, 9, 6, 7, 5, 0, 10)
    });
    internal static IReadOnlyList<RecipeDefinition> DefaultRecipes { get; } = Array.AsReadOnly(new[]
    {
        Recipe("F03", "nutrifruit_juice", InfusionTowerItem, Needs(Need("nutrifruit", 3), Water(500)), NutrifruitJuice.Id, stars: 1),
        Recipe("F04", "meat_sauce_noodles", FoodProcessorItem, Needs(Need("cup_noodle", 1), Need(ProcessedMeatId, 1)), MeatSauceNoodles.Id, stars: 2),
        Recipe("F06", "canned_coffee", InfusionTowerItem, Needs(Need("coffee_bean", 1), Water(250)), CannedCoffee.Id, stars: 1),
        Recipe("F12", "large_jerky", FoodProcessorItem, Needs(RawMeat(2)), LargeJerky.Id, stars: 1),
        Recipe("F14", "expedition_ration", FoodProcessorItem, Needs(Need(ProcessedMeatId, 1), Need("nutrifruit", 1)), ExpeditionRation.Id, stars: 2),
        Recipe("F15", "livestock_immune_serum", InfusionTowerItem, Needs(Need("common_medical", 1), Need("nutrifruit", 2), Water(250)), LivestockImmuneSerumId, stars: 2),
        Recipe("F16", "livestock_steroid_serum", InfusionTowerItem, Needs(Need("common_medical", 1), Need(ProcessedMeatId, 1), Water(250)), LivestockSteroidSerumId, stars: 2),
        Recipe("F17", "rubbing_alcohol", InfusionTowerItem, Needs(Need("common_chemical", 1), Water(750, 2)), RubbingAlcoholId, 3, stars: 1),
        Recipe("F18", "fish_floss", FoodProcessorItem, Needs(Need("fish_fillet", 2)), FishFloss.Id, stars: 1),
        Recipe("F24", "caviar_eel_platter", FoodProcessorItem, Needs(Need("eel_fillet", 1), Need("caviar", 1), Need("processed_cheese", 1)), CaviarEelPlatter.Id, stars: 3),
        Recipe("F25", "minced_fish_can", FoodProcessorItem, Needs(Need("fish_scrap", 3), Need(ProcessedMeatId, 1)), MincedFishCan.Id, stars: 2),
        Recipe("P10", "blue_blood_bag", InfusionTowerItem, Needs(Need(BloodBagId, 1), Need(ImmunivaxId, 1, Minimum(RecipeProperty.GenuineInjector, 1))), BlueBloodBagId, stars: 3),
        Recipe("P12", "compressed_nutrient_liquid", InfusionTowerItem, Needs(Need("nutrifruit", 3), Need("processed_juice", 1)), CompressedNutrientLiquid.Id, stars: 3),
        Recipe("F27", "mushroom_bake", FoodProcessorItem, Needs(Need("edible_mushroom", 2), Need("processed_milk", 1)), MushroomBake.Id, stars: 2),
        Recipe("F28", "spicy_meat_bites", FoodProcessorItem, Needs(Need("small_raw_meat", 2), Need("red_beer", 1)), SpicyMeatBites.Id, stars: 2),
        Recipe("F29", "galaxy_sundae", InfusionTowerItem, Needs(Need("galaxy_blend", 1), Need("beis_icecream", 1)), GalaxySundae.Id, stars: 2),
        Recipe("F30", "roe_fiber_patty", FoodProcessorItem, Needs(Need("fillerweed", 1), Need("sturgeon_roe", 1), Need("processed_cheese", 1)), RoeFiberPatty.Id, 2, stars: 3),
        Recipe("F31", "supply_jelly", InfusionTowerItem, Needs(Need("energy_drink", 1), Need("li_eat_snackbar", 1)), SupplyJelly.Id, 2, stars: 2),
        Recipe("F32", "starport_fizz", InfusionTowerItem, Needs(Need("wine_bottle", 1), Need(Id("glowberry_soda"), 1)), StarportFizz.Id, stars: 2),
        Recipe("F33", "recombined_cheese", InfusionTowerItem, Needs(Need("processed_milk", 2), Need("fat_meat", 1)), NativeCheeseId, 4, stars: 1),
        Recipe("F34", "industrial_fish_feed", FoodProcessorItem, Needs(Need("fish_scrap", 3), Need("nutrifruit", 1)), IndustrialFishFeedId, stars: 1),
        Recipe("F35", "fish_fiber_salad", FoodProcessorItem, Needs(Need("fish_fillet", 1), Need("fillerweed", 1)), FishFiberSalad.Id, stars: 2),
        Recipe("F36", "animal_fat", FoodProcessorItem, Needs(Need("small_raw_meat", 2)), NativeAnimalFatId, stars: 1),
        Recipe("F37", "instant_ramen", FoodProcessorItem, Needs(Need("nutrifruit", 2), Need("edible_mushroom", 1)), NativeInstantRamenId, 2, stars: 1),
        Recipe("F38", "yeast_cheese", InfusionTowerItem, Needs(Need("processed_milk", 1), Need("wine_yeast", 1)), NativeCheeseId, 2, stars: 1),
        Recipe("F39", "soda", InfusionTowerItem, Needs(Need("processed_juice", 1), Water(500)), NativeSodaId, 2, stars: 1),
        Recipe("F40", "energy_drink", InfusionTowerItem, Needs(Need(NativeSodaId, 1), Need("caffeine_pill", 1)), NativeEnergyDrinkId, stars: 1),
        Recipe("F41", "peat", FoodProcessorItem, Needs(Need("fish_scrap", 2), Need("fillerweed", 1)), ProcessedMeatId, stars: 1)
    }.Concat(FoodExpansion.AllProducts.Select(product => product.Recipe)).ToArray());
    public static IReadOnlyList<RecipeDefinition> Recipes { get; private set; } = DefaultRecipes;
    public static IReadOnlyList<GuideDefinition> Guides => [new(FoodProcessingGuideItem.Id, Recipes)];
    internal static void UseRecipes(IReadOnlyList<RecipeDefinition> recipes) => Recipes = Array.AsReadOnly(recipes.ToArray());
    internal static bool IsCareKit(string id) => id == NomoreCareKitId || IsPooledMedicalKit(id);
    internal static bool IsPooledMedicalKit(string id) => id is BasicMedicalKitId or AdvancedMedicalKitId or RestrictedMedicalKitId;
    public static bool IsNativeConsumable(string id) => id is BlueBloodBagId or LivestockImmuneSerumId or LivestockSteroidSerumId or RubbingAlcoholId;
    public static bool IsCustomFood(string id) => Items.Any(item => item.Id == id && item.Kind is ItemKind.Drink or ItemKind.Food);
    public static bool IsFoodOutput(string id) => id is NativeCheeseId or NativeAnimalFatId or NativeInstantRamenId or
        NativeSodaId or NativeEnergyDrinkId or ProcessedMeatId || IsCustomFood(id);
    public static ItemDefinition Item(string id) => Items.Single(item => item.Id == id);
    public static MachineDefinition Machine(string id) => Machines.Single(machine => machine.ItemId == id);
}
