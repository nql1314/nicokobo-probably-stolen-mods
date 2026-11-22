using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using AugMechanicalAscension.Services;

namespace AugMechanicalAscension.Integration;

// Opening grants and their journal stay in memory until the native night save.
internal static class OpeningRevisionNativeSaveAdapter
{
    internal static OpeningRevisionSaveResult Apply(PlayerStore? store,
        SessionCoordinator sessions, RunState previous, RunState next)
    {
        if (store == null || NativeObject.Handle((PlayerStore.HasInstance ? PlayerStore.Instance : null)) != NativeObject.Handle(store) ||
            store.runID != previous.GameRunId || store.saveSlotId != previous.SaveSlotId ||
            StoreStation.GetDayCounter() != previous.StartDay)
            return new(OpeningRevisionSaveStatus.IdentityMismatch);
        if (!ReferenceEquals(sessions.Current?.State, previous))
            return new(OpeningRevisionSaveStatus.NoCurrentSession);
        var staged = ModDataWriter.StageUpdate(store, previous, next);
        if (staged.Status != ModDataWriteStatus.StagedInMemory)
            return new(OpeningRevisionSaveStatus.StageFailed, staged.Status);
        var accepted = sessions.AcceptStagedOpeningRevision(previous, next,
            store.modData[RunState.SaveKey]);
        if (accepted.Status == OpeningSessionAdvanceStatus.Advanced)
            return new(OpeningRevisionSaveStatus.Advanced, staged.Status);
        sessions.ClearIfCurrent(previous);
        return new(OpeningRevisionSaveStatus.NoCurrentSession, staged.Status,
            Error: accepted.Status.ToString());
    }
}
