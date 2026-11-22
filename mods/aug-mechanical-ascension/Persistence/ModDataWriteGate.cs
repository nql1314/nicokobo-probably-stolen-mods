using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Persistence;

internal enum WriteGateStatus
{
    Ready, MissingState, ExistingState, IdentityMismatch, ContentConflict,
    InvalidTransition, InvalidState
}

internal sealed record WritePlan(string? ExpectedJson, string NextJson, string GameRunId, int SaveSlotId);
internal sealed record WriteGateResult(WriteGateStatus Status, WritePlan? Plan = null);

// Pure preflight for updating one Mod-owned key in the native modData dictionary.
// The plan is an in-memory compare-and-write request, not a disk transaction.
internal static class ModDataWriteGate
{
    internal static WriteGateResult RecheckPreparedNew(InitialSavePlanResult prepared,
        bool keyPresent, string? existingJson, string nativeRunId, int nativeSlotId)
    {
        if (prepared.Status != InitialSavePlanStatus.Ready ||
            prepared.Binding == null || prepared.State == null || prepared.Write == null)
            return new(WriteGateStatus.InvalidState);

        var fresh = New(keyPresent, existingJson, prepared.Binding,
            prepared.State, nativeRunId, nativeSlotId);
        if (fresh.Status != WriteGateStatus.Ready || fresh.Plan == null) return fresh;
        return fresh.Plan == prepared.Write
            ? fresh
            : new(WriteGateStatus.ContentConflict);
    }

    internal static WriteGateResult RecheckPreparedOpening(OpeningSavePlanResult prepared,
        bool keyPresent, string? existingJson, string nativeRunId, int nativeSlotId)
    {
        if (prepared.Status != OpeningSavePlanStatus.Ready ||
            prepared.Previous == null || prepared.State == null || prepared.Write == null)
            return new(WriteGateStatus.InvalidState);
        var fresh = Update(keyPresent, existingJson, prepared.Previous,
            prepared.State, nativeRunId, nativeSlotId);
        if (fresh.Status != WriteGateStatus.Ready || fresh.Plan == null) return fresh;
        return fresh.Plan == prepared.Write
            ? fresh
            : new(WriteGateStatus.ContentConflict);
    }

    internal static WriteGateResult New(bool keyPresent, string? existingJson, BoundStart binding, RunState next,
        string nativeRunId, int nativeSlotId)
    {
        if (keyPresent) return new(WriteGateStatus.ExistingState);
        if (existingJson != null) return new(WriteGateStatus.InvalidState);
        if (!Guid.TryParse(binding.Token, out var token) || token == Guid.Empty ||
            !SameNativeIdentity(binding.GameRunId, binding.SaveSlotId, nativeRunId, nativeSlotId) ||
            !SameNativeIdentity(next.GameRunId, next.SaveSlotId, nativeRunId, nativeSlotId))
            return new(WriteGateStatus.IdentityMismatch);
        if (next.ConfigSnapshot == null || next.Cybernetics?.Purchases == null ||
            next.SupplyPurchases?.Receipts == null ||
            next.NeuralModules?.Receipts == null ||
            next.Machines?.Connections == null ||
            next.Appraisals?.Submissions == null ||
            next.Appraisals.ScannedItems == null ||
            next.Appraisals.ScanReceipts == null ||
            next.Appraisals.ScanCache == null)
            return new(WriteGateStatus.InvalidState);
        if (next.Revision != 0 || next.InitPhase != "pending" || next.Opening != null ||
            next.PendingOperation != null ||
            next.SupplyPurchases.Pending != null ||
            next.SupplyPurchases.Receipts.Length != 0 ||
            next.Cybernetics.PendingPurchase != null || next.Cybernetics.Purchases.Length != 0 ||
            next.NeuralModules.PendingDelivery != null ||
            next.NeuralModules.Receipts.Length != 0 ||
            next.Machines.Connections.Length != 0 ||
            next.Appraisals.Submissions.Length != 0 ||
            next.Appraisals.ScannedItems.Length != 0 ||
            next.Appraisals.ScanReceipts.Length != 0 ||
            next.Appraisals.ScanCache.Length != 0 ||
            next.Appraisals.DebugAllCategoriesUnlocked ||
            next.Appraisals.PendingScan != null ||
            next.EyeToolInstanceId != null)
            return new(WriteGateStatus.InvalidTransition);
        try
        {
            return new(WriteGateStatus.Ready,
                new WritePlan(null, RunStateCodec.Write(next), nativeRunId, nativeSlotId));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            return new(WriteGateStatus.InvalidState);
        }
    }

    internal static WriteGateResult Update(bool keyPresent, string? existingJson, RunState previous, RunState next,
        string nativeRunId, int nativeSlotId, OwnedCyberneticEffects effects = default,
        bool mechanicalAscension = false)
    {
        if (!keyPresent) return new(WriteGateStatus.MissingState);
        if (existingJson == null) return new(WriteGateStatus.InvalidState);
        if (!SameNativeIdentity(previous.GameRunId, previous.SaveSlotId, nativeRunId, nativeSlotId) ||
            !SameNativeIdentity(next.GameRunId, next.SaveSlotId, nativeRunId, nativeSlotId) ||
            next.RunId != previous.RunId)
            return new(WriteGateStatus.IdentityMismatch);
        if (previous.Cybernetics?.Purchases is not { } previousPurchases ||
            previous.SupplyPurchases?.Receipts == null ||
            next.SupplyPurchases?.Receipts == null ||
            next.Cybernetics?.Purchases is not { } nextPurchases ||
            previous.NeuralModules?.Receipts == null ||
            next.NeuralModules?.Receipts == null ||
            previous.Appraisals == null || next.Appraisals == null)
            return new(WriteGateStatus.InvalidState);
        if (previous.Revision == long.MaxValue || next.Revision != previous.Revision + 1 ||
            !OpeningJournalRules.Transition(previous, next) ||
            !SupplyPurchaseRules.ValidTransition(previous, next) ||
            !SurvivalTransitionRules.Valid(previous, next, effects, mechanicalAscension) ||
            !CyberneticTransitionRules.Valid(previous, next) ||
            !NeuralModuleRules.ValidTransition(previous, next) ||
            !MachineRules.ValidTransition(previous, next) ||
            next.StartDay != previous.StartDay || next.StartId != previous.StartId ||
            next.BuildProfileId != previous.BuildProfileId || next.ConfigHash != previous.ConfigHash ||
            next.LastObservedDay < previous.LastObservedDay || next.LastSettledDay < previous.LastSettledDay ||
            previous.EyeToolInstanceId != null &&
            next.EyeToolInstanceId != previous.EyeToolInstanceId ||
            next.NextOperationSequence < previous.NextOperationSequence ||
            next.LastCommittedOperationSequence < previous.LastCommittedOperationSequence ||
            nextPurchases.Length < previousPurchases.Length ||
            !previousPurchases.SequenceEqual(nextPurchases.Take(previousPurchases.Length)) ||
            (next.Cybernetics.NetworkUnlocks ?? []).Length <
                (previous.Cybernetics.NetworkUnlocks ?? []).Length ||
            !(previous.Cybernetics.NetworkUnlocks ?? []).SequenceEqual(
                (next.Cybernetics.NetworkUnlocks ?? []).Take(
                    (previous.Cybernetics.NetworkUnlocks ?? []).Length)) ||
            next.Machines.NextConnectionEpoch < previous.Machines.NextConnectionEpoch ||
            next.Appraisals.DebugAllCategoriesUnlocked !=
                previous.Appraisals.DebugAllCategoriesUnlocked ||
            !AppraisalScanRules.ProgressDoesNotRegress(previous.Appraisals,
                next.Appraisals) ||
            previous.FailureReason != "none" && next.FailureReason != previous.FailureReason)
            return new(WriteGateStatus.InvalidTransition);
        try
        {
            string expected = RunStateCodec.Write(previous);
            // A retired schema is never rewritten into the current format, so only
            // a byte-for-byte current-schema baseline may be updated.
            if (existingJson != expected)
                return new(WriteGateStatus.ContentConflict);
            return new(WriteGateStatus.Ready,
                new WritePlan(existingJson, RunStateCodec.Write(next), nativeRunId, nativeSlotId));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            return new(WriteGateStatus.InvalidState);
        }
    }

    // Dedicated debug-page transition. Only the revision and survival values
    // may differ; all game identity, journals, receipts and configuration stay
    // byte-for-byte equivalent after those fields are normalized.
    internal static WriteGateResult DebugUpdate(bool keyPresent, string? existingJson,
        RunState previous, RunState next, string nativeRunId, int nativeSlotId)
    {
        if (!keyPresent) return new(WriteGateStatus.MissingState);
        if (existingJson == null) return new(WriteGateStatus.InvalidState);
        if (!SameNativeIdentity(previous.GameRunId, previous.SaveSlotId,
                nativeRunId, nativeSlotId) ||
            !SameNativeIdentity(next.GameRunId, next.SaveSlotId,
                nativeRunId, nativeSlotId) || next.RunId != previous.RunId)
            return new(WriteGateStatus.IdentityMismatch);
        if (previous.Appraisals == null || next.Appraisals == null)
            return new(WriteGateStatus.InvalidState);
        if (previous.Revision == long.MaxValue ||
            next.Revision != previous.Revision + 1 ||
            previous.InitPhase != "complete" || next.InitPhase != "complete" ||
            previous.Appraisals.DebugAllCategoriesUnlocked &&
                !next.Appraisals.DebugAllCategoriesUnlocked ||
            next.FailureReason != (next.Health == 0 ? "bodyCollapse"
                : next.Exposure == next.ConfigSnapshot.MaxExposure ? "exposure" : "none") ||
            next.ExposureDeadlineDay != (next.Exposure == next.ConfigSnapshot.MaxExposure
                ? (int?)next.LastObservedDay : null))
            return new(WriteGateStatus.InvalidTransition);
        try
        {
            string expected = RunStateCodec.Write(previous);
            var normalized = next with
            {
                Revision = previous.Revision,
                Health = previous.Health,
                Exposure = previous.Exposure,
                ExposureDeadlineDay = previous.ExposureDeadlineDay,
                FailureReason = previous.FailureReason,
                Appraisals = next.Appraisals with
                {
                    DebugAllCategoriesUnlocked =
                        previous.Appraisals.DebugAllCategoriesUnlocked
                }
            };
            if (RunStateCodec.Write(normalized) != expected || existingJson != expected)
                return new(WriteGateStatus.ContentConflict);
            return new(WriteGateStatus.Ready,
                new WritePlan(existingJson, RunStateCodec.Write(next),
                    nativeRunId, nativeSlotId));
        }
        catch (Exception ex) when (ex is ArgumentException or
                                   InvalidOperationException or OverflowException)
        {
            return new(WriteGateStatus.InvalidState);
        }
    }

    private static bool SameNativeIdentity(string candidateRunId, int candidateSlotId,
        string nativeRunId, int nativeSlotId)
        => candidateSlotId == nativeSlotId && candidateSlotId >= 0 &&
           Guid.TryParse(candidateRunId, out var candidate) && candidate != Guid.Empty &&
           Guid.TryParse(nativeRunId, out var native) && candidate == native;
}
