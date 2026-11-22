namespace AugMechanicalAscension.Domain;

internal static class StoryEvidence
{
    internal const string Prefix = "nicokobo.aug.story.evidence.";
    internal static string? EpilogueKey(string? ending) => ending switch
    {
        "legal_property" => "ownership", "settled_account" => "execution",
        "lost_and_found" => "contact", "beyond_the_world" => "investigation", _ => null
    };
    // Calendar-driven additions are declared here so text and counter delivery agree.
    internal static int OvernightUpdateDay(string key, StoryState s, int day)
    {
        if (key == EpilogueKey(s.EndingId) && day > s.EndingDay) return s.EndingDay + 1;
        if (key == "investigation" && s.EndingId == null && s.Investigation == 1 &&
            s.ExperimentDay >= 0 && day > s.ExperimentDay) return s.ExperimentDay + 1;
        return -1;
    }
    internal static string OvernightAppendix(string key, StoryState s, int day, bool? en)
    {
        if (OvernightUpdateDay(key, s, day) < 0) return "";
        if (key == EpilogueKey(s.EndingId)) return StoryCatalog.Ending(s.EndingId!, s, en);
        return AugText.GetFor("aug.the_overnight_observation_is_done_read_the", en);
    }
    internal static readonly string[] Keys = ["receipt", "broken", "index", "photo", "repair", "contract", "recording", "copy", "offer", "credential", "investigation", "ownership", "execution", "contact", StoryJournal.Key];
    internal static (int Width, int Height) Size(string key) => key switch
    {
        "broken" => (3, 2),
        "repair" or "contract" or "offer" or "ownership" or StoryJournal.Key => (2, 3),
        "copy" => (2, 2),
        _ when Keys.Contains(key) => (2, 2),
        _ => throw new ArgumentException("Unknown evidence size")
    };
    internal static string Name(string key, bool? en) => key switch
    {
        StoryJournal.Key => AugText.GetFor("aug.story_journal", en),
        "receipt" => AugText.GetFor("aug.old_receipt_copy", en),
        "broken" => AugText.GetFor("aug.broken_prosthetic", en),
        "index" => AugText.GetFor("aug.repair_index_and_supply_slip", en),
        "photo" => AugText.GetFor("aug.lin_s_photograph", en),
        "repair" => AugText.GetFor("aug.repair_dossier", en),
        "contract" => AugText.GetFor("aug.body_use_contract", en),
        "recording" => AugText.GetFor("aug.recorder", en),
        "copy" => AugText.GetFor("aug.evidence_copy", en),
        "offer" => AugText.GetFor("aug.recovery_offer", en),
        "credential" => AugText.GetFor("aug.one_use_disposition_card", en),
        "investigation" => AugText.GetFor("aug.timing_investigation_notes", en),
        "ownership" => AugText.GetFor("aug.body_ownership_certificate", en),
        "execution" => AugText.GetFor("aug.batch_scrapping_receipt", en),
        "contact" => AugText.GetFor("aug.contact_receipt", en),
        _ => throw new ArgumentException("Unknown evidence")
    };
    internal static string InspectionNote(string key, StoryState? s, bool? en) => key switch
    {
        "broken" when s?.ProstheticInspected == true => MagnifierNote(key, en),
        "photo" when s?.PhotoInspected == true => MagnifierNote(key, en),
        _ => ""
    };
    internal static string MagnifierNote(string key, bool? en) => key switch
    {
        "broken" => AugText.GetFor("aug.up_close_the_scratches_turn_out_to", en),
        "photo" => AugText.GetFor("aug.magnified_the_wrist_scar_in_the_photo", en),
        _ => ""
    };
    internal static string Description(string key, StoryState? s, bool? en)
    {
        if (key is "broken" or "photo") return Body(key, null, en);
        if (key == StoryJournal.Key) return AugText.GetFor("aug.who_came_by_what_they_left_how", en);
        string Text(string key) => AugText.GetFor(key, en);
        string description = key switch
        {
            "receipt" => Text("aug.the_receipt_the_returning_customer_left_the"),
            "index" => Text("aug.the_repair_index_grayline_left_his_supply"),
            "repair" => Text("aug.the_modification_file_of_this_body_s"),
            "contract" => Text("aug.the_old_shopkeeper_s_contract_lets_the"),
            "recording" => s?.RecordingRepaired == true
                ? Text("aug.the_last_thing_the_old_shopkeeper_left")
                : Text("aug.the_recorder_grayline_brought_what_the_old"),
            "copy" => Text("aug.logs_the_recording_and_the_testimony_copied"),
            "offer" => Text("aug.shao_s_offer_hand_over_the_originals"),
            "credential" => Text("aug.the_old_shopkeeper_s_final_signing_authority"),
            "investigation" => s?.EndingId == "beyond_the_world" ? Text("aug.the_receipt_has_an_extra_line_so") : s?.Investigation >= 3 ? Text("aug.the_unfamiliar_address_in_the_diagnostics_answered") : s?.Investigation >= 2 ? Text("aug.even_offline_another_device_in_the_shop") : Text("aug.documents_written_on_different_days_plus_the"),
            "ownership" => Text("aug.the_company_dropped_its_recovery_this_body"),
            "execution" => Text("aug.the_company_s_control_over_lf_041_2"),
            "contact" => Text("aug.the_receipt_lin_brought_some_accepted_some"),
            _ => Text("aug.retained_evidence_read_it_related_actions_are")
        };
        string note = InspectionNote(key, s, en);
        return note.Length == 0 ? description : description + "\n" + note;
    }
    internal static string[] Grants(string ev, string option, bool rewards = true) => ev switch
    {
        "E01" => option == "decline" ? ["receipt"] : ["receipt", "broken"],
        "E02" => rewards ? ["index", "@nicokobo.mechcore.synthesis.titanium_alloy"] : ["index"],
        "E03" => ["photo"],
        "E04" => rewards ? ["repair", "@nicokobo.mechcore.synthesis.advanced_circuit"] : ["repair"],
        "E05" => ["contract"],
        "E07" => rewards && option == "withhold_name" ? ["@nicokobo.mechcore.synthesis.control_chip"] : [],
        "E09" => rewards ? ["offer", "@nicokobo.mechcore.synthesis.power_regulator"] : ["offer"],
        "E10" => option switch { "legal_property" => ["ownership"], "settled_account" => ["execution"], _ => [] },
        _ => []
    };
    internal static string[] EndingInputs(string ending) => ending switch
    {
        "legal_property" => ["credential", "receipt", "index", "repair", "contract", "recording", "offer"],
        "settled_account" => ["credential", "recording", "contract"],
        "lost_and_found" => ["credential", "recording"],
        "beyond_the_world" => ["credential", "recording", "investigation"],
        _ => []
    };
    internal static string Body(string key, StoryState? s, bool? en)
    {
        if (key == StoryJournal.Key) return StoryJournal.Body(s, en);
        string marks = InspectionNote(key, s, en);
        string appendix = marks.Length == 0 ? "" : AugText.GetFor("aug.story.magnifier_appendix", en, marks);
        return key switch
        {
            "receipt" => AugText.GetFor("aug.used_goods_receipt_lot_lf_041_returnable", en) +
                (s?.Choice("E02") != "" && s != null ? AugText.GetFor("aug.index_check_lf_041_is_the_number", en) : "") +
                (s?.Choice("E05") != "" && s != null ? AugText.GetFor("aug.handwriting_check_the_receipt_the_old_shopkeeper", en) : ""),
            "broken" => AugText.GetFor("aug.the_joint_is_snapped_and_won_t", en) + appendix +
                (s?.ProstheticInspected == true && s.Choice("E02") != "" ? AugText.GetFor("aug.index_check_the_repair_bench_stamps_these", en) : ""),
            "photo" => AugText.GetFor("aug.on_the_back_at_the_stair_landing", en) + appendix,
            "index" => AugText.GetFor("aug.repair_index_lf_041_a_batch_of", en),
            "repair" => AugText.GetFor("aug.repair_dossier_lf_041_identity_the_former", en) +
                (s?.ProstheticInspected == true ? AugText.GetFor("aug.calibration_note_the_prosthetic_s_point_count", en) : "") +
                (s?.ProstheticMarksTransferred == true ? AugText.GetFor("aug.the_original_has_been_taken_apart_the", en) : ""),
            "contract" => AugText.GetFor("aug.body_use_contract_signatory_the_former_owner", en),
            "recording" => RecordingBody(s, en),
            "copy" => CopyBody(s, en),
            "credential" => AugText.GetFor("aug.lf_041_disposition_card_the_card_holds", en) +
                AugText.GetFor("aug.note_the_card_came_out_of_the", en) +
                (s?.Choice("E09") != "" && s != null ? AugText.GetFor("aug.checked_against_shao_s_inventory_the_card", en) : "") +
                (s?.Investigation == 3 ? AugText.GetFor("aug.channel_check_the_unfamiliar_address_answered_the", en) : ""),
            "offer" => AugText.GetFor("aug.recovery_offer_unsigned_the_company_only_holds", en),
            "investigation" => InvestigationBody(s, en),
            "ownership" => AugText.GetFor("aug.title_correction_former_claimant_local_body_contractor", en),
            "execution" => AugText.GetFor("aug.lf_041_batch_scrapping_receipt_done_the", en) +
                (s?.Choice("E08") == "share" ? AugText.GetFor("aug.lin_confirms_she_got_it_and_will", en) : AugText.GetFor("aug.the_evidence_went_to_the_old_merchant_2", en)),
            "contact" => AugText.GetFor("aug.lf_041_division_receipt_the_core_reached", en),
            _ => throw new ArgumentException("Unknown evidence body")
        };
    }
    internal static string RecordingBody(StoryState? s, bool? en)
    {
        if (s?.RecordingRepaired != true) return AugText.GetFor("aug.the_main_record_won_t_read_a", en);
        return MainRecord(en) + AugText.GetFor("aug.shipping_and_payment_records_the_body_came", en) +
            (s.ConfessionHeard ? AugText.GetFor("aug.grayline_s_account_he_admits_he_had", en) : AugText.GetFor("aug.note_the_logs_final_diagnosis_and_contract", en)) +
            AugText.GetFor("aug.repair_record_the_one_use_disposition_card", en) +
            (s.DiagnosticRetained ? AugText.GetFor("aug.the_peripheral_diagnostic_tail_was_kept_it", en) : AugText.GetFor("aug.the_peripheral_diagnostic_tail_was_wiped_during", en));
    }
    internal static string MainRecord(bool? en) => AugText.GetFor("aug.original_procedure_log_sealed_by_the_old", en);
    private static string CopyBody(StoryState? s, bool? en)
    {
        string name = s?.Choice("E07") == "withhold_name" ? AugText.GetFor("aug.an_unnamed_supplier", en) : AugText.GetFor("aug.grayline", en);
        string speaker = char.ToUpperInvariant(name[0]) + name[1..];
        string body = (AugText.GetFor("aug.evidence_copy_2", en)) + MainRecord(en);
        body += AugText.GetFor("aug.shipping_and_payment_records_the_body_went", en, name);
        if (s?.ConfessionHeard == true) body += AugText.GetFor("aug.story.confession_appendix", en, speaker);
        if (s?.Choice("E07") == "withhold_name") body += AugText.GetFor("aug.public_extract_his_name_and_the_payment", en);
        return body;
    }
    private static string InvestigationBody(StoryState? s, bool? en)
    {
        string text = s?.Investigation > 0 ? AugText.GetFor("aug.timing_comparison_the_receipt_the_contract_and", en) : "";
        if (s?.ExperimentDay >= 0) text += AugText.GetFor("aug.offline_observation_day_going_by_the_diagnostics", en, s.ExperimentDay) +
            (s.Investigation >= 2 ? AugText.GetFor("aug.result_both_records_skipped_a_beat_at", en) : AugText.GetFor("aug.the_result_hasn_t_been_read_yet", en));
        if (s?.Investigation == 3) text += AugText.GetFor("aug.channel_check_following_the_reply_numbers_i", en);
        if (s?.EndingId == "beyond_the_world") text += AugText.GetFor("aug.final_receipt_running_state_fragment_received_lf", en);
        return text.Length == 0 ? AugText.GetFor("aug.no_verified_comparison_recorded_yet", en) : text;
    }
    internal static string CoreBody(StoryState s, bool? en)
    {
        string text = AugText.GetFor("aug.read_request_detected_no_i_mean_is", en);
        if (s.CoreConsented) text += AugText.GetFor("aug.you_lin_agreed_to_help_verify_the", en);
        if (s.CoreCustodyConsented) text += AugText.GetFor("aug.you_it_s_over_do_you_want", en);
        return text;
    }
}
