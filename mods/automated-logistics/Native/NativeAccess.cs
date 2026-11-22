using HarmonyLib;
using UnityEngine;
using TMPro;

namespace Nicokobo.AutomatedLogistics.Native;

// Only inspected game/UI members are accessed here. No loader, scanner, trust,
// arbitrary assembly or filesystem access is involved.
internal static class NativeAccess
{
    private static readonly Dictionary<string, System.Reflection.FieldInfo> Fields = new();
    internal static T Get<T>(ItemContextHandler instance, string name)
    {
        if (!Fields.TryGetValue(name, out var field)) Fields.Add(name, field = AccessTools.Field(typeof(ItemContextHandler), name)
            ?? throw new MissingFieldException(nameof(ItemContextHandler), name));
        return (T)field.GetValue(instance);
    }
    private static readonly System.Reflection.FieldInfo TextNode = AccessTools.Field(typeof(RichTextElement), "textNode");
    internal static TMP_Text Text(RichTextElement element) => ((TreeNodeRender)TextNode.GetValue(element)).text;
    internal static void Reset(InputActionHandler? handler) => handler?.UpdateReset();
    internal static Dictionary<string, Dictionary<string, Sprite>> Atlases =>
        (Dictionary<string, Dictionary<string, Sprite>>)AccessTools.Field(typeof(RenderHandler), "atlasCache").GetValue(null);
    internal static Sprite? Sprite(GameItemElement element) => (Sprite?)AccessTools.Method(typeof(GameItemElement), "ResolveItemSprite").Invoke(element, null);
    internal static int CellCount(GridShape shape)
    { int count = 0; for (int y = shape.minY; y <= shape.maxY; y++) for (int x = shape.minX; x <= shape.maxX; x++) if (shape.Get(x, y) == 1) count++; return count; }
    internal static void FixTooltipBounds(this PixelWindow window, Vector2 point) => AccessTools.Method(typeof(PixelWindow), "FixTooltipBounds").Invoke(window, new object[] { point });
    internal static void UnpatchSelf(this Harmony harmony) => harmony.UnpatchAll(harmony.Id);
}
