using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;

internal static class OfficialPersistenceChecks
{
    internal static void Run(Catalog catalog, Func<int, ulong, RunState> create, Action<bool, string> check, Action<Action, string> reject)
    {
        var state = create(2, 98765); state.OpeningGranted = true; state.OpeningCash = 500;
        var configured = StateCodec.Clone(state); configured.QualificationSource = "config";
        var configuredLoaded = StateCodec.Decode(StateCodec.Encode(configured), configured.RunId, configured.SlotId, catalog);
        check(StateCodec.Encode(configuredLoaded) == StateCodec.Encode(configured),
            "Configuration-based access survives normal serialization without changing progress, targets or opening receipts");
        configured.QualificationSource = "unknown";
        reject(() => StateCodec.Decode(StateCodec.Encode(configured), configured.RunId, configured.SlotId, catalog),
            "Adding configuration access does not accept unknown qualification sources");
        var store = new PlayerStore { runID = state.RunId, saveSlotId = state.SlotId }; PlayerStore.Instance = store;
        check(RunData.Read(store).Status == RunReadStatus.Missing, "An unrelated existing save has no invented theft eligibility");
        string json = StateCodec.Encode(state); store.modData[Identity.SaveKey] = json;
        check(RunData.Read(store).Json == json, "Legacy owner.state is read without adding the official prefix twice");
        string staged = RunData.Stage(store, json, state);
        check(store.modData[RunData.FullKey] == staged && !store.modData.ContainsKey(Identity.SaveKey), "Normal-save staging moves legacy data to owner:state once");
        reject(() => RunData.Stage(store, "stale state", state), "Concurrent foreign edits cannot be overwritten by theft staging");
        store.modData[Identity.SaveKey] = "different legacy data";
        check(RunData.Read(store).Status == RunReadStatus.Conflict, "Conflicting legacy and official state is rejected rather than reset");
        reject(() => RunData.Stage(store, staged, state), "Conflicting keys cannot be silently consolidated");
        store.modData.Remove(Identity.SaveKey);
        store.saveSlotId = 9; NewGameData.Instance = null;
        reject(() => RunData.DecodeLoaded(staged, store, catalog), "A changed slot requires the native selected destination");
        NewGameData.Instance = new NewGameData { saveSlot = 9 };
        var rebound = RunData.DecodeLoaded(staged, store, catalog);
        check(rebound.RunId == state.RunId && rebound.SlotId == 9 && rebound.OpeningGranted && rebound.OpeningCash == 500,
            "Native copied slots retain opening receipts while rebinding only slot identity");
        check(rebound.RandomState == state.RandomState && rebound.Night!.RandomState == state.Night!.RandomState && rebound.Night.Npcs.SelectMany(x => x.Items).Select(x => x.UniqueId)
            .SequenceEqual(state.Night.Npcs.SelectMany(x => x.Items).Select(x => x.UniqueId)), "Rebinding cannot reroll NPCs, loot identity or random sequence");
        check(StateCodec.Encode(RunData.DecodeLoaded(StateCodec.Encode(rebound), store, catalog)) == StateCodec.Encode(rebound), "A copied-slot receipt is idempotent on subsequent loads");
        store.runID = "unrelated-run";
        reject(() => RunData.DecodeLoaded(staged, store, catalog), "Another run's payload is not accepted as a historical copy");
        reject(() => StateCodec.Decode(json.Replace("\"Schema\":5", "\"Schema\":999"), state.RunId, state.SlotId, catalog), "Unknown schemas remain errors");
        reject(() => StateCodec.Decode(json[..^1] + ",\"Qualified\":true}", state.RunId, state.SlotId, catalog), "Duplicate gameplay properties are rejected");
        var restored = StateCodec.Decode(json, state.RunId, state.SlotId, catalog);
        TheftRules.Settle(restored, 1); int wind = restored.Wind;
        TheftRules.Settle(restored, 1); check(restored.Wind == wind, "Nightly heat is settled once per day");
        NewGameData.Instance = null;
    }
}
