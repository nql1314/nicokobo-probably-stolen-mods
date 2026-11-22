using System;
using UnityEngine;

namespace SpeedUp
{
    internal readonly struct KeyCombination
    {
        internal KeyCode Key { get; }
        readonly bool control, alt, shift;

        KeyCombination(KeyCode key, bool control, bool alt, bool shift)
        { Key = key; this.control = control; this.alt = alt; this.shift = shift; }

        internal static bool TryParse(string text, out KeyCombination combination)
        {
            combination = default;
            if (string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), "None", StringComparison.OrdinalIgnoreCase)) return true;
            string[] parts = text.Split('+');
            bool ctrl = false, alt = false, shift = false;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string modifier = parts[i].Trim();
                if (modifier.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) && !ctrl) ctrl = true;
                else if (modifier.Equals("Alt", StringComparison.OrdinalIgnoreCase) && !alt) alt = true;
                else if (modifier.Equals("Shift", StringComparison.OrdinalIgnoreCase) && !shift) shift = true;
                else return false;
            }
            string keyName = parts[parts.Length - 1].Trim();
            if (keyName.Length == 0 || !char.IsLetter(keyName[0]) || !Enum.TryParse(keyName, true, out KeyCode key)
                || !Enum.IsDefined(typeof(KeyCode), key) || !IsKeyboardKey(key)) return false;
            combination = new KeyCombination(key, ctrl, alt, shift);
            return true;
        }

        static bool IsKeyboardKey(KeyCode key) => key > KeyCode.None && key < KeyCode.Mouse0 &&
            key != KeyCode.Escape && key != KeyCode.LeftControl && key != KeyCode.RightControl &&
            key != KeyCode.LeftAlt && key != KeyCode.RightAlt && key != KeyCode.LeftShift && key != KeyCode.RightShift &&
            key != KeyCode.LeftCommand && key != KeyCode.RightCommand && key != KeyCode.LeftWindows && key != KeyCode.RightWindows;

        internal bool IsPressed()
        {
            if (Key == KeyCode.None || control != (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                || alt != (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                || shift != (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return false;
            return Input.GetKeyDown(Key) || Key == KeyCode.Equals && Input.GetKeyDown(KeyCode.KeypadPlus)
                || Key == KeyCode.Minus && Input.GetKeyDown(KeyCode.KeypadMinus);
        }

        public override string ToString() => Key == KeyCode.None ? "None" :
            (control ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "") + Key;
    }
}
