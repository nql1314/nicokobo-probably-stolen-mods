using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nicokobo.ContentInterop;

internal sealed record AchievementReward(string ItemId, string NameKey, int Count = 1);
internal sealed record AchievementDefinition(string Id, AchievementReward[] Rewards, float X, float Y);
internal sealed record AchievementItem(int Uid, string Id, bool Owned, int Units = 1,
    bool FullPureWater = false, bool LiveRat = false, float RatGrams = 0,
    bool PerfectIngot = false, bool MasterWine = false, int WineDays = 0, int ScannerChance = 0);
internal sealed record AchievementObservation(IReadOnlyList<AchievementItem> Items,
    int Wealth, int Attractiveness, double Reputation);
internal sealed record AchievementEvidence(bool Met, string Progress, int[] Uids);
internal sealed record RewardReceipt(string ItemId, int Uid, int Units);
internal sealed record AchievementCompletion(string Proof, int[] Uids);
internal sealed record PendingReward(string NodeId, RewardReceipt[] Items);
internal sealed record AchievementState(int Schema, string RunId, int SlotId,
    Dictionary<string, AchievementCompletion> Completed, HashSet<string> Claimed,
    PendingReward? Pending = null)
{
    internal static AchievementState Empty(string runId, int slotId) =>
        new(1, runId, slotId, new(StringComparer.Ordinal), new(StringComparer.Ordinal));
}

internal static class AchievementRules
{
    internal const string SaveKey = "nicokobo.forge.native_achievements";
    internal const string VictoryNode = "workshop_master";
    internal static readonly string[] Tools = ["flashlight", "screwdriver", "wire_cutter", "welder", "metal_scanner"];
    internal static readonly string[] Factions = ["FACTION_LOWER_LEVEL", "FACTION_UPPER_LEVEL",
        "FACTION_SECURITY", "FACTION_BLACK_MARKET", "FACTION_REVOLUTION", "FACTION_CARTEL"];
    internal static readonly AchievementDefinition[] Definitions =
    [
        new("pure_water_jug",
            [new("water_filter_adv", "forge.advanced_water_filter")], -0.78f, -0.68f),
        new("rat_800g",
            [new("serum_green", "forge.livestock_immunity_serum", 5),
             new("serum_red", "forge.livestock_steroid_serum", 5)], -0.28f, -0.84f),
        new("quality_overload",
            [new("system_capped_neural_core", "forge.capped_neural_core_module")], 0.28f, -0.84f),
        new("master_brewer",
            [new("wine_yeast_infinite", "forge.yeast_bioreactor")], 0.78f, -0.68f),
        new("scavenger_toolset",
            [new("backpack_large", "forge.large_backpack")], -0.96f, 0f),
        new("max_scav_scanner",
            [new("scav_token", "forge.scavenger_token")], -0.68f, 0.72f),
        new("wealthy_store",
            [new("machine_bay_ext", "forge.extended_machine_bay")], 0.96f, 0f),
        new("store_attractiveness",
            [new("storage_bay_large", "forge.extended_storage_bay")], 0.68f, 0.72f),
        new("faction_max_rep",
            [new("smuggler_bay_mod", "forge.modified_smuggler_s_bay")], 0f, 0.82f),
        new("workshop_master",
            [], 0f, 0f)
    ];

    internal static Dictionary<string, AchievementEvidence> Evaluate(AchievementObservation observation, bool storeOwned)
    {
        var owned = observation.Items.Where(x => x.Owned && x.Uid > 0 && x.Units > 0)
            .GroupBy(x => x.Uid).Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
        var result = new Dictionary<string, AchievementEvidence>(StringComparer.Ordinal);
        void ItemGoal(string id, Func<AchievementItem, bool> predicate, string progress)
        {
            var item = owned.FirstOrDefault(predicate);
            result.Add(id, new(item != null, progress, item == null ? [] : [item.Uid]));
        }
        ItemGoal("pure_water_jug", x => x.Id == "water_jug" && x.FullPureWater,
            ForgeText.Get("forge.fill_a_jug_of_water_to_capacity"));
        float grams = owned.Where(x => x.Id == "rat" && x.LiveRat && float.IsFinite(x.RatGrams)).Select(x => x.RatGrams).DefaultIfEmpty().Max();
        ItemGoal("rat_800g", x => x.Id == "rat" && x.LiveRat && float.IsFinite(x.RatGrams) && x.RatGrams >= 800f,
            (ForgeText.Get("forge.heaviest_live_rat")) + $"{grams:0.##} / {800f:0}g");
        ItemGoal("quality_overload", x => x.Id == "metal_ingot" && x.PerfectIngot,
            ForgeText.Get("forge.own_a_perfect_quality_metal_ingot"));
        int age = owned.Where(x => IsWineBottle(x.Id) && x.MasterWine).Select(x => x.WineDays).DefaultIfEmpty().Max();
        ItemGoal("master_brewer", x => IsWineBottle(x.Id) && x.MasterWine && x.WineDays >= 10,
            ForgeText.Get("forge.master_vintage_age_10_days", age));
        var tools = Tools.Select(id => owned.FirstOrDefault(x => x.Id == id)).Where(x => x != null).ToArray();
        result.Add("scavenger_toolset", new(tools.Length == Tools.Length,
            $"{tools.Length} / {Tools.Length}" + (ForgeText.Get("forge.tools")), tools.Select(x => x!.Uid).ToArray()));
        int chance = owned.Where(x => x.Id == "metal_scanner").Select(x => x.ScannerChance).DefaultIfEmpty().Max();
        ItemGoal("max_scav_scanner", x => x.Id == "metal_scanner" && x.ScannerChance >= 100, $"{chance}% / 100%");
        result.Add("wealthy_store", new(observation.Wealth > 100000,
            ForgeText.Get("forge.credits_must_exceed_100_000", observation.Wealth), []));
        result.Add("store_attractiveness", new(observation.Attractiveness >= 1000,
            $"{observation.Attractiveness:N0} / 1,000" + (ForgeText.Get("forge.points")), []));
        result.Add("faction_max_rep", new(double.IsFinite(observation.Reputation) && observation.Reputation >= 200,
            $"{observation.Reputation:0.##} / 200", []));
        result.Add(VictoryNode, StoreOwnership(storeOwned));
        return result;
    }

    internal static bool IsFullWaterJug(int totalVolume, int capacity) =>
        capacity > 0 && totalVolume > 0 &&
        Math.Abs((long)totalVolume - capacity) <= 1000;

    private static AchievementEvidence StoreOwnership(bool storeOwned) =>
        new(storeOwned, ForgeText.Get(storeOwned ? "forge.store_owned" : "forge.buy_out_the_store_or_pay_off"), []);

    internal static AchievementState RecordStoreOwnership(AchievementState state)
    {
        if (state.Completed.ContainsKey(VictoryNode)) return state;
        return Latch(state, new Dictionary<string, AchievementEvidence> { [VictoryNode] = StoreOwnership(true) });
    }
    internal static bool HasVictoryBudgetReward(AchievementState? state) =>
        state?.Claimed.Contains(VictoryNode) == true;
    // Native self-brewing retains wine_bottle even after it becomes finished BloomBerry Wine.
    internal static bool IsWineBottle(string id) => id is "wine_bottle" or "wine_bloomberry";
    internal static AchievementState Latch(AchievementState state, IReadOnlyDictionary<string, AchievementEvidence> evidence)
    {
        var completed = new Dictionary<string, AchievementCompletion>(state.Completed, StringComparer.Ordinal);
        foreach (var (id, proof) in evidence)
            if (proof.Met && !completed.ContainsKey(id)) completed.Add(id, new(proof.Progress, proof.Uids));
        return state with { Completed = completed };
    }
    internal static AchievementState Claim(AchievementState state)
    {
        var pending = state.Pending ?? throw new InvalidOperationException("No pending reward");
        if (!state.Completed.ContainsKey(pending.NodeId) || state.Claimed.Contains(pending.NodeId))
            throw new InvalidOperationException("Reward cannot be claimed in this state");
        var claimed = new HashSet<string>(state.Claimed, StringComparer.Ordinal) { pending.NodeId };
        return state with { Claimed = claimed, Pending = null };
    }
    internal static bool ReceiptsMatch(PendingReward pending, IEnumerable<RewardReceipt> owned)
    {
        var snapshot = owned.ToArray();
        return pending.Items.All(expected => snapshot.Count(x => x.Uid == expected.Uid) == 1 && snapshot.Contains(expected));
    }
    internal static string Encode(AchievementState state) => JsonConvert.SerializeObject(state);
    internal static AchievementState Decode(string json, string runId, int slotId)
    {
        var state = DecodeLoadedRun(json, runId);
        if (state.SlotId != slotId) throw new InvalidDataException("Invalid achievement slot identity");
        return state;
    }
    // The caller must prove this is the synchronous native Normal-load bind.
    // Validate the complete original state before changing only its slot identity.
    internal static AchievementState DecodeLoadedRun(string json, string runId)
    {
        var document = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        var state = document.ToObject<AchievementState>() ?? throw new InvalidDataException("Missing achievement state");
        var ids = Definitions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (state.Schema != 1 || string.IsNullOrWhiteSpace(runId) || state.RunId != runId ||
            document["SlotId"]?.Type != JTokenType.Integer || state.SlotId < 0 || state.Completed == null || state.Claimed == null ||
            state.Completed.Any(x => !ids.Contains(x.Key) || x.Value == null || string.IsNullOrWhiteSpace(x.Value.Proof) ||
                x.Value.Uids == null || x.Value.Uids.Any(id => id <= 0)) ||
            state.Claimed.Any(x => !state.Completed.ContainsKey(x))) throw new InvalidDataException("Invalid achievement state or run identity");
        if (state.Pending is { } pending)
        {
            var definition = Definitions.FirstOrDefault(x => x.Id == pending.NodeId);
            if (definition == null || !state.Completed.ContainsKey(pending.NodeId) || state.Claimed.Contains(pending.NodeId))
                throw new InvalidDataException("Invalid pending reward owner");
            if (pending.Items == null ||
                pending.Items.Any(x => x == null || x.Uid <= 0 || x.Units != 1 || string.IsNullOrWhiteSpace(x.ItemId)) ||
                pending.Items.Select(x => x.Uid).Distinct().Count() != pending.Items.Length ||
                pending.Items.Length != definition.Rewards.Sum(reward => reward.Count) ||
                definition.Rewards.Any(reward => pending.Items.Count(item => item.ItemId == reward.ItemId) != reward.Count))
                throw new InvalidDataException("Invalid reward identities");
        }
        return state;
    }
}
