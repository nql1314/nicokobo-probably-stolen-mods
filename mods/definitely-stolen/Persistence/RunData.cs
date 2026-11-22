using DefinitelyStolen.Domain;
using Newtonsoft.Json.Linq;

namespace DefinitelyStolen.Persistence;

internal enum RunReadStatus { Missing, Present, Conflict }
internal sealed record RunRead(RunReadStatus Status, string? Json = null);

internal static class RunData
{
    internal const string Key = "state";
    internal static string FullKey => Identity.Owner + ":" + Key;
    internal static RunRead Read(PlayerStore store)
    {
        var data = store.ModData();
        data.TryGetValue(FullKey, out var current); data.TryGetValue(Identity.SaveKey, out var legacy);
        if (current != null && legacy != null && current != legacy) return new(RunReadStatus.Conflict);
        return current != null || legacy != null ? new(RunReadStatus.Present, current ?? legacy) : new(RunReadStatus.Missing);
    }
    internal static RunState DecodeLoaded(string json, PlayerStore store, Catalog catalog)
    {
        var identity = JObject.Parse(json);
        string sourceRun = identity.Value<string>(nameof(RunState.RunId)) ?? "";
        int sourceSlot = identity.Value<int>(nameof(RunState.SlotId));
        // Native DuplicateSlot copies the run ID and payload verbatim, then
        // LoadGame selects the destination slot. Only that native copy case is rebound.
        if (sourceRun != store.runID) throw new InvalidDataException("Theft payload belongs to a different run");
        var state = StateCodec.Decode(json, sourceRun, sourceSlot, catalog);
        if (sourceSlot != store.saveSlotId)
        {
            if (NewGameData.Instance == null || NewGameData.Instance.saveSlot != store.saveSlotId || store.saveSlotId < 0)
                throw new InvalidDataException("Theft payload has no verified native destination slot");
            state = StateCodec.Decode(StateCodec.RebindFork(json, sourceRun, sourceSlot, store.runID, store.saveSlotId, catalog), store.runID, store.saveSlotId, catalog);
        }
        return state;
    }
    // Called only from PlayerStore.SaveGame's prefix. It stages memory; the
    // game's normal save is solely responsible for writing this state to disk.
    internal static string Stage(PlayerStore store, string? expected, RunState next)
    {
        if (!ReferenceEquals(store, PlayerStore.Instance) || store.runID != next.RunId || store.saveSlotId != next.SlotId || string.IsNullOrWhiteSpace(next.RunId) || next.SlotId < 0)
            throw new InvalidOperationException("Native run identity changed before staging theft state");
        var data = store.ModData();
        data.TryGetValue(FullKey, out var current); data.TryGetValue(Identity.SaveKey, out var legacy);
        if ((current ?? legacy) != expected || current != null && legacy != null && current != legacy)
            throw new InvalidOperationException("Theft state changed outside this revision");
        string json = StateCodec.Encode(next);
        ModHelper.SetModData(Identity.Owner, Key, json);
        if (ModHelper.GetModData(Identity.Owner, Key) != json) throw new InvalidOperationException("Native theft state staging failed");
        if (legacy != null) data.Remove(Identity.SaveKey);
        return json;
    }
}
