using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing.Definitions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nicokobo.FoodProcessing;

internal static class FoodGuide
{
    private static CustomUIWindow? _window;
    internal static void Close() { _window?.Close(); _window = null; }

    internal static void Open()
    {
        Close();
        if (CustomUIManager.Instance == null) return;
        var ui = CustomUIManager.Window(FoodDefinitions.ModId + ".guide",
            FoodText.Name(FoodDefinitions.FoodProcessingGuideItem), CustomUIManager.LAYER_PAPER)
            .SetSize(760, 680).SetCloseOnEscape().UseMachineryStyle();
        string?[] groups = [FoodDefinitions.FoodProcessorItem.Id, FoodDefinitions.InfusionTowerItem.Id, null];
        var entries = groups.OfType<string>().SelectMany(FoodRecipeEntry.ForMachine).ToArray();
        var available = entries.ToDictionary(entry => entry.Recipe.Id, _ => false);
        var icons = entries.Select(entry => entry.Recipe.OutputItemId).Distinct()
            .ToDictionary(id => id, id => RecipePresentation.ReadItem(id).Sprite);
        var pages = new List<(RectTransform Root, string? Machine)>();
        string query = "";
        bool byMarkup = true;
        int stars = 0;
        void Render()
        {
            foreach (var page in pages)
            {
                CustomUIBuilder.ClearContainer(page.Root);
                var body = new CustomUIBuilder(CustomUIManager.Instance, ui.Handle, page.Root);
                body.AddLabel(page.Machine == null
                    ? FoodText.Get("food.native_uses_charges_and_pricing_are_preserved").Trim()
                    : FoodText.Get("food.nutrition_note"));
                var matching = entries.Where(entry => (page.Machine == null
                    ? !entry.IsFood : entry.IsFood && entry.Recipe.MachineId == page.Machine) &&
                    (stars == 0 || entry.Stars == stars) && entry.Matches(query));
                var sorted = FoodRecipeEntry.Sort(matching, byMarkup).ToArray();
                if (sorted.Length == 0)
                    body.AddLabel(FoodText.Get("food.no_matches"));
                else
                {
                    FoodRecipeGrid.Begin(body);
                    foreach (var entry in sorted)
                    {
                        var tile = FoodRecipeGrid.AddTile(body, entry, icons[entry.Recipe.OutputItemId], selectionStyle: true);
                        tile.image.sprite = null;
                        tile.image.color = Color.clear;
                        var icon = tile.root.GetComponentsInChildren<Image>().First(image => image != tile.image);
                        icon.color = new Color(1, 1, 1, available[entry.Recipe.Id] ? 1f : 0.3f);
                        string? tooltip = null;
                        tile.root.AddComponent<FoodRecipeTooltip>().Build = () => tooltip ??= Tooltip(entry);
                        tile.SetTooltip("");
                    }
                    body.End();
                }
                CustomUIBuilder.RebuildLayout(page.Root);
                if (page.Root.GetComponentInParent<ScrollRect>() is { } scroll) scroll.verticalNormalizedPosition = 1;
            }
        }

        void Dropdown(string[] labels, float width, Action<int> change, string? tooltip = null)
        {
            var control = ui.AddDropdown(labels, onChanged: change).Last.SetFixedWidth(width);
            if (control.dropdown?.captionText is { } caption)
            {
                caption.enableWordWrapping = false;
                caption.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (tooltip != null) control.SetTooltip(tooltip);
        }

        ui.AddLabel(FoodText.Get("food.guide_processing_summary"));
        ui.AddLabel(FoodText.Get("food.guide_selection_summary"))
            .Last.SetTooltip(FoodText.Get("food.priority_hint"));
        var scanStatus = ui.AddLabel(FoodText.Get("food.scanning_stock")).Last;
        ui.BeginRow(spacing: 6, alignment: TextAnchor.MiddleLeft);
        ui.AddInput(FoodText.Get("food.search_recipes_short"), onTyping: value => { query = value ?? ""; Render(); })
            .Last.SetFixedWidth(350).SetTooltip(FoodText.Get("food.search_recipes"));
        Dropdown(new[] { FoodText.Get("food.sort_markup"), FoodText.Get("food.sort_priority") },
            224, index => { byMarkup = index == 0; Render(); }, FoodText.Get("food.priority_rules"));
        Dropdown(new[] { FoodText.Get("food.all_stars"), "★", "★★", "★★★" }, 132, index => { stars = index; Render(); });
        ui.End();
        ui.BeginTabs();
        foreach (string? machine in groups)
        {
            ui.BeginTab(machine == null ? FoodText.Get("food.supplies_tab") : FoodText.Name(FoodDefinitions.Item(machine)));
            ui.BeginScrollFlexible(180);
            pages.Add((ui.CurrentContainer, machine));
            ui.End(); ui.End();
        }
        ui.EndTabs();
        ui.AddButton(LocHelper.GetLocalizedMainMenu("close"), Close);
        Render();
        _window = ui.Show();
        _window.root.AddComponent<FoodRecipeAvailability>().Refresh(entries, null, result =>
        {
            available = result;
            scanStatus.SetVisible(false);
            Render();
        });
    }

    private static string Tooltip(FoodRecipeEntry entry)
    {
        // Some native ingredient factories roll variants when creating a prototype.
        var randomState = UnityEngine.Random.state;
        FoodRecipeValue.Quote? quote;
        try { quote = FoodRecipeValue.Reference(entry.Recipe); }
        finally { UnityEngine.Random.state = randomState; }
        string value;
        if (quote == null)
            value = FoodText.Get("food.guide_value_unavailable");
        else if (entry.Recipe.OutputCount == 1)
            value = FoodText.Get("food.guide_value", quote.First);
        else
        {
            string unitValue = quote.First == quote.Last ? quote.First.ToString() : $"{quote.Last}–{quote.First}";
            value = FoodText.Get("food.guide_value_batch", unitValue, quote.Total);
        }
        string details = entry.Details(value);
        if (!entry.IsFood) details += "\n" + FoodText.Name(FoodDefinitions.Item(entry.Recipe.MachineId));
        if (entry.Recipe.OutputItemId == FoodDefinitions.IndustrialFishFeedId)
            details += "\n" + FoodText.Get("food.native_feed_uses_actual_calories_value_1").Trim();
        return details;
    }
}
