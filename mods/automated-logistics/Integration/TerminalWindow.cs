using static PsModI18n.I18n;
using TMPro;
using Nicokobo.AutomatedLogistics.Domain;
using Nicokobo.ModUtilities;
using ProbablyStolen.ModSupport;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEventTrigger = UnityEngine.EventSystems.EventTrigger;

namespace Nicokobo.AutomatedLogistics.Integration;

/// <summary>Read-only inventory views and explicit automation actions in the native menu palette.</summary>
internal sealed partial class TerminalWindow
{
    private readonly AutomatedLogisticsRuntime runtime;
    private readonly Action<string> log;
    internal TerminalWindow(AutomatedLogisticsRuntime runtime, Action<string> log)
    {
        this.runtime = runtime; this.log = log;
        runtime.NetworkPresentationChanged += () => _dirty = true;
        runtime.MachinePresentationChanged += () =>
        { if (Visible && !_statisticsPage && !_powerStatisticsPage && !_waterStatisticsPage && _renameId == 0) _dirty = true; };
    }
    private GameObject? _root, _panel, _body;
    private TMP_FontAsset? _font;
    private Texture2D? _statusDotTexture;
    private Sprite? _statusDotSprite;
    private const float WindowWidth = 1040, WindowHeight = 660;
    private const int TitleSize = 24, HeadingSize = 18, BodySize = 15, CaptionSize = 13;
    private const float ControlHeight = 32;
    private static readonly Color Paper = Rgb(224, 211, 158), Border = Rgb(207, 130, 83),
        Tan = Rgb(212, 160, 120), Ink = Rgb(67, 70, 77), Selection = Rgb(145, 49, 31),
        ListPaper = Rgb(234, 221, 177), Frame = Rgb(83, 85, 80);
    private GameItem? _tool;
    private int _machine, _renameId;
    private bool _dirty, _renameStandalone;
    private string _locale = "";
    private string _notice = "", _nameDraft = "", _nameError = "";
    private TMP_InputField? _nameField;
    private GameObject? _renamePopup, _hoverTip;
    private readonly Dictionary<string, ScrollRect> _scrolls = new();
    private readonly Dictionary<string, float> _positions = new();
    private int _screenWidth, _screenHeight;
    internal bool Visible => _root != null && _root.activeSelf;
    internal bool PointerInside => Visible && ReadingWindowDrag.ContainsPointer(_renameStandalone ? _renamePopup : _panel);
    internal bool TextEditing => Visible && (_nameField != null && _nameField.isFocused || _networkFields.Any(input => input != null && input.isFocused));

    internal void Open(GameItem item)
    {
        if (!NativeHooks.UiAvailable) { log(PsModI18n.I18n.Get("logistics_window_unavailable_input_protection_did_not")); return; }
        try
        {
            runtime.EnsureLoaded();
            if (!runtime.Ready) throw new InvalidOperationException(runtime.Error);
            runtime.UnlockAvailableNetworks();
            EnsureWindow(); ClearRename(); ClearNetworkPopups();
            _panel!.SetActive(true); _tool = item;
            SelectStatisticsPage();
            _root!.SetActive(true); _dirty = true; NativeHooks.Opened();
        }
        catch (Exception ex) { Close(); log(PsModI18n.I18n.Get("could_not_open_the_automation_window") + ex.Message); }
    }
    internal void OpenMachine(GameItem tool, GameItem machine)
    {
        Open(tool);
        if (Visible) { SelectMachinePage(); _machine = machine.GetUniqueID(); }
    }
    internal void OpenRename(GameItem item)
    {
        if (!NativeHooks.UiAvailable) { log(PsModI18n.I18n.Get("rename_window_unavailable_input_protection_did_not")); return; }
        try
        {
            runtime.EnsureLoaded();
            if (!runtime.Ready) throw new InvalidOperationException(runtime.Error);
            if (!AutomatedLogisticsRuntime.CanRename(item)) return;
            bool standalone = !Visible;
            EnsureWindow(); ClearRename();
            _renameStandalone = standalone; _renameId = item.GetUniqueID();
            _nameDraft = GeneralHelper.HasCustomName(item) ? GeneralHelper.GetCustomName(item) : runtime.State.Names.GetValueOrDefault(_renameId) ?? "";
            _nameError = "";
            if (standalone) _tool = item;
            _panel!.SetActive(!standalone); _root!.SetActive(true); _dirty = true; NativeHooks.Opened();
        }
        catch (Exception ex) { log(PsModI18n.I18n.Get("could_not_open_the_rename_window") + ex); }
    }
    private void ClearRename()
    {
        if (_nameField != null) _nameField.DeactivateInputField(); _nameField = null;
        if (_renamePopup != null) { _renamePopup.SetActive(false); UnityEngine.Object.Destroy(_renamePopup); }
        _renamePopup = null; _renameId = 0; _renameStandalone = false; _nameDraft = _nameError = "";
    }
    private void FinishRename()
    {
        if (_renameStandalone) Close(); else { ClearRename(); _dirty = true; }
    }
    internal void Close()
    {
        if (!Visible) return;
        NativeHooks.Capture.Close(Time.frameCount, NativeHooks.MouseHeld);
        HideHover(); ClearNetworkPopups();
        foreach (var input in _networkFields) if (input != null) input.DeactivateInputField();
        _networkFields.Clear(); ClearRename(); _root!.SetActive(false); _tool = null;
        Resources.FindObjectsOfTypeAll<StoreUIManager>().FirstOrDefault()?.OnMouseFlagChanged();
    }
    // Kept as the input-capture callback used by Plugin; there are no card menus.
    internal bool DismissPopup()
    {
        if (_renameId > 0 && !_renameStandalone) { FinishRename(); return true; }
        if (DismissNetworkPopup()) return true;

        return false;
    }
    internal void Clear()
    {
        HideHover(); Close(); if (_root != null) UnityEngine.Object.Destroy(_root);
        if (_statusDotSprite != null) UnityEngine.Object.Destroy(_statusDotSprite);
        if (_statusDotTexture != null) UnityEngine.Object.Destroy(_statusDotTexture);
        _statusDotSprite = null; _statusDotTexture = null;
        _root = _body = _panel = null; _font = null; _machine = 0;
        _notice = ""; _scrolls.Clear(); _positions.Clear(); ClearNetworkView();
        NativeHooks.Capture.Reset();
    }
    internal void Update()
    {
        if (!Visible) return;
        if (_tool == null || !NativeInventoryApi.IsPlayerOwned(_tool)) { Close(); return; }
        if (!ModText.NativeTablesReady) return;
        string locale = ModText.LocaleCode;
        if (_locale != locale) { _locale = locale; _dirty = true; }
        ReadingWindowDrag.KeepCursor(_renameStandalone ? _renamePopup : _panel);
        if (_screenWidth != Screen.width || _screenHeight != Screen.height)
        { _screenWidth = Screen.width; _screenHeight = Screen.height; ReadingWindowDrag.Clamp(_panel!.GetComponent<RectTransform>()); }
        // A background transfer can invalidate inventory rows while a filter
        // is being edited. Keep that control and its input composition alive;
        // onEndEdit requests the deferred render with the latest inventory.
        if (_statisticsPage && TextEditing) return;
        RefreshStatisticsView();
        if (!_dirty) return;
        _dirty = false;
        try { Render(); }
        catch (Exception ex) { _notice = ex.Message; log(PsModI18n.I18n.Get("automation_window") + ex); }
    }
    private void EnsureWindow()
    {
        if (_root != null) return;
        var scanner = Resources.FindObjectsOfTypeAll<WaterScannerUIManager>().FirstOrDefault();
        _font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f.name == "NotoSansSC-VariableFont_wght")
            ?? scanner?.testedTMP?.font ?? scanner?.labelTMP?.font ?? throw new InvalidOperationException(PsModI18n.I18n.Get("the_native_ui_font_has_not_loaded"));
        _root = Node("AutomatedLogisticsTerminal", null, 0, 0, WindowWidth, WindowHeight); _root.SetActive(false);
        var canvas = _root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 29990; canvas.pixelPerfect = true;
        var scaler = _root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new(1280, 720); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        _root.AddComponent<GraphicRaycaster>();
        _panel = Node("Window", _root.transform, 0, 0, WindowWidth, WindowHeight); Fill(_panel, Frame, true);
        MenuBox(Node("MenuFrame", _panel.transform, 0, 0, WindowWidth - 6, WindowHeight - 6), Paper);
        ReadingWindowDrag.Attach(_panel);
        var handle = _panel.transform.Find("NicoReadingWindowDrag").GetComponent<RectTransform>();
        handle.sizeDelta = new(-152, 48); handle.anchoredPosition = new(-60, -10);
    }
    private void Render()
    {
        HideHover();
        if (_renameStandalone) { RenderRename(); return; }
        foreach (var pair in _scrolls) if (pair.Value != null) _positions[pair.Key] = pair.Value.verticalNormalizedPosition;
        _scrolls.Clear(); _networkFields.Clear();
        if (_body != null) { _body.SetActive(false); UnityEngine.Object.Destroy(_body); }
        _body = Node("Page", _panel!.transform, 0, 0, WindowWidth - 28, WindowHeight - 24);
        Fill(Node("TitleBand", _body.transform, 0, 294, 992, 46), Tan);
        Text(Get("title"), -342, 294, 292, 36, TitleSize);
        Text(_statisticsPage ? LocHelper.GetLocalizedMechanic("mech_vending_label_inventory") : _powerStatisticsPage ? LocHelper.GetLocalizedMechanic("mech_printer_label_power") : _waterStatisticsPage ? LocHelper.GetLocalizedMechanic("liquid_water") : Get("machines"), 69, 294, 500, 24, BodySize);
        Button(LocHelper.GetLocalizedMainMenu("close"), 456, 294, 72, Close);
        if (_tool?.identifier is Content.Terminal or Content.Feeder or Content.Ejector)
        {
            float tab = -410;
            Button(LocHelper.GetLocalizedMechanic("mech_vending_label_inventory"), tab, 246, 172, SelectStatisticsPage, selected: _statisticsPage); tab += 184;
            Button(LocHelper.GetLocalizedMechanic("mech_printer_label_power"), tab, 246, 172, SelectPowerStatisticsPage, selected: _powerStatisticsPage); tab += 184;
            Button(LocHelper.GetLocalizedMechanic("liquid_water"), tab, 246, 172, SelectWaterStatisticsPage, selected: _waterStatisticsPage); tab += 184;
            Button(Get("machines"), tab, 246, 172, SelectMachinePage, selected: !_statisticsPage && !_powerStatisticsPage && !_waterStatisticsPage);
        }
        Text(AutomatedLogisticsNumbers.InternalTestPreset ? PsModI18n.I18n.Get("internal_test_values") : "", 234, 246, 296, 30, CaptionSize);
        Rule(0, 224, 992);
        if (_statisticsPage) RenderStatistics(); else if (_powerStatisticsPage) RenderPowerStatistics(); else if (_waterStatisticsPage) RenderWaterStatistics(); else RenderMachines();
        Rule(0, -290, 992); Text(_notice, 0, -309, 984, 24, CaptionSize);
        RenderNetworkPopups(); if (_renameId > 0) RenderRename();
        Canvas.ForceUpdateCanvases();
        foreach (var pair in _scrolls) pair.Value.verticalNormalizedPosition = _positions.GetValueOrDefault(pair.Key, 1);
    }
    private void SelectMachinePage()
    {
        LeaveNetworkPage(); _statisticsPage = false; _dirty = true;
    }
    private void RenderMachines()
    {
        var views = runtime.LogisticsMachines();
        var machines = views.Select(view => view.Machine).ToArray();
        if (!machines.Any(m => m.GetUniqueID() == _machine)) _machine = machines.FirstOrDefault()?.GetUniqueID() ?? 0;
        Text(Get("supported_machines"), -364, 204, 264, 28, HeadingSize);
        Text(Get("status_legend"), -364, 180, 264, 22, CaptionSize);
        var list = Scroll("Machines", -364, -39, 264, 400, machines.Length * 38 + 16);
        for (int i = 0; i < machines.Length; i++)
        {
            var machine = machines[i];
            var row = Button(runtime.Label(machine), 0, Row(list, i, 38), 232, () => { _machine = machine.GetUniqueID(); ResetScroll("MachinePlan"); },
                list, machine.GetUniqueID() == _machine, height: 32, left: true, renameItem: machine);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            label.rectTransform.anchoredPosition = new(4, 0); label.rectTransform.sizeDelta = new(162, 26);
            var icon = Node("MachineIcon", row.transform, -99, 0, 24, 26).AddComponent<Image>();
            icon.raycastTarget = false; icon.preserveAspect = true;
            icon.sprite = (machine is GameItemElement machineElement ? NativeAccess.Sprite(machineElement) : null) ?? RenderHandler.unknownSprite;
            var lamp = Node("StatusLamp", row.transform, 101, 0, 12, 12).AddComponent<Image>();
            lamp.raycastTarget = false; lamp.sprite = StatusDotSprite();
            lamp.color = views[i].Status.Signal switch
            {
                MachineAutomationSignal.Enabled => Rgb(110, 215, 120),
                MachineAutomationSignal.Shortage => Rgb(255, 211, 76),
                _ => Rgb(251, 104, 91)
            };
        }
        var current = machines.FirstOrDefault(m => m.GetUniqueID() == _machine);
        if (current == null) { Text(Get("no_machines"), 142, 105, 708, 60); return; }
        var settings = runtime.Settings(current);
        bool dispenser = MachineAccess.IsFeedDispenser(current);
        bool feeder = dispenser || NativeModuleApi.FindInstalled(current, Content.Feeder).Count > 0;
        bool ejector = NativeModuleApi.FindInstalled(current, Content.Ejector).Count > 0;
        bool restocks = MachineAccess.HasRestockInput(current);
        RenameText(Text(runtime.Label(current), 64, 202, 552, 32, HeadingSize), current);
        Button(Get("open_machine"), 430, 202, 132, () =>
        {
            if (!MachineAccess.Eligible(current) || current.contentWindow == null || current.contentWindow.isLocked ||
                !GeneralHelper.IsItemOwned(current) || ItemMouseDoubleClickHandler.current == null)
                throw new InvalidOperationException(PsModI18n.I18n.Get("this_machine_cannot_be_opened_now"));
            Close();
            ItemMouseDoubleClickHandler.current.OpenContentAction(current, Input.mousePosition);
        }, enabled: current.contentWindow != null && !current.contentWindow.isLocked && GeneralHelper.IsItemOwned(current));
        Button((dispenser ? PsModI18n.I18n.Get("feed_restocking") : PsModI18n.I18n.Get("restocking")) + (dispenser && !runtime.FeedDispenserRestockingEnabled() ? PsModI18n.I18n.Get("disabled_in_settings") : !restocks ? PsModI18n.I18n.Get("no_auto_restock") : !feeder ? Get("not_installed") : settings.FillEnabled ? Get("enabled") : Get("paused")), -38, 158, 348,
            () => { var latest = runtime.Settings(current); latest.FillEnabled = !latest.FillEnabled; runtime.ChangeSettings(latest); }, enabled: feeder && restocks);
        Button(dispenser ? PsModI18n.I18n.Get("food_stays_in_native_calorie_storage") : PsModI18n.I18n.Get("ejection") + (!ejector ? Get("not_installed") : settings.EjectEnabled ? Get("enabled") : Get("paused")), 322, 158, 348,
            () => { var latest = runtime.Settings(current); latest.EjectEnabled = !latest.EjectEnabled; runtime.ChangeSettings(latest); }, enabled: ejector);
        if (dispenser)
        {
            Button(Get("record_feed"), -38, 116, 348,
                () => _notice = runtime.RecordDispenserFoods(current));
            Button(Get("clear_feed"), 322, 116, 348,
                () => _notice = runtime.RecordDispenserFoods(current, clear: true));
        }
        else if (current.identifier == "water_purifier")
        {
            Button(PsModI18n.I18n.Get("collection_target") + (settings.PurifierTarget == PurifierCollectionTarget.PureWater
                ? LocHelper.GetLocalizedMechanic("mech_condition_pure_water") : PsModI18n.I18n.Get("machine_purity_limit")), 142, 116, 708,
                () =>
                {
                    var latest = runtime.Settings(current);
                    latest.PurifierTarget = latest.PurifierTarget == PurifierCollectionTarget.PureWater
                        ? PurifierCollectionTarget.MachineLimit : PurifierCollectionTarget.PureWater;
                    runtime.ChangeSettings(latest);
                });
        }
        else if (current.identifier == "hydroponic")
        {
            Button(PsModI18n.I18n.Get("record_feed_specification"), -38, 116, 348,
                () => _notice = runtime.RecordHydroponicFeed(current));
            Text(PsModI18n.I18n.Get("load_the_desired_nutrient_mix_first_record"), 322, 124, 348, 42, CaptionSize);
        }
        else Text(current.identifier switch
        {
            "evaporator" => PsModI18n.I18n.Get("collects_distillate_only_load_feed_and_handle"),
            "plant_dna_manipulator" => PsModI18n.I18n.Get("collects_disks_from_the_native_eject_slot"),
            _ => PsModI18n.I18n.Get("load_ingredients_manually_first_restock_actual_consumption")
        }, 142, 124, 708, 42, CaptionSize);
        int currentId = current.GetUniqueID();
        string details = PsModI18n.I18n.Get("restock_resource_network_nearby_containers_warehouse_collect") +
            "\n\n" + PsModI18n.I18n.Get("last_restock") + runtime.FillStatus.GetValueOrDefault(currentId, PsModI18n.I18n.Get("no_restocking_recorded")) +
            "\n" + PsModI18n.I18n.Get("last_collection") + runtime.EjectStatus.GetValueOrDefault(currentId, PsModI18n.I18n.Get("no_collection_recorded")) +
            "\n" + PsModI18n.I18n.Get("power") + runtime.PowerStatus(current) +
            "\n" + PsModI18n.I18n.Get("current_state") + views.First(view => view.Machine.GetUniqueID() == _machine).Status.Description +
            "\n\n" + runtime.RestockSourceSummary(current);
        Paragraph("MachinePlan", details, 142, -80, 708, 344, BodySize);
        Text(PsModI18n.I18n.Get("right_click_a_name_to_rename_restock"), -364, -264, 264, 40, CaptionSize);
    }
    private Sprite StatusDotSprite()
    {
        if (_statusDotSprite != null) return _statusDotSprite;
        const int side = 12;
        _statusDotTexture = new Texture2D(side, side, TextureFormat.RGBA32, false)
        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            float dx = x + .5f - side / 2f, dy = y + .5f - side / 2f;
            _statusDotTexture.SetPixel(x, y, dx * dx + dy * dy <= 36 ? Color.white : Color.clear);
        }
        _statusDotTexture.Apply(false, true);
        _statusDotSprite = Sprite.Create(_statusDotTexture, new Rect(0, 0, side, side), new Vector2(.5f, .5f), 100);
        _statusDotSprite.hideFlags = HideFlags.HideAndDontSave;
        return _statusDotSprite;
    }
    private void RenderRename()
    {

        if (_nameField != null) _nameField.DeactivateInputField();
        if (_renamePopup != null) { _renamePopup.SetActive(false); UnityEngine.Object.Destroy(_renamePopup); }
        _nameField = null;
        var target = runtime.Items().FirstOrDefault(i => i.GetUniqueID() == _renameId && AutomatedLogisticsRuntime.CanRename(i));
        if (target == null) { FinishRename(); return; }
        Transform parent = _root!.transform;
        if (!_renameStandalone)
        {
            var cover = Node("RenameCover", _panel!.transform, 0, 0, WindowWidth - 28, WindowHeight - 24);
            Fill(cover, Paper, true); parent = cover.transform; _renamePopup = cover;
        }
        var dialog = Node("RenameDialog", parent, 0, 0, 540, 276); MenuBox(dialog, Paper); _renamePopup ??= dialog;
        Fill(Node("Title", dialog.transform, 0, 105, 504, 42), Tan); Text(Get("rename"), 0, 105, 476, 32, TitleSize, dialog.transform);
        Text(MachineAccess.DisplayName(target), 0, 56, 492, 42, BodySize, dialog.transform);
        _nameField = InputField("InstanceName", _nameDraft, Get("enter_name"), 0, 9, 492, dialog.transform, InstanceNames.MaxLength);
        _nameField.onValueChanged.AddListener(NativeDelegate.Convert<UnityEngine.Events.UnityAction<string>>(new Action<string>(value => _nameDraft = value)));
        var hint = Text(_nameError.Length > 0 ? _nameError : PsModI18n.I18n.Get("up_to_24_characters_leave_blank_to") + _renameId, 0, -44, 492, 48, CaptionSize, dialog.transform);
        if (_nameError.Length > 0) hint.color = Selection;
        void ApplyName(string value)
        {
            runtime.Rename(_renameId, value); _notice = value.Trim().Length == 0 ? PsModI18n.I18n.Get("id_display_restored") : PsModI18n.I18n.Get("renamed") + value.Trim(); FinishRename();
        }
        Button(Get("reset_id"), -186, -108, 120, () => ApplyName(""), dialog.transform);
        Button(LocHelper.GetLocalizedMainMenu("load_cancel"), 46, -108, 112, FinishRename, dialog.transform);
        Button(Get("save_name"), 178, -108, 136, () => ApplyName(_nameDraft), dialog.transform, selected: true); _nameField.ActivateInputField();
    }
    private TMP_InputField InputField(string name, string value, string hint, float x, float y, float width, Transform parent, int limit)
    {
        var node = Node(name, parent, x, y, width, 38);
        var background = MenuBox(node, ListPaper);
        var viewport = Node("TextArea", node.transform, 0, 0, width - 28, 32); viewport.AddComponent<RectMask2D>();
        var label = Text(value, 0, 0, width - 40, 30, BodySize, viewport.transform);
        var placeholder = Text(hint, 0, 0, width - 40, 30, BodySize, viewport.transform);
        label.alignment = placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = placeholder.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        placeholder.color = new(Ink.r, Ink.g, Ink.b, .6f);
        var field = node.AddComponent<TMP_InputField>(); field.textViewport = viewport.GetComponent<RectTransform>();
        field.targetGraphic = background; field.textComponent = label; field.placeholder = placeholder;
        field.characterLimit = limit; field.text = value; return field;
    }
    private Transform Scroll(string name, float x, float y, float width, float height, float contentHeight, Transform? parent = null)
    {
        var frame = Node(name, parent ?? _body!.transform, x, y, width, height);
        MenuBox(frame, ListPaper);
        var viewport = Node("Viewport", frame.transform, -6, 0, width - 24, height - 10); viewport.AddComponent<RectMask2D>();
        var hit = viewport.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        var content = Node("Content", viewport.transform, 0, 0, width - 24, Math.Max(height - 10, contentHeight));
        var rect = content.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new(.5f, 1); rect.pivot = new(.5f, 1);
        rect.anchoredPosition = Vector2.zero;
        var scroll = frame.AddComponent<ScrollRect>(); scroll.content = rect; scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = false; scroll.scrollSensitivity = 28;
        var rail = Node("Scrollbar", frame.transform, width / 2 - 8, 0, 10, height - 12);
        Fill(rail, Rgb(196, 174, 131), true);
        var handleNode = Node("Handle", rail.transform, 0, 0, 10, height - 12);
        var handle = Fill(handleNode, Border, true); var handleRect = handleNode.GetComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero; handleRect.anchorMax = Vector2.one; handleRect.sizeDelta = Vector2.zero;
        var scrollbar = rail.AddComponent<Scrollbar>(); scrollbar.handleRect = handleRect; scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop; scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        scroll.verticalScrollbar = scrollbar;
        _scrolls[name] = scroll; return content.transform;
    }
    private void ResetScroll(string name) { _positions.Remove(name); _scrolls.Remove(name); }
    private void Paragraph(string name, string value, float x, float y, float width, float height, int size, Transform? parent = null)
    {
        var content = Scroll(name, x, y, width, height, height, parent);
        var text = Text(value, 0, 0, width - 40, height - 26, size, content);
        float textHeight = Math.Max(size + 6, text.GetPreferredValues(value, width - 40, 0).y);
        float contentHeight = Math.Max(height - 10, textHeight + 16);
        var rect = content.GetComponent<RectTransform>(); rect.sizeDelta = new(width - 24, contentHeight);
        text.rectTransform.sizeDelta = new(width - 40, textHeight);
        text.rectTransform.anchoredPosition = new(0, contentHeight / 2 - 8 - textHeight / 2);
    }
    private static float Row(Transform content, int index, float step) => content.GetComponent<RectTransform>().rect.height / 2 - 8 - step / 2 - index * step;
    private static GameObject Node(string name, Transform? parent, float x, float y, float width, float height)
    {
        var node = new GameObject(name); var rect = node.AddComponent<RectTransform>();
        if (parent != null) rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new(.5f, .5f); rect.anchoredPosition = new(x, y); rect.sizeDelta = new(width, height);
        return node;
    }
    private static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1);
    private static Image Fill(GameObject node, Color color, bool hit = false)
    {
        var image = node.AddComponent<Image>(); image.color = color; image.raycastTarget = hit; return image;
    }
    private static Image MenuBox(GameObject node, Color color)
    {
        Fill(node, Border, true);
        var inner = Node("Fill", node.transform, 0, 0, 0, 0).GetComponent<RectTransform>();
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one; inner.sizeDelta = new(-4, -4);
        return Fill(inner.gameObject, color, true);
    }
    private void Rule(float x, float y, float width, Transform? parent = null)
    {
        Fill(Node("Divider", parent ?? _body!.transform, x, y, width, 2), Border);
    }
    private TextMeshProUGUI Text(string value, float x, float y, float width, float height, int size = BodySize, Transform? parent = null)
    {
        var node = Node("Text", parent ?? _body!.transform, x, y, width, height); var text = node.AddComponent<TextMeshProUGUI>();
        text.font = _font; text.fontSize = size; text.richText = false; text.text = value; text.color = Ink;
        if (size >= HeadingSize) text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.TopLeft; text.enableWordWrapping = true; text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Ellipsis; return text;
    }
    private GameObject Button(string label, float x, float y, float width, Action action, Transform? parent = null,
        bool selected = false, float height = ControlHeight, bool enabled = true, bool left = false, int size = BodySize,
        GameItem? renameItem = null)
    {
        var node = Node(label, parent ?? _body!.transform, x, y, width, height);
        var image = MenuBox(node, selected ? Selection : Tan);
        var button = node.AddComponent<Button>(); button.targetGraphic = image; button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.interactable = enabled;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new(.95f, .95f, .88f, 1);
        colors.pressedColor = new(.82f, .79f, .7f, 1); colors.selectedColor = colors.normalColor;
        colors.disabledColor = new(.78f, .78f, .78f, .6f); button.colors = colors;
        button.onClick.AddListener(NativeDelegate.Convert<UnityEngine.Events.UnityAction>(new Action(() => RunAction(action))));
        if (renameItem != null) RenameEvents(node, () => OpenRename(renameItem));
        var text = Text(label, 0, 0, width - 20, height - 6, size, node.transform);
        text.alignment = left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
        text.enableWordWrapping = false; text.color = selected ? Paper : Ink;
        if (!enabled) text.color = new(Ink.r, Ink.g, Ink.b, .5f);
        return node;
    }
    private void RunAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { _notice = _nameError = ex.Message; log(PsModI18n.I18n.Get("automation_action_failed") + ex); }
        _dirty = true;
    }
    private void RenameText(TextMeshProUGUI text, GameItem item)
    {
        text.raycastTarget = true;
        RenameEvents(text.gameObject, () => OpenRename(item));
    }
    private void RenameEvents(GameObject node, Action rename)
    {
        var trigger = node.AddComponent<UnityEventTrigger>();
        void On(EventTriggerType type, Action<PointerEventData> action)
        {
            var entry = new UnityEventTrigger.Entry { eventID = type };
            entry.callback.AddListener(NativeDelegate.Convert<UnityEngine.Events.UnityAction<BaseEventData>>(
                new Action<BaseEventData>(data => action(data.Cast<PointerEventData>()))));
            trigger.triggers.Add(entry);
        }
        On(EventTriggerType.PointerClick, data =>
        {
            if (data.button == PointerEventData.InputButton.Right) { RunAction(rename); }
        });
        var scroll = node.GetComponentInParent(typeof(ScrollRect))?.TryCast<ScrollRect>();
        if (scroll == null) return;
        On(EventTriggerType.Scroll, scroll.OnScroll);
        // These events scroll the list without moving the item.
        On(EventTriggerType.BeginDrag, data =>
        {
            data.eligibleForClick = false;
            scroll.OnBeginDrag(data);
        });
        On(EventTriggerType.Drag, scroll.OnDrag);
        On(EventTriggerType.EndDrag, scroll.OnEndDrag);
    }
    private void HideHover()
    {
        if (_nativeHoverWindow?.handler != null) _nativeHoverWindow.Hide();
        if (_hoverTip != null) { _hoverTip.SetActive(false); UnityEngine.Object.Destroy(_hoverTip); }
        _hoverTip = null;
    }
    private void HoverText(GameObject target, string value)
    {
        // Native UnityEventTrigger only: no additional IL2CPP component registration.
        var graphic = target.GetComponent<Graphic>();
        if (graphic != null) graphic.raycastTarget = true;
        var trigger = target.AddComponent<UnityEventTrigger>();
        var enter = new UnityEventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(NativeDelegate.Convert<UnityEngine.Events.UnityAction<BaseEventData>>(new Action<BaseEventData>(_ =>
        {
            HideHover();
            if (_body == null) return;
            const float width = 440;
            _hoverTip = Node("FullLabel", _body.transform, 0, 0, width, 48);
            Fill(_hoverTip, Frame);
            var inner = Node("Paper", _hoverTip.transform, 0, 0, width - 4, 44);
            Fill(inner, ListPaper);
            var text = Text(value, 0, 0, width - 24, 24, BodySize, _hoverTip.transform);
            float height = Math.Min(240, Math.Max(44, text.GetPreferredValues(value, width - 24, 0).y + 20));
            _hoverTip.GetComponent<RectTransform>().sizeDelta = new(width, height);
            inner.GetComponent<RectTransform>().sizeDelta = new(width - 4, height - 4);
            text.rectTransform.sizeDelta = new(width - 24, height - 20);
            var position = _body.transform.InverseTransformPoint(target.transform.position);
            float below = position.y - target.GetComponent<RectTransform>().rect.height / 2 - 8 - height / 2;
            _hoverTip.GetComponent<RectTransform>().anchoredPosition = new(
                Mathf.Clamp(position.x, -496 + width / 2, 496 - width / 2),
                Mathf.Clamp(below, -282 + height / 2, 272 - height / 2));
            // All tooltip graphics ignore pointer events so underlying rows remain clickable.
        })));
        var exit = new UnityEventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(NativeDelegate.Convert<UnityEngine.Events.UnityAction<BaseEventData>>(new Action<BaseEventData>(_ => HideHover())));
        trigger.triggers.Add(enter); trigger.triggers.Add(exit);
        // UnityEventTrigger consumes these events; preserve scrolling and dragging on list rows.
        var scroll = target.GetComponentInParent(typeof(ScrollRect))?.TryCast<ScrollRect>();
        if (scroll == null) return;
        void Forward(EventTriggerType type, Action<PointerEventData> action)
        {
            var entry = new UnityEventTrigger.Entry { eventID = type };
            entry.callback.AddListener(NativeDelegate.Convert<UnityEngine.Events.UnityAction<BaseEventData>>(
                new Action<BaseEventData>(data => { HideHover(); action(data.Cast<PointerEventData>()); })));
            trigger.triggers.Add(entry);
        }
        Forward(EventTriggerType.Scroll, scroll.OnScroll);
        Forward(EventTriggerType.InitializePotentialDrag, scroll.OnInitializePotentialDrag);
        Forward(EventTriggerType.BeginDrag, data =>
        {
            data.eligibleForClick = false;
            scroll.OnBeginDrag(data);
        });
        Forward(EventTriggerType.Drag, scroll.OnDrag);
        Forward(EventTriggerType.EndDrag, scroll.OnEndDrag);
    }
}
