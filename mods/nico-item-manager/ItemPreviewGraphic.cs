using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NicoItemManager
{
    internal sealed class ItemPreviewGraphic
    {
        readonly RectTransform root;
        readonly Image baseImage;
        readonly List<Image> overlays = new List<Image>();
        readonly CustomUITooltipTrigger tooltip;

        internal ItemPreviewGraphic(RectTransform root)
        {
            this.root = root;
            baseImage = Image("Base", root);
            tooltip = root.gameObject.AddComponent<CustomUITooltipTrigger>();
        }

        internal void Show(GameItem item, string description)
        {
            Sprite sprite = item is GameItemElement element ? element.ResolveSpriteByName(item.spritePath)
                : RenderHandler.LoadFromAtlas(item.spriteAtlasPath, item.spritePath);
            tooltip.tooltipText = description;
            tooltip.enabled = !string.IsNullOrWhiteSpace(description);
            if (sprite == null)
            {
                baseImage.enabled = false;
                foreach (Image overlay in overlays) overlay.enabled = false;
                return;
            }
            var modifiers = (item.getItemSpriteModifierForItemFunc?.Invoke(item) ?? new List<ItemSpriteModifier>())
                .Where(layer => layer.sprite != null).OrderBy(layer => layer.priority).ToList();
            float left = 0, top = 0, right = sprite.rect.width, bottom = -sprite.rect.height;
            foreach (ItemSpriteModifier layer in modifiers)
            {
                bool turned = (layer.orientation & 1) != 0;
                left = Mathf.Min(left, layer.position.x);
                top = Mathf.Max(top, layer.position.y);
                right = Mathf.Max(right, layer.position.x + (turned ? layer.sprite.rect.height : layer.sprite.rect.width));
                bottom = Mathf.Min(bottom, layer.position.y - (turned ? layer.sprite.rect.width : layer.sprite.rect.height));
            }
            float scale = Mathf.Min(148 / Math.Max(1, right - left), 128 / Math.Max(1, top - bottom));
            Vector2 origin = new Vector2(6 + (148 - (right - left) * scale) / 2 - left * scale,
                -6 - (128 - (top - bottom) * scale) / 2 - top * scale);
            Place(baseImage, sprite, Vector2.zero, 0, false, Color.white, origin, scale);
            for (int i = 0; i < modifiers.Count; i++)
            {
                ItemSpriteModifier layer = modifiers[i];
                if (overlays.Count <= i) overlays.Add(Image("Layer", root));
                Image image = overlays[i];
                image.transform.SetAsLastSibling();
                Place(image, layer.sprite, layer.position, layer.orientation, layer.isFlipped, layer.color,
                    origin, scale);
            }
            for (int i = modifiers.Count; i < overlays.Count; i++) overlays[i].enabled = false;
        }

        static Image Image(string name, RectTransform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            var image = obj.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        static void Place(Image image, Sprite sprite, Vector2 position, int orientation, bool flip,
            Color color, Vector2 origin, float scale)
        {
            image.enabled = true;
            image.sprite = sprite;
            image.color = color;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            // Match GameItemElement's native overlay pivots, rotation and Y axis.
            orientation = (orientation % 4 + 4) % 4;
            float side = flip ? 1 : 0;
            rect.pivot = orientation == 0 ? new Vector2(side, 1)
                : orientation == 1 ? new Vector2(1 - side, 1)
                : orientation == 2 ? new Vector2(1 - side, 0) : new Vector2(side, 0);
            rect.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height) * scale;
            rect.anchoredPosition = origin + position * scale;
            rect.localEulerAngles = new Vector3(0, 0, 90 * orientation);
            rect.localScale = new Vector3(flip ? -1 : 1, 1, 1);
        }
    }
}
