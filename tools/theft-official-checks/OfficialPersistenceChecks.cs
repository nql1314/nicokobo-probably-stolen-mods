using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;
using Newtonsoft.Json.Linq;

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
        RunState Fresh() => new() { RunId = store.runID, SlotId = store.saveSlotId, RandomState = 42 };
        RunState Read() => RunData.Read(store, Fresh(), catalog, _ => { });
        check(!Read().Qualified, "An unrelated existing save has no invented theft eligibility");
        string json = StateCodec.Encode(state); store.modData[Identity.SaveKey] = json;
        check(StateCodec.Encode(Read()) == json, "Legacy owner.state is read without adding the official prefix twice");
        RunData.Stage(store, state);
        check(store.modData[RunData.FullKey] == json && !store.modData.ContainsKey(Identity.SaveKey), "Normal-save staging moves legacy data to owner:state once");
        store.modData[Identity.SaveKey] = "different legacy data";
        check(StateCodec.Encode(Read()) == json, "The valid official key wins over a conflicting legacy key");
        store.modData[RunData.FullKey] = "unreadable official data";
        store.modData[Identity.SaveKey] = json;
        check(StateCodec.Encode(Read()) == json, "An unreadable official key can recover from the legacy key");
        store.modData["another.mod:state"] = "untouched";
        RunData.Stage(store, state);
        check(store.modData[RunData.FullKey] == json && !store.modData.ContainsKey(Identity.SaveKey) &&
            store.modData["another.mod:state"] == "untouched", "Saving repairs both owned keys without touching other mods");
        store.saveSlotId = 9;
        var rebound = Read();
        check(rebound.RunId == state.RunId && rebound.SlotId == 9 && rebound.OpeningGranted && rebound.OpeningCash == 500,
            "Native copied slots retain opening receipts while rebinding only slot identity");
        check(rebound.RandomState == state.RandomState && rebound.Night!.RandomState == state.Night!.RandomState && rebound.Night.Npcs.SelectMany(x => x.Items).Select(x => x.UniqueId)
            .SequenceEqual(state.Night.Npcs.SelectMany(x => x.Items).Select(x => x.UniqueId)), "Rebinding cannot reroll NPCs, loot identity or random sequence");
        RunData.Stage(store, rebound);
        check(StateCodec.Encode(Read()) == StateCodec.Encode(rebound), "A copied-slot receipt is idempotent on subsequent loads");
        var originalNight = rebound.Night!;
        var damaged = JObject.FromObject(rebound);
        damaged["Night"]!["RegionEvents"] = new JArray();
        store.modData[RunData.FullKey] = damaged.ToString();
        var repairedEvents = Read();
        check(repairedEvents.Night?.RegionEvents.Count == 3 && repairedEvents.Night.RandomState == originalNight.RandomState &&
            repairedEvents.Night.Npcs[0].Items[0].UniqueId == originalNight.Npcs[0].Items[0].UniqueId,
            "A damaged event snapshot resets the modifiers without rerolling the night");
        var attempting = StateCodec.Clone(rebound);
        var target = attempting.Night!.Npcs[0];
        TimingRules.Begin(attempting, target, catalog.Role(target.RoleId), false, 639000000000000000);
        var damagedAttempt = JObject.FromObject(attempting);
        damagedAttempt["Night"]!["Timing"]!["StartedUtcTicks"] = 0;
        store.modData[RunData.FullKey] = damagedAttempt.ToString();
        check(Read().Night is { Timing: null, Started: true, SpentActions: 1 },
            "Invalid timing is cleared without replaying or refunding the attempt");
        var carry = originalNight.Npcs[0].Items[0];
        damaged["Night"]!["Npcs"] = new JArray();
        damaged["Night"]!["Carry"] = JArray.FromObject(new[] { new CarryState { UniqueId = carry.UniqueId, Graph = carry.Graph } });
        store.modData[RunData.FullKey] = damaged.ToString();
        var repairedNight = Read();
        check(repairedNight.Night == null && repairedNight.Experience == rebound.Experience && repairedNight.OpeningGranted &&
            repairedNight.RecoveryCarry?.Single().UniqueId == carry.UniqueId,
            "A broken roster resets the night and retains progression, opening receipt and an intact carry graph");
        RunData.Stage(store, repairedNight);
        check(Read().RecoveryCarry?.Single().Graph == carry.Graph, "Pending carry survives normal save and reload before it has been placed");
        damaged = JObject.FromObject(rebound);
        damaged["OpeningCash"] = -1;
        store.modData[RunData.FullKey] = damaged.ToString();
        check(Read() is { OpeningGranted: false, Night: not null }, "A broken opening receipt does not reset a valid night");
        store.runID = "unrelated-run";
        check(Read() is { RunId: "unrelated-run", Night: null, OpeningGranted: false }, "An unrelated run gets fresh theft data and no foreign items");
        reject(() => StateCodec.Decode(json.Replace("\"Schema\":5", "\"Schema\":999"), state.RunId, state.SlotId, catalog), "Unknown schemas remain errors");
        reject(() => StateCodec.Decode(json[..^1] + ",\"Qualified\":true}", state.RunId, state.SlotId, catalog), "Duplicate gameplay properties are rejected");
        var restored = StateCodec.Decode(json, state.RunId, state.SlotId, catalog);
        TheftRules.Settle(restored, 1); int wind = restored.Wind;
        TheftRules.Settle(restored, 1); check(restored.Wind == wind, "Nightly heat is settled once per day");
    }
}
