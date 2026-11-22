using DefinitelyStolen.Domain;
using DefinitelyStolen.Integration;
using DefinitelyStolen.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DefinitelyStolen.UI;

internal sealed class TheftPanel
{
    private readonly RunController _run;
    private readonly Catalog _catalog;
    private CustomUIWindow? _window, _debug;
    private CustomUIElement? _stats, _events, _details, _message, _legend, _clock, _action, _mode, _loot, _drop, _return;
    private const float BarWidth = 360;
    private GameObject? _stage;
    private SpriteRenderer? _background;
    private RectTransform? _controls;
    private SceneLayout? _layout;
    private Vector2 _viewSize;
    private readonly CustomUIElement?[] _people = new CustomUIElement?[3], _names = new CustomUIElement?[3], _rare = new CustomUIElement?[3];
    private readonly Image?[] _zones = new Image?[5];
    private Image? _needle;
    private int _region, _position = -1;
    private bool _dirty;
    private float _refreshAt;
    private TimingAttempt? _practice;
    private string? _practiceResult;
    private double _displayPosition;
    private string[] _lootIds = Array.Empty<string>();
    private string[]? _lootLabels;
    private int _roleChoice, _slotChoice, _itemChoice, _batteryChoice, _waterChoice;
    private string[] _debugItems = Array.Empty<string>();
    private CustomUIElement? _debugItemList, _debugStats;
    internal TheftPanel(RunController run, Catalog catalog)
    { _run = run; _catalog = catalog; _run.Changed += () => _dirty = true; }
    internal bool Visible => _window?.IsVisible == true;
    internal GameObject StageObject
    {
        get
        {
            if (_stage == null)
            {
                _stage = new GameObject(Identity.Owner + ".stage");
                _background = _stage.AddComponent<SpriteRenderer>();
                _background.sortingOrder = 20;
                _stage.SetActive(false);
            }
            return _stage;
        }
    }
    internal void Arrive(int region)
    {
        _region = region; _position = -1; _practice = null; _practiceResult = null; _dirty = true;
        _layout = SceneLayout.Create(_run.State!.RunId, _run.State.Night!, region);
        _background!.sprite = Art.Get(SceneLayout.SceneName(region, _layout.Background));
    }
    internal GameObject CreateOverlay(int region)
    {
        if (_window?.IsAlive == true) return _window.root;
        var builder = CustomUIManager.Instance.CreateWindow(Identity.Owner + ".theft", layerId: CustomUIManager.LAYER_META);
        Build(builder, region);
        var window = builder.Show();
        var darkness = GenericConfirmationUIManager.Instance?.confirmationDarkness;
        // Show brings the theft window to the front of the same canvas as the return dialog.
        if (darkness != null && darkness.transform.parent == window.Rect.parent)
            window.Rect.SetSiblingIndex(darkness.transform.GetSiblingIndex());
        return window.root;
    }
    internal void Build(CustomUIBuilder builder, int region)
    {
        _region = region; _window = builder.GetWindow(); _lootLabels = null;
        builder.SetCloseOnEscape(false).SetDraggable(false).UseMetaPrefabs();
        // This window is only a transparent interaction surface. The native
        // world stage renders the backdrop below the unchanged carry window.
        foreach (var image in _window.root.GetComponentsInChildren<Image>(true))
        { image.enabled = false; image.raycastTarget = false; }
        if (_window.frame.titleText != null) _window.frame.titleText.gameObject.SetActive(false);
        if (_window.frame.closeButton != null) _window.frame.closeButton.gameObject.SetActive(false);
        _window.ContentRoot.SetParent(_window.Rect, false);
        foreach (var rect in new[] { _window.Rect, _window.ContentRoot })
        {
            if (rect.GetComponent<LayoutGroup>() is { } group) group.enabled = false;
            if (rect.GetComponent<ContentSizeFitter>() is { } fitter) fitter.enabled = false;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new(.5f, .5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            var npc = _run.State!.Night!.Npcs.Single(x => x.Region == region && x.Position == i);
            string portrait = SceneLayout.PortraitName(_run.State.RunId, npc);
            builder.AddIconButton(Art.Get(portrait), PortraitAnchors.Height,
                () => { if (_practice == null && _run.State?.Night?.Timing == null) { _position = slot; _practiceResult = null; _dirty = true; } }, id: "theft.person." + i);
            _people[i] = builder.Last;
            builder.Last.image.preserveAspect = true;
            builder.AddLabel("", "theft.name." + i); _names[i] = builder.Last;
            builder.Last.text.fontSize = 19; builder.Last.text.alignment = TextAlignmentOptions.Center;
            builder.Last.text.raycastTarget = false;
        }
        builder.BeginPanel(400, 1000, TextAnchor.UpperLeft, padding: 16, spacing: 6, id: "theft.controls");
        _controls = builder.CurrentContainer;
        builder.AddHeader(Locations.Title(region)); builder.Last.text.fontSize = 24;
        builder.AddLabel("", "theft.events"); _events = builder.Last; _events.text.fontSize = 15; _events.SetPreferredSize(BarWidth, 54);
        builder.AddLabel("", "theft.stats"); _stats = builder.Last; _stats.text.fontSize = 17; _stats.SetPreferredSize(BarWidth, 52);
        builder.AddLabel("", "theft.details"); _details = builder.Last; _details.text.fontSize = 18; _details.SetPreferredSize(BarWidth, 138);
        builder.AddLabel(Native.Localize("possible_rare"));
        builder.BeginRow();
        for (int i = 0; i < 3; i++) { builder.AddImage((Sprite)null!, 46, 46, "theft.rare." + i); _rare[i] = builder.Last; }
        builder.End();
        builder.AddToggle(Native.Localize("automatic"), _run.State!.Night!.Mode == TheftMode.Probability, value =>
        { if (_practice == null) _run.SelectMode(value ? TheftMode.Probability : TheftMode.Timing); }, "theft.mode"); _mode = builder.Last;
        builder.AddLabel("", "theft.legend"); _legend = builder.Last; _legend.text.fontSize = 17; _legend.SetPreferredSize(BarWidth, 48);
        builder.BeginPanel(BarWidth, 38, padding: 0, id: "theft.bar");
        var bar = builder.CurrentContainer;
        // Zone widths and the needle are driven by timing, not a vertical stack.
        bar.GetComponent<LayoutGroup>().enabled = false;
        var colors = new[] { new Color(.69f, .26f, .3f), new Color(.61f, .5f, .25f), new Color(.28f, .63f, .48f), new Color(.61f, .5f, .25f), new Color(.69f, .26f, .3f) };
        for (int i = 0; i < 5; i++) _zones[i] = Solid(bar, "zone" + i, colors[i]);
        _needle = Solid(bar, "needle", Color.white); Rect(_needle.rectTransform, 0, 0, 3, 38);
        builder.End();
        builder.AddLabel("", "theft.clock"); _clock = builder.Last; _clock.text.fontSize = 16; _clock.SetPreferredSize(BarWidth, 38);
        builder.AddButton("", Act, "theft.action"); _action = builder.Last;
        builder.AddLabel("", "theft.message"); _message = builder.Last; _message.text.fontSize = 16; _message.SetPreferredSize(BarWidth, 76);
        builder.AddDropdown(new[] { Native.Localize("loot") }, id: "theft.loot"); _loot = builder.Last;
        builder.AddButton(Native.Localize("abandon"), () =>
        { int selected = _loot!.GetIndex(); if (selected >= 0 && selected < _lootIds.Length) _run.Abandon(int.Parse(_lootIds[selected])); }, "theft.abandon"); _drop = builder.Last;
        builder.AddButton(LocHelper.GetLocalizedUI("afterhour_return_action"), _run.Return, "theft.return"); _return = builder.Last;
        builder.End();
        _viewSize = Vector2.zero; LayoutScene(); _dirty = true; Refresh();
    }

    private void LayoutScene()
    {
        var size = _window!.Rect.rect.size;
        if (size == _viewSize || size.x <= 0 || size.y <= 0) return;
        _viewSize = size;
        _controls!.anchorMin = _controls.anchorMax = _controls.pivot = Vector2.one;
        _controls.anchoredPosition = new(-20, -20); _controls.sizeDelta = new(400, Mathf.Min(1000, size.y - 40));
        float scale = Mathf.Max(size.x / 1920, size.y / 1080);
        for (int i = 0; i < 3; i++)
        {
            var npc = _run.State!.Night!.Npcs.Single(x => x.Region == _region && x.Position == i);
            var ground = _layout!.People[i];
            var center = PortraitAnchors.Center(ground, SceneLayout.PortraitName(_run.State.RunId, npc));
            Place(_people[i]!, center.X * scale, center.Y * scale, PortraitAnchors.Width * ground.Scale * scale, PortraitAnchors.Height * ground.Scale * scale);
            Place(_names[i]!, ground.X * scale, ground.Y * scale - 25, 260, 52);
        }
        var camera = Camera.main;
        var bounds = _background!.sprite.bounds.size;
        float cover = Mathf.Max(2 * camera.orthographicSize * camera.aspect / bounds.x, 2 * camera.orthographicSize / bounds.y);
        _stage!.transform.localScale = Vector3.one * cover;
    }
    private static void Place(CustomUIElement element, float x, float y, float w, float h)
    {
        var layout = element.root.GetComponent<LayoutElement>() ?? element.root.AddComponent<LayoutElement>(); layout.ignoreLayout = true;
        Rect(element.Rect, x, y, w, h);
    }
    private static void Rect(RectTransform rect, float x, float y, float w, float h)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = new(x, y); rect.sizeDelta = new(w, h); }
    private static Image Solid(Transform parent, string name, Color color)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image)); root.transform.SetParent(parent, false);
        var image = root.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
    }
    private NpcState? Selected() => _run.State?.Night?.Npcs.SingleOrDefault(x => x.Region == _region && x.Position == _position);
    private static string Occupation(RoleDefinition role) => role.LocalizedName;

    private string Name(NpcState npc, string separator = " · ")
    {
        string occupation = Occupation(_catalog.Role(npc.RoleId));
        var special = SpecialNpcs.Select(_run.State!.RunId, npc);
        if (special == null) return occupation;
        string person = LocHelper.GetLocalizedClientName(special.Id switch
        {
            "joeWilde" => "name_joe_wild",
            "jackson" => "name_dr._jackson",
            "officerRen" => "name_officer_lun",
            _ => "name_" + special.Id
        });
        return person + separator + occupation;
    }
    private string Description(NpcState npc)
    {
        var role = _catalog.Role(npc.RoleId);
        var text = Name(npc) + "\n" + role.LocalizedBackground + "\n" +
            Native.Localize("faction", RunController.FactionName(npc.Faction));
        int available = npc.Items.Count(item => item.Available);
        text += "\n" + Native.Localize("target_stats", npc.SuccessfulThefts, available);
        if (available != npc.Items.Count) text += Native.Localize("candidates_without_their_complete_native_state_are");
        if (npc.Alert) text += "\n" + Native.Localize("alert");
        var note = _run.State!.Notes.LastOrDefault(x => x.Day == _run.State.Night!.Day && x.Target == npc.Key && x.Delivered && !x.Expired);
        if (note?.InvalidationReason == DefinitelyStolen.Persistence.CandidateState.MissingChemicalState)
            text += Native.Localize("intel_invalid_the_legacy_candidate_has_no");
        else if (note?.InvalidationReason == DefinitelyStolen.Persistence.CandidateState.MissingNativeState)
            text += Native.Localize("intel_invalid_the_candidate_s_native_state");
        else if (note is { Invalidated: false }) text += "\n" + (npc.Items.FirstOrDefault(x => x.UniqueId == note.ItemUniqueId) is { } item
            ? Native.Localize("intel", _run.Items.Name(item))
            : Native.Localize("intel_taken"));
        return text;
    }
    private void Act()
    {
        if (_run.Busy) return;
        if (_practice != null) { StopPractice(); return; }
        if (_run.State?.Night?.Timing is { } timing) { _run.StopTheft(_run.StoppedPosition ?? _displayPosition); return; }
        if (Selected() is not { } npc) return;
        _practiceResult = null;
        if (_run.State!.Night!.Mode == TheftMode.Timing && !_run.State.ManualPracticeUsed) _practice = _run.BeginPractice(npc.Key);
        else _run.BeginTheft(npc.Key);
        _dirty = true;
    }
    private void StopPractice()
    {
        if (_practice == null) return;
        double position = TimingRules.Elapsed(_practice, DateTime.UtcNow.Ticks) >= TimingRules.Duration ? 0 : _displayPosition;
        var outcome = _practice.Zones.At(position);
        _practiceResult = Native.Localize("practice_result") + outcome switch
        { AttemptOutcome.Success => Native.Localize("success_no_item_awarded"), AttemptOutcome.Caught => Native.Localize("exposed_no_penalty"), _ => Native.Localize("missed_no_cost") };
        _practice = null; _dirty = true;
    }
    internal void Tick()
    {
        if (!Visible) return;
        if (!_run.Browsing || !Locations.IsOwn(MapUIManager.Instance?.Current?.Id)) { Close(); return; }
        LayoutScene();
        bool typing = EventSystem.current?.currentSelectedGameObject?.GetComponent<TMP_InputField>()?.isFocused == true;
        if (!KeyBindHelper.SuppressHotkeys && !typing && Input.GetKeyDown(KeyCode.D) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))) ToggleDebug();
        if (!KeyBindHelper.SuppressHotkeys && !typing && Input.GetKeyDown(KeyCode.F5) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))) DebugRefreshNpcs();
        var timing = _practice ?? _run.State?.Night?.Timing;
        if (timing != null)
        {
            double elapsed = TimingRules.Elapsed(timing, DateTime.UtcNow.Ticks);
            _displayPosition = _practice == null && _run.StoppedPosition is { } stopped ? stopped : TimingRules.Position(timing, elapsed);
            if (_needle != null) Rect(_needle.rectTransform, (float)(_displayPosition - .5) * BarWidth, 0, 3, 38);
            _clock?.SetText(timing.AutomaticPosition != null ? Native.Localize("automatic_attempt") : Native.Localize("space_or_stop_s_remaining", Math.Max(0, TimingRules.Duration - elapsed)));
            if (_practice != null && elapsed >= TimingRules.Duration) StopPractice();
            else if (_practice == null && (timing.AutomaticPosition != null ? elapsed >= TimingRules.AutomaticDuration : elapsed >= TimingRules.Duration))
                _run.StopTheft(timing.AutomaticPosition ?? 0);
            else if (timing.AutomaticPosition == null && !KeyBindHelper.SuppressHotkeys && !typing && Input.GetKeyDown(KeyCode.Space)) Act();
        }
        if (_dirty || Time.unscaledTime >= _refreshAt) { _refreshAt = Time.unscaledTime + .15f; _dirty = false; Refresh(); }
    }
    private void Refresh()
    {
        if (_window?.IsAlive != true || _run.State?.Night is not { } night) return;
        if (night.Timing is { } pending) _position = night.Npcs.Single(x => x.Key == pending.Target).Position;
        int level = TheftRules.Level(_run.State.Experience);
        var events = night.RegionEvents.Single(x => x.Region == _region);
        _events?.SetText(NightEvents.Summary(events, _catalog))
            .SetTooltip(NightEvents.Summary(events, _catalog, details: true));
        _stats?.SetText(Native.Localize("run_stats", level, _run.State.Experience, TheftRules.Threshold(Math.Min(10, level + 1)),
            _run.State.Wind, TheftRules.Remaining(_run.State)));
        bool running = _practice != null || night.Timing != null;
        _mode?.SetBool(night.Mode == TheftMode.Probability).SetInteractable(!running && !night.Ended && !night.Escaped);
        for (int i = 0; i < 3; i++)
        {
            var npc = night.Npcs.Single(x => x.Region == _region && x.Position == i);
            _names[i]?.SetText((i == _position ? "> " : "") + Name(npc, "\n") + (npc.Alert ? " !" : ""));
            _people[i]?.SetInteractable(!running && !night.Ended).SetTooltip(Description(npc));
        }
        var selected = Selected(); _details?.SetText(selected == null ? Native.Localize("choose_a_person_discovery_penalizes_security_and") : Description(selected));
        var rares = selected?.Items.Where(x => x.Available && x.Rare).DistinctBy(x => x.Id).Take(3).ToArray() ?? Array.Empty<ItemState>();
        for (int i = 0; i < 3; i++)
        {
            _rare[i]?.SetVisible(i < rares.Length);
            if (i < rares.Length) { var meta = _catalog.Item(rares[i].Id); _rare[i]?.SetSprite(RenderHandler.LoadFromAtlas(meta.Atlas, meta.Sprite)).SetTooltip(_run.Items.Name(rares[i])); }
        }
        var zones = _practice?.Zones ?? night.Timing?.Zones ?? (selected == null ? new TimingZones(0, 0) : TimingRules.Preview(_run.State, selected, _catalog.Role(selected.RoleId), _run.ActiveTuning, _run.ManualDifficultyCoefficient, _run.RandomizeSuccessZoneAfterSuccess));
        _legend?.SetText(Native.Localize("success_miss_exposure_penalties", zones.Success, zones.Failure, zones.Exposure,
            _run.ReputationPenaltyPreview(selected?.Faction, _region)));
        var widths = new[] { zones.Exposure / 2, zones.LeftFailure, zones.Success, zones.RightFailure, zones.Exposure / 2 }; double left = -BarWidth / 2;
        for (int i = 0; i < 5; i++) if (_zones[i] != null) { float width = (float)(widths[i] * BarWidth); Rect(_zones[i]!.rectTransform, (float)left + width / 2, 0, width, 32); left += width; }
        if (_needle != null) _needle.gameObject.SetActive(running || _practiceResult != null);
        bool automatic = night.Mode == TheftMode.Probability;
        _action?.SetText(running ? automatic ? Native.Localize("attempting") : Native.Localize("stop") : !automatic && !_run.State.ManualPracticeUsed ? Native.Localize("practice") : Native.Localize("steal"));
        _action?.SetInteractable(!_run.Busy && !night.Ended && !night.Escaped && (!running ? selected != null && TheftRules.CanSteal(selected) &&
            ((_run.Infinite || TheftRules.Remaining(_run.State) > 0) && _run.CanCarryTarget(selected) || !automatic && !_run.State.ManualPracticeUsed) : !automatic));
        if (!running) _clock?.SetText(automatic ? Native.Localize("click_steal_for_one_automatic_attempt") : Native.Localize("space_or_stop_six_seconds_timeout_exposes"));
        _message?.SetText(_practice != null ? Native.Localize("practice_consumes_no_actions_outings_or_items") : _practiceResult ?? (night.Escaped ? Native.Localize("one_smoke_grenade_consumed_loot_retained_theft") : _run.Message));
        int lootIndex = _loot?.GetIndex() ?? -1;
        string? selectedLoot = lootIndex >= 0 && lootIndex < _lootIds.Length ? _lootIds[lootIndex] : null;
        var lootIds = night.Loot.Select(x => x.UniqueId.ToString()).ToArray();
        var lootLabels = night.Loot.Count == 0 ? new[] { Native.Localize("no_loot") } : night.Loot.Select(item => _run.Items.Name(item)).ToArray();
        if (_loot != null && (_lootLabels == null || !_lootIds.SequenceEqual(lootIds) || !_lootLabels.SequenceEqual(lootLabels)))
        {
            // SetOptions clears TMP's selection; retain the actual loot UID
            // across periodic refreshes, removals and locale changes.
            _loot.SetOptions(lootLabels).SetIndex(Math.Max(0, Array.IndexOf(lootIds, selectedLoot)));
        }
        _lootIds = lootIds; _lootLabels = lootLabels;
        _drop?.SetInteractable(!running && night.Loot.Count > 0 && !night.Ended);
        _return?.SetInteractable(!running && !_run.Busy);
        RefreshDebug();
    }

    private void DebugRefreshNpcs()
    {
        if (!Visible || _practice != null || !_run.DebugRefreshNpcs(_region)) return;
        _position = -1; _practiceResult = null;
        _layout = SceneLayout.Create(_run.State!.RunId, _run.State.Night!, _region);
        _viewSize = Vector2.zero;
        for (int i = 0; i < 3; i++)
        {
            var npc = _run.State.Night!.Npcs.Single(x => x.Region == _region && x.Position == i);
            _people[i]?.SetSprite(Art.Get(SceneLayout.PortraitName(_run.State.RunId, npc)));
        }
    }
    internal void ToggleDebug()
    {
        if (_debug?.IsVisible == true) { _debug.Hide(); return; }
        _run.DebugEnabled = true;
        if (_debug?.IsAlive == true) { _debug.Show(); return; }
        var b = CustomUIManager.Window(Identity.Owner + ".debug", Native.Localize("theft_debug_alt_d"), CustomUIManager.LAYER_META).SetSize(480, 780).SetPosition(new Vector2(550, 0));
        b.BeginScroll(670);
        b.AddSlider(0, 10, TheftRules.Level(_run.State!.Experience), value => _run.DebugLevel((int)value), true).WithTooltip(Native.Localize("proficiency_level"));
        b.AddSlider(0, 100, _run.State.Wind, value => _run.DebugWind((int)value), true).WithTooltip(Native.Localize("rumour"));
        b.AddToggle(Native.Localize("infinite_actions"), _run.Infinite, value => _run.Infinite = value);
        b.AddButton(Native.Localize("restore_actions_and_attempt_risk"), _run.DebugRefreshActions);
        b.AddToggle(Native.Localize("override_success_manual_width"), false, value => { _run.Tuning.Success = value ? .5 : null; _dirty = true; });
        b.AddSlider(0, 1, .5f, value => { if (_run.Tuning.Success != null) _run.Tuning.Success = value; _dirty = true; });
        b.AddToggle(Native.Localize("override_miss_exposure"), false, value => { _run.Tuning.Exposure = value ? .5 : null; _dirty = true; });
        b.AddSlider(0, 1, .5f, value => { if (_run.Tuning.Exposure != null) _run.Tuning.Exposure = value; _dirty = true; });
        b.AddDropdown(_catalog.Enabled.Select(Occupation).ToArray(), _roleChoice, value => _roleChoice = value);
        b.AddDropdown(Enumerable.Range(0, 9).Select(x => $"{x / 3 + 1}-{x % 3 + 1}").ToArray(), _slotChoice, value => _slotChoice = value);
        b.AddButton(Native.Localize("replace_district_slot_npc"), () => _run.DebugNpc(_slotChoice / 3, _slotChoice % 3, _catalog.Enabled[_roleChoice].Id));
        b.AddButton(Native.Localize("refresh_selected_belongings"), () => { if (Selected() is { } npc) _run.DebugRefreshItems(npc.Key, _region); });
        b.AddButton(Native.Localize("refresh_district_belongings"), () => _run.DebugRefreshItems(null, _region));
        b.AddButton(Native.Localize("refresh_all_npcs_in_current_district_alt"), DebugRefreshNpcs);
        _debugItems = _catalog.DropIds.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        b.AddInput(Native.Localize("search_item_name_id"), onTyping: FilterDebugItems);
        b.AddDropdown(_debugItems.Select(ItemLabel).ToArray(), onChanged: value => _itemChoice = value, id: "debug.items"); _debugItemList = b.Last;
        b.AddDropdown(new[] { "Random", "0%", "25%", "50%", "75%", "100%" }, _batteryChoice, value => _batteryChoice = value);
        b.AddDropdown(new[] { "99.9–100%", "99–99.9%", "98–99%" }, _waterChoice, value => _waterChoice = value);
        b.AddButton(Native.Localize("generate_selected_item"), () => { if (_itemChoice >= 0 && _itemChoice < _debugItems.Length) _run.DebugDrop(_debugItems[_itemChoice], _batteryChoice == 0 ? null : _batteryChoice - 1, _waterChoice); });
        b.AddLabel("", "debug.stats"); _debugStats = b.Last; _debugStats.text.fontSize = 13;
        b.End(); _debug = b.Show(); RefreshDebug();
    }
    private string ItemLabel(string id) => _run.Items.CatalogName(id) + " · " + id;
    private void FilterDebugItems(string query)
    {
        _debugItems = _catalog.DropIds.Where(x => x.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || ItemLabel(x).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(x => x).ToArray();
        _itemChoice = 0; _debugItemList?.SetOptions(_debugItems.Select(ItemLabel).ToArray());
    }
    private void RefreshDebug()
    {
        if (_debug?.IsVisible != true) return;
        if (_run.State?.Night?.Timing != null || _practice != null) { _debug.Hide(); return; }
        var npc = Selected();
        _debugStats?.SetText(npc == null ? Native.Localize("choose_a_target_for_actual_state") : Description(npc) + "\n" +
            string.Join("\n", npc.Items.Select(item => _run.Items.Name(item, revealQuality: true) + $" · V={item.Value} · UID={item.UniqueId} · W={item.Weight} · {item.Condition}")));
    }
    internal void Close()
    {
        _practice = null; _practiceResult = null;
        _debug?.Close(); _debug = null;
        if (_window?.IsAlive == true) _window.Close();
        _window = null; _position = -1; _layout = null; _controls = null;
        if (_stage != null) UnityEngine.Object.Destroy(_stage);
        _stage = null; _background = null;
    }
}
