using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace Nicokobo.ContentInterop;

[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ValidateSelect), new[] { typeof(bool) })]
internal static class ModItemHitAreaPatch
{
    private static readonly ConditionalWeakTable<GameItemElement, ModItemHitArea> HitAreas = new();
    private static bool _enabled;

    internal static void Enable() => _enabled = true;

    internal static void Disable()
    {
        _enabled = false;
        foreach (var entry in HitAreas)
        {
            var area = entry.Value;
            if (area == null) continue;
            area.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(area.gameObject);
        }
        HitAreas.Clear();
    }

    private static void Postfix(GameItemElement __instance)
    {
        if (!_enabled || HitAreas.TryGetValue(__instance, out _)) return;
        if (__instance.useSpriteFromMod || ModSourceHelper.GetItemModName(__instance) != null)
            HitAreas.Add(__instance, ModItemHitArea.Create(__instance));
    }
}

internal sealed class ModItemHitArea : MonoBehaviour, ICanvasRaycastFilter
{
    private GameItemElement _item = null!;
    private RectTransform _bounds = null!;

    internal static ModItemHitArea Create(GameItemElement item)
    {
        // Follow the real item's rotation and flip. Let its sprite and controls take hits first.
        var node = TreeNodeRender.Instantiate().SetParent(item.rotator);
        node.gameObject.name = "Mod Item Hit Area";
        var bounds = node.rectTransform;
        bounds.anchorMin = Vector2.zero;
        bounds.anchorMax = Vector2.one;
        bounds.offsetMin = Vector2.zero;
        bounds.offsetMax = Vector2.zero;
        bounds.SetAsFirstSibling();

        node.eventHandler = item.background.eventHandler;
        node.image.sprite = null;
        node.image.color = Color.clear;
        node.image.canvasRenderer.cullTransparentMesh = false;
        node.image.raycastTarget = true;
        node.image.enabled = true;

        var area = node.gameObject.AddComponent<ModItemHitArea>();
        area._item = item;
        area._bounds = bounds;
        return area;
    }

    public bool IsRaycastLocationValid(Vector2 screenPosition, Camera eventCamera)
    {
        var background = _item.background.image;
        if (!background.isActiveAndEnabled || !background.raycastTarget ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(_bounds, screenPosition, eventCamera, out var point))
            return false;

        var shape = _item.modifiedShape;
        var rect = _bounds.rect;
        int gridSize = UISettings.current.gridSize;
        int x = Mathf.FloorToInt((point.x - rect.xMin) / gridSize);
        int y = Mathf.FloorToInt((rect.yMax - point.y) / gridSize);
        return x >= 0 && x < shape.width && y >= 0 && y < shape.height && shape.GetLocal(x, y) != 0;
    }
}
