namespace AugMechanicalAscension.Domain;

internal sealed record StatusPanelView(string Title, IReadOnlyList<string> Lines,
    bool Current);

// Read-only display model. Native identity is checked by the UI before calling this;
// a day mismatch never presents saved survival values as live values.
internal static class StatusPanelRules
{
    internal static StatusPanelView Build(RunState state, int nativeDay,
        bool? preferEnglish, OwnedCyberneticEffects effects = default, bool mechanicalAscension = false)
    {
        string title = AugText.GetFor(StartDefinition.Name.Key, preferEnglish);
        if (state.InitPhase != "complete")
        {
            var warningLines = new List<string>
            {
                state.InitPhase == "needsRecovery"
                    ? (AugText.GetFor("aug.opening_needs_reconciliation", preferEnglish))
                    : (AugText.GetFor("aug.run_initialization_is_incomplete", preferEnglish)),
                AugText.GetFor("aug.saved_phase", preferEnglish, state.InitPhase)
            };
            if (state.Opening?.Phase == OpeningJournalRules.Applied)
                warningLines.Add(AugText.GetFor("aug.opening_saved_verifying_grants_reload_the_save", preferEnglish));
            AddPendingWarning(state, preferEnglish, warningLines);
            return new(title, warningLines, false);
        }
        if (nativeDay != state.LastObservedDay)
        {
            var warningLines = new List<string>
            {
                AugText.GetFor("aug.mod_state_is_waiting_for_day_reconciliation", preferEnglish),
                AugText.GetFor("aug.game_day_saved_day", preferEnglish, nativeDay, state.LastObservedDay)
            };
            AddPendingWarning(state, preferEnglish, warningLines);
            return new(title, warningLines, false);
        }
        if (state.FailureReason != "none")
            return new(title,
                [AugText.GetFor("aug.this_mod_run_has_ended", preferEnglish),
                 AugText.GetFor("aug.reason", preferEnglish, state.FailureReason)], false);

        var lines = new List<string>
        {
            AugText.GetFor("aug.perk_aug_built_in_0_points_0", preferEnglish),
            AugText.GetFor("aug.health", preferEnglish, state.Health, state.ConfigSnapshot.MaxHealth),
            AugText.GetFor("aug.exposure", preferEnglish, state.Exposure, state.ConfigSnapshot.MaxExposure)
        };
        lines.Add(DailyChangeLine(state, preferEnglish, effects, mechanicalAscension));
        if (effects.CommunicationReceiver)
            lines.Add(EventInformationLine(state, preferEnglish));
        AddPendingWarning(state, preferEnglish, lines);
        return new(title, lines, true);
    }

    internal static string EventInformationLine(RunState state, bool? english)
    {
        int chance = ExposureVisitRules.InspectionChancePercent(state.Exposure);
        return AugText.GetFor("aug.holographic_communicator_security_inspection_chance_after_a", english, chance);
    }

    internal static string DailyChangeLine(RunState state, bool? english,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false)
    {
        if (mechanicalAscension) return AugText.GetFor("aug.ascended_no_aug_health_drain_exposure_gain", english);
        var (health, exposure) = SurvivalRules.DailyChanges(state.ConfigSnapshot, effects);
        return AugText.GetFor("aug.daily_health_exposure", english, health, exposure);
    }

    private static void AddPendingWarning(RunState state, bool? preferEnglish,
        List<string> lines)
    {
        if (state.SupplyPurchases.Pending is { } purchase)
            lines.Add(purchase.Phase == SupplyPurchaseRules.NeedsRecovery
                ? (AugText.GetFor("aug.supply_purchase_needs_reconciliation", preferEnglish))
                : (AugText.GetFor("aug.supply_purchase_is_pending", preferEnglish)));
        else if (state.HasPendingExternalOperation)
            lines.Add(AugText.GetFor("aug.an_operation_is_pending", preferEnglish));
    }
}
