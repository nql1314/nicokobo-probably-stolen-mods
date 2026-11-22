namespace AugMechanicalAscension.Persistence;

internal enum ModDataWriteStatus { StagedInMemory, Rejected, Unavailable, Indeterminate }
internal sealed record ModDataWriteResult(ModDataWriteStatus Status,
    WriteGateStatus? GateStatus = null);
