namespace AugMechanicalAscension.Domain;

internal enum NeuralModuleStatus
{
    Applied, Duplicate, AlreadyCommitted, AtOwnershipCap, NoFunds,
    NoSpace, OperationPending, RunNotReady, RunEnded, DayMismatch,
    RunMismatch, RevisionConflict, InvalidInput, InvalidPhase,
    NeedsRecovery
}

internal sealed record NeuralModuleResult(NeuralModuleStatus Status, RunState State);

// The module is an actual 1x1 item. Receipts authorize neither manufacture nor
// ownership by themselves; native adapters must verify the saved item instance.
internal static class NeuralModuleRules
{
    internal const string ItemId = "nicokobo.aug.brain_computer_interface_module";
    internal const string Opening = "opening";
    internal const string Shop = "shop";
    internal const string Prepared = "prepared";
    internal const string Applying = "applying";
    internal const string ExternalConfirmed = "externalConfirmed";
    internal const string NeedsRecovery = "needsRecovery";
    internal const int MaximumOwned = AugNumbers.NeuralInterface.MaximumOwned;

    // Every verified owned interface raises the base of every owned interface.
    internal static (int Performance, int Efficiency, int Quality) BaseStats(int ownedCount)
    {
        if (ownedCount <= 0) return (0, 0, 0);
        return checked((AugNumbers.NeuralInterface.PerformancePerOwned * ownedCount,
            AugNumbers.NeuralInterface.EfficiencyPerOwned * ownedCount,
            AugNumbers.NeuralInterface.QualityPerOwned * ownedCount));
    }

    internal static int UnitValue(int ownedCount, int achievementRewardCount)
    {
        const int basePrice = (int)AugNumbers.NeuralInterface.BasePrice;
        if (ownedCount < 0 || ownedCount >= MaximumOwned) return basePrice;
        int priceGrowthCount = Math.Max(0, ownedCount - achievementRewardCount);
        return checked(basePrice << priceGrowthCount);
    }

    // The opening interface is the first one (base value 100) and is granted
    // without payment. The next purchase costs 200, then 400, 800, and so on.
    internal static bool TryPurchasePrice(RunState state, out long price)
    {
        price = 0;
        int ownedCount = state.NeuralModules.Receipts.Length;
        if (ownedCount < 1 || ownedCount > AugNumbers.NeuralInterface.MaxPriceShift) return false;
        const long basePrice = AugNumbers.NeuralInterface.BasePrice;
        if (basePrice > int.MaxValue >> ownedCount) return false;
        price = basePrice << ownedCount;
        return true;
    }

    internal static NeuralModuleResult PreparePurchase(RunState state,
        string expectedRunId, long expectedRevision, string requestId,
        int gameDay, long currentCredits, bool inventorySpaceVerified)
    {
        if (state.RunId != expectedRunId)
            return new(NeuralModuleStatus.RunMismatch, state);
        if (!ValidGuid(requestId))
            return new(NeuralModuleStatus.InvalidInput, state);
        if (state.NeuralModules.Receipts.Any(x => x.RequestId == requestId))
            return new(NeuralModuleStatus.AlreadyCommitted, state);
        if (state.NeuralModules.PendingDelivery is { } pending)
            return pending.RequestId == requestId
                ? new(NeuralModuleStatus.Duplicate, state)
                : new(NeuralModuleStatus.OperationPending, state);
        if (state.Revision != expectedRevision)
            return new(NeuralModuleStatus.RevisionConflict, state);
        if (state.InitPhase != "complete")
            return new(NeuralModuleStatus.RunNotReady, state);
        if (state.FailureReason != "none")
            return new(NeuralModuleStatus.RunEnded, state);
        if (gameDay != state.LastObservedDay)
            return new(NeuralModuleStatus.DayMismatch, state);
        if (state.HasPendingExternalOperation)
            return new(NeuralModuleStatus.OperationPending, state);
        if (state.NeuralModules.Receipts.Length >= MaximumOwned)
            return new(NeuralModuleStatus.AtOwnershipCap, state);
        if (!TryPurchasePrice(state, out long price))
            return new(NeuralModuleStatus.InvalidInput, state);
        if (currentCredits < price)
            return new(NeuralModuleStatus.NoFunds, state);
        if (!inventorySpaceVerified)
            return new(NeuralModuleStatus.NoSpace, state);
        if (state.Revision == long.MaxValue)
            return new(NeuralModuleStatus.InvalidInput, state);
        var prepared = new NeuralModuleDeliveryPending(requestId, Shop,
            gameDay, Prepared, price, currentCredits);
        return Change(state, state.NeuralModules with { PendingDelivery = prepared });
    }

    internal static NeuralModuleResult MarkApplying(RunState state,
        string requestId, int gameDay, long currentCredits,
        bool inventorySpaceVerified)
    {
        var pending = state.NeuralModules.PendingDelivery;
        if (pending?.RequestId != requestId)
            return new(NeuralModuleStatus.InvalidInput, state);
        if (pending.Phase == Applying)
            return new(NeuralModuleStatus.Duplicate, state);
        if (pending.Phase == NeedsRecovery)
            return new(NeuralModuleStatus.NeedsRecovery, state);
        if (pending.Phase != Prepared)
            return new(NeuralModuleStatus.InvalidPhase, state);
        if (gameDay != pending.Day || gameDay != state.LastObservedDay)
            return new(NeuralModuleStatus.DayMismatch, state);
        if (currentCredits != pending.CreditsBefore ||
            !TryPurchasePrice(state, out long price) ||
            (pending.Price != price &&
             !(state.ConfigSnapshot.NeuralModuleTestPrice != AugNumbers.OpeningAndSurvival.NeuralModuleTestPrice &&
               pending.Price == state.ConfigSnapshot.NeuralModuleTestPrice)) ||
            state.NeuralModules.Receipts.Length >= MaximumOwned)
            return new(NeuralModuleStatus.InvalidInput, state);
        if (!inventorySpaceVerified)
            return new(NeuralModuleStatus.NoSpace, state);
        return Change(state, state.NeuralModules with
        { PendingDelivery = pending with { Phase = Applying } });
    }

    internal static NeuralModuleResult ConfirmExternal(RunState state,
        string requestId, string instanceId, long creditsAfter,
        string ownershipEvidenceId, bool nativeReloadVerified)
    {
        var pending = state.NeuralModules.PendingDelivery;
        if (pending?.RequestId != requestId)
            return new(NeuralModuleStatus.InvalidInput, state);
        if (pending.Phase == NeedsRecovery)
            return new(NeuralModuleStatus.NeedsRecovery, state);
        if (pending.Phase == ExternalConfirmed)
            return pending.InstanceId == instanceId &&
                   pending.CreditsAfter == creditsAfter &&
                   pending.OwnershipEvidenceId == ownershipEvidenceId
                ? new(NeuralModuleStatus.Duplicate, state)
                : Recover(state, pending, "conflictingExternalEvidence");
        if (pending.Phase != Applying)
            return new(NeuralModuleStatus.InvalidPhase, state);
        if (!nativeReloadVerified || !MachineRules.ValidText(instanceId) ||
            !MachineRules.ValidText(ownershipEvidenceId) ||
            state.NeuralModules.Receipts.Any(x => x.InstanceId == instanceId) ||
            creditsAfter != pending.CreditsBefore - pending.Price)
            return Recover(state, pending, "purchaseResultUnverified");
        return Change(state, state.NeuralModules with
        {
            PendingDelivery = pending with
            {
                Phase = ExternalConfirmed, InstanceId = instanceId,
                CreditsAfter = creditsAfter,
                OwnershipEvidenceId = ownershipEvidenceId
            }
        });
    }

    internal static NeuralModuleResult CommitPurchase(RunState state,
        string requestId)
    {
        var pending = state.NeuralModules.PendingDelivery;
        if (pending?.RequestId != requestId)
            return state.NeuralModules.Receipts.Any(x => x.RequestId == requestId)
                ? new(NeuralModuleStatus.AlreadyCommitted, state)
                : new(NeuralModuleStatus.InvalidInput, state);
        if (pending.Phase == NeedsRecovery)
            return new(NeuralModuleStatus.NeedsRecovery, state);
        if (pending.Phase != ExternalConfirmed || pending.InstanceId == null ||
            pending.CreditsAfter == null || pending.OwnershipEvidenceId == null)
            return new(NeuralModuleStatus.InvalidPhase, state);
        if (state.NeuralModules.Receipts.Length >= MaximumOwned)
            return Recover(state, pending, "ownershipCapChanged");
        var receipt = new NeuralModuleReceipt(Shop, requestId,
            pending.InstanceId, pending.Day, pending.OwnershipEvidenceId,
            pending.Price, pending.CreditsBefore, pending.CreditsAfter);
        return Change(state, state.NeuralModules with
        {
            PendingDelivery = null,
            Receipts = [.. state.NeuralModules.Receipts, receipt]
        });
    }

    internal static bool ValidTransition(RunState previous, RunState next)
    {
        if (previous.NeuralModules?.Receipts == null ||
            next.NeuralModules?.Receipts == null)
            return false;
        var before = previous.NeuralModules;
        var after = next.NeuralModules;
        if (after.Receipts.Length < before.Receipts.Length ||
            after.Receipts.Length > before.Receipts.Length + 1 ||
            !before.Receipts.SequenceEqual(after.Receipts.Take(before.Receipts.Length)))
            return false;
        if (after.Receipts.Length == before.Receipts.Length + 1)
        {
            var added = after.Receipts[^1];
            if (added.Source == Opening)
            {
                var grant = next.Opening?.Grants.FirstOrDefault(x =>
                    x.Kind == "neuralModule");
                return before.PendingDelivery == null &&
                    after.PendingDelivery == null &&
                    before.Receipts.Length == 0 &&
                    grant?.Result is { Status: OpeningGrantStatus.Applied } result &&
                    previous.Opening?.Grants.FirstOrDefault(x =>
                        x.Kind == "neuralModule")?.Result == null &&
                    added.RequestId == next.Opening!.OperationId &&
                    added.InstanceId == result.NativeItemInstanceId &&
                    added.OwnershipEvidenceId == result.OwnershipEvidenceId;
            }
            if (added.Source != Shop || before.PendingDelivery is not
                { Phase: ExternalConfirmed } committed ||
                after.PendingDelivery != null)
                return false;
            return added == new NeuralModuleReceipt(Shop,
                committed.RequestId, committed.InstanceId!, committed.Day,
                committed.OwnershipEvidenceId!, committed.Price,
                committed.CreditsBefore, committed.CreditsAfter);
        }
        if (before.PendingDelivery == after.PendingDelivery) return true;
        if (before.PendingDelivery == null)
        {
            var candidate = after.PendingDelivery;
            if (candidate is not { Phase: Prepared, Source: Shop }) return false;
            var planned = PreparePurchase(previous, previous.RunId,
                previous.Revision, candidate.RequestId, candidate.Day,
                candidate.CreditsBefore, true);
            return planned.Status == NeuralModuleStatus.Applied &&
                planned.State.NeuralModules.PendingDelivery == candidate;
        }
        var old = before.PendingDelivery;
        var current = after.PendingDelivery;
        if (current == null) return false;
        if (old.RequestId != current.RequestId || old.Source != current.Source ||
            old.Day != current.Day || old.Price != current.Price ||
            old.CreditsBefore != current.CreditsBefore)
            return false;
        if (old.Phase == Prepared && current.Phase == Applying)
            return current.InstanceId == null && current.CreditsAfter == null &&
                current.OwnershipEvidenceId == null;
        if (old.Phase == Applying && current.Phase == ExternalConfirmed)
            return MachineRules.ValidText(current.InstanceId) &&
                MachineRules.ValidText(current.OwnershipEvidenceId) &&
                current.CreditsAfter == old.CreditsBefore - old.Price &&
                !before.Receipts.Any(x => x.InstanceId == current.InstanceId);
        if (current.Phase == NeedsRecovery)
            return MachineRules.ValidText(current.RecoveryReason) &&
                current.InstanceId == old.InstanceId &&
                current.CreditsAfter == old.CreditsAfter &&
                current.OwnershipEvidenceId == old.OwnershipEvidenceId;
        return false;
    }

    internal static bool ValidState(RunState state)
    {
        var modules = state.NeuralModules;
        if (modules?.Receipts == null ||
            modules.Receipts.Any(x => x == null) ||
            modules.Receipts.Length > MaximumOwned) return false;
        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        var requestIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var receipt in modules.Receipts)
            if (!instanceIds.Add(receipt.InstanceId) ||
                !requestIds.Add(receipt.RequestId)) return false;
        for (int i = 0; i < modules.Receipts.Length; i++)
        {
            var receipt = modules.Receipts[i];
            if (!MachineRules.ValidText(receipt.InstanceId) ||
                !MachineRules.ValidText(receipt.OwnershipEvidenceId) ||
                !ValidGuid(receipt.RequestId) ||
                receipt.Day < state.StartDay || receipt.Day > state.LastObservedDay)
                return false;
            if (receipt.Source == Opening)
            {
                if (receipt != modules.Receipts[0] ||
                    receipt.RequestId != state.Opening?.OperationId ||
                    receipt.Price != 0 || receipt.CreditsBefore != null ||
                    receipt.CreditsAfter != null) return false;
            }
            else if (receipt.Source == Shop)
            {
                if (i < 1 || i > AugNumbers.NeuralInterface.MaxPriceShift ||
                    (receipt.Price != AugNumbers.NeuralInterface.BasePrice << i &&
                     !(state.ConfigSnapshot.NeuralModuleTestPrice != AugNumbers.OpeningAndSurvival.NeuralModuleTestPrice &&
                       receipt.Price == state.ConfigSnapshot.NeuralModuleTestPrice)) ||
                    receipt.CreditsBefore < receipt.Price ||
                    receipt.CreditsAfter != receipt.CreditsBefore - receipt.Price)
                    return false;
            }
            else return false;
        }
        var pending = modules.PendingDelivery;
        if (pending == null) return true;
        return pending.Source == Shop && ValidGuid(pending.RequestId) &&
            pending.Day == state.LastObservedDay &&
            TryPurchasePrice(state, out long expectedPrice) &&
            (pending.Price == expectedPrice ||
             state.ConfigSnapshot.NeuralModuleTestPrice != AugNumbers.OpeningAndSurvival.NeuralModuleTestPrice &&
             pending.Price == state.ConfigSnapshot.NeuralModuleTestPrice) &&
            pending.CreditsBefore >= pending.Price &&
            modules.Receipts.Length < MaximumOwned &&
            pending.Phase is Prepared or Applying or ExternalConfirmed or NeedsRecovery &&
            (pending.Phase is Prepared or Applying &&
             pending.InstanceId == null && pending.CreditsAfter == null &&
             pending.OwnershipEvidenceId == null && pending.RecoveryReason == null ||
             pending.Phase == ExternalConfirmed &&
             MachineRules.ValidText(pending.InstanceId) &&
             MachineRules.ValidText(pending.OwnershipEvidenceId) &&
             pending.CreditsAfter == pending.CreditsBefore - pending.Price &&
             pending.RecoveryReason == null ||
             pending.Phase == NeedsRecovery &&
             MachineRules.ValidText(pending.RecoveryReason));
    }

    private static NeuralModuleResult Recover(RunState state,
        NeuralModuleDeliveryPending pending, string reason)
        => Change(state, state.NeuralModules with
        { PendingDelivery = pending with { Phase = NeedsRecovery, RecoveryReason = reason } },
            NeuralModuleStatus.NeedsRecovery);

    private static NeuralModuleResult Change(RunState state,
        NeuralModuleState modules, NeuralModuleStatus status = NeuralModuleStatus.Applied)
        => new(status, state with
        { Revision = checked(state.Revision + 1), NeuralModules = modules });

    private static bool ValidGuid(string? text)
        => Guid.TryParse(text, out var id) && id != Guid.Empty &&
           id.ToString("D") == text;
}
