namespace AugMechanicalAscension.Domain;

internal static class StoryReplies
{
    internal static string Get(string eventId, string option, bool? en, StoryState? state = null)
    {
        if (eventId == "E08" && option == "share" && state?.Choice("E03") == "pretend")
            return AugText.GetFor("aug.you_tell_her_the_truth_the_amnesia", en);
        return (eventId, option) switch
        {
            ("E01", "refund") => AugText.GetFor("aug.fine_i_ll_take_the_money_and", en),
            ("E01", "investigate") => AugText.GetFor("aug.sixty_what_am_i_a_beggar_forget", en),
            ("E01", "decline") => AugText.GetFor("aug.huh_fuck_you_you_crook_just_wait", en),
            ("E02", "accept") => AugText.GetFor("aug.keep_that_receipt_we_ll_need_it", en),
            ("E03", "honest") => AugText.GetFor("aug.i_see_then_i_won_t_call", en),
            ("E03", "pretend") => AugText.GetFor("aug.you_never_used_to_talk_to_me", en),
            ("E04", "compare") => AugText.GetFor("aug.families_usually_keep_a_copy_of_contracts", en),
            ("E05", "keep") => AugText.GetFor("aug.tell_me_when_you_find_something_and", en),
            ("E06", "buy") => AugText.GetFor("aug.two_hundred_got_it_don_t_let", en),
            ("E06", "refuse") => AugText.GetFor("aug.fine_i_ll_take_it_with_me", en),
            ("E07", "disclose") => AugText.GetFor("aug.fine_write_it_i_moved_the_goods", en),
            ("E07", "withhold_name") => AugText.GetFor("aug.thanks_leave_the_original_alone_everything_stays", en),
            ("E08", "share") => AugText.GetFor("aug.he_s_not_coming_back_he_couldn", en),
            ("E08", "hide") => AugText.GetFor("aug.i_m_the_one_who_gave_you", en),
            ("E09", "read") => AugText.GetFor("aug.of_course_the_original_registration_and_the", en),
            ("E10", "legal_property") => AugText.GetFor("aug.the_originals_are_all_here_and_the", en),
            ("E10", "settled_account") => AugText.GetFor("aug.well_this_batch_is_dead_business_fine", en),
            ("E10", "lost_and_found") => AugText.GetFor("aug.the_communicator_lights_up_a_line_from", en),
            ("E10", "beyond_the_world") => AugText.GetFor("aug.good_last_page_i_still_need_you", en),
            (_, StoryRules.SellCore) => AugText.GetFor("aug.three_hundred_count_it_the_core_and", en),
            (_, StoryRules.ArchiveCore) => AugText.GetFor("aug.the_core_reached_the_repair_station_it", en),
            _ => throw new InvalidOperationException("Missing story reply: " + eventId + "/" + option)
        };
    }
}
