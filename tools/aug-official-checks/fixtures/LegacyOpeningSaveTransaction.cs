using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

namespace AugMechanicalAscension.Services;

internal static class OpeningRevisionSaveTransaction
{
    internal static OpeningRevisionSaveResult Apply(SessionCoordinator sessions,
        IOpeningRevisionSavePort port, RunState previous, RunState next)
    {
        if (sessions.Current is not { } view ||
            !ReferenceEquals(view.State, previous))
            return new(OpeningRevisionSaveStatus.NoCurrentSession);
        try
        {
            if (ModDataWriteGate.Update(true, RunStateCodec.Write(previous),
                    previous, next, previous.GameRunId, previous.SaveSlotId).Status !=
                WriteGateStatus.Ready)
                return new(OpeningRevisionSaveStatus.InvalidRevision);
        }
        catch (Exception ex)
        {
            return new(OpeningRevisionSaveStatus.InvalidRevision,
                Error: ex.GetType().Name);
        }

        if (!Matches(port, previous, out var matchError))
        {
            sessions.ClearIfCurrent(previous);
            return new(OpeningRevisionSaveStatus.IdentityMismatch,
                Error: matchError);
        }

        string? saveDirectory;
        try
        {
            saveDirectory = port.SaveDirectory;
            var baseline = Es3SlotReadback.Compare(saveDirectory,
                previous.SaveSlotId, previous.GameRunId,
                RunStateCodec.Write(previous),
                new NativeSaveSnapshot(Day: previous.StartDay,
                    StartType: StartDefinition.NativeStartTypeId));
            if (baseline.Status != SlotReadbackStatus.StateMatched)
            {
                sessions.ClearIfCurrent(previous);
                return new(OpeningRevisionSaveStatus.BaselineMismatch,
                    ReadbackStatus: baseline.Status);
            }
        }
        catch (Exception ex)
        {
            sessions.ClearIfCurrent(previous);
            return new(OpeningRevisionSaveStatus.BaselineMismatch,
                Error: ex.GetType().Name);
        }

        ModDataWriteResult staged;
        try { staged = port.Stage(previous, next); }
        catch (Exception ex)
        {
            sessions.ClearIfCurrent(previous);
            return new(OpeningRevisionSaveStatus.StageFailed,
                ModDataWriteStatus.Indeterminate, Error: ex.GetType().Name);
        }
        if (staged.Status != ModDataWriteStatus.StagedInMemory)
        {
            sessions.ClearIfCurrent(previous);
            return new(OpeningRevisionSaveStatus.StageFailed, staged.Status);
        }

        if (!Matches(port, next, out matchError))
        {
            sessions.ClearIfCurrent(previous);
            return new(OpeningRevisionSaveStatus.IdentityMismatch,
                ModDataWriteStatus.StagedInMemory, Error: matchError);
        }
        try { port.SaveOnce(); }
        catch (Exception ex)
        {
            sessions.ClearIfCurrent(previous);
            return new(OpeningRevisionSaveStatus.SaveFailed,
                ModDataWriteStatus.StagedInMemory, Error: ex.GetType().Name);
        }
        if (!Matches(port, next, out matchError))
        {
            sessions.ClearIfCurrent(previous);
            return new(OpeningRevisionSaveStatus.IdentityMismatch,
                ModDataWriteStatus.StagedInMemory, Error: matchError);
        }
        try
        {
            var advanced = sessions.AcceptSavedOpeningRevision(previous, next,
                saveDirectory);
            if (advanced.Status == OpeningSessionAdvanceStatus.Advanced)
                return new(OpeningRevisionSaveStatus.Advanced,
                    ModDataWriteStatus.StagedInMemory, advanced.ReadbackStatus);
            sessions.ClearIfCurrent(previous);
            return new(advanced.Status == OpeningSessionAdvanceStatus.ReadbackMismatch
                ? OpeningRevisionSaveStatus.ReadbackFailed :
                OpeningRevisionSaveStatus.NoCurrentSession,
                ModDataWriteStatus.StagedInMemory, advanced.ReadbackStatus);
        }
        catch (Exception ex)
        {
            sessions.ClearIfCurrent(previous);
            return new(OpeningRevisionSaveStatus.ReadbackFailed,
                ModDataWriteStatus.StagedInMemory, Error: ex.GetType().Name);
        }
    }

    private static bool Matches(IOpeningRevisionSavePort port, RunState state,
        out string? error)
    {
        try
        {
            error = null;
            return port.Matches(state);
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            return false;
        }
    }
}
