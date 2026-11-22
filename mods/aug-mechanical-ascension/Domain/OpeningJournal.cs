namespace AugMechanicalAscension.Domain;

// Persisted before any native opening mutation. A result is an observation, not
// permission to replay the request after a crash or an uncertain native call.
internal sealed record OpeningGrantRecord(string Kind, OpeningGrantRequest Request,
    OpeningGrantResult? Result = null);

internal sealed record OpeningJournal(string OperationId, string GameRunId,
    int SaveSlotId, int StartDay, string Phase,
    OpeningGrantRecord[] Grants, OpeningEconomyApplyResult? Economy = null);

internal static class OpeningJournalRules
{
    internal const string MaterialRefinerId = "nicokobo.mechcore.synthesis.material_refiner";
    internal const string Prepared = "prepared";
    internal const string Applying = "applying";
    internal const string Applied = "applied";
    internal const string NeedsRecovery = "needsRecovery";
    internal const string Committed = "committed";

    // Authorize only the next intended native call. Persistence and same-session
    // first-attempt evidence are separate boundaries supplied by the caller.
    internal static bool MayStartEconomyAttempt(RunState? state)
        => ActiveApplying(state) &&
           state!.Opening!.Economy == null &&
           state.Opening.Grants.All(x => x.Result == null);

    internal static bool MayStartGrantAttempt(RunState? state,
        OpeningGrantRequest? request)
    {
        if (!ActiveApplying(state) || request == null ||
            state!.Opening!.Economy is not
                { Status: OpeningEconomyApplyStatus.Applied or
                    OpeningEconomyApplyStatus.AlreadyMatched, After: { } } ||
            !OpeningEconomyRules.Matches(state.ConfigSnapshot, state.Opening.Economy))
            return false;
        var grants = state.Opening.Grants;
        int index = Array.FindIndex(grants, x => x.Request == request);
        if (index < 0 || grants[index].Result != null)
            return false;
        for (int i = 0; i < grants.Length; i++)
        {
            if (i < index && !GrantFullyObserved(grants[i])) return false;
            if (i > index && grants[i].Result != null) return false;
        }
        return true;
    }

    private static bool ActiveApplying(RunState? state)
        => state != null && state.InitPhase == "applying" &&
           state.Opening?.Phase == Applying && state.Revision >= 2 &&
           state.FailureReason == "none" &&
           state.LastObservedDay == state.StartDay && Valid(state);

    private static bool GrantFullyObserved(OpeningGrantRecord grant)
        => grant.Result is { Status: OpeningGrantStatus.Applied } result &&
           result.OwnedBefore == grant.Request.ExpectedOwnedBefore &&
           result.OwnedAfter == grant.Request.ExpectedOwnedBefore +
               grant.Request.Units && result.GrantCalls == grant.Request.Units &&
           (grant.Kind != "neuralModule" ||
            MachineRules.ValidText(result.NativeItemInstanceId) &&
            MachineRules.ValidText(result.OwnershipEvidenceId));

    internal static bool Valid(RunState state)
    {
        var journal = state.Opening;
        if (journal == null)
            return state.InitPhase == "pending" && state.Revision == 0;
        if (!Guid.TryParse(journal.OperationId, out var operationId) || operationId == Guid.Empty ||
            journal.GameRunId != state.GameRunId || journal.SaveSlotId != state.SaveSlotId ||
            journal.StartDay != state.StartDay || journal.Grants == null ||
            journal.Phase is not (Prepared or Applying or Applied or NeedsRecovery or Committed) ||
            state.Revision < (journal.Phase switch
            {
                Prepared => 1,
                Applying or NeedsRecovery => 2,
                Applied => 3,
                Committed => 4,
                _ => long.MaxValue
            }) ||
            journal.Grants.Length is not (6 or 7) ||
            journal.Grants.Any(x => x == null || x.Request == null ||
                x.Kind is not ("neuralModule" or "materialRefiner" or "furnace" or "alarm" or "battery" or
                    "medical" or "electronic" or "oxymore" or "scrapMetal" or "materialSign") ||
                x.Request.OperationId != journal.OperationId ||
                string.IsNullOrWhiteSpace(x.Request.ItemId) || x.Request.ItemId.Length > AugNumbers.Validation.MaxIdentifierChars ||
                x.Request.Units <= 0 || x.Request.Units > AugNumbers.Validation.MaxGrantUnits ||
                x.Request.ExpectedOwnedBefore < 0 ||
                x.Request.ExpectedOwnedBefore > int.MaxValue - x.Request.Units ||
                x.Result is { } result && !ValidGrantResult(x.Request, result)) ||
            journal.Grants.GroupBy(x => x.Kind).Any(group => group.Count() != 1) ||
            journal.Grants.Select(x => x.Request.ItemId).Distinct(StringComparer.Ordinal).Count() !=
                journal.Grants.Length ||
            journal.Grants.Count(x => x.Kind == "neuralModule") != 1 ||
            journal.Grants.Count(x => x.Kind is "materialRefiner" or "furnace" or "alarm") != 1 ||
            journal.Grants.Count(x => x.Kind == "battery") != 1 ||
            journal.Grants.Count(x => x.Kind == "medical") != 1 ||
            journal.Grants.Count(x => x.Kind == "electronic") != 1) return false;

        // Keep the recorded six-grant manifest readable for existing runs.
        // New runs replace the two extra injectors with scrap and a material sign.
        bool legacySupplies = journal.Grants.Length == 6 &&
            journal.Grants[5] is { Kind: "oxymore", Request: { ItemId: "pink_injector", Units: 2 } };
        bool productionSupplies = journal.Grants.Length == 7 &&
            journal.Grants[1].Kind == "materialRefiner" &&
            journal.Grants[5] is { Kind: "scrapMetal", Request: { ItemId: "scrap_metal", Units: AugNumbers.OpeningItems.ScrapMetalCount } } &&
            journal.Grants[6] is { Kind: "materialSign", Request: { ItemId: "sign_material", Units: AugNumbers.OpeningItems.MaterialSignCount } };
        if (!legacySupplies && !productionSupplies) return false;

        // Existing runs retain their recorded furnace or alarm grant.
        if (journal.Grants[0].Request is not { ItemId: NeuralModuleRules.ItemId, Units: AugNumbers.OpeningItems.NeuralInterfaceCount } ||
            journal.Grants[1] is not
                ({ Kind: "materialRefiner", Request: { ItemId: MaterialRefinerId, Units: AugNumbers.OpeningItems.MaterialRefinerCount } } or
                 { Kind: "furnace", Request: { ItemId: "furnace", Units: 1 } } or
                 { Kind: "alarm", Request: { ItemId: "security_alarm", Units: 1 } }) ||
            journal.Grants[2].Request is not { ItemId: "energy_credit", Units: AugNumbers.OpeningItems.BatteryCount } ||
            journal.Grants[3].Request is not { ItemId: "topical_bandage_item", Units: AugNumbers.OpeningItems.MedicalCount } ||
            journal.Grants[4].Request is not { ItemId: "common_electronic", Units: AugNumbers.OpeningItems.ElectronicCount } ||
            state.ConfigSnapshot.InitialBatteryCount != AugNumbers.OpeningAndSurvival.InitialBatteryCount ||
            state.ConfigSnapshot.InitialMedicalCount != AugNumbers.OpeningAndSurvival.InitialMedicalCount ||
            state.ConfigSnapshot.InitialElectronicCount != AugNumbers.OpeningAndSurvival.InitialElectronicCount)
            return false;
        if (journal.Grants[0].Kind != "neuralModule" ||
            journal.Grants[2].Kind != "battery" ||
            journal.Grants[3].Kind != "medical" ||
            journal.Grants[4].Kind != "electronic")
            return false;

        bool missingEarlierGrant = false;
        foreach (var grant in journal.Grants)
        {
            if (grant.Result == null) missingEarlierGrant = true;
            else if (missingEarlierGrant) return false;
        }
        if (journal.Grants.Any(x => x.Result != null) &&
            (journal.Economy is not
                { Status: OpeningEconomyApplyStatus.Applied or
                    OpeningEconomyApplyStatus.AlreadyMatched, After: { } } ||
             !OpeningEconomyRules.Matches(state.ConfigSnapshot, journal.Economy)))
            return false;
        // A legacy appraisal prototype can still populate this later in a
        // synthetic completed run; it is never a required opening grant.
        if (journal.Phase != Committed && state.EyeToolInstanceId != null) return false;
        var moduleGrant = journal.Grants[0];
        var openingReceipt = state.NeuralModules.Receipts.FirstOrDefault(x =>
            x.Source == NeuralModuleRules.Opening);
        if (GrantFullyObserved(moduleGrant) != (openingReceipt != null) ||
            openingReceipt != null &&
            (openingReceipt.RequestId != journal.OperationId ||
             openingReceipt.InstanceId != moduleGrant.Result?.NativeItemInstanceId ||
             openingReceipt.OwnershipEvidenceId !=
                 moduleGrant.Result?.OwnershipEvidenceId))
            return false;

        if (journal.Economy is { } economy &&
            (economy.Status is not (OpeningEconomyApplyStatus.Applied or
                                     OpeningEconomyApplyStatus.AlreadyMatched or
                                     OpeningEconomyApplyStatus.Partial or
                                     OpeningEconomyApplyStatus.Indeterminate) ||
             economy.After is { } after && !after.NativeReadVerified)) return false;

        if (journal.Phase == Prepared &&
            (journal.Economy != null || journal.Grants.Any(x => x.Result != null))) return false;
        if (journal.Phase != NeedsRecovery &&
            (journal.Economy is { Status: OpeningEconomyApplyStatus.Partial or
                OpeningEconomyApplyStatus.Indeterminate } ||
             journal.Grants.Any(x => x.Result is { Status: OpeningGrantStatus.Partial or
                 OpeningGrantStatus.Indeterminate }))) return false;
        if (journal.Phase == Applying && AllObserved(state)) return false;
        if (journal.Phase is Applied or Committed && !AllObserved(state)) return false;
        return state.InitPhase == (journal.Phase == Committed ? "complete" :
            journal.Phase == NeedsRecovery ? "needsRecovery" : "applying");
    }

    internal static bool Transition(RunState previous, RunState next)
    {
        if (previous.InitPhase == "pending" && previous.Opening == null)
            return next.InitPhase == "applying" &&
                   next.Opening?.Phase == Prepared && Valid(next);
        if (previous.Opening == null) return false;
        if (next.Opening is not { } current || !Valid(next)) return false;
        var prior = previous.Opening;
        if (prior.OperationId != current.OperationId || prior.GameRunId != current.GameRunId ||
            prior.SaveSlotId != current.SaveSlotId || prior.StartDay != current.StartDay ||
            prior.Grants.Length != current.Grants.Length) return false;
        for (int i = 0; i < prior.Grants.Length; i++)
        {
            var old = prior.Grants[i];
            var fresh = current.Grants[i];
            if (old.Kind != fresh.Kind || old.Request != fresh.Request ||
                old.Result != null && old.Result != fresh.Result) return false;
        }
        if (prior.Economy != null && prior.Economy != current.Economy) return false;
        int newlyObserved = (prior.Economy == null && current.Economy != null) ? 1 : 0;
        newlyObserved += prior.Grants.Zip(current.Grants).Count(pair =>
            pair.First.Result == null && pair.Second.Result != null);
        if (newlyObserved > 1 || prior.Phase == Prepared && newlyObserved != 0)
            return false;
        return prior.Phase switch
        {
            Prepared => current.Phase is Prepared or Applying or NeedsRecovery &&
                        (current.Phase == NeedsRecovery || SameObservations(prior, current)),
            Applying => current.Phase == Applying && newlyObserved == 1 ||
                        current.Phase == Applied && newlyObserved == 1 ||
                        current.Phase == NeedsRecovery,
            Applied => current.Phase is Applied or Committed &&
                       SameObservations(prior, current),
            NeedsRecovery => current.Phase == NeedsRecovery && SameObservations(prior, current),
            Committed => current.Phase == Committed && SameObservations(prior, current),
            _ => false
        };
    }

    private static bool AllObserved(RunState state)
    {
        var journal = state.Opening!;
        return journal.Economy is { Status: OpeningEconomyApplyStatus.Applied or
                OpeningEconomyApplyStatus.AlreadyMatched, After: { } } &&
            OpeningEconomyRules.Matches(state.ConfigSnapshot, journal.Economy) &&
            journal.Grants.All(GrantFullyObserved);
    }

    private static bool ValidGrantResult(OpeningGrantRequest request, OpeningGrantResult result)
        => Enum.IsDefined(typeof(OpeningGrantStatus), result.Status) &&
           result.GrantCalls >= 0 && result.GrantCalls <= request.Units &&
           result.OwnedBefore is null or >= 0 && result.OwnedAfter is null or >= 0 &&
           (result.NativeItemInstanceId == null ||
            MachineRules.ValidText(result.NativeItemInstanceId)) &&
           (result.OwnershipEvidenceId == null ||
            MachineRules.ValidText(result.OwnershipEvidenceId));

    private static bool SameObservations(OpeningJournal left, OpeningJournal right)
        => left.Economy == right.Economy &&
           left.Grants.Zip(right.Grants).All(pair => pair.First.Result == pair.Second.Result);
}
