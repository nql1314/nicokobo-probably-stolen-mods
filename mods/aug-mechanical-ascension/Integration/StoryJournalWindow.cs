using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Official;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
namespace AugMechanicalAscension.Integration;
internal static class StoryJournalWindow
{
    private static CustomUIWindow? _window;
    private static GameItem? _item;
    private static PlayerStore? _store;
    private static string? _run;
    private static int _slot, _page;
    private static StoryJournal.Page[] _pages = [];
    private static Func<GameItem, StoryJournal.Page[]>? _contents;
    private static Action<string>? _log;
    internal static bool ReadingVisible => _item != null && _window?.IsVisible == true;
    internal static bool PointerOverReadingWindow => OfficialStoryUi.PointerOverWindow;
    internal static void Install(Func<GameItem, StoryJournal.Page[]> contents, Action<string> log)
    {
        _contents = contents; _log = log;
        var harmony = new Harmony("nicokobo.aug.story.journal");
        try
        {
            Patch(typeof(StoreUIManager), nameof(StoreUIManager.ResolveEscape), Type.EmptyTypes, nameof(EscapePrefix));
            Patch(typeof(StoreUIManager), nameof(StoreUIManager.CloseAllUI), Type.EmptyTypes, nameof(CloseAll));
            Patch(typeof(PaperUIManager), nameof(PaperUIManager.OpenBook), [typeof(GameItem)], nameof(CloseAll));
            Patch(typeof(MultiPageUIManager), nameof(MultiPageUIManager.OpenUI), [typeof(int)], nameof(CloseAll));
            void Patch(Type type, string name, Type[] args, string callback) => harmony.Patch(
                NativeHookSet.Find(type, name, args), prefix: new HarmonyMethod(typeof(StoryJournalWindow), callback));
        }
        catch { harmony.UnpatchAll(harmony.Id); throw; }
    }
    internal static void Open(GameItem item, string? evidenceBody = null)
    {
        Close();
        try
        {
            var store = OfficialStoryGame.Store;
            if (store == null || !OfficialInventoryApi.IsPlayerOwned(item)) return;
            var nativeReader = HandnoteUIManager.Instance;
            if (nativeReader?.panel != null && nativeReader.panel.activeInHierarchy) nativeReader.ClosePanel();
            _pages = evidenceBody == null ? _contents?.Invoke(item) ?? [] : [new(item.name, evidenceBody)];
            if (_pages.Length == 0) return;
            _item = item; _store = store; _run = store.runID; _slot = store.saveSlotId;
            var builder = OfficialStoryUi.Create("nicokobo.aug.story.journal", evidenceBody != null
                ? AugText.Get("aug.evidence_record") : AugText.Get("aug.story_journal"), 540, 660,
                () => { _window = null; _item = null; _store = null; _pages = []; });
            _window = builder.Handle;
            builder.AddHeader("", "heading").BeginScrollFlexible(420);
            OfficialStoryUi.Body(builder, "", "body");
            builder.End().AddLabel(AugText.Get("aug.scroll_to_read_right_click_the_item"), "hint")
                .BeginRow(8, TextAnchor.MiddleCenter).AddButton("‹", () => ShowPage(_page - 1), "previous")
                .AddLabel("", "number").AddButton("›", () => ShowPage(_page + 1), "next").End()
                .AddButton(AugText.Get("aug.close"), Close, "close");
            OfficialStoryUi.Show(builder);
            ShowPage(evidenceBody != null ? 0 : _pages.Length - 1);
        }
        catch (Exception error) { Close(); _log?.Invoke("[ERROR] [Story] Journal window: " + error.Message); }
    }
    private static bool EscapePrefix()
    {
        if (!OfficialStoryUi.AnyVisible) return true;
        CloseAll(); PsModInput.TextInputFocus.BlockCurrentFrame(); return false;
    }
    private static void CloseAll() { Close(); OfficialStoryUi.CloseAll(); }
    internal static void Update()
    {
        if (_item == null) return;
        var store = OfficialStoryGame.Store;
        if (_window?.IsVisible != true || !ReferenceEquals(store, _store) || store == null ||
            store.runID != _run || store.saveSlotId != _slot || !OfficialInventoryApi.IsPlayerOwned(_item)) Close();
    }
    internal static void Close()
    {
        var window = _window; _window = null; _item = null; _store = null; _pages = [];
        window?.Close();
    }
    private static void ShowPage(int page)
    {
        if (_pages.Length == 0 || _window == null) return;
        _page = Math.Clamp(page, 0, _pages.Length - 1);
        _window.Get("heading").SetText(_pages[_page].Title);
        OfficialStoryUi.SetBody(_window, "body", _pages[_page].Body);
        _window.Get("number").SetText($"{_page + 1} / {_pages.Length}");
        _window.Get("previous").SetInteractable(_page > 0);
        _window.Get("next").SetInteractable(_page + 1 < _pages.Length);
        CustomUIBuilder.RebuildLayout(_window.ContentRoot);
        var scroll = _window.root.GetComponentInChildren<ScrollRect>(true);
        if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; }
    }
}
