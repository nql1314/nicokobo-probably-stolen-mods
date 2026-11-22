namespace AugMechanicalAscension.Domain;

internal enum OperationStatus
{
    Applied, Duplicate, AlreadyCommitted, RunMismatch, RevisionConflict,
    RunNotReady, RunEnded, NoNeed, InvalidInput, OperationPending,
    UnknownOperation, InvalidPhase, DayMismatch, NeedsRecovery
}

internal sealed record OperationResult(OperationStatus Status, RunState State, string? OperationId = null);

// This journal records intent and observed external effects. It does not consume game items.
// A game adapter must persist each phase at a verified save boundary and supply real evidence.
internal static class OperationJournal
{
    internal const string MedicalConsume = "medicalConsume";
    internal const string ExposureRepair = "exposureRepair";
    internal const string Prepared = "prepared";
    internal const string Applying = "applying";
    internal const string ExternalConfirmed = "externalConfirmed";
    internal const string NeedsRecovery = "needsRecovery";

    internal static OperationResult PrepareMedical(RunState state, string expectedRunId, long expectedRevision,
        string requestId, string itemInstanceId, long confirmedPreUseValue)
        => Prepare(state, expectedRunId, expectedRevision, requestId, itemInstanceId,
            confirmedPreUseValue, MedicalConsume);

    internal static OperationResult PrepareExposureRepair(RunState state, string expectedRunId, long expectedRevision,
        string requestId, string electronicInstanceId, bool inspectionActive)
    {
        if (inspectionActive) return new(OperationStatus.InvalidPhase, state);
        return Prepare(state, expectedRunId, expectedRevision, requestId, electronicInstanceId,
            1, ExposureRepair);
    }

    private static OperationResult Prepare(RunState state, string expectedRunId, long expectedRevision,
        string requestId, string itemInstanceId, long expectedValue, string kind)
    {
        if (state.RunId != expectedRunId) return new(OperationStatus.RunMismatch, state);
        if (state.PendingOperation is { } existing)
            return existing.RequestId == requestId && existing.Kind == kind &&
                   existing.ItemInstanceId == itemInstanceId && existing.ExpectedValue == expectedValue
                ? new(OperationStatus.Duplicate, state, existing.OperationId)
                : new(OperationStatus.OperationPending, state, existing.OperationId);
        if (state.LastCommittedRequestId == requestId)
            return new(OperationStatus.AlreadyCommitted, state,
                $"{state.RunId}:{state.LastCommittedOperationSequence}");
        if (state.Cybernetics.PendingPurchase != null ||
            state.NeuralModules.PendingDelivery != null)
            return new(OperationStatus.OperationPending, state);
        if (state.Revision != expectedRevision) return new(OperationStatus.RevisionConflict, state);
        if (state.FailureReason != "none") return new(OperationStatus.RunEnded, state);
        if (state.InitPhase != "complete") return new(OperationStatus.RunNotReady, state);
        if (kind == MedicalConsume && state.Health >= state.ConfigSnapshot.MaxHealth ||
            kind == ExposureRepair && state.Exposure <= 0)
            return new(OperationStatus.NoNeed, state);
        if (!Guid.TryParse(requestId, out var requestGuid) || requestGuid == Guid.Empty ||
            string.IsNullOrWhiteSpace(itemInstanceId) || itemInstanceId.Length > AugNumbers.Validation.MaxIdentifierChars || expectedValue <= 0)
            return new(OperationStatus.InvalidInput, state);

        long sequence = state.NextOperationSequence;
        var pending = new PendingOperation
        {
            Sequence = sequence,
            OperationId = $"{state.RunId}:{sequence}",
            RequestId = requestId,
            Kind = kind,
            Day = state.LastObservedDay,
            ItemInstanceId = itemInstanceId,
            ExpectedValue = expectedValue
        };
        return new(OperationStatus.Applied, state with
        {
            Revision = checked(state.Revision + 1),
            NextOperationSequence = checked(sequence + 1),
            PendingOperation = pending
        }, pending.OperationId);
    }

    internal static OperationResult MarkApplying(RunState state, string operationId)
    {
        var pending = state.PendingOperation;
        if (pending?.OperationId != operationId) return NotCurrent(state, operationId);
        if (pending.Phase == Applying) return new(OperationStatus.Duplicate, state, operationId);
        if (pending.Phase == NeedsRecovery) return new(OperationStatus.NeedsRecovery, state, operationId);
        if (pending.Phase != Prepared) return new(OperationStatus.InvalidPhase, state, operationId);
        if (pending.Day != state.LastObservedDay) return new(OperationStatus.DayMismatch, state, operationId);
        return ChangePending(state, pending with { Phase = Applying });
    }

    internal static OperationResult ConfirmExternal(RunState state, string operationId,
        string itemInstanceId, long actualValue, string evidenceId)
    {
        var pending = state.PendingOperation;
        if (pending?.OperationId != operationId) return NotCurrent(state, operationId);
        if (pending.Phase == NeedsRecovery) return new(OperationStatus.NeedsRecovery, state, operationId);
        if (pending.Phase == ExternalConfirmed)
            return pending.ItemInstanceId == itemInstanceId && pending.ExpectedValue == actualValue &&
                   pending.ConfirmationEvidence == evidenceId
                ? new(OperationStatus.Duplicate, state, operationId)
                : MarkNeedsRecovery(state, operationId, "conflictingExternalEvidence");
        if (pending.Phase != Applying) return new(OperationStatus.InvalidPhase, state, operationId);
        if (pending.Day != state.LastObservedDay || pending.ItemInstanceId != itemInstanceId ||
            pending.ExpectedValue != actualValue || string.IsNullOrWhiteSpace(evidenceId) || evidenceId.Length > AugNumbers.Validation.MaxIdentifierChars)
            return MarkNeedsRecovery(state, operationId, "externalResultMismatch");
        return ChangePending(state, pending with
        {
            Phase = ExternalConfirmed,
            ConfirmationEvidence = evidenceId
        });
    }

    internal static OperationResult Commit(RunState state, string operationId)
    {
        var pending = state.PendingOperation;
        if (pending?.OperationId != operationId) return NotCurrent(state, operationId);
        if (pending.Phase == NeedsRecovery) return new(OperationStatus.NeedsRecovery, state, operationId);
        if (pending.Phase != ExternalConfirmed || pending.ConfirmationEvidence == null)
            return new(OperationStatus.InvalidPhase, state, operationId);
        if (pending.Day != state.LastObservedDay)
            return MarkNeedsRecovery(state, operationId, "dayChangedBeforeCommit");

        int health = state.Health;
        int exposure = state.Exposure;
        int? deadline = state.ExposureDeadlineDay;
        if (pending.Kind == MedicalConsume)
            health += (int)Math.Min(pending.ExpectedValue, state.ConfigSnapshot.MaxHealth - health);
        else if (pending.Kind == ExposureRepair)
        {
            exposure = Math.Max(0, exposure - state.ConfigSnapshot.ExposureRepairPerElectronic);
            if (exposure < state.ConfigSnapshot.MaxExposure) deadline = null;
        }
        else return MarkNeedsRecovery(state, operationId, "unknownOperationKind");

        return new(OperationStatus.Applied, state with
        {
            Revision = checked(state.Revision + 1),
            Health = health,
            Exposure = exposure,
            ExposureDeadlineDay = deadline,
            LastCommittedOperationSequence = pending.Sequence,
            LastCommittedRequestId = pending.RequestId,
            LastCommittedOperation = new CommittedOperationReceipt(pending.Sequence,
                pending.RequestId, pending.Kind, pending.Day, pending.ItemInstanceId,
                pending.ExpectedValue, pending.ConfirmationEvidence),
            PendingOperation = null
        }, operationId);
    }

    internal static OperationResult CancelPrepared(RunState state, string operationId)
    {
        var pending = state.PendingOperation;
        if (pending?.OperationId != operationId) return NotCurrent(state, operationId);
        if (pending.Phase != Prepared) return new(OperationStatus.InvalidPhase, state, operationId);
        return new(OperationStatus.Applied, state with
        {
            Revision = checked(state.Revision + 1),
            PendingOperation = null
        }, operationId);
    }

    internal static OperationResult MarkNeedsRecovery(RunState state, string operationId, string reason)
    {
        var pending = state.PendingOperation;
        if (pending?.OperationId != operationId) return NotCurrent(state, operationId);
        if (pending.Phase == NeedsRecovery) return new(OperationStatus.NeedsRecovery, state, operationId);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > AugNumbers.Validation.MaxIdentifierChars)
            return new(OperationStatus.InvalidInput, state, operationId);
        return new(OperationStatus.NeedsRecovery, state with
        {
            Revision = checked(state.Revision + 1),
            PendingOperation = pending with { Phase = NeedsRecovery, RecoveryReason = reason }
        }, operationId);
    }

    private static OperationResult ChangePending(RunState state, PendingOperation pending)
        => new(OperationStatus.Applied, state with
        {
            Revision = checked(state.Revision + 1),
            PendingOperation = pending
        }, pending.OperationId);

    private static OperationResult NotCurrent(RunState state, string operationId)
        => state.LastCommittedOperationSequence > 0 &&
           operationId == $"{state.RunId}:{state.LastCommittedOperationSequence}"
            ? new(OperationStatus.AlreadyCommitted, state, operationId)
            : new(OperationStatus.UnknownOperation, state, operationId);
}
