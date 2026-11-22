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
        sealed class RowView
        {
            internal CatalogEntry Entry;
            internal int Revision = -1;
            internal GameObject Root;
            internal CustomUIElement Icon, Generate;
            internal readonly List<CustomUIElement> Cells = new List<CustomUIElement>();
        }

        readonly List<RowView> rows = new List<RowView>();
        bool catalogBuilt;
        RectTransform catalogList;
        float[] columnWidths;
        CustomUIElement emptyCatalog, previousPage, nextPage, pageLabel, exportButton;

        void BuildCatalog()
        {
            var builder = new CustomUIBuilder(CustomUIManager.Instance, window, catalogRoot);
            builder.BeginRow(12, TextAnchor.MiddleLeft);
            RectTransform header = builder.CurrentContainer;
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
            builder.End();
            builder.AddSeparator();
            builder.BeginScrollFlexible(80);
            catalogList = builder.CurrentContainer;
            ScrollRect scroll = catalogList.GetComponentInParent<ScrollRect>();
            float scrollbarWidth = 0;
            if (scroll != null)
            {
                scroll.horizontal = false;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
                if (scroll.verticalScrollbar != null)
                    scrollbarWidth = Mathf.Max(16, ((RectTransform)scroll.verticalScrollbar.transform).rect.width) + 6;
                if (scroll.viewport != null)
                {
                    scroll.viewport.anchorMin = Vector2.zero;
                    scroll.viewport.anchorMax = Vector2.one;
                    scroll.viewport.offsetMin = Vector2.zero;
                    scroll.viewport.offsetMax = new Vector2(-scrollbarWidth, 0);
                }
                catalogList.anchorMin = new Vector2(0, 1);
                catalogList.anchorMax = Vector2.one;
                catalogList.pivot = new Vector2(0.5f, 1);
                catalogList.sizeDelta = new Vector2(0, catalogList.sizeDelta.y);
            }
            var layout = catalogList.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = new RectOffset();
                layout.spacing = 4;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
            }
            builder.AddLabel("");
            emptyCatalog = builder.Last;
            builder.End();
            builder.BeginRow(8, TextAnchor.MiddleLeft);
            builder.AddButton(Text.Get("previous"), () => { page--; RequestRebuild(); });
            previousPage = builder.Last.SetFixedWidth(124);
            builder.AddLabel("");
            pageLabel = builder.Last.SetFixedWidth(100);
            builder.AddButton(Text.Get("next"), () => { page++; RequestRebuild(); });
            nextPage = builder.Last.SetFixedWidth(124);
            builder.AddFlexibleSpace();
            builder.AddButton(Text.Get("export"), Export);
            exportButton = builder.Last.SetFixedWidth(174);
            builder.End();
            StyleControls(catalogRoot);
            CustomUIBuilder.RebuildLayout(window.ContentRoot);
            float available = catalogList.rect.width > 0 ? catalogList.rect.width
                : window.ContentRoot.rect.width - scrollbarWidth;
            float textWidth = Mathf.Max(350, available - 40 - 90 - 170 - 12 * 5);
            columnWidths = new[] { textWidth * 0.25f, textWidth * 0.50f, 90, textWidth * 0.25f };
            var headings = new CustomUIBuilder(CustomUIManager.Instance, window, header);
            Cell(headings, " ", 40, 17, 28);
            string[] labels = { "ID", Text.Get("name"),
                Text.Get("value"), Text.Get("types") };
            for (int i = 0; i < labels.Length; i++) Cell(headings, labels[i], columnWidths[i], 17, 28);
            Cell(headings, Text.Get("action"), 170, 17, 28);
            catalogBuilt = true;
        }

        RowView CreateRow()
        {
            var builder = new CustomUIBuilder(CustomUIManager.Instance, window, catalogList);
            builder.BeginRow(12, TextAnchor.MiddleLeft);
            var row = new RowView { Root = builder.CurrentContainer.gameObject };
            var background = row.Root.AddComponent<Image>();
            background.color = new Color(0.84f, 0.82f, 0.72f, rows.Count % 2 == 0 ? 0.08f : 0.025f);
            var interaction = row.Root.AddComponent<CatalogRow>();
            interaction.Background = background;
            interaction.NormalColor = background.color;
            interaction.OnSelect = () => OpenEditor(row.Entry);
            builder.AddImage((Sprite)null, 40, 40);
            row.Icon = builder.Last;
            Fixed(row.Icon, 40, 44);
            if (row.Icon.image != null) row.Icon.image.raycastTarget = false;
            for (int i = 0; i < columnWidths.Length; i++)
                row.Cells.Add(Cell(builder, "", columnWidths[i], i == 1 ? 18 : 16));
            row.Cells[2].text.alignment = TextAlignmentOptions.MidlineRight;
            builder.BeginRow(6, TextAnchor.MiddleLeft);
            var actions = builder.CurrentContainer.gameObject.AddComponent<LayoutElement>();
            actions.minWidth = actions.preferredWidth = 170;
            builder.AddButton(Text.Get("edit"), () => OpenEditor(row.Entry));
            Fixed(builder.Last, 76, 36);
            builder.AddButton(Text.Get("spawn"), () => Spawn(row.Entry.Id, 1));
            row.Generate = builder.Last;
            Fixed(row.Generate, 88, 36);
            builder.End();
            builder.End();
            StyleControls((RectTransform)row.Root.transform);
            return row;
        }

        bool RefreshCatalog()
        {
            bool changed = false;
            int pageCount = Math.Max(1, (filtered.Count + PageSize - 1) / PageSize);
            page = Math.Max(0, Math.Min(page, pageCount - 1));
            int count = Math.Min(PageSize, filtered.Count - page * PageSize);
            owner.Catalog.Prioritize(filtered.Skip(page * PageSize).Take(count).Select(entry => entry.Id));
            for (int i = 0; i < count; i++)
            {
                if (rows.Count <= i) rows.Add(CreateRow());
                RowView row = rows[i];
                CatalogEntry entry = filtered[page * PageSize + i];
                if (!row.Root.activeSelf) { row.Root.SetActive(true); changed = true; }
                if (!ReferenceEquals(row.Entry, entry) || row.Revision != entry.Revision)
                {
                    changed = true;
                    row.Entry = entry;
                    row.Revision = entry.Revision;
                    row.Icon.SetSprite(entry.Icon);
                    if (row.Icon.image != null) row.Icon.image.enabled = entry.Icon != null;
                    SetCell(row.Cells[0], entry.Id, columnWidths[0]);
                    SetCell(row.Cells[1], entry.Name, columnWidths[1]);
                    SetCell(row.Cells[2], !entry.Resolved ? "…" : entry.Error == null ? entry.Value.ToString("N0") : "—", columnWidths[2]);
                    SetCell(row.Cells[3], string.Join(" / ", entry.Categories.Select(CategoryLabels.Display)), columnWidths[3]);
                }
                row.Generate.SetInteractable(entry.Resolved && entry.Error == null && ItemSpawner.IsStoreReady());
            }
            for (int i = count; i < rows.Count; i++)
                if (rows[i].Root.activeSelf) { rows[i].Root.SetActive(false); changed = true; }
            changed |= emptyCatalog.root.activeSelf != (count == 0);
            emptyCatalog.SetVisible(count == 0).SetText(!ItemSpawner.IsStoreReady()
                ? Text.Get("enter_the_store_to_read_this_version")
                : owner.IsScanning ? Text.Get("reading") : Text.Get("no_match"));
            previousPage.SetInteractable(page > 0);
            nextPage.SetInteractable(page + 1 < pageCount);
            string pageText = $"{page + 1} / {pageCount}";
            changed |= pageLabel.GetText() != pageText;
            pageLabel.SetText(pageText);
            exportButton.SetInteractable(owner.Catalog.Ready && !owner.IsScanning);
            return changed;
        }

        static CustomUIElement Cell(CustomUIBuilder builder, string value, float width, float fontSize, float height = 44)
        {
            builder.AddLabel(value);
            CustomUIElement cell = builder.Last;
            Fixed(cell, width, height);
            Label(cell, fontSize);
            if (cell.text != null)
            {
                cell.text.margin = new Vector4(6, 0, 6, 0);
                cell.text.raycastTarget = false;
            }
            return cell;
        }

        static void SetCell(CustomUIElement cell, string value, float width)
        {
            cell.SetText(string.IsNullOrEmpty(value) ? "—" : value);
            bool tooltip = !string.IsNullOrEmpty(value) && cell.text.GetPreferredValues(value).x > width - 12;
            if (tooltip) cell.SetTooltip(value);
            var trigger = cell.root.GetComponent<CustomUITooltipTrigger>();
            if (trigger != null) trigger.enabled = tooltip;
        }
    }
}
