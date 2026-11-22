using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing.Definitions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nicokobo.FoodProcessing;

// Presentation order may change; Rank always refers to the machine's attempt order.
internal sealed class FoodRecipeEntry
{
    internal RecipeView Recipe { get; }
    internal int Rank { get; }
    internal string Name { get; }
    internal string Description { get; }
    internal string Requirements { get; }
    internal int? Markup { get; }
    internal int Stars { get; }
    internal int RequiredQuality { get; }
    internal string Category { get; }
    internal string Brand { get; }
    internal bool IsFood => FoodDefinitions.IsFoodOutput(Recipe.OutputItemId);

    internal FoodRecipeEntry(RecipeView recipe, int rank)
    {
        Recipe = recipe; Rank = rank;
        var item = RecipePresentation.ReadItemDetails(recipe.OutputItemId, includeSprite: false);
        Name = RecipePresentation.Output(recipe, item.Name);
        Description = item.Description;
        var definition = FoodDefinitions.Recipes.FirstOrDefault(value => value.Id == recipe.Id);
        Requirements = definition != null
            ? string.Join(" + ", definition.Requirements.Select(FoodInterop.LocalizedRequirement))
            : string.Join(" + ", recipe.Ingredients.Select(RecipePresentation.Ingredient));
        Markup = definition != null ? RecipeConfiguration.RecipeMarkup(definition) : null;
        Stars = definition?.Stars ?? 0; RequiredQuality = definition?.RequiredQuality ?? 0;
        Category = definition == null ? "" : FoodExpansion.Category(definition);
        Brand = definition == null ? "" : FoodExpansion.Brand(definition);
    }

    internal bool Matches(string query) => query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
        .All(word => new[] { Name, Requirements, Recipe.Id, Recipe.OutputItemId, FoodText.Get("food.category." + Category), Brand.Length == 0 ? "" : FoodText.Get("food.brand." + Brand) }
            .Any(value => value.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0));

    private string Caption => (Stars == 0 ? "" : new string('★', Stars) + " · " + FoodText.Get("food.quality_requirement", RequiredQuality) + " · ") + FoodText.Get("food.priority_number", Rank) +
        (Markup.HasValue ? " · " + FoodText.Get("food.markup", Markup.Value) : "");

    internal string TileTitle => Stars > 0 ? new string('★', Stars) + " " + Name : Name;

    internal string Details(string? value = null) => string.Join("\n", new[]
    {
        $"{Name} ×{Recipe.OutputCount}", Caption, Requirements, value,
        Brand.Length == 0 ? "" : FoodBrands.Label(Brand), Description
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    internal static FoodRecipeEntry[] ForMachine(string machineId) => FoodProduction.RecipeChoices(machineId)
        .Select((recipe, index) => new FoodRecipeEntry(recipe, index + 1)).ToArray();

    internal static IEnumerable<FoodRecipeEntry> Sort(IEnumerable<FoodRecipeEntry> entries, bool byMarkup) => byMarkup
        ? entries.OrderByDescending(entry => entry.Markup ?? -1).ThenBy(entry => entry.Rank)
        : entries.OrderBy(entry => entry.Rank);
}

internal static class FoodRecipeGrid
{
    internal const int Columns = 4;
    private const float CellWidth = 170, CellHeight = 168, Spacing = 6;
    internal const float RowHeight = CellHeight + Spacing;

    internal static void Begin(CustomUIBuilder builder) => builder.BeginGrid(Columns, CellWidth, CellHeight, Spacing);

    internal static CustomUIElement AddTile(CustomUIBuilder builder, FoodRecipeEntry entry, Sprite? sprite, bool selectionStyle = false)
    {
        builder.BeginPanel(CellWidth, CellHeight, TextAnchor.UpperCenter, padding: 8, spacing: 2);
        var tile = builder.Last;
        var icon = builder.AddImage(sprite, 64, 64).Last;
        icon.SetFixedWidth(64);
        icon.image.preserveAspect = true;
        icon.image.raycastTarget = false;
        icon.image.enabled = sprite != null;
        var iconLayout = icon.root.GetComponent<LayoutElement>();
        iconLayout.minHeight = iconLayout.preferredHeight = 64;
        iconLayout.flexibleHeight = 0;
        var lines = selectionStyle
            ? new[] { (Text: entry.Name, Height: 54f), (Text: new string('★', entry.Stars), Height: 30f) }
            : new[] { (Text: new string('★', entry.Stars), Height: 30f), (Text: entry.Name, Height: 54f) };
        foreach (var line in lines)
        {
            var label = builder.AddLabel(line.Text).Last;
            label.text.alignment = TextAlignmentOptions.Top;
            label.text.fontSize = 18;
            label.text.enableWordWrapping = line.Height == 54f;
            label.text.overflowMode = line.Height == 54f ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            label.text.raycastTarget = false;
            var layout = label.root.GetComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = line.Height;
            layout.flexibleHeight = 0;
            if (line.Height == 54f) tile.text = label.text;
            else if (selectionStyle) label.text.color = MachineryStyle.Hex("e4bd60");
        }
        builder.End();
        return tile;
    }
}
