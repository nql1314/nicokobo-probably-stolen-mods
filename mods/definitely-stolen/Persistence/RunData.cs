using DefinitelyStolen.Domain;
using Newtonsoft.Json.Linq;

namespace DefinitelyStolen.Persistence;

internal static class RunData
{
    internal const string Key = "state";
    internal static string FullKey => Identity.Owner + ":" + Key;
    internal static RunState Read(PlayerStore store, RunState fresh, Catalog catalog, Action<string> warn)
    {
        var data = store.ModData();
        data.TryGetValue(FullKey, out var current); data.TryGetValue(Identity.SaveKey, out var legacy);
        foreach (string? json in new[] { current, legacy }.Distinct(StringComparer.Ordinal))
        {
            if (json == null) continue;
            JObject? payload = null;
            try
            {
                var parsed = JObject.Parse(json);
                if (parsed.Value<string>(nameof(RunState.RunId)) != store.runID)
                {
                    warn("Ignored theft data from a different run");
                    continue;
                }
                if ((parsed.Value<int?>(nameof(RunState.Schema)) ?? 5) is < 1 or > 5)
                {
                    warn("Ignored unsupported theft save version");
                    continue;
                }
                payload = parsed;
                var state = StateCodec.Decode(json, store.runID, parsed.Value<int>(nameof(RunState.SlotId)), catalog);
                state.SlotId = store.saveSlotId;
                return state;
            }
            catch (Exception ex)
            {
                warn("Recovering theft data: " + ex.Message);
                if (payload != null)
                {
                    try { return RunRecovery.Decode(payload, fresh, catalog, warn); }
                    catch (Exception recovery) { warn("Could not recover theft data: " + recovery.Message); }
                }
            }
        }
        return fresh;
    }
    // Called only from PlayerStore.SaveGame's prefix. It stages memory; the
    // game's normal save is solely responsible for writing this state to disk.
    internal static void Stage(PlayerStore store, RunState next)
    {
        if (!ReferenceEquals(store, PlayerStore.Instance) || store.runID != next.RunId || store.saveSlotId != next.SlotId || string.IsNullOrWhiteSpace(next.RunId) || next.SlotId < 0)
            throw new InvalidOperationException("Native run identity changed before staging theft state");
        var data = store.ModData();
        data[FullKey] = StateCodec.Encode(next);
        data.Remove(Identity.SaveKey);
    }
}
