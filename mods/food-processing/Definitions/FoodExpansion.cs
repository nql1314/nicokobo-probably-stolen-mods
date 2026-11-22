namespace Nicokobo.FoodProcessing.Definitions;

// Authored product catalog. Existing IDs remain in FoodDefinitions; new item codes
// use X_ because the original catalog already owns E01, E04, etc.
internal static class FoodExpansion
{
    internal sealed record Product(ItemDefinition Item, RecipeDefinition Recipe);
    private static string Id(string suffix) => FoodDefinitions.IdPrefix + "." + suffix;
    private static RecipeRequirement Need(string id, int amount = 1, params RecipeCondition[] conditions) =>
        new(RecipeResourceKind.Item, id, amount, RecipeAmountBasis.PerBatch, conditions);
    private static RecipeRequirement Berry() => Need("wine_berry", 1, new RecipeCondition(RecipeProperty.UsableGlowberry, 1));
    private static RecipeRequirement Wine() => Need("wine_bottle", 1, new RecipeCondition(RecipeProperty.WineScore, 5));
    private static RecipeRequirement Single(string id) => Need(id, 1, new RecipeCondition(RecipeProperty.SingleItem, 1));
    private static Product Make(string code, string suffix, bool liquid, int stars, string brand, string category,
        int width, int height, int calories, int value, int count, bool meat, bool alcohol, params RecipeRequirement[] needs)
    {
        var kind = category == "medical" ? ItemKind.Medical : liquid && category != "jelly" ? ItemKind.Drink : ItemKind.Food;
        var item = new ItemDefinition("X_" + code, Id(suffix), kind,
            width, height, value, liquid ? "infusion" : "processing", kind == ItemKind.Drink ? "processed_juice" : "processed_meat",
            IsLuxury: stars == 3, Brand: brand, Category: category, Calories: calories, IsMeat: meat,
            IsAlcohol: alcohol, IsTreat: category is "snack" or "dessert" or "jelly");
        var recipe = new RecipeDefinition(code, Id("recipe." + suffix),
            Id(liquid ? "liquid_infusion_tower" : "food_processor"), needs, item.Id, count, OutputRule.Fixed,
            StarMarkup(stars), stars);
        return new(item, recipe);
    }

    internal static int StarMarkup(int stars) => stars switch { 1 => 25, 2 => 50, 3 => 100, _ => throw new ArgumentOutOfRangeException(nameof(stars)) };

    internal static readonly IReadOnlyList<Product> Products = new Product[]
    {
        Make("A01", "nutrifruit_soda", true, 1, "colaco", "soda", 1, 2, 225, 50, 2, false, false, Need("nutrifruit"), Need("soda_red")),
        Make("A02", "glowberry_soda", true, 1, "colaco", "soda", 1, 2, 175, 60, 2, false, false, Berry(), Need("soda_red")),
        Make("A03", "pilk_latte", true, 1, "stardeer", "coffee", 1, 2, 450, 80, 2, false, false, Need(Id("canned_coffee")), Need("processed_milk")),
        Make("A04", "chocolate_pilk", true, 1, "beis", "drink", 2, 3, 650, 85, 2, false, false, Need("cat_bar"), Need("processed_milk")),
        Make("A11", "springwater_coffee", true, 3, "stardeer", "coffee", 1, 2, 0, 380, 2, false, false, Need("bottled_water_premium"), Need("coffee_bean", 2)),
        Make("B01", "dockside_shandy", true, 1, "redimp", "alcohol", 1, 2, 150, 65, 2, false, true, Need("red_beer"), Need("soda_red")),
        Make("B05", "coffee_beer", true, 2, "redimp", "alcohol", 1, 2, 100, 90, 2, false, true, Need("red_beer"), Need(Id("canned_coffee"))),
        Make("B09", "reserve_spritz", true, 3, "redimp", "alcohol", 2, 2, 150, 400, 2, false, true, Wine(), Need("soda_red")),
        Make("B12", "vanilla_wine_float", true, 3, "redimp", "dessert", 2, 2, 650, 480, 2, false, true, Wine(), Need("beis_icecream"), Need("soda_red")),
        Make("C01", "cheese_noodles", false, 1, "tang", "noodles", 2, 2, 1250, 85, 1, false, false, Need("cup_noodle"), Need("processed_cheese")),
        Make("C02", "mushroom_noodles", false, 1, "tang", "noodles", 2, 2, 1100, 70, 1, false, false, Need("cup_noodle"), Need("edible_mushroom")),
        Make("C05", "beer_stew_noodles", false, 2, "tang", "noodles", 2, 2, 1500, 230, 1, true, false, Need("cup_noodle"), Need(Id("spicy_meat_bites"))),
        Make("C07", "fish_floss_noodles", false, 2, "tang", "noodles", 2, 2, 1600, 300, 1, true, false, Need("cup_noodle"), Need(Id("fish_floss"))),
        Make("C10", "eel_cheese_noodles", false, 3, "tang", "noodles", 2, 2, 1800, 480, 1, true, false, Need("cup_noodle"), Need("eel_fillet"), Need("processed_cheese")),
        Make("D01", "chocolate_energy_bites", false, 1, "starnest", "snack", 2, 1, 400, 60, 3, false, false, Need("li_eat_snackbar", 2), Need("cat_bar")),
        Make("D02", "cheese_fiber_crackers", false, 1, "davre", "snack", 2, 1, 300, 55, 3, false, false, Need("fillerweed", 2), Need("processed_cheese")),
        Make("D09", "caviar_cheese_crackers", false, 3, "nineorbits", "luxury", 2, 1, 350, 330, 3, true, false, Need(Id("cheese_fiber_crackers")), Need("caviar")),
        Make("D12", "reserve_wine_chocolates", false, 3, "beis", "dessert", 2, 1, 450, 280, 4, false, true, Wine(), Need("cat_bar", 3)),
        Make("E04", "cheese_luncheon_meat", false, 1, "davre", "meat", 2, 2, 450, 70, 2, true, false, Need("processed_meat"), Need("processed_cheese")),
        Make("E05", "mushroom_meat_can", false, 2, "davre", "canned", 2, 2, 750, 125, 2, true, false, Need("processed_meat", 2), Need("edible_mushroom", 2)),
        Make("E06", "surf_turf_banquet_can", false, 3, "davre", "canned", 2, 2, 950, 400, 2, true, false, Need("raw_meat"), Need("eel_fillet"), Need("edible_mushroom")),
        Make("E14", "nightshift_coffee", true, 2, "stardeer", "coffee", 1, 2, 150, 150, 2, false, false, Need(Id("canned_coffee"), 2), Need("energy_drink")),
        Make("E21", "berry_spring_fizz", true, 3, "colaco", "soda", 1, 3, 125, 290, 3, false, false, Need("bottled_water_premium"), Berry(), Need("soda_red")),
        Make("E22", "mushroom_energy_bar", false, 1, "starnest", "supply", 2, 1, 350, 50, 2, false, false, Need("li_eat_snackbar"), Need("edible_mushroom"))
    };

    internal static readonly IReadOnlyList<Product> FarmAndPharmacyProducts = new Product[]
    {
        Make("F42", "dried_nutrifruit", false, 1, "pastel", "snack", 2, 2, 360, 35, 1, false, false,
            Need("nutrifruit", 2, new RecipeCondition(RecipeProperty.SmallOrStandardNutrifruit, 1))),
        Make("F43", "bloomberry_jam", false, 2, "pastel", "dessert", 2, 2, 225, 33, 2, false, false,
            Need("wine_berry", 2, new RecipeCondition(RecipeProperty.RipeGlowberry, 1)), Need("processed_juice")),
        Make("F44", "meal_cap_cream_soup", true, 2, "davre", "soup", 2, 2, 350, 31, 2, false, false,
            Need("edible_mushroom"), Need("processed_milk")),
        Make("F45", "fish_cream_soup", true, 1, "davre", "soup", 2, 2, 450, 40, 2, true, false,
            Need("fish_fillet", 2, new RecipeCondition(RecipeProperty.SmallFishFillet, 1)), Need("processed_milk")),
        Make("F46", "pan_fried_fish", false, 2, "davre", "seafood", 2, 1, 1650, 130, 1, true, false,
            Need("fish_fillet", 1, new RecipeCondition(RecipeProperty.StandardFishFillet, 1)), Need("fat_meat")),
        Make("F47", "thick_cut_fish_duo", false, 3, "nineorbits", "seafood", 3, 2, 700, 195, 2, true, false,
            Need("large_fish_fillet"), Need("nutrifruit", 1, new RecipeCondition(RecipeProperty.LargeOrHugeNutrifruit, 1)), Need("edible_mushroom")),
        Make("F48", "fillerweed_meat_patty", false, 1, "davre", "meat", 2, 1, 450, 35, 2, true, false,
            Need("small_raw_meat"), Need("fillerweed")),
        Make("F49", "mushroom_cream_steak", false, 3, "davre", "meat", 3, 2, 975, 133, 2, true, false,
            Need("raw_meat"), Need("edible_mushroom"), Need("processed_milk")),
        Make("F50", "galaxy_duo_punch", true, 1, "pastel", "drink", 1, 2, 450, 30, 2, false, false,
            Need("galaxy_blend"), Need("processed_juice")),
        Make("F51", "nightshift_jelly", true, 2, "starnest", "jelly", 2, 1, 450, 63, 1, false, false,
            Need("galaxy_blend"), Need("caffeine_pill")),
        Make("F52", "antiseptic_cotton_pack", true, 2, "", "medical", 1, 1, -1, 32, 2, false, false,
            Need("kotton_fabric"), Need(FoodDefinitions.RubbingAlcoholId)),
        Make("F53", "nomore_care_kit", true, 3, "", "medical", 2, 2, -1, 0, 1, false, false,
            Single("pink_alt_injector"), Single("bandage_item"), Single(FoodDefinitions.RubbingAlcoholId))
    };

    internal static readonly IReadOnlyList<Product> MedicalKitProducts = new Product[]
    {
        Kit("F54", FoodDefinitions.BasicMedicalKitId, 1, FoodDefinitions.BasicMedicalPool),
        Kit("F55", FoodDefinitions.AdvancedMedicalKitId, 2, FoodDefinitions.AdvancedMedicalPool),
        Kit("F56", FoodDefinitions.RestrictedMedicalKitId, 3, FoodDefinitions.RestrictedMedicalPool)
    };

    internal static IEnumerable<Product> AllProducts => Products.Concat(FarmAndPharmacyProducts).Concat(MedicalKitProducts);

    private static Product Kit(string code, string id, int stars, string pool)
    {
        var item = new ItemDefinition("X_" + code, id, ItemKind.Medical, 2, 2, 0, "processing", "processed_meat",
            Category: "medical");
        var recipe = new RecipeDefinition(code, Id("recipe." + id.Substring(FoodDefinitions.IdPrefix.Length + 1)),
            Id("food_processor"), [Need(pool, FoodDefinitions.MedicalKitMinimum, new RecipeCondition(RecipeProperty.SingleItem, 1))],
            id, 1, OutputRule.Fixed, StarMarkup(stars), stars);
        return new(item, recipe);
    }

    internal static ItemDefinition Decorate(ItemDefinition item)
    {
        if (item.Kind is not (ItemKind.Food or ItemKind.Drink)) return item;
        return item with
        {
            Brand = item.Code switch
            {
                "D02" or "D04" => "pastel", "D05" => "stardeer", "E01" => "tang",
                "E14" => "nineorbits", "E19" => "beis", "E21" => "starnest", "D09" => "redimp", _ => "davre"
            },
            Category = item.Code switch
            {
                "D02" or "E07" or "E21" => "supply", "D04" => "drink", "D05" => "coffee", "D09" => "alcohol",
                "E01" => "noodles", "E14" => "luxury", "E15" => "canned", "E19" => "dessert",
                "E08" or "E20" or "E22" => "seafood", _ => "meat"
            },
            IsAlcohol = item.Code == "D09", IsTreat = item.Code is "E19" or "E21"
        };
    }

    internal static string Brand(RecipeDefinition recipe) => FoodDefinitions.Items.FirstOrDefault(item => item.Id == recipe.OutputItemId)?.Brand ??
        (recipe.OutputItemId switch
        {
            FoodDefinitions.NativeInstantRamenId => "tang",
            FoodDefinitions.NativeSodaId or FoodDefinitions.NativeEnergyDrinkId => "",
            _ => FoodDefinitions.IsNativeConsumable(recipe.OutputItemId) ? "" : "davre"
        });
    internal static string Category(RecipeDefinition recipe) => FoodDefinitions.Items.FirstOrDefault(item => item.Id == recipe.OutputItemId)?.Category ??
        (recipe.OutputItemId switch
        {
            FoodDefinitions.NativeSodaId => "soda",
            FoodDefinitions.NativeEnergyDrinkId => "drink",
            FoodDefinitions.ProcessedMeatId => "canned",
            FoodDefinitions.IndustrialFishFeedId => "feed",
            _ => FoodDefinitions.IsNativeConsumable(recipe.OutputItemId) ? "medical" : "basic"
        });
}
