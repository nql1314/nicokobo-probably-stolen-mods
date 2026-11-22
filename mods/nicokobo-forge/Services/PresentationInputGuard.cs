
using HarmonyLib;
using UnityEngine;

namespace Nicokobo.ContentInterop;

internal static class PresentationInputGuard
{
    private const string HarmonyId = "nicokobo.forge.windows.input";
    private static readonly Nicokobo.ContentInterop.WindowInputCapture Capture = new();
    private static HarmonyLib.Harmony? _harmony;
    private static bool _windowVisible;
    private static bool _resetSelectionPending;
    private static bool AnyVisible => OfficialStoryUi.AnyVisible || OfficialWorkshopApi.IsVisible || RecipeQuery.IsVisible;
    private static bool PointerOverWindow => OfficialStoryUi.PointerOverWindow ||
        OfficialWorkshopApi.PointerOverWindow || RecipeQuery.PointerOverWindow;
    private static bool MouseHeld => Input.GetMouseButton(0) ||
        Input.GetMouseButton(1) || Input.GetMouseButton(2);

    internal static void Install()
    {
        if (_harmony != null) return;
        // StoreUIManager.Update inlines its canvas raycast in this game build.
        // A CheckRaycast prefix alone is overwritten at the end of Update.
        var hooks = new[]
        {
            (Target: AccessTools.Method(typeof(InputActionManager), "Update", Type.EmptyTypes), Callback: AccessTools.Method(typeof(PresentationInputGuard), nameof(InputPrefix)), Postfix: false),
            (Target: AccessTools.Method(typeof(CustomEventHandler), "Update", Type.EmptyTypes), Callback: AccessTools.Method(typeof(PresentationInputGuard), nameof(DoubleClickPrefix)), Postfix: false),
            (Target: AccessTools.Method(typeof(StoreUIManager), nameof(StoreUIManager.CheckRaycast), Type.EmptyTypes), Callback: AccessTools.Method(typeof(PresentationInputGuard), nameof(RaycastPrefix)), Postfix: false),
            (Target: AccessTools.Method(typeof(StoreUIManager), "Update", Type.EmptyTypes), Callback: AccessTools.Method(typeof(PresentationInputGuard), nameof(StoreUpdatePostfix)), Postfix: true)
        };
        foreach (var hook in hooks)
        {
            if (!OfficialStoryHooks.ValidateTarget(hook.Target, out var reason))
                throw new InvalidOperationException(reason);
            if (hook.Target!.ReturnType != typeof(void) || hook.Target.IsStatic || hook.Callback == null)
                throw new MissingMethodException("Guide input hook contract mismatch");
        }
        var harmony = new HarmonyLib.Harmony(HarmonyId);
        try
        {
            foreach (var hook in hooks)
                harmony.Patch(hook.Target!, prefix: hook.Postfix ? null : new HarmonyMethod(hook.Callback),
                    postfix: hook.Postfix ? new HarmonyMethod(hook.Callback) : null);
        }
        catch
        {
            try { harmony.UnpatchAll(harmony.Id); } catch { }
            throw;
        }
        _harmony = harmony;
    }

    internal static void Uninstall()
    {
        var harmony = _harmony;
        _harmony = null;
        _windowVisible = _resetSelectionPending = false; Capture.Reset();
        try { harmony?.UnpatchAll(harmony.Id); }
        catch { }
    }

    internal static void Opened()
    {
        _windowVisible = true;
        Capture.Open(Time.frameCount, MouseHeld);
        // Magnifier targeting opens this window inside native EndDrag. Resetting
        // there clears currentItem before its cleanup; wait for the next dispatch.
        _resetSelectionPending = true;
        InputActionManager.current?.Reset();
        CustomEventHandler.ClearDoubleClickTarget();
    }

    internal static void Closed()
    {
        _windowVisible = AnyVisible;
        if (_windowVisible) return;
        Capture.Close(Time.frameCount, MouseHeld);
    }

    private static bool BlocksBackground()
    {
        if (_windowVisible && !AnyVisible) Closed();
        return Capture.BlocksPointer(Time.frameCount,
            PointerOverWindow, MouseHeld);
    }

    private static bool InputPrefix(InputActionManager __instance, ref Vector3 ___lastMousePosition)
    {
        if (_resetSelectionPending)
        {
            _resetSelectionPending = false;
            ItemMouseDragHandler.current?.UpdateReset();
        }
        if (PointerOverWindow &&
            (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2)))
        {
            // Native targeting can find inventory behind a plain page Image.
            // Cancel that placement while still letting the release be dispatched.
            var drag = ItemMouseDragHandler.current;
            drag?.UpdateReset();
        }
        if (!BlocksBackground()) return true;
        __instance.Reset();
        ___lastMousePosition = Input.mousePosition;
        // These windows' Unity buttons and EventTrigger still run through the
        // EventSystem. This manager dispatches the inventory/page underneath.
        return false;
    }

    private static bool DoubleClickPrefix(CustomEventHandler __instance, ref TreeNodeRender? ___lastHandlerClicked,
        ref bool ___hasRayCast, ref float ___timeDeltaSeconds)
    {
        if (!BlocksBackground()) return true;
        ___lastHandlerClicked = null;
        ___hasRayCast = false;
        ___timeDeltaSeconds = __instance.doubleClickTimeThreshold;
        return false;
    }

    private static bool RaycastPrefix(StoreUIManager __instance)
    {
        if (!PointerOverWindow && !BlocksBackground()) return true;
        __instance.hoveringStoreUI = true;
        return false;
    }

    private static void StoreUpdatePostfix(StoreUIManager __instance)
    {
        if (PointerOverWindow || BlocksBackground())
            __instance.hoveringStoreUI = true;
    }
}
