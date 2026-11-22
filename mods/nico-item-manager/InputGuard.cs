using HarmonyLib;
using UnityEngine;

namespace NicoItemManager
{
    // These narrow patches only block the game's pixel UI while our modal is
    // visible. Unity's EventSystem stays enabled for the native window controls.
    [HarmonyPatch(typeof(InputActionManager), "Update")]
    internal static class InputGuard
    {
        static bool Prefix(InputActionManager __instance, ref Vector3 ___lastMousePosition)
        {
            if (NicoItemManagerMod.Active?.Panel?.BlocksGameInput != true) return true;
            __instance.Reset();
            foreach (InputActionHandler handler in __instance.actionHandlers) handler.UpdateReset();
            ___lastMousePosition = Input.mousePosition;
            return false;
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.ResolveEscape))]
    internal static class EscapeGuard
    {
        static bool Prefix(ref bool __result)
        {
            if (NicoItemManagerMod.Active?.Panel?.BlocksGameInput != true) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), "IsTyping")]
    internal static class GameplayTypingGuard
    {
        static void Postfix(ref bool __result)
        {
            if (NicoItemManagerMod.Active?.Panel?.BlocksGameInput == true) __result = true;
        }
    }

    [HarmonyPatch(typeof(KeyBindHelper), nameof(KeyBindHelper.GetKeyDown), new[] { typeof(string) })]
    internal static class GameplayHotkeyGuard
    {
        static bool Prefix(string __0, ref bool __result)
        {
            // Other vanilla hotkeys are already gated by StoreUIManager.IsTyping.
            if (__0 != "advance_dialogue" || NicoItemManagerMod.Active?.Panel?.BlocksGameInput != true) return true;
            __result = false;
            return false;
        }
    }
}
