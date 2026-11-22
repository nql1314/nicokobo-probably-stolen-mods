using Newtonsoft.Json;
using Nicokobo.ContentInterop;

internal static class AchievementChecks
{
    private static int _checks;
    private static void Expect(bool value, string reason) { _checks++; if (!value) throw new Exception(reason); }
    private static void Reject(Action action, string reason)
    {
        _checks++;
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or JsonException) { return; }
        throw new Exception(reason);
    }
    private static Dictionary<string, AchievementEvidence> Goals(params AchievementItem[] items) =>
        AchievementRules.Evaluate(new(items, 0, 0, 0), false);
    internal static void Run()
    {
        Expect(AchievementRules.Definitions.Length == 10, "Achievement catalog must have ten goals");
        Expect(!Goals(new AchievementItem(1, "water_jug", false, FullPureWater: true))["pure_water_jug"].Met, "Unowned proof qualified");
        Expect(!Goals(new AchievementItem(1, "water_jug", true))["pure_water_jug"].Met, "Water identifier alone qualified");
        Expect(Goals(new AchievementItem(1, "water_jug", true, FullPureWater: true))["pure_water_jug"].Met, "Native full pure water did not qualify");
        Expect(Goals(new AchievementItem(13893, "water_jug", true,
            FullPureWater: AchievementRules.IsFullWaterJug(5_999_999, 6_000_000)))["pure_water_jug"].Met,
            "Pure water from save_25 was rejected for a one-microlitre shortfall");
        foreach (var (volume, met) in new[]
        {
            (6_000_000, true), (5_999_000, true), (5_998_999, false),
            (6_000_001, true), (6_001_000, true), (6_001_001, false), (0, false)
        })
            Expect(AchievementRules.IsFullWaterJug(volume, 6_000_000) == met,
                $"Incorrect one-millilitre capacity tolerance at volume {volume}");
        Expect(!AchievementRules.IsFullWaterJug(1, 0) && !AchievementRules.IsFullWaterJug(0, 1000),
            "Invalid capacity or an empty container qualified within the volume tolerance");
        Expect(!Goals(new AchievementItem(1, "rat", true, LiveRat: true, RatGrams: 799.999f))["rat_800g"].Met, "Rounded rat weight qualified");
        Expect(!Goals(new AchievementItem(1, "rat", true, LiveRat: true, RatGrams: 400), new(2, "rat", true, LiveRat: true, RatGrams: 400))["rat_800g"].Met, "Rat weights were summed");
        Expect(!Goals(new AchievementItem(1, "rat", true, LiveRat: false, RatGrams: 800))["rat_800g"].Met, "Dead rat qualified");
        Expect(!Goals(new AchievementItem(1, "rat", true, LiveRat: true, RatGrams: float.NaN))["rat_800g"].Met, "Invalid weight qualified");
        Expect(Goals(new AchievementItem(1, "rat", true, LiveRat: true, RatGrams: 800))["rat_800g"].Met, "800g boundary failed");
        Expect(!Goals(new AchievementItem(1, "metal_ingot", true))["quality_overload"].Met, "Imperfect ingot qualified");
        Expect(Goals(new AchievementItem(1, "metal_ingot", true, PerfectIngot: true))["quality_overload"].Met, "Perfect native ingot failed");
        Expect(!Goals(new AchievementItem(1, "wine_bloomberry", true, MasterWine: true, WineDays: 9))["master_brewer"].Met, "Nine-day wine qualified");
        Expect(!Goals(new AchievementItem(1, "wine_bloomberry", true, MasterWine: true, WineDays: 1), new(2, "wine_bloomberry", true, WineDays: 15))["master_brewer"].Met,
            "Quality and age were taken from different bottles");
        Expect(Goals(new AchievementItem(1, "wine_bloomberry", true, MasterWine: true, WineDays: 10))["master_brewer"].Met, "Ten-day master wine failed");
        Expect(Goals(new AchievementItem(1, "wine_bottle", true, MasterWine: true, WineDays: 10))["master_brewer"].Met, "Native self-brewed wine was rejected by unchanged bottle ID");
        Expect(!Goals(new AchievementItem(1, "wine_gloomberry", true, MasterWine: true, WineDays: 10))["master_brewer"].Met, "Wrong wine identity qualified");
        var tools = AchievementRules.Tools.Select((id, i) => new AchievementItem(i + 1, id, true)).ToArray();
        Expect(Goals(tools)["scavenger_toolset"].Met, "Whole tool set failed");
        Expect(!Goals(tools[..4])["scavenger_toolset"].Met, "Incomplete tool set qualified");
        Expect(!Goals(tools.Select(item => item with { Uid = 1 }).ToArray())["scavenger_toolset"].Met, "Duplicate UID qualified as five tools");
        var empty = AchievementState.Empty("run-a", 0);
        var history = empty;
        foreach (var tool in tools) history = AchievementRules.Latch(history, Goals(tool));
        Expect(!history.Completed.ContainsKey("scavenger_toolset"), "Historical tools were unioned");
        Expect(!Goals(new AchievementItem(1, "metal_scanner", true, ScannerChance: 99))["max_scav_scanner"].Met, "Unfinished scanner qualified");
        Expect(Goals(new AchievementItem(1, "metal_scanner", true, ScannerChance: 100))["max_scav_scanner"].Met, "Max scanner failed");
        foreach (int assets in new[] { 99999, 100000, 100001 })
            Expect(AchievementRules.Evaluate(new([], assets, 0, 0), false)["wealthy_store"].Met == (assets > 100000), "Wealth boundary failed");
        Expect(!AchievementRules.Evaluate(new([], 0, 999, 199.999), false)["store_attractiveness"].Met, "Attractiveness below threshold qualified");
        var business = AchievementRules.Evaluate(new([], 0, 1000, 200), false);
        Expect(business["store_attractiveness"].Met && business["faction_max_rep"].Met, "Business boundaries failed");
        Expect(!AchievementRules.Evaluate(new([], 0, 0, 199.999), false)["faction_max_rep"].Met, "Rounded reputation qualified");
        Expect(!Goals()[AchievementRules.VictoryNode].Met, "A store that is not owned qualified");
        var ownership = AchievementRules.Evaluate(new([], 0, 0, 0), storeOwned: true);
        Expect(AchievementRules.Latch(empty, ownership).Completed.Keys.SequenceEqual([AchievementRules.VictoryNode]),
            "Store ownership depended on other goals");
        var state = AchievementRules.Latch(empty, Goals(new AchievementItem(5, "rat", true, LiveRat: true, RatGrams: 800)));
        Expect(state.Completed.ContainsKey("rat_800g") && state.Claimed.Count == 0, "Completion was also a claim");
        Expect(AchievementRules.Latch(state, Goals()).Completed.ContainsKey("rat_800g"), "Proof sale revoked completion");
        string json = AchievementRules.Encode(state);
        Expect(AchievementRules.Decode(json, "run-a", 0).Completed.ContainsKey("rat_800g"), "Completion did not round-trip");
        Reject(() => AchievementRules.Decode(json, "run-b", 0), "Cross-run state accepted");
        Reject(() => AchievementRules.Decode(json, "run-a", 1), "Cross-slot state accepted");
        Reject(() => AchievementRules.Decode(json.Replace("\"Schema\":1", "\"Schema\":2"), "run-a", 0), "Unknown schema accepted");
        Reject(() => AchievementRules.Decode(json.Replace("\"Schema\":1", "\"Schema\":1,\"Schema\":1"), "run-a", 0), "Duplicate state key accepted");
        var manifest = new PendingReward("rat_800g", Enumerable.Range(0, 10).Select(i => new RewardReceipt(i < 5 ? "serum_green" : "serum_red", 100 + i, 1)).ToArray());
        var pendingState = state with { Pending = manifest };
        string pendingJson = AchievementRules.Encode(pendingState);
        Expect(AchievementRules.Decode(pendingJson, "run-a", 0).Pending != null, "Interrupted delivery not preserved");
        Expect(AchievementRules.ReceiptsMatch(manifest, manifest.Items), "Complete delivery not recognized");
        Expect(!AchievementRules.ReceiptsMatch(manifest, manifest.Items[..9]), "Partial delivery recognized as complete");
        Expect(!AchievementRules.ReceiptsMatch(manifest, manifest.Items.Concat(manifest.Items[..1])), "Duplicate delivery UID accepted");
        Expect(!AchievementRules.ReceiptsMatch(manifest, manifest.Items.Select(x => x with { Units = 2 })), "Altered units accepted");
        Reject(() => AchievementRules.Decode(AchievementRules.Encode(state with { Pending = new("rat_800g", []) }), "run-a", 0), "Empty item reward receipt accepted");
        Reject(() => AchievementRules.Decode(AchievementRules.Encode(state with { Pending = manifest with { Items = manifest.Items.Select(x => x with { ItemId = "serum_green" }).ToArray() } }), "run-a", 0), "Wrong reward bundle accepted");
        Reject(() => AchievementRules.Decode(AchievementRules.Encode(state with { Pending = manifest with { Items = [manifest.Items[0], manifest.Items[0]] } }), "run-a", 0), "Duplicate pending reward UID accepted");
        var claimed = AchievementRules.Claim(pendingState);
        Expect(claimed.Claimed.SetEquals(new[] { "rat_800g" }) && claimed.Pending == null, "Recovery did not finalize exactly one claim");
        Reject(() => AchievementRules.Claim(claimed), "Repeat claim accepted");
        Reject(() => AchievementRules.Claim(empty with { Pending = new("workshop_master", []) }), "Uncompleted goal claimed");
        var victory = AchievementRules.RecordStoreOwnership(empty);
        Expect(victory.Completed.Keys.SequenceEqual([AchievementRules.VictoryNode]), "The store purchase must record only its own achievement");
        Expect(ReferenceEquals(victory, AchievementRules.RecordStoreOwnership(victory)), "Repeated ownership observation must preserve the original proof");
        Expect(AchievementRules.RecordStoreOwnership(state).Completed.ContainsKey("rat_800g"), "Store ownership must retain existing completions");
        Expect(!AchievementRules.HasVictoryBudgetReward(null) && !AchievementRules.HasVictoryBudgetReward(empty),
            "Victory budget activated without this run's claimed reward");
        Expect(!AchievementRules.HasVictoryBudgetReward(victory) &&
            !AchievementRules.HasVictoryBudgetReward(victory with { Pending = new("workshop_master", []) }),
            "Completion or pending delivery activated the victory budget");
        var badge = AchievementRules.Claim(victory with { Pending = new("workshop_master", []) });
        Expect(badge.Claimed.Contains("workshop_master"), "Inventory-free victory badge failed");
        Expect(AchievementRules.HasVictoryBudgetReward(badge), "Claimed victory did not activate its budget reward");
        Expect(AchievementRules.HasVictoryBudgetReward(AchievementRules.Decode(AchievementRules.Encode(badge), "run-a", 0)),
            "Victory claim did not preserve the budget reward across reload");
        Console.WriteLine($"Achievement checks passed: {_checks}");
    }

}
