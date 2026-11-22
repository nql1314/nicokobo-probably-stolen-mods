using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Persistence;

internal enum InitialSavePlanStatus
{
    Ready, IntentMismatch, InvalidNewRun, WriteRejected
}

internal sealed record InitialSavePlanResult(InitialSavePlanStatus Status,
    BoundStart? Binding = null, RunState? State = null, WritePlan? Write = null,
    WriteGateStatus? GateStatus = null);

// Called only after InitialSave assigned native slot/run IDs and before its
// SaveGame call serializes PlayerStore. This plans an in-memory write; the
// native adapter must recheck identity and dictionary contents at mutation time.
internal static class InitialSavePlanner
{
    internal static InitialSavePlanResult Prepare(StartIntentCoordinator intents,
        PendingStart reservation, string? nativeRunId, int nativeSlotId, int nativeDay,
        string buildProfileId, ConfigSnapshot config, bool keyPresent, string? existingJson)
    {
        var binding = intents.Bind(reservation, nativeRunId, nativeSlotId);
        if (binding == null)
        {
            intents.Invalidate();
            return new(InitialSavePlanStatus.IntentMismatch);
        }

        try
        {
            var state = RunStateCodec.CreateNew(buildProfileId, binding.GameRunId,
                binding.SaveSlotId, nativeDay, config);
            var gate = ModDataWriteGate.New(keyPresent, existingJson, binding,
                state, nativeRunId!, nativeSlotId);
            return gate.Status == WriteGateStatus.Ready && gate.Plan != null
                ? new(InitialSavePlanStatus.Ready, binding, state, gate.Plan, gate.Status)
                : new(InitialSavePlanStatus.WriteRejected, GateStatus: gate.Status);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            return new(InitialSavePlanStatus.InvalidNewRun);
        }
    }

}
