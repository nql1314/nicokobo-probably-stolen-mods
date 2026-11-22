using AugMechanicalAscension.Official;
using HarmonyLib;
using UnityEngine;

namespace AugMechanicalAscension.Integration;

// The native menu owns input, selection and placement. Only these appended rows are ours.
internal sealed class StoryItemContextMenu(StoryRuntimeFlow flow, Action<string> log)
{
    private readonly StoryRuntimeFlow _flow = flow;
    private readonly Action<string> _log = log;
    private const string HarmonyId = "nicokobo.aug.story.item_menu";
    private const int RowCount = 4;
    private static readonly AccessTools.FieldRef<ItemContextHandler, GameItem> CurrentItem = AccessTools.FieldRefAccess<ItemContextHandler, GameItem>("currentItem");
    private static readonly AccessTools.FieldRef<ItemContextHandler, PixelWindow> ContextWindow = AccessTools.FieldRefAccess<ItemContextHandler, PixelWindow>("contextWindow");
    private static readonly AccessTools.FieldRef<ItemContextHandler, RichTextElement> ButtonUse = AccessTools.FieldRefAccess<ItemContextHandler, RichTextElement>("buttonUse");
    private static readonly AccessTools.FieldRef<ItemContextHandler, RenderHandler.ColorPalette> TextColor = AccessTools.FieldRefAccess<ItemContextHandler, RenderHandler.ColorPalette>("textColor");
    private static readonly AccessTools.FieldRef<ItemContextHandler, RenderHandler.ColorPalette> DisabledColor = AccessTools.FieldRefAccess<ItemContextHandler, RenderHandler.ColorPalette>("textDisabledColor");
    private static readonly AccessTools.FieldRef<ItemContextHandler, RenderHandler.ColorPalette> SelectedColor = AccessTools.FieldRefAccess<ItemContextHandler, RenderHandler.ColorPalette>("textSelectColor");
    private static readonly AccessTools.FieldRef<RichTextElement, TreeNodeRender> TextNode = AccessTools.FieldRefAccess<RichTextElement, TreeNodeRender>("textNode");
    private sealed record Menu(IntPtr Owner, PixelWindow Window, GridPixelElement Layout, RichTextElement[] Buttons)
    {
        internal long? Generation;
        internal IntPtr Item;
        internal int Uid;
        internal StoryRuntimeFlow.ItemAction[] Actions = [];
    }
    private static StoryItemContextMenu? _active;
    private Menu? _menu;

    internal static void Clear()
    {
        var self = _active; _active = null;
        var menu = self?._menu;
        if (menu == null) return;
        menu.Actions = [];
        try
        {
            foreach (var button in menu.Buttons)
            {
                menu.Layout.Detach(button);
                button.handler.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(button.handler.gameObject);
            }
            // Restore only the exact root that this instance wrapped.
            if (ReferenceEquals(menu.Window.childElement, menu.Layout) && menu.Layout.GetElement(0, 0) is { } original &&
                menu.Window.Detach(menu.Layout) && menu.Layout.Detach(original) && menu.Window.Attach(original))
                UnityEngine.Object.Destroy(menu.Layout.handler.gameObject);
            menu.Window.Validate();
        }
        catch (Exception error) { self!._log("[WARN] [Story] Item menu cleanup: " + error.Message); }
        self!._menu = null;
    }

    internal static void Install(StoryRuntimeFlow flow, Action<string> log)
    {
        var harmony = new HarmonyLib.Harmony(HarmonyId);
        try
        {
            var colors = AccessTools.Method(typeof(ItemContextHandler), "UpdateSelectButtonColor", [typeof(RichTextElement)]);
            var widths = AccessTools.Method(typeof(ItemContextHandler), "EqualizeButtonWidths", Type.EmptyTypes);
            var trigger = AccessTools.Method(typeof(ItemContextHandler), "TriggerButton", [typeof(RichTextElement)]);
            foreach (var method in new[] { colors, widths, trigger })
                if (method == null || method.ReturnType != typeof(void) || !OfficialHookApi.TryValidateNativeTarget(method, out _))
                    throw new InvalidOperationException("Story item menu native target unavailable");
            harmony.Patch(colors!, postfix: new HarmonyMethod(typeof(StoryItemContextMenu), nameof(ColorsPostfix)));
            harmony.Patch(widths!, postfix: new HarmonyMethod(typeof(StoryItemContextMenu), nameof(WidthsPostfix)));
            harmony.Patch(trigger!, prefix: new HarmonyMethod(typeof(StoryItemContextMenu), nameof(TriggerPrefix)));
            _active = new(flow, log);
        }
        catch { harmony.UnpatchAll(harmony.Id); throw; }
    }

    private static void ColorsPostfix(ItemContextHandler __instance, RichTextElement? selectedButton)
    {
        var self = _active;
        if (self == null || __instance == null || NativeObject.Handle(__instance) == IntPtr.Zero) return;
        try
        {
            var item = CurrentItem(__instance);
            var actions = item == null ? [] : self._flow.ItemActions(item);
            if (actions.Length > RowCount) throw new InvalidOperationException("Too many story item actions");
            var menu = actions.Length == 0 ? self._menu : self.EnsureMenu(__instance);
            if (menu == null || menu.Owner != NativeObject.Handle(__instance) || NativeObject.Handle(menu.Window) != NativeObject.Handle(ContextWindow(__instance)) || !Attached(menu)) return;
            menu.Generation = self._flow.Generation;
            menu.Item = NativeObject.Handle(item);
            menu.Uid = item?.uniqueId ?? 0;
            menu.Actions = actions;
            var nativeText = TextNode(ButtonUse(__instance)).text;
            for (int index = 0; index < RowCount; index++)
            {
                var button = menu.Buttons[index];
                bool visible = index < actions.Length;
                bool attached = NativeObject.Handle(menu.Layout.GetElement(0, index + 1)) == NativeObject.Handle(button);
                if (visible != attached)
                {
                    var scale = button.handler.transform.localScale;
                    bool changed = visible ? menu.Layout.AttachPos(button, 0, index + 1) : menu.Layout.Detach(button);
                    if (!changed) throw new InvalidOperationException("Story menu row visibility could not be changed");
                    button.handler.transform.localScale = scale;
                    button.handler.gameObject.SetActive(visible);
                }
                if (!visible) continue;
                var text = TextNode(button).text;
                text.font = nativeText.font;
                text.fontStyle = nativeText.fontStyle;
                text.enableAutoSizing = false;
                text.fontSize = nativeText.fontSize;
                text.rectTransform.localScale = nativeText.rectTransform.localScale;
                button.SetFixedSize(-1, -1);
                button.SetText(actions[index].Label, 10000,
                    !actions[index].Enabled ? DisabledColor(__instance) :
                    NativeObject.Handle(selectedButton) == NativeObject.Handle(button) ? SelectedColor(__instance) : TextColor(__instance));
            }
            menu.Layout.Validate();
            menu.Window.Validate();
        }
        catch (Exception ex) { self._log("[WARN] [Story] Item menu: " + ex.Message); }
    }

    private Menu? EnsureMenu(ItemContextHandler handler)
    {
        var window = ContextWindow(handler);
        if (window == null || window.childElement == null) return null;
        if (_menu is { } current && current.Owner == NativeObject.Handle(handler) && NativeObject.Handle(current.Window) == NativeObject.Handle(window) && Attached(current)) return current;
        var layout = new GridPixelElement(1, RowCount + 1);
        layout.handler.gameObject.SetActive(false);
        var buttons = new RichTextElement[RowCount];
        for (int index = 0; index < RowCount; index++)
        {
            var button = buttons[index] = new RichTextElement();
            button.handler.gameObject.name = "nodeButtonAugStory" + index;
            button.SetText("", 10000, DisabledColor(handler));
            if (!layout.AttachPos(button, 0, index + 1)) throw new InvalidOperationException("Story menu row could not be attached");
        }
        foreach (var child in layout.handler.GetComponentsInChildren<Transform>(true)) child.gameObject.tag = "ContextMenuNode";
        var original = window.childElement;
        var originalScale = original.handler.transform.localScale;
        try
        {
            if (!window.Detach(original) || !layout.AttachPos(original, 0, 0) || !window.Attach(layout))
                throw new InvalidOperationException("Story menu could not be attached");
            layout.handler.gameObject.SetActive(true);
            return _menu = new(NativeObject.Handle(handler), window, layout, buttons);
        }
        catch
        {
            if (NativeObject.Handle(window.childElement) == NativeObject.Handle(layout)) window.Detach(layout);
            layout.Detach(original);
            if (window.childElement == null && layout.GetElement(0, 0) == null) window.Attach(original);
            if (layout.GetElement(0, 0) == null)
            {
                UnityEngine.Object.Destroy(layout.handler.gameObject);
                foreach (var button in buttons) if (button != null) UnityEngine.Object.Destroy(button.handler.gameObject);
            }
            throw;
        }
        finally { original.handler.transform.localScale = originalScale; window.Validate(); }
    }

    private static bool Attached(Menu menu) => menu.Layout.handler.transform.IsChildOf(menu.Window.handler.transform);

    private static void WidthsPostfix(ItemContextHandler __instance)
    {
        var self = _active;
        var menu = self?._menu;
        if (menu == null || menu.Owner != NativeObject.Handle(__instance) || NativeObject.Handle(menu.Window) != NativeObject.Handle(ContextWindow(__instance)) || !Attached(menu)) return;
        try
        {
            int width = ButtonUse(__instance).widthPixels;
            for (int index = 0; index < menu.Actions.Length; index++) width = Math.Max(width, menu.Buttons[index].widthPixels);
            for (int index = 0; index < menu.Actions.Length; index++) menu.Buttons[index].SetFixedSize(width, ButtonUse(__instance).heightPixels);
            menu.Window.Validate();
        }
        catch (Exception ex) { self!._log("[WARN] [Story] Item menu width: " + ex.Message); }
    }

    private static bool TriggerPrefix(ItemContextHandler __instance, RichTextElement button)
    {
        var self = _active;
        var menu = self?._menu;
        if (menu == null || __instance == null || button == null || menu.Owner != NativeObject.Handle(__instance)) return true;
        int index = Array.FindIndex(menu.Buttons, x => NativeObject.Handle(x) == NativeObject.Handle(button));
        if (index < 0) return true;
        try
        {
            var item = CurrentItem(__instance);
            if (NativeObject.Handle(menu.Window) != NativeObject.Handle(ContextWindow(__instance)) || !Attached(menu) ||
                NativeObject.Handle(menu.Layout.GetElement(0, index + 1)) != NativeObject.Handle(button) || index >= menu.Actions.Length ||
                menu.Generation != self!._flow.Generation || item == null || NativeObject.Handle(item) != menu.Item || item.uniqueId != menu.Uid) return false;
            string action = menu.Actions[index].Id;
            if (!self._flow.ItemActions(item).Any(x => x.Id == action && x.Enabled)) return false;
            menu.Actions = [];
            __instance.Stop();
            self._flow.ExecuteItemAction(item, action);
        }
        catch (Exception ex) { self!._log("[ERROR] [Story] Item action rejected: " + ex.Message); }
        return false;
    }
}
