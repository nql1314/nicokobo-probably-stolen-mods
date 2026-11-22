namespace AugMechanicalAscension.Domain;

// A save revision may change survival values only through one of the domain
// transitions that owns them. Native evidence and disk persistence are separate
// requirements checked by the caller before this pure write gate is used.
internal static class SurvivalTransitionRules
{
    internal static bool Valid(RunState previous, RunState next,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false)
    {
        if (previous.InitPhase != "complete" || next.InitPhase != "complete")
            return SameSurvival(previous, next) &&
                   previous.LastObservedDay == next.LastObservedDay &&
                   previous.LastSettledDay == next.LastSettledDay;
        try
        {
            if (next.LastObservedDay == previous.LastObservedDay + 1 &&
                next.LastSettledDay == previous.LastObservedDay)
            {
                var reconciled = SurvivalRules.ReconcileDay(previous,
                    next.LastObservedDay, effects, mechanicalAscension);
                return reconciled.Status == SettleStatus.Applied &&
                       SameSurvival(reconciled.State, next) &&
                       reconciled.State.LastObservedDay == next.LastObservedDay &&
                       reconciled.State.LastSettledDay == next.LastSettledDay;
            }
            if (next.LastSettledDay != previous.LastSettledDay)
            {
                if (next.LastObservedDay != previous.LastObservedDay) return false;
                var settled = SurvivalRules.SettleDay(previous,
                    next.LastSettledDay, effects, mechanicalAscension);
                return settled.Status == SettleStatus.Applied &&
                       SameSurvival(settled.State, next) &&
                       settled.State.LastSettledDay == next.LastSettledDay;
            }
            if (SameSurvival(previous, next)) return true;
            if (next.LastObservedDay != previous.LastObservedDay) return false;
            return MatchesVisit(previous, next, mechanicalAscension) ||
                   MatchesAugAction(previous, next, mechanicalAscension) ||
                   MatchesOperationCommit(previous, next);
        }
        catch (Exception ex) when (ex is ArgumentException or
                                   InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    private static bool MatchesVisit(RunState previous, RunState next,
        bool mechanicalAscension)
    {
        if (next.LastSettledDay != previous.LastSettledDay) return false;
        var expected = ExposureVisitRules.ApplyVisit(previous,
            ExposureVisitRules.Security, mechanicalAscension);
        return expected != null && SameSurvival(expected, next);
    }

    private static bool MatchesAugAction(RunState previous, RunState next,
        bool mechanicalAscension)
    {
        if (next.LastSettledDay != previous.LastSettledDay) return false;
        foreach (var action in new[] { AugAction.Threat, AugAction.Kill })
        {
            var expected = AugInteractionRules.ApplyAction(previous, action, mechanicalAscension);
            if (expected != null && SameSurvival(expected, next)) return true;
        }
        return false;
    }

    private static bool MatchesOperationCommit(RunState previous, RunState next)
    {
        if (previous.PendingOperation is not
                { Phase: OperationJournal.ExternalConfirmed } pending ||
            next.PendingOperation != null)
            return false;
        var committed = OperationJournal.Commit(previous, pending.OperationId);
        return committed.Status == OperationStatus.Applied &&
               SameSurvival(committed.State, next) &&
               next.LastCommittedOperation == committed.State.LastCommittedOperation &&
               next.LastCommittedOperationSequence ==
                   committed.State.LastCommittedOperationSequence &&
               next.LastCommittedRequestId ==
                   committed.State.LastCommittedRequestId;
    }

    private static bool SameSurvival(RunState left, RunState right)
        => left.Health == right.Health &&
           left.Exposure == right.Exposure &&
           left.ExposureDeadlineDay == right.ExposureDeadlineDay &&
           left.FailureReason == right.FailureReason;
}
