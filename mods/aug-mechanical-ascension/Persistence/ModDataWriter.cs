using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Persistence;

// Only stage a validated JSON value in the existing PlayerStore.modData dictionary.
// A verified game callback must explicitly call this on the main thread; this class never calls SaveGame.
internal static class ModDataWriter
{
    internal static ModDataWriteResult StagePreparedNew(PlayerStore? store,
        InitialSavePlanResult prepared)
        => Stage(store, (present, existing, runId, slotId) =>
            ModDataWriteGate.RecheckPreparedNew(prepared, present, existing, runId, slotId));

    internal static ModDataWriteResult StageUpdate(PlayerStore? store, RunState previous, RunState next,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false)
        => Stage(store, (present, existing, runId, slotId) =>
            ModDataWriteGate.Update(present, existing, previous, next, runId, slotId,
                effects, mechanicalAscension));

    internal static ModDataWriteResult StageDebugUpdate(PlayerStore? store,
        RunState previous, RunState next)
        => Stage(store, (present, existing, runId, slotId) =>
            ModDataWriteGate.DebugUpdate(present, existing, previous, next,
                runId, slotId));

    private static ModDataWriteResult Stage(PlayerStore? store,
        Func<bool, string?, string, int, WriteGateResult> preflight)
    {
        if (store == null) return new(ModDataWriteStatus.Unavailable);
        bool writeAttempted = false;
        try
        {
            var data = store.modData;
            if (data == null) return new(ModDataWriteStatus.Unavailable);
            string runId = store.runID;
            int slotId = store.saveSlotId;
            bool present = data.ContainsKey(RunState.SaveKey);
            string? existing = present ? data[RunState.SaveKey] : null;
            var gate = preflight(present, existing, runId, slotId);
            if (gate.Status != WriteGateStatus.Ready || gate.Plan == null)
                return new(ModDataWriteStatus.Rejected, gate.Status);

            var plan = gate.Plan;
            bool stillPresent = data.ContainsKey(RunState.SaveKey);
            if (store.runID != plan.GameRunId || store.saveSlotId != plan.SaveSlotId ||
                stillPresent != (plan.ExpectedJson != null) ||
                (stillPresent && data[RunState.SaveKey] != plan.ExpectedJson))
                return new(ModDataWriteStatus.Rejected, WriteGateStatus.ContentConflict);

            writeAttempted = true;
            if (plan.ExpectedJson == null) data.Add(RunState.SaveKey, plan.NextJson);
            else data[RunState.SaveKey] = plan.NextJson;
            return data.ContainsKey(RunState.SaveKey) && data[RunState.SaveKey] == plan.NextJson
                ? new(ModDataWriteStatus.StagedInMemory)
                : new(ModDataWriteStatus.Indeterminate);
        }
        catch
        {
            return new(writeAttempted ? ModDataWriteStatus.Indeterminate : ModDataWriteStatus.Unavailable);
        }
    }
}
