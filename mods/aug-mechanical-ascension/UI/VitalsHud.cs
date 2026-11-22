using AugMechanicalAscension.Domain;
using UnityEngine;

namespace AugMechanicalAscension.UI;

// Passive screen-space HUD, independent of the calendar and debug windows.
internal static class VitalsHud
{
    private static Texture2D? _eyeTexture;
    private static Texture2D? _crossTexture;
    private static GUIStyle? _valueStyle;
    private static GUIStyle? _eyeStyle;
    private static GUIStyle? _crossStyle;
    private static int _fontSize;
    private static int _health, _maxHealth, _exposure, _maxExposure;
    private static string _healthText = "", _exposureText = "";

    internal static void Draw(RunState state)
    {
        // Fixed rectangles need no layout or input pass; Unity calls OnGUI multiple times.
        if (Event.current?.type != EventType.Repaint) return;
        if (state.InitPhase != "complete" || state.FailureReason != "none") return;
        if (AppDomain.CurrentDomain.GetData("nicokobo.definitely_stolen.TheftPanelVisible") is Func<bool> theftVisible && theftVisible()) return;
        _eyeTexture ??= CreateIcon(false);
        _crossTexture ??= CreateIcon(true);
        _valueStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(0, 0, 0, 0)
        };
        _eyeStyle ??= TextureStyle(_eyeTexture);
        _crossStyle ??= TextureStyle(_crossTexture);
        float scale = Mathf.Clamp(Screen.height / 1080f, 0.65f, 1.5f);
        int fontSize = Mathf.RoundToInt(22f * scale);
        if (_fontSize != fontSize)
        {
            _fontSize = fontSize;
            _valueStyle.fontSize = fontSize;
            _valueStyle.normal.textColor = Color.white;
        }
        if (_healthText.Length == 0 || _health != state.Health || _maxHealth != state.ConfigSnapshot.MaxHealth)
        {
            _health = state.Health; _maxHealth = state.ConfigSnapshot.MaxHealth;
            _healthText = $"{_health}/{_maxHealth}";
        }
        if (_exposureText.Length == 0 || _exposure != state.Exposure || _maxExposure != state.ConfigSnapshot.MaxExposure)
        {
            _exposure = state.Exposure; _maxExposure = state.ConfigSnapshot.MaxExposure;
            _exposureText = $"{_exposure}/{_maxExposure}";
        }
        float width = 332f * scale;
        float x = Math.Max(8f, Screen.width - width - 20f * scale);
        float y = 16f * scale;
        var oldColor = GUI.color;
        int oldDepth = GUI.depth;
        try
        {
            GUI.depth = -100;
            DrawVital(x + 12f * scale, y, scale, _eyeStyle,
                state.Exposure, state.ConfigSnapshot.MaxExposure, false, _exposureText);
            DrawVital(x + 178f * scale, y, scale, _crossStyle,
                state.Health, state.ConfigSnapshot.MaxHealth, true, _healthText);
        }
        finally
        {
            GUI.color = oldColor;
            GUI.depth = oldDepth;
        }
    }

    private static void DrawVital(float x, float y, float scale, GUIStyle iconStyle,
        int value, int maximum, bool health, string text)
    {
        float ratio = maximum > 0 ? (float)value / maximum : 0f;
        float danger = health ? 1f - ratio : ratio;
        GUI.color = danger >= 0.75f ? new Color(1f, 0.35f, 0.32f)
            : danger >= 0.5f ? new Color(1f, 0.76f, 0.25f)
            : new Color(0.36f, 0.88f, 0.57f);
        GUI.Label(new Rect(x, y + 10f * scale, 28f * scale, 28f * scale), "", iconStyle);
        GUI.Label(new Rect(x + 36f * scale, y, 112f * scale, 48f * scale),
            text, _valueStyle);
    }

    // GUI.DrawTexture is an unstripped stub in this game's IL2CPP assemblies.
    // A native Label renders its style background without that managed call path.
    private static GUIStyle TextureStyle(Texture2D texture)
    {
        var style = new GUIStyle();
        style.normal.background = texture;
        return style;
    }

    // Code-drawn icons avoid depending on glyph coverage in the game's font.
    private static Texture2D CreateIcon(bool health)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.filterMode = FilterMode.Bilinear;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f - size / 2f) / (size / 2f);
            float dy = (y + 0.5f - size / 2f) / (size / 2f);
            bool filled = health
                ? (Math.Abs(dx) < 0.22f && Math.Abs(dy) < 0.8f) ||
                  (Math.Abs(dy) < 0.22f && Math.Abs(dx) < 0.8f)
                : (Math.Abs(dx) < 0.92f &&
                   Math.Abs(Math.Abs(dy) - 0.55f * (1f - dx * dx / 0.85f)) < 0.075f) ||
                  dx * dx + dy * dy < 0.065f;
            pixels[y * size + x] = filled ? Color.white : Color.clear;
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

}
