using Newtonsoft.Json.Linq;
using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;

internal static class NightContentChecks
{
    internal static void Run(Catalog catalog, Func<int, ulong, RunState> create, ItemFactory factory,
        Action<bool, string> check, Action<Action, string> reject)
    {
        check(catalog.OpeningRoles.Count == 27 && catalog.OpeningRoles.Sum(role => role.Items.Count(id => catalog.Item(id).OpeningAllowed)) == 320,
            "Night expansion retains exactly the previous opening sources and item relations");
        var previous = Catalog.Load(); previous.Roles.RemoveAll(role => !role.OpeningAllowed);
        foreach (var role in previous.Roles)
        {
            role.Items.RemoveAll(id => !previous.Item(id).OpeningAllowed);
            role.CoreItems.RemoveAll(id => !role.Items.Contains(id));
        }
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var nowRandom = new StableRandom(seed); var oldRandom = new StableRandom(seed);
            var current = OpeningRules.Plan(catalog, Array.Empty<ItemState>(), factory, nowRandom, _ => true)!;
            var old = OpeningRules.Plan(previous, Array.Empty<ItemState>(), factory, oldRandom, _ => true)!;
            check(current != null && old != null && current.CashDelta == old.CashDelta && nowRandom.State == oldRandom.State &&
                current.Items.Select(x => (x.Id, x.Value, x.Heat)).SequenceEqual(old.Items.Select(x => (x.Id, x.Value, x.Heat))),
                "Night-only content cannot alter the same-seed opening package: " + seed);
        }

        var common = new[] { factory("printer_plastic", 2, new StableRandom(3)), factory("common_electronic", 2, new StableRandom(4)) };
        var core = new HashSet<string> { "printer_plastic" }; var biased = new StableRandom(1029384756); var uniform = new StableRandom(1029384756);
        int preferred = 0, ordinary = 0;
        for (int i = 0; i < 12000; i++)
        {
            if (TheftRules.Draw(common, 2, 0, biased, coreItems: core).Id == "printer_plastic") preferred++;
            if (TheftRules.Draw(common, 2, 0, uniform).Id == "printer_plastic") ordinary++;
        }
        check(preferred is > 7700 and < 8300 && ordinary is > 5700 and < 6300,
            "Common roster candidates favor profession items two to one; ordinary theft draws remain uniform");
        var rare = new[] { factory("system_module_eco", 2, new StableRandom(8)), factory("module_bay_expansion_kit", 2, new StableRandom(9)) };
        for (ulong seed = 1; seed <= 64; seed++)
        {
            check(TheftRules.Draw(rare, 2, 0, new StableRandom(seed), coreItems: new HashSet<string> { rare[0].Id }).Id ==
                TheftRules.Draw(rare, 2, 0, new StableRandom(seed)).Id, "Core preference never modifies the rare pool: " + seed);
            var state = create(0, seed); var npc = state.Night!.Npcs[0]; var role = catalog.Role("printTechnician");
            npc.RoleId = role.Id; npc.Faction = role.Faction; npc.TokenEligible = false;
            npc.Items = new List<ItemState> { factory("printer_plastic", 2, new StableRandom(3)), factory("common_electronic", 2, new StableRandom(4)) };
            TheftRules.SelectMode(state, TheftMode.Timing);
            TimingRules.Begin(state, npc, role, false, 639000000000000000, new DebugTuning { Success = .3, Exposure = 0 }, randomizeSuccessZoneAfterSuccess: false);
            TimingRules.Stop(state, .5, 639000000000000000);
            var expected = TheftRules.Draw(npc.Items, role.Tier, 0, new StableRandom(state.Night.RandomState), TheftRules.RegionFactors(npc.Region).Rare);
            TheftRules.Attempt(state, npc, role, false);
            check(state.Night.Loot.Single().UniqueId == expected.UniqueId, "Actual theft does not apply the generation-only core preference again: " + seed);
        }

        foreach (var definition in NightEvents.All)
        {
            var source = Enumerable.Range(0, 3).Select(region => NightEvents.Snapshot(region, new[] { definition.Id })).ToArray();
            var state = create(2, 99887766);
            state.Night = GenerationRules.Night(state, 2, catalog, factory, (role, _) => role.Id, source);
            check(state.Night.Npcs.Count == 9 && state.Night.Npcs.GroupBy(npc => npc.Region).All(group =>
                    group.Select(npc => npc.RoleId).Distinct().Count() == 3 && group.All(npc => npc.Items.Count == 5 && npc.Items.Select(x => x.Id).Distinct().Count() == 5)),
                "An event preserves the three-person, five-item roster contract: " + definition.Suffix);
            var snapshot = state.Night.RegionEvents.Single(x => x.Region == definition.Region);
            check(snapshot.EventIds.SequenceEqual(new[] { definition.Id }) && snapshot.ExposurePercent == definition.ExposurePercent,
                "Only the event's own district receives its snapshot: " + definition.Suffix);
            source[definition.Region].EventIds.Clear(); source[definition.Region].RoleBonuses.Clear(); source[definition.Region].ExposurePercent = 0;
            check(snapshot.EventIds.Single() == definition.Id && snapshot.RoleBonuses.Count == definition.RoleBonuses.Count,
                "Later live-event changes cannot mutate the already generated night: " + definition.Suffix);
            var loaded = StateCodec.Decode(StateCodec.Encode(state), state.RunId, state.SlotId, catalog);
            check(StateCodec.Encode(loaded) == StateCodec.Encode(state), "Event snapshots and RNG round trip without a reroll: " + definition.Suffix);
        }

        var recovering = create(2, 882233);
        string? brokenId = null;
        var skipped = new List<string>();
        recovering.Night = GenerationRules.Night(recovering, 2, catalog, (id, tier, random) =>
        {
            brokenId ??= id;
            if (id == brokenId) throw new InvalidDataException("Candidate factory failed");
            return factory(id, tier, random);
        }, (role, _) => role.Id, onCandidateFailure: (id, _) => skipped.Add(id));
        var rebuilt = StateCodec.Decode(StateCodec.Encode(recovering), recovering.RunId, recovering.SlotId, catalog);
        check(skipped.Count > 0 && rebuilt.Night!.Npcs.Count == 9 &&
            rebuilt.Night.Npcs.Any(npc => npc.Items.Count > 0) &&
            rebuilt.Night.Npcs.All(npc => npc.Items.All(item => item.Id != brokenId)),
            "A failing candidate leaves a usable, persistable roster with the other candidates");

        var weighted = create(0, 54321);
        weighted.Night!.RegionEvents = Enumerable.Range(0, 3).Select(region => NightEvents.Snapshot(region, NightEvents.All.Select(e => e.Id))).ToList();
        check(NightEvents.Weight(weighted.Night, 0, catalog.Regions[0].Roles.Single(x => x.Id == "merchant")) == 4.5 &&
            NightEvents.Weight(weighted.Night, 0, catalog.Regions[0].Roles.Single(x => x.Id == "hydroponicGrower")) == 12 &&
            NightEvents.Weight(weighted.Night, 0, catalog.Regions[0].Roles.Single(x => x.Id == "citizenHousehold")) == 13,
            "Two events can coexist with exact profession multipliers and unchanged unrelated weights");
        foreach (var (region, suffix, ratio) in new[] { (1, "shift_change", .85), (2, "security_check", 1.25) })
        {
            var state = create(5, 12345); var npc = state.Night!.Npcs.First(x => x.Region == region); var role = catalog.Role(npc.RoleId);
            var baseline = TheftRules.Preview(state, npc, role);
            state.Night.RegionEvents[region] = NightEvents.Snapshot(region, NightEvents.All.Where(e => e.Suffix == suffix).Select(e => e.Id));
            var modified = TheftRules.Preview(state, npc, role);
            check(modified.Success == baseline.Success && Math.Abs(modified.Exposure - baseline.Exposure * ratio) < 1e-12,
                "Event exposure is relative, with no change to success: " + suffix);
            TheftRules.SelectMode(state, TheftMode.Timing);
            var zones = TimingRules.Preview(state, npc, role);
            var practice = TimingRules.Practice(state, npc, role, 639000000000000000);
            TimingRules.Begin(state, npc, role, false, 639000000000000000);
            var reloaded = StateCodec.Decode(StateCodec.Encode(state), state.RunId, state.SlotId, catalog);
            check(practice.Zones == zones && reloaded.Night!.Timing!.Zones == zones,
                "Practice, frozen attempts and reload use the same event-adjusted manual zones: " + suffix);
        }

        var legacy = create(2, 10203040);
        var oldTarget = legacy.Night!.Npcs[0];
        TimingRules.Begin(legacy, oldTarget, catalog.Role(oldTarget.RoleId), false, 639000000000000000);
        string inventory = string.Join(',', legacy.Night.Npcs.SelectMany(npc => npc.Items).Select(item => item.UniqueId));
        var oldJson = JObject.Parse(StateCodec.Encode(legacy)); oldJson["Schema"] = 4; ((JObject)oldJson["Night"]!).Remove("RegionEvents");
        var upgraded = StateCodec.Decode(oldJson.ToString(), legacy.RunId, legacy.SlotId, catalog);
        check(upgraded.Schema == 5 && upgraded.Night!.RegionEvents.All(x => x.RuleVersion == 0 && x.EventIds.Count == 0 && x.ExposurePercent == 0) &&
            upgraded.Night.Timing!.Zones == legacy.Night.Timing!.Zones && upgraded.Night.RandomState == legacy.Night.RandomState &&
            string.Join(',', upgraded.Night.Npcs.SelectMany(npc => npc.Items).Select(item => item.UniqueId)) == inventory,
            "An old in-flight night gains no event modifiers and keeps its targets, timing and RNG");

        foreach (string id in new[] { "industrial_fish_feed", "gutterflow_pellet", "nutrifruit_seed", "kotton_seed", "fillerweed_seed",
            "hydroreed_seed", "bloomberry_seed", "plant_dna_disk", "nutrifruit", "hydroreed_filter", "metal_ingot" })
        {
            var state = create(0, 334455); var target = state.Night!.Npcs[0]; var role = catalog.Enabled.First(x => x.Items.Contains(id));
            target.RoleId = role.Id; target.Faction = role.Faction; target.TokenEligible = false;
            var item = factory(id, role.Tier, new StableRandom(10));
            var graph = JObject.Parse(item.Graph); var node = graph["saveItems"]![0]!;
            node["unitValue"] = item.Value;
            if (id is "industrial_fish_feed" or "gutterflow_pellet")
            {
                node["_keys"] = new JArray("CALORIE_VALUE_TAG");
                node["_values"] = new JArray(new JObject { ["valueInt"] = 2500, ["valueEnabled"] = true });
                item.Value = id == "industrial_fish_feed" ? 50 : 25; node["unitValue"] = item.Value; TheftRules.Classify(item);
            }
            else if (id.EndsWith("_seed", StringComparison.Ordinal))
            {
                node["_keys"] = new JArray("PLANT_TRAIT_infertile");
                node["_values"] = new JArray(new JObject { ["valueInt"] = 1, ["valueEnabled"] = true });
            }
            else if (id == "plant_dna_disk")
            {
                node["_keys"] = new JArray("DNA_DISK_TRAIT_STRING", "DNA_DISK_RANK_INT", "DNA_DISK_USES_INT");
                node["_values"] = new JArray(new JObject { ["valueString"] = "infertile" },
                    new JObject { ["valueInt"] = 2 }, new JObject { ["valueInt"] = 3 });
            }
            item.Graph = graph.ToString(); target.Items = new List<ItemState> { item };
            CandidateState.Compact(item);
            var loaded = StateCodec.Decode(StateCodec.Encode(state), state.RunId, state.SlotId, catalog).Night!.Npcs[0].Items.Single();
            check(item.Graph == graph.ToString() && loaded.Graph == item.Graph && loaded.Value == item.Value && loaded.NativeStateVersion == 1,
                "Candidate graph bytes and recorded value survive compaction and reload without a second codec: " + id);
        }
        foreach (string id in new[] { "nutrifruit", "metal_ingot" })
        {
            var state = create(0, 665544); var target = state.Night!.Npcs[0]; var role = catalog.Enabled.First(x => x.Items.Contains(id));
            target.RoleId = role.Id; target.Faction = role.Faction; target.TokenEligible = false;
            var item = factory(id, role.Tier, new StableRandom(10)); item.Graph = ""; item.NativeStateVersion = null; target.Items = new() { item };
            var loaded = StateCodec.Decode(StateCodec.Encode(state), state.RunId, state.SlotId, catalog).Night!.Npcs[0].Items.Single();
            check(loaded.Available && loaded.Graph.Length == 0 && loaded.NativeStateVersion == null,
                "Legacy lightweight crop/ingot templates retain their supported original meaning: " + id);
        }
        var invalid = create(0, 12345); invalid.Night!.RegionEvents.RemoveAt(0);
        reject(() => StateCodec.Validate(invalid, catalog), "A new-format save cannot silently lose a district event snapshot");
    }
}
