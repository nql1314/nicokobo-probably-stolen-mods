using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using AugMechanicalAscension.Services;

namespace AugMechanicalAscension.Integration;

internal enum InitialStateStageStatus
{
    Staged, Unavailable, PlanRejected, StageFailed, IdentityChanged
}

internal sealed record InitialStateStageResult(InitialStateStageStatus Status,
    InitialStateStagedAttempt? Attempt = null,
    InitialSavePlanStatus? PlanStatus = null,
    ModDataWriteStatus? WriteStatus = null,
    string? Error = null);

internal enum InitialStateCompleteStatus
{
    Adopted, InvalidAttempt, IdentityChanged, ReadbackRejected
}

internal sealed record InitialStateCompleteResult(InitialStateCompleteStatus Status,
    InitialSessionAdoptionStatus? AdoptionStatus = null,
    SlotReadbackStatus? ReadbackStatus = null,
    string? Error = null);

internal sealed class InitialStateStagedAttempt
{
    private int _completed;

    internal InitialStateStagedAttempt(InitialSavePlanResult plan,
        IntPtr storePointer, int threadId)
    {
        Plan = plan;
        StorePointer = storePointer;
        ThreadId = threadId;
    }

    internal InitialSavePlanResult Plan { get; }
    internal IntPtr StorePointer { get; }
    internal int ThreadId { get; }
    internal bool TryCompleteOnce()
        => Interlocked.Exchange(ref _completed, 1) == 0;
}

// Installed hook for the narrow window between InitialSave assigning native IDs
// and its SaveGame serialization; PlayableStartFlow drives it from the
// InitialSave prefix/postfix and the nested SaveGame prefix. Stage never calls
// SaveGame; the native InitialSave continuation owns that call. NextFreeSlotId
// has already advanced the index generator, and InitialSave has saved run count
// and active slot before this window; skipping SaveGame alone is not a complete
// abort. A verified pre-entry abort or a narrowly owned cleanup path is still
// required before this start can be exposed as a release build.
internal static class InitialStateNativeSaveAdapter
{
    internal static InitialStateStageResult Stage(PlayerStore? store,
        StartIntentCoordinator intents, PendingStart reservation,
        ConfigSnapshot config)
    {
        if (store == null || NativeObject.Handle(store) == IntPtr.Zero ||
            !StartDefinition.MatchesNativeStartType((int)store.startType))
            return new(InitialStateStageStatus.Unavailable);
        try
        {
            var data = store.modData;
            if (data == null) return new(InitialStateStageStatus.Unavailable);
            bool present = data.ContainsKey(RunState.SaveKey);
            string? existing = present ? data[RunState.SaveKey] : null;
            var prepared = InitialSavePlanner.Prepare(intents, reservation,
                store.runID, store.saveSlotId, StoreStation.GetDayCounter(),
                BuildProfile.ProfileId, config, present, existing);
            if (prepared.Status != InitialSavePlanStatus.Ready ||
                prepared.Binding == null || prepared.State == null ||
                prepared.Write == null)
                return new(InitialStateStageStatus.PlanRejected,
                    PlanStatus: prepared.Status);
            var staged = ModDataWriter.StagePreparedNew(store, prepared);
            if (staged.Status != ModDataWriteStatus.StagedInMemory)
                return new(InitialStateStageStatus.StageFailed,
                    PlanStatus: prepared.Status, WriteStatus: staged.Status);
            if (!Matches(store, prepared, NativeObject.Handle(store),
                    Environment.CurrentManagedThreadId))
                return new(InitialStateStageStatus.IdentityChanged,
                    PlanStatus: prepared.Status, WriteStatus: staged.Status);
            return new(InitialStateStageStatus.Staged,
                new InitialStateStagedAttempt(prepared, NativeObject.Handle(store),
                    Environment.CurrentManagedThreadId), prepared.Status,
                staged.Status);
        }
        catch (Exception ex)
        {
            return new(InitialStateStageStatus.StageFailed,
                Error: ex.GetType().Name);
        }
    }

    // Call only after the same native InitialSave invocation returns without
    // an escaping exception. The attempt is consumed even when readback fails.
    internal static InitialStateCompleteResult Complete(PlayerStore? store,
        InitialStateStagedAttempt? attempt, SessionCoordinator sessions)
    {
        if (attempt == null || !attempt.TryCompleteOnce() || store == null ||
            !StartDefinition.MatchesNativeStartType((int)store.startType))
            return new(InitialStateCompleteStatus.InvalidAttempt);
        try
        {
            if (!Matches(store, attempt.Plan, attempt.StorePointer,
                    attempt.ThreadId))
                return new(InitialStateCompleteStatus.IdentityChanged);
            var adoption = sessions.AdoptInitialState(attempt.Plan);
            return adoption.Status == InitialSessionAdoptionStatus.Adopted
                ? new(InitialStateCompleteStatus.Adopted, adoption.Status,
                    adoption.ReadbackStatus)
                : new(InitialStateCompleteStatus.ReadbackRejected,
                    adoption.Status, adoption.ReadbackStatus);
        }
        catch (Exception ex)
        {
            return new(InitialStateCompleteStatus.ReadbackRejected,
                Error: ex.GetType().Name);
        }
    }

    private static bool Matches(PlayerStore store,
        InitialSavePlanResult prepared, IntPtr expectedPointer,
        int expectedThread)
    {
        if (prepared.Binding == null || prepared.State == null ||
            prepared.Write == null || NativeObject.Handle(store) != expectedPointer ||
            Environment.CurrentManagedThreadId != expectedThread ||
            store.saveSlotId != prepared.Binding.SaveSlotId ||
            !StartDefinition.MatchesNativeStartType((int)store.startType) ||
            !Guid.TryParse(store.runID, out var nativeRun) ||
            !Guid.TryParse(prepared.Binding.GameRunId, out var boundRun) ||
            nativeRun != boundRun ||
            StoreStation.GetDayCounter() != prepared.State.StartDay)
            return false;
        var data = store.modData;
        return data != null && data.ContainsKey(RunState.SaveKey) &&
            data[RunState.SaveKey] == prepared.Write.NextJson;
    }
}
