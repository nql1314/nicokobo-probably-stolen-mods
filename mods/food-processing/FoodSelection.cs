using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing.Definitions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nicokobo.FoodProcessing;

internal static class FoodSelection
{
    private static CustomUIWindow? _window;
    internal static void Close() { _window?.Close(); _window = null; }
    internal static string Label(GameItem machine)
    {
        string? selected = machine.GetTagReadonly(FoodProduction.SelectionTag).GetString();
        if (string.IsNullOrEmpty(selected)) return Text("recipe_label", Text("automatic"));
        var recipe = FoodProduction.RecipeChoices(machine.identifier).FirstOrDefault(value => value.Id == selected);
        return Text("recipe_label", recipe == null ? Text("unavailable") : RecipePresentation.Output(recipe,
            RecipePresentation.ReadItem(recipe.OutputItemId).Name));
    }

    internal static void Open(GameItem machine, Action refresh)
    {
        Close();
        if (CustomUIManager.Instance == null) return;
        var entries = FoodRecipeEntry.ForMachine(machine.identifier);
        var available = entries.ToDictionary(entry => entry.Recipe.Id, _ => false);
        FoodRecipeAvailability? scanner = null;
        var icons = entries.Select(entry => entry.Recipe.OutputItemId).Distinct()
            .ToDictionary(id => id, id => RecipePresentation.ReadItem(id).Sprite);
        int rows = (entries.Length + FoodRecipeGrid.Columns - 1) / FoodRecipeGrid.Columns;
        var ui = CustomUIManager.Window(FoodDefinitions.ModId + ".selection", Text("select_recipe"), CustomUIManager.LAYER_PAPER)
            .SetSize(760, Math.Clamp(158 + rows * FoodRecipeGrid.RowHeight, 280, 600))
            .SetCloseOnEscape().UseMachineryStyle();
        string selectedId = machine.GetTagReadonly(FoodProduction.SelectionTag).GetString() ?? "";
        var tiles = new List<(FoodRecipeEntry Entry, CustomUIElement Button, Image Icon, Image Border)>();
        int sort = 0;
        bool ready = false;

        ui.BeginRow(spacing: 8);
        var automatic = ui.AddButton(Text("automatic"), null).Last.SetTooltip(Text("priority_rules"));
        var fill = ui.AddButton(Text("fill_ingredients"), null).Last;
        ui.AddDropdown(new[] { Text("sort_priority"), Text("sort_ready"), Text("sort_stars"), Text("sort_markup"), Text("sort_name") },
            onChanged: index => { sort = index; if (ready) UpdateSelection(); }).Last.SetFixedWidth(260);
        ui.End();
        var scanStatus = ui.AddLabel(Text("scanning_stock")).Last;
        ui.BeginScrollFlexible(120);
        FoodRecipeGrid.Begin(ui);
        foreach (var entry in entries)
        {
            var tile = FoodRecipeGrid.AddTile(ui, entry, icons[entry.Recipe.OutputItemId], selectionStyle: true);
            var icon = tile.root.GetComponentsInChildren<Image>().First(image => image != tile.image);
            tile.image.sprite = null;
            tile.image.color = Color.clear;
            // A separate hollow frame marks selection without filling the card.
            var borderRoot = new GameObject("selection", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            borderRoot.transform.SetParent(tile.root.transform, false);
            var borderRect = (RectTransform)borderRoot.transform;
            borderRect.anchorMin = Vector2.zero; borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = borderRect.offsetMax = Vector2.zero;
            borderRoot.GetComponent<LayoutElement>().ignoreLayout = true;
            var border = borderRoot.GetComponent<Image>();
            border.sprite = MachineryStyle.BevelFrame;
            border.type = Image.Type.Sliced;
            border.fillCenter = false;
            border.color = MachineryStyle.Hex("e4bd60");
            border.raycastTarget = false;
            tile.button = tile.root.AddComponent<Button>();
            tile.button.targetGraphic = tile.image;
            tile.button.transition = Selectable.Transition.None;
            tile.root.AddComponent<FoodRecipeTooltip>().Build = () => entry.Details() + "\n" +
                FoodProduction.PreviewForSelection(machine, entry.Recipe, includeRecipeSummary: false);
            tile.SetTooltip("");
            tiles.Add((entry, tile, icon, border));
        }
        ui.End();
        ui.End();
        var status = ui.AddLabel("").Last.SetVisible(false);

        void Select(string id)
        {
            if (id.Length != 0 && !FoodProduction.QualityAllows(machine, id)) return;
            machine.ModifyTag(FoodProduction.SelectionTag, tag => tag.Enable().SetString(id));
            selectedId = id;
            status.SetVisible(false);
            refresh();
            UpdateSelection();
        }
        void FillSelected()
        {
            var entry = entries.FirstOrDefault(value => value.Recipe.Id == selectedId);
            if (entry == null) return;
            string message;
            try { message = FoodRecipeFill.Fill(machine, entry.Recipe); }
            catch (Exception error)
            {
                FoodMod.Warn($"Stopped recipe fill for {entry.Recipe.Id}: {error}");
                message = Text("fill_interrupted");
            }
            status.SetText(message).SetVisible(true);
            refresh();
            scanStatus.SetVisible(true);
            scanner!.Refresh(entries, machine, result =>
            {
                available = result;
                scanStatus.SetVisible(false);
                UpdateSelection();
            });
            UpdateSelection();
        }
        void UpdateSelection()
        {
            StyleButton(automatic, selectedId.Length == 0, true);
            bool canFill = false;
            foreach (var (entry, tile, icon, border) in tiles)
            {
                bool active = entry.Recipe.Id == selectedId;
                bool unlocked = FoodProduction.QualityAllows(machine, entry.Recipe.Id);
                tile.button.interactable = unlocked;
                border.enabled = active;
                tile.text.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
                icon.color = new Color(1, 1, 1, available[entry.Recipe.Id] ? 1f : 0.3f);
                canFill |= active && unlocked;
            }
            StyleButton(fill, false, canFill);
            fill.SetTooltip(canFill ? "" : Text("fill_select_recipe"));
            var ordered = sort switch
            {
                1 => entries.OrderByDescending(entry => available[entry.Recipe.Id]).ThenBy(entry => entry.Rank),
                2 => entries.OrderByDescending(entry => entry.Stars).ThenBy(entry => entry.Rank),
                3 => entries.OrderByDescending(entry => entry.Markup ?? -1).ThenBy(entry => entry.Rank),
                4 => entries.OrderBy(entry => entry.Name, StringComparer.CurrentCulture).ThenBy(entry => entry.Rank),
                _ => entries.OrderBy(entry => entry.Rank).ThenBy(entry => entry.Rank)
            };
            int position = 0;
            foreach (var entry in ordered)
                tiles.First(tile => tile.Entry == entry).Button.root.transform.SetSiblingIndex(position++);
        }

        automatic.OnClick(() => Select(""));
        fill.OnClick(FillSelected);
        foreach (var (entry, tile, _, _) in tiles) tile.OnClick(() => Select(entry.Recipe.Id));
        ready = true;
        UpdateSelection();
        _window = ui.Show();
        scanner = _window.root.AddComponent<FoodRecipeAvailability>();
        scanner.Refresh(entries, machine, result =>
        {
            available = result;
            scanStatus.SetVisible(false);
            UpdateSelection();
        });
    }

    private static void StyleButton(CustomUIElement element, bool selected, bool enabled)
    {
        // The builder's disabled style suppresses pointer events, including tooltip hover.
        element.button.interactable = enabled;
        element.image.sprite = selected ? MachineryStyle.ButtonPressed : MachineryStyle.ButtonNormal;
        element.image.color = enabled ? Color.white : MachineryStyle.BUTTON_DISABLED_TINT;
        element.text.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
    }

    private static string Text(string key, params object[] args) => FoodText.Get("food." + key, args);
}

internal sealed class FoodRecipeTooltip : CustomUITooltipTrigger, IPointerEnterHandler
{
    internal Func<string> Build = null!;

    void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
    {
        tooltipText = Build();
        base.OnPointerEnter(eventData);
    }
}
