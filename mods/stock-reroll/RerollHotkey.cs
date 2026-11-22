using System;
using UnityEngine;

namespace StockReroll
{
    internal readonly struct RerollHotkey
    {
        readonly KeyCode _key;
        readonly bool _ctrl;
        readonly bool _alt;
        readonly bool _shift;

        RerollHotkey(KeyCode key, bool ctrl, bool alt, bool shift)
        {
            _key = key;
            _ctrl = ctrl;
            _alt = alt;
            _shift = shift;
        }

        public static bool TryParse(string value, out RerollHotkey hotkey)
        {
            hotkey = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
                return true;

            KeyCode key = KeyCode.None;
            bool ctrl = false;
            bool alt = false;
            bool shift = false;
            foreach (string part in value.Split('+'))
            {
                string token = part.Trim();
                switch (token.ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL":
                    case "LEFTCONTROL":
                    case "RIGHTCONTROL":
                        ctrl = true;
                        continue;
                    case "ALT":
                    case "LEFTALT":
                    case "RIGHTALT":
                        alt = true;
                        continue;
                    case "SHIFT":
                    case "LEFTSHIFT":
                    case "RIGHTSHIFT":
                        shift = true;
                        continue;
                }

                if (token == "`")
                    token = nameof(KeyCode.BackQuote);

                if (key != KeyCode.None
                    || int.TryParse(token, out _)
                    || !Enum.TryParse(token, true, out KeyCode parsedKey)
                    || !Enum.IsDefined(typeof(KeyCode), parsedKey)
                    || parsedKey == KeyCode.None)
                    return false;

                key = parsedKey;
            }

            if (key == KeyCode.None)
                return false;

            hotkey = new RerollHotkey(key, ctrl, alt, shift);
            return true;
        }

        public bool WasPressedThisFrame()
        {
            if (_key == KeyCode.None || !Input.GetKeyDown(_key))
                return false;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return ctrl == _ctrl && alt == _alt && shift == _shift;
        }
    }
}
