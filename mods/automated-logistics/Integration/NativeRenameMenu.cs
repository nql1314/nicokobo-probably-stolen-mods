using static PsModI18n.I18n;
using UnityEngine;

namespace Nicokobo.AutomatedLogistics.Integration;

/// <summary>Add one native menu row while retaining all existing actions.</summary>
internal static class NativeRenameMenu
{
    private static ItemContextHandler? _owner;
    private static PixelWindow? _window;
    private static PixelElement? _original;
    private static GridPixelElement? _layout;
    private static RichTextElement? _rename;

    internal static void Refresh(ItemContextHandler owner)
    {
        var window = NativeAccess.Get<PixelWindow>(owner, "contextWindow");
        if (window?.childElement == null) return;
        bool allowed = AutomatedLogisticsRuntime.CanRename(NativeAccess.Get<GameItem>(owner, "currentItem"));
        if (!allowed && _owner?.Handle() != owner.Handle()) return;
        if (!IsAttached(owner))
        {
            Clear();
            var original = window.childElement;
            var rename = new RichTextElement(false, -1, -1, true);
            rename.SetPadding(3, 3, 2, 2);
            rename.SetText(PsModI18n.I18n.Get("rename"), 10000, NativeAccess.Get<RenderHandler.ColorPalette>(owner, "textColor"));
            var layout = new GridPixelElement(1, 2, false);
            var originalScale = original.handler.transform.localScale;
            try
            {
                if (!window.Detach(original)) throw new InvalidOperationException(PsModI18n.I18n.Get("could_not_extend_the_native_context_menu"));
                if (!layout.Attach(original) || !layout.Attach(rename.Cast<PixelElement>()) || !window.Attach(layout.Cast<PixelElement>()))
                    throw new InvalidOperationException(PsModI18n.I18n.Get("could_not_attach_the_rename_context_menu"));
                foreach (var node in rename.handler.GetComponentsInChildren<Transform>(true)) node.gameObject.tag = "ContextMenuNode";
                rename.handler.gameObject.name = "AutomatedLogisticsRenameMenuNode";
                _owner = owner; _window = window; _original = original; _layout = layout; _rename = rename;
            }
            catch
            {
                if (window.childElement?.Handle() == layout.Handle()) window.Detach(layout.Cast<PixelElement>());
                layout.Detach(original);
                if (window.childElement == null && layout.GetElement(0, 0) == null) window.Attach(original);
                window.Validate();
                if (layout.GetElement(0, 0) == null)
                {
                    UnityEngine.Object.Destroy(layout.handler.gameObject);
                    UnityEngine.Object.Destroy(rename.handler.gameObject);
                }
                throw;
            }
            finally { original.handler.transform.localScale = originalScale; }
        }
        if (!IsAttached(owner) || _rename == null) return;
        bool attached = _layout!.GetElement(0, 1)?.Handle() == _rename.Handle();
        if (allowed != attached)
        {
            var scale = _rename.handler.transform.localScale;
            try
            {
                bool changed = allowed
                    ? _layout.AttachPos(_rename.Cast<PixelElement>(), 0, 1)
                    : _layout.Detach(_rename.Cast<PixelElement>());
                if (!changed) throw new InvalidOperationException(PsModI18n.I18n.Get("could_not_update_the_rename_menu_row"));
            }
            finally { _rename.handler.transform.localScale = scale; }
        }
        _rename.handler.gameObject.SetActive(allowed);
        if (allowed)
        {
            var nativeText = NativeAccess.Text(NativeAccess.Get<RichTextElement>(owner, "buttonUse"));
            var text = NativeAccess.Text(_rename);
            text.font = nativeText.font; text.fontStyle = nativeText.fontStyle;
            text.enableAutoSizing = false; text.fontSize = nativeText.fontSize;
            text.rectTransform.localScale = nativeText.rectTransform.localScale;
            _rename.SetFixedSize(-1, -1);
            _rename.SetText(PsModI18n.I18n.Get("rename"), 10000, NativeAccess.Get<RenderHandler.ColorPalette>(owner, "textColor"));
        }
        Equalize(owner); _layout!.Validate(); window.Validate();
    }
    private static bool IsAttached(ItemContextHandler owner) =>
        _owner?.Handle() == owner.Handle() && NativeAccess.Get<PixelWindow>(owner, "contextWindow") is { } window &&
        _window?.Handle() == window.Handle() &&
        _layout?.handler.transform.IsChildOf(window.handler.transform) == true;

    internal static void Equalize(ItemContextHandler owner)
    {
        if (!IsAttached(owner) || _rename?.handler.gameObject.activeSelf != true) return;
        var buttons = new[] { NativeAccess.Get<RichTextElement>(owner, "buttonOpen"), NativeAccess.Get<RichTextElement>(owner, "buttonUse"), NativeAccess.Get<RichTextElement>(owner, "buttonEquip"),
            NativeAccess.Get<RichTextElement>(owner, "buttonActivate"), NativeAccess.Get<RichTextElement>(owner, "buttonToggle"), NativeAccess.Get<RichTextElement>(owner, "buttonUnload"), _rename };
        int width = buttons.Where(b => b != null).Max(b => b!.widthPixels);
        foreach (var button in buttons) button?.SetFixedSize(width, -1);
    }
    internal static bool Trigger(ItemContextHandler owner, RichTextElement button)
    {
        if (!IsAttached(owner) || button?.Handle() != _rename?.Handle() ||
            _layout!.GetElement(0, 1)?.Handle() != _rename?.Handle()) return false;
        var item = NativeAccess.Get<GameItem>(owner, "currentItem");
        NativeAccess.Reset(owner);
        if (item != null && AutomatedLogisticsRuntime.CanRename(item)) NativeHooks.Rename?.Invoke(item);
        return true;
    }
    internal static void Clear()
    {
        // Scene unload destroys the handlers before the persistent Mod clears
        // its managed PixelElement references.
        if (_layout?.handler == null || _original?.handler == null || _rename?.handler == null || _window?.handler == null)
        {
            _owner = null; _window = null; _original = null; _layout = null; _rename = null;
            return;
        }
        if (_layout != null && _rename != null)
        {
            _layout.Detach(_rename.Cast<PixelElement>());
            _rename.handler.gameObject.SetActive(false);
        }
        if (_owner != null && _owner.Handle() == ItemContextHandler.current?.Handle() &&
            _layout != null && _original != null && _window?.childElement?.Handle() == _layout.Handle())
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
        if (_layout?.GetElement(0, 0) != null) _layout.Validate();
        _window?.Validate();
        // If another extension wraps this layout, retain its native content and
        // remove only our row; replacing the window root would discard theirs.
        _owner = null; _window = null; _original = null; _layout = null; _rename = null;
    }
}
