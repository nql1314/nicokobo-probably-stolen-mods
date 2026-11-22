using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Persistence;

// Rebinding changes native addressing only. Mod run ID, revision, requests,
// item UIDs and completed receipts remain historical facts, never new grants.
internal static class RunForkMigration
{
    internal static string Run(string json, string sourceRun, int sourceSlot, string destinationRun, int destinationSlot)
    {
        var read = RunStateCodec.ReadForGame(json, sourceRun, sourceSlot);
        if (read.Status != ReadStatus.Ready || read.State is not { } state ||
            state.InitPhase != "complete" || state.HasPendingExternalOperation || state.FailureReason != "none")
            throw new InvalidDataException("Aug fork requires a complete, playable state without an unfinished transaction");
        var next = state with
        {
            GameRunId = destinationRun, SaveSlotId = destinationSlot,
            Opening = state.Opening == null ? null : state.Opening with
            { GameRunId = destinationRun, SaveSlotId = destinationSlot }
        };
        return RunStateCodec.Write(next);
    }

    internal static string Story(string json, string sourceRun, int sourceSlot, string destinationRun, int destinationSlot)
    {
        var state = StoryStateCodec.Read(json, sourceRun, sourceSlot);
        if (state.Pending != null) throw new InvalidDataException("Story transaction is unresolved");
        string next = StoryStateCodec.Write(state with { GameRunId = destinationRun, SaveSlotId = destinationSlot });
        StoryStateCodec.Read(next, destinationRun, destinationSlot);
        return next;
    }

    internal static string Supply(string json, string sourceRun, int sourceSlot, string destinationRun, int destinationSlot)
    {
        var state = GraylineSupplyCodec.Read(json, sourceRun, sourceSlot);
        if (state.Pending != null) throw new InvalidDataException("Supply transaction is unresolved");
        string next = GraylineSupplyCodec.Write(state with { GameRunId = destinationRun, SaveSlotId = destinationSlot });
        GraylineSupplyCodec.Read(next, destinationRun, destinationSlot);
        return next;
    }

    internal static string Workshop(string json, string sourceRun, int sourceSlot, string destinationRun, int destinationSlot)
    {
        var state = WorkshopAchievements.Decode(json, sourceRun, sourceSlot);
        if (state.Pending != null) throw new InvalidDataException("Aug workshop reward delivery is unresolved");
        return WorkshopAchievements.Encode(state with { RunId = destinationRun, SlotId = destinationSlot });
    }
}
