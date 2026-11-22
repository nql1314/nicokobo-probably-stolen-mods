namespace AugMechanicalAscension.Domain;

internal enum CyberneticStatus
{
    Ready, Applied, Duplicate, AlreadyPurchased, AlreadyCommitted,
    InvalidInput, RunMismatch, RevisionConflict, RunNotReady, RunEnded,
    InsufficientCredits, BalanceChanged, OperationPending, UnknownRequest,
    InvalidPhase, DayMismatch, NeedsRecovery
}

internal sealed record CyberneticResult(CyberneticStatus Status, RunState State,
    long? Price = null);

// Native cash is never changed here. The adapter must persist each phase and create the
// confirmation from a verified native before/after cash observation.
internal static class CyberneticRules
{
    internal const string SleepControl = "nicokobo.aug.sleep_control";
    internal const string ArtificialAnkle = "nicokobo.aug.artificial_ankle";
    internal const string InfraredVision = "nicokobo.aug.infrared_vision";
    internal const string CyberEye = "nicokobo.aug.cyber_eye";
    internal const string CommunicationReceiver = "nicokobo.aug.communication_receiver";
    internal const string KineticRecovery = "nicokobo.aug.kinetic_recovery";
    internal const string TitaniumSkeleton = "nicokobo.aug.titanium_skeleton";
    internal const string FineMotorAssistant = "nicokobo.aug.fine_motor_assistant";
    internal const string SyntheticVocalCords = "nicokobo.aug.synthetic_vocal_cords";
    internal const string ArtificialHeart = "nicokobo.aug.artificial_heart";
    internal const string MimeticSkin = "nicokobo.aug.mimetic_skin";
    internal const string AiAssistantCore = "nicokobo.aug.ai_assistant_core";
    internal const string Prepared = "prepared";
    internal const string Applying = "applying";
    internal const string ExternalConfirmed = "externalConfirmed";
    internal const string NeedsRecovery = "needsRecovery";

    internal static bool IsCatalogItem(string? itemId)
        => itemId is SleepControl or ArtificialAnkle or InfraredVision or CyberEye or
            CommunicationReceiver or KineticRecovery or TitaniumSkeleton or
            FineMotorAssistant or SyntheticVocalCords or
            ArtificialHeart or MimeticSkin or AiAssistantCore;

    internal static long Price(ConfigSnapshot config, string itemId) => itemId switch
    {
        SleepControl => config.CyberneticPrices.SleepControl,
        ArtificialAnkle => config.CyberneticPrices.ArtificialAnkle,
        InfraredVision => config.CyberneticPrices.InfraredVision,
        CyberEye => config.CyberneticPrices.CyberEye,
        CommunicationReceiver => config.CyberneticPrices.CommunicationReceiver,
        KineticRecovery => config.CyberneticPrices.KineticRecovery,
        TitaniumSkeleton => config.CyberneticPrices.TitaniumSkeleton,
        FineMotorAssistant => config.CyberneticPrices.FineMotorAssistant,
        SyntheticVocalCords => config.CyberneticPrices.SyntheticVocalCords,
        ArtificialHeart => config.CyberneticPrices.ArtificialHeart,
        MimeticSkin => config.CyberneticPrices.MimeticSkin,
        AiAssistantCore => config.CyberneticPrices.AiAssistantCore,
        _ => throw new ArgumentException("Unknown cybernetic", nameof(itemId))
    };

    internal static string NetworkUpgradeId(string itemId) =>
        "NICOKOBO_AUG_" + itemId[(itemId.LastIndexOf('.') + 1)..].ToUpperInvariant();

    internal static string MaterialItemId(CyberneticConfig config, string itemId)
    {
        var materials = config.UnlockMaterials;
        if (materials == null) return config.UnlockMaterialItemIds[0];
        return itemId switch
        {
            SleepControl => materials.SleepControl,
            ArtificialHeart => materials.ArtificialHeart,
            MimeticSkin => materials.MimeticSkin,
            ArtificialAnkle => materials.ArtificialAnkle,
            InfraredVision => materials.InfraredVision,
            CyberEye => materials.CyberEye,
            CommunicationReceiver => materials.CommunicationReceiver,
            KineticRecovery => materials.KineticRecovery,
            TitaniumSkeleton => materials.TitaniumSkeleton,
            FineMotorAssistant => materials.FineMotorAssistant,
            SyntheticVocalCords => materials.SyntheticVocalCords,
            AiAssistantCore => materials.AiAssistantCore,
            _ => throw new ArgumentException("Unknown cybernetic", nameof(itemId))
        };
    }

    internal static bool IsRecipeKey(string key) => key is
        "sleepControl" or "artificialHeart" or "mimeticSkin" or
        "artificialAnkle" or "infraredVision" or "cyberEye" or
        "communicationReceiver" or "kineticRecovery" or
        "titaniumSkeleton" or "fineMotorAssistant" or
        "syntheticVocalCords" or
        "aiAssistantCore" or "mechanicalAscension";

    internal static WorkshopMaterialCost[] Materials(CyberneticConfig config,
        string itemId)
    {
        string key = itemId switch
        {
            SleepControl => "sleepControl",
            ArtificialHeart => "artificialHeart",
            MimeticSkin => "mimeticSkin",
            ArtificialAnkle => "artificialAnkle",
            InfraredVision => "infraredVision",
            CyberEye => "cyberEye",
            CommunicationReceiver => "communicationReceiver",
            KineticRecovery => "kineticRecovery",
            TitaniumSkeleton => "titaniumSkeleton",
            FineMotorAssistant => "fineMotorAssistant",
            SyntheticVocalCords => "syntheticVocalCords",
            AiAssistantCore => "aiAssistantCore",
            "nicokobo.aug.mechanical_ascension" => "mechanicalAscension",
            _ => throw new ArgumentException("Unknown workshop item", nameof(itemId))
        };
        if (config.UnlockRecipes?.TryGetValue(key, out var recipe) == true)
            return recipe;
        string legacy = key == "mechanicalAscension"
            ? config.UnlockMaterials?.MechanicalAscension ?? config.UnlockMaterialItemIds[0]
            : MaterialItemId(config, itemId);
        return [new WorkshopMaterialCost(legacy, AugNumbers.Cybernetics.UnlockMaterialCount)];
    }

    internal static bool ValidMaterialReceipt(CyberneticConfig config,
        string itemId, WorkshopMaterialReceipt[]? paid)
    {
        if (paid is not { Length: > 0 and <= AugNumbers.Validation.MaxRecipeMaterialUnits } || paid.Any(x => x == null ||
            string.IsNullOrWhiteSpace(x.ItemId) || x.UniqueId <= 0 ||
            x.UnitsBefore < x.Quantity || x.Quantity is <= 0 or > AugNumbers.Validation.MaxRecipeMaterialUnits) ||
            paid.Select(x => x.UniqueId).Distinct().Count() != paid.Length)
            return false;
        var recipe = Materials(config, itemId);
        return paid.GroupBy(x => x.ItemId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity), StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .SequenceEqual(recipe.ToDictionary(x => x.ItemId, x => x.Quantity,
                StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal));
    }

    internal static bool ValidLegacyMaterial(CyberneticConfig config,
        string itemId, string? materialItemId) =>
        Materials(config, itemId) is [{ Quantity: 1 } single] &&
        single.ItemId == materialItemId;

    internal static CyberneticResult GrantWorkshopUnlockWithMaterials(RunState state,
        string expectedRunId, long expectedRevision, string itemId,
        int creditsBefore, int creditsAfter, WorkshopMaterialReceipt[] materials,
        int gameDay)
    {
        if (state.RunId != expectedRunId) return new(CyberneticStatus.RunMismatch, state);
        if (state.Revision != expectedRevision)
            return new(CyberneticStatus.RevisionConflict, state);
        if (!IsCatalogItem(itemId) || !ValidMaterialReceipt(
                state.ConfigSnapshot.CyberneticPrices, itemId, materials))
            return new(CyberneticStatus.InvalidInput, state);
        long price = Price(state.ConfigSnapshot, itemId);
        if (price is <= 0 or > int.MaxValue || creditsBefore < price ||
            creditsAfter != creditsBefore - price)
            return new(CyberneticStatus.InvalidInput, state);
        if (state.InitPhase != "complete") return new(CyberneticStatus.RunNotReady, state);
        if (state.FailureReason != "none") return new(CyberneticStatus.RunEnded, state);
        if (gameDay != state.LastObservedDay)
            return new(CyberneticStatus.DayMismatch, state);
        if (state.HasPendingExternalOperation)
            return new(CyberneticStatus.OperationPending, state);
        if (state.Cybernetics.Purchased(itemId))
            return new(CyberneticStatus.AlreadyPurchased, state);
        var receipt = new NetworkCyberneticUnlockReceipt(NetworkUpgradeId(itemId),
            itemId, 0, 0, gameDay, (int)price, creditsBefore, creditsAfter,
            Materials: materials);
        return Change(state, state.Cybernetics with
        {
            NetworkUnlocks = [.. (state.Cybernetics.NetworkUnlocks ?? []), receipt]
        }, CyberneticStatus.Applied, price);
    }

    internal static CyberneticResult GrantWorkshopUnlock(RunState state,
        string expectedRunId, long expectedRevision, string itemId,
        int creditsBefore, int creditsAfter, string materialItemId,
        int materialUniqueId, int materialUnitsBefore, int gameDay)
    {
        if (state.RunId != expectedRunId)
            return new(CyberneticStatus.RunMismatch, state);
        if (state.Revision != expectedRevision)
            return new(CyberneticStatus.RevisionConflict, state);
        if (!IsCatalogItem(itemId))
            return new(CyberneticStatus.InvalidInput, state);
        long price = Price(state.ConfigSnapshot, itemId);
        if (price is <= 0 or > int.MaxValue || creditsBefore < price ||
            creditsAfter != creditsBefore - price || materialUniqueId <= 0 ||
            materialUnitsBefore <= 0 || !ValidLegacyMaterial(
                state.ConfigSnapshot.CyberneticPrices, itemId, materialItemId))
            return new(CyberneticStatus.InvalidInput, state);
        if (state.InitPhase != "complete")
            return new(CyberneticStatus.RunNotReady, state);
        if (state.FailureReason != "none")
            return new(CyberneticStatus.RunEnded, state);
        if (gameDay != state.LastObservedDay)
            return new(CyberneticStatus.DayMismatch, state);
        if (state.HasPendingExternalOperation)
            return new(CyberneticStatus.OperationPending, state);
        if (state.Cybernetics.Purchased(itemId))
            return new(CyberneticStatus.AlreadyPurchased, state);
        var receipt = new NetworkCyberneticUnlockReceipt(NetworkUpgradeId(itemId),
            itemId, 0, 0, gameDay, (int)price, creditsBefore, creditsAfter,
            materialItemId, materialUniqueId, materialUnitsBefore);
        return Change(state, state.Cybernetics with
        {
            NetworkUnlocks = [.. (state.Cybernetics.NetworkUnlocks ?? []), receipt]
        }, CyberneticStatus.Applied, price);
    }

    internal static CyberneticResult GrantNetworkUnlock(RunState state,
        string expectedRunId, long expectedRevision, string itemId,
        string upgradeId, int wildFavorCost, int wildFavorAfter, int gameDay)
    {
        if (state.RunId != expectedRunId)
            return new(CyberneticStatus.RunMismatch, state);
        if (state.Revision != expectedRevision)
            return new(CyberneticStatus.RevisionConflict, state);
        if (!IsCatalogItem(itemId) || NetworkUpgradeId(itemId) != upgradeId ||
            wildFavorCost <= 0 || wildFavorCost != Price(state.ConfigSnapshot, itemId) ||
            wildFavorAfter < 0 ||
            wildFavorAfter > int.MaxValue - wildFavorCost)
            return new(CyberneticStatus.InvalidInput, state);
        if (state.InitPhase != "complete")
            return new(CyberneticStatus.RunNotReady, state);
        if (state.FailureReason != "none")
            return new(CyberneticStatus.RunEnded, state);
        if (gameDay != state.LastObservedDay)
            return new(CyberneticStatus.DayMismatch, state);
        if (state.HasPendingExternalOperation)
            return new(CyberneticStatus.OperationPending, state);
        if (state.Cybernetics.Purchased(itemId))
            return new(CyberneticStatus.AlreadyPurchased, state);
        var receipt = new NetworkCyberneticUnlockReceipt(upgradeId, itemId,
            wildFavorCost, wildFavorAfter, gameDay);
        var existing = state.Cybernetics.NetworkUnlocks ?? [];
        return Change(state, state.Cybernetics with
        {
            NetworkUnlocks = [.. existing, receipt]
        }, CyberneticStatus.Applied, wildFavorCost);
    }

    internal static CyberneticResult RecordCyberEyeDelivery(RunState state,
        string expectedRunId, long expectedRevision, int nativeUniqueId,
        int gameDay, string evidenceId)
    {
        if (state.RunId != expectedRunId)
            return new(CyberneticStatus.RunMismatch, state);
        if (state.Revision != expectedRevision)
            return new(CyberneticStatus.RevisionConflict, state);
        if (nativeUniqueId <= 0 || gameDay != state.LastObservedDay ||
            evidenceId != $"native-item:{nativeUniqueId}:owned")
            return new(CyberneticStatus.InvalidInput, state);
        if (state.InitPhase != "complete")
            return new(CyberneticStatus.RunNotReady, state);
        if (state.FailureReason != "none")
            return new(CyberneticStatus.RunEnded, state);
        if (state.HasPendingExternalOperation)
            return new(CyberneticStatus.OperationPending, state);
        if (!state.Cybernetics.Purchased(CyberEye))
            return new(CyberneticStatus.InvalidPhase, state);
        if (state.Cybernetics.CyberEyeDelivery != null)
            return new(CyberneticStatus.AlreadyCommitted, state);
        return Change(state, state.Cybernetics with
        {
            CyberEyeDelivery = new(CyberEye, nativeUniqueId, gameDay, evidenceId)
        }, CyberneticStatus.Applied);
    }

    internal static CyberneticResult Preview(RunState state, string itemId, int gameDay,
        long currentCredits)
    {
        if (!IsCatalogItem(itemId) || currentCredits is < 0 or > int.MaxValue)
            return new(CyberneticStatus.InvalidInput, state);
        long price = Price(state.ConfigSnapshot, itemId);
        if (state.InitPhase != "complete") return new(CyberneticStatus.RunNotReady, state, price);
        if (state.FailureReason != "none") return new(CyberneticStatus.RunEnded, state, price);
        if (gameDay != state.LastObservedDay) return new(CyberneticStatus.DayMismatch, state, price);
        if (state.Cybernetics.Purchased(itemId))
            return new(CyberneticStatus.AlreadyPurchased, state, price);
        if (state.HasPendingExternalOperation)
            return new(CyberneticStatus.OperationPending, state, price);
        return currentCredits < price
            ? new(CyberneticStatus.InsufficientCredits, state, price)
            : new(CyberneticStatus.Ready, state, price);
    }

    internal static CyberneticResult Prepare(RunState state, string expectedRunId,
        long expectedRevision, string requestId, string itemId, int gameDay,
        long currentCredits)
    {
        if (state.RunId != expectedRunId) return new(CyberneticStatus.RunMismatch, state);
        if (!ValidId(requestId) || !IsCatalogItem(itemId))
            return new(CyberneticStatus.InvalidInput, state);
        if (state.Cybernetics.PendingPurchase is { } pending)
            return pending.RequestId == requestId && pending.ItemId == itemId
                ? new(CyberneticStatus.Duplicate, state, pending.Price)
                : new(CyberneticStatus.OperationPending, state);
        if (state.Cybernetics.Purchases.Any(x => x.RequestId == requestId))
            return new(CyberneticStatus.AlreadyCommitted, state);
        if (state.Revision != expectedRevision)
            return new(CyberneticStatus.RevisionConflict, state);
        var preview = Preview(state, itemId, gameDay, currentCredits);
        if (preview.Status != CyberneticStatus.Ready) return preview;
        var purchase = new CyberneticPurchasePending(requestId, itemId, preview.Price!.Value,
            currentCredits, gameDay, Prepared);
        return Change(state, state.Cybernetics with { PendingPurchase = purchase },
            CyberneticStatus.Applied, purchase.Price);
    }

    internal static CyberneticResult MarkApplying(RunState state, string requestId,
        long currentCredits)
    {
        var pending = state.Cybernetics.PendingPurchase;
        if (pending?.RequestId != requestId) return NotPending(state, requestId);
        if (pending.Phase == Applying) return new(CyberneticStatus.Duplicate, state, pending.Price);
        if (pending.Phase == NeedsRecovery) return new(CyberneticStatus.NeedsRecovery, state);
        if (pending.Phase != Prepared) return new(CyberneticStatus.InvalidPhase, state);
        if (pending.Day != state.LastObservedDay) return new(CyberneticStatus.DayMismatch, state);
        if (currentCredits is < 0 or > int.MaxValue)
            return new(CyberneticStatus.InvalidInput, state);
        if (currentCredits != pending.CreditsBefore)
            return new(CyberneticStatus.BalanceChanged, state, pending.Price);
        return Change(state, state.Cybernetics with
        { PendingPurchase = pending with { Phase = Applying } }, CyberneticStatus.Applied);
    }

    internal static CyberneticResult ConfirmExternal(RunState state, string requestId,
        long actualCreditsAfter, string evidenceId)
    {
        var pending = state.Cybernetics.PendingPurchase;
        if (pending?.RequestId != requestId) return NotPending(state, requestId);
        if (pending.Phase == NeedsRecovery) return new(CyberneticStatus.NeedsRecovery, state);
        if (pending.Phase == ExternalConfirmed)
            return pending.CreditsAfter == actualCreditsAfter && pending.EvidenceId == evidenceId
                ? new(CyberneticStatus.Duplicate, state)
                : Recover(state, pending, "conflictingExternalEvidence",
                    actualCreditsAfter, evidenceId);
        if (pending.Phase != Applying) return new(CyberneticStatus.InvalidPhase, state);
        if (pending.Day != state.LastObservedDay || state.PendingOperation != null ||
            actualCreditsAfter != pending.CreditsBefore - pending.Price ||
            !ValidText(evidenceId))
            return Recover(state, pending, "externalResultMismatch",
                actualCreditsAfter, evidenceId);
        return Change(state, state.Cybernetics with
        {
            PendingPurchase = pending with
            {
                Phase = ExternalConfirmed, CreditsAfter = actualCreditsAfter,
                EvidenceId = evidenceId
            }
        }, CyberneticStatus.Applied);
    }

    internal static CyberneticResult Commit(RunState state, string requestId)
    {
        var pending = state.Cybernetics.PendingPurchase;
        if (pending?.RequestId != requestId) return NotPending(state, requestId);
        if (pending.Phase == NeedsRecovery) return new(CyberneticStatus.NeedsRecovery, state);
        if (pending.Phase != ExternalConfirmed || pending.CreditsAfter == null ||
            pending.EvidenceId == null)
            return new(CyberneticStatus.InvalidPhase, state);
        if (pending.Day != state.LastObservedDay || state.PendingOperation != null)
            return Recover(state, pending, "stateChangedBeforeCommit");
        var receipt = new CyberneticPurchaseReceipt(requestId, pending.ItemId,
            pending.Price, pending.CreditsBefore, pending.CreditsAfter.Value,
            pending.Day, pending.EvidenceId);
        return Change(state, state.Cybernetics with
        {
            Purchases = [.. state.Cybernetics.Purchases, receipt],
            EnabledIds = [.. state.Cybernetics.EnabledIds, pending.ItemId],
            PendingPurchase = null
        }, CyberneticStatus.Applied);
    }

    internal static CyberneticResult CancelPrepared(RunState state, string requestId)
    {
        var pending = state.Cybernetics.PendingPurchase;
        if (pending?.RequestId != requestId) return NotPending(state, requestId);
        if (pending.Phase != Prepared) return new(CyberneticStatus.InvalidPhase, state);
        return Change(state, state.Cybernetics with { PendingPurchase = null },
            CyberneticStatus.Applied);
    }

    internal static CyberneticResult SetEnabled(RunState state, string expectedRunId,
        long expectedRevision, string itemId, bool enabled)
    {
        if (state.RunId != expectedRunId) return new(CyberneticStatus.RunMismatch, state);
        if (!IsCatalogItem(itemId)) return new(CyberneticStatus.InvalidInput, state);
        if (state.Revision != expectedRevision)
            return new(CyberneticStatus.RevisionConflict, state);
        if (state.HasPendingExternalOperation)
            return new(CyberneticStatus.OperationPending, state);
        if (state.InitPhase != "complete") return new(CyberneticStatus.RunNotReady, state);
        if (state.FailureReason != "none") return new(CyberneticStatus.RunEnded, state);
        if (!state.Cybernetics.Purchased(itemId))
            return new(CyberneticStatus.InvalidPhase, state);
        if (state.Cybernetics.Enabled(itemId) == enabled)
            return new(CyberneticStatus.Duplicate, state);
        var ids = enabled
            ? [.. state.Cybernetics.EnabledIds, itemId]
            : state.Cybernetics.EnabledIds.Where(id => id != itemId).ToArray();
        return Change(state, state.Cybernetics with { EnabledIds = ids },
            CyberneticStatus.Applied);
    }

    private static CyberneticResult Recover(RunState state, CyberneticPurchasePending pending,
        string reason, long? observedCreditsAfter = null, string? evidenceId = null)
    {
        var changed = Change(state, state.Cybernetics with
        {
            PendingPurchase = pending with
            {
                Phase = NeedsRecovery, RecoveryReason = reason,
                RecoveryObservation = observedCreditsAfter is >= 0 and <= int.MaxValue &&
                                      ValidText(evidenceId)
                    ? new CyberneticPurchaseObservation(observedCreditsAfter.Value,
                        evidenceId!) : null
            }
        },
            CyberneticStatus.NeedsRecovery);
        return changed;
    }

    private static CyberneticResult NotPending(RunState state, string requestId)
        => state.Cybernetics.Purchases.Any(x => x.RequestId == requestId)
            ? new(CyberneticStatus.AlreadyCommitted, state)
            : new(CyberneticStatus.UnknownRequest, state);

    private static CyberneticResult Change(RunState state, CyberneticState cybernetics,
        CyberneticStatus status, long? price = null)
        => new(status, state with
        {
            Revision = checked(state.Revision + 1), Cybernetics = cybernetics
        }, price);

    private static bool ValidId(string? value)
        => Guid.TryParse(value, out var guid) && guid != Guid.Empty &&
           guid.ToString("D") == value;

    private static bool ValidText(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= AugNumbers.Validation.MaxIdentifierChars;
}
