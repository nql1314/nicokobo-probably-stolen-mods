using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

namespace AugMechanicalAscension.Services;

internal enum OpeningRevisionSaveStatus
{
    Advanced, NoCurrentSession, InvalidRevision, IdentityMismatch,
    BaselineMismatch, StageFailed, SaveFailed, ReadbackFailed
}

internal sealed record OpeningRevisionSaveResult(OpeningRevisionSaveStatus Status,
    ModDataWriteStatus? StageStatus = null,
    SlotReadbackStatus? ReadbackStatus = null,
    string? Error = null);

// The port must operate on one verified native PlayerStore on the game thread.
// A SaveGame return alone is never treated as persistence evidence.
internal interface IOpeningRevisionSavePort
{
    bool Matches(RunState state);
    ModDataWriteResult Stage(RunState previous, RunState next);
    void SaveOnce();
    string? SaveDirectory { get; }
}

// Sequencing for each opening revision save, invoked through
// OpeningRevisionNativeSaveAdapter from OpeningRuntimeFlow. Ambiguous staging,
// saving, or readback revokes the current in-memory session; a later load must
// reconcile the native slot.
