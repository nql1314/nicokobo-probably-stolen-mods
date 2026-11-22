using System.Reflection;
using Nicokobo.ContentInterop;

internal static class Program
{
    private static int _checks, _failures;
    internal static void Expect(bool value, string reason)
    { _checks++; if (!value) throw new InvalidOperationException(reason); }
    internal static void Reject(Action action, string reason)
    {
        _checks++;
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or Newtonsoft.Json.JsonException) { return; }
        throw new InvalidOperationException(reason);
    }
    private static void Run(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static int Main(string[] args)
    {
        if (args.Contains("--measure")) { LiquidChecks.Measure(); return 0; }
        NativeWorkshop.Install();
        Run("opening the workshop recognizes an already-owned store", StoreOwnership);
        Run("load duplicate slots preserve completed/claimed state", Duplicate);
        Run("pending migration waits for inventory recovery without replacement", Pending);
        Run("cross-slot outside synchronous Normal remains rejected", OrdinaryContexts);
        Run("invalid loaded records remain unchanged", InvalidRecords);
        Run("loaded identity changes reject the migration", IdentityChanges);
        Run("liquid ledger value and invalidation regressions", LiquidChecks.Run);
        NativeWorkshop.Uninstall();
        Console.WriteLine($"Forge native-domain source-linked checks: {_checks}; failures: {_failures}");
        return _failures == 0 ? 0 : 1;
    }
    private static AchievementState Progress(int slot)
    {
        var state = AchievementState.Empty("run-a", slot);
        state.Completed["pure_water_jug"] = new("full", [11]);
        state.Completed["rat_800g"] = new("800g", [12]);
        state.Completed[AchievementRules.VictoryNode] = new("buyout", []);
        state.Claimed.UnionWith(["pure_water_jug", AchievementRules.VictoryNode]);
        return state;
    }
    private static PlayerStore Store(string json, int slot = 5, string run = "run-a")
    {
        ModHook.Init();
        var store = PlayerStore.Instance = new() { runID = run, saveSlotId = slot };
        store.modData.Add(AchievementRules.SaveKey, json);
        EmporiumEntry.Instance = new();
        DirectoryMaster.Created = 0;
        return store;
    }
    private static string Json(PlayerStore store) => store.modData[AchievementRules.SaveKey];
    private static AchievementState State(PlayerStore store) => AchievementRules.Decode(Json(store), store.runID, store.saveSlotId);
    private static void Invoke(string name) => typeof(NativeWorkshop).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
    private static void StoreOwnership()
    {
        var store = Store(AchievementRules.Encode(AchievementState.Empty("run-a", 5)));
        store.IsPropertyPaid = true;
        ModHook.Normal();
        OfficialWorkshopApi.Opened();

        var state = State(store);
        Expect(state.Completed.Keys.SequenceEqual([AchievementRules.VictoryNode]),
            "Opening the workshop did not record the already-owned store without an ending event");
        Expect(state.Claimed.Count == 0, "Recognizing the already-owned store also claimed its reward");

        var assets = new LoadedMod { Manifest = new() { ID = NativeWorkshop.OwnerId } };
        assets.Localization["ko"] = new() { ["forge.advanced_water_filter"] = "고급 정수기" };
        ModLoader.Mods.Add(assets);
        LocHelper.LocaleCode = "ko-KR";
        try
        {
            var entry = OfficialWorkshopApi.Snapshot()!.Entries.Single(value => value.Id == "pure_water_jug");
            Expect(entry.Rewards.Single().DisplayName == "고급 정수기",
                "The workshop reward label must read the loaded Korean table for ko-KR");
        }
        finally
        {
            LocHelper.LocaleCode = "en";
            ModLoader.Mods.Remove(assets);
        }
    }
    private static void Duplicate()
    {
        var before = Progress(2);
        string source = AchievementRules.Encode(before);
        foreach (int slot in new[] { 5, 7 })
        {
            var store = Store(source, slot);
            ModHook.Normal();
            var state = State(store);
            Expect(state.SlotId == slot && state.RunId == before.RunId, "Loaded duplicate was not rebound to selected slot");
            Expect(Json(store) == AchievementRules.Encode(before with { SlotId = slot }), "Migration changed more than its SlotId");
            Expect(state.Claimed.SetEquals(before.Claimed) && state.Completed.Count == before.Completed.Count, "Progress was lost");
            Expect(NativeVictoryBudget.Active && OfficialWorkshopApi.Snapshot() != null, "Claimed reward or page remained unbound");
            int writes = store.modData.Writes;
            ModHook.Normal(); OfficialWorkshopApi.Opened(); OfficialWorkshopApi.Claim("pure_water_jug");
            Expect(store.modData.Writes == writes && DirectoryMaster.Created == 0, "Repeated load/open reissued a claimed reward");
        }
        Expect(AchievementRules.Encode(before) == source, "Source-slot payload was modified");
        var original = Store(source, 2);
        int originalWrites = original.modData.Writes;
        ModHook.Normal();
        Expect(Json(original) == source && original.modData.Writes == originalWrites, "Same-slot load rewrote its historical state");
        Reject(() => AchievementRules.Decode(source, "run-a", 5), "Strict Decode accepted cross-slot state");
    }
    private static void Pending()
    {
        var receipts = Enumerable.Range(0, 10).Select(i => new RewardReceipt(i < 5 ? "serum_green" : "serum_red", 200 + i, 1)).ToArray();
        var before = Progress(2) with { Pending = new("rat_800g", receipts) };
        var store = Store(AchievementRules.Encode(before));
        // Normal runs before actual inventory restoration. Even pre-existing
        // matching objects must not trigger Recover from the load callback.
        store.Items = receipts.Select(item => new GameItem { identifier = item.ItemId, uniqueId = item.Uid }).ToList();
        ModHook.Normal();
        var migrated = State(store);
        Expect(migrated.Pending != null && migrated.Pending.Items.SequenceEqual(receipts), "Pending receipt changed during migration");
        Expect(!NativeVictoryBudget.Active && DirectoryMaster.Created == 0, "Unconfirmed load accepted or redelivered a pending reward");
        store.Items.RemoveAt(9);
        OfficialWorkshopApi.Opened();
        Expect(State(store).Pending != null && DirectoryMaster.Created == 0, "Missing pending item was replaced or finalized");
        store.Items.Add(new() { identifier = receipts[9].ItemId, uniqueId = receipts[9].Uid });
        OfficialWorkshopApi.Opened();
        Expect(State(store).Pending == null && State(store).Claimed.Contains("rat_800g"), "Complete original receipt was not recovered");
        Expect(NativeVictoryBudget.Active, "Confirmed recovered victory claim did not reactivate budget");
        string final = Json(store); int writes = store.modData.Writes;
        OfficialWorkshopApi.Opened(); OfficialWorkshopApi.Claim("rat_800g");
        Expect(Json(store) == final && store.modData.Writes == writes && DirectoryMaster.Created == 0, "Recovery was not idempotent");
    }
    private static void OrdinaryContexts()
    {
        string source = AchievementRules.Encode(Progress(2));
        foreach (string context in new[] { "Opened", "Update", "InitialSave", "DeferredNormal" })
        {
            var store = Store(source);
            if (context == "Opened") OfficialWorkshopApi.Opened();
            if (context == "Update") NativeWorkshop.Update();
            if (context == "InitialSave") { Invoke("InitialPostfix"); NativeWorkshop.Update(); }
            if (context == "DeferredNormal")
            {
                EmporiumEntry.Instance!.invElement = null; ModHook.Normal();
                EmporiumEntry.Instance.invElement = new(); NativeWorkshop.Update();
            }
            Expect(Json(store) == source && store.modData.Writes == 1, context + " borrowed loaded-slot migration permission");
            Expect(OfficialWorkshopApi.Snapshot() == null, context + " accepted cross-slot state");
        }
    }
    private static void InvalidRecords()
    {
        string source = AchievementRules.Encode(Progress(2));
        var invalid = new Dictionary<string, string>
        {
            ["run"] = source.Replace("run-a", "run-b"),
            ["schema"] = source.Replace("\"Schema\":1", "\"Schema\":2"),
            ["duplicate key"] = source.Replace("\"Schema\":1", "\"Schema\":1,\"Schema\":1"),
            ["claimed without completion"] = source.Replace("\"Claimed\":[", "\"Claimed\":[\"master_brewer\","),
            ["pending malformed receipt"] = AchievementRules.Encode(Progress(2) with { Pending = new("rat_800g", []) }),
            ["pending already claimed"] = AchievementRules.Encode(Progress(2) with { Pending = new("pure_water_jug", [new("water_filter_adv", 111, 1)]) }),
            ["negative source slot"] = source.Replace("\"SlotId\":2", "\"SlotId\":-1"),
            ["missing source slot"] = source.Replace("\"SlotId\":2,", ""),
            ["malformed"] = "{"
        };
        foreach (var (name, json) in invalid)
        {
            var store = Store(json); ModHook.Normal();
            Expect(Json(store) == json && store.modData.Writes == 1, name + " was overwritten");
            Expect(OfficialWorkshopApi.Snapshot() == null && !NativeVictoryBudget.Active, name + " was accepted");
        }
    }
    private static void IdentityChanges()
    {
        string source = AchievementRules.Encode(Progress(2));
        foreach (string change in new[] { "store", "run", "data", "revision" })
        {
            var store = Store(source);
            var data = store.modData;
            int reads = 0;
            data.BeforeRead = _ =>
            {
                if (change == "revision" && ++reads < 2) return;
                data.BeforeRead = null;
                switch (change)
                {
                    case "store": PlayerStore.Instance = new() { saveSlotId = 5 }; break;
                    case "run": store.runID = "run-changed"; break;
                    case "data": store.modData = new(); store.modData.Add(AchievementRules.SaveKey, source); break;
                    case "revision": data[AchievementRules.SaveKey] = source + " "; break;
                }
            };
            ModHook.Normal();
            Expect(data[AchievementRules.SaveKey] == (change == "revision" ? source + " " : source), change + " changed the loaded source data");
            Expect(OfficialWorkshopApi.Snapshot() == null && !NativeVictoryBudget.Active, change + " accepted a different load context");
        }
    }
}
