using Nicokobo.FoodProcessing.Definitions;
using Nicokobo.FoodProcessing.Domain;

namespace Nicokobo.FoodProcessing;

internal static class FoodIngredients
{
    internal static string? Rejection(GameItem item)
    {
        if (GutterflowPelletHelper.IsTainted(item)) return FoodText.Get("food.tainted_ingredient");
        if (item.IsTag("ANIMAL_PRION_TAG")) return FoodText.Get("food.prion_tainted_meat");
        if (item.IsTag("FRESH_FOOD_TAG") && item.GetTagReadonly("FRESHNESS_TAG").GetInt() <= 0)
            return FoodText.Get("food.rotten_ingredient");
        if (item.identifier == "wine_bottle" && (!item.IsTag("WINE_CONTAINER_TAG") ||
            item.GetTagReadonly("WINE_CONTAINER_STATE").GetInt() != 2 || item.IsTag("WINE_COUNTERFEIT_TAG") ||
            !item.IsGameItemType("ALCOHOL")))
            return FoodText.Get("food.finished_drinkable_genuine_homebrew_wine_is_required");
        if (item.identifier is "meal_cap" or "dream_cap" or "fillerweed")
        {
            if (!item.IsGameItemType("FOOD") || item.IsTag("CONTRABAND_ITEM_TAG") || item.IsGameItemType("CONTRABAND"))
                return FoodText.Get("food.an_edible_non_contraband_crop_is_required");
        }
        return null;
    }

    internal static bool Matches(GameItem item, RecipeRequirement input, Func<GameItem, bool> genuine)
    {
        if (Rejection(item) != null) return false;
        if (input.Kind == RecipeResourceKind.TaggedItem ? !item.IsTag(input.ResourceId) :
            !FoodProcessingRules.ItemInputIds(input).Contains(item.identifier)) return false;
        if (input.ResourceId == FoodDefinitions.AdvancedMedicalPool && item.identifier == FoodDefinitions.ImmunivaxId && !genuine(item))
            return false;
        return input.Conditions.All(condition => condition.Property switch
        {
            RecipeProperty.GenuineInjector => genuine(item),
            RecipeProperty.WineScore => item.GetTagReadonly("WINE_SCORE_INT").GetInt() >= condition.MinimumValue,
            RecipeProperty.UsableGlowberry => GlowberryDrinkRules.CanUse(BerryCondition(item)),
            RecipeProperty.RipeGlowberry => GlowberryDrinkRules.FromNativeCondition(BerryCondition(item)) is GlowberryGrade.Normal or GlowberryGrade.Small,
            RecipeProperty.RawMeat => FoodProcessingRules.IsRawMeat(item.identifier),
            RecipeProperty.SmallOrStandardNutrifruit => item.spritePath is "nutrifruit_s" or "nutrifruit",
            RecipeProperty.LargeOrHugeNutrifruit => item.spritePath is "nutrifruit_l" or "nutrifruit_xl",
            RecipeProperty.SmallFishFillet or RecipeProperty.StandardFishFillet => true,
            RecipeProperty.SingleItem => item.unitCount == 1,
            _ => false
        });
    }

    private static int? BerryCondition(GameItem item) =>
        item.identifier == "wine_berry" && item.IsTag("BERRY_TAG") && item.IsTag(GlowberryDrinkRules.ConditionTag)
            ? item.GetTagReadonly(GlowberryDrinkRules.ConditionTag).valueInt : null;
}
