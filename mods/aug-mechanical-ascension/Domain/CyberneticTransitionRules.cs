namespace AugMechanicalAscension.Domain;

// A persisted cybernetic revision must follow one purchase or enable step.
// Native payment and durable-save evidence remain the adapter's responsibility.
internal static class CyberneticTransitionRules
{
    internal static bool Valid(RunState previous, RunState next)
    {
        if (previous.Cybernetics?.Purchases == null ||
            previous.Cybernetics.EnabledIds == null ||
            next.Cybernetics?.Purchases == null ||
            next.Cybernetics.EnabledIds == null)
            return false;
        var before = previous.Cybernetics;
        var after = next.Cybernetics;
        if (before.PendingPurchase != null &&
            next.LastObservedDay != previous.LastObservedDay)
            return false;
        if (Same(before, after)) return true;
        try
        {
            var beforeNetwork = before.NetworkUnlocks ?? [];
            var afterNetwork = after.NetworkUnlocks ?? [];
            if (before.CyberEyeDelivery == null &&
                after.CyberEyeDelivery is { } delivery &&
                before.PendingPurchase == after.PendingPurchase &&
                before.Purchases.SequenceEqual(after.Purchases) &&
                before.EnabledIds.SequenceEqual(after.EnabledIds) &&
                beforeNetwork.SequenceEqual(afterNetwork))
            {
                var recorded = CyberneticRules.RecordCyberEyeDelivery(previous,
                    previous.RunId, previous.Revision, delivery.NativeUniqueId,
                    delivery.Day, delivery.EvidenceId);
                return recorded.Status == CyberneticStatus.Applied &&
                       Same(recorded.State.Cybernetics, after);
            }
            if (afterNetwork.Length == beforeNetwork.Length + 1 &&
                beforeNetwork.SequenceEqual(afterNetwork.Take(beforeNetwork.Length)) &&
                before.PendingPurchase == after.PendingPurchase &&
                before.Purchases.SequenceEqual(after.Purchases) &&
                before.EnabledIds.SequenceEqual(after.EnabledIds))
            {
                var receipt = afterNetwork[^1];
                var granted = receipt.CreditsCost is > 0
                    ? receipt.Materials != null
                        ? CyberneticRules.GrantWorkshopUnlockWithMaterials(previous,
                            previous.RunId, previous.Revision, receipt.ItemId,
                            receipt.CreditsBefore ?? -1, receipt.CreditsAfter ?? -1,
                            receipt.Materials, receipt.Day)
                        : CyberneticRules.GrantWorkshopUnlock(previous,
                        previous.RunId, previous.Revision, receipt.ItemId,
                        receipt.CreditsBefore ?? -1, receipt.CreditsAfter ?? -1,
                        receipt.MaterialItemId ?? "", receipt.MaterialUniqueId ?? -1,
                        receipt.MaterialUnitsBefore ?? -1, receipt.Day)
                    : CyberneticRules.GrantNetworkUnlock(previous,
                        previous.RunId, previous.Revision, receipt.ItemId,
                        receipt.UpgradeId, receipt.WildFavorCost,
                        receipt.WildFavorAfter, receipt.Day);
                return granted.Status == CyberneticStatus.Applied &&
                       Same(granted.State.Cybernetics, after);
            }
            if (before.PendingPurchase == null)
            {
                if (after.PendingPurchase is { Phase: CyberneticRules.Prepared } prepared)
                {
                    var result = CyberneticRules.Prepare(previous, previous.RunId,
                        previous.Revision, prepared.RequestId, prepared.ItemId,
                        prepared.Day, prepared.CreditsBefore);
                    return result.Status == CyberneticStatus.Applied &&
                           Same(result.State.Cybernetics, after);
                }
                if (after.PendingPurchase != null ||
                    !before.Purchases.SequenceEqual(after.Purchases))
                    return false;
                var removed = before.EnabledIds.Except(after.EnabledIds,
                    StringComparer.Ordinal).ToArray();
                var added = after.EnabledIds.Except(before.EnabledIds,
                    StringComparer.Ordinal).ToArray();
                if (removed.Length + added.Length != 1) return false;
                string itemId = added.Length == 1 ? added[0] : removed[0];
                var toggled = CyberneticRules.SetEnabled(previous, previous.RunId,
                    previous.Revision, itemId, added.Length == 1);
                return toggled.Status == CyberneticStatus.Applied &&
                       Same(toggled.State.Cybernetics, after);
            }

            var pending = before.PendingPurchase;
            if (after.PendingPurchase is { } current)
            {
                if (pending.Phase == CyberneticRules.Prepared &&
                    current.Phase == CyberneticRules.Applying)
                {
                    var applying = CyberneticRules.MarkApplying(previous,
                        pending.RequestId, pending.CreditsBefore);
                    return applying.Status == CyberneticStatus.Applied &&
                           Same(applying.State.Cybernetics, after);
                }
                if (pending.Phase == CyberneticRules.Applying &&
                    current.Phase == CyberneticRules.ExternalConfirmed &&
                    current.CreditsAfter is { } creditsAfter &&
                    current.EvidenceId is { } evidence)
                {
                    var confirmed = CyberneticRules.ConfirmExternal(previous,
                        pending.RequestId, creditsAfter, evidence);
                    return confirmed.Status == CyberneticStatus.Applied &&
                           Same(confirmed.State.Cybernetics, after);
                }
                if (pending.Phase is CyberneticRules.Applying or
                        CyberneticRules.ExternalConfirmed &&
                    current.Phase == CyberneticRules.NeedsRecovery)
                    return before.Purchases.SequenceEqual(after.Purchases) &&
                           before.EnabledIds.SequenceEqual(after.EnabledIds) &&
                           current == pending with
                           {
                               Phase = CyberneticRules.NeedsRecovery,
                               RecoveryReason = current.RecoveryReason,
                               RecoveryObservation = current.RecoveryObservation
                           };
                return false;
            }
            if (pending.Phase == CyberneticRules.Prepared)
            {
                var cancelled = CyberneticRules.CancelPrepared(previous,
                    pending.RequestId);
                return cancelled.Status == CyberneticStatus.Applied &&
                       Same(cancelled.State.Cybernetics, after);
            }
            if (pending.Phase == CyberneticRules.ExternalConfirmed)
            {
                var committed = CyberneticRules.Commit(previous,
                    pending.RequestId);
                return committed.Status == CyberneticStatus.Applied &&
                       Same(committed.State.Cybernetics, after);
            }
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or
                                   InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    private static bool Same(CyberneticState left, CyberneticState right)
        => left.PendingPurchase == right.PendingPurchase &&
           left.Purchases.SequenceEqual(right.Purchases) &&
           left.EnabledIds.SequenceEqual(right.EnabledIds) &&
           (left.NetworkUnlocks ?? []).SequenceEqual(right.NetworkUnlocks ?? []) &&
           left.CyberEyeDelivery == right.CyberEyeDelivery;
}
