using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Persistence;

internal sealed record OpeningItemSnapshot(string Kind, string ItemId, int Units,
    int OwnedBefore, bool BlueprintVerified, bool PlayerDestinationVerified);

// Current native identity and inventory supplied by the opening adapter.
// InitialStateReady refers to loaded/staged modData, without file readback.
internal sealed record OpeningNativeSnapshot(string GameRunId, int SaveSlotId,
    int Day, bool NativeIdentityVerified, bool InitialStateReady,
    OpeningItemSnapshot[] Items);

internal enum OpeningSavePlanStatus
{
    Ready, StateNotPending, IdentityMismatch, UnverifiedNative,
    InvalidManifest, InvalidState, WriteRejected
}

internal sealed record OpeningSavePlanResult(OpeningSavePlanStatus Status,
    RunState? Previous = null, RunState? State = null, WritePlan? Write = null,
    WriteGateStatus? GateStatus = null);

// Prepare an exact write plan for the first journal revision. No native cash,
// inventory, modData, or disk mutation occurs here. The caller stages the
// Prepared state before granting and lets the game save it with inventory.
internal static class OpeningSavePlanner
{
    internal static OpeningSavePlanResult Prepare(RunState? previous,
        OpeningNativeSnapshot? native, bool keyPresent, string? existingJson)
    {
        if (previous == null || native == null || native.Items == null)
            return new(OpeningSavePlanStatus.InvalidState);
        try { RunStateCodec.ValidateState(previous); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { return new(OpeningSavePlanStatus.InvalidState); }

        if (previous.Revision != 0 || previous.InitPhase != "pending" ||
            previous.Opening != null || previous.EyeToolInstanceId != null ||
            previous.LastObservedDay != previous.StartDay ||
            previous.LastSettledDay != previous.StartDay - 1 ||
            previous.FailureReason != "none" || previous.HasPendingExternalOperation)
            return new(OpeningSavePlanStatus.StateNotPending);
        if (!Guid.TryParse(native.GameRunId, out var observedRun) ||
            !Guid.TryParse(previous.GameRunId, out var expectedRun) ||
            observedRun != expectedRun || native.SaveSlotId != previous.SaveSlotId ||
            native.Day != previous.StartDay)
            return new(OpeningSavePlanStatus.IdentityMismatch);
        if (!native.NativeIdentityVerified || !native.InitialStateReady ||
            native.Items.Any(x => x == null || !x.BlueprintVerified ||
                !x.PlayerDestinationVerified))
            return new(OpeningSavePlanStatus.UnverifiedNative);

        string operationId = Guid.NewGuid().ToString("D");
        var grants = native.Items.Select(x => new OpeningGrantRecord(x.Kind,
            new OpeningGrantRequest(operationId, x.ItemId, x.Units, x.OwnedBefore))).ToArray();
        var next = previous with
        {
            Revision = 1,
            InitPhase = "applying",
            Opening = new OpeningJournal(operationId, previous.GameRunId,
                previous.SaveSlotId, previous.StartDay, OpeningJournalRules.Prepared,
                grants)
        };
        if (!OpeningJournalRules.Valid(next) ||
            !OpeningJournalRules.Transition(previous, next))
            return new(OpeningSavePlanStatus.InvalidManifest);

        var gate = ModDataWriteGate.Update(keyPresent, existingJson, previous,
            next, native.GameRunId, native.SaveSlotId);
        return gate.Status == WriteGateStatus.Ready && gate.Plan != null
            ? new(OpeningSavePlanStatus.Ready, previous, next, gate.Plan, gate.Status)
            : new(OpeningSavePlanStatus.WriteRejected, GateStatus: gate.Status);
    }
}
