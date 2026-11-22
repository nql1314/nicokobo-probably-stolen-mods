namespace MechcoreProtocol.SynthesisExpansion.Definitions;

/// <summary>
/// A glass input and its material-refiner recipe.
/// </summary>
public sealed record GlassFeedstock(RecipeDefinition Recipe)
{
    public RecipeRequirement Input => Recipe.ItemInputs.Single();
    public string ItemId => Input.ResourceId;
    public int RequiredCount => Input.Amount;
}

/// <summary>
/// Glass inputs cannot be mixed to meet a single batch requirement.
/// </summary>
public static class GlassRecycling
{
    internal static IReadOnlyList<GlassFeedstock> DefaultFeedstocks { get; } =
        Array.AsReadOnly(new[]
        {
            Feedstock("furnace_glass_bottle", "empty_beer_bottle", SynthesisNumbers.Glass.BeerBottleInputCount, SynthesisNumbers.Glass.BeerBottleOutputCount),
            Feedstock("furnace_glass_shiv", "glass_shard_shiv", SynthesisNumbers.Glass.GlassShivInputCount, SynthesisNumbers.Glass.GlassShivOutputCount),
            Feedstock("furnace_glass_shard", "glass_shard", SynthesisNumbers.Glass.GlassShardInputCount, SynthesisNumbers.Glass.GlassShardOutputCount),
            Feedstock("furnace_wine_bottle", "wine_bottle", SynthesisNumbers.Glass.WineBottleInputCount, SynthesisNumbers.Glass.WineBottleOutputCount)
        });

    public static IReadOnlyList<GlassFeedstock> Feedstocks { get; private set; } =
        DefaultFeedstocks;

    internal static void UseRecipes(IReadOnlyList<RecipeDefinition> recipes) =>
        Feedstocks = Array.AsReadOnly(recipes.Select(recipe =>
            new GlassFeedstock(recipe)).ToArray());

    internal static bool IsRecipe(string id) =>
        DefaultFeedstocks.Any(feedstock => feedstock.Recipe.Id == id);

    private static GlassFeedstock Feedstock(string code, string itemId,
        int count, int outputCount) => new(new RecipeDefinition(code,
            SynthesisDefinitions.OwnerId + ".recipe." + code,
            SynthesisDefinitions.RefinerItem.Id,
            Array.AsReadOnly(new[]
            {
                new RecipeRequirement(RecipeResourceKind.Item, itemId, count,
                    RecipeAmountBasis.PerBatch,
                    Array.Empty<RecipeCondition>())
            }),
            SynthesisDefinitions.Quartz.Id, outputCount, OutputRule.Fixed));

    public static bool IsGlassInput(string? id) =>
        id != null && Feedstocks.Any(feedstock => feedstock.ItemId == id);

    public static GlassFeedstock? Select(IReadOnlyDictionary<string, int> counts) =>
        Feedstocks.FirstOrDefault(feedstock =>
            counts.TryGetValue(feedstock.ItemId, out int count) &&
            count >= feedstock.RequiredCount);
}
