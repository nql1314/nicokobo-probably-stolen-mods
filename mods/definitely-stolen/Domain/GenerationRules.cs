namespace DefinitelyStolen.Domain;

internal delegate ItemState ItemFactory(string id, int tier, StableRandom random);

internal static class GenerationRules
{
    internal static NightState Night(RunState run, int day, Catalog catalog, ItemFactory factory,
        Func<RoleDefinition, StableRandom, string> portrait, IReadOnlyList<RegionEventState>? events = null,
        Action<string, Exception>? onCandidateFailure = null)
    {
        int level = TheftRules.Level(run.Experience);
        var random = new StableRandom(run.RandomState);
        var night = new NightState { Mode = run.PreferredMode, Day = day, GenerationLevel = level,
            WindPenalty = TheftRules.WindPenalty(run.Wind), NoteRolled = run.Notes.Any(note => note.Day == day) };
        night.RegionEvents = (events ?? Enumerable.Range(0, 3).Select(region => NightEvents.Snapshot(region, Array.Empty<string>())).ToArray())
            .Select(x => new RegionEventState { Region = x.Region, RuleVersion = x.RuleVersion, EventIds = x.EventIds.ToList(),
                RoleBonuses = new Dictionary<string, double>(x.RoleBonuses, StringComparer.Ordinal), ExposurePercent = x.ExposurePercent }).ToList();
        NightEvents.Validate(night.RegionEvents, catalog);
        foreach (var region in catalog.Regions)
        {
            var remaining = region.Roles.ToList();
            for (int position = 0; position < 3; position++)
            {
                var chosen = random.Weighted(remaining, x => NightEvents.Weight(night, region.Id, x));
                remaining.Remove(chosen);
                var role = catalog.Role(chosen.Id);
                night.Npcs.Add(Npc(run.RunId, day, region.Id, position, level, role, random, factory, portrait, onCandidateFailure));
            }
        }
        night.RandomState = random.Next();
        run.RandomState = random.State;
        return night;
    }

    internal static NpcState Npc(string runId, int day, int region, int position, int level, RoleDefinition role,
        StableRandom random, ItemFactory factory, Func<RoleDefinition, StableRandom, string> portrait,
        Action<string, Exception>? onCandidateFailure = null)
    {
        if (!role.Enabled) throw new InvalidOperationException("Disabled identity");
        var npc = new NpcState { Key = $"{day}:{region}:{position}", Region = region, Position = position,
            RoleId = role.Id, Faction = role.Faction, Background = role.Background, Sprite = portrait(role, random) };
        if (role.Id == "scavGeneral")
        { npc.TokenAdmission = TheftRules.TokenAdmission[level]; npc.TokenEligible = random.Unit() < npc.TokenAdmission; }
        var candidates = new List<ItemState>();
        foreach (string id in SpecialNpcs.Items(runId, npc, role).Where(id => id != "scav_token" || npc.TokenEligible))
        {
            try
            {
                var item = factory(id, role.Tier, random);
                TheftRules.Classify(item);
                candidates.Add(item);
            }
            catch (Exception ex) when (onCandidateFailure != null) { onCandidateFailure(id, ex); }
        }
        var core = role.CoreItems.ToHashSet(StringComparer.Ordinal);
        for (int n = 0; n < TheftRules.InitialItems && candidates.Count > 0; n++)
        {
            var item = TheftRules.Draw(candidates, role.Tier, level, random, TheftRules.RegionFactors(region).Rare, core);
            candidates.Remove(item); npc.Items.Add(item);
        }
        return npc;
    }

    internal static NoteState? RollNote(RunState next, Catalog catalog, Func<ItemState, string> displayName, Func<ItemState, string>? englishName = null)
    {
        var night = next.Night ?? throw new InvalidOperationException("Generate targets before intelligence");
        if (night.NoteRolled) return null;
        night.NoteRolled = true;
        var random = new StableRandom(night.RandomState);
        night.NoteWon = random.Unit() < .5 && night.Npcs.Any(npc => npc.Items.Any(item => item.Available));
        NoteState? note = null;
        if (night.NoteWon)
        {
            var pairs = night.Npcs.Where(x => x.Items.Any(item => item.Available)).Select(npc =>
                (Npc: npc, Item: npc.Items.Where(item => item.Available).OrderByDescending(x => x.Value).ThenBy(x => x.UniqueId).First(), Tie: random.Next()))
                .OrderByDescending(x => x.Item.Value).ThenBy(x => x.Tie).Take(3).ToArray();
            var chosen = pairs[random.Index(pairs.Length)];
            var role = catalog.Role(chosen.Npc.RoleId); var region = catalog.Regions.Single(x => x.Id == chosen.Npc.Region);
            note = new NoteState { Day = night.Day, Target = chosen.Npc.Key, ItemUniqueId = chosen.Item.UniqueId,
                ItemName = displayName(chosen.Item), EnglishItemName = englishName?.Invoke(chosen.Item) ?? catalog.Item(chosen.Item.Id).English,
                Body = CatalogText.GetFor("note.body", false, night.Day, region.Name,
                    SpecialNpcs.DisplayName(next.RunId, chosen.Npc, role, false), chosen.Npc.Background, displayName(chosen.Item)),
                EnglishBody = CatalogText.GetFor("note.body", true, night.Day, region.English,
                    SpecialNpcs.DisplayName(next.RunId, chosen.Npc, role, true), role.EnglishBackground,
                    englishName?.Invoke(chosen.Item) ?? catalog.Item(chosen.Item.Id).English) };
            next.Notes.Add(note);
        }
        night.RandomState = random.State;
        next.Revision++;
        return note;
    }
}

internal sealed record OpeningPlan(IReadOnlyList<ItemState> Items, int CashDelta);

internal static class OpeningRules
{
    internal const int LowerReputationDelta = -15;
    internal const int GoodsAndCashBudget = 500;
    internal static readonly string[] Fixed = { "black_lamp" };
    internal static readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.Ordinal)
    { "neuroactive_perfume", "pheromone_perfume", "system_capped_neural_core", "turbo_booster_adv", "portable_water_purifier", "scav_token" };

    internal static OpeningPlan? Plan(Catalog catalog, IReadOnlyList<ItemState> equipment,
        ItemFactory factory, StableRandom random, Func<IReadOnlyList<ItemState>, bool> fits)
    {
        // State is fixed before combination search. Failed budgets cannot
        // repeatedly charge a battery or reroll a cigarette to inflate value.
        var openingRoles = catalog.OpeningRoles;
        var pools = openingRoles.ToDictionary(role => role.Id, role => role.Items
            .Where(id => catalog.Item(id).OpeningAllowed && !Excluded.Contains(id)).Select(id => factory(id, role.Tier, random)).ToArray());
        foreach (var item in pools.Values.SelectMany(x => x)) TheftRules.Classify(item);
        for (int attempt = 0; attempt < 4096; attempt++)
        {
            var picked = new List<ItemState>(); var seen = new HashSet<string>(Fixed, StringComparer.Ordinal);
            int low = 3 + random.Index(2), mid = 3 + random.Index(2);
            bool complete = true;
            for (int tier = 1; tier <= 3 && complete; tier++)
            {
                int count = tier == 1 ? low : tier == 2 ? mid : 1;
                for (int n = 0; n < count; n++)
                {
                    var sources = openingRoles.Where(r => r.Tier == tier).Select(r => (Role: r,
                        Items: pools[r.Id].Where(x => !seen.Contains(x.Id) && PriceBand(x.Value) == tier).ToArray()))
                        .Where(x => x.Items.Length > 0).ToArray();
                    if (sources.Length == 0) { complete = false; break; }
                    var source = sources[random.Index(sources.Length)];
                    var item = TheftRules.Draw(source.Items, tier, 2, random);
                    picked.Add(item); seen.Add(item.Id);
                }
            }
            if (!complete || picked.Where(x => PriceBand(x.Value) < 3).Sum(x => x.Value) <= picked.Single(x => PriceBand(x.Value) == 3).Value) continue;
            // After reserving the black lamp, random goods plus the cash delta
            // share one fixed budget, independent of native and third-party grants.
            if (!fits(equipment.Concat(picked).ToArray())) continue;
            int cashDelta = checked((int)(GoodsAndCashBudget - picked.Sum(x => x.Value)));
            return new(equipment.Concat(picked).ToArray(), cashDelta);
        }
        return null;
    }
    internal static int PriceBand(long value) => value <= 50 ? 1 : value <= 150 ? 2 : 3;
}
