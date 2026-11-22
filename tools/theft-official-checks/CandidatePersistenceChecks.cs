using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.RegularExpressions;
using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;

internal static class CandidatePersistenceChecks
{
    internal static void Run(Catalog catalog, Func<int, ulong, RunState> create,
        Action<bool, string> check, Action<Action, string> reject)
    {
        var legacy = create(2, 12345);
        legacy.Schema = 3;
        var candidate = legacy.Night!.Npcs.SelectMany(x => x.Items)
            .First(x => !CandidateState.IsWater(x.Id) && !CandidateState.NeedsNativeState(x.Id));
        var graph = JsonNode.Parse(candidate.Graph)!;
        graph["unusedTemplate"] = new string('x', 180_000);
        candidate.Graph = graph.ToJsonString();
        GenerationRules.RollNote(legacy, catalog, x => x.Id);
        string source = StateCodec.Encode(legacy);
        var loaded = StateCodec.Decode(source, legacy.RunId, legacy.SlotId, catalog);
        check(loaded.Schema == 5 && loaded.Night!.Npcs.SelectMany(x => x.Items).All(x =>
                CandidateState.NeedsNativeState(x.Id) ? x.Graph.Length != 0 : x.Graph.Length == 0),
            "Legacy NPCs retain native graphs only where their chemical state requires them");
        check(StateCodec.Encode(loaded).Length < source.Length / 4,
            "A container-sized legacy NPC graph no longer dominates the saved state");
        check(loaded.RandomState == legacy.RandomState && loaded.Night!.RandomState == legacy.Night.RandomState &&
            loaded.Night.Npcs.SelectMany(x => x.Items).Select(x => (x.Id, x.UniqueId, x.Value, x.Condition))
                .SequenceEqual(legacy.Night.Npcs.SelectMany(x => x.Items).Select(x => (x.Id, x.UniqueId, x.Value, x.Condition))),
            "Migration preserves the roster, fixed item identity, conditions, values and random sequence");
        check(loaded.Notes.Select(x => (x.Target, x.ItemUniqueId, x.Body))
            .SequenceEqual(legacy.Notes.Select(x => (x.Target, x.ItemUniqueId, x.Body))),
            "Existing intelligence still points to the same candidate after migration");
        var reloaded = StateCodec.Decode(StateCodec.Encode(loaded), loaded.RunId, loaded.SlotId, catalog);
        check(StateCodec.Encode(reloaded) == StateCodec.Encode(loaded),
            "Lightweight candidates round trip without regenerating NPCs or their items");

        var chemicalRun = create(2, 12345);
        var chemicalRole = catalog.Enabled.First(role => role.Items.Contains("pink_alt_injector"));
        var chemicalNpc = chemicalRun.Night!.Npcs[0];
        chemicalNpc.RoleId = chemicalRole.Id; chemicalNpc.Faction = chemicalRole.Faction; chemicalNpc.TokenEligible = false;
        int chemicalUid = chemicalRun.Night.Npcs.SelectMany(npc => npc.Items).Max(item => item.UniqueId) + 1;
        string chemicalGraph = JsonSerializer.Serialize(new { saveItems = new[] { new {
            identifier = "pink_alt_injector", uniqueId = chemicalUid, unitCount = 1,
            _keys = new[] { "CURRENT_PART_NOMORE", "CURRENT_PART_STARCH" },
            _values = new[] { new { valueInt = 1900 }, new { valueInt = 2100 } } } },
            forgeItemFeatures = "fixed-actual-and-display-quality" });
        var chemical = new ItemState { Id = "pink_alt_injector", UniqueId = chemicalUid,
            Value = catalog.Item("pink_alt_injector").ReferenceValue, Graph = chemicalGraph };
        TheftRules.Classify(chemical); chemicalNpc.Items.Clear(); chemicalNpc.Items.Add(chemical);
        chemicalRun.Schema = 3;
        var chemicalLoaded = StateCodec.Decode(StateCodec.Encode(chemicalRun), chemicalRun.RunId, chemicalRun.SlotId, catalog);
        var fixedChemical = chemicalLoaded.Night!.Npcs[0].Items.Single();
        check(fixedChemical.Graph == chemicalGraph && fixedChemical.UniqueId == chemicalUid && fixedChemical.NativeStateVersion == 1,
            "Legacy complete chemistry and feature graphs retain original UID and bytes under version one");
        check(StateCodec.Encode(StateCodec.Decode(StateCodec.Encode(chemicalLoaded), chemicalRun.RunId, chemicalRun.SlotId, catalog)) == StateCodec.Encode(chemicalLoaded),
            "A chemical candidate round trip cannot reroll composition or quality");
        fixedChemical.Graph = ""; fixedChemical.NativeStateVersion = null;
        chemicalLoaded.Notes.Clear(); chemicalLoaded.Notes.Add(new NoteState { Day = chemicalLoaded.Night.Day,
            Target = chemicalLoaded.Night.Npcs[0].Key, ItemUniqueId = chemicalUid });
        var missingChemical = StateCodec.Decode(StateCodec.Encode(chemicalLoaded), chemicalRun.RunId, chemicalRun.SlotId, catalog);
        var missingNpc = missingChemical.Night!.Npcs[0];
        check(!missingNpc.Items.Single().Available && missingNpc.Items.Single().NativeStateVersion == 1 && !TheftRules.CanSteal(missingNpc) &&
            missingChemical.Notes.Single().Invalidated && missingChemical.Notes.Single().InvalidationReason == CandidateState.MissingChemicalState,
            "A legacy lightweight drug is excluded locally and its intelligence explains the missing state");
        check(missingChemical.Night.Npcs.Skip(1).SelectMany(npc => npc.Items).Select(item => item.UniqueId)
            .SequenceEqual(chemicalLoaded.Night.Npcs.Skip(1).SelectMany(npc => npc.Items).Select(item => item.UniqueId)),
            "Missing legacy chemical state does not regenerate or reject the other targets");
        var available = new ItemState { Id = "known", UniqueId = chemicalUid + 1, Value = 10, Weight = 1 };
        missingNpc.Items.Add(available); missingNpc.Items[0].Rare = true;
        check(TheftRules.CanSteal(missingNpc) && TheftRules.RareChance(missingNpc.Items, 1, 1) == 0 &&
            Enumerable.Range(1, 16).All(seed => ReferenceEquals(TheftRules.Draw(missingNpc.Items, 1, 1, new StableRandom((ulong)seed)), available)),
            "Drawing and rarity previews share the available candidate set");
        fixedChemical.Graph = chemicalGraph; fixedChemical.NativeStateVersion = 2;
        reject(() => StateCodec.Decode(StateCodec.Encode(chemicalLoaded), chemicalRun.RunId, chemicalRun.SlotId, catalog),
            "Unknown chemical graph versions are rejected instead of being regenerated");
        foreach (var (id, value, weight) in new[] { ("crypto_module_med", 250L, 3d),
            ("pheromone_perfume", 200L, 5d), ("neuroactive_perfume", 150L, 5d),
            ("crypto_module_med", 250L, .75d) })
        {
            var historical = create(2, 12345);
            var role = catalog.Enabled.First(x => x.Items.Contains(id));
            var npc = historical.Night!.Npcs[0];
            npc.RoleId = role.Id; npc.Faction = role.Faction; npc.TokenEligible = false;
            int uid = historical.Night.Npcs.SelectMany(x => x.Items).Max(x => x.UniqueId) + 1;
            var saved = new ItemState { Id = id, UniqueId = uid, Value = value };
            TheftRules.Classify(saved); saved.Weight = weight;
            npc.Items.Clear(); npc.Items.Add(saved);
            string historicalJson = StateCodec.Encode(historical);
            var accepted = StateCodec.Decode(historicalJson, historical.RunId, historical.SlotId, catalog);
            check(StateCodec.Encode(accepted) == historicalJson,
                "Saved draw weights preserve the complete night without matching current rules: " + id);
            var replayBefore = StateCodec.Clone(historical); var replayAfter = StateCodec.Clone(accepted);
            TheftRules.Attempt(replayBefore, replayBefore.Night!.Npcs[0], role, false);
            TheftRules.Attempt(replayAfter, replayAfter.Night!.Npcs[0], role, false);
            check(StateCodec.Encode(replayBefore) == StateCodec.Encode(replayAfter),
                "Loading historical weights preserves the next theft outcome: " + id);
            saved.Graph = JsonSerializer.Serialize(new { saveItems = new[] {
                new { identifier = id, uniqueId = uid, unitCount = 1 } } });
            historical.Schema = 3;
            var migrated = StateCodec.Decode(StateCodec.Encode(historical), historical.RunId, historical.SlotId, catalog);
            check(migrated.Schema == 5 && migrated.Night!.Npcs[0].Items.Single().Weight == weight,
                "Full-graph legacy candidates preserve their historical weight: " + id);
            historical.Schema = 4; npc.Items.Clear(); historical.Night.Loot.Add(saved);
            var physicalReload = StateCodec.Decode(StateCodec.Encode(historical), historical.RunId, historical.SlotId, catalog);
            check(physicalReload.Night!.Loot.Single().Weight == weight && physicalReload.Night.Loot.Single().Graph == saved.Graph,
                "Historical physical loot preserves its native graph and weight: " + id);
            saved.Weight = 0;
            reject(() => StateCodec.Validate(historical, catalog),
                "Non-positive weights still fail item validation: " + id);
            saved.Weight = weight; saved.Rare = false;
            var savedRarity = StateCodec.Decode(StateCodec.Encode(historical), historical.RunId, historical.SlotId, catalog);
            check(!savedRarity.Night!.Loot.Single().Rare && savedRarity.Night.Loot.Single().Weight == weight,
                "Saved rarity and weight remain unchanged when they differ from current value rules: " + id);
        }
        var left = StateCodec.Clone(loaded); var right = StateCodec.Clone(reloaded);
        var leftReceipt = TheftRules.Attempt(left, left.Night!.Npcs[0], catalog.Role(left.Night.Npcs[0].RoleId), false);
        var rightReceipt = TheftRules.Attempt(right, right.Night!.Npcs[0], catalog.Role(right.Night.Npcs[0].RoleId), false);
        check(StateCodec.Encode(left) == StateCodec.Encode(right) && leftReceipt.ItemUniqueId == rightReceipt.ItemUniqueId,
            "Reloading a lightweight roster cannot reroll the attempt or selected drop");
        var invalid = StateCodec.Clone(reloaded);
        var unmaterialized = invalid.Night!.Npcs[0].Items[0];
        invalid.Night.Npcs[0].Items.Remove(unmaterialized); invalid.Night.Loot.Add(unmaterialized);
        reject(() => StateCodec.Validate(invalid, catalog),
            "A candidate must become a complete native item before entering saved loot custody");

        invalid = StateCodec.Clone(reloaded);
        invalid.Night!.Npcs[1].Items[0].UniqueId = invalid.Night.Npcs[0].Items[0].UniqueId;
        reject(() => StateCodec.Validate(invalid, catalog), "Lightweight candidates still reject conflicting root identities");

        var liquid = legacy.Night.Npcs.SelectMany(x => x.Items).FirstOrDefault(x => CandidateState.IsWater(x.Id));
        if (liquid != null)
        {
            var parts = liquid.LiquidParts!;
            liquid.Graph = JsonSerializer.Serialize(new { saveItems = new[] { new { identifier = liquid.Id,
                uniqueId = liquid.UniqueId, unitCount = 1, _keys = parts.Keys,
                _values = parts.Values.Select(value => new { valueInt = value, valueEnabled = true }) } } });
            liquid.LiquidParts = null;
            var migratedWater = StateCodec.Decode(StateCodec.Encode(legacy), legacy.RunId, legacy.SlotId, catalog)
                .Night!.Npcs.SelectMany(x => x.Items).Single(x => x.UniqueId == liquid.UniqueId);
            check(migratedWater.Graph.Length == 0 && migratedWater.LiquidParts!.OrderBy(x => x.Key).SequenceEqual(parts.OrderBy(x => x.Key)) &&
                migratedWater.Purity == liquid.Purity && migratedWater.VolumeMl == liquid.VolumeMl,
                "Legacy water preserves exact constituent quantities, volume and purity without a graph");
            migratedWater.LiquidParts!["CURRENT_PART_WATER"]--;
            reject(() => CandidateState.Validate(migratedWater), "Inconsistent candidate liquid quantities are rejected");
        }

        var farmer = catalog.Role("retiredAquafarmer");
        foreach (string id in farmer.Items.Where(CandidateState.IsFish))
        {
            var fishRun = create(2, 7654321);
            var fishNpc = fishRun.Night!.Npcs[0];
            fishNpc.RoleId = farmer.Id; fishNpc.Faction = farmer.Faction; fishNpc.TokenEligible = false;
            int uid = fishRun.Night.Npcs.SelectMany(x => x.Items).Max(x => x.UniqueId) + 1;
            var fish = new ItemState { Id = id, UniqueId = uid, Value = catalog.Item(id).ReferenceValue,
                Condition = id is "tilapia" or "graft_tilapia" or "eel" ? "MALE" : "FEMALE" };
            TheftRules.Classify(fish);
            fishNpc.Items.Clear(); fishNpc.Items.Add(fish);
            string saved = StateCodec.Encode(fishRun);
            var restored = StateCodec.Decode(saved, fishRun.RunId, fishRun.SlotId, catalog);
            var restoredFish = restored.Night!.Npcs[0].Items.Single();
            check(StateCodec.Encode(restored) == saved && restoredFish.Condition == fish.Condition &&
                restoredFish.UniqueId == uid && restoredFish.Graph.Length == 0,
                "Aquafarmer candidates preserve fish sex and identity without native graphs: " + id);
            var before = StateCodec.Clone(fishRun); var after = StateCodec.Clone(restored);
            TheftRules.Attempt(before, before.Night!.Npcs[0], farmer, false);
            TheftRules.Attempt(after, after.Night!.Npcs[0], farmer, false);
            check(StateCodec.Encode(before) == StateCodec.Encode(after),
                "Reloading a fish candidate preserves the next theft outcome: " + id);
            restoredFish.Condition = "native";
            reject(() => StateCodec.Validate(restored, catalog),
                "A fish candidate without its original sex cannot be silently rerolled: " + id);
        }

        var outside = StateCodec.Clone(reloaded);
        var physical = outside.Night!.Npcs[0].Items[0];
        outside.Night.Npcs[0].Items.Remove(physical); outside.Night.Loot.Add(physical);
        physical.Graph = JsonSerializer.Serialize(new { saveItems = new[] {
            new { identifier = physical.Id, uniqueId = physical.UniqueId, unitCount = 1 },
            new { identifier = "water_tablet", uniqueId = 987654321, unitCount = 9 } } });
        outside.Night.Outside = true;
        outside.Night.Carry.Add(new CarryState { UniqueId = physical.UniqueId, Graph = physical.Graph });
        var actualReload = StateCodec.Decode(StateCodec.Encode(outside), outside.RunId, outside.SlotId, catalog);
        check(actualReload.Night!.Loot.Single().Graph == physical.Graph && actualReload.Night.Carry.Single().Graph == physical.Graph,
            "Actual outside loot retains complete native roots, children, quantities and carry snapshots");

        outside.OpeningItems.Add(new ItemState { Id = "fanny_pack", UniqueId = 7, Graph = physical.Graph });
        outside.Notes.Add(new NoteState { Day = 99, Delivered = true, Paper = new ItemState { UniqueId = 8, Graph = physical.Graph } });
        outside.Notes.Add(new NoteState { Day = 100, Paper = new ItemState { UniqueId = 9, Graph = physical.Graph } });
        StateCodec.TrimReceipts(outside);
        check(outside.OpeningItems.Single().UniqueId == 7 && outside.OpeningItems.Single().Graph.Length == 0 &&
            outside.Notes.Single(x => x.Day == 99).Paper!.Graph.Length == 0 && outside.Notes.Single(x => x.Day == 99).Paper!.UniqueId == 8 &&
            outside.Notes.Single(x => x.Day == 100).Paper!.Graph == physical.Graph && outside.Night.Carry.Single().Graph == physical.Graph,
            "Delivered receipts drop redundant graphs while pending papers and actual carry retain theirs");
    }

    internal static void Inspect(string path, Catalog catalog, Action<bool, string> check)
    {
        string save = File.ReadAllText(path);
        var match = Regex.Match(save, "\"" + Regex.Escape(Identity.SaveKey) + "\"\\s*:\\s*");
        if (!match.Success) throw new InvalidDataException("Save contains no theft state");
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(save[(match.Index + match.Length)..]));
        if (!reader.Read() || reader.TokenType != JsonTokenType.String) throw new InvalidDataException("Invalid saved theft state");
        string source = reader.GetString()!;
        using var json = JsonDocument.Parse(source);
        string runId = json.RootElement.GetProperty("RunId").GetString()!;
        int slot = json.RootElement.GetProperty("SlotId").GetInt32();
        var loaded = StateCodec.Decode(source, runId, slot, catalog);
        string encoded = StateCodec.Encode(loaded);
        check(StateCodec.Encode(StateCodec.Decode(encoded, runId, slot, catalog)) == encoded, "Real saved state survives migration and a second reload");
        check(loaded.Night?.Npcs.SelectMany(x => x.Items).All(x => CandidateState.NeedsNativeState(x.Id) || x.Graph.Length == 0) != false,
            "Real saved NPCs keep native graphs only for stateful chemicals");
        Console.WriteLine($"{Path.GetFileName(path)}: {Encoding.UTF8.GetByteCount(source)} -> {Encoding.UTF8.GetByteCount(encoded)} bytes; " +
            $"NPC items={loaded.Night?.Npcs.Sum(x => x.Items.Count) ?? 0}; schema={loaded.Schema}");
    }
}
