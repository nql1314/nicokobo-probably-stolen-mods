using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Persistence;

// Accept an existing store from a verified game callback. Accessing PlayerStore.Instance
// on the main menu can create a temporary store and is intentionally avoided here.
internal static class ModDataReader
{
    internal const string StateKey = RunState.SaveKey;

    internal static ReadResult Read(PlayerStore? store)
    {
        if (store == null)
            return new(ReadStatus.Unavailable, null, "PlayerStore is not available");

        try
        {
            var data = store.modData;
            if (data == null)
                return new(ReadStatus.Unavailable, null, "PlayerStore.modData is not available");

            if (!data.ContainsKey(StateKey))
                return new(ReadStatus.Missing, null, null);
            var json = data[StateKey];
            if (json == null)
                return new(ReadStatus.Corrupt, null, "Mod state key contains a null value");
            return RunStateCodec.ReadForGame(json, store.runID, store.saveSlotId);
        }
        catch (Exception ex)
        {
            return new(ReadStatus.Unavailable, null, $"Unable to read PlayerStore.modData: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
