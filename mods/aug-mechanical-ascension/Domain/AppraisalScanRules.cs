namespace AugMechanicalAscension.Domain;

internal enum EyeScanStatus
{
    Applied, Duplicate, Pending, Cached, AlreadyCommitted, RunMismatch,
    RevisionConflict, RunNotReady, RunEnded, DayMismatch, EyeLocked,
    EyeDisabled, UnsupportedCategory, NotOwned, UnstableIdentity,
    NoPower, InvalidEvidence, InvalidPhase, NeedsRecovery,
    CapacityReached, OperationPending, StateChanged
}

internal sealed record EyeScanResponse(EyeScanStatus Status, RunState State,
    AppraisalScanCache? Cache = null);

internal sealed record EyeScanInput(string RequestId, string ItemInstanceId,
    string CategoryId, string Fingerprint, string AdapterVersion,
    AppraisalScanResult Result, int Day, bool ItemOwned,
    bool ItemIdentityStable, string IdentityEvidenceId,
    string EyeToolInstanceId, bool EyeToolOwned,
    string EyeToolEvidenceId, bool BatteryAttachedToEyeTool,
    string BatteryInstanceId, bool BatteryOwned,
    string BatteryOwnershipEvidenceId, int BatteryCharge);

internal sealed record EyeScanFreshSnapshot(int Day, string ItemInstanceId,
    string Fingerprint, bool ItemOwned, bool ItemIdentityStable,
    string EyeToolInstanceId, bool EyeToolOwned,
    string EyeToolEvidenceId, bool BatteryAttachedToEyeTool,
    string BatteryInstanceId, bool BatteryOwned, int BatteryCharge,
    string IdentityEvidenceId, string BatteryOwnershipEvidenceId);

// Native adapter must supply a stable item identity, state fingerprint, scan
// result and a real battery-debit observation. These rules never debit power.
internal static class AppraisalScanRules
{
    internal const string Prepared = "prepared";
    internal const string Applying = "applying";
    internal const string ExternalConfirmed = "externalConfirmed";
    internal const string NeedsRecovery = "needsRecovery";

    internal static bool ProgressDoesNotRegress(AppraisalState previous,
        AppraisalState next)
    {
        if (previous == null || next == null ||
            previous.Submissions == null || next.Submissions == null ||
            previous.ScannedItems == null || next.ScannedItems == null ||
            previous.ScanReceipts == null || next.ScanReceipts == null ||
            previous.ScanCache == null || next.ScanCache == null ||
            previous.Submissions.Any(x => x == null) ||
            next.Submissions.Any(x => x == null) ||
            previous.ScannedItems.Any(x => x == null) ||
            next.ScannedItems.Any(x => x == null) ||
            previous.ScanReceipts.Any(x => x == null) ||
            next.ScanReceipts.Any(x => x == null) ||
            previous.ScanCache.Any(x => x == null) ||
            next.ScanCache.Any(x => x == null))
            return false;
        if (next.Submissions.Length < previous.Submissions.Length ||
            next.Submissions.Length > previous.Submissions.Length + 1 ||
            !Prefixes(next.Submissions, previous.Submissions) ||
            next.ScannedItems.Length < previous.ScannedItems.Length ||
            next.ScannedItems.Length > previous.ScannedItems.Length + 1 ||
            !Prefixes(next.ScannedItems, previous.ScannedItems) ||
            next.ScanReceipts.Length < previous.ScanReceipts.Length ||
            next.ScanReceipts.Length > previous.ScanReceipts.Length + 1 ||
            !Prefixes(next.ScanReceipts, previous.ScanReceipts))
            return false;
        if (next.Submissions.Length > previous.Submissions.Length &&
            (previous.ScannedItems.Any(x =>
                 x.ItemInstanceId == next.Submissions[^1].ItemInstanceId) ||
             next.ScannedItems.Any(x =>
                 x.ItemInstanceId == next.Submissions[^1].ItemInstanceId)))
            return false;

        var oldPending = previous.PendingScan;
        var pending = next.PendingScan;
        if (oldPending == null)
        {
            if (next.ScanReceipts.Length != previous.ScanReceipts.Length ||
                next.ScannedItems.Length != previous.ScannedItems.Length ||
                !SameCaches(previous.ScanCache, next.ScanCache))
                return false;
            return pending == null || pending.Phase == Prepared;
        }
        if (pending != null)
        {
            if (!SamePendingCore(oldPending, pending) ||
                next.ScanReceipts.Length != previous.ScanReceipts.Length ||
                next.ScannedItems.Length != previous.ScannedItems.Length ||
                !SameCaches(previous.ScanCache, next.ScanCache))
                return false;
            return oldPending.Phase switch
            {
                Prepared => pending.Phase == Applying &&
                            pending.Observation == null &&
                            pending.RecoveryReason == null,
                Applying => (pending.Phase == ExternalConfirmed &&
                             pending.Observation != null) ||
                            (pending.Phase == NeedsRecovery &&
                             AppraisalRules.ValidText(pending.RecoveryReason)),
                ExternalConfirmed or NeedsRecovery => pending.Phase == oldPending.Phase &&
                    pending.Observation == oldPending.Observation &&
                    pending.RecoveryReason == oldPending.RecoveryReason,
                _ => false
            };
        }
        if (oldPending.Phase != ExternalConfirmed ||
            oldPending.Observation == null ||
            next.ScanReceipts.Length != previous.ScanReceipts.Length + 1)
            return false;
        var observation = oldPending.Observation;
        var expectedReceipt = new AppraisalScanReceipt(oldPending.RequestId,
            oldPending.ItemInstanceId, oldPending.CategoryId,
            oldPending.Fingerprint, oldPending.AdapterVersion,
            oldPending.Day, oldPending.BatteryInstanceId,
            observation.ChargeBefore, observation.ChargeAfter,
            observation.EvidenceId);
        if (next.ScanReceipts[^1] != expectedReceipt)
            return false;
        var alreadyScanned = previous.ScannedItems.Any(x =>
            x.ItemInstanceId == oldPending.ItemInstanceId);
        if (alreadyScanned)
        {
            if (next.ScannedItems.Length != previous.ScannedItems.Length)
                return false;
        }
        else if (next.ScannedItems.Length != previous.ScannedItems.Length + 1 ||
                 next.ScannedItems[^1] != new AppraisalScannedItem(
                     oldPending.ItemInstanceId, oldPending.CategoryId,
                     oldPending.RequestId, oldPending.Day,
                     oldPending.IdentityEvidenceId))
            return false;
        var expectedCache = new AppraisalScanCache(oldPending.ItemInstanceId,
            oldPending.CategoryId, oldPending.Fingerprint,
            oldPending.AdapterVersion, oldPending.Result,
            oldPending.RequestId, oldPending.Day,
            oldPending.BatteryInstanceId, observation.ChargeBefore,
            observation.ChargeAfter, observation.EvidenceId);
        return SameCaches(next.ScanCache,
            BuildCache(previous.ScanCache, expectedCache));
    }

    internal static AppraisalScanCache? Cached(RunState state,
        string itemInstanceId, string fingerprint, string adapterVersion)
        => state.Appraisals.ScanCache.FirstOrDefault(cache =>
            cache.ItemInstanceId == itemInstanceId &&
            cache.Fingerprint == fingerprint &&
            cache.AdapterVersion == adapterVersion);

    internal static EyeScanResponse Prepare(RunState state,
        string expectedRunId, long expectedRevision, EyeScanInput input)
    {
        if (state.RunId != expectedRunId)
            return new(EyeScanStatus.RunMismatch, state);
        if (input == null || !Guid.TryParse(input.RequestId, out var requestId) ||
            requestId == Guid.Empty || !ValidInput(input))
            return new(EyeScanStatus.InvalidEvidence, state);
        if (!AppraisalRules.IsCategory(input.CategoryId))
            return new(EyeScanStatus.UnsupportedCategory, state);
        var committed = state.Appraisals.ScanReceipts.FirstOrDefault(x =>
            x.RequestId == input.RequestId);
        if (committed != null)
            return new(EyeScanStatus.AlreadyCommitted, state,
                state.Appraisals.ScanCache.FirstOrDefault(x =>
                    x.RequestId == committed.RequestId));
        if (state.Appraisals.PendingScan is { } old)
            return new(old.RequestId == input.RequestId ?
                EyeScanStatus.Duplicate : EyeScanStatus.Pending, state);
        if (input.Day != state.LastObservedDay)
            return new(EyeScanStatus.DayMismatch, state);
        if (!input.ItemOwned)
            return new(EyeScanStatus.NotOwned, state);
        if (!input.ItemIdentityStable)
            return new(EyeScanStatus.UnstableIdentity, state);
        if (state.EyeToolInstanceId != input.EyeToolInstanceId ||
            !input.EyeToolOwned || !input.BatteryAttachedToEyeTool)
            return new(EyeScanStatus.StateChanged, state);
        if (state.Appraisals.ScannedItems.Any(x =>
                x.ItemInstanceId == input.ItemInstanceId &&
                x.CategoryId != input.CategoryId) ||
            state.Appraisals.Submissions.Any(x =>
                x.ItemInstanceId == input.ItemInstanceId &&
                x.CategoryId != input.CategoryId))
            return new(EyeScanStatus.StateChanged, state);
        var cached = Cached(state, input.ItemInstanceId,
            input.Fingerprint, input.AdapterVersion);
        if (cached != null)
            return new(EyeScanStatus.Cached, state, cached);
        if (state.Revision != expectedRevision)
            return new(EyeScanStatus.RevisionConflict, state);
        if (state.InitPhase != "complete")
            return new(EyeScanStatus.RunNotReady, state);
        if (state.FailureReason != "none")
            return new(EyeScanStatus.RunEnded, state);
        if (state.HasPendingExternalOperation)
            return new(EyeScanStatus.OperationPending, state);
        if (!input.BatteryOwned)
            return new(EyeScanStatus.NotOwned, state);
        if (!AppraisalRules.IsUnlocked(state, input.CategoryId))
            return new(EyeScanStatus.EyeLocked, state);
        if (!state.Cybernetics.Enabled(CyberneticRules.CyberEye))
            return new(EyeScanStatus.EyeDisabled, state);
        if (input.BatteryCharge < state.ConfigSnapshot.EyeScanBatteryCost)
            return new(EyeScanStatus.NoPower, state);
        if (state.Appraisals.ScanReceipts.Length >= AugNumbers.Validation.MaxScanReceipts ||
            state.Appraisals.ScannedItems.Length >= AugNumbers.Validation.MaxScannedItems &&
            !state.Appraisals.ScannedItems.Any(x =>
                x.ItemInstanceId == input.ItemInstanceId))
            return new(EyeScanStatus.CapacityReached, state);

        var result = input.Result with { Facts = [.. input.Result.Facts] };
        var pending = new AppraisalScanPending(input.RequestId,
            input.ItemInstanceId, input.CategoryId, input.Fingerprint,
            input.AdapterVersion, result, input.Day,
            input.IdentityEvidenceId, input.EyeToolInstanceId,
            input.EyeToolEvidenceId, input.BatteryInstanceId,
            input.BatteryCharge, input.BatteryOwnershipEvidenceId,
            state.ConfigSnapshot.EyeScanBatteryCost, Prepared);
        return Change(state, state.Appraisals with { PendingScan = pending });
    }

    internal static EyeScanResponse MarkApplying(RunState state,
        string requestId, long expectedRevision, EyeScanFreshSnapshot fresh)
    {
        var pending = state.Appraisals.PendingScan;
        if (pending == null || pending.RequestId != requestId || fresh == null)
            return new(EyeScanStatus.InvalidEvidence, state);
        if (pending.Phase == Applying)
            return new(EyeScanStatus.Duplicate, state);
        if (pending.Phase == NeedsRecovery)
            return new(EyeScanStatus.NeedsRecovery, state);
        if (pending.Phase != Prepared)
            return new(EyeScanStatus.InvalidPhase, state);
        if (state.Revision != expectedRevision)
            return new(EyeScanStatus.RevisionConflict, state);
        if (fresh.Day != state.LastObservedDay || fresh.Day != pending.Day)
            return new(EyeScanStatus.DayMismatch, state);
        if (!fresh.ItemOwned || !fresh.BatteryOwned)
            return new(EyeScanStatus.NotOwned, state);
        if (!fresh.ItemIdentityStable)
            return new(EyeScanStatus.UnstableIdentity, state);
        if (state.EyeToolInstanceId != pending.EyeToolInstanceId ||
            !fresh.EyeToolOwned || !fresh.BatteryAttachedToEyeTool)
            return new(EyeScanStatus.StateChanged, state);
        if (!state.Cybernetics.Enabled(CyberneticRules.CyberEye))
            return new(EyeScanStatus.EyeDisabled, state);
        if (fresh.ItemInstanceId != pending.ItemInstanceId ||
            fresh.Fingerprint != pending.Fingerprint ||
            fresh.IdentityEvidenceId != pending.IdentityEvidenceId ||
            fresh.EyeToolInstanceId != pending.EyeToolInstanceId ||
            fresh.EyeToolEvidenceId != pending.EyeToolEvidenceId ||
            fresh.BatteryInstanceId != pending.BatteryInstanceId ||
            fresh.BatteryOwnershipEvidenceId !=
                pending.BatteryOwnershipEvidenceId ||
            fresh.BatteryCharge != pending.ChargeBefore)
            return new(EyeScanStatus.StateChanged, state);
        return Change(state, state.Appraisals with
        { PendingScan = pending with { Phase = Applying } });
    }

    internal static EyeScanResponse ConfirmBattery(RunState state,
        string requestId, AppraisalBatteryObservation observation)
    {
        var pending = state.Appraisals.PendingScan;
        if (pending == null || pending.RequestId != requestId ||
            observation == null ||
            !AppraisalRules.ValidText(observation.BatteryInstanceId) ||
            !AppraisalRules.ValidText(observation.EvidenceId) ||
            observation.ChargeBefore is < 0 or > AugNumbers.Validation.MaxBatteryCharge ||
            observation.ChargeAfter is < 0 or > AugNumbers.Validation.MaxBatteryCharge)
            return new(EyeScanStatus.InvalidEvidence, state);
        if (pending.Phase == ExternalConfirmed)
            return new(pending.Observation == observation ?
                EyeScanStatus.Duplicate : EyeScanStatus.InvalidEvidence, state);
        if (pending.Phase == NeedsRecovery)
            return new(EyeScanStatus.NeedsRecovery, state);
        if (pending.Phase != Applying)
            return new(EyeScanStatus.InvalidPhase, state);
        if (observation.BatteryInstanceId != pending.BatteryInstanceId ||
            observation.ChargeBefore != pending.ChargeBefore ||
            observation.ChargeAfter != pending.ChargeBefore - pending.Cost)
            return Change(state, state.Appraisals with
            {
                PendingScan = pending with
                {
                    Phase = NeedsRecovery,
                    Observation = observation,
                    RecoveryReason = "batteryDebitMismatch"
                }
            }, EyeScanStatus.NeedsRecovery);
        return Change(state, state.Appraisals with
        {
            PendingScan = pending with
            { Phase = ExternalConfirmed, Observation = observation }
        });
    }

    // A native debit may throw after changing power or return only a partial
    // debit. Preserve that fact even if the remaining charge cannot be read.
    internal static EyeScanResponse RecordUncertainNativeDebit(RunState state,
        BatteryDebitResult result, string evidenceId)
    {
        var pending = state.Appraisals.PendingScan;
        if (pending == null || result == null || result.Request == null ||
            !AppraisalRules.ValidText(evidenceId) ||
            result.Request.RequestId != pending.RequestId ||
            result.Request.Cost != pending.Cost ||
            result.Request.ExpectedCharge != pending.ChargeBefore ||
            result.Before?.Charge != pending.ChargeBefore ||
            !result.NativeCallAttempted ||
            result.Status is not (BatteryDebitStatus.Partial or
                BatteryDebitStatus.Indeterminate))
            return new(EyeScanStatus.InvalidEvidence, state);
        if (pending.Phase == NeedsRecovery)
            return new(EyeScanStatus.NeedsRecovery, state);
        if (pending.Phase != Applying)
            return new(EyeScanStatus.InvalidPhase, state);
        var observed = result.After == null ? null :
            new AppraisalBatteryObservation(pending.BatteryInstanceId,
                pending.ChargeBefore, result.After.Charge, evidenceId);
        return Change(state, state.Appraisals with
        {
            PendingScan = pending with
            {
                Phase = NeedsRecovery,
                Observation = observed,
                RecoveryReason = result.Status == BatteryDebitStatus.Partial
                    ? "nativeBatteryDebitPartial" : "nativeBatteryDebitUnknown"
            }
        }, EyeScanStatus.NeedsRecovery);
    }

    // The native adapter's exact return and charge delta must both succeed.
    // Callers must not construct a matching observation from a partial debit.
    internal static EyeScanResponse ConfirmNativeDebit(RunState state,
        BatteryDebitResult result, string evidenceId)
    {
        var pending = state.Appraisals.PendingScan;
        if (pending == null || result == null || result.Request == null ||
            !AppraisalRules.ValidText(evidenceId) ||
            result.Status != BatteryDebitStatus.Applied ||
            !result.NativeCallAttempted ||
            result.Request.RequestId != pending.RequestId ||
            result.Request.Cost != pending.Cost ||
            result.Request.ExpectedCharge != pending.ChargeBefore ||
            result.Before?.Charge != pending.ChargeBefore ||
            result.After?.Charge != pending.ChargeBefore - pending.Cost ||
            result.NativeReturned != pending.Cost)
            return new(EyeScanStatus.InvalidEvidence, state);
        return ConfirmBattery(state, pending.RequestId,
            new AppraisalBatteryObservation(pending.BatteryInstanceId,
                pending.ChargeBefore, result.After.Charge, evidenceId));
    }

    internal static EyeScanResponse Commit(RunState state, string requestId)
    {
        var pending = state.Appraisals.PendingScan;
        if (pending == null)
            return new(state.Appraisals.ScanReceipts.Any(x =>
                    x.RequestId == requestId) ? EyeScanStatus.AlreadyCommitted :
                EyeScanStatus.InvalidEvidence, state);
        if (pending.RequestId != requestId)
            return new(EyeScanStatus.InvalidEvidence, state);
        if (pending.Phase == NeedsRecovery)
            return new(EyeScanStatus.NeedsRecovery, state);
        if (pending.Phase != ExternalConfirmed || pending.Observation == null)
            return new(EyeScanStatus.InvalidPhase, state);
        if (state.Appraisals.ScanReceipts.Length >= AugNumbers.Validation.MaxScanReceipts)
            return new(EyeScanStatus.CapacityReached, state);
        var observation = pending.Observation;
        var receipt = new AppraisalScanReceipt(pending.RequestId,
            pending.ItemInstanceId, pending.CategoryId, pending.Fingerprint,
            pending.AdapterVersion, pending.Day, pending.BatteryInstanceId,
            observation.ChargeBefore, observation.ChargeAfter,
            observation.EvidenceId);
        var cache = new AppraisalScanCache(pending.ItemInstanceId,
            pending.CategoryId, pending.Fingerprint, pending.AdapterVersion,
            pending.Result, pending.RequestId, pending.Day,
            pending.BatteryInstanceId, observation.ChargeBefore,
            observation.ChargeAfter, observation.EvidenceId);
        var scanned = state.Appraisals.ScannedItems.Any(x =>
            x.ItemInstanceId == pending.ItemInstanceId) ?
            state.Appraisals.ScannedItems :
            [.. state.Appraisals.ScannedItems,
                new AppraisalScannedItem(pending.ItemInstanceId,
                    pending.CategoryId, pending.RequestId, pending.Day,
                    pending.IdentityEvidenceId)];
        return Change(state, state.Appraisals with
        {
            PendingScan = null,
            ScanReceipts = [.. state.Appraisals.ScanReceipts, receipt],
            ScannedItems = scanned,
            ScanCache = BuildCache(state.Appraisals.ScanCache, cache)
        }, EyeScanStatus.Applied, cache);
    }

    // True when next starts with exactly the same elements as previous.
    private static bool Prefixes<T>(T[] next, T[] previous)
    {
        if (next.Length < previous.Length) return false;
        for (int index = 0; index < previous.Length; index++)
            if (!EqualityComparer<T>.Default.Equals(next[index], previous[index]))
                return false;
        return true;
    }

    private static AppraisalScanCache[] BuildCache(
        AppraisalScanCache[] previous, AppraisalScanCache latest)
    {
        var retained = previous.Where(x =>
            x.ItemInstanceId != latest.ItemInstanceId).ToList();
        if (retained.Count >= AugNumbers.Validation.MaxScanCacheEntries)
        {
            // Evict the oldest entry by day, then by item instance ID, in one pass.
            int oldestIndex = 0;
            for (int index = 1; index < retained.Count; index++)
            {
                var candidate = retained[index];
                var oldest = retained[oldestIndex];
                if (candidate.Day < oldest.Day ||
                    candidate.Day == oldest.Day && string.CompareOrdinal(
                        candidate.ItemInstanceId, oldest.ItemInstanceId) < 0)
                    oldestIndex = index;
            }
            retained.RemoveAt(oldestIndex);
        }
        retained.Add(latest);
        return retained.ToArray();
    }

    private static bool SamePendingCore(AppraisalScanPending left,
        AppraisalScanPending right)
        => left.RequestId == right.RequestId &&
           left.ItemInstanceId == right.ItemInstanceId &&
           left.CategoryId == right.CategoryId &&
           left.Fingerprint == right.Fingerprint &&
           left.AdapterVersion == right.AdapterVersion &&
           SameResult(left.Result, right.Result) &&
           left.Day == right.Day &&
           left.IdentityEvidenceId == right.IdentityEvidenceId &&
           left.EyeToolInstanceId == right.EyeToolInstanceId &&
           left.EyeToolEvidenceId == right.EyeToolEvidenceId &&
           left.BatteryInstanceId == right.BatteryInstanceId &&
           left.ChargeBefore == right.ChargeBefore &&
           left.BatteryOwnershipEvidenceId ==
               right.BatteryOwnershipEvidenceId &&
           left.Cost == right.Cost;

    private static bool SameCaches(AppraisalScanCache[] left,
        AppraisalScanCache[] right)
        => left.Length == right.Length && left.Zip(right).All(pair =>
            pair.First.ItemInstanceId == pair.Second.ItemInstanceId &&
            pair.First.CategoryId == pair.Second.CategoryId &&
            pair.First.Fingerprint == pair.Second.Fingerprint &&
            pair.First.AdapterVersion == pair.Second.AdapterVersion &&
            SameResult(pair.First.Result, pair.Second.Result) &&
            pair.First.RequestId == pair.Second.RequestId &&
            pair.First.Day == pair.Second.Day &&
            pair.First.BatteryInstanceId == pair.Second.BatteryInstanceId &&
            pair.First.ChargeBefore == pair.Second.ChargeBefore &&
            pair.First.ChargeAfter == pair.Second.ChargeAfter &&
            pair.First.BatteryEvidenceId == pair.Second.BatteryEvidenceId);

    private static bool SameResult(AppraisalScanResult left,
        AppraisalScanResult right)
        => left != null && right != null &&
           left.Facts != null && right.Facts != null &&
           left.BaseValue == right.BaseValue &&
           left.Facts.SequenceEqual(right.Facts);

    internal static bool ValidResult(AppraisalScanResult? result)
        => result?.Facts is { Length: > 0 and <= AugNumbers.Validation.MaxFindings } facts &&
           facts.All(fact => fact != null &&
               AppraisalRules.ValidText(fact.Key) &&
               AppraisalRules.ValidText(fact.Value)) &&
           facts.Select(fact => fact.Key)
               .Distinct(StringComparer.Ordinal).Count() == facts.Length &&
           result.BaseValue is null or >= 0 and <= AugNumbers.Validation.MaxAppraisedBaseValue;

    private static bool ValidInput(EyeScanInput input)
        => AppraisalRules.ValidText(input.ItemInstanceId) &&
           AppraisalRules.ValidText(input.Fingerprint) &&
           AppraisalRules.ValidText(input.AdapterVersion) &&
           AppraisalRules.ValidText(input.IdentityEvidenceId) &&
           AppraisalRules.ValidText(input.EyeToolInstanceId) &&
           AppraisalRules.ValidText(input.EyeToolEvidenceId) &&
           AppraisalRules.ValidText(input.BatteryInstanceId) &&
           AppraisalRules.ValidText(input.BatteryOwnershipEvidenceId) &&
           input.BatteryCharge is >= 0 and <= AugNumbers.Validation.MaxBatteryCharge &&
           ValidResult(input.Result);

    private static EyeScanResponse Change(RunState state,
        AppraisalState appraisal, EyeScanStatus status = EyeScanStatus.Applied,
        AppraisalScanCache? cache = null)
        => new(status, state with
        {
            Revision = checked(state.Revision + 1), Appraisals = appraisal
        }, cache);
}
