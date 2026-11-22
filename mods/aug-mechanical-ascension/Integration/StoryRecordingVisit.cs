using AugMechanicalAscension.Official;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Integration;

internal sealed partial class StoryRuntimeFlow
{
    // Repair choices use the same physical transaction as the carrier's item menu.
    // Only an action's reply offers continuation to the next stage of this visit.
    private OfficialPresentationResult QueueRecordingVisit(string request, string run, int slot, string sprite)
    {
        var generation = _generation;
        bool Valid(OfficialDialogueContext context) => generation == _generation && _visitRequest == request &&
            context.RequestId == request && context.RunId == run && context.SlotId == slot && Current();
        var definition = new OfficialSupplierDefinition(Owner + ".story.recording", AugText.Get("aug.grayline"),
            context => Valid(context) ? RecordingDialogue(request) : new(AugText.Get("aug.that_s_enough_for_today"), []),
            _ => { },
            ConfirmLabel: AugText.Get("aug.confirm"), BackLabel: AugText.Get("aug.back"), ContinueLabel: AugText.Get("aug.continue"),
            LeaveLabel: AugText.Get("aug.end_the_visit"), FailureText: AugText.Get("aug.this_isn_t_done_yet_let_s"))
            {
                RequireConfirmation = false,
                AllowMenuRefresh = false,
                NextPageLabel = AugText.Get("aug.more_choices"),
                PreviousPageLabel = AugText.Get("aug.previous_choices"),
                CanContinue = context => Valid(context) && Ready && _state?.ActiveEventId == "E07" && _state.Choice("E07") == ""
            };
        return OfficialSupplierApi.Queue(Owner, request, run, slot, VisitorId, sprite, definition);
    }

    private OfficialSupplierPage RecordingDialogue(string request)
    {
        if (!Ready || _state == null) return new(AugText.Get("aug.that_s_enough_for_today"), []);
        if (_state.Choice("E07") is { Length: > 0 } choice)
            return new(StoryReplies.Get("E07", choice, null), []);
        if (_state.ActiveEventId != "E07") return new(AugText.Get("aug.that_s_enough_for_today"), []);
        var generation = _generation;
        string run = _state.GameRunId; int slot = _state.SaveSlotId;
        bool ValidSelection(OfficialDialogueContext context) => generation == _generation && _visitRequest == request &&
            context.RequestId == request && context.RunId == run && context.SlotId == slot && Ready && _state?.ActiveEventId == "E07";
        if (_state.RecordingRepaired)
        {
            var ev = StoryCatalog.Events[6];
            // Capture can run while a page is only being constructed. The explicit
            // continuation is reachable after the visible confession, and owns its receipt.
            if (!_state.ConfessionHeard)
                return new(StoryEvidence.MainRecord(null) + "\n\n" + ev.Dialogue(),
                    [new("heard_confession", AugText.Get("aug.i_ve_heard_you_let_s_talk"), "", context =>
                    {
                        if (!ValidSelection(context) || !Require("recording", "credential")) return _message;
                        if (_state!.ConfessionHeard) return AugText.Get("aug.the_testimony_is_already_down");
                        return CommitChoice(_state with { ConfessionHeard = true, Revision = _state.Revision + 1 })
                            ? AugText.Get("aug.i_ve_said_what_i_had_to") : _message;
                    })]);
            return new(AugText.Get("aug.i_won_t_touch_the_original_the"), ev.Options.Select(option => new OfficialSupplierOffer(option.Id,
                option.Label(), "", context =>
                {
                    if (!ValidSelection(context)) return AugText.Get("aug.that_s_enough_for_today");
                    return Apply(option.Id) ? StoryReplies.Get("E07", option.Id, null) : _message;
                })).ToArray());
        }
        if (!_state.Items.Any(x => x.Key == "recording"))
            return new(AugText.Get("aug.i_brought_the_recorder_it_s_not"),
                [new("receive_carrier", AugText.Get("aug.collect_the_recorder_2x2"), "", context =>
                    ValidSelection(context) && EnsureRecordingCarrier()
                        ? AugText.Get("aug.here_the_interface_is_broken_you_ll") : _message)]);
        if (!Require("recording")) return new(_message, []);
        return new(AugText.Get("aug.those_missing_pages_they_ve_been_in"),
            [RepairOffer("repair_parts", AugText.Get("aug.repair_it_yourself_1_common_electronic_part")),
             RepairOffer("repair_cash", AugText.Get("aug.have_grayline_repair_it_100_credits_wipe"))]);

        OfficialSupplierOffer RepairOffer(string action, string label) => new(action, label, "", context =>
        {
            if (!ValidSelection(context)) return AugText.Get("aug.this_visit_has_expired");
            if (_state!.RecordingRepaired)
                return AugText.Get("aug.the_recorder_s_already_fixed_read_the");
            return PhysicalAction(action);
        }) { Enabled = HasPhysicalInputs(action, Owned()) };
    }
}
