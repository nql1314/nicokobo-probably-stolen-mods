using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using AugMechanicalAscension.Services;

namespace AugMechanicalAscension.Integration;

internal enum RuntimeRevisionSaveStatus
{
    Saved, StagedInMemory, FailureStagedInMemory, IdentityMismatch, WriteRejected, SaveFailed, ReadbackMismatch
}

internal sealed record RuntimeRevisionSaveResult(RuntimeRevisionSaveStatus Status,
    WriteGateStatus? WriteStatus = null, SlotReadbackStatus? ReadbackStatus = null,
    string? Error = null);

internal static class RuntimeRevisionNativeSaveAdapter
{
    internal static RuntimeRevisionSaveResult Stage(PlayerStore? store,
        SessionCoordinator sessions, RunState previous, RunState next, int nativeDay,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false)
    {
        if (!Matches(store, previous, nativeDay) ||
            !ReferenceEquals(sessions.Current?.State, previous))
            return new(RuntimeRevisionSaveStatus.IdentityMismatch);
        if (next.LastObservedDay != nativeDay || next.InitPhase != "complete")
            return new(RuntimeRevisionSaveStatus.WriteRejected, WriteGateStatus.InvalidTransition);
        var staged = ModDataWriter.StageUpdate(store!, previous, next, effects, mechanicalAscension);
        if (staged.Status != ModDataWriteStatus.StagedInMemory)
            return new(RuntimeRevisionSaveStatus.WriteRejected,
                staged.GateStatus, Error: staged.Status.ToString());
        if (next.FailureReason != "none")
            return StageFailure(sessions.AcceptRuntimeFailure(previous, next,
                nativeDay, effects, mechanicalAscension), staged.GateStatus);
        var accepted = sessions.AcceptStagedRuntimeRevision(previous, next,
            store!.modData[RunState.SaveKey], nativeDay, effects, mechanicalAscension);
        return accepted.Status == RuntimeSessionAdvanceStatus.Advanced
            ? new(RuntimeRevisionSaveStatus.StagedInMemory, staged.GateStatus)
            : new(RuntimeRevisionSaveStatus.WriteRejected, staged.GateStatus,
                Error: accepted.Status.ToString());
    }

    internal static RuntimeRevisionSaveResult Apply(PlayerStore? store,
        SessionCoordinator sessions, RunState previous, RunState next,
        int nativeDay,
        IReadOnlyDictionary<string, string>? expectedExtraModData = null,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false)
    {
        if (!Matches(store, previous, nativeDay) ||
            !ReferenceEquals(sessions.Current?.State, previous))
            return new(RuntimeRevisionSaveStatus.IdentityMismatch);
        if (expectedExtraModData != null && expectedExtraModData.Any(pair =>
                !store!.modData.ContainsKey(pair.Key) || store.modData[pair.Key] != pair.Value))
            return new(RuntimeRevisionSaveStatus.WriteRejected, Error: "Extra modData mismatch");
        return Stage(store, sessions, previous, next, nativeDay, effects, mechanicalAscension);
    }

    internal static RuntimeRevisionSaveResult ApplyDebug(PlayerStore? store,
        SessionCoordinator sessions, RunState previous, RunState next,
        int nativeDay)
    {
        if (!Matches(store, previous, nativeDay) ||
            !ReferenceEquals(sessions.Current?.State, previous))
            return new(RuntimeRevisionSaveStatus.IdentityMismatch);
        var staged = ModDataWriter.StageDebugUpdate(store!, previous, next);
        if (staged.Status != ModDataWriteStatus.StagedInMemory)
            return new(RuntimeRevisionSaveStatus.WriteRejected,
                staged.GateStatus, Error: staged.Status.ToString());
        if (next.FailureReason != "none")
            return StageFailure(sessions.AcceptRuntimeFailure(previous, next,
                nativeDay, debug: true), staged.GateStatus);
        var accepted = sessions.AcceptStagedRuntimeRevision(previous, next,
            store!.modData[RunState.SaveKey], nativeDay, debug: true);
        return accepted.Status == RuntimeSessionAdvanceStatus.Advanced
            ? new(RuntimeRevisionSaveStatus.StagedInMemory, staged.GateStatus)
            : new(RuntimeRevisionSaveStatus.WriteRejected, staged.GateStatus,
                Error: accepted.Status.ToString());
    }

    private static RuntimeRevisionSaveResult StageFailure(
        RuntimeSessionAdvanceResult accepted, WriteGateStatus? writeStatus)
        => accepted.Status == RuntimeSessionAdvanceStatus.Advanced
            ? new(RuntimeRevisionSaveStatus.FailureStagedInMemory, writeStatus)
            : new(RuntimeRevisionSaveStatus.WriteRejected, writeStatus,
                Error: accepted.Status.ToString());

    private static bool Matches(PlayerStore? store, RunState state, int nativeDay)
    {
        var current = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        var data = store?.modData;
        return store != null && current != null &&
            NativeObject.Handle(current) == NativeObject.Handle(store) &&
            data != null && data.ContainsKey(RunState.SaveKey) &&
            Guid.TryParse(store.runID, out var nativeRun) &&
            Guid.TryParse(state.GameRunId, out var expectedRun) &&
            nativeRun == expectedRun && store.saveSlotId == state.SaveSlotId &&
            StoreStation.GetDayCounter() == nativeDay;
    }
}
