using System.Text.Json;
using System.Text.Json.Nodes;
using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;

internal static class FailureScopeChecks
{
    internal static void Run(Catalog catalog, Func<int, ulong, RunState> create,
        Action<bool, string> check, Action<Action, string> reject)
    {
        var state = create(2, 12345);
        state.LastAction = new ActionReceipt { Outcome = AttemptOutcome.Success, Success = double.NaN };
        var cloned = StateCodec.Clone(state);
        check(ReferenceEquals(cloned.LastAction, state.LastAction) && cloned.Night != state.Night,
            "Cloning preserves the session's animation receipt while isolating mutable gameplay state");
        string json = StateCodec.Encode(state);
        var legacy = JsonNode.Parse(json)!.AsObject();
        legacy["LastAction"] = new JsonObject { ["Mode"] = "damaged legacy display data", ["ExperienceAward"] = 9999 };
        var loaded = StateCodec.Decode(legacy.ToJsonString(), state.RunId, state.SlotId, catalog);
        check(loaded.Qualified && loaded.Experience == state.Experience && loaded.Revision == state.Revision &&
            loaded.RandomState == state.RandomState && loaded.Night!.RandomState == state.Night!.RandomState &&
            loaded.Night.Attempts == state.Night.Attempts && loaded.Night.SpentActions == state.Night.SpentActions &&
            loaded.LastAction == null,
            "Malformed legacy display receipts cannot discard eligibility, progress, spent actions or random state");
        check(StateCodec.Encode(loaded) == json, "Ignoring a display receipt does not reroll or rewrite the saved roster");
        var duplicatedDisplay = StateCodec.Decode(json[..^1] + ",\"LastAction\":{\"Mode\":0,\"Mode\":1}}",
            state.RunId, state.SlotId, catalog);
        check(StateCodec.Encode(duplicatedDisplay) == json,
            "Ignored legacy display data is also excluded from recursive duplicate-property checks");
        reject(() => StateCodec.Decode(legacy.ToJsonString(), "another-run", state.SlotId, catalog),
            "Display recovery does not relax run identity checks");
        var conflict = StateCodec.Clone(loaded);
        conflict.Night!.Npcs[1].Items[0].UniqueId = conflict.Night.Npcs[0].Items[0].UniqueId;
        reject(() => StateCodec.Validate(conflict, catalog), "Display recovery still rejects duplicate physical identities");

        Catalog Copy() => JsonSerializer.Deserialize<Catalog>(JsonSerializer.Serialize(catalog))!;
        var expanded = Copy();
        var extraRole = JsonSerializer.Deserialize<RoleDefinition>(JsonSerializer.Serialize(expanded.Enabled.First(x => x.Id != "scavGeneral")))!;
        extraRole.Id = "additional-valid-role";
        expanded.Roles.Add(extraRole);
        foreach (var region in expanded.Regions)
        {
            region.Roles.RemoveAt(region.Roles.Count - 1);
            foreach (var role in region.Roles) role.Weight *= 2;
        }
        expanded.Validate();
        check(expanded.Enabled.Count == catalog.Enabled.Count + 1 && expanded.Regions.All(x =>
                x.Roles.Count == catalog.Regions.Single(original => original.Id == x.Id).Roles.Count - 1),
            "Valid content additions and relative-weight changes do not prevent runtime initialization");
        var invalid = Copy(); invalid.Regions[1].Id = invalid.Regions[0].Id;
        reject(invalid.Validate, "Region identities remain unique and cover the three playable regions");
        invalid = Copy(); invalid.Regions[0].Roles.RemoveRange(2, invalid.Regions[0].Roles.Count - 2);
        reject(invalid.Validate, "Each region still needs enough distinct roles for its three targets");
        invalid = Copy(); invalid.Regions[0].Roles[1].Id = invalid.Regions[0].Roles[0].Id;
        reject(invalid.Validate, "A regional role cannot occupy two entries in the draw pool");
        invalid = Copy(); invalid.Roles[0].Items.Clear();
        reject(invalid.Validate, "An enabled role still needs real drop candidates");
    }
}
