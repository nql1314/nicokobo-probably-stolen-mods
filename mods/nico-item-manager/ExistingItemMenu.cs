using System;
using HarmonyLib;
using UnityEngine;

namespace NicoItemManager
{
    [HarmonyPatch]
    internal static class ExistingItemMenu
    {
        const int HorizontalPadding = 3;
        const int VerticalPadding = 2;
        static ItemContextHandler owner;
        static PixelWindow window;
        static PixelElement original;
        static GridPixelElement layout;
        static RichTextElement manage;
        static bool faulted;

        static bool IsAttached(ItemContextHandler handler, PixelWindow target) => ReferenceEquals(owner, handler)
            && ReferenceEquals(window, target) && layout?.handler != null && target?.handler != null
            && layout.handler.transform.IsChildOf(target.handler.transform);

        static bool IsVisible(ItemContextHandler handler) => ReferenceEquals(owner, handler)
            && manage?.handler != null && manage.handler.gameObject.activeInHierarchy;

        [HarmonyPatch(typeof(ItemContextHandler), "EqualizeButtonWidths")]
        [HarmonyPostfix]
        static void Prepare(ItemContextHandler __instance, GameItem ___currentItem, PixelWindow ___contextWindow,
            RichTextElement ___buttonUse, RenderHandler.ColorPalette ___textColor)
        {
            if (faulted || NicoItemManagerMod.Active?.Panel == null) return;
            try
            {
                bool allowed = ExistingItems.UnavailableReason(___currentItem) == null;
                if (!IsAttached(__instance, ___contextWindow))
                {
                    if (!allowed) return;
                    Attach(__instance, ___contextWindow);
                }

                // Keep the row parented so reparenting through the temp root cannot compound its local scale.
                manage.handler.gameObject.SetActive(allowed);
                if (allowed)
                {
                    var nativeText = ___buttonUse.GetText().text;
                    var text = manage.GetText().text;
                    text.font = nativeText.font;
                    text.fontStyle = nativeText.fontStyle;
                    text.enableAutoSizing = false;
                    text.fontSize = nativeText.fontSize;
                    text.rectTransform.localScale = nativeText.rectTransform.localScale;

                    // Use the label and font as inputs so a missing measurement cannot reuse the previous row size.
                    string label = Text.Get("manage_item");
                    Vector2 size = text.GetPreferredValues(label);
                    int width = Mathf.Max(___buttonUse.widthPixels, Mathf.CeilToInt(size.x) + 2 * HorizontalPadding);
                    int height = Mathf.Max(UISettings.current.fontHeight, Mathf.CeilToInt(size.y)) + 2 * VerticalPadding;
                    manage.SetFixedSize(width, height);
                    manage.SetText(label, 10000, ___textColor);
                }

                // The native handler validates and positions the window after this postfix.
                layout.SetSpacing(0, allowed ? window.yPadding : 0);
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning("Existing item menu: " + error);
                Clear();
                faulted = true;
            }
        }

        [HarmonyPatch(typeof(ItemContextHandler), "UpdateSelectButtonColor")]
        [HarmonyPostfix]
        static void Highlight(ItemContextHandler __instance, RichTextElement __0,
            RenderHandler.ColorPalette ___textColor, RenderHandler.ColorPalette ___textSelectColor)
        {
            if (!IsVisible(__instance)) return;
            manage.GetText().text.color = RenderHandler.GetColorFromPalette(
                ReferenceEquals(__0, manage) ? ___textSelectColor : ___textColor);
        }

        [HarmonyPatch(typeof(ItemContextHandler), "TriggerButton")]
        [HarmonyPrefix]
        static bool Trigger(ItemContextHandler __instance, RichTextElement __0, GameItem ___currentItem)
        {
            if (!ReferenceEquals(__0, manage) || !IsVisible(__instance)) return true;
            __instance.Stop();
            NicoItemManagerMod.Active?.Panel?.OpenExistingItem(___currentItem);
            return false;
        }

        static void Attach(ItemContextHandler handler, PixelWindow target)
        {
            Clear();
            PixelElement content = target?.childElement;
            if (content?.handler == null) throw new InvalidOperationException("Item context menu is unavailable.");
            var button = new RichTextElement(false, 0, 0, true);
            button.SetPadding(HorizontalPadding, HorizontalPadding, VerticalPadding, VerticalPadding);
            button.handler.gameObject.SetActive(false);
            var wrapper = new GridPixelElement(1, 2);
            Vector3 scale = content.handler.transform.localScale;
            try
            {
                foreach (Transform node in button.handler.GetComponentsInChildren<Transform>(true)) node.gameObject.tag = "ContextMenuNode";
                button.handler.gameObject.name = "NicoItemManager.ManageItem";
                if (!wrapper.AttachPos(button, 0, 1) || !target.Detach(content)
                    || !wrapper.AttachPos(content, 0, 0) || !target.Attach(wrapper))
                    throw new InvalidOperationException("Could not attach the item management menu.");
                owner = handler;
                window = target;
                original = content;
                layout = wrapper;
                manage = button;
            }
            catch
            {
                if (ReferenceEquals(target.childElement, wrapper)) target.Detach(wrapper);
                wrapper.Detach(content);
                if (wrapper.GetElement(0, 0) == null)
                {
                    if (target.childElement == null) target.Attach(content);
                    UnityEngine.Object.Destroy(wrapper.handler.gameObject);
                    UnityEngine.Object.Destroy(button.handler.gameObject);
                }
                throw;
            }
            finally { content.handler.transform.localScale = scale; }
        }

        internal static void Clear()
        {
            try
            {
                if (layout?.handler == null || manage?.handler == null) return;
                manage.handler.gameObject.SetActive(false);
                layout.Detach(manage);
                UnityEngine.Object.Destroy(manage.handler.gameObject);

                // A different Mod may own the outer layout. Leave its rows and the original menu attached.
                if (window?.handler != null && original?.handler != null && ReferenceEquals(window.childElement, layout))
                {
                    Vector3 scale = original.handler.transform.localScale;
                    if (layout.Detach(original))
                    {
                        window.Attach(original);
                        original.handler.transform.localScale = scale;
                        UnityEngine.Object.Destroy(layout.handler.gameObject);
                    }
                }
                else layout.SetSpacing(0, 0);
            }
            catch (Exception error) { NicoItemManagerMod.Log?.Warning("Existing item menu cleanup: " + error); }
            finally
            {
                owner = null;
                window = null;
                original = null;
                layout = null;
                manage = null;
                faulted = false;
            }
        }
    }
}
