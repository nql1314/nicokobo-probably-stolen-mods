using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StockReroll
{
    internal static class TextInputFocus
    {
        static int blockedFrame = -1;

        internal static bool ShouldSuppressHotkeys()
        {
            if (KeyBindHelper.SuppressHotkeys || !Application.isFocused || blockedFrame == Time.frameCount)
                return true;
            if (CustomUIManager.Instance != null
                && CustomUIManager.Instance.IsOpen("nicokobo.hotkey-manager.window"))
                return true;

            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            TMP_InputField tmp = selected != null ? selected.GetComponentInParent<TMP_InputField>() : null;
            InputField legacy = selected != null ? selected.GetComponentInParent<InputField>() : null;
            bool editing = StoreUIManager.IsTextFieldFocused() || !string.IsNullOrEmpty(Input.compositionString)
                || tmp != null && tmp.isActiveAndEnabled && tmp.isFocused
                || legacy != null && legacy.isActiveAndEnabled && legacy.isFocused;
            if (editing)
                blockedFrame = Time.frameCount;
            return editing;
        }
    }
}
