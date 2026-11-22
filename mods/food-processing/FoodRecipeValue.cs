using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing.Definitions;
using Nicokobo.FoodProcessing.Domain;
using Nicokobo.ModUtilities;

namespace Nicokobo.FoodProcessing;

internal static class FoodRecipeValue
{
    internal sealed record Quote(long First, long Last, long Total);

    internal static decimal WaterValue(int parts, decimal factor) => checked(parts / 25000m * factor);

    internal static long CalculateBatch(RecipeDefinition recipe, decimal materials, int basePower) =>
        checked((long)decimal.Ceiling(materials * (100m + RecipeConfiguration.RecipeMarkup(recipe)) / 100m + basePower));

    internal static long PortionBasis(GameItem product, long batch, long calories, int count, int index)
    {
        if (product.identifier == FoodDefinitions.IndustrialFishFeedId)
            IndustrialFishFeedHelper.SetKcal(product, checked((int)FoodProcessingRules.Share(calories, count, index)));
        long share = FoodProcessingRules.Share(batch, count, index);
        return ModSourceHelper.GetItemModName(product) == null ? Math.Max(share, product.unitValue) : share;
    }

    internal static Quote? ReadOutputs(RecipeDefinition recipe, long batch, long calories)
    {
        int count = recipe.BaseOutputCount;
        if (FoodDefinitions.Items.Any(item => item.Id == recipe.OutputItemId))
            return new(FoodProcessingRules.Share(batch, count, 0), FoodProcessingRules.Share(batch, count, count - 1), batch);
        var product = DirectoryMaster.Create<GameItem>(recipe.OutputItemId);
        if (product == null) return null;
        try
        {
            long first = PortionBasis(product, batch, calories, count, 0);
            long last = first, total = first;
            for (int index = 1; index < count; index++)
            {
                last = PortionBasis(product, batch, calories, count, index);
                total = checked(total + last);
            }
            return new(first, last, total);
        }
        finally { ItemGraphCleanup.Destroy(product); }
    }

    internal static Quote? Reference(RecipeView view)
    {
        var recipe = FoodProduction.DefinitionFor(view);
        if (FoodDefinitions.IsPooledMedicalKit(recipe.OutputItemId)) return null;
        decimal materials = 0;
        long calories = 0;
        var inputs = recipe.ItemInputs;
        for (int index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var matches = view.Ingredients[index].Matches;
            if (ReadIngredient(input, view is ExtraRecipe
                ? item => FoodProduction.AcceptsIngredient(item) && matches(item)
                : matches) is not { } sample) return null;
            materials += sample.Value * input.Amount;
            calories += (long)sample.Calories * input.Amount;
        }

        var (parts, quality) = FoodProduction.WaterNeeds(recipe);
        if (parts > 0)
        {
            var water = quality switch
            {
                3 => ItemConditionList.CreatePureWater(),
                2 => ItemConditionList.CreateHighQualityWater(),
                _ => ItemConditionList.CreateBaseWater()
            };
            materials += WaterValue(parts, 1m + water.modValue / 100m);
        }
        if (recipe.OutputItemId == FoodDefinitions.IndustrialFishFeedId && calories < recipe.BaseOutputCount) return null;
        long batch = CalculateBatch(recipe, materials, FoodDefinitions.Machine(recipe.MachineId).BasePowerCost);
        return ReadOutputs(recipe, batch, calories);
    }

    private static (decimal Value, int Calories)? ReadIngredient(RecipeRequirement input, Func<GameItem, bool> matches)
    {
        var ids = input.Kind == RecipeResourceKind.TaggedItem
            ? new[] { "small_raw_meat", "raw_meat" }
            : FoodProcessingRules.ItemInputIds(input);
        foreach (string id in ids)
        {
            var sample = DirectoryMaster.Create<GameItem>(id);
            if (sample == null) continue;
            try
            {
                if (input.Conditions.Any(condition => condition.Property == RecipeProperty.LargeOrHugeNutrifruit))
                {
                    // PreBuiltItemHelper.CreateNutrifruitLarge assigns a player item ID.
                    sample.spritePath = "nutrifruit_l";
                    sample.unitValue = 18;
                    sample.ModifyTag(FoodProcessingRules.CalorieTag, tag => tag.Enable().SetInt(384));
                }
                else if (id == "wine_berry") WineHelper.SetBerryCondition(sample, 1);
                else if (id == "wine_bottle")
                {
                    int score = input.Conditions.FirstOrDefault(condition => condition.Property == RecipeProperty.WineScore)?.MinimumValue ?? 3;
                    sample.ModifyTag("WINE_SCORE_INT", tag => tag.Enable().SetInt(score));
                    sample.DisableTag("LIQUID_CONTAINER_TAG");
                    WineHelper.FinishFermentation(sample);
                }
                if (matches(sample))
                    return (FoodProduction.InputValue(sample), sample.GetTagReadonly(FoodProcessingRules.CalorieTag).GetInt());
            }
            finally { ItemGraphCleanup.Destroy(sample); }
        }
        return null;
    }
}
