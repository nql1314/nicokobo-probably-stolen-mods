using AugMechanicalAscension.Official;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using HarmonyLib;

namespace AugMechanicalAscension.Integration;

// Native dialogue clients own presentation; this journal owns progress, never the NPC list.
internal sealed partial class StoryRuntimeFlow(NativeSessionBridge bridge, Action<string> log, Func<bool> enabled)
{
    private const string Owner = "nicokobo.aug", VisitorId = "nicokobo.aug.story.visitor";
    private static StoryRuntimeFlow? _active;
    internal static bool ForkBusy => _active is { } flow && (flow._busy || flow._graylineBusy);
    private long? _generation;
    private PlayerStore? _store;
    private StoryState? _state;
    private string? _json;
    private bool _blocked, _busy;
    private string _message = "";
    private string? _visitRequest;
    private string? _visitEventId;
    // Only the active live-debug visit bypasses dates; this is never saved.
    private string? _debugVisitEventId;
    private string? _carrierDeliveryRequest;
    private DateTime _nextUpdate;
    private int _automaticVisitDay = -1;
    private string? _automaticVisitEventId;
    private bool _automaticVisitInvestigationComplete;
    internal bool Enabled => enabled();
    internal StoryState? State => _state;
    internal string Message => _message;
    internal bool Ready => _state != null && !_blocked && !_busy && Current();
    internal long? Generation => _generation;

    internal static StoryRuntimeFlow? Install(NativeSessionBridge bridge, Action<string> log,
        Func<bool> enabled)
    {
        var harmony = new HarmonyLib.Harmony("nicokobo.aug.story.runtime");
        try
        {
            // Keep item factories available when disabled so existing saves can restore their evidence/core.
            if (!StoryCoreRegistration.Install(log) || !StoryPhysicalItems.Register(log))
                throw new InvalidOperationException("Story content registration failed");
            if (!OfficialDialogueApi.IsAvailable || !OfficialClientVisitApi.IsAvailable || !OfficialReadingApi.IsAvailable)
                throw new InvalidOperationException("Official story presentation is unavailable");
            StoryPhysicalItems.InstallReadingHooks();
            var flow = new StoryRuntimeFlow(bridge, log, enabled);
            harmony.Patch(AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.SaveGame), Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(StoryRuntimeFlow), nameof(AfterSave)));
            flow.InstallDeadlineHooks();
            flow.InstallGraylineHooks();
            StoryItemContextMenu.Install(flow, log);
            StoryPhysicalItems.Resolve = item => flow.ReadableState(item);
            StoryPhysicalItems.InspectCompleted = flow.ShowMagnified;
            StoryPhysicalItems.ReadCompleted = item =>
            {
                var state = flow.ReadableState(item);
                string? key = state?.EndingId switch { "legal_property" => "ownership", "settled_account" => "execution", "lost_and_found" => "contact", "beyond_the_world" => "investigation", _ => null };
                if (key != null && item.identifier == StoryEvidence.Prefix + key) flow.MarkEpilogueRead();
            };
            StoryJournalWindow.Install(flow.JournalPages, log);
            _active = flow;
            return flow;
        }
        catch (Exception ex)
        {
            Uninstall(log);
            log("[ERROR] [Story] Installation failed: " + ex.Message);
            return null;
        }
    }

    internal static void Uninstall(Action<string> log)
    {
        _active?.CloseMagnifier();
        _active = null;
        StoryPhysicalItems.Uninstall();
        StoryPortraits.Clear();
        StoryIconAtlas.Clear();
        StoryPhysicalItems.Resolve = null;
        StoryPhysicalItems.InspectCompleted = null;
        StoryPhysicalItems.ReadCompleted = null;
        StoryItemContextMenu.Clear();
        StoryJournalWindow.Close();

        foreach (string id in new[] { "nicokobo.aug.story.runtime", "nicokobo.aug.story.reading",
            "nicokobo.aug.story.deadline", "nicokobo.aug.grayline-counter",
            "nicokobo.aug.story.item_menu", "nicokobo.aug.story.journal" })
            try { new HarmonyLib.Harmony(id).UnpatchAll(id); }
            catch (Exception ex) { log("[ERROR] [Story] Hook cleanup " + id + ": " + ex.Message); }
    }

    private long _nextSlowStoryLog;

    internal void Update()
    {
        if (!Enabled)
        {
            CloseMagnifier();
            return;
        }
        var view = bridge.CurrentSession;
        if (view == null || _generation != view.Generation)
        {
            Clear();
            if (view == null) return;
            _generation = view.Generation;
        }
        if (DateTime.UtcNow < _nextUpdate) return;
        _nextUpdate = DateTime.UtcNow.AddSeconds(1);
        try
        {
            var timing = new AugUpdateTiming();
            if (!Current()) return;
            if (_state == null && !_blocked) Bind();
            timing.Step(nameof(Bind));
            UpdateGrayline();
            timing.Step(nameof(UpdateGrayline));
            DeliverContactReceipt();
            timing.Step(nameof(DeliverContactReceipt));
            DeliverOvernightUpdates();
            timing.Step(nameof(DeliverOvernightUpdates));
            UpdateStoryVisit();
            timing.Step(nameof(UpdateStoryVisit));
            timing.LogIfSlow(nameof(StoryRuntimeFlow), ref _nextSlowStoryLog);
        }
        catch (Exception ex) { Block(ex.Message); }
    }

    private bool Current()
    {
        var view = bridge.CurrentSession;
        if (!Enabled || view == null || view.Generation != _generation || view.State.InitPhase != "complete" ||
            view.State.FailureReason != "none" || view.State.HasPendingExternalOperation) return false;
        var store = OfficialStoryGame.Store;
        if (store == null || NativeObject.Handle(store) != bridge.CurrentStorePointer ||
            store.runID != view.State.GameRunId || store.saveSlotId != view.State.SaveSlotId ||
            StoreStation.GetDayCounter() != view.State.LastObservedDay) return false;
        return _store == null || NativeObject.Handle(_store) == NativeObject.Handle(store);
    }

    private void Bind()
    {
        _store = OfficialStoryGame.Store;
        if (_store == null) return;
        var read = OfficialRunDataApi.Read(_store, Owner, StoryState.SaveKey);
        if (read.Status == RunDataStatus.Missing)
        {
            _state = new StoryState { GameRunId = _store.runID, SaveSlotId = _store.saveSlotId, ContentVersion = 4 };
        }
        else if (read.Status == RunDataStatus.Present)
        {
            _state = StoryStateCodec.Read(read.Json!, _store.runID, _store.saveSlotId);
            _json = read.Json;
            if (_state.Pending != null) Block("Unconfirmed story transaction; no automatic replay");

        }
        else Block(read.Reason ?? "Story data unavailable");
        if (!_blocked && _state != null)
        {
            bool existingRun = bridge.NativeLoadedRevision.HasValue ||
                StoreStation.GetDayCounter() > bridge.CurrentSession!.State.StartDay;
            var initialized = StoryRules.InitializeOpening(_state, existingRun);
            if ((read.Status == RunDataStatus.Missing || initialized != _state) && !Stage(initialized)) return;
            EnsureJournal();
            foreach (var item in Owned().Where(x => StoryPhysicalItems.IsEvidence(x.identifier) &&
                _state.Items.Any(r => StoryEvidence.Prefix + r.Key == x.identifier)))
                StoryPhysicalItems.Write(item, _state);
        }
    }

    private void Clear()
    {
        CloseMagnifier();
        ClearGrayline();
        _generation = null; _store = null; _state = null; _json = null;
        _letterArmed = false;
        _blocked = false; _busy = false; _message = ""; _visitRequest = null;
        _visitEventId = null;
        _debugVisitEventId = null;
        _carrierDeliveryRequest = null;
        _automaticVisitInvestigationComplete = false;
        _automaticVisitDay = -1; _automaticVisitEventId = null; _nextUpdate = DateTime.MinValue;
    }

    private bool Stage(StoryState next)
    {
        if (!Current() || _store == null || _blocked) return false;
        var json = StoryStateCodec.Write(next);
        var write = OfficialRunDataApi.Stage(_store, Owner, StoryState.SaveKey,
            next.GameRunId, next.SaveSlotId, _json, json);
        if (write.Status != RunDataStatus.StagedInMemory) { Block(write.Reason ?? "Story conflict"); return false; }
        _state = next; _json = json;
        if (next.ActiveEventId != _debugVisitEventId) _debugVisitEventId = null;
        return true;
    }

    private void Block(string reason)
    {
        _blocked = true;
        _message = AugText.Get("aug.story_unavailable_existing_records_retained_no_automatic");
        log("[ERROR] [Story] " + reason);
    }

    private StoryContext Context()
    {
        var run = bridge.CurrentSession!.State;
        var owned = Owned();
        var proofs = owned.Select(x => new WorkshopOwnedItem(x.identifier, x.uniqueId, x.unitCount, true));
        bool hasCore = _state?.CoreDisposition == "held" && owned.Any(x =>
            x.identifier == StoryState.CoreId && x.unitCount == 1 && x.uniqueId > 0);
        return new(StoreStation.GetDayCounter(), run.StartDay, StoryInvestigation.OwnedCybernetics(proofs),
            _store!.playerCash, hasCore, NicoWorkshop.HasMechanicalAscension(run),
            _debugVisitEventId != null && _state?.ActiveEventId == _debugVisitEventId);
    }

    // Read-only diagnostic snapshot; never stage data or advance a story event.
    internal string DebugSummary()
    {
        if (!Current() || _store == null || _state == null)
            return AugText.Get("aug.the_story_session_is_not_ready");
        var s = _state;
        var c = Context();
        string Flag(bool value) => value ? AugText.Get("aug.yes") : AugText.Get("aug.no");
        string None(string? value) => string.IsNullOrEmpty(value) ? "—" : value;
        var next = StoryCatalog.Events.ElementAtOrDefault(s.Choices.Length);
        var available = StoryRules.Available(s, c);
        var visit = _visitRequest == null ? "—" : OfficialClientVisitApi.Status(Owner, _visitRequest).Status.ToString();
        var lines = new List<string>
        {
            AugText.Get("aug.live_run_values_live_mode_changes_these"),
            AugText.Get("aug.ready_blocked_busy", Flag(Ready), Flag(_blocked), Flag(_busy)),
            AugText.Get("aug.day_start_shop", c.Day, c.StartDay, _store.storeState),
            AugText.Get("aug.cash_health_exposure", c.Cash, bridge.CurrentSession!.State.Health, bridge.CurrentSession.State.Exposure),
            AugText.Get("aug.owned_cybernetics_12_distinct_kinds", c.OwnedCybernetics),
            AugText.Get("aug.mechanical_ascension_achievement_completed", Flag(c.MechanicalAscensionCompleted)),
            AugText.Get("aug.completed_10_active", s.Choices.Length, None(s.ActiveEventId)),
            AugText.Get("aug.next_required_owned", next?.Id ?? "—", next?.RequiredCybernetics.ToString() ?? "—"),
            AugText.Get("aug.rule_eligible_event_last_event_day", available?.Id ?? "—", s.LastStartedDay),
            AugText.Get("aug.visit_revision", visit, s.Revision),
            VisitHint(c),
            DeadlineText(),
            AugText.Get("aug.core_uid_disposition", s.CoreUid, s.CoreDisposition),
            AugText.Get("aug.owns_core_consent", Flag(c.OwnsCore), Flag(s.CoreConsented)),
            AugText.Get("aug.confession_heard_placement_consent_scale_transferred", Flag(s.ConfessionHeard), Flag(s.CoreCustodyConsented), Flag(s.ProstheticMarksTransferred)),
            AugText.Get("aug.aid_prerequisites_met", Flag(StoryRules.CanAid(s, c))),
            AugText.Get("aug.evidence_investigation_3_experiment_day", s.Items.Count(x => !x.Removed), s.Investigation, s.ExperimentDay),
            AugText.Get("aug.h1_inspected_h2_compared_h3_diagnostics", Flag(s.ProstheticInspected), Flag(s.Investigation >= 1), Flag(s.DiagnosticRetained)),
            AugText.Get("aug.h4_observed_h5_receipt_e09_reserved", Flag(s.Investigation >= 2), Flag(s.Investigation == 3), Flag(s.OfferReserved)),
            AugText.Get("aug.investigation_loss_reason", s.HiddenClosedReason),
            GraylineSummary(),
            AugText.Get("aug.ending_day", None(s.EndingId), s.EndingDay),
            AugText.Get("aug.epilogue_read_pending", Flag(s.EpilogueSeen), s.Pending?.ActionId ?? "—"),
            AugText.Get("aug.recorded_choices_means_not_chosen")
        };
        for (int i = 0; i < StoryCatalog.Events.Length; i += 2)
            lines.Add(string.Join("    ", StoryCatalog.Events.Skip(i).Take(2).Select(ev => $"{ev.Id}: {None(s.Choice(ev.Id))}")));
        return string.Join("\n", lines);
    }

    private string VisitHint(StoryContext context)
    {
        if (_state == null) return AugText.Get("aug.story_not_ready");
        return StoryRules.ArrivalDenial(_state, context) switch
        {
            "pending" => AugText.Get("aug.an_unconfirmed_transaction_pauses_the_story_it"),
            "ending" => AugText.Get("aug.this_run_s_ending_is_locked_no"),
            "first_night" => AugText.Get("aug.available_after_the_first_night"),
            "achievement" => AugText.Get("aug.complete_the_mechanical_ascension_achievement_before_shao"),
            "ownership" => AugText.Get("aug.own_more_target_cybernetic_kinds", StoryCatalog.Events[_state.Choices.Length].RequiredCybernetics - context.OwnedCybernetics),
            "day_quota" => AugText.Get("aug.today_s_story_event_is_complete_return"),
            "visit_date" => AugText.Get("aug.story.visit_date", StoryRules.VisitDay(_state, context.Day),
                AugText.Get(StoryRules.VisitWeekday(StoryCatalog.Events[_state.Choices.Length].Id) == 2 ? "aug.weekday.tuesday" : "aug.weekday.monday")),
            _ => _store!.storeState != PlayerStore.StoreState.OPEN
                ? AugText.Get("aug.story_prerequisites_met_open_the_shop_to")
                : AugText.Get("aug.story_prerequisites_met_you_can_request_a")
        };
    }

    internal void RequestVisit(bool coreBusiness = false)
        => RequestVisit(coreBusiness, null);

    internal bool RequestTestVisit(string eventId, bool real = false)
    {
        if (!real) { RequestVisit(false, eventId); return false; }
        Update();
        if (!Ready || _state == null) return false;
        if (StoryCatalog.Events.ElementAtOrDefault(_state.Choices.Length)?.Id != eventId)
        { _message = AugText.Get("aug.live_mode_only_runs_the_current_event"); return false; }
        RequestVisit(false, null, debugImmediate: true);
        return _debugVisitEventId == eventId && _visitRequest != null &&
            OfficialClientVisitApi.Status(Owner, _visitRequest).Status is OfficialPresentationStatus.Queued or OfficialPresentationStatus.Arrived;
    }

    private void RequestVisit(bool coreBusiness, string? testEventId, bool debugImmediate = false)
    {
        try
        {
            Update();
            if (!Ready || _store == null)
            { _message = AugText.Get("aug.the_story_session_is_not_ready"); return; }
            if (_store.storeState != PlayerStore.StoreState.OPEN)
            { _message = AugText.Get("aug.request_a_visit_while_the_shop_is"); return; }
            if (_visitRequest != null && OfficialClientVisitApi.Status(Owner, _visitRequest).Status is
                OfficialPresentationStatus.Queued or OfficialPresentationStatus.Arrived or OfficialPresentationStatus.Indeterminate)
            {
                if (debugImmediate && _visitEventId != null && _state?.ActiveEventId == _visitEventId &&
                    OfficialClientVisitApi.Status(Owner, _visitRequest).Status is OfficialPresentationStatus.Queued or OfficialPresentationStatus.Arrived)
                {
                    _debugVisitEventId = _visitEventId;
                    CallDebugVisitor();
                    _message = AugText.Get("aug.live_debug_visitor_called");
                }
                else _message = AugText.Get("aug.a_story_visitor_is_already_waiting");
                return;
            }
            var context = Context() with { IgnoreVisitDays = debugImmediate };
            if (testEventId == null && _state!.ContentVersion != 4)
            { _message = AugText.Get("aug.only_v4_story_content_is_supported_existing"); return; }
            StoryEvent? ev = null;
            if (testEventId != null)
            {
                ev = StoryCatalog.Events.FirstOrDefault(x => x.Id == testEventId);
                if (ev == null) { _message = AugText.Get("aug.unknown_story_event"); return; }
            }
            else if (coreBusiness)
            {
                var action = _state!.EndingId == null ? StoryRules.SellCore : StoryRules.ArchiveCore;
                if (StoryRules.Denial(_state, context, action) != null)
                { _message = AugText.Get("aug.the_core_can_t_be_sent_away"); return; }
            }
            else
            {
                if (StoryRules.ArrivalDenial(_state!, context) != null) { _message = VisitHint(context); return; }
                ev = StoryRules.Available(_state!, context);
                if (ev == null) { _message = VisitHint(context); return; }
                if (!Stage(StoryRules.Begin(_state!, context))) return;
            }
            var options = ev != null ? ev.Options.Where(x => testEventId != null || ev.Id != "E10" || x.Id != "beyond_the_world" || StoryInvestigation.Complete(_state!)).ToArray() :
                [_state!.EndingId == null
                    ? new StoryOption(StoryRules.SellCore, "aug.story.option.sell_core")
                    : new StoryOption(StoryRules.ArchiveCore, "aug.story.option.archive_core")];
            var generation = _generation;
            string run = _store.runID;
            int slot = _store.saveSlotId;
            string request = Guid.NewGuid().ToString("D");
            var script = OfficialDialogueApi.Create(Owner, new OfficialDialogueDefinition(
                Owner + ".story." + (ev?.Id.ToLowerInvariant() ?? "core"),
                ev == null ? AugText.Get("aug.grayline") : ev.SpeakerName(),
                ev == null ? AugText.Get("aug.the_company_pays_three_hundred_the_core")
                    : (testEventId == null ? "" : AugText.Get("aug.test_preview_choices_do_not_change_progress")) + StoryCatalog.Body(ev, _state!),
                options.Select(option => new OfficialDialogueChoice(option.Id, ev == null ? option.Label() : StoryCatalog.OptionLabel(ev, option, _state!),
                    "", callback =>
                    {
                        if (generation != _generation || _visitRequest != request || callback.RunId != run || callback.SlotId != slot || !Current())
                            return AugText.Get("aug.this_visit_has_expired");
                        if (testEventId != null)
                        {
                            log($"[StoryTest] event={testEventId}; choice={option.Id}");
                            return StoryReplies.Get(ev!.Id, option.Id, null);
                        }
                        return ReplyToVisitChoice(ev?.Id, option.Id);
                    })).ToArray(),
                AugText.Get("aug.confirm"), AugText.Get("aug.back_2"), AugText.Get("aug.end_the_visit"),
                testEventId == null && ev != null && StoryRules.TimedDecision(ev.Id)
                    ? AugText.Get("aug.defer_7_days_from_first_encounter") : AugText.Get("aug.later"), AugText.Get("aug.operation_not_confirmed_check_the_story_record"))
                {
                    RequireConfirmation = false,
                    AllowLater = ev?.Id is not ("E02" or "E04" or "E05" or "E09"),
                    NextPageLabel = AugText.Get("aug.more_choices"),
                    PreviousPageLabel = AugText.Get("aug.previous_choices")
                });
            // Art is content-owned; only native queuing/dialogue lifetime moves to Official.
            string sprite = StoryPortraits.Register(StoryPortraits.ForEvent(ev?.Id));
            _visitRequest = request;
            _visitEventId = testEventId == null ? ev?.Id : null;
            _debugVisitEventId = debugImmediate ? ev?.Id : null;
            var queued = testEventId == null && ev?.Id == "E07"
                ? QueueRecordingVisit(request, run, slot, sprite)
                : testEventId == null && ev?.Id == "E10"
                    ? QueueEndingVisit(request, run, slot, sprite)
                    : OfficialClientVisitApi.Queue(Owner, _visitRequest, run, slot, VisitorId, sprite, script);
            if (testEventId == null && ev != null && queued.Status == OfficialPresentationStatus.Queued)
            {
                var started = _state!;
                if (ev.Id == "E09") started = StoryInvestigation.ReserveOffer(started);
                if (started != _state && !Stage(started)) return;
            }
            if (testEventId != null && queued.Status == OfficialPresentationStatus.Queued)
            {
                AugIdentityLabel.ClearForcedNextCustomer();
                log($"[StoryTest] queued={testEventId}; request={_visitRequest}");
            }
            if (debugImmediate && queued.Status == OfficialPresentationStatus.Queued)
            {
                CallDebugVisitor();
                log($"[StoryTest] live={ev?.Id}; request={_visitRequest}; ignoreVisitDays=true");
            }
            if (queued.Status != OfficialPresentationStatus.Queued) _debugVisitEventId = null;
            _message = queued.Status == OfficialPresentationStatus.Queued
                ? debugImmediate ? AugText.Get("aug.live_debug_visitor_called")
                    : AugText.Get("aug.visit_queued_close_the_calendar_and_receive")
                : AugText.Get("aug.visit_not_confirmed_check_the_story_record");
            if (testEventId == null) _message += "\n" + DeadlineText();
            if (queued.Status != OfficialPresentationStatus.Queued) log($"[Story] Visit={queued.Status}; {queued.Reason}");
        }
        catch (Exception ex) { _message = AugText.Get("aug.visit_unavailable"); log("[ERROR] [Story] Visit: " + ex.Message); }
    }

    private void CallDebugVisitor()
    {
        StoryJournalWindow.Close();
        AugIdentityLabel.ClearForcedNextCustomer();
        var calendar = AdvCalendarUIManager.Instance;
        if (calendar != null && calendar.UIPanel != null && calendar.UIPanel.activeInHierarchy) calendar.CloseUI();
        var reader = HandnoteUIManager.Instance;
        if (reader != null && reader.panel != null && reader.panel.activeInHierarchy) reader.ClosePanel();
        var dialog = DialogUIManager.Instance;
        if (_store != null && _store.currentClientInstance == null && !_store.isClientArrived &&
            dialog != null && dialog.isConversationActiveFlag == 0)
            _store.TryCallNextClient();
    }

    internal void OpenRecord()
    {
        if (!Current() || _store == null) return;
        var journal = _state == null ? null : Evidence(StoryJournal.Key);
        var reader = HandnoteUIManager.Instance;
        if (journal == null || reader == null)
        { _message = AugText.Get("aug.retrieve_the_story_journal_if_it_has"); return; }
        reader.OpenPanel(journal);
    }

    private bool Apply(string action)
    {
        if (!Ready || _state == null || _store == null)
        {
            if (!_blocked) _message = AugText.Get("aug.the_story_session_is_not_ready");
            return false;
        }
        var before = _state; var context = Context();
        if (StoryRules.Denial(before, context, action) is { } denial)
        { _message = denial switch
            {
                "cash" => AugText.Get("aug.insufficient_cash_or_cash_limit_reached_no"),
                "core" => AugText.Get("aug.the_corresponding_core_is_not_in_this"),
                "aid" => AugText.Get("aug.to_let_everyone_choose_you_need_the"),
                "consent" => AugText.Get("aug.ask_the_core_whether_it_wishes_to"),
                "confession" => AugText.Get("aug.after_repairing_the_recorder_keep_talking_to"),
                _ => AugText.Get("aug.this_choice_was_already_processed_or_is")
            }; return false; }
        return ApplyPhysicalChoice(before, context, action);
    }

    private bool CommitChoice(StoryState next)
    {
        if (!Stage(next)) return false;
        _message = "";
        return true;
    }

    internal void MarkEpilogueRead()
    {
        if (Ready) Stage(StoryRules.ReadEpilogue(_state!, StoreStation.GetDayCounter()));
    }
}
