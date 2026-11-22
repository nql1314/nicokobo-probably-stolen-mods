namespace AugMechanicalAscension.Domain;

internal enum SupplyPurchaseStatus
{
    Ready, Applied, Duplicate, AlreadyCommitted, InvalidInput, InvalidSnapshot,
    RunMismatch, RevisionConflict, RunNotReady, RunEnded, DayMismatch,
    Unavailable, SoldOut, InsufficientCredits, DestinationUnavailable,
    SnapshotChanged, OperationPending, InvalidPhase, NeedsRecovery, LedgerFull
}

internal sealed record SupplyPurchaseResult(SupplyPurchaseStatus Status, RunState State,
    long? Price = null);

// All snapshots must come from a verified native player inventory and cash adapter.
// EvidenceId must identify delivery of the exact item to the owned destination, not merely
// a coincidental net increase of one unit while another inventory mutation was in flight.
// No rule here pays money, creates an item, or saves the game.
internal static class SupplyPurchaseRules
{
    internal const string Medical = "medical";
    internal const string Electronic = "electronic";
    internal const string Prepared = "prepared";
    internal const string Applying = "applying";
    internal const string ExternalConfirmed = "externalConfirmed";
    internal const string NeedsRecovery = "needsRecovery";
    internal const int MaxReceipts = AugNumbers.SupplyPurchaseRulesLimits.MaxReceipts;

    internal static SupplyPurchaseResult Preview(RunState state, string kind, int day,
        long credits, int ownedUnits, bool verifiedDestinationReady)
    {
        if (kind is not (Medical or Electronic) || credits is < 0 or > int.MaxValue ||
            ownedUnits is < 0 or > AugNumbers.Validation.MaxOwnedSupplyUnits)
            return new(SupplyPurchaseStatus.InvalidSnapshot, state);
        if (state.InitPhase != "complete") return new(SupplyPurchaseStatus.RunNotReady, state);
        if (state.FailureReason != "none") return new(SupplyPurchaseStatus.RunEnded, state);
        if (day != state.LastObservedDay) return new(SupplyPurchaseStatus.DayMismatch, state);
        if (state.HasPendingExternalOperation)
            return new(SupplyPurchaseStatus.OperationPending, state);
        if (state.SupplyPurchases.Receipts.Length >= MaxReceipts)
            return new(SupplyPurchaseStatus.LedgerFull, state);
        if (!TryOffer(state, kind, day, out _, out _, out int stock, out long price))
            return new(SupplyPurchaseStatus.Unavailable, state);
        if (stock < 1) return new(SupplyPurchaseStatus.SoldOut, state, price);
        if (price is < 1 or > int.MaxValue)
            return new(SupplyPurchaseStatus.InvalidSnapshot, state);
        if (credits < price)
            return new(SupplyPurchaseStatus.InsufficientCredits, state, price);
        if (!verifiedDestinationReady)
            return new(SupplyPurchaseStatus.DestinationUnavailable, state, price);
        return new(SupplyPurchaseStatus.Ready, state, price);
    }

    internal static SupplyPurchaseResult Prepare(RunState state, string expectedRunId,
        long expectedRevision, string requestId, string kind, int day, long credits,
        int ownedUnits, bool verifiedDestinationReady)
    {
        if (state.RunId != expectedRunId) return new(SupplyPurchaseStatus.RunMismatch, state);
        if (!ValidId(requestId) || kind is not (Medical or Electronic))
            return new(SupplyPurchaseStatus.InvalidInput, state);
        if (state.SupplyPurchases.Pending is { } existing)
            return existing.RequestId == requestId && existing.Kind == kind
                ? new(SupplyPurchaseStatus.Duplicate, state, existing.UnitPrice)
                : new(SupplyPurchaseStatus.OperationPending, state);
        if (state.SupplyPurchases.Receipts.Any(x => x.RequestId == requestId))
            return new(SupplyPurchaseStatus.AlreadyCommitted, state);
        if (state.Revision != expectedRevision)
            return new(SupplyPurchaseStatus.RevisionConflict, state);
        var preview = Preview(state, kind, day, credits, ownedUnits,
            verifiedDestinationReady);
        if (preview.Status != SupplyPurchaseStatus.Ready) return preview;
        if (!TryOffer(state, kind, day, out string itemId, out int period,
                out int stock, out long price))
            return new(SupplyPurchaseStatus.Unavailable, state);
        var pending = new SupplyPurchasePending(requestId, kind, itemId, period, day,
            stock, price, credits, ownedUnits, Prepared);
        return Change(state, state.SupplyPurchases with { Pending = pending },
            SupplyPurchaseStatus.Applied, price);
    }

    internal static SupplyPurchaseResult MarkApplying(RunState state, string requestId,
        long credits, int ownedUnits, bool verifiedDestinationReady)
    {
        if (state.SupplyPurchases.Pending is not { } pending ||
            pending.RequestId != requestId)
            return NotPending(state, requestId);
        if (pending.Phase == Applying)
            return new(SupplyPurchaseStatus.Duplicate, state, pending.UnitPrice);
        if (pending.Phase == NeedsRecovery)
            return new(SupplyPurchaseStatus.NeedsRecovery, state);
        if (pending.Phase != Prepared)
            return new(SupplyPurchaseStatus.InvalidPhase, state);
        if (!SameOffer(state, pending))
            return new(SupplyPurchaseStatus.SnapshotChanged, state);
        if (credits != pending.CreditsBefore || ownedUnits != pending.OwnedUnitsBefore)
            return new(SupplyPurchaseStatus.SnapshotChanged, state);
        if (!verifiedDestinationReady)
            return new(SupplyPurchaseStatus.DestinationUnavailable, state);
        return Change(state, state.SupplyPurchases with
        { Pending = pending with { Phase = Applying } }, SupplyPurchaseStatus.Applied);
    }

    internal static SupplyPurchaseResult ConfirmExternal(RunState state, string requestId,
        long creditsAfter, int ownedUnitsAfter, string itemId, string evidenceId)
    {
        if (state.SupplyPurchases.Pending is not { } pending ||
            pending.RequestId != requestId)
            return NotPending(state, requestId);
        if (pending.Phase == NeedsRecovery)
            return new(SupplyPurchaseStatus.NeedsRecovery, state);
        if (pending.Phase == ExternalConfirmed)
            return pending.CreditsAfter == creditsAfter &&
                   pending.OwnedUnitsAfter == ownedUnitsAfter &&
                   pending.ItemId == itemId && pending.EvidenceId == evidenceId
                ? new(SupplyPurchaseStatus.Duplicate, state)
                : Recover(state, pending, "conflictingExternalEvidence",
                    creditsAfter, ownedUnitsAfter, itemId, evidenceId);
        if (pending.Phase != Applying)
            return new(SupplyPurchaseStatus.InvalidPhase, state);
        if (!SameOffer(state, pending) ||
            creditsAfter != pending.CreditsBefore - pending.UnitPrice ||
            ownedUnitsAfter != pending.OwnedUnitsBefore + 1 ||
            itemId != pending.ItemId ||
            !SupplyDeliveryProofRules.IsEvidenceForRequest(requestId, evidenceId))
            return Recover(state, pending, "externalResultMismatch",
                creditsAfter, ownedUnitsAfter, itemId, evidenceId);
        return Change(state, state.SupplyPurchases with
        {
            Pending = pending with
            {
                Phase = ExternalConfirmed, CreditsAfter = creditsAfter,
                OwnedUnitsAfter = ownedUnitsAfter, EvidenceId = evidenceId
            }
        }, SupplyPurchaseStatus.Applied);
    }

    internal static SupplyPurchaseResult RecordUncertainNativeResult(RunState state,
        string requestId, string reason)
    {
        if (state.SupplyPurchases.Pending is not { } pending ||
            pending.RequestId != requestId)
            return NotPending(state, requestId);
        if (pending.Phase == NeedsRecovery)
            return new(SupplyPurchaseStatus.NeedsRecovery, state);
        if (pending.Phase != Applying || !ValidText(reason))
            return new(SupplyPurchaseStatus.InvalidPhase, state);
        return Recover(state, pending, reason);
    }

    internal static SupplyPurchaseResult Commit(RunState state, string requestId)
    {
        if (state.SupplyPurchases.Pending is not { } pending ||
            pending.RequestId != requestId)
            return NotPending(state, requestId);
        if (pending.Phase == NeedsRecovery)
            return new(SupplyPurchaseStatus.NeedsRecovery, state);
        if (pending.Phase != ExternalConfirmed || pending.CreditsAfter == null ||
            pending.OwnedUnitsAfter == null ||
            !SupplyDeliveryProofRules.IsEvidenceForRequest(requestId,
                pending.EvidenceId))
            return new(SupplyPurchaseStatus.InvalidPhase, state);
        if (pending.CreditsAfter != pending.CreditsBefore - pending.UnitPrice ||
            pending.OwnedUnitsAfter != pending.OwnedUnitsBefore + 1)
            return Recover(state, pending, "externalResultChangedBeforeCommit");
        if (!SameOffer(state, pending) || state.PendingOperation != null ||
            state.Cybernetics.PendingPurchase != null ||
            state.NeuralModules.PendingDelivery != null ||
            state.Appraisals.PendingScan != null)
            return Recover(state, pending, "stateChangedBeforeCommit");
        var receipt = new SupplyPurchaseReceipt(pending.RequestId, pending.Kind,
            pending.ItemId, pending.PeriodKey, pending.Day, pending.StockBefore,
            pending.UnitPrice, pending.CreditsBefore, pending.CreditsAfter.Value,
            pending.OwnedUnitsBefore, pending.OwnedUnitsAfter.Value, pending.EvidenceId!,
            pending.StandardPurchasePrice, pending.OfferEvidenceId);
        var purchases = state.SupplyPurchases with
        {
            Receipts = [.. state.SupplyPurchases.Receipts, receipt], Pending = null
        };
        var next = state with { Revision = checked(state.Revision + 1),
            SupplyPurchases = purchases };
        next = pending.Kind == Medical
            ? next with { MedicalSupply = state.MedicalSupply! with
                { Stock = pending.StockBefore - 1 } }
            : next with { ElectronicSupply = state.ElectronicSupply! with
                { Stock = pending.StockBefore - 1 } };
        return new(SupplyPurchaseStatus.Applied, next);
    }

    internal static SupplyPurchaseResult CancelPrepared(RunState state, string requestId)
    {
        if (state.SupplyPurchases.Pending is not { } pending ||
            pending.RequestId != requestId)
            return NotPending(state, requestId);
        if (pending.Phase != Prepared)
            return new(SupplyPurchaseStatus.InvalidPhase, state);
        return Change(state, state.SupplyPurchases with { Pending = null },
            SupplyPurchaseStatus.Applied);
    }

    internal static bool ValidTransition(RunState previous, RunState next)
    {
        var before = previous.SupplyPurchases;
        var after = next.SupplyPurchases;
        if (before?.Receipts == null || after?.Receipts == null ||
            after.Receipts.Length < before.Receipts.Length ||
            after.Receipts.Length > before.Receipts.Length + 1 ||
            !before.Receipts.SequenceEqual(after.Receipts.Take(before.Receipts.Length)))
            return false;
        if (after.Receipts.Length == before.Receipts.Length + 1)
        {
            var pending = before.Pending;
            var receipt = after.Receipts[^1];
            if (pending?.Phase != ExternalConfirmed || after.Pending != null ||
                receipt.RequestId != pending.RequestId || receipt.Kind != pending.Kind ||
                receipt.ItemId != pending.ItemId || receipt.PeriodKey != pending.PeriodKey ||
                receipt.Day != pending.Day || receipt.StockBefore != pending.StockBefore ||
                receipt.UnitPrice != pending.UnitPrice ||
                receipt.CreditsBefore != pending.CreditsBefore ||
                receipt.CreditsAfter != pending.CreditsAfter ||
                receipt.OwnedUnitsBefore != pending.OwnedUnitsBefore ||
                receipt.OwnedUnitsAfter != pending.OwnedUnitsAfter ||
                receipt.EvidenceId != pending.EvidenceId ||
                receipt.StandardPurchasePrice != pending.StandardPurchasePrice ||
                receipt.OfferEvidenceId != pending.OfferEvidenceId)
                return false;
            return pending.Kind == Medical
                ? previous.MedicalSupply is { } medical &&
                  next.MedicalSupply == medical with { Stock = medical.Stock - 1 } &&
                  next.ElectronicSupply == previous.ElectronicSupply
                : previous.ElectronicSupply is { } electronic &&
                  next.ElectronicSupply == electronic with { Stock = electronic.Stock - 1 } &&
                  next.MedicalSupply == previous.MedicalSupply;
        }
        if (!ValidNonPurchaseStockTransition(previous, next)) return false;
        if ((before.Pending != null || after.Pending != null) &&
            (previous.MedicalSupply != next.MedicalSupply ||
             previous.ElectronicSupply != next.ElectronicSupply))
            return false;
        if (before.Pending == null)
            return after.Pending == null || after.Pending.Phase == Prepared;
        if (after.Pending == null)
            return before.Pending.Phase == Prepared;
        var old = before.Pending;
        var current = after.Pending;
        if (old.RequestId != current.RequestId || old.Kind != current.Kind ||
            old.ItemId != current.ItemId || old.PeriodKey != current.PeriodKey ||
            old.Day != current.Day || old.StockBefore != current.StockBefore ||
            old.UnitPrice != current.UnitPrice ||
            old.CreditsBefore != current.CreditsBefore ||
            old.OwnedUnitsBefore != current.OwnedUnitsBefore ||
            old.StandardPurchasePrice != current.StandardPurchasePrice ||
            old.OfferEvidenceId != current.OfferEvidenceId)
            return false;
        return (old.Phase, current.Phase) switch
        {
            (Prepared, Applying) => true,
            (Applying, ExternalConfirmed) => true,
            (Applying or ExternalConfirmed, NeedsRecovery) => true,
            _ => false
        };
    }

    private static bool ValidNonPurchaseStockTransition(RunState previous,
        RunState next)
    {
        var oldMedical = previous.MedicalSupply;
        var newMedical = next.MedicalSupply;
        if (oldMedical != null &&
            (newMedical == null || newMedical.PeriodDay < oldMedical.PeriodDay ||
             (newMedical.PeriodDay == oldMedical.PeriodDay &&
              newMedical != oldMedical) ||
             (newMedical.PeriodDay > oldMedical.PeriodDay &&
              (newMedical.Stock < oldMedical.Stock ||
               newMedical.ItemId != oldMedical.ItemId ||
               newMedical.SpecificationKey != oldMedical.SpecificationKey ||
               newMedical.RecoveryPerUnit != oldMedical.RecoveryPerUnit))))
            return false;
        var oldElectronic = previous.ElectronicSupply;
        var newElectronic = next.ElectronicSupply;
        if (oldElectronic != null &&
            (newElectronic == null || newElectronic.PeriodId < oldElectronic.PeriodId ||
             (newElectronic.PeriodId == oldElectronic.PeriodId &&
              newElectronic != oldElectronic) ||
             (newElectronic.PeriodId > oldElectronic.PeriodId &&
              newElectronic.Stock < oldElectronic.Stock)))
            return false;
        return true;
    }

    private static bool SameOffer(RunState state, SupplyPurchasePending pending)
        => pending.Day == state.LastObservedDay &&
           TryOffer(state, pending.Kind, pending.Day, out string itemId,
               out int period, out int stock, out long price) &&
           itemId == pending.ItemId && period == pending.PeriodKey &&
           stock == pending.StockBefore && price == pending.UnitPrice;

    private static bool TryOffer(RunState state, string kind, int day,
        out string itemId, out int period, out int stock, out long price)
    {
        itemId = ""; period = 0; stock = 0; price = 0;
        if (kind == Medical && state.MedicalSupply is { } medical &&
            medical.PeriodDay == day)
        {
            itemId = medical.ItemId; period = medical.PeriodDay;
            stock = medical.Stock; price = medical.UnitPrice;
            return true;
        }
        if (kind == Electronic && state.ElectronicSupply is { } electronic &&
            electronic.PeriodId ==
                (day - state.StartDay) / state.ConfigSnapshot.ElectronicSupplyCycleDays)
        {
            itemId = SupplyRules.ElectronicItemId; period = electronic.PeriodId;
            stock = electronic.Stock; price = electronic.UnitPrice;
            return true;
        }
        return false;
    }

    private static SupplyPurchaseResult Recover(RunState state,
        SupplyPurchasePending pending, string reason, long? creditsAfter = null,
        int? unitsAfter = null, string? itemId = null, string? evidenceId = null)
    {
        var observation = creditsAfter is >= 0 and <= int.MaxValue &&
                          unitsAfter is >= 0 and <= AugNumbers.Validation.MaxOwnedSupplyUnits &&
                          ValidText(itemId) && ValidText(evidenceId)
            ? new SupplyPurchaseObservation(creditsAfter.Value, unitsAfter.Value,
                itemId!, evidenceId!) : null;
        return Change(state, state.SupplyPurchases with
        {
            Pending = pending with
            {
                Phase = NeedsRecovery, RecoveryReason = reason,
                RecoveryObservation = observation
            }
        }, SupplyPurchaseStatus.NeedsRecovery);
    }

    private static SupplyPurchaseResult NotPending(RunState state, string requestId)
        => state.SupplyPurchases.Receipts.Any(x => x.RequestId == requestId)
            ? new(SupplyPurchaseStatus.AlreadyCommitted, state)
            : new(SupplyPurchaseStatus.InvalidInput, state);

    private static SupplyPurchaseResult Change(RunState state,
        SupplyPurchaseState purchases, SupplyPurchaseStatus status, long? price = null)
        => new(status, state with
        { Revision = checked(state.Revision + 1), SupplyPurchases = purchases }, price);

    private static bool ValidId(string? value)
        => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty &&
           parsed.ToString("D") == value;

    private static bool ValidText(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= AugNumbers.Validation.MaxIdentifierChars;
}
