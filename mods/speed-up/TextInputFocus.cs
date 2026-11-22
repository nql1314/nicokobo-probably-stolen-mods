using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpeedUp
{
    internal static class TextInputFocus
    {
        static int blockedFrame = -1;

        internal static bool ShouldSuppressHotkeys()
        {
            if (KeyBindHelper.SuppressHotkeys || !Application.isFocused || blockedFrame == Time.frameCount) return true;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var tmp = selected != null ? selected.GetComponentInParent<TMP_InputField>() : null;
            var legacy = selected != null ? selected.GetComponentInParent<InputField>() : null;
            bool editing = StoreUIManager.IsTextFieldFocused() || !string.IsNullOrEmpty(Input.compositionString)
                || tmp != null && tmp.isActiveAndEnabled && tmp.isFocused
                || legacy != null && legacy.isActiveAndEnabled && legacy.isFocused;
            if (editing) blockedFrame = Time.frameCount;
            return editing;
        }
    }
}
