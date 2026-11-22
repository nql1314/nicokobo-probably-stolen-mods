using Nicokobo.ModUtilities;
using System.Globalization;
using Nicokobo.Forge;
using UnityEngine;

namespace Nicokobo.ContentInterop;

// One runtime per shared assembly, regardless of which content package attached
// first. ContentRuntime owns creation and destruction; no extra IMod/PatchAll.
internal static class RecipeQuery
{
    private const string Owner = "nicokobo.content_interop";
    private const int PageSize = 8;
    private static GameObject? _host;
    private static ConfigEntry<KeyCode>? _key;
    private static CustomUIWindow? _window;
    private static RectTransform? _results;
    private static GameItem? _target;
    private static string? _itemFilter;
    private static string _query = "", _locale = "";
    private static int _page;
    private static bool _dirty;
    private static readonly Dictionary<string, (string Name, Sprite? Sprite)> Names = new(StringComparer.Ordinal);
    internal static bool IsVisible => _window?.IsVisible == true;
    internal static bool PointerOverWindow => IsVisible && _window!.Rect != null &&
        RectTransformUtility.RectangleContainsScreenPoint(_window.Rect, Input.mousePosition,
            _window.root.GetComponentInParent<Canvas>()?.worldCamera);
    private static string Locale => ModText.LocaleCode;
    private static string Text(string key, params object[] args) =>
        ModText.Get(ForgeMod.Id, "recipe_query." + key, args);

    internal static void Attach()
    {
        if (_host != null) return;
        _key = ModHelper.GetConfig(Owner).DefineEnum("RecipeQueryKey", KeyCode.G, "Controls",
            () => Text("title"));
        ModHelper.AddGameOptionKeyBind(Owner, ModHelper.OPTION_SECTION_KEYBIND, "recipe_query",
            () => Text("key_name"), () => _key.Value, value => _key.Set(value), () => Text("key_hint"));
        _host = new GameObject("Nicokobo.RecipeQuery");
        UnityEngine.Object.DontDestroyOnLoad(_host);
        _host.AddComponent<RecipeQueryDriver>();
    }

    internal static void Detach()
    {
        Close(); Names.Clear(); _target = null;
        ModHelper.RemoveGameOptions(Owner);
        if (_host != null) UnityEngine.Object.Destroy(_host);
        _host = null; _key = null;
    }

    internal static void Tick()
    {
        if (!PlayerStore.HasInstance || EmporiumEntry.Instance == null || StoreUIManager.Instance == null)
        { Close(); Names.Clear(); return; }
        if (_window != null && !_window.IsAlive) Closed();
        string locale = Locale;
        if (_locale != locale)
        {
            _locale = locale; Names.Clear();
            if (_window?.IsVisible == true) Open(_target, _itemFilter);
        }
        if (_dirty && _window?.IsVisible == true) { _dirty = false; RenderResults(); }
        if (_key == null || _key.Value == KeyCode.None || !Application.isFocused ||
            KeyBindHelper.SuppressHotkeys || Nicokobo.Forge.Input.TextInputFocus.ShouldSuppressHotkeys() || StoreUIManager.IsAnyTyping() ||
            !string.IsNullOrEmpty(Input.compositionString) || !Input.GetKeyDown(_key.Value)) return;
        if (ItemMouseDragHandler.current?.IsDraggingItem == true || UIDragHandler.isDraggingAnyUI) return;
        GameItem? hovered = PointerOverWindow ? null : RenderHandler.RaycastElement<GameItem>(Input.mousePosition);
        if (_window?.IsVisible == true && (hovered == null || hovered.identifier == _target?.identifier)) { Close(); return; }
        Open(hovered);
    }

    internal static void OpenItem(string id)
    {
        if (DirectoryMaster.Has<GameItem>(id)) Open(null, id);
    }

    internal static bool HasRecipesForItem(string id) => Catalog().Any(recipe => Relevant(recipe, null, id));

    internal static void BringToFront()
    {
        if (_window?.IsVisible == true) _window.BringToFront();
    }

    private static void Open(GameItem? target, string? itemFilter = null)
    {
        Close();
        if (CustomUIManager.Instance == null || !Catalog().Any(recipe => Relevant(recipe, target, itemFilter))) return;
        _target = target; _itemFilter = itemFilter; _query = ""; _page = 0;
        string title = Text("title") + (target != null ? " · " + target.GetDisplayName()
            : itemFilter == null ? "" : " · " + Read(itemFilter).Name);
        var ui = CustomUIManager.Window(Owner + ".recipe_query", title, CustomUIManager.LAYER_OVERLAY)
            .SetSize(800, 620).SetCloseOnEscape().UseMachineryStyle().OnClose(Closed);
        _window = ui.Handle;
        ui.AddLabel(KeyBindHelper.KeyName(_key?.Value ?? KeyCode.G) + " · " + Text("key_hint") + "\n" + Text("source_hint"));
        ui.AddInput(Text("search"),
            initial: _query, id: "query", onTyping: value => { _query = value ?? ""; _page = 0; _dirty = true; });
        ui.BeginScroll(420); _results = ui.CurrentContainer; ui.End();
        ui.BeginRow();
        ui.AddButton(Text("previous"), () => { _page = Math.Max(0, _page - 1); _dirty = true; });
        ui.AddButton(Text("next"), () => { _page++; _dirty = true; });
        ui.AddButton(Text("all"), () => Open(null));
        ui.AddButton(LocHelper.GetLocalizedMainMenu("close"), Close); ui.End();
        RenderResults(); ui.Show();
        PresentationInputGuard.Opened();
    }

    private static void RenderResults()
    {
        if (_window == null || _results == null || CustomUIManager.Instance == null) return;
        var recipes = Catalog().Where(recipe => Relevant(recipe, _target, _itemFilter))
            .Where(Search).OrderBy(recipe => recipe.MachineId, StringComparer.Ordinal)
            .ThenBy(recipe => recipe.Id, StringComparer.Ordinal).ToArray();
        int pages = Math.Max(1, (recipes.Length + PageSize - 1) / PageSize);
        _page = Math.Min(_page, pages - 1);
        CustomUIBuilder.ClearContainer(_results);
        var ui = new CustomUIBuilder(CustomUIManager.Instance, _window, _results);
        ui.AddLabel(Text("page", recipes.Length, _page + 1, pages));
        if (recipes.Length == 0) ui.AddLabel(Text("no_match"));
        foreach (var recipe in recipes.Skip(_page * PageSize).Take(PageSize))
        {
            var output = Read(recipe.OutputItemId);
            ui.BeginRow();
            if (output.Sprite != null) ui.AddImage(output.Sprite, 48, 48);
            string product = RecipePresentation.Output(recipe, output.Name);
            string inputs = string.Join(" + ", recipe.Ingredients.Select(RecipePresentation.Ingredient));
            if (recipe.WaterParts > 0) inputs += (inputs.Length == 0 ? "" : " + ") +
                LocHelper.GetLocalizedMechanic("liquid_water") + " " +
                (recipe.WaterParts / 1000m).ToString("0.###", CultureInfo.InvariantCulture) + " ml";
            ui.AddLabel(Read(recipe.MachineId).Name + " · " + Role(recipe) + "\n" + inputs + " → " + product + " ×" + recipe.OutputCount);
            if (_target?.identifier == recipe.MachineId && recipe.Preview != null)
                ui.AddLabel(recipe.Preview(_target));
            ui.End();
        }
        CustomUIBuilder.RebuildLayout(_results);
    }

    private static IEnumerable<RecipeView> Catalog() => MachineCatalog.MachineIds().SelectMany(MachineCatalog.Recipes)
        .GroupBy(recipe => recipe.Id, StringComparer.Ordinal).Select(group => group.First());

    private static bool Relevant(RecipeView recipe, GameItem? target, string? itemFilter)
    {
        string? id = target?.identifier ?? itemFilter;
        // A tagged ingredient can impose additional conditions on the hovered item.
        return id == null || recipe.MachineId == id || recipe.OutputItemId == id ||
            recipe.Ingredients.Any(input => input.ItemId == id || (target != null && input.Matches(target)));
    }
    private static string Role(RecipeView recipe) => Text(_target == null && _itemFilter == null ? "recipe"
        : recipe.MachineId == (_target?.identifier ?? _itemFilter) ? "machine"
        : recipe.OutputItemId == (_target?.identifier ?? _itemFilter) ? "output" : "ingredient");
    private static bool Search(RecipeView recipe)
    {
        string query = _query.Trim();
        if (query.Length == 0) return true;
        var words = new[] { recipe.Id, recipe.OutputItemId, recipe.MachineId,
                RecipePresentation.Output(recipe, Read(recipe.OutputItemId).Name), Read(recipe.MachineId).Name }
            .Concat(recipe.Ingredients.SelectMany(input => new[] { input.ItemId, input.Chinese, input.English,
                input.LocalizedLabel?.Invoke(), Read(input.ItemId).Name }));
        return words.Any(word => word?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    private static (string Name, Sprite? Sprite) Read(string id)
    {
        if (Names.TryGetValue(id, out var value)) return value;
        value = RecipePresentation.ReadItem(id);
        Names[id] = value;
        return value;
    }
    private static void Closed()
    {
        _window = null; _results = null; _target = null; _itemFilter = null; _dirty = false;
        PresentationInputGuard.Closed();
    }
    private static void Close() { var window = _window; Closed(); window?.Close(); }
}

internal sealed class RecipeQueryDriver : MonoBehaviour
{
    private void Update() => RecipeQuery.Tick();
}
