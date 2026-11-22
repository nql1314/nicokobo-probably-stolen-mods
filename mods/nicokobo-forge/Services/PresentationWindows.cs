
using UnityEngine;
using UnityEngine.UI;

namespace Nicokobo.ContentInterop;

// All long-form story documents use the official window, locale font and scroll controls.
internal static class OfficialStoryUi
{
    private static readonly Dictionary<CustomUIWindow, string> Windows = new();
    private static CustomUIWindow? _document;
    private static PlayerStore? _documentStore;
    private static string? _owner, _id, _run;
    private static int _slot;
    internal static bool AnyVisible => Windows.Keys.Any(window => window.IsVisible);
    internal static bool PointerOverWindow => Windows.Keys.Any(window => window.IsVisible && window.Rect != null &&
        RectTransformUtility.RectangleContainsScreenPoint(window.Rect, Input.mousePosition,
            window.root.GetComponentInParent<Canvas>()?.worldCamera));

    internal static CustomUIBuilder Create(string owner, string id, string title, float width, float height, Action? onClose = null)
    {
        if (CustomUIManager.Instance == null) throw new InvalidOperationException("The official CustomUI manager is unavailable");
        var builder = CustomUIManager.Window(id, title, CustomUIManager.LAYER_PAPER)
            ?? throw new InvalidOperationException("The official story window could not be created");
        builder.SetSize(width, height).SetDraggable(true).SetCloseOnEscape(false).Center();
        var window = builder.Handle;
        Windows[window] = owner;
        builder.OnClose(() =>
        {
            Windows.Remove(window);
            PresentationInputGuard.Closed();
            onClose?.Invoke();
        });
        return builder;
    }

    internal static void Show(CustomUIBuilder builder)
    {
        var window = builder.Show();
        if (!window.IsVisible || window.ContentRoot == null)
            throw new InvalidOperationException("Story window did not become visible");
        if (!Windows.ContainsKey(window)) throw new InvalidOperationException("Window was not created by Forge");
        PresentationInputGuard.Opened();
        CustomUIBuilder.RebuildLayout(window.ContentRoot);
    }

    internal static void Body(CustomUIBuilder builder, string text, string id)
    {
        builder.AddLabel(text, id);
        var element = builder.Last;
        if (element?.text == null) throw new InvalidOperationException("Story text control is unavailable");
        element.text.richText = false;
        element.text.enableWordWrapping = true;
        element.text.fontSize = 18;
        element.text.enableAutoSizing = false;
        var layout = element.root.GetComponent<LayoutElement>() ?? element.root.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1; layout.flexibleHeight = 0;
        layout.preferredHeight = Math.Max(24, element.text.GetPreferredValues(text, Math.Max(180, builder.Handle.Rect.rect.width - 80), 0).y + 12);
    }

    internal static void SetBody(CustomUIWindow window, string id, string text)
    {
        var element = window.Get(id);
        element.SetText(text);
        var layout = element.root.GetComponent<LayoutElement>() ?? element.root.AddComponent<LayoutElement>();
        layout.preferredHeight = Math.Max(24, element.text.GetPreferredValues(text, Math.Max(180, window.Rect.rect.width - 80), 0).y + 12);
    }

    internal static OfficialPresentationResult OpenDocument(string owner, string id, string run, int slot,
        string title, string body, string signature)
    {
        if (!OfficialStoryConversationContract.Owned(owner, id) || title == null || body == null || signature == null)
            return new(OfficialPresentationStatus.Invalid);
        var store = OfficialStoryGame.Store;
        if (store == null || CustomUIManager.Instance == null) return new(OfficialPresentationStatus.Unavailable);
        if (store.runID != run || store.saveSlotId != slot) return new(OfficialPresentationStatus.Conflict);
        var nativeReader = HandnoteUIManager.Instance;
        if (nativeReader?.panel != null && nativeReader.panel.activeInHierarchy)
            return new(OfficialPresentationStatus.Conflict, "Another reading window is visible");
        if (_document?.IsVisible == true && (_owner != owner || _id != id))
            return new(OfficialPresentationStatus.Conflict, "Another story document is visible");
        try
        {
            CloseCurrentDocument();
            CloseOwner(owner);
            _owner = owner; _id = id; _run = run; _slot = slot; _documentStore = store;
            var builder = Create(owner, id, title, 580, 650, () => { _document = null; _documentStore = null; });
            _document = builder.Handle;
            builder.BeginScrollFlexible(420);
            Body(builder, body, "body");
            builder.End().AddLabel(signature, "signature").AddButton(LocHelper.GetLocalizedMainMenu("close"), CloseCurrentDocument, "close");
            Show(builder);
            return _document.IsVisible && _document.Get("body")?.GetText() == body
                ? new(OfficialPresentationStatus.Opened) : new(OfficialPresentationStatus.Indeterminate);
        }
        catch (Exception error)
        {
            CloseCurrentDocument();
            return new(OfficialPresentationStatus.Indeterminate, error.Message);
        }
    }

    internal static OfficialPresentationResult CloseDocument(string owner, string id)
    {
        if (!OfficialStoryConversationContract.Owned(owner, id)) return new(OfficialPresentationStatus.Invalid);
        if (_document == null) return new(OfficialPresentationStatus.Closed);
        if (_owner != owner || _id != id) return new(OfficialPresentationStatus.Conflict);
        CloseCurrentDocument();
        return new(OfficialPresentationStatus.Closed);
    }

    private static void CloseCurrentDocument()
    {
        var old = _document; _document = null; _documentStore = null;
        old?.Close();
    }

    internal static void Update()
    {
        foreach (var window in Windows.Keys.Where(window => !window.IsAlive).ToArray()) Windows.Remove(window);
        var store = OfficialStoryGame.Store;
        if (_document != null && (!ReferenceEquals(store, _documentStore) || store == null || store.runID != _run || store.saveSlotId != _slot))
            CloseCurrentDocument();
    }

    internal static void CloseOwner(string owner)
    { foreach (var window in Windows.Where(pair => pair.Value == owner).Select(pair => pair.Key).ToArray()) window.Close(); }
    internal static void CloseAll()
    {
        foreach (var window in Windows.Keys.ToArray()) window.Close();
        Windows.Clear(); _document = null; _documentStore = null;
    }
}
