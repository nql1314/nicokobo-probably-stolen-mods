using static PsModI18n.I18n;
using System.Globalization;
using TMPro;
using Nicokobo.AutomatedLogistics.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEventTrigger = UnityEngine.EventSystems.EventTrigger;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class TerminalWindow
{
    private const int StoragePageSize = 7;
    private bool _statisticsPage;
    private string _groupId = "", _storageSearch = "", _typeFilter = "", _tagFilter = "";
    private int _groupPage, _detailPage, _selectedInstance, _sort = 2, _detailSort;
    private bool _ascending;
    private string _transferMode = "";
    private readonly List<TMP_InputField> _networkFields = new();
    private ResourceItemView[] _resourceItems = [];
    private readonly Dictionary<int, string> _containerNames = new();
    private PixelWindow? _nativeHoverWindow;
    private RichTextElement? _nativeHoverText;
    private string _inspectionTitle = "", _inspectionText = "";
    private sealed record StorageGroup(string Id, string Name, GameItem[] Items, long Count, decimal? Value);
    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Price(decimal? value) => value.HasValue ? Number(value.Value) : PsModI18n.I18n.Get("unknown");
    private static decimal? ValueOf(GameItem item) => NativeItemInspectionApi.CurrentUnitValue(item) is { } value ? checked((decimal)value * item.unitCount) : null;
    private static decimal? TotalValue(IEnumerable<GameItem> items)
    {
        decimal total = 0;
        foreach (var item in items) { var value = ValueOf(item); if (!value.HasValue) return null; total = checked(total + value.Value); }
        return total;
    }
    private static string[] Tokens(string value) => value.Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries);
    private void ClearNetworkView()
    {
        _networkFields.Clear(); _statisticsPage = false;
        _groupId = _storageSearch = _typeFilter = _tagFilter = "";
        _groupPage = _detailPage = _selectedInstance = 0;
        _resourceItems = []; _containerNames.Clear(); ClearNetworkPopups();
    }
    private void ClearNetworkPopups() { _transferMode = _inspectionText = _inspectionTitle = ""; }
    private void LeaveNetworkPage()
    {
        _powerStatisticsPage = _waterStatisticsPage = false;
        foreach (var input in _networkFields) if (input != null) input.DeactivateInputField();
        _networkFields.Clear(); ClearNetworkPopups();
    }
    private void SelectStatisticsPage()
    { LeaveNetworkPage(); _statisticsPage = true; _dirty = true; }
    private bool DismissNetworkPopup()
    {
        if (_inspectionText.Length > 0) { _inspectionText = ""; _dirty = true; return true; }
        if (_transferMode.Length > 0) { _transferMode = ""; _dirty = true; return true; }
        return false;
    }
    private void ResetInventorySelection()
    { _groupPage = _detailPage = _selectedInstance = 0; _groupId = ""; }
    private TMP_InputField Field(string name, string value, string hint, float x, float y, float width, Action<string> set,
        Transform? parent = null, int limit = 200, Action? ended = null)
    {
        var field = InputField(name, value, hint, x, y, width, parent ?? _body!.transform, limit);
        _networkFields.Add(field);
        field.onValueChanged.AddListener(NativeDelegate.Convert<UnityEngine.Events.UnityAction<string>>(new Action<string>(set)));
        field.onEndEdit.AddListener(NativeDelegate.Convert<UnityEngine.Events.UnityAction<string>>(new Action<string>(_ => { ended?.Invoke(); _dirty = true; })));
        return field;
    }
    private void RenderInventoryBrowser(GameItem[] all)
    {
        RenderResourceFilters();
        var typeTokens = Tokens(_typeFilter); var tagTokens = Tokens(_tagFilter);
        var filtered = all.Where(item => (MachineAccess.DisplayName(item).Contains(_storageSearch, StringComparison.OrdinalIgnoreCase) || item.identifier.Contains(_storageSearch, StringComparison.OrdinalIgnoreCase)) &&
            (typeTokens.Length == 0 || typeTokens.Any(item.IsGameItemType)) && tagTokens.All(item.IsTag));
        var groups = filtered.GroupBy(item => item.identifier).Select(group => new StorageGroup(group.Key, MachineAccess.DisplayName(group.OrderBy(item => item.GetUniqueID()).First()),
            group.ToArray(), group.Sum(item => (long)item.unitCount), TotalValue(group))).ToArray();
        IOrderedEnumerable<StorageGroup> sorted = _sort switch
        {
            0 => _ascending ? groups.OrderBy(group => group.Name, StringComparer.Ordinal) : groups.OrderByDescending(group => group.Name, StringComparer.Ordinal),
            1 => _ascending ? groups.OrderBy(group => group.Count) : groups.OrderByDescending(group => group.Count),
            _ => groups.OrderBy(group => group.Value == null ? 1 : 0).ThenBy(group => _ascending ? group.Value ?? 0 : -(group.Value ?? 0))
        };
        sorted = sorted.ThenBy(group => group.Id, StringComparer.Ordinal);
        Button(PsModI18n.I18n.Get("sort") + new[] { PsModI18n.I18n.Get("name"), PsModI18n.I18n.Get("count"), PsModI18n.I18n.Get("total_value") }[_sort], -325, 111, 328, () => { _sort = (_sort + 1) % 3; _groupPage = 0; }, size: CaptionSize);
        Button(_ascending ? PsModI18n.I18n.Get("asc") : PsModI18n.I18n.Get("desc"), -102, 111, 104, () => { _ascending = !_ascending; _groupPage = 0; }, size: CaptionSize);
        int pages = PageRange(groups.Length, ref _groupPage), row = 0;
        foreach (var group in sorted.Skip(_groupPage * StoragePageSize).Take(StoragePageSize))
        {
            float y = 65 - row++ * 38;
            void Select() { _groupId = group.Id; _detailPage = _selectedInstance = 0; }
            Icon(group.Items[0], -466, y, 32, Select, group.Name + PsModI18n.I18n.Get("count_2") + group.Count + PsModI18n.I18n.Get("total_value_3") + Price(group.Value));
            Button(group.Name + " × " + group.Count + PsModI18n.I18n.Get("total_value_2") + Price(group.Value), -252, y, 380, Select,
                selected: _groupId == group.Id, height: 34, left: true, size: CaptionSize);
        }
        if (groups.Length == 0) Text(PsModI18n.I18n.Get("no_matching_inventory_items"), -276, 30, 430, 50, BodySize);
        PageControls(-276, -218, 430, _groupPage, pages, () => _groupPage--, () => _groupPage++);
        Text(PsModI18n.I18n.Get("matched") + groups.Length + PsModI18n.I18n.Get("types") + groups.Sum(group => group.Count) + PsModI18n.I18n.Get("items_total_value") + Price(TotalValue(filtered)), -276, -262, 430, 36, CaptionSize);
        RenderStorageDetails(groups.FirstOrDefault(group => group.Id == _groupId));
    }
    private string ItemLocation(GameItem item) =>
        _resourceItems.FirstOrDefault(view => view.Item.GetUniqueID() == item.GetUniqueID()) is { } origin ?
            _containerNames.GetValueOrDefault(origin.ContainerId) ?? PsModI18n.I18n.Get("container") + origin.ContainerId : PsModI18n.I18n.Get("location_changed");
    private void RenderStorageDetails(StorageGroup? group)
    {
        if (group == null) { _selectedInstance = 0; Text(PsModI18n.I18n.Get("select_an_item_type_to_view_its"), 225, 25, 530, 100, BodySize); return; }
        Text(group.Name + PsModI18n.I18n.Get("individual_items"), 225, 111, 530, 30, HeadingSize);
        Button(_detailSort == 0 ? PsModI18n.I18n.Get("stack_value") : PsModI18n.I18n.Get("unit_value_2"), 225, 74, 530, () => { _detailSort = 1 - _detailSort; _detailPage = 0; }, size: CaptionSize);
        var items = group.Items.OrderBy(item => (_detailSort == 0 ? ValueOf(item) : NativeItemInspectionApi.CurrentUnitValue(item)) == null ? 1 : 0)
            .ThenByDescending(item => _detailSort == 0 ? ValueOf(item) : NativeItemInspectionApi.CurrentUnitValue(item)).ThenBy(item => item.GetUniqueID()).ToArray();
        int pages = PageRange(items.Length, ref _detailPage), row = 0;
        foreach (var item in items.Skip(_detailPage * StoragePageSize).Take(StoragePageSize))
        {
            float y = 38 - row++ * 38;
            Icon(item, -17, y, 29, () => _selectedInstance = item.GetUniqueID(), actualTooltip: true);
            var entry = Button("#" + item.GetUniqueID() + " × " + item.unitCount + PsModI18n.I18n.Get("value") + Price(ValueOf(item)) + "\n" + ItemLocation(item),
                248, y, 486, () => _selectedInstance = item.GetUniqueID(), selected: _selectedInstance == item.GetUniqueID(), height: 34, left: true, size: CaptionSize);
            HoverText(entry, ItemLocation(item) + PsModI18n.I18n.Get("unit_value") + Price(NativeItemInspectionApi.CurrentUnitValue(item)) + PsModI18n.I18n.Get("total_value_3") + Price(ValueOf(item)));
        }
        PageControls(225, -243, 530, _detailPage, pages, () => _detailPage--, () => _detailPage++);
        Text(PsModI18n.I18n.Get("hover_over_an_item_icon_for_quality"), 225, -277, 530, 24, CaptionSize);
    }

    private void RenderStatistics()
    {
        if (!runtime.State.ResourcesUnlocked || runtime.State.Resources == null)
        {
            Text(PsModI18n.I18n.Get("unlock_statistics.resource",
                LocHelper.GetLocalizedNetworkUpgrade("network_" + AutomatedLogisticsNumbers.ResourceUpgrade + "_title"),
                Content.LocalizedName(Content.ResourceDevice)!), 0, 70, 940, 160, BodySize);
            return;
        }
        if (!runtime.ResourceStorageReady)
        { Text(PsModI18n.I18n.Get("storage_is_unavailable_statistics_could_not_be") + runtime.NetworkError, 0, 70, 940, 160, BodySize); return; }
        _resourceItems = runtime.ResourceContents().ToArray();
        var all = _resourceItems.Where(view => !view.LiquidEndpoint).Select(view => view.Item).ToArray();
        long occupied = all.Sum(item => (long)NativeAccess.CellCount(item.modifiedShape));
        long capacity = runtime.ResourceContainers().Where(view => !view.Liquid).SelectMany(view => view.Inventories)
            .Sum(grid => (long)grid.inventoryShape.width * grid.inventoryShape.height);
        Text(PsModI18n.I18n.Get("inventory_types_entries_items", all.Select(item => item.identifier).Distinct().Count(), all.Length, all.Sum(item => (long)item.unitCount)), -246, 201, 480, 34, HeadingSize);
        Text(PsModI18n.I18n.Get("void_container_cells", occupied, capacity), 250, 201, 480, 34, CaptionSize);
        _containerNames.Clear();
        foreach (var view in _resourceItems) _containerNames[view.ContainerId] = view.LiquidEndpoint ? PsModI18n.I18n.Get("void_tank") : PsModI18n.I18n.Get("void_container");
        RenderInventoryBrowser(all);
    }
    private void OpenTransfer(string mode)
    { _transferMode = mode; }
    private void RenderNetworkPopups()
    {
        if (_transferMode is "filter-type" or "filter-tag") RenderResourceFilterPicker(_transferMode == "filter-tag");
        if (_inspectionText.Length > 0)
        {
            var panel = Popup("ActualInspection", _inspectionTitle);
            Paragraph("ActualInspectionText", _inspectionText, 0, -8, 950, 488, BodySize, panel);
        }
    }
    private static int PageRange(int count, ref int current)
    { int pages = Math.Max(1, (count + StoragePageSize - 1) / StoragePageSize); current = Math.Clamp(current, 0, pages - 1); return pages; }
    private void PageControls(float x, float y, float width, int page, int pages, Action previous, Action next)
    {
        Button(PsModI18n.I18n.Get("previous"), x - width / 2 + 47, y, 88, previous, enabled: page > 0, size: CaptionSize);
        Text((page + 1) + " / " + pages, x, y, width - 194, 24, CaptionSize);
        Button(PsModI18n.I18n.Get("next"), x + width / 2 - 47, y, 88, next, enabled: page + 1 < pages, size: CaptionSize);
    }
    private GameObject Icon(GameItem item, float x, float y, float size, Action click, string? groupTooltip = null, bool actualTooltip = false, Transform? parent = null)
    {
        var icon = Button("", x, y, size, click, parent, height: size);
        var visual = Node("ActualItemIcon", icon.transform, 0, 0, size - 6, size - 6).AddComponent<Image>();
        visual.raycastTarget = false; visual.preserveAspect = true;
        visual.sprite = (item is GameItemElement element ? NativeAccess.Sprite(element) : null) ?? RenderHandler.unknownSprite;
        if (actualTooltip) HoverActualItem(icon, item); else HoverText(icon, groupTooltip ?? item.name);
        return icon;
    }
    private void HoverActualItem(GameObject target, GameItem item)
    {
        var trigger = target.AddComponent<UnityEventTrigger>();
        var enter = new UnityEventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(NativeDelegate.Convert<UnityEngine.Events.UnityAction<BaseEventData>>(new Action<BaseEventData>(_ =>
        {
            HideHover();
            try
            {
                if (!runtime.Ready || !NativeInventoryApi.IsPlayerOwned(item) || item.unitCount <= 0) return;
                var rich = NativeItemInspectionApi.BuildTooltip(item);
                if (_nativeHoverWindow == null)
                {
                    _nativeHoverWindow = new PixelWindow(true, ""); _nativeHoverWindow.HideBorder();
                    _nativeHoverText = new RichTextElement(false, -1, -1, false);
                    if (!_nativeHoverWindow.Attach(_nativeHoverText.Cast<PixelElement>())) throw new InvalidOperationException(PsModI18n.I18n.Get("could_not_create_the_native_tooltip_window"));
                }
                _nativeHoverText!.SetRichText(rich!.Build(), 440);
                var mouse = Input.mousePosition;
                _nativeHoverWindow.Show(new Vector2(mouse.x + 12, mouse.y - 12), Vector2.zero, false);
                _nativeHoverWindow.FixTooltipBounds(new Vector2(mouse.x, mouse.y));
                var node = _nativeHoverWindow.handler.rectTransform.gameObject;
                var canvas = node.GetComponent<Canvas>() ?? node.AddComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 30030;
                foreach (var graphic in node.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            }
            catch (Exception ex) { _notice = PsModI18n.I18n.Get("native_tooltip_unavailable") + ex.Message; log(_notice); }
        })));
        var exit = new UnityEventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(NativeDelegate.Convert<UnityEngine.Events.UnityAction<BaseEventData>>(new Action<BaseEventData>(_ => HideHover())));
        trigger.triggers.Add(enter); trigger.triggers.Add(exit);
    }
    private void OpenInspection(string title, string value)
    { _inspectionTitle = title; _inspectionText = value.Length > 0 ? value : PsModI18n.I18n.Get("no_matching_inventory"); ResetScroll("ActualInspectionText"); _dirty = true; }
    private Transform Popup(string name, string title)
    {
        var cover = Node(name, _body!.transform, 0, 0, 1008, 626); MenuBox(cover, Paper);
        Fill(Node("PopupTitle", cover.transform, 0, 272, 974, 44), Tan);
        Text(title, -86, 272, 784, 34, TitleSize, cover.transform);
        Button(PsModI18n.I18n.Get("back"), 438, 272, 86, () => DismissNetworkPopup(), cover.transform); return cover.transform;
    }
}
