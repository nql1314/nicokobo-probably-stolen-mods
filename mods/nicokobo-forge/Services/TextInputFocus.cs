#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nicokobo.Forge.Input
{
    internal static class TextInputFocus
    {
        const string StateKey = "Nicokobo.Mods.TextInputFocus";
        internal const string SuppressHotkeysKey = "Nicokobo.Mods.SuppressTextInputHotkeys";
        static readonly Tuple<Dictionary<string, Func<bool>>, int[]> State = GetSharedState();

        static Tuple<Dictionary<string, Func<bool>>, int[]> GetSharedState()
        {
            // Independent utility Mods also publish focus here using BCL types.
            var domain = AppDomain.CurrentDomain;
            if (domain.GetData(StateKey) is Tuple<Dictionary<string, Func<bool>>, int[]> state)
                return state;
            state = Tuple.Create(new Dictionary<string, Func<bool>>(), new[] { -1 });
            domain.SetData(StateKey, state);
            domain.SetData(SuppressHotkeysKey, (Func<bool>)ShouldSuppressHotkeys);
            return state;
        }

        internal static void Register(string owner, Func<bool> isEditing) => State.Item1[owner] = isEditing;
        internal static void Unregister(string owner) => State.Item1.Remove(owner);

        internal static void BlockCurrentFrame() => State.Item2[0] = Time.frameCount;

        internal static bool ShouldSuppressHotkeys()
        {
            if (!Application.isFocused || State.Item2[0] == Time.frameCount)
                return true;

            if (!HasTextFocus())
                return false;

            // Submit, cancel or a click can end editing during another Mod's
            // Update. Keep that frame consumed for all later shortcut handlers.
            BlockCurrentFrame();
            return true;
        }

        static bool HasTextFocus()
        {
            foreach (Func<bool> isEditing in State.Item1.Values)
                if (isEditing())
                    return true;

            // Unity wrappers can outlive their native objects during scene changes.
            var eventSystem = EventSystem.current;
            var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected != null)
            {
                // The single-argument helper is absent from some generated bindings.
                // Call the native overload with the same includeInactive=false behavior.
                var tmp = selected.GetComponentInParent(typeof(TMP_InputField), false) as TMP_InputField;
                if (tmp != null && tmp.isActiveAndEnabled && tmp.isFocused)
                    return true;
                var legacy = selected.GetComponentInParent(typeof(InputField), false) as InputField;
                if (legacy != null && legacy.isActiveAndEnabled && legacy.isFocused)
                    return true;
            }

            return !string.IsNullOrEmpty(UnityEngine.Input.compositionString);
        }
    }
}
