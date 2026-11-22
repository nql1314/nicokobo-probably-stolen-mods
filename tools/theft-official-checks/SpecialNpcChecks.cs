using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;
using DefinitelyStolen.UI;

internal static class SpecialNpcChecks
{
    internal static void Run(Catalog catalog, Func<int, ulong, RunState> create, ItemFactory factory, Action<bool, string> check)
    {
        var seen = new HashSet<string>();
        var officerDrops = new HashSet<string>();
        bool stable = true, unique = true, correctLoot = true, unchanged = true, named = true, validTiming = true, intelMatches = true;
        for (ulong seed = 1; seed <= 512; seed++)
        {
            var run = create(2, seed);
            string saved = StateCodec.Encode(run);
            var loaded = StateCodec.Decode(saved, run.RunId, run.SlotId, catalog);
            var names = new List<string>();
            foreach (var npc in run.Night!.Npcs)
            {
                var role = catalog.Role(npc.RoleId);
                var special = SpecialNpcs.Select(run.RunId, npc);
                if (special == null)
                { correctLoot &= npc.Items.All(x => role.Items.Contains(x.Id)); continue; }
                seen.Add(special.Id); names.Add(special.Id);
                if (special.Id == "officerRen") officerDrops.UnionWith(npc.Items.Select(x => x.Id));
                var reloaded = loaded.Night!.Npcs.Single(x => x.Key == npc.Key);
                stable &= SpecialNpcs.Select(loaded.RunId, reloaded) == special;
                reloaded.Alert = true; reloaded.Attempts++;
                stable &= SpecialNpcs.Select(loaded.RunId, reloaded) == special;
                correctLoot &= special.RoleId == npc.RoleId && npc.Items.All(x => SpecialNpcs.Items(run.RunId, npc, role).Contains(x.Id));
                named &= SpecialNpcs.DisplayName(run.RunId, npc, role, false) == special.Name + " · " + role.Name
                    && SpecialNpcs.DisplayName(run.RunId, npc, role, true) == special.English + " · " + role.English
                    && SceneLayout.PortraitName(run.RunId, npc) == special.Portrait;
                var preview = TheftRules.Preview(run, npc, role);
                var timing = TimingRules.Preview(run, npc, role);
                validTiming &= double.IsFinite(preview.Success) && timing.Success > 0;
            }
            unique &= names.Distinct().Count() == names.Count;
            unchanged &= saved == StateCodec.Encode(run);
            var note = GenerationRules.RollNote(run, catalog, item => catalog.Item(item.Id).Name);
            if (note != null)
            {
                var target = run.Night.Npcs.Single(x => x.Key == note.Target);
                var role = catalog.Role(target.RoleId);
                intelMatches &= note.Body.Contains(SpecialNpcs.DisplayName(run.RunId, target, role, false))
                    && note.EnglishBody.Contains(SpecialNpcs.DisplayName(run.RunId, target, role, true));
            }
        }
        check(seen.SetEquals(SpecialNpcs.All.Select(x => x.Id)), "Generated nights can encounter all eight named characters");
        check(stable && unchanged, "Named appearance survives load and attempts without mutating saves or gameplay random state");
        check(unique, "A named character never appears twice in one generated night");
        check(correctLoot && named, "Name, occupation, portrait and generated loot refer to the same character");
        check(intelMatches, "Chinese and English intelligence identify the same target shown in the scene");
        check(validTiming, "Every special character supports probability and manual theft previews");
        check(SpecialNpcs.All.Single(x => x.Id == "georgio").RoleId == "scavGeneral"
            && catalog.Role("scavGeneral").Name == "拾荒者", "Georgio uses the scavenger occupation and loot pool");
        check(officerDrops.IsSupersetOf(new[] { "dream_dust", "pink_injector" }),
            "Generated Officer Ren targets can carry Dream Dust and Oxymore Injectors");
        foreach (var special in SpecialNpcs.All.Where(x => x.RareItem != null))
        {
            RunState? run = null;
            NpcState? target = null;
            for (ulong seed = 1; seed <= 4096 && target == null; seed++)
            {
                var candidate = create(10, seed);
                target = candidate.Night!.Npcs.SingleOrDefault(x => SpecialNpcs.Select(candidate.RunId, x)?.Id == special.Id);
                if (target != null) run = candidate;
            }
            check(run != null && target != null, "Find a generated named target for rare-loot checks: " + special.Id);
            var role = catalog.Role(target!.RoleId);
            check(!role.Items.Contains(special.RareItem!), "Named rare candidate stays out of the shared occupation pool: " + special.Id);
            var random = new StableRandom(54321);
            NpcState? carrying = null;
            for (int sample = 0; sample < 2048 && carrying == null; sample++)
            {
                var refreshed = GenerationRules.Npc(run!.RunId, run.Night!.Day, target.Region, target.Position, 10,
                    role, random, factory, (_, _) => target.Sprite);
                if (refreshed.Items.Any(x => x.Id == special.RareItem)) carrying = refreshed;
            }
            check(carrying != null && carrying.Items.Count == TheftRules.InitialItems
                && carrying.Items.Select(x => x.Id).Distinct().Count() == TheftRules.InitialItems,
                "Refreshing a named target can draw its rare item without adding an extra carried slot: " + special.Id);
            target.Items = carrying!.Items;
            var rare = target.Items.Single(x => x.Id == special.RareItem);
            check(rare.Rare && rare.Value >= 100, "Named high-value item enters the rare pool: " + special.Id);
            var loaded = StateCodec.Decode(StateCodec.Encode(run!), run!.RunId, run.SlotId, catalog);
            var restored = loaded.Night!.Npcs.Single(x => x.Key == target.Key).Items.Single(x => x.Id == special.RareItem);
            check(SpecialNpcs.Select(run.RunId, target) == special && restored.UniqueId == rare.UniqueId
                && restored.Value == rare.Value && restored.Weight == rare.Weight && restored.Rare == rare.Rare
                && restored.Graph == rare.Graph, "Named rare loot survives save/load with its identity and state intact: " + special.Id);
            var other = SpecialNpcs.All.First(x => x.RareItem != null && x.Id != special.Id);
            target.Items[target.Items.IndexOf(rare)] = factory(other.RareItem!, role.Tier, random);
            bool rejected = false;
            try { StateCodec.Validate(run, catalog); }
            catch (InvalidDataException ex) when (ex.Message == "Candidate escaped its identity") { rejected = true; }
            check(rejected, "A different character's rare item remains invalid for this target: " + special.Id);
        }
        var factions = new[] { "FACTION_SECURITY", "FACTION_MIDDLE" };
        for (int region = 0; region < 3; region++)
        {
            check(TheftRules.ReputationPenalties("FACTION_MIDDLE", TheftRules.TargetReputationPenalty(region), factions)
                .SequenceEqual(new[] { ("FACTION_SECURITY", 1), ("FACTION_MIDDLE", region + 1) }),
                "Caught theft charges fixed security reputation and the regional target penalty: " + region);
        }
        check(TheftRules.ReputationPenalties("FACTION_SECURITY", TheftRules.TargetReputationPenalty(1), factions)
            .SequenceEqual(new[] { ("FACTION_SECURITY", 1), ("FACTION_SECURITY", 2) }),
            "An off-duty officer at the transit hub charges both deductions to Security");
        check(TheftRules.ReputationPenalties("FACTION_TOURIST", 2, factions)
            .SequenceEqual(new[] { ("FACTION_SECURITY", 1) }), "A tourist without reputation only charges the fixed security penalty");
        foreach (var special in SpecialNpcs.All.Where(x => !x.DedicatedRole))
        {
            int count = 0;
            for (int day = 0; day < 4096; day++)
                if (SpecialNpcs.Select("appearance-frequency", new NpcState
                    { Key = $"{day}:{special.Region}:0", Region = special.Region, RoleId = special.RoleId }) != null) count++;
            check(count is > 360 and < 620, "Named appearance stays uncommon: " + special.Id);
        }
        var cleaner = catalog.Role("streetCleaner");
        var officer = catalog.Role("offDutySecurity");
        check(cleaner.Items.Contains("shampoo") && cleaner.Items.Contains("paper_towel") &&
            officer.Items.Contains("cup_noodle") && officer.Items.Contains("salve") &&
            !cleaner.Items.Concat(officer.Items).Intersect(new[] { "combat_knife", "sec_keycard", "crypto_module_sec", "blank_keycard" }).Any(),
            "Cleaner and off-duty officer carry relevant daily goods without restricted security gear");
    }
}
