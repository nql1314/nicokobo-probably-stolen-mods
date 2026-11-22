using static PsModI18n.I18n;
using UnityEngine;

namespace Nicokobo.AutomatedLogistics.Integration;

internal static class NativeStorageGatherMenu
{
    private static ItemContextHandler? _owner;
    private static PixelWindow? _window;
    private static PixelElement? _original;
    private static GridPixelElement? _layout;
    private static RichTextElement[] _buttons = [];
    private static string[] Labels => [PsModI18n.I18n.Get("gather_items")];
    private static bool IsVoidStorage(GameGridInventory? grid) => grid != null &&
        grid.identifier == Content.ContentsId && grid.GetParentItem() is { } item &&
        item.identifier == Content.ResourceDevice && NativeInventoryApi.IsPlayerOwned(item);

    private static bool Attached(ItemContextHandler owner) => _owner?.Handle() == owner.Handle() &&
        NativeAccess.Get<PixelWindow>(owner, "sortWindow") is { } window && _window?.Handle() == window.Handle() &&
        _layout?.handler.transform.IsChildOf(window.handler.transform) == true;

    internal static void Refresh(ItemContextHandler owner)
    {
        var window = NativeAccess.Get<PixelWindow>(owner, "sortWindow");
        if (window?.childElement == null) return;
        bool allowed = NativeAccess.Get<GameItem>(owner, "currentItem") == null && IsVoidStorage(NativeAccess.Get<GameGridInventory>(owner, "currentInventory"));
        if (!allowed && _owner?.Handle() != owner.Handle()) return;
        if (!Attached(owner))
        {
            Clear();
            var original = window.childElement;
            var layout = new GridPixelElement(1, Labels.Length + 1, false);
            var buttons = Labels.Select(label =>
            {
                var button = new RichTextElement(false, -1, -1, true);
                button.SetPadding(3, 3, 2, 2); button.SetText(label, 10000, NativeAccess.Get<RenderHandler.ColorPalette>(owner, "textColor"));
                foreach (var node in button.handler.GetComponentsInChildren<Transform>(true)) node.gameObject.tag = "SortContextMenuNode";
                return button;
            }).ToArray();
            var scale = original.handler.transform.localScale;
            try
            {
                if (!window.Detach(original) || !layout.Attach(original)) throw new InvalidOperationException(PsModI18n.I18n.Get("could_not_attach_the_gathering_menu"));
                for (int i = 0; i < buttons.Length; i++)
                    if (!layout.AttachPos(buttons[i].Cast<PixelElement>(), 0, i + 1)) throw new InvalidOperationException(PsModI18n.I18n.Get("could_not_attach_the_gathering_button"));
                if (!window.Attach(layout.Cast<PixelElement>())) throw new InvalidOperationException(PsModI18n.I18n.Get("could_not_attach_the_gathering_menu_2"));
                _owner = owner; _window = window; _original = original; _layout = layout; _buttons = buttons;
            }
            catch
            {
                if (window.childElement?.Handle() == layout.Handle()) window.Detach(layout.Cast<PixelElement>());
                layout.Detach(original);
                if (window.childElement == null && layout.GetElement(0, 0) == null) window.Attach(original);
                if (layout.GetElement(0, 0) == null) UnityEngine.Object.Destroy(layout.handler.gameObject);
                foreach (var button in buttons) UnityEngine.Object.Destroy(button.handler.gameObject);
                window.Validate(); throw;
            }
            finally { original.handler.transform.localScale = scale; }
        }
        if (!Attached(owner)) return;
        for (int i = 0; i < _buttons.Length; i++)
        {
            var button = _buttons[i];
            bool attached = _layout!.GetElement(0, i + 1)?.Handle() == button.Handle();
            var scale = button.handler.transform.localScale;
            try
            {
                if (allowed != attached && !(allowed ? _layout.AttachPos(button.Cast<PixelElement>(), 0, i + 1) : _layout.Detach(button.Cast<PixelElement>())))
                    throw new InvalidOperationException(PsModI18n.I18n.Get("could_not_update_the_gathering_button"));
            }
            finally { button.handler.transform.localScale = scale; }
            button.handler.gameObject.SetActive(allowed);
            var template = NativeAccess.Text(NativeAccess.Get<RichTextElement>(owner, "buttonSortBigStart"));
            var text = NativeAccess.Text(button);
            text.font = template.font; text.fontStyle = template.fontStyle;
            text.enableAutoSizing = false; text.fontSize = template.fontSize;
            text.rectTransform.localScale = template.rectTransform.localScale;
            button.SetFixedSize(-1, -1); button.SetText(Labels[i], 10000, NativeAccess.Get<RenderHandler.ColorPalette>(owner, "textColor"));
        }
        if (allowed)
        {
            var all = new[] { NativeAccess.Get<RichTextElement>(owner, "buttonSortBigStart"), NativeAccess.Get<RichTextElement>(owner, "buttonSortSmallStart"), NativeAccess.Get<RichTextElement>(owner, "buttonSortBigEnd"), NativeAccess.Get<RichTextElement>(owner, "buttonSortSmallEnd") }.Concat(_buttons).ToArray();
            int width = all.Max(button => button.widthPixels);
            foreach (var button in all) button.SetFixedSize(width, -1);
        }
        _layout!.Validate(); window.Validate();
    }

    internal static bool Trigger(ItemContextHandler owner, RichTextElement button)
    {
        if (!Attached(owner) || button == null) return false;
        int index = Array.FindIndex(_buttons, candidate => candidate.Handle() == button.Handle());
        if (index < 0 || _layout!.GetElement(0, index + 1)?.Handle() != button.Handle()) return false;
        var grid = NativeAccess.Get<GameGridInventory>(owner, "currentInventory");
        bool allowed = NativeAccess.Get<GameItem>(owner, "currentItem") == null && IsVoidStorage(grid);
        NativeAccess.Reset(owner);
        if (allowed && index == Labels.Length - 1)
        {
            try
            {
                if (NativeHooks.Runtime is { } runtime)
                {
                    var result = runtime.GatherResourceStorage(grid);
                    NativeHooks.Log?.Invoke(PsModI18n.I18n.Get("void_container_gathering_items", result.Moved, result.Status));
                }
            }
            catch (Exception ex) { NativeHooks.Log?.Invoke(PsModI18n.I18n.Get("void_container_gathering") + ex); }
        }
        return true;
    }

    internal static void Clear()
    {
        foreach (var button in _buttons)
        {
            _layout?.Detach(button.Cast<PixelElement>());
            button.handler.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(button.handler.gameObject);
        }
        if (_layout != null && _original != null && _window?.childElement?.Handle() == _layout.Handle())
        {
            var scale = _original.handler.transform.localScale;
            try
            {
                _window.Detach(_layout.Cast<PixelElement>());
                if (_layout.Detach(_original)) _window.Attach(_original);
                if (_layout.GetElement(0, 0) == null) UnityEngine.Object.Destroy(_layout.handler.gameObject);
            }
            finally { _original.handler.transform.localScale = scale; }
        }
        _window?.Validate();
        _owner = null; _window = null; _original = null; _layout = null; _buttons = [];
    }
}
