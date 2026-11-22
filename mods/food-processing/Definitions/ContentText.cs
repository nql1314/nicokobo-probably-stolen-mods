using Nicokobo.ModUtilities;

namespace Nicokobo.FoodProcessing.Definitions;
public static class ContentText
{
    public static IReadOnlyDictionary<string, (string Chinese, string English)> InterfaceText { get; } =
        new[] { "food.ingredient_restriction", "food.finished_wine", "food.usable_berry", "food.select_recipe",
            "food.auto_selection", "food.recipe_label", "food.automatic", "food.unavailable", "food.clean_input",
            "food.small_or_standard_crop", "food.large_or_huge_crop", "food.ripe_bloomberry", "food.unstacked_ingredient" }
        .ToDictionary(key => key, Pair, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> NativeIngredientNameKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kotton_fabric"] = "food.kotton_fabric",
            ["kotton_fiber"] = "food.kotton_fiber",
            ["bandage_item"] = "food.basic_bandage",
            ["hemostatic_bandage_item"] = "food.hemostatic_bandage",
            ["topical_bandage_item"] = "food.topical_bandage",
            ["salve"] = "food.nightingale_salve",
            ["smelling_salt"] = "food.smelling_salts",
            ["unlicensed_phagimycin_pill"] = "food.unlicensed_phagimycin_pill",
            ["pink_alt_injector"] = "food.nomore_injector",
            ["blood_bag"] = "food.blood_bag",
            ["large_purple_injector"] = "food.genuine_immunivax",
            ["blue_blood_bag"] = "food.blue_blood_bag",
            ["serum_green"] = "food.livestock_immunity_serum",
            ["serum_red"] = "food.livestock_steroid_serum",
            ["rubbing_alcohol"] = "food.rubbing_alcohol",
            ["common_medical"] = "food.common_medical_supplies",
            ["common_chemical"] = "food.common_chemical_supplies",
            ["water"] = "food.water",
            ["BIO_PRINTABLE_TAG"] = "food.raw_meat_small_large",
            ["nutrifruit"] = "food.nutrifruit",
            ["galaxy_blend"] = "food.galaxy_blend",
            ["cat_bar"] = "food.cat_chocolate_bar",
            ["li_eat_snackbar"] = "food.li_eat_snack_bar",
            ["beis_icecream"] = "food.bei_s_vanilla_ice_cream",
            ["processed_milk"] = "food.pilk",
            ["processed_cheese"] = "food.processed_cheese",
            ["cup_noodle"] = "food.instant_ramen",
            ["coffee_bean"] = "food.coffee_beans",
            ["caffeine_pill"] = "food.caffeine_pill",
            ["energy_drink"] = "food.energy_drink",
            ["raw_meat"] = "food.raw_meat_standard",
            ["small_raw_meat"] = "food.raw_meat_small",
            ["morsel"] = "food.morsel",
            ["small_morsel"] = "food.small_morsel",
            ["fat_meat"] = "food.animal_fat",
            ["processed_meat"] = "food.peat",
            ["tiny_fish_fillet"] = "food.tiny_fish_fillet",
            ["small_fish_fillet"] = "food.small_fish_fillet",
            ["fish_fillet"] = "food.fish_fillet_any_size",
            ["large_fish_fillet"] = "food.large_fish_fillet",
            ["small_eel_fillet"] = "food.small_eel_fillet",
            ["eel_fillet"] = "food.eel_fillet_small_standard",
            ["fish_scrap"] = "food.fish_scrap",
            ["caviar"] = "food.caviar",
            ["industrial_fish_feed"] = "food.industrial_fish_feed",
            ["sturgeon_roe"] = "food.sturgeon_roe",
            ["meal_cap"] = "food.meal_cap",
            ["dream_cap"] = "food.edible_dream_cap",
            ["fillerweed"] = "food.fillerweed",
            ["soda_red"] = "food.soda",
            ["processed_juice"] = "food.puice",
            ["red_beer"] = "food.red_imp_beer",
            ["nudka"] = "food.nudka",
            ["wine_bottle"] = "food.finished_homebrew_wine",
            ["wine_yeast"] = "food.regular_yeast",
            ["wine_berry"] = "food.glowberry",
            ["bottle_hot_sauce"] = "food.hot_sauce",
            ["edible_mushroom"] = "food.edible_mushroom_meal_cap_edible_dream_cap",
            ["small_meat_cut"] = "food.small_meat_cut_morsel_small_morsel",
            ["alcohol_base"] = "food.alcohol_base_red_imp_beer_nudka_finished",
        };
    private static (string Chinese, string English) Pair(string key) =>
        (ModText.GetFor(FoodDefinitions.ModId, key, "zh"), ModText.GetFor(FoodDefinitions.ModId, key, "en"));

    public static (string Chinese, string English) Description(ItemDefinition item) =>
        Pair(item.Id + ".description");

    public static (string Chinese, string English) FlavorText(ItemDefinition item) =>
        Pair(item.Id + ".flavor");

    public static string Name(string id, bool english)
    {
        var item = FoodDefinitions.Items.FirstOrDefault(value => value.Id == id);
        if (item != null) return english ? item.EnglishName : item.ChineseName;
        return NativeIngredientNameKeys.TryGetValue(id, out var key)
            ? ModText.GetFor(FoodDefinitions.ModId, key, english ? "en" : "zh") : id;
    }

    internal static string LocalizedNativeName(string id) =>
        NativeItemText.Name(id, NativeIngredientNameKeys.TryGetValue(id, out var key) ? FoodText.Get(key) : id);
}
