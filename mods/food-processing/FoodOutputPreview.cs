using Nicokobo.ContentInterop;
using Nicokobo.ModUtilities;
using TMPro;
using UnityEngine;

namespace Nicokobo.FoodProcessing;

// Render-only children of the output grid: no GameItem, UID, inventory slot or save data.
internal sealed class FoodOutputPreview : MonoBehaviour
{
    private static readonly HashSet<FoodOutputPreview> Active = new();
    private GameItem _machine = null!;
    private GameGridInventory _output = null!;
    private readonly List<(TreeNodeRender Image, TreeNodeRender Count)> _graphics = new();
    private float _nextRefresh;
    private string _locale = "";
    private readonly Dictionary<string, (string Name, Sprite? Sprite, int Width, int Height)> _art = new(StringComparer.Ordinal);
    internal string Description { get; private set; } = "";
    internal string LinkId => "food_output_preview_" + GetInstanceID();

    internal static FoodOutputPreview Attach(GameItem machine, GameGridInventory output)
    {
        var preview = output.inner.gameObject.AddComponent<FoodOutputPreview>();
        Active.Add(preview);
        preview._machine = machine;
        preview._output = output;
        preview.Refresh();
        return preview;
    }

    private void OnEnable() => _nextRefresh = 0;
    private void OnDestroy()
    {
        Active.Remove(this);
        TMPIconHover.CustomLinks.Remove(LinkId);
        foreach (var graphic in _graphics)
        {
            if (graphic.Image != null) Destroy(graphic.Image.gameObject);
            if (graphic.Count != null) Destroy(graphic.Count.gameObject);
        }
    }

    internal static void CloseAll()
    {
        foreach (var preview in Active.ToArray())
        {
            preview.enabled = false;
            Destroy(preview);
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.25f;
        Refresh();
    }

    internal void Refresh()
    {
        foreach (var graphic in _graphics)
        {
            graphic.Image.image.enabled = false;
            graphic.Count.text.enabled = false;
        }
        var cycle = FoodProduction.PreviewCycle(_machine);
        Description = FoodText.Get("food.preview_batches", cycle.Batches, cycle.Capacity) + "\n" +
            FoodText.Get("food.preview_note");
        var products = cycle.Outputs.ToList();
        if (cycle.Batches == 0)
        {
            var waiting = FoodProduction.OutputPreview(_machine);
            if (waiting == null)
            {
                Description += "\n" + FoodText.Get("food.preview_empty");
                return;
            }
            products.Add(new FoodProduction.PreviewOutput(waiting, 0, 0));
            Description += "\n" + FoodProduction.PreviewForSelection(_machine, waiting);
        }
        else
        {
            Description += "\n" + string.Join(" + ", cycle.Inputs.Select(take => take.Item.GetDisplayName() + " ×" + take.Count)) +
                "\n" + FoodText.Get("food.preview_totals", cycle.Calories, cycle.WaterParts / 1000, cycle.Power);
        }
        string locale = ModText.LocaleCode;
        if (_locale != locale) { _locale = locale; _art.Clear(); }
        var occupied = _output.childItems.Select(item => item.modifiedShape).ToList();
        bool noRoom = false;
        for (int index = 0; index < products.Count; index++)
        {
            var product = products[index];
            var recipe = product.Recipe;
            if (!_art.TryGetValue(recipe.OutputItemId, out var art))
            {
                if (!DirectoryMaster.Has<GameItem>(recipe.OutputItemId)) continue;
                var prototype = DirectoryMaster.Create<GameItem>(recipe.OutputItemId);
                try
                {
                    var sprite = prototype is GameItemElement element ? element.ResolveSpriteByName(prototype.spritePath)
                        : RenderHandler.LoadFromAtlas(prototype.spriteAtlasPath, prototype.spritePath);
                    art = (prototype.GetDisplayName(), sprite, prototype.modifiedShape.globalWidth, prototype.modifiedShape.globalHeight);
                    _art[recipe.OutputItemId] = art;
                }
                finally { ItemGraphCleanup.Destroy(prototype); }
            }
            Description += "\n" + FoodText.Get("food.preview_product",
                RecipePresentation.Output(recipe, art.Name),
                product.Quantity, product.Batches);
            if (art.Sprite == null) continue;
            while (_graphics.Count <= index)
            {
                var image = TreeNodeRender.Instantiate().SetParent(_output.inner);
                image.image.raycastTarget = false;
                image.image.preserveAspect = true;
                image.image.color = new Color(1, 1, 1, 0.38f);
                image.image.enabled = false;
                var count = TreeNodeRender.InstantiateText().SetParent(_output.inner);
                MachineryStyle.InitText(count, 6, new Color(1, 1, 1, 0.7f), HorizontalAlignmentOptions.Right);
                count.text.raycastTarget = false;
                count.text.enabled = false;
                _graphics.Add((image, count));
            }
            var graphic = _graphics[index];
            // Reserve illustration rectangles only in this local list. Actual
            // item stacking, output capacity and module wear are checked at production.
            bool placed = false;
            for (int y = 0; !placed && y <= _output.inventoryShape.height - art.Height; y++)
            for (int x = 0; !placed && x <= _output.inventoryShape.width - art.Width; x++)
            {
                bool free = true;
                for (int dy = 0; free && dy < art.Height; dy++)
                for (int dx = 0; free && dx < art.Width; dx++)
                    free = _output.inventoryShape.Get(x + dx, y + dy) == 0 &&
                        !occupied.Any(shape => shape.Get(x + dx, y + dy) == 1);
                if (!free) continue;
                int cell = UISettings.current.gridSize;
                MachineryStyle.Place(graphic.Image, x * cell, y * cell, art.Width * cell, art.Height * cell);
                MachineryStyle.Place(graphic.Count, x * cell, (y + art.Height) * cell - 9, art.Width * cell, 9);
                graphic.Image.image.sprite = art.Sprite;
                graphic.Image.image.enabled = true;
                graphic.Count.text.text = "×" + product.Quantity;
                graphic.Count.text.enabled = true;
                occupied.Add(new GridShapeBuilder(art.Width, art.Height).SetDataFill(1).SetPosition(x, y).Build());
                placed = true;
            }
            noRoom |= !placed;
        }
        if (noRoom) Description += "\n" + FoodText.Get("food.preview_no_room");
    }
}
