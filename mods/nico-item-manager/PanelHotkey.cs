using System;
using UnityEngine;

namespace NicoItemManager
{
    internal sealed class PanelHotkey
    {
        string _raw;
        KeyCode _key;
        bool _ctrl, _alt, _shift;

        internal bool WasPressed(string raw)
        {
            if (!string.Equals(_raw, raw, StringComparison.Ordinal))
                Parse(raw);
            return _key != KeyCode.None && Input.GetKeyDown(_key)
                && _ctrl == (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && _alt == (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                && _shift == (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        void Parse(string raw)
        {
            _raw = raw;
            _key = KeyCode.None;
            if (string.IsNullOrWhiteSpace(raw)) return;
            KeyCode key = KeyCode.None;
            bool ctrl = false, alt = false, shift = false;
            foreach (string part in raw.Split('+'))
            {
                string token = part.Trim();
                switch (token.ToUpperInvariant())
                {
                    case "CTRL": case "CONTROL": case "LEFTCONTROL": case "RIGHTCONTROL": ctrl = true; continue;
                    case "ALT": case "LEFTALT": case "RIGHTALT": alt = true; continue;
                    case "SHIFT": case "LEFTSHIFT": case "RIGHTSHIFT": shift = true; continue;
                }
                if (token == "`") token = nameof(KeyCode.BackQuote);
                if (key != KeyCode.None || int.TryParse(token, out _)
                    || !Enum.TryParse(token, true, out key) || !Enum.IsDefined(typeof(KeyCode), key)
                    || key == KeyCode.None)
                    return;
            }
            _key = key;
            _ctrl = ctrl;
            _alt = alt;
            _shift = shift;
        }
    }
}
