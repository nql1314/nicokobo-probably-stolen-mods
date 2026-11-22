using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nicokobo.ContentInterop;

// Graph elements use fixed positions; prose stays in the scrollable detail panel.
internal static class WorkshopGraph
{
    internal const float Width = 648, Height = 580;
    internal static readonly Color Accent = MachineryStyle.Hex("77d4d0");
    internal static readonly Color PanelTint = MachineryStyle.Hex("172b35");
    private static readonly Color Muted = MachineryStyle.Hex("78919c");
    private static readonly Color WireIdle = MachineryStyle.Hex("385c6b");

    internal static void Draw(CustomUIBuilder ui, OfficialWorkshopSnapshot snapshot,
        string? selectedId, Action<string> select)
    {
        ui.BeginPanel(Width, Height, TextAnchor.UpperLeft, 0);
        ui.CurrentContainer.GetComponent<VerticalLayoutGroup>().enabled = false;
        ui.AddImage(Nicokobo.Forge.ForgeMod.Id + ":workshop_circuit_board");
        Place(ui.Last, new(Width / 2, Height / 2), Width, Height);
        ui.Last.image.preserveAspect = false;
        var branches = snapshot.Groups.Where(group => group.EntryCount > 1).ToArray();
        for (int i = 0; i < branches.Length; i++)
        {
            float width = (Width - 48) / branches.Length;
            Label(ui, snapshot.English ? branches[i].EnglishTitle : branches[i].ChineseTitle,
                new(24 + width * (i + .5f), 64), width - 8, 32, 13);
            ui.Last.SetTextColor(Accent);
        }
        var positions = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.Entries.Count; i++)
        {
            var entry = snapshot.Entries[i];
            float angle = -Mathf.PI / 2 + 2 * Mathf.PI * i / snapshot.Entries.Count;
            float x = entry.GraphX ?? Mathf.Cos(angle) * .9f;
            float y = entry.GraphY ?? Mathf.Sin(angle) * .9f;
            positions[entry.Id] = new(Width / 2 + x * 250, 310 + y * 225);
        }
        foreach (var entry in snapshot.Entries)
        {
            foreach (string source in entry.Dependencies.Concat(entry.VisualLinks).Distinct(StringComparer.Ordinal))
            {
                if (!positions.TryGetValue(source, out var from)) continue;
                var prerequisite = snapshot.Entries.First(e => e.Id == source);
                var to = positions[entry.Id];
                var direction = (to - from).normalized;
                from += direction * (NodeSize(prerequisite) / 2 + 3);
                to -= direction * (NodeSize(entry) / 2 + 3);
                Color color = prerequisite.Unlocked ? Accent : WireIdle;
                Line(ui, from, to, new Color(color.r, color.g, color.b, .18f), 6);
                Line(ui, from, to, color, 2);
                if (entry.Dependencies.Contains(source))
                {
                    var arrow = Vector2.Lerp(from, to, .7f);
                    var normal = new Vector2(-direction.y, direction.x);
                    Line(ui, arrow - direction * 7 + normal * 4, arrow, color, 2);
                    Line(ui, arrow - direction * 7 - normal * 4, arrow, color, 2);
                }
            }
        }
        var frame = CustomUIManager.Instance.GetSprite(Nicokobo.Forge.ForgeMod.Id + ":workshop_node");
        int groupEnd = 0, groupIndex = 0;
        for (int i = 0; i < snapshot.Entries.Count; i++)
        {
            var entry = snapshot.Entries[i];
            while (groupIndex < snapshot.Groups.Count && i >= groupEnd + snapshot.Groups[groupIndex].EntryCount)
                groupEnd += snapshot.Groups[groupIndex++].EntryCount;
            var group = groupIndex < snapshot.Groups.Count ? snapshot.Groups[groupIndex] : null;
            var center = positions[entry.Id];
            bool selected = entry.Id == selectedId;
            float size = NodeSize(entry);
            if (selected)
            {
                ui.AddImage(frame);
                Place(ui.Last, center, size + 8, size + 8);
                ui.Last.SetImageColor(Accent);
            }
            ui.AddIconButton(frame, size, () => select(entry.Id));
            Place(ui.Last, center, size, size);
            ui.Last.SetImageColor(StateColor(entry));
            ui.Last.image.type = Image.Type.Simple;
            ui.Last.image.preserveAspect = true;
            ui.Last.SetTooltip((group == null ? "" : (snapshot.English ? group.EnglishTitle : group.ChineseTitle) + " · ") +
                entry.Title + "\n" + entry.StateText);
            var icon = ReadIcon(entry);
            if (icon != null)
            {
                ui.AddImage(icon);
                Place(ui.Last, center, size * .52f, size * .52f);
                ui.Last.SetImageColor(new Color(1, 1, 1, entry.PrerequisiteMet || entry.Completed == true ? 1 : .55f));
            }
            else if (size == 80)
            {
                Label(ui, "★", center, 44, 44, 32);
                ui.Last.SetTextColor(StateColor(entry));
            }
            // The small nodes follow the quest-book convention: item art and a
            // short state, with full names on hover and in the detail panel.
            if (size == 80)
            {
                Label(ui, entry.Title, center + new Vector2(0, 61), 160, 40, 16, true);
                ui.Last.SetTextColor(Accent);
            }
            string status = entry.Unlocked ? (ForgeText.Get("forge.claimed"))
                : entry.ActionEnabled ? (ForgeText.Get("forge.claim_2"))
                : entry.Completed == true ? (ForgeText.Get("forge.done"))
                : entry.NodeCostText;
            Label(ui, status, center + new Vector2(0, size / 2 + (size == 80 ? 56 : 12)), size == 80 ? 120 : 60, 20, 11, true);
            ui.Last.SetTextColor(StateColor(entry));
        }
        Label(ui, ForgeText.Get("forge.hover_name_click_details"), new(Width / 2, Height - 14), 300, 22, 12);
        ui.Last.SetTextColor(Muted);
        ui.End();
    }

    internal static Color StateColor(OfficialWorkshopEntry entry) => entry.Unlocked
        ? MachineryStyle.PROGRESS_FILL : entry.ActionEnabled || entry.Completed == true
            ? MachineryStyle.PROGRESS_FILL_WARNING : entry.PrerequisiteMet ? Accent : Muted;

    internal static Sprite? ReadIcon(OfficialWorkshopEntry entry)
    {
        var icon = NativeWorkshop.ReadIcon(entry.Id);
        if (icon != null) return icon;
        var proof = entry.Conditions.FirstOrDefault(condition => condition.Kind == OfficialWorkshopResourceKind.Item);
        var iconId = proof?.ResourceId ?? entry.Rewards.FirstOrDefault(reward => reward.Kind == OfficialWorkshopResourceKind.Item)?.ResourceId;
        return iconId == null ? null : RecipePresentation.ReadItem(iconId).Sprite;
    }

    private static float NodeSize(OfficialWorkshopEntry entry) => entry.GraphX == 0 && entry.GraphY == 0 ? 80 : 48;

    private static void Line(CustomUIBuilder ui, Vector2 from, Vector2 to, Color color, float width)
    {
        ui.AddImage(MachineryStyle.White);
        Place(ui.Last, (from + to) / 2, Vector2.Distance(from, to), width);
        ui.Last.Rect.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg);
        ui.Last.image.preserveAspect = false;
        ui.Last.SetImageColor(color);
    }

    private static void Label(CustomUIBuilder ui, string text, Vector2 center, float width, float height, float fontSize, bool backing = false)
    {
        if (backing)
        {
            ui.AddImage(MachineryStyle.White);
            Place(ui.Last, center, width, height);
            ui.Last.image.preserveAspect = false;
            ui.Last.SetImageColor(PanelTint);
        }
        ui.AddLabel(text);
        Place(ui.Last, center, width, height);
        ui.Last.text.fontSize = fontSize;
        ui.Last.text.alignment = TextAlignmentOptions.Center;
        ui.Last.text.enableWordWrapping = true;
        ui.Last.text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void Place(CustomUIElement element, Vector2 center, float width, float height)
    {
        element.root.GetComponent<LayoutElement>().ignoreLayout = true;
        element.Rect.anchorMin = element.Rect.anchorMax = new Vector2(0, 1);
        element.Rect.pivot = new Vector2(.5f, .5f);
        element.Rect.anchoredPosition = new Vector2(center.x, -center.y);
        element.Rect.sizeDelta = new Vector2(width, height);
    }
}
