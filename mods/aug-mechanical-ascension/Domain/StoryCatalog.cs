namespace AugMechanicalAscension.Domain;

internal static class StoryCatalog
{
    private static StoryOption O(string id, string key, int cash = 0, bool hasConsequence = true) =>
        new(id, key, cash, hasConsequence);
    internal static readonly StoryEvent[] Events =
    [
        new("E01", 0, [O("investigate", "aug.story.option.buy_it_as_scrap_60_credits", -60),
             O("decline", "aug.story.option.refuse_the_return")]),
        new("E02", 0, [O("accept", "aug.story.option.take_the_index_and_the_titanium")]),
        new("E03", 0, [O("honest", "aug.story.option.tell_her_you_don_t_know_her"),
             O("pretend", "aug.story.option.pretend_to_be_her_brother_and_say")]),
        new("E04", 2, [O("compare", "aug.story.option.take_the_file_and_keep_digging")]),
        new("E05", 4, [O("keep", "aug.story.option.take_the_contract_and_help_her_check")]),
        new("E06", 0, [O("buy", "aug.story.option.pay_200_credits_keep_the_core", -200),
             O("refuse", "aug.story.option.pass_on_it")]),
        new("E07", 8, [O("disclose", "aug.story.option.put_his_name_in_the_copy"),
             O("withhold_name", "aug.story.option.leave_his_name_out")]),
        new("E08", 0, [O("share", "aug.story.option.give_her_the_evidence_copy"),
             O("hide", "aug.story.option.don_t_show_her")]),
        new("E09", 10, [O("read", "aug.story.option.take_the_offer_and_answer_later")]),
        new("E10", 0, [O("beyond_the_world", "aug.story.option.diagnostic_upload_this_address"),
             O("legal_property", "aug.story.option.return_to_the_company_trade_the_card"),
             O("settled_account", "aug.story.option.destroy_the_control_system_publish_the_evidence"),
             O("lost_and_found", "aug.story.option.divide_it_among_them_let_everyone_in")])
    ];

    // Retain the label only for completed choices in existing saves.
    internal static StoryOption? RecordedOption(StoryEvent ev, string id) =>
        ev.Id == "E01" && id == "refund"
            ? O("refund", "aug.story.option.pay_100_credits_in_compensation_keep_the", -100, hasConsequence: false)
            : ev.Options.FirstOrDefault(x => x.Id == id);

    internal static string Body(StoryEvent ev, StoryState state, bool? en = null)
    {
        string body = ev.Dialogue(en);
        if (ev.Id == "E10") body += StoryInvestigation.Complete(state)
            ? AugText.GetFor("aug.one_more_thing_there_s_a_test", en)
            : AugText.GetFor("aug.you_ve_seen_the_inventory_too_which", en);
        return body;
    }
    internal static string OptionLabel(StoryEvent ev, StoryOption option, StoryState state, bool? en = null) =>
        ev.Id == "E08" && option.Id == "share" && state.Choice("E03") == "pretend"
            ? AugText.GetFor("aug.admit_you_lied_to_her_then_give", en)
            : option.Label(en);

    internal static string Ending(string id, StoryState state, bool? en)
    {
        return id switch
        {
            "legal_property" => AugText.GetFor("aug.the_body_s_recovery_record_is_cancelled", en) +
                (state.Choice("E08") == "share" ? AugText.GetFor("aug.lin_the_copy_s_still_with_me", en) : "") +
                AugText.GetFor("aug.redemption_ending_achieved", en),
            "settled_account" => AugText.GetFor("aug.the_company_s_control_over_lf_041", en) +
                (state.Choice("E08") == "share" ? AugText.GetFor("aug.lin_says_she_got_the_materials_and", en) : AugText.GetFor("aug.the_evidence_went_to_the_old_merchant", en)) +
                AugText.GetFor("aug.revenge_ending_achieved", en),
            "lost_and_found" => AugText.GetFor("aug.those_who_said_yes_and_had_their", en) +
                AugText.GetFor("aug.liberation_ending_achieved", en),
            "beyond_the_world" => AugText.GetFor("aug.i_opened_the_shop_as_usual_today", en),
            _ => ""
        };
    }
}
