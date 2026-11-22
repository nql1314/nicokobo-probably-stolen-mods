using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

internal static class RunForkMigrationChecks
{
    internal static void Run(RunState source, Action<bool, string> check)
    {
        string destination = Guid.NewGuid().ToString("D");
        int slot = source.SaveSlotId + 1;
        string before = RunStateCodec.Write(source);
        string migrated = RunForkMigration.Run(before, source.GameRunId, source.SaveSlotId, destination, slot);
        var restored = RunStateCodec.ReadForGame(migrated, destination, slot);
        check(restored.Status == ReadStatus.Ready && restored.State!.RunId == source.RunId &&
            restored.State.Revision == source.Revision && restored.State.Opening!.Phase == OpeningJournalRules.Committed,
            "Fork preserves the logical Aug run, revision and completed opening");
        check(RunForkMigration.Run(migrated, destination, slot, source.GameRunId, source.SaveSlotId) == before,
            "Fork changes only native addressing, preserving all receipts, item identities and configuration");
        check(RunStateCodec.ReadForGame(migrated, source.GameRunId, source.SaveSlotId).Status == ReadStatus.RunMismatch,
            "Fork migration did not disable ordinary cross-save identity checks");
        bool rejected = false;
        try { RunForkMigration.Run(before, Guid.NewGuid().ToString("D"), source.SaveSlotId, destination, slot); }
        catch (Exception ex) when(ex is InvalidDataException or AugMechanicalAscension.Serialization.JsonException) { rejected = true; }
        check(rejected, "Unverified Aug source was rebound");
        rejected = false;
        try { RunForkMigration.Run(RunStateCodec.Write(RunStateCodec.CreateNew(source.BuildProfileId, source.GameRunId,
                source.SaveSlotId, source.StartDay, source.ConfigSnapshot)),
            source.GameRunId, source.SaveSlotId, destination, slot); }
        catch (Exception ex) when(ex is InvalidDataException or AugMechanicalAscension.Serialization.JsonException) { rejected = true; }
        check(rejected, "An unfinished opening was forked");

        var story = new StoryState { GameRunId = source.GameRunId, SaveSlotId = source.SaveSlotId, Revision = 4 };
        string storyJson = StoryStateCodec.Write(story);
        string nextStory = RunForkMigration.Story(storyJson, source.GameRunId, source.SaveSlotId, destination, slot);
        check(StoryStateCodec.Read(nextStory, destination, slot).Revision == 4 &&
            RunForkMigration.Story(nextStory, destination, slot, source.GameRunId, source.SaveSlotId) == storyJson,
            "Story fork lost its revision or historical facts");
        var supply = new GraylineSupplyState { GameRunId = source.GameRunId, SaveSlotId = source.SaveSlotId, Revision = 3 };
        string supplyJson = GraylineSupplyCodec.Write(supply);
        string nextSupply = RunForkMigration.Supply(supplyJson, source.GameRunId, source.SaveSlotId, destination, slot);
        check(GraylineSupplyCodec.Read(nextSupply, destination, slot).Revision == 3 &&
            RunForkMigration.Supply(nextSupply, destination, slot, source.GameRunId, source.SaveSlotId) == supplyJson,
            "Supply fork changed stock, appointment or receipts");
        var workshop = new WorkshopAchievementState { RunId = source.GameRunId, SlotId = source.SaveSlotId, DebugAscensionUnlocked = true };
        string workshopJson = WorkshopAchievements.Encode(workshop);
        string nextWorkshop = RunForkMigration.Workshop(workshopJson, source.GameRunId, source.SaveSlotId, destination, slot);
        check(WorkshopAchievements.Decode(nextWorkshop, destination, slot).DebugAscensionUnlocked &&
            RunForkMigration.Workshop(nextWorkshop, destination, slot, source.GameRunId, source.SaveSlotId) == workshopJson,
            "Workshop fork changed completed rewards or ascension state");
    }
}
