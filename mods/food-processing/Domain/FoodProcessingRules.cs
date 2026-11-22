using Nicokobo.FoodProcessing.Definitions;

namespace Nicokobo.FoodProcessing.Domain;

public static class FoodProcessingRules
{
    public const string CalorieTag = "CALORIE_VALUE_TAG";

    public static int CaloriesPerOutput(IEnumerable<(int Count, int Calories)> inputs, int outputCount)
    {
        if (outputCount < 1) throw new ArgumentOutOfRangeException(nameof(outputCount));
        return checked((int)(TotalCalories(inputs) / outputCount));
    }

    public static long TotalCalories(IEnumerable<(int Count, int Calories)> inputs)
    {
        long total = 0;
        foreach (var input in inputs)
        {
            if (input.Count < 1 || input.Calories < 0) throw new ArgumentOutOfRangeException(nameof(inputs));
            total = checked(total + (long)input.Count * input.Calories);
        }
        return total;
    }

    public static long Share(long total, int count, int index) => total / count + (index < total % count ? 1 : 0);

    public static IReadOnlyList<string> ItemTypes(ItemDefinition item)
    {
        if (item.Kind == ItemKind.Medical) return item.IsLuxury ? ["MEDICAL", "LUXURY_ITEM"] : ["MEDICAL"];
        if (item.Kind is not (ItemKind.Food or ItemKind.Drink)) return Array.Empty<string>();
        var types = new List<string> { "FOOD", "PROCESSED_FOOD" };
        if (item.Kind == ItemKind.Drink) types.Add("BREVAGE");
        if (item.IsTreat || item.Id == FoodDefinitions.GalaxySundae.Id ||
            item.Id == FoodDefinitions.SupplyJelly.Id) types.Add("TREAT");
        if (item.IsAlcohol || item.Id == FoodDefinitions.StarportFizz.Id) { types.Add("ALCOHOL"); types.Add("SUBSTANCE"); }
        if (item.Id == FoodDefinitions.CompressedNutrientLiquid.Id) types.Add("MEDICAL");
        if (item.IsLuxury) types.Add("LUXURY_ITEM");
        return types;
    }

    public static bool IsRawMeat(string itemId) => itemId is "raw_meat" or "small_raw_meat";

    public static IReadOnlyList<string> ItemInputIds(RecipeRequirement input)
    {
        if (input.ResourceId == "fish_fillet")
        {
            if (input.Conditions.Any(condition => condition.Property == RecipeProperty.SmallFishFillet))
                return ["tiny_fish_fillet", "small_fish_fillet"];
            if (input.Conditions.Any(condition => condition.Property == RecipeProperty.StandardFishFillet))
                return ["fish_fillet"];
        }
        return ItemInputIds(input.ResourceId);
    }

    public static IReadOnlyList<string> ItemInputIds(string resourceId) => resourceId switch
    {
        "fish_fillet" => ["tiny_fish_fillet", "small_fish_fillet", "fish_fillet", "large_fish_fillet"],
        "eel_fillet" => ["small_eel_fillet", "eel_fillet"],
        "edible_mushroom" => ["meal_cap", "dream_cap"],
        "small_meat_cut" => ["morsel", "small_morsel"],
        "alcohol_base" => ["red_beer", "nudka", "wine_bottle"],
        FoodDefinitions.BasicMedicalPool => ["kotton_fiber", FoodDefinitions.RubbingAlcoholId, "bandage_item", "hemostatic_bandage_item",
            "topical_bandage_item", "birth_control_pill", FoodDefinitions.BloodBagId, "smelling_salt"],
        FoodDefinitions.AdvancedMedicalPool => [FoodDefinitions.ImmunivaxId, "salve", FoodDefinitions.BloodBagId, "common_medical",
            "pink_alt_injector", "tilapia_skin_graft", "hemostatic_bandage_item"],
        FoodDefinitions.RestrictedMedicalPool => ["caffeine_pill", "fanta_pill", "dream_dust", "nightmare_dust", "zyanide_pill",
            "phagimycin_pill", "unlicensed_phagimycin_pill", "black_injector", FoodDefinitions.BlueBloodBagId],
        _ => [resourceId]
    };

    internal static bool IsMedicalPool(string id) => id is FoodDefinitions.BasicMedicalPool or FoodDefinitions.AdvancedMedicalPool or FoodDefinitions.RestrictedMedicalPool;

    public static int MinimumWaterQuality(RecipeRequirement input) =>
        input.Conditions.FirstOrDefault(condition => condition.Property == RecipeProperty.WaterQuality)?.MinimumValue ?? 1;

}
