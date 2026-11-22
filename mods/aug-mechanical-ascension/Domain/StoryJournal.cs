namespace AugMechanicalAscension.Domain;

internal static class StoryJournal
{
    internal sealed record Page(string Title, string Body);
    internal const string Key = "journal";
    internal const string ItemId = StoryEvidence.Prefix + Key;

    internal static int Act(string eventId) => eventId switch
    {
        "E01" or "E02" or "E03" => 0,
        "E04" or "E05" or "E06" => 1,
        "E07" or "E08" => 2,
        "E09" or "E10" => 3,
        _ => throw new ArgumentException("Unknown story event: " + eventId)
    };

    internal static Page[] Pages(StoryState? state, bool? english)
    {
        string[] titles = Enumerable.Range(1, 4).Select(act => AugText.GetFor("aug.story.journal_act_" + act, english)).ToArray();
        int latest = state?.Choices.Select(x => Act(x.EventId)).DefaultIfEmpty(0).Max() ?? 0;
        if (state?.ActiveEventId is { } active) latest = Math.Max(latest, Act(active));
        var pages = new List<Page>();
        for (int act = 0; act <= latest; act++)
        {
            var text = new System.Text.StringBuilder();
            foreach (var choice in state?.Choices.Where(x => Act(x.EventId) == act) ?? [])
            {
                var ev = StoryCatalog.Events.First(x => x.Id == choice.EventId);
                var option = StoryCatalog.RecordedOption(ev, choice.OptionId)!;
                text.AppendLine(AugText.GetFor("aug.story.journal_date", english, choice.Day, ev.Title(english)));
                text.AppendLine(option.Label(english));
                text.AppendLine(Summary(ev.Id, option.Id, english));
                text.AppendLine(AugText.GetFor("aug.story.journal_speaker", english, ev.SpeakerName(english), StoryReplies.Get(ev.Id, option.Id, english, state)));
                text.AppendLine();
            }
            if (act == latest) text.Append(Body(state, english, includeHistory: false));
            pages.Add(new(titles[act], text.ToString()));
        }
        return pages.ToArray();
    }

    internal static string Summary(string eventId, string optionId, bool? english)
    {
        return eventId switch
        {
            "E01" => AugText.GetFor("aug.someone_came_to_return_a_prosthetic_with", english),
            "E02" => AugText.GetFor("aug.grayline_says_lf_041_is_a_batch", english),
            "E03" => AugText.GetFor("aug.a_woman_named_lin_cen_called_me", english),
            "E04" => AugText.GetFor("aug.the_old_shopkeeper_was_lin_s_brother", english),
            "E05" => AugText.GetFor("aug.the_contract_only_lets_the_company_use", english),
            "E06" => AugText.GetFor("aug.grayline_brought_a_core_from_lf_041", english),
            "E07" => AugText.GetFor("aug.i_heard_the_recording_the_company_erased", english),
            "E08" when optionId == "share" => AugText.GetFor("aug.lin_read_the_evidence_she_knows_her", english),
            "E08" => AugText.GetFor("aug.i_didn_t_give_lin_the_evidence", english),
            "E09" => AugText.GetFor("aug.shao_heng_from_the_company_came_by", english),
            "E10" => AugText.GetFor("aug.lf_041_is_closed_the_card_is", english),
            _ => ""
        };
    }

    internal static string Body(StoryState? state, bool? english, bool includeHistory = true)
    {
        var text = AugText.GetFor("aug.story_journal_who_came_by_what_they", english);
        if (state == null) return text + AugText.GetFor("aug.nothing_recorded_yet", english);
        if (state.ActiveEventId is { } id)
        {
            var current = StoryCatalog.Events.First(x => x.Id == id);
            text += AugText.GetFor("aug.still_waiting_on_your_answer", english) + current.SpeakerName(english) +
                " · " + current.Title(english) + "\n";
        }
        if (state.ActiveEventId == "E07" && !state.RecordingRepaired)
            text += AugText.GetFor("aug.the_recorder_isn_t_fixed_yet_right", english);
        if (state.ActiveEventId == "E07" && state.RecordingRepaired)
            text += state.ConfessionHeard ? AugText.GetFor("aug.grayline_s_account_is_noted_keep_talking", english)
                : AugText.GetFor("aug.the_main_record_is_back_read_the", english);
        if (state.Choice("E07") != "" && state.Choice("E08") == "")
            text += state.Items.Any(x => x.Key == "copy" && !x.Removed)
                ? AugText.GetFor("aug.the_evidence_copy_is_ready_keep_the", english)
                : AugText.GetFor("aug.the_complete_recording_is_saved_to_give", english);
        if (includeHistory) text += AugText.GetFor("aug.what_s_happened", english);
        if (includeHistory && state.Choices.Length == 0) text += AugText.GetFor("aug.nothing_settled_yet", english);
        foreach (var choice in includeHistory ? state.Choices : [])
        {
            var ev = StoryCatalog.Events.First(x => x.Id == choice.EventId);
            var option = StoryCatalog.RecordedOption(ev, choice.OptionId)!;
            text += "\n" + AugText.GetFor("aug.story.journal_date", english, choice.Day, ev.Title(english)) + "\n" +
                (option.Label(english)) + "\n" + Summary(ev.Id, option.Id, english) + "\n";
        }
        if (state.ProstheticInspected) text += state.ProstheticMarksTransferred
            ? AugText.GetFor("aug.the_prosthetic_s_marks_and_full_scale", english)
            : AugText.GetFor("aug.the_marks_on_the_old_prosthetic_have", english);
        if (state.Choice("E08") == "share" && state.CoreDisposition == "held" && !state.CoreConsented)
            text += AugText.GetFor("aug.lin_has_the_copy_right_click_the", english);
        if (state.CoreCustodyConsented && state.CoreDisposition == "held")
            text += AugText.GetFor("aug.the_core_wants_to_go_to_the_2", english);
        if (state.CoreDisposition == "archived") text += AugText.GetFor("aug.the_core_made_it_to_the_offline", english);
        if (state.Investigation > 0) text += AugText.GetFor("aug.the_comparison_observations_and_next_leads_are", english);
        if (state.EndingId == "lost_and_found" && !state.Items.Any(x => x.Key == "contact"))
            text += AugText.GetFor("aug.the_final_decision_is_made_lin_will", english);
        else if (state.EndingId != null) text += AugText.GetFor("aug.the_final_decision_is_made_what_comes", english);
        return text;
    }
}
