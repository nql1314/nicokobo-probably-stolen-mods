using UnityEngine;

namespace SpeedUp
{
    internal static class SpeedNotice
    {
        static string message;
        static float until;
        static GUIStyle labelStyle, shadowStyle;

        internal static void Clear() => message = null;

        internal static void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            message = text;
            until = Time.unscaledTime + 2.5f;
        }

        internal static void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            bool notice = !string.IsNullOrEmpty(message) && Time.unscaledTime < until;
            if (!notice && !SpeedUpSettings.ShowSpeedStatus) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter, fontSize = 20,
                    fontStyle = FontStyle.Bold, wordWrap = false
                };
                labelStyle.normal.textColor = Color.white;
                shadowStyle = new GUIStyle(labelStyle);
                shadowStyle.normal.textColor = new Color(0f, 0f, 0f, .75f);
            }
            float width = Mathf.Min(1000f, Screen.width - 16f);
            float x = (Screen.width - width) * .5f;
            string text = notice ? message : SpeedUpSettings.SpeedSummary();
            if (GlobalSpeed.Active) text += "\n" + GlobalSpeed.Status;
            shadowStyle.Draw(new Rect(x + 1f, 57f, width, 64f), new GUIContent(text), false, false, false, false);
            labelStyle.Draw(new Rect(x, 56f, width, 64f), new GUIContent(text), false, false, false, false);
        }
    }
}
