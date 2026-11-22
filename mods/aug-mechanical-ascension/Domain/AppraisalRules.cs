namespace AugMechanicalAscension.Domain;

internal enum AppraisalStatus
{
    Applied, Duplicate, AlreadyHandled, AlreadyScanned, RunMismatch,
    RevisionConflict, RunNotReady, RunEnded, DayMismatch,
    NotOwned, UnstableIdentity, InvalidEvidence, UnsupportedCategory,
    OperationPending
}

internal sealed record AppraisalResult(AppraisalStatus Status, RunState State,
    AppraisalSubmissionReceipt? Receipt = null);

internal sealed record ManualAppraisalInput(string ItemInstanceId,
    string CategoryId, string RequestId, int Day, bool OwnershipVerified,
    bool IdentityStable, bool RevealedBeforeSubmission,
    string IdentityEvidenceId, string OutcomeEvidenceId,
    bool InspectionComplete = false,
    bool? NativePerfect = null, string[]? ActualFindings = null,
    string[]? SubmittedFindings = null);

// Category IDs are Mod-owned rule groups. Native item IDs and item identity
// must be supplied by a verified per-category adapter before this is used.
internal static class AppraisalRules
{
    internal const string Cigarette = "cigarette";
    internal const string FoodCoupon = "foodCoupon";
    internal const string Immunivax = "immunivax";
    internal const string PlasmaFuelCell = "plasmaFuelCell";
    internal const string BionicHand = "bionicHand";
    internal const string RibBurger = "ribBurger";
    internal const string ManualExact = "manualExact";
    internal const string NativePerfect = "nativePerfect";

    internal static bool IsCategory(string? categoryId)
        => categoryId is Cigarette or FoodCoupon or Immunivax or
            PlasmaFuelCell or BionicHand or RibBurger;

    internal static string? VerificationKind(string? categoryId)
        => categoryId switch
        {
            Cigarette or FoodCoupon or Immunivax => ManualExact,
            PlasmaFuelCell or BionicHand or RibBurger => NativePerfect,
            _ => null
        };

    internal static int PerfectCount(RunState state, string categoryId)
        => state.Appraisals.Submissions.Count(receipt =>
            receipt.CategoryId == categoryId && receipt.Perfect);

    internal static bool IsUnlocked(RunState state, string categoryId)
        => IsCategory(categoryId) &&
           (state.Appraisals.DebugAllCategoriesUnlocked ||
            state.Cybernetics.Purchased(CyberneticRules.CyberEye) &&
            PerfectCount(state, categoryId) >=
            state.ConfigSnapshot.AppraisalPerfectRequired);

    internal static AppraisalResult Submit(RunState state, string expectedRunId,
        long expectedRevision, ManualAppraisalInput input)
    {
        if (state.RunId != expectedRunId)
            return new(AppraisalStatus.RunMismatch, state);
        if (input == null || !ValidText(input.ItemInstanceId) ||
            !Guid.TryParse(input.RequestId, out var requestId) ||
            requestId == Guid.Empty || !ValidText(input.IdentityEvidenceId) ||
            !ValidText(input.OutcomeEvidenceId))
            return new(AppraisalStatus.InvalidEvidence, state);
        if (!IsCategory(input.CategoryId))
            return new(AppraisalStatus.UnsupportedCategory, state);
        var old = state.Appraisals.Submissions.FirstOrDefault(x =>
            x.ItemInstanceId == input.ItemInstanceId);
        if (old != null)
            return new(old.RequestId == input.RequestId ?
                AppraisalStatus.Duplicate : AppraisalStatus.AlreadyHandled,
                state, old);
        if (state.Appraisals.ScannedItems.Any(x =>
                x.ItemInstanceId == input.ItemInstanceId))
            return new(AppraisalStatus.AlreadyScanned, state);
        if (state.Revision != expectedRevision)
            return new(AppraisalStatus.RevisionConflict, state);
        if (state.InitPhase != "complete")
            return new(AppraisalStatus.RunNotReady, state);
        if (state.FailureReason != "none")
            return new(AppraisalStatus.RunEnded, state);
        if (state.HasPendingExternalOperation)
            return new(AppraisalStatus.OperationPending, state);
        if (input.Day != state.LastObservedDay)
            return new(AppraisalStatus.DayMismatch, state);
        if (!input.OwnershipVerified)
            return new(AppraisalStatus.NotOwned, state);
        if (!input.IdentityStable)
            return new(AppraisalStatus.UnstableIdentity, state);
        if (input.RevealedBeforeSubmission ||
            state.Appraisals.Submissions.Length >= AugNumbers.Validation.MaxAppraisalSubmissions)
            return new(AppraisalStatus.InvalidEvidence, state);

        var verificationKind = VerificationKind(input.CategoryId)!;
        bool perfect;
        if (verificationKind == NativePerfect)
        {
            if (input.NativePerfect == null || input.ActualFindings != null ||
                input.SubmittedFindings != null)
                return new(AppraisalStatus.InvalidEvidence, state);
            perfect = input.NativePerfect.Value;
        }
        else
        {
            if (!input.InspectionComplete || input.NativePerfect != null ||
                !ValidFindings(input.ActualFindings) ||
                !ValidFindings(input.SubmittedFindings))
                return new(AppraisalStatus.InvalidEvidence, state);
            perfect = input.ActualFindings!.ToHashSet(StringComparer.Ordinal)
                .SetEquals(input.SubmittedFindings!);
        }

        var receipt = new AppraisalSubmissionReceipt(input.ItemInstanceId,
            input.CategoryId, input.RequestId, input.Day, perfect,
            verificationKind, input.OutcomeEvidenceId,
            input.IdentityEvidenceId);
        var updated = state with
        {
            Revision = checked(state.Revision + 1),
            Appraisals = state.Appraisals with
            { Submissions = [.. state.Appraisals.Submissions, receipt] }
        };
        return new(AppraisalStatus.Applied, updated, receipt);
    }

    internal static bool ValidText(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= AugNumbers.Validation.MaxIdentifierChars;

    internal static bool ValidFindings(string[]? findings)
        => findings != null && findings.Length <= AugNumbers.Validation.MaxFindings &&
           findings.All(ValidText) &&
           findings.Distinct(StringComparer.Ordinal).Count() == findings.Length;
}
