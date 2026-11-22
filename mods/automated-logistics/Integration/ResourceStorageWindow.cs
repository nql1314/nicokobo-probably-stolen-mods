using static PsModI18n.I18n;
using UnityEngine;
using UnityEngine.UI;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class TerminalWindow
{
    private void RenderResourceFilters()
    {
        Field("StorageSearch", _storageSearch, PsModI18n.I18n.Get("name_item_id"), -307, 155, 378, value => _storageSearch = value, ended: ResetInventorySelection);
        Field("StorageTypeFilter", _typeFilter, PsModI18n.I18n.Get("type"), -1, 155, 206, value => _typeFilter = value, ended: ResetInventorySelection);
        Button(PsModI18n.I18n.Get("pick"), 133, 155, 62, () => OpenTransfer("filter-type"), size: CaptionSize);
        Field("StorageTagFilter", _tagFilter, PsModI18n.I18n.Get("tag"), 289, 155, 214, value => _tagFilter = value, ended: ResetInventorySelection);
        Button(PsModI18n.I18n.Get("tags"), 456, 155, 72, () => OpenTransfer("filter-tag"), size: CaptionSize);
    }
    private void RenderResourceFilterPicker(bool tag)
    {
        var panel = Popup("ResourceFilterPicker", tag ? PsModI18n.I18n.Get("filter_by_tag_match_all_selected_tags") : PsModI18n.I18n.Get("filter_by_type_match_any_selected_type"));
        var items = runtime.ResourceContents().Where(view => !view.LiquidEndpoint).Select(view => view.Item).ToArray();
        var selected = Tokens(tag ? _tagFilter : _typeFilter).ToHashSet(StringComparer.Ordinal);
        var options = items.SelectMany(item => ResourceFilterOptions(item, tag))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal)
            .Select(value => (Id: value, Items: items.Where(item => tag ? item.IsTag(value) : item.IsGameItemType(value)).ToArray()))
            .Where(option => option.Items.Length > 0).OrderBy(option => selected.Contains(option.Id) ? 0 : 1).ThenBy(option => option.Id, StringComparer.Ordinal).ToArray();
        Text(PsModI18n.I18n.Get("choose_from_current_inventory_or_enter_multiple"), 0, 218, 940, 34, BodySize, panel);
        var list = Scroll("ResourceFilterOptions", 0, -8, 954, 394, (options.Length + 1) / 2 * 62 + 16, panel);
        for (int index = 0; index < options.Length; index++)
        {
            var option = options[index]; bool enabled = selected.Contains(option.Id);
            Button((enabled ? "✓ " : "□ ") + option.Id + " · " + option.Items.Length + PsModI18n.I18n.Get("entries_example") + MachineAccess.DisplayName(option.Items[0]),
                index % 2 == 0 ? -235 : 235, Row(list, index / 2, 62), 452, () =>
                {
                    if (!selected.Add(option.Id)) selected.Remove(option.Id);
                    string value = string.Join(",", selected.OrderBy(id => id, StringComparer.Ordinal));
                    if (tag) _tagFilter = value; else _typeFilter = value;
                    ResetInventorySelection();
                }, list, selected: enabled, height: 54, left: true, size: CaptionSize);
        }
        if (options.Length == 0) Text(PsModI18n.I18n.Get("no_options_in_the_current_inventory"), 0, 40, 900, 80, BodySize, panel);
        Button(PsModI18n.I18n.Get("clear_filters"), -244, -251, 440, () =>
        { if (tag) _tagFilter = ""; else _typeFilter = ""; ResetInventorySelection(); _transferMode = ""; }, panel);
        Button(PsModI18n.I18n.Get("apply_filters"), 244, -251, 440, () => _transferMode = "", panel, selected: true);
    }
    private static IEnumerable<string> ResourceFilterOptions(GameItem item, bool tag)
    {
        if (tag) { foreach (var key in item.modifiedState.dict.Keys) yield return key; }
        else { foreach (var value in item.GetGameItemType()) yield return value; }
    }
}
