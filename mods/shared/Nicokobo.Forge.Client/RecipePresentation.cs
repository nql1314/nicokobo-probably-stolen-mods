using UnityEngine;
using Nicokobo.ModUtilities;

namespace Nicokobo.ContentInterop;

// Handbooks and the shared query use the enabled provider's actual item art and text.
public static class RecipePresentation
{
    public static string Ingredient(IngredientView input) => Ingredient(input, !ModText.IsChinese);

    public static string Ingredient(IngredientView input, bool english)
    {
        string text = input.LocalizedLabel?.Invoke() ?? (english ? input.English : input.Chinese);
        if (string.IsNullOrWhiteSpace(text) || text == input.ItemId) text = ReadItem(input.ItemId).Name;
        // A provider can supply either a material label or a complete requirement.
        return text.Contains("×") || text.IndexOf(" x" + input.Amount, StringComparison.OrdinalIgnoreCase) >= 0
            ? text : text + " ×" + input.Amount;
    }

    public static string Output(RecipeView recipe, string itemName) => Output(recipe, itemName, !ModText.IsChinese);

    public static string Output(RecipeView recipe, string itemName, bool english) =>
        recipe is ExtraRecipe extra
            ? extra.LocalizedOutputName?.Invoke() ?? (english ? extra.EnglishOutput : extra.ChineseOutput)
            : itemName;

    public static (string Name, Sprite? Sprite) ReadItem(string id)
    {
        var item = ReadItemDetails(id);
        return (item.Name, item.Sprite);
    }

    internal static (string Name, Sprite? Sprite, string Description) ReadItemDetails(string id, bool includeSprite = true)
    {
        GameItem? item = null;
        string name = id;
        string description = "";
        try
        {
            if (!DirectoryMaster.Has<GameItem>(id)) return (id, null, "");
            // Create reads a prototype without Item() assigning a UID or firing
            // item-spawn permit hooks. Never insert it into a player inventory.
            item = DirectoryMaster.Create<GameItem>(id);
            if (item == null) return (id, null, "");
            name = item.GetDisplayName();
            description = item.shortDescription ?? "";
            if (!includeSprite) return (name, null, description);
            // UI string keys do not resolve item atlases. This also supports
            // providers that override the item's sprite resolver.
            var sprite = item is GameItemElement element ? element.ResolveSpriteByName(item.spritePath)
                : RenderHandler.LoadFromAtlas(item.spriteAtlasPath, item.spritePath);
            return (name, sprite, description);
        }
        catch (Exception error) { Debug.LogWarning("[ContentInterop] Recipe illustration failed for " + id + ": " + error.Message); return (name, null, description); }
        finally { if (item != null && item.parentInventory == null) ItemGraphCleanup.Destroy(item); }
    }
}
