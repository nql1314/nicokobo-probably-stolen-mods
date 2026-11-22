using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Integration;
using UnityEngine;

namespace AugMechanicalAscension.UI;

// Independent debug window for bounded state edits and test fixtures.
internal sealed class StatusOverlay(NativeSessionBridge bridge, Action<string> log, StoryRuntimeFlow? story)
{
    private const int NoField = 0;
    private const int HealthField = 1;
    private const int ExposureField = 2;
    private const float DebugPanelHeight = 464f;

    private readonly DebugRuntimeController _debug = new(bridge, log);
    private string? _activeRunId;
    private DateTime _nextErrorLogUtc;
    private bool _debugOpen;
    private bool _debugDragging;
    private Vector2? _debugPosition;
    private Vector2 _debugDragOffset;
    private bool _storyPage;
    private bool _realStory;
    private int _storyPageIndex;
    private int _storyValuesPage;
    private GUIStyle? _storyTextStyle;
    private string _storyValues = "";
    private DateTime _nextStoryRefresh;
    private int _focusedField = NoField;
    private string _healthText = "";
    private string _exposureText = "";
    private string _debugMessage = "";
    private string? _noticeText;
    private string? _noticeKey;
    private GUIStyle? _noticeStyle;
    internal bool TextEditing => _debugOpen && _focusedField != NoField;

    // Unity invokes OnGUI several times per frame. Handle input once in Update,
    // then let OnGUI render passive controls from the resulting state.
    internal void Update()
    {
        if (TextEditing) PsModInput.TextInputFocus.BlockCurrentFrame();
        try { if (story?.UpdateItemInput() != true) UpdateInput(); }
        catch (Exception ex)
        {
            if (DateTime.UtcNow < _nextErrorLogUtc) return;
            _nextErrorLogUtc = DateTime.UtcNow.AddSeconds(AugNumbers.Timing.StatusOverlayErrorLogIntervalSeconds);
            log($"[ERROR] [Debug] input unavailable; {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void UpdateInput()
    {
        if (story?.Enabled != true) { _storyPage = false; _realStory = false; }
        var view = bridge.CurrentSession;
        if (view == null)
        {
            _debugOpen = false;
            _debugDragging = false;
            _focusedField = NoField;
            _activeRunId = null;
            _storyPage = false; _realStory = false;
            return;
        }
        var state = view.State;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        if (store == null || NativeObject.Handle(store) == IntPtr.Zero ||
            NativeObject.Handle(store) != bridge.CurrentStorePointer ||
            store.runID != state.GameRunId ||
            store.saveSlotId != state.SaveSlotId) return;

        if (_activeRunId != state.RunId)
        {
            _activeRunId = state.RunId;
            _storyPage = false; _realStory = false;
            _debugOpen = false;
            _debugDragging = false;
            _focusedField = NoField;
        }

        if (Input.GetKeyDown(KeyCode.D) &&
            (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
            !PsModInput.TextInputFocus.ShouldSuppressHotkeys())
        {
            ToggleDebug(state);
            return;
        }
        if (!_debugOpen || !Application.isFocused)
        {
            _debugDragging = false;
            return;
        }
        if (_storyPage && DateTime.UtcNow >= _nextStoryRefresh)
        {
            _storyValues = story?.DebugSummary() ?? (AugText.Get("aug.story_api_unavailable"));
            _nextStoryRefresh = DateTime.UtcNow.AddMilliseconds(500);
        }
        var panel = DebugRect();
        float x = panel.x;
        float y = panel.y;
        float width = 345f;
        var mouse = Input.mousePosition;
        mouse.y = Screen.height - mouse.y;

        if (_debugDragging)
        {
            if (Input.GetMouseButton(0))
            {
                _debugPosition = new Vector2(mouse.x - _debugDragOffset.x, mouse.y - _debugDragOffset.y);
                DebugRect();
            }
            else _debugDragging = false;
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (new Rect(x + panel.width - 38f, y + 6f, 30f, 25f)
                    .Contains(mouse))
            {
                ToggleDebug(state);
                return;
            }
            if (new Rect(x + 8f, y + 5f, panel.width - 54f, 26f).Contains(mouse))
            {
                _debugDragging = true;
                _debugPosition = new Vector2(x, y);
                _debugDragOffset = new Vector2(mouse.x - x, mouse.y - y);
                _focusedField = NoField;
                return;
            }
            var health = new Rect(x + 105f, y + 34f, 118f, 24f);
            if (_storyPage)
            {
                _focusedField = NoField;
                if (new Rect(x + 12f, y + 34f, width - 24f, 26f).Contains(mouse))
                    _storyPage = false;
                for (int row = 0; row < 5; row++)
                {
                    int index = _storyPageIndex * 5 + row;
                    if (index < StoryCatalog.Events.Length &&
                        new Rect(x + 12f, y + 68f + row * 32f, width - 24f, 28f).Contains(mouse))
                    {
                        if (story?.RequestTestVisit(StoryCatalog.Events[index].Id, _realStory) == true)
                        { _debugOpen = false; _debugDragging = false; }
                        _debugMessage = story?.Message ?? (AugText.Get("aug.story_api_unavailable"));
                    }
                }
                if (new Rect(x + 12f, y + 328f, 321f, 28f).Contains(mouse)) { _realStory = !_realStory; _debugMessage = ""; }
                if (new Rect(x + 12f, y + 364f, 321f, 28f).Contains(mouse)) story?.OpenRecord();
                if (new Rect(x + 12f, y + 232f, 150f, 26f).Contains(mouse)) _storyPageIndex = 0;
                if (new Rect(x + 174f, y + 232f, 159f, 26f).Contains(mouse)) _storyPageIndex = 1;
                if (new Rect(x + 357f, y + panel.height - 38f, panel.width - 369f, 26f).Contains(mouse))
                    _storyValuesPage = (_storyValuesPage + 1) % Math.Max(1, (_storyValues.Split('\n').Length + 13) / 14);
                return;
            }
            var exposure = new Rect(x + 105f, y + 68f, 118f, 24f);
            _focusedField = health.Contains(mouse) ? HealthField
                : exposure.Contains(mouse) ? ExposureField : NoField;
            if (_focusedField == HealthField) _healthText = "";
            if (_focusedField == ExposureField) _exposureText = "";
            if (new Rect(x + 231f, y + 34f, 100f, 24f).Contains(mouse))
                ApplyNumber(_healthText, true);
            else if (new Rect(x + 231f, y + 68f, 100f, 24f).Contains(mouse))
                ApplyNumber(_exposureText, false);
            else if (new Rect(x + 12f, y + 106f,
                         width - 24f, 28f).Contains(mouse))
                ArmNext(1);
            else if (new Rect(x + 12f, y + 140f,
                         width - 24f, 28f).Contains(mouse))
                ArmNext(2);
            else if (new Rect(x + 12f, y + 174f,
                         width - 24f, 28f).Contains(mouse))
                ArmNext(3);
            else if (new Rect(x + 12f, y + 208f,
                         width - 24f, 28f).Contains(mouse))
                SpawnGenuineImmunivax();
            else if (new Rect(x + 12f, y + 242f, width - 24f, 28f).Contains(mouse))
                FillMissingCybernetics();
            else if (new Rect(x + 12f, y + 276f, width - 24f, 28f).Contains(mouse))
                _debugMessage = NicoWorkshop.DebugUnlockMechanicalAscension();
            else if (new Rect(x + 12f, y + 310f, width - 24f, 28f).Contains(mouse))
                ApplyNativeAction(false);
            else if (new Rect(x + 12f, y + 344f, width - 24f, 28f).Contains(mouse))
                ApplyNativeAction(true);
            else if (story?.Enabled == true && new Rect(x + 178f, y + 378f, 153f, 28f).Contains(mouse))
            {
                if (story.RequestGraylineDebugVisit()) { _debugOpen = false; _debugDragging = false; }
                _debugMessage = story.Message;
            }
            else if (story?.Enabled == true && new Rect(x + 12f, y + 378f, 158f, 28f).Contains(mouse))
            {
                _storyPage = true;
                _storyValues = "";
                _storyValuesPage = 0;
                _nextStoryRefresh = DateTime.MinValue;
                _debugMessage = "";
            }
        }

        if (_focusedField == NoField) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _focusedField = NoField;
            return;
        }
        ref string buffer = ref (_focusedField == HealthField
            ? ref _healthText : ref _exposureText);
        if (Input.GetKeyDown(KeyCode.Backspace) && buffer.Length > 0)
            buffer = buffer[..^1];
        foreach (char digit in Input.inputString ?? "")
            if (digit is >= '0' and <= '9' && buffer.Length < 4)
                buffer += digit;
    }

    private void ToggleDebug(RunState state)
    {
        _debugOpen = !_debugOpen;
        _debugDragging = false;
        _focusedField = NoField;
        if (_debugOpen)
        {
            _healthText = state.Health.ToString();
            _exposureText = state.Exposure.ToString();
            _debugMessage = "";
        }
        log($"[Debug] panel={(_debugOpen ? "open" : "closed")}; " +
            $"phase={state.InitPhase}; revision={state.Revision}");
    }

    private Rect DebugRect()
    {
        float width = _storyPage ? 760f : 345f;
        float height = _storyPage ? 440f : DebugPanelHeight;
        var position = _debugPosition ?? new Vector2(Screen.width - width - 16f, 76f);
        position.x = Math.Clamp(position.x, 8f, Math.Max(8f, Screen.width - width - 8f));
        position.y = Math.Clamp(position.y, 8f, Math.Max(8f, Screen.height - height - 8f));
        if (_debugPosition.HasValue) _debugPosition = position;
        return new(position.x, position.y, width, height);
    }

    internal void Draw()
    {
        try
        {
            var view = bridge.CurrentSession;
            if (view == null)
            {
                DrawLegacySchemaNotice();
                return;
            }
            var state = view.State;
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            if (store == null || NativeObject.Handle(store) == IntPtr.Zero ||
                NativeObject.Handle(store) != bridge.CurrentStorePointer ||
                store.runID != state.GameRunId ||
                store.saveSlotId != state.SaveSlotId)
                return;
            VitalsHud.Draw(state);
            if (story?.DrawItemUi() == true) return;
            if (!_debugOpen) return;
            var panel = DebugRect();
            var evt = Event.current;
            if (evt != null && evt.type is EventType.MouseDown or
                EventType.MouseUp or EventType.MouseDrag or EventType.ScrollWheel)
            {
                if (_debugDragging || panel.Contains(evt.mousePosition)) evt.Use();
            }
            int oldDepth = GUI.depth;
            try
            {
                GUI.depth = 25000;
                if (_storyPage) DrawStoryPage(panel);
                else DrawDebugPage(panel.x, panel.y, panel.width, state);
                GUI.Label(new Rect(panel.x + panel.width - 38f,
                    panel.y + 6f, 30f, 25f), "X", GUI.skin.button);
            }
            finally { GUI.depth = oldDepth; }
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow < _nextErrorLogUtc) return;
            _nextErrorLogUtc = DateTime.UtcNow.AddSeconds(AugNumbers.Timing.StatusOverlayErrorLogIntervalSeconds);
            try
            {
                log($"[ERROR] [P0] StatusOverlay unavailable: {ex}");
            }
            catch { /* UI diagnostics must not interrupt game rendering. */ }
        }
    }

    // A loaded slot whose Mod state was written by a retired schema keeps the
    // native save intact but cannot continue. Report that without touching any
    // game state, and rebuild the message only when the notice or language changes.
    private void DrawLegacySchemaNotice()
    {
        string? notice = bridge.LegacySchemaNotice;
        if (notice == null) return;
        try
        {
            string key = Nicokobo.ModUtilities.ModText.LocaleCode + "|" + notice;
            if (_noticeText == null || _noticeKey != key)
            {
                _noticeKey = key;
                _noticeText = AugText.Get("aug.story.legacy_notice") + "\n" + notice;
            }
            _noticeStyle ??= new GUIStyle(GUI.skin.box)
            {
                wordWrap = true,
                padding = new RectOffset(10, 10, 8, 8)
            };
            GUI.Box(new Rect(16f, 16f, 470f, 104f), _noticeText, _noticeStyle);
        }
        catch { /* A notice must never interrupt native rendering. */ }
    }

    private void DrawDebugPage(float x, float top, float width,
        RunState state)
    {
        float y = top;
        GUI.Box(new Rect(x, y, width, DebugPanelHeight),
            AugText.Get("aug.aug_debug_drag_title"));

        var healthRect = new Rect(x + 105f, y + 34f, 118f, 24f);
        var exposureRect = new Rect(x + 105f, y + 68f, 118f, 24f);
        GUI.Label(new Rect(x + 12f, y + 35f, 92f, 22f),
            AugText.Get("aug.health_2"));
        DrawNumberField(healthRect, ref _healthText, HealthField);
        GUI.Label(new Rect(x + 231f, y + 34f, 100f, 24f),
            AugText.Get("aug.set"), GUI.skin.button);

        GUI.Label(new Rect(x + 12f, y + 69f, 92f, 22f),
            AugText.Get("aug.exposure_2"));
        DrawNumberField(exposureRect, ref _exposureText, ExposureField);
        GUI.Label(new Rect(x + 231f, y + 68f, 100f, 24f),
            AugText.Get("aug.set"), GUI.skin.button);

        string nextLabel = AugIdentityLabel.NextAugArmed
            ? (AugText.Get("aug.next_npc_aug_armed"))
            : (AugText.Get("aug.force_next_npc_to_aug"));
        GUI.Label(new Rect(x + 12f, y + 106f,
            width - 24f, 28f), nextLabel, GUI.skin.button);

        string introLabel = AugIdentityLabel.AppraisalIntroArmed
            ? (AugText.Get("aug.appraisal_intro_seller_armed"))
            : (AugText.Get("aug.next_appraisal_intro_seller"));
        GUI.Label(new Rect(x + 12f, y + 140f,
            width - 24f, 28f), introLabel, GUI.skin.button);

        string thiefLabel = AugIdentityLabel.NextThiefArmed
            ? (AugText.Get("aug.next_npc_thief_armed"))
            : (AugText.Get("aug.force_next_npc_to_thief"));
        GUI.Label(new Rect(x + 12f, y + 174f,
            width - 24f, 28f), thiefLabel, GUI.skin.button);

        GUI.Label(new Rect(x + 12f, y + 208f, width - 24f, 28f),
            AugText.Get("aug.spawn_genuine_immunivax_injector_1"), GUI.skin.button);

        GUI.Label(new Rect(x + 12f, y + 242f, width - 24f, 28f),
            AugText.Get("aug.fill_missing_cybernetics"), GUI.skin.button);

        GUI.Label(new Rect(x + 12f, y + 276f, width - 24f, 28f),
            AugText.Get("aug.unlock_mechanical_ascension_no_items"), GUI.skin.button);

        GUI.Label(new Rect(x + 12f, y + 310f, width - 24f, 28f),
            AugText.Get("aug.add_10000_money"), GUI.skin.button);
        GUI.Label(new Rect(x + 12f, y + 344f, width - 24f, 28f),
            AugText.Get("aug.advance_one_day"), GUI.skin.button);

        if (story?.Enabled == true)
        {
            GUI.Label(new Rect(x + 12f, y + 378f, 158f, 28f),
                AugText.Get("aug.story_npc_tests"), GUI.skin.button);
            GUI.Label(new Rect(x + 178f, y + 378f, 153f, 28f),
                AugText.Get("aug.grayline_visit_now"), GUI.skin.button);
        }
        GUI.Label(new Rect(x + 12f, y + 408f, width - 24f, 47f),
            _debugMessage);
    }

    private void DrawStoryPage(Rect panel)
    {
        float x = panel.x, y = panel.y, width = 345f;
        GUI.Box(panel, AugText.Get("aug.story_npc_debugging_drag_title"));
        GUI.Label(new Rect(x + 12f, y + 328f, 321f, 28f), _realStory
            ? (AugText.Get("aug.live_commits_progress_cash_and_items"))
            : (AugText.Get("aug.preview_no_changes_click_to_switch")), GUI.skin.button);
        GUI.Label(new Rect(x + 12f, y + 364f, 321f, 28f), AugText.Get("aug.read_story_journal"), GUI.skin.button);
        GUI.Label(new Rect(x + 12f, y + 34f, width - 24f, 26f),
            AugText.Get("aug.debug_controls"), GUI.skin.button);
        for (int row = 0; row < 5; row++)
        {
            int index = _storyPageIndex * 5 + row;
            if (index >= StoryCatalog.Events.Length) break;
            var ev = StoryCatalog.Events[index];
            GUI.Label(new Rect(x + 12f, y + 68f + row * 32f, width - 24f, 28f),
                $"{ev.Id} · {ev.SpeakerName()} · {ev.Title()}", GUI.skin.button);
        }
        GUI.Label(new Rect(x + 12f, y + 232f, 150f, 26f), "E01–E05", GUI.skin.button);
        GUI.Label(new Rect(x + 174f, y + 232f, 159f, 26f), "E06–E10", GUI.skin.button);
        _storyTextStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12 };
        var valueLines = _storyValues.Split('\n');
        int pages = Math.Max(1, (valueLines.Length + 13) / 14);
        _storyValuesPage = Math.Min(_storyValuesPage, pages - 1);
        GUI.Label(new Rect(x + 357f, y + 34f, panel.width - 369f, panel.height - 80f),
            string.Join("\n", valueLines.Skip(_storyValuesPage * 14).Take(14)), _storyTextStyle);
        GUI.Label(new Rect(x + 357f, y + panel.height - 38f, panel.width - 369f, 26f),
            AugText.Get("aug.variables_next", _storyValuesPage + 1, pages), GUI.skin.button);
        GUI.Label(new Rect(x + 12f, y + 264f, width - 24f, 58f),
            string.IsNullOrEmpty(_debugMessage)
                ? _realStory ? (AugText.Get("aug.live_current_event_arrives_directly_ignoring_visit"))
                    : (AugText.Get("aug.preview_any_event_choices_do_not_change"))
                : _debugMessage, _storyTextStyle);
    }

    // GUI.TextField requires an unavailable IL2CPP method on this game build.
    private void DrawNumberField(Rect rect, ref string buffer, int field)
    {
        GUI.Box(rect, GUIContent.none);
        bool focused = _focusedField == field;
        bool caret = focused && (Time.frameCount / 20) % 2 == 0;
        string shown = buffer.Length == 0
            ? caret ? "|" : ""
            : caret ? buffer + "|" : buffer;
        GUI.Label(new Rect(rect.x + 6f, rect.y + 3f,
            rect.width - 12f, rect.height - 6f), shown);
    }

    private void ApplyNumber(string text, bool health)
    {
        log($"[Debug] click=set-{(health ? "health" : "exposure")}; input={text}");
        if (!int.TryParse(text, out int value))
        {
            _debugMessage = AugText.Get("aug.enter_a_whole_number");
            return;
        }
        var result = health ? _debug.SetHealth(value) :
            _debug.SetExposure(value);
        _debugMessage = result.Status == RuntimeRevisionSaveStatus.StagedInMemory
            ? (AugText.Get("aug.updated_saved_overnight"))
            : result.Status == RuntimeRevisionSaveStatus.FailureStagedInMemory
                ? (AugText.Get("aug.ending_triggered"))
            : AugText.Get("aug.debug.modify_failed", result.Status, result.Error ?? AugText.Get("aug.debug.no_details"));
    }

    private void ApplyNativeAction(bool advanceDay)
    {
        var result = advanceDay ? _debug.AdvanceDay() : _debug.AddMoney();
        _debugMessage = result.Status switch
        {
            DebugNativeActionStatus.Applied => advanceDay
                ? (AugText.Get("aug.day", result.Before, result.After))
                : (AugText.Get("aug.added_10000_cash_saved_overnight", result.After)),
            DebugNativeActionStatus.IdentityMismatch => AugText.Get("aug.no_active_aug_session"),
            DebugNativeActionStatus.ClientActive => AugText.Get("aug.dismiss_the_current_npc_first"),
            DebugNativeActionStatus.DayAdvanceBlocked => AugText.Get("aug.day_advance_blocked_resolve_the_native_or"),
            DebugNativeActionStatus.Unconfirmed => AugText.Get("aug.cash_change_unconfirmed_check_the_current_balance"),
            _ => AugText.Get("aug.failed", result.Error ?? "")
        };
    }

    private void ArmNext(int kind)
    {
        bool armed = kind switch
        {
            1 => AugIdentityLabel.ArmNextAug(),
            2 => AugIdentityLabel.ArmNextAppraisalIntro(),
            3 => AugIdentityLabel.ArmNextThief(),
            _ => false
        };
        log($"[Debug] click=next-npc; kind={kind}; armed={armed}");
        _debugMessage = !armed
            ? (AugText.Get("aug.no_active_aug_session"))
            : kind switch
            {
                1 => AugText.Get("aug.the_next_arriving_npc_will_be_a"),
                3 => AugText.Get("aug.the_next_arriving_npc_will_be_a_2"),
                _ => AugText.Get("aug.the_next_npc_will_be_the_appraisal")
            };
    }

    private void FillMissingCybernetics()
    {
        log("[Debug] click=fill-cybernetics");
        var result = _debug.FillMissingCybernetics();
        log($"[Debug] fill-cybernetics; status={result.Status}; added={result.Added}; alreadyOwned={result.AlreadyOwned}; error={result.Error ?? "none"}");
        string message = result.Status switch
        {
            DebugItemSpawnStatus.Placed => AugText.Get("aug.all_12_nico_workshop_achievement_cybernetics_are"),
            DebugItemSpawnStatus.IdentityMismatch => AugText.Get("aug.no_active_aug_session"),
            DebugItemSpawnStatus.InventoryUnavailable => AugText.Get("aug.the_main_inventory_is_unavailable"),
            DebugItemSpawnStatus.InventoryFull => AugText.Get("aug.no_room_make_space_and_click_again"),
            DebugItemSpawnStatus.Unconfirmed => AugText.Get("aug.placement_unconfirmed_check_inventory"),
            _ => AugText.Get("aug.failed_to_fill_cybernetics")
        };
        _debugMessage = AugText.Get("aug.added_already_owned", message, result.Added, result.AlreadyOwned);
    }

    private void SpawnGenuineImmunivax()
    {
        log("[Debug] click=spawn-genuine-immunivax");
        var result = _debug.SpawnGenuineImmunivax();
        log($"[Debug] spawn-genuine-immunivax; status={result.Status}; error={result.Error ?? "none"}");
        _debugMessage = result.Status switch
        {
            DebugItemSpawnStatus.Placed => AugText.Get("aug.genuine_immunivax_1_added_to_the_main"),
            DebugItemSpawnStatus.IdentityMismatch => AugText.Get("aug.no_active_aug_session"),
            DebugItemSpawnStatus.InventoryUnavailable => AugText.Get("aug.the_main_inventory_is_unavailable"),
            DebugItemSpawnStatus.InventoryFull => AugText.Get("aug.no_room_in_the_main_inventory"),
            DebugItemSpawnStatus.Unconfirmed => AugText.Get("aug.placement_unconfirmed_check_the_main_inventory"),
            _ => AugText.Get("aug.failed_to_generate_a_genuine_immunivax_injector")
        };
    }
}
