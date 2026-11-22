using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NicoItemManager
{
    internal sealed partial class ManagerPanel
    {
        sealed class FilterChip
        {
            internal string Key;
            internal CustomUIElement Button, Count;
        }

        readonly List<FilterChip> collectionChips = new List<FilterChip>();
        readonly List<FilterChip> categoryChips = new List<FilterChip>();
        RectTransform categoryFilterRoot;
        bool filtersReady, filtersLayoutDirty;

        void BuildFilters(CustomUIBuilder builder)
        {
            BuildFilterStrip(builder, Text.Get("collection"),
                CatalogFilterGroups.CollectionKeys, true, collectionChips);
            builder.BeginColumn(6);
            categoryFilterRoot = builder.CurrentContainer;
            builder.End();
            BuildCategoryFilters();
        }

        void BuildCategoryFilters()
        {
            CustomUIBuilder.ClearContainer(categoryFilterRoot);
            categoryChips.Clear();
            filtersReady = owner.Catalog.Ready;
            // Keep the layout stable during loading; hide unused categories once at completion.
            IEnumerable<string> keys = filtersReady ? owner.Catalog.Categories : CatalogFilterGroups.OrderedKeys;
            if (collection != null) keys = keys.Where(key => CatalogFilterGroups.CollectionFor(key) == collection);
            BuildFilterStrip(new CustomUIBuilder(CustomUIManager.Instance, window, categoryFilterRoot),
                Text.Get("category"), keys, false, categoryChips);
            filtersLayoutDirty = true;
        }

        void BuildFilterStrip(CustomUIBuilder builder, string title, IEnumerable<string> keys,
            bool isCollection, List<FilterChip> chips)
        {
            builder.BeginRow(12, TextAnchor.UpperLeft);
            builder.AddLabel(title);
            CustomUIElement heading = builder.Last;
            Label(heading, 16);
            Fixed(heading, Mathf.Max(64, Mathf.Ceil(heading.text.GetPreferredValues(title).x)), 38);
            float headingWidth = heading.root.GetComponent<LayoutElement>().preferredWidth;
            float available = window.ContentRoot.rect.width - headingWidth - 12;
            builder.BeginColumn(6);
            builder.CurrentContainer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            builder.BeginRow(6, TextAnchor.MiddleLeft);
            float used = 0;
            LoadedMod assets = ModLoader.Mods.FirstOrDefault(mod => mod.Manifest.ID == NicoItemManagerMod.Id);
            foreach (string key in new string[] { null }.Concat(keys))
            {
                string label = isCollection ? CatalogFilterGroups.CollectionDisplay(key) : CatalogFilterGroups.Display(key);
                float width = Mathf.Min(available, Mathf.Ceil(heading.text.GetPreferredValues(label).x * 18 / 16 + 96));
                if (used > 0 && used + 6 + width > available)
                {
                    builder.End();
                    builder.BeginRow(6, TextAnchor.MiddleLeft);
                    used = 0;
                }
                string choice = key;
                builder.AddButton(label, () => SelectFilter(choice, isCollection));
                CustomUIElement button = builder.Last;
                Fixed(button, width, 38);
                Label(button, 18);
                var layout = button.root.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = 6;
                layout.padding = new RectOffset(10, 10, 0, 0);
                button.text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                button.image.sprite = MachineryStyle.TabPlate;
                button.image.type = Image.Type.Sliced;
                button.image.color = Color.white;
                button.button.transition = Selectable.Transition.ColorTint;
                var content = new CustomUIBuilder(CustomUIManager.Instance, window, (RectTransform)button.root.transform);
                Sprite icon = null;
                assets?.PaperSprites.TryGetValue("filter_" + (key ?? "all"), out icon);
                content.AddImage(icon, 22, 22);
                Fixed(content.Last, 22, 22);
                content.Last.image.raycastTarget = false;
                content.Last.image.enabled = icon != null;
                content.Last.root.transform.SetAsFirstSibling();
                content.AddLabel("0");
                CustomUIElement count = content.Last;
                Label(count, 14);
                count.text.alignment = TextAlignmentOptions.MidlineRight;
                count.text.raycastTarget = false;
                Fixed(count, 34, 28);
                chips.Add(new FilterChip { Key = key, Button = button, Count = count });
                used += (used > 0 ? 6 : 0) + width;
            }
            builder.End();
            builder.End();
            builder.End();
        }

        void SelectFilter(string key, bool isCollection)
        {
            if (isCollection)
            {
                if (collection == key) return;
                collection = key;
                category = null;
                BuildCategoryFilters();
            }
            else category = key;
            page = 0;
            BackToCatalog();
            UpdateFilters();
            Filter();
            RequestRebuild();
        }

        void UpdateFilters()
        {
            if (categoryFilterRoot == null) return;
            if (filtersReady != owner.Catalog.Ready) BuildCategoryFilters();
            if (filtersReady && category != null && !owner.Catalog.Categories.Contains(category)) category = null;
            var categoryCounts = new Dictionary<string, int>();
            var collectionCounts = new Dictionary<string, int>();
            var parents = new HashSet<string>();
            foreach (CatalogEntry entry in owner.Catalog.Entries)
            {
                parents.Clear();
                foreach (string key in entry.Groups)
                {
                    categoryCounts.TryGetValue(key, out int count);
                    categoryCounts[key] = count + 1;
                    parents.Add(CatalogFilterGroups.CollectionFor(key));
                }
                // Count each item once per collection even if several child categories match.
                foreach (string parent in parents)
                {
                    collectionCounts.TryGetValue(parent, out int count);
                    collectionCounts[parent] = count + 1;
                }
            }
            int all = owner.Catalog.Entries.Count;
            RefreshFilterChips(collectionChips, collection, collectionCounts, all);
            if (collection != null) collectionCounts.TryGetValue(collection, out all);
            RefreshFilterChips(categoryChips, category, categoryCounts, all);
        }

        static void RefreshFilterChips(List<FilterChip> chips, string selectedKey, Dictionary<string, int> counts, int all)
        {
            foreach (FilterChip chip in chips)
            {
                int count = all;
                if (chip.Key != null) counts.TryGetValue(chip.Key, out count);
                bool active = chip.Key == selectedKey;
                chip.Count.SetText(count.ToString());
                chip.Button.SetInteractable(chip.Key == null || count > 0);
                Color plate = MachineryStyle.TAB_PLATE;
                ColorBlock colors = chip.Button.button.colors;
                colors.normalColor = new Color(plate.r, plate.g, plate.b, active ? 1 : 0.12f);
                colors.highlightedColor = new Color(plate.r, plate.g, plate.b, active ? 1 : 0.3f);
                colors.pressedColor = new Color(plate.r, plate.g, plate.b, 0.7f);
                colors.selectedColor = colors.normalColor;
                colors.disabledColor = new Color(plate.r, plate.g, plate.b, 0.04f);
                colors.colorMultiplier = 1;
                chip.Button.button.colors = colors;
                Color text = active ? MachineryStyle.BUTTON_TEXT : MachineryStyle.BODY_TEXT;
                if (count == 0 && chip.Key != null) text.a = 0.4f;
                chip.Button.text.color = chip.Count.text.color = text;
                chip.Button.text.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            }
        }
    }
}
