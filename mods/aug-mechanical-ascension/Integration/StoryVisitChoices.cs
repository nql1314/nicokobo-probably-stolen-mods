using AugMechanicalAscension.Official;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Integration;

internal sealed partial class StoryRuntimeFlow
{
    private OfficialPresentationResult QueueEndingVisit(string request, string run, int slot, string sprite)
    {
        var generation = _generation;
        bool Valid(OfficialDialogueContext context) => generation == _generation && _visitRequest == request &&
            context.RequestId == request && context.RunId == run && context.SlotId == slot && Current();
        var definition = new OfficialSupplierDefinition(Owner + ".story.e10", AugText.Get("aug.shao_heng"),
            context => Valid(context) ? EndingDialogue(request) : new(AugText.Get("aug.this_visit_has_expired"), []),
            _ => { },
            ConfirmLabel: AugText.Get("aug.confirm"), BackLabel: AugText.Get("aug.back_2"), ContinueLabel: AugText.Get("aug.continue"),
            LeaveLabel: AugText.Get("aug.end_the_visit"), FailureText: AugText.Get("aug.operation_not_confirmed_check_the_story_record"))
        {
            RequireConfirmation = false,
            AllowMenuRefresh = true,
            NextPageLabel = AugText.Get("aug.more_choices"),
            PreviousPageLabel = AugText.Get("aug.previous_choices"),
            CanContinue = context => Valid(context) && Ready && _state?.ActiveEventId == "E10" && _state.Choice("E10") == ""
        };
        return OfficialSupplierApi.Queue(Owner, request, run, slot, VisitorId, sprite, definition);
    }

    private OfficialSupplierPage EndingDialogue(string request)
    {
        if (!Ready || _state == null) return new(AugText.Get("aug.this_visit_has_expired"), []);
        if (_state.Choice("E10") is { Length: > 0 } recorded)
            return new(StoryReplies.Get("E10", recorded, null, _state), []);
        if (_state.ActiveEventId != "E10") return new(AugText.Get("aug.this_visit_has_expired"), []);
        var generation = _generation;
        string run = _state.GameRunId; int slot = _state.SaveSlotId;
        bool ValidSelection(OfficialDialogueContext context) => generation == _generation && _visitRequest == request &&
            context.RequestId == request && context.RunId == run && context.SlotId == slot && Current();
        var ev = StoryCatalog.Events[9];
        var offers = ev.Options.Where(option => option.Id != "beyond_the_world" || StoryInvestigation.Complete(_state))
            .Select(option => new OfficialSupplierOffer(option.Id, StoryCatalog.OptionLabel(ev, option, _state, null), "", context =>
                ValidSelection(context) ? ReplyToVisitChoice("E10", option.Id) : AugText.Get("aug.this_visit_has_expired"))).ToArray();
        bool automaticEscape = StoryInvestigation.Complete(_state) &&
            StoryRules.Denial(_state, Context(), "beyond_the_world") == null &&
            HasPhysicalInputs("beyond_the_world", Owned());
        return new(StoryCatalog.Body(ev, _state, null), offers)
        {
            AutomaticOfferId = automaticEscape ? "beyond_the_world" : null,
            AutomaticDelaySeconds = 0.6
        };
    }

    private void UpdateStoryVisit()
    {
        if (!Ready || _store == null || _state == null || _state.ContentVersion != 4) return;
        if (_visitRequest != null && _visitEventId != null && _state.ActiveEventId == _visitEventId &&
            OfficialClientVisitApi.Status(Owner, _visitRequest).Status == OfficialPresentationStatus.Arrived)
        {
            var arrived = StoryRules.Arrive(_state, StoreStation.GetDayCounter());
            if (arrived != _state) Stage(arrived);
            if (_state?.ActiveEventId == "E07" && !_state.Items.Any(x => x.Key == "recording") && _carrierDeliveryRequest != _visitRequest)
            {
                _carrierDeliveryRequest = _visitRequest;
                EnsureRecordingCarrier();
            }
            return;
        }
        if (_store.storeState != PlayerStore.StoreState.OPEN) return;
        int day = StoreStation.GetDayCounter();
        var nextEventId = StoryCatalog.Events.ElementAtOrDefault(_state.Choices.Length)?.Id;
        bool investigationComplete = nextEventId == "E10" && StoryInvestigation.Complete(_state);
        bool alreadyRequested = _automaticVisitDay == day && _automaticVisitEventId == nextEventId &&
            (!investigationComplete || _automaticVisitInvestigationComplete);
        if (alreadyRequested) return;
        if (_visitRequest != null && OfficialClientVisitApi.Status(Owner, _visitRequest).Status is
            OfficialPresentationStatus.Queued or OfficialPresentationStatus.Arrived or OfficialPresentationStatus.Indeterminate) return;
        var context = Context() with { IgnoreVisitDays = false };
        if (StoryRules.ArrivalDenial(_state, context) != null) return;
        _automaticVisitDay = context.Day;
        _automaticVisitEventId = nextEventId;
        _automaticVisitInvestigationComplete = investigationComplete;
        RequestVisit();
    }

    private string ReplyToVisitChoice(string? eventId, string action)
    {
        if (!Ready || _state == null)
            return string.IsNullOrEmpty(_message) ? AugText.Get("aug.the_story_session_is_not_ready") : _message;
        // Item actions can settle a visit while its native choice buttons remain visible.
        // A retained button must report the recorded outcome without applying it again.
        if (eventId != null)
        {
            if (_state.Choice(eventId) is { Length: > 0 } recorded)
                return StoryReplies.Get(eventId, recorded, null, _state);
            if (_state.ActiveEventId != eventId)
                return AugText.Get("aug.this_visit_has_expired");
        }
        else
        {
            if (action is not (StoryRules.SellCore or StoryRules.ArchiveCore))
                return AugText.Get("aug.this_visit_has_expired");
            if (_state.CoreDisposition is "sold" or "archived")
                return StoryReplies.Get("core", _state.CoreDisposition == "sold" ? StoryRules.SellCore : StoryRules.ArchiveCore, null, _state);
        }
        bool applied = Apply(action);
        return applied ? StoryReplies.Get(eventId ?? "core", action, null, _state)
            : AugText.Get("aug.hold_on_you_don_t_have_everything") + "\n\n" + _message;
    }
}
