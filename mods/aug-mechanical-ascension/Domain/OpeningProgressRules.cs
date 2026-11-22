namespace AugMechanicalAscension.Domain;

internal enum OpeningProgressStatus
{
    Advanced, NeedsRecovery, NoJournalObservation, InvalidPhase,
    RevisionConflict, InvalidEvidence, UnverifiedBoundary
}

internal sealed record OpeningProgressResult(OpeningProgressStatus Status,
    RunState? State);

// Turns one native observation into exactly one candidate state revision. The
// caller must stage and verify that revision before starting the next
// native operation. These rules do not write modData or call a game save.
internal static class OpeningProgressRules
{
    internal static OpeningProgressResult BeginApplying(RunState? state,
        string operationId, long expectedRevision,
        bool preparedJournalVerified)
    {
        if (state == null || state.Opening?.OperationId != operationId ||
            state.InitPhase != "applying" ||
            state.Opening.Phase != OpeningJournalRules.Prepared ||
            !OpeningJournalRules.Valid(state))
            return new(OpeningProgressStatus.InvalidPhase, state);
        if (state.Revision != expectedRevision)
            return new(OpeningProgressStatus.RevisionConflict, state);
        if (!preparedJournalVerified)
            return new(OpeningProgressStatus.UnverifiedBoundary, state);
        var next = state with
        {
            Revision = state.Revision + 1,
            Opening = state.Opening with { Phase = OpeningJournalRules.Applying }
        };
        return Advance(state, next, true);
    }

    internal static OpeningProgressResult RecordEconomy(RunState? state,
        string operationId, long expectedRevision,
        OpeningEconomyApplyResult result)
    {
        if (state == null || result == null ||
            state.Opening?.OperationId != operationId ||
            !OpeningJournalRules.MayStartEconomyAttempt(state))
            return new(OpeningProgressStatus.InvalidPhase, state);
        if (state.Revision != expectedRevision)
            return new(OpeningProgressStatus.RevisionConflict, state);
        if (result.After is { NativeReadVerified: false })
            return new(OpeningProgressStatus.InvalidEvidence, state);

        bool attempted = result.CashAttempted || result.RentAttempted ||
            result.ReputationAttempted;
        OpeningEconomyApplyResult observed;
        bool successful;
        switch (result.Status)
        {
            case OpeningEconomyApplyStatus.Applied:
            case OpeningEconomyApplyStatus.AlreadyMatched:
                if (result.After == null)
                    return new(OpeningProgressStatus.InvalidEvidence, state);
                observed = result;
                successful = OpeningEconomyRules.Matches(state.ConfigSnapshot,
                    result);
                break;
            case OpeningEconomyApplyStatus.Partial:
            case OpeningEconomyApplyStatus.Indeterminate:
                if (!attempted)
                    return new(OpeningProgressStatus.InvalidEvidence, state);
                observed = result;
                successful = false;
                break;
            default:
                if (!attempted)
                    return new(OpeningProgressStatus.NoJournalObservation, state);
                observed = result with
                {
                    Status = OpeningEconomyApplyStatus.Indeterminate,
                    Error = result.Error ?? "Unexpected native economy status"
                };
                successful = false;
                break;
        }

        var next = state with
        {
            Revision = state.Revision + 1,
            InitPhase = successful ? "applying" : "needsRecovery",
            Opening = state.Opening! with
            {
                Phase = successful ? OpeningJournalRules.Applying :
                    OpeningJournalRules.NeedsRecovery,
                Economy = observed
            }
        };
        return Advance(state, next, successful);
    }

    // An attempt lease was consumed, but its result did not provide a usable
    // native observation. Persist uncertainty instead of leaving an applying
    // journal that might later be mistaken for a safe retry opportunity.
    internal static OpeningProgressResult MarkUnobservedEconomy(
        RunState? state, string operationId, long expectedRevision,
        OpeningEconomyApplyResult result)
    {
        var recorded = RecordEconomy(state, operationId, expectedRevision, result);
        if (recorded.Status != OpeningProgressStatus.NoJournalObservation ||
            state?.Opening == null)
            return new(recorded.Status is OpeningProgressStatus.Advanced or
                OpeningProgressStatus.NeedsRecovery or
                OpeningProgressStatus.NoJournalObservation
                ? OpeningProgressStatus.InvalidEvidence : recorded.Status, state);
        var next = state with
        {
            Revision = state.Revision + 1,
            InitPhase = "needsRecovery",
            Opening = state.Opening with
            {
                Phase = OpeningJournalRules.NeedsRecovery,
                Economy = new(OpeningEconomyApplyStatus.Indeterminate,
                    Error: $"Unobserved {result.Status} after attempt")
            }
        };
        return Advance(state, next, false);
    }

    internal static OpeningProgressResult RecordGrant(RunState? state,
        OpeningGrantRequest request, long expectedRevision,
        OpeningGrantResult result, string? eyeToolInstanceId = null)
    {
        if (state == null || request == null || result == null ||
            !OpeningJournalRules.MayStartGrantAttempt(state, request))
            return new(OpeningProgressStatus.InvalidPhase, state);
        if (state.Revision != expectedRevision)
            return new(OpeningProgressStatus.RevisionConflict, state);
        int index = Array.FindIndex(state.Opening!.Grants,
            x => x.Request == request);
        if (index < 0 || result.GrantCalls < 0 ||
            result.GrantCalls > request.Units ||
            result.OwnedBefore is < 0 || result.OwnedAfter is < 0)
            return new(OpeningProgressStatus.InvalidEvidence, state);

        bool exact = result.Status == OpeningGrantStatus.Applied &&
            result.OwnedBefore == request.ExpectedOwnedBefore &&
            result.OwnedAfter == request.ExpectedOwnedBefore + request.Units &&
            result.GrantCalls == request.Units;
        bool neuralModule = state.Opening.Grants[index].Kind == "neuralModule";
        if (neuralModule && exact &&
            (!MachineRules.ValidText(result.NativeItemInstanceId) ||
             !MachineRules.ValidText(result.OwnershipEvidenceId) ||
             state.NeuralModules.Receipts.Length != 0))
            exact = false;
        if (eyeToolInstanceId != null)
            return new(OpeningProgressStatus.InvalidEvidence, state);
        if (!exact && result.GrantCalls == 0)
            return new(OpeningProgressStatus.NoJournalObservation, state);

        var observed = !exact && result.Status is not
            (OpeningGrantStatus.Partial or OpeningGrantStatus.Indeterminate)
            ? result with
            {
                Status = OpeningGrantStatus.Indeterminate,
                Error = result.Error ?? "Native grant not fully verified"
            }
            : result;
        var grants = state.Opening.Grants.ToArray();
        grants[index] = grants[index] with { Result = observed };
        bool allDone = exact && index == grants.Length - 1;
        var next = state with
        {
            Revision = state.Revision + 1,
            InitPhase = exact ? "applying" : "needsRecovery",
            NeuralModules = neuralModule && exact ?
                state.NeuralModules with
                {
                    Receipts =
                    [
                        .. state.NeuralModules.Receipts,
                        new NeuralModuleReceipt(NeuralModuleRules.Opening,
                            state.Opening.OperationId, result.NativeItemInstanceId!,
                            state.StartDay, result.OwnershipEvidenceId!)
                    ]
                } : state.NeuralModules,
            Opening = state.Opening with
            {
                Phase = !exact ? OpeningJournalRules.NeedsRecovery :
                    allDone ? OpeningJournalRules.Applied :
                    OpeningJournalRules.Applying,
                Grants = grants
            }
        };
        return Advance(state, next, exact);
    }

    internal static OpeningProgressResult MarkUnobservedGrant(
        RunState? state, OpeningGrantRequest request, long expectedRevision,
        OpeningGrantResult result)
    {
        var recorded = RecordGrant(state, request, expectedRevision, result);
        if (recorded.Status != OpeningProgressStatus.NoJournalObservation ||
            state?.Opening == null)
            return new(recorded.Status is OpeningProgressStatus.Advanced or
                OpeningProgressStatus.NeedsRecovery or
                OpeningProgressStatus.NoJournalObservation
                ? OpeningProgressStatus.InvalidEvidence : recorded.Status, state);
        var grants = state.Opening.Grants.ToArray();
        int index = Array.FindIndex(grants, x => x.Request == request);
        if (index < 0) return new(OpeningProgressStatus.InvalidEvidence, state);
        grants[index] = grants[index] with
        {
            Result = new OpeningGrantResult(OpeningGrantStatus.Indeterminate,
                Error: $"Unobserved {result.Status} after attempt")
        };
        var next = state with
        {
            Revision = state.Revision + 1,
            InitPhase = "needsRecovery",
            Opening = state.Opening with
            {
                Phase = OpeningJournalRules.NeedsRecovery,
                Grants = grants
            }
        };
        return Advance(state, next, false);
    }

    // Called only after the Applied revision has independently survived the
    // verified live journal and a final native inventory/economy reconciliation.
    internal static OpeningProgressResult PrepareCommit(RunState? state,
        string operationId, long expectedRevision,
        bool appliedJournalVerified,
        bool nativeOpeningReconciledVerified)
    {
        if (state == null || state.Opening?.OperationId != operationId ||
            state.InitPhase != "applying" ||
            state.Opening.Phase != OpeningJournalRules.Applied ||
            !OpeningJournalRules.Valid(state))
            return new(OpeningProgressStatus.InvalidPhase, state);
        if (state.Revision != expectedRevision)
            return new(OpeningProgressStatus.RevisionConflict, state);
        if (!appliedJournalVerified || !nativeOpeningReconciledVerified)
            return new(OpeningProgressStatus.UnverifiedBoundary, state);
        var next = state with
        {
            Revision = state.Revision + 1,
            InitPhase = "complete",
            Opening = state.Opening with { Phase = OpeningJournalRules.Committed }
        };
        return Advance(state, next, true);
    }

    private static OpeningProgressResult Advance(RunState current,
        RunState next, bool successful)
        => current.Revision == long.MaxValue ||
           !OpeningJournalRules.Valid(next) ||
           !OpeningJournalRules.Transition(current, next)
            ? new(OpeningProgressStatus.InvalidEvidence, current)
            : new(successful ? OpeningProgressStatus.Advanced :
                OpeningProgressStatus.NeedsRecovery, next);
}
