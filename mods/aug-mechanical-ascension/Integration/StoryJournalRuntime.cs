using AugMechanicalAscension.Official;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

namespace AugMechanicalAscension.Integration;

internal sealed partial class StoryRuntimeFlow
{
    internal sealed record ItemAction(string Id, string Label, bool Enabled = true);

    private StoryJournal.Page[] JournalPages(GameItem item)
    {
        var store = OfficialStoryGame.Store;
        if (store == null || item.identifier != StoryJournal.ItemId || !OfficialInventoryApi.IsPlayerOwned(item))
            return StoryJournal.Pages(null, null);
        StoryState? state = null;
        try
        {
            var read = OfficialRunDataApi.Read(store, Owner, StoryState.SaveKey);
            if (read.Status == RunDataStatus.Present) state = StoryStateCodec.Read(read.Json!, store.runID, store.saveSlotId);
            else if (read.Status != RunDataStatus.Missing) return JournalUnavailable();
        }
        catch (Exception ex)
        { log("[ERROR] [Story] Journal read: " + ex.Message); return JournalUnavailable(); }
        var pages = StoryJournal.Pages(state, null);
        var text = pages[^1].Body;
        if (!Enabled) text += AugText.Get("aug.the_story_is_paused");
        if (Enabled && Current() && _state != null && state?.Revision == _state.Revision)
        {
            text += "\n\n" + _message;
            if (!_blocked) text += "\n" + VisitHint(Context()) + "\n" + DeadlineText();
        }
        if (state != null)
        {
            var owned = OfficialInventoryApi.CaptureRunItems(store).Where(OfficialInventoryApi.IsPlayerOwned).ToArray();
            text += AugText.Get("aug.retained_evidence");
            foreach (var receipt in state.Items.Where(x => x.Key != StoryJournal.Key))
            {
                bool present = owned.Any(x => x.identifier == StoryEvidence.Prefix + receipt.Key && x.unitCount == 1 && x.uniqueId > 0);
                text += StoryEvidence.Name(receipt.Key, null) + " · " + (present ? AugText.Get("aug.owned") : receipt.Removed ? AugText.Get("aug.delivered_consumed") : AugText.Get("aug.retrieve")) + "\n";
            }
        }
        pages[^1] = pages[^1] with { Body = text };
        return pages;
    }

    private static StoryJournal.Page[] JournalUnavailable() => [new(AugText.Get("aug.story_journal"),
        AugText.Get("aug.the_story_record_is_unavailable_its_original"))];

    internal ItemAction[] ItemActions(GameItem item)
    {
        if (!Ready || _state == null || item == null || NativeObject.Handle(item) == IntPtr.Zero || item.uniqueId <= 0 || item.unitCount != 1 || !OfficialInventoryApi.IsPlayerOwned(item)) return [];
        string key;
        if (StoryPhysicalItems.IsEvidence(item.identifier))
        {
            key = item.identifier[StoryEvidence.Prefix.Length..];
        }
        else if (item.identifier == StoryState.CoreId && _state.CoreDisposition == "held") key = "core";
        else if (item.identifier == CyberneticProductDefinitions.ItemId("communication_receiver")) key = "communicator";
        else if (item.identifier == JacksonOpeningNote.ItemId && !_state.Items.Any(x => x.Key == StoryJournal.Key))
            return [new("claim_journal", AugText.Get("aug.collect_the_story_journal_2x3"))];
        else return [];
        var owned = Owned();
        return StoryItemActionRules.Actions(_state, key, StoreStation.GetDayCounter())
            .Select(id => new ItemAction(id, ItemActionLabel(id), HasPhysicalInputs(id, owned)))
            .Where(action => action.Enabled || action.Id == "repair_parts").ToArray();
    }

    private string ItemActionLabel(string id)
    {
        if (id.StartsWith("deadline:", StringComparison.Ordinal))
        {
            var option = StoryCatalog.Events.First(x => x.Id == _state!.ActiveEventId).Options.First(x => x.Id == id[9..]);
            return AugText.Get("aug.reply") + (option.Label());
        }
        return id switch
        {
            "request_visit" => AugText.Get("aug.continue_the_story_visit"),
            "repair_parts" => AugText.Get("aug.repair_yourself_one_common_electronic_part"),
            "repair_cash" => AugText.Get("aug.have_grayline_repair_it_100_credits_wipes"),
            "copy_parts" => AugText.Get("aug.make_an_evidence_copy_one_common_electronic"),
            "copy_cash" => AugText.Get("aug.make_an_evidence_copy_100_credits"),
            "deliver_copy" => CopyVisitorPresent() ? AugText.Get("aug.give_the_evidence_copy_to_lin") : AugText.Get("aug.contact_lin_to_deliver_the_copy"),
            "salvage" => AugText.Get("aug.salvage_one_precision_part"),
            "x1" => AugText.Get("aug.compare_the_prosthetic_s_marks_with_the"),
            "x2_start" => AugText.Get("aug.watch_the_clocks_offline_overnight_one_common"),
            "x2_read" => AugText.Get("aug.read_the_overnight_observation"),
            "x3" => AugText.Get("aug.trace_the_address_in_the_diagnostics_and"),
            "read_core" => AugText.Get("aug.read_core_communications"),
            "core_visit" => AugText.Get("aug.call_grayline_to_pass_the_core_to"),
            StoryRules.RequestCoreAid => AugText.Get("aug.ask_the_core_to_help_contact_the"),
            StoryRules.RequestCoreCustody => AugText.Get("aug.ask_the_core_where_it_wants_to"),
            StoryRules.ArchiveCore => AugText.Get("aug.take_the_core_to_the_repair_station"),
            "letter" => AugText.Get("aug.retain_the_ending_letter"),
            _ => throw new InvalidOperationException("Unknown story item action: " + id)
        };
    }

    private bool CopyVisitorPresent() => _state?.ActiveEventId == "E08" && _visitEventId == "E08" && _visitRequest != null &&
        OfficialClientVisitApi.Status(Owner, _visitRequest).Status == OfficialPresentationStatus.Arrived;

    internal void ExecuteItemAction(GameItem item, string action)
    {
        if (!ItemActions(item).Any(x => x.Id == action && x.Enabled)) return;
        var previous = _actionItem;
        _actionItem = item;
        try
        {
            string title = item.name;
            if (action == "deliver_copy" && CopyVisitorPresent())
                _message = ReplyToVisitChoice("E08", "share");
            else _message = PhysicalAction(action);
            if (!Current() || _store == null || action is "read_core" or StoryRules.RequestCoreAid or StoryRules.RequestCoreCustody) return;
            if (action is "request_visit" or "claim_journal") { OpenRecord(); return; }
            OfficialReadingApi.Open(Owner, Owner + ".story.item_result", _store.runID, _store.saveSlotId,
                title, _message, AugText.Get("aug.personal_record"));
        }
        finally { _actionItem = previous; }
    }
}
