
using System.Text.Json;
using System.Text.Json.Nodes;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

internal static class WorkshopAchievementChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception("Workshop: " + message);
        }
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (Exception ex) when (ex is InvalidOperationException or JsonException or AugMechanicalAscension.Serialization.JsonException) { rejected = true; }
            Check(rejected, message);
        }
        var catalog = WorkshopAchievements.Definitions;
        string runId = Guid.NewGuid().ToString("D");
        var empty = new WorkshopAchievementState { RunId = runId, SlotId = 7 };
        Check(catalog.Length == 13 && catalog.Select(x => x.Id).Distinct().Count() == 13 &&
            catalog.Where(x => x.ProofItemId != null).Select(x => x.ProofItemId).Distinct().Count() == 12,
            "twelve distinct real products and one final goal");
        Check(catalog.Count(x => x.ProofItemId?.StartsWith("nicokobo.mechcore.synthesis.") == true) == 6,
            "Synthesis owns exactly six proof products");
        string[] expectedRewards = ["metal_scanner:1", "energy_credit_ext:1", "welder:1",
            "turbo_booster_adv:1", "energy_credit_breeder:1",
            "nicokobo.mechcore_protocol.module_matrix.performance_conversion_node:1",
            "nicokobo.mechcore_protocol.module_matrix.efficiency_conversion_node:1",
            "nicokobo.mechcore_protocol.module_matrix.quality_conversion_node:1",
            "surgery_tool:1", "surgery_tool:1",
            NeuralModuleRules.ItemId + ":1", NeuralModuleRules.ItemId + ":1",
            NeuralModuleRules.ItemId + ":1+system_uncapped_neural_core:1"];
        Check(catalog.Select(x => string.Join("+", x.Rewards.Select(r => r.ItemId + ":" + r.Count))).SequenceEqual(expectedRewards),
            "requested per-node rewards, including the complete mixed final bundle");
        Check(catalog.Single(x => x.Id == CyberneticRules.MimeticSkin).Prerequisite == CyberneticRules.SyntheticVocalCords &&
            catalog.Single(x => x.Id == CyberneticRules.AiAssistantCore).Prerequisite == CyberneticRules.MimeticSkin &&
            catalog.Single(x => x.Id == CyberneticRules.ArtificialHeart).Prerequisite == CyberneticRules.AiAssistantCore,
            "latest identity branch order");
        var items = catalog.Where(x => x.ProofItemId != null)
            .Select((x, i) => new WorkshopOwnedItem(x.ProofItemId!, 100 + i, 1, true)).ToArray();
        Check(WorkshopAchievements.Latch(empty, items.Select(x => x with { Owned = false }), 2).Completed.Count == 0 &&
            WorkshopAchievements.Latch(empty, items.Select(x => x with { Units = 0 }), 2).Completed.Count == 0 &&
            WorkshopAchievements.Latch(empty, items.Select(x => x with { UniqueId = 0 }), 2).Completed.Count == 0,
            "customer items, zero units and invalid identities are not proof");
        var complete = WorkshopAchievements.Latch(empty, items.Concat(items), 2);
        Check(complete.Completed.Count == 12 && empty.Completed.Count == 0,
            "deduplicated ownership latches without mutating the previous state");
        var consumed = WorkshopAchievements.Latch(complete, [], 3);
        Check(consumed.Completed.Count == 12 && consumed.Completed.Values.All(x => x.Day == 2), "proof survives consuming or selling products");
        Check(!WorkshopAchievements.CanClaim(complete, WorkshopAchievements.AscensionId) &&
            !WorkshopAchievements.CanClaim(complete, CyberneticRules.ArtificialHeart) &&
            WorkshopAchievements.CanClaim(complete, CyberneticRules.InfraredVision) &&
            WorkshopAchievements.CanClaim(complete, CyberneticRules.TitaniumSkeleton) &&
            !WorkshopAchievements.CanClaim(empty, CyberneticRules.InfraredVision), "completion and prerequisite claims are independent");
        Check(!WorkshopAchievements.IsMechanicallyAscended(null, runId, 7) &&
            !WorkshopAchievements.IsMechanicallyAscended(complete, runId, 7),
            "owning all twelve products does not enable the final survival exemption");
        var debugUnlocked = WorkshopAchievements.UnlockAscensionForDebug(empty);
        var debugRestored = WorkshopAchievements.Decode(WorkshopAchievements.Encode(debugUnlocked), runId, 7);
        Check(WorkshopAchievements.IsMechanicallyAscended(debugRestored, runId, 7) &&
            WorkshopAchievements.Completed(debugRestored, WorkshopAchievements.AscensionId) &&
            !WorkshopAchievements.IsMechanicallyAscended(debugRestored, Guid.NewGuid().ToString("D"), 7) &&
            !WorkshopAchievements.IsMechanicallyAscended(debugRestored, runId, 8),
            "debug final unlock survives serialization and respects run/slot identity");
        Check(debugRestored.Completed.Count == 0 && debugRestored.Claimed.Length == 0 &&
            debugRestored.Pending == null && WorkshopAchievements.DeliveredNeuralInterfaces(debugRestored) == 0 &&
            !WorkshopAchievements.CanClaim(debugRestored, WorkshopAchievements.AscensionId) &&
            !empty.DebugAscensionUnlocked,
            "debug unlock requires no item proof, creates no reward receipt and prevents a final reward grant");
        Check(!WorkshopAchievements.Decode(WorkshopAchievements.Encode(empty)
                .Replace(",\"debugAscensionUnlocked\":false", ""), runId, 7).DebugAscensionUnlocked,
            "existing achievement records without the debug field retain normal behavior");
        var pendingDebug = WorkshopAchievements.Prepare(complete, CyberneticRules.InfraredVision, 0,
            [new("metal_scanner", 901, 1)]);
        Reject(() => WorkshopAchievements.UnlockAscensionForDebug(pendingDebug),
            "debug unlock cannot discard an unconfirmed reward delivery");
        Reject(() => WorkshopAchievements.Encode(debugRestored with { Claimed = [WorkshopAchievements.AscensionId] }),
            "debug unlock cannot masquerade as a real final reward claim");
        var ambiguous = WorkshopAchievements.Latch(empty,
            [items[0], items[1] with { UniqueId = items[0].UniqueId }], 2);
        Check(ambiguous.Completed.Count == 0, "one UID cannot prove two different products");
        var state = complete;
        int uid = 1000;
        int neuralRewards = 0;
        Check(WorkshopAchievements.DeliveredNeuralInterfaces(complete) == 0 &&
            NeuralModuleRules.UnitValue(1, 0) == 200,
            "completion without a reward claim does not change interface pricing");
        foreach (var definition in catalog)
        {
            Check(WorkshopAchievements.CanClaim(state, definition.Id), "planned branch order becomes claimable: " + definition.Id);
            if (definition.Id == WorkshopAchievements.AscensionId)
                Check(!WorkshopAchievements.IsMechanicallyAscended(state, runId, 7),
                    "twelve claimed cybernetics still require claiming Mechanical Ascension");
            var receipts = definition.Rewards.SelectMany(reward => Enumerable.Range(0, reward.Count).Select(_ =>
                new WorkshopRewardReceipt(reward.ItemId, uid++, 1,
                    WorkshopAchievements.IsBattery(reward.ItemId) ? 250 : null))).ToArray();
            var prepared = WorkshopAchievements.Prepare(state, definition.Id, 0, receipts);
            Check(prepared.Pending!.CashBefore == 0 && !WorkshopAchievements.CanClaim(prepared, definition.Id), "zero-credit delivery blocks duplicate claims");
            var loaded = WorkshopAchievements.Decode(WorkshopAchievements.Encode(prepared), runId, 7);
            Check(loaded.Pending!.Items.Select(x => x.UniqueId).SequenceEqual(receipts.Select(x => x.UniqueId)), "pending reward identities survive reload");
            neuralRewards += definition.Rewards.Where(reward => reward.ItemId == NeuralModuleRules.ItemId).Sum(reward => reward.Count);
            Check(WorkshopAchievements.DeliveredNeuralInterfaces(loaded) == neuralRewards &&
                NeuralModuleRules.UnitValue(1 + neuralRewards, WorkshopAchievements.DeliveredNeuralInterfaces(loaded)) == 200 &&
                NeuralModuleRules.UnitValue(2 + neuralRewards, WorkshopAchievements.DeliveredNeuralInterfaces(loaded)) == 400,
                "delivered rewards awaiting commit do not advance either shop price: " + definition.Id);
            if (definition.Id == WorkshopAchievements.AscensionId)
                Check(!WorkshopAchievements.IsMechanicallyAscended(loaded, runId, 7),
                    "pending final reward delivery does not enable Mechanical Ascension");
            state = WorkshopAchievements.Commit(loaded);
            Check(state.Pending == null && state.Claimed.Contains(definition.Id) &&
                !WorkshopAchievements.CanClaim(state, definition.Id), "committed reward cannot be claimed again");
            Check(WorkshopAchievements.DeliveredNeuralInterfaces(state) == neuralRewards &&
                NeuralModuleRules.UnitValue(1 + neuralRewards, WorkshopAchievements.DeliveredNeuralInterfaces(state)) == 200,
                "committing a reward preserves the interface price: " + definition.Id);
        }
        Check(state.Claimed.Length == 13 && WorkshopAchievements.ClaimedCybernetics(state) == 12 &&
            WorkshopAchievements.Completed(state, WorkshopAchievements.AscensionId), "final achievement requires all twelve claims");
        var ascended = WorkshopAchievements.Decode(WorkshopAchievements.Encode(state), runId, 7);
        Check(WorkshopAchievements.DeliveredNeuralInterfaces(ascended) == 3 &&
            NeuralModuleRules.UnitValue(4, WorkshopAchievements.DeliveredNeuralInterfaces(ascended)) == 200 &&
            NeuralModuleRules.UnitValue(5, WorkshopAchievements.DeliveredNeuralInterfaces(ascended)) == 400 &&
            NeuralModuleRules.BaseStats(5) == (40, 40, 20),
            "new saved claims exclude three interface rewards from prices while retaining their stat growth");
        var historical = JsonNode.Parse(WorkshopAchievements.Encode(ascended))!.AsObject();
        historical.Remove("grantedNeuralInterfaces");
        var oldClaims = WorkshopAchievements.Decode(historical.ToJsonString(), runId, 7);
        Check(oldClaims.Completed.Count == 12 && oldClaims.Claimed.Length == 13 &&
            WorkshopAchievements.DeliveredNeuralInterfaces(oldClaims) == 4,
            "old completions/claims and four previously granted interfaces survive the catalog change");
        var beforeFinal = ascended with { Claimed = ascended.Claimed.Where(id => id != WorkshopAchievements.AscensionId).ToArray(), GrantedNeuralInterfaces = 2 };
        Reject(() => WorkshopAchievements.Prepare(beforeFinal, WorkshopAchievements.AscensionId, 0,
            [new(NeuralModuleRules.ItemId, 5000, 1)]), "final reward cannot omit the uncapped core");
        Reject(() => WorkshopAchievements.Prepare(beforeFinal, WorkshopAchievements.AscensionId, 0,
            [new("system_uncapped_neural_core", 5001, 1)]), "final reward cannot omit the interface");
        Reject(() => WorkshopAchievements.Prepare(beforeFinal, WorkshopAchievements.AscensionId, 0,
            [new(NeuralModuleRules.ItemId, 5000, 1), new("system_uncapped_neural_core", 5000, 1)]),
            "mixed reward items must not share an identity");
        var oldPending = beforeFinal with { GrantedNeuralInterfaces = null, Pending = new(WorkshopAchievements.AscensionId, 0,
            [new(NeuralModuleRules.ItemId, 5000, 1), new(NeuralModuleRules.ItemId, 5001, 1)]) };
        var recovered = WorkshopAchievements.Commit(WorkshopAchievements.Decode(WorkshopAchievements.Encode(oldPending), runId, 7));
        Check(recovered.Claimed.Length == 13 && WorkshopAchievements.DeliveredNeuralInterfaces(recovered) == 4,
            "an existing final delivery keeps its original receipt instead of issuing new catalog rewards");
        Check(WorkshopAchievements.IsMechanicallyAscended(ascended, runId, 7) &&
            WorkshopAchievements.IsMechanicallyAscended(WorkshopAchievements.Latch(ascended, [], 3), runId, 7) &&
            !WorkshopAchievements.IsMechanicallyAscended(ascended, Guid.NewGuid().ToString("D"), 7) &&
            !WorkshopAchievements.IsMechanicallyAscended(ascended, runId, 8),
            "saved final claim survives reload and losing items without leaking to another run or slot");
        foreach (var missing in catalog.Where(x => x.ProofItemId != null))
            Check(!WorkshopAchievements.Completed(state with { Claimed = state.Claimed.Where(x => x != missing.Id).ToArray() },
                WorkshopAchievements.AscensionId), "final goal rejects each missing node: " + missing.Id);
        var root = catalog[0];
        Reject(() => WorkshopAchievements.Prepare(complete, root.Id, 0, []), "empty reward manifest rejected");
        Reject(() => WorkshopAchievements.Prepare(complete, root.Id, 0,
            [new(root.Rewards[0].ItemId, 3000, 1), new(root.Rewards[0].ItemId, 3000, 1)]), "duplicate reward UID rejected");
        Reject(() => WorkshopAchievements.Prepare(complete, root.Id, 0,
            [new("common_medical", 3000, 1), new("common_medical", 3001, 1)]), "wrong reward rejected");
        Reject(() => WorkshopAchievements.Prepare(complete, CyberneticRules.ArtificialHeart, 0,
            [new(NeuralModuleRules.ItemId, 3000, 1)]), "claim cannot bypass prerequisite");
        Reject(() => WorkshopAchievements.Decode(WorkshopAchievements.Encode(state), Guid.NewGuid().ToString(), 7), "cross-run state rejected");
        Reject(() => WorkshopAchievements.Decode(WorkshopAchievements.Encode(state), runId, 8), "cross-slot state rejected");
        Reject(() => WorkshopAchievements.Decode(WorkshopAchievements.Encode(empty).Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), runId, 7), "duplicate JSON key rejected");
        Reject(() => WorkshopAchievements.Decode(WorkshopAchievements.Encode(empty).Replace("\"schemaVersion\":1,", ""), runId, 7), "missing schema is not treated as a new achievement record");
        Reject(() => WorkshopAchievements.Encode(state with { Claimed = [.. state.Claimed, root.Id] }), "duplicate claimed node rejected");
        Reject(() => WorkshopAchievements.Encode(empty with { Claimed = [WorkshopAchievements.AscensionId] }), "forged final claim rejected");
        checks += SavedRewardChecks(runId);
        return checks;
    }

    private static int SavedRewardChecks(string runId)
    {
        int checks = 0;
        var directory = System.IO.Directory.CreateTempSubdirectory("aug-reward-check-");
        try
        {
            string rootJson = "{}", achievementJson = WorkshopAchievements.Encode(new() { RunId = runId, SlotId = 7 });
            string[] names = ["IS_OWNED_TAG", "power_source_item", "power_source_item_energy", "power_source_item_max_energy"];
            var battery = new JsonObject
            {
                ["identifier"] = WorkshopAchievements.BatteryId, ["uniqueId"] = 901, ["unitCount"] = 1,
                ["childItems"] = new JsonArray(),
                ["_keys"] = new JsonArray(names.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["_values"] = new JsonArray(names.Select(name => (JsonNode?)new JsonObject
                { ["<identifier>k__BackingField"] = name, ["valueEnabled"] = true,
                    ["valueInt"] = name.Contains("energy") ? 250 : 0 }).ToArray())
            };
            void Check(SlotReadbackStatus expected, string message, JsonObject? item = null, string inventoryField = "mainInvJSON", int cash = 0,
                string? json = null, bool reachable = true)
            {
                checks++;
                var inventory = new JsonObject { ["saveItems"] = new JsonArray(
                    new JsonObject { ["childItems"] = reachable ? new JsonArray(1) : new JsonArray() }, (item ?? battery).DeepClone()) };
                var store = new JsonObject { ["runID"] = runId, ["saveSlotId"] = 7, ["startType"] = 55, ["playerCash"] = cash,
                    ["rentValue"] = 250, ["storeReputations"] = new JsonArray(new JsonObject { ["factionId"] = "FACTION_SECURITY", ["amount"] = -15 }),
                    ["modData"] = new JsonObject { [RunState.SaveKey] = rootJson, [WorkshopAchievements.SaveKey] = json ?? achievementJson },
                    ["mainInvJSON"] = inventoryField == "mainInvJSON" ? inventory.ToJsonString() :
                        new JsonObject { ["saveItems"] = new JsonArray(new JsonObject { ["childItems"] = new JsonArray() }) }.ToJsonString() };
                if (inventoryField != "mainInvJSON") store[inventoryField] = inventory.ToJsonString();
                var fixture = new JsonObject { ["playerStore"] = new JsonObject
                    { ["__type"] = "PlayerStore,Assembly-CSharp", ["value"] = store },
                    ["storeStation"] = new JsonObject { ["__type"] = "StoreStation,Assembly-CSharp", ["value"] = new JsonObject { ["dayCounter"] = 2 } } };
                File.WriteAllText(Path.Combine(directory.FullName, "save_7.es3"), fixture.ToJsonString());
                var result = Es3SlotReadback.Compare(directory.FullName, 7, runId, rootJson,
                    new NativeSaveSnapshot(Cash: 0, StartType: 55,
                        ExtraModData: new Dictionary<string, string> { [WorkshopAchievements.SaveKey] = achievementJson },
                        OwnedItems: [new(WorkshopAchievements.BatteryId, 901, true, 1, NativeItemSearchScope.AllSavedInventories,
                            FullEnergy: 250, RequirePlayerOwned: true)]));
                if (result.Status != expected) throw new Exception("Saved reward: " + message + "; actual=" + result.Status);
            }
            Check(SlotReadbackStatus.StateMatched, "full battery and matching achievement state");
            Check(SlotReadbackStatus.StateMatched, "owned backroom reward remains recoverable", inventoryField: "backInvJSON");
            Check(SlotReadbackStatus.NativeInventoryMismatch, "sold reward is not owned", inventoryField: "soldInvJSON");
            Check(SlotReadbackStatus.NativeEconomyMismatch, "cash changed", cash: 1);
            Check(SlotReadbackStatus.NativeModDataMismatch, "achievement state changed", json: "{}");
            Check(SlotReadbackStatus.NativeInventoryMismatch, "detached save entry", reachable: false);
            var drained = (JsonObject)battery.DeepClone();
            drained["_values"]![2]!["valueInt"] = 249;
            Check(SlotReadbackStatus.NativeInventoryMismatch, "battery is not full", drained);
            var unowned = (JsonObject)battery.DeepClone();
            unowned["_values"]![0]!["valueEnabled"] = false;
            Check(SlotReadbackStatus.NativeInventoryMismatch, "customer ownership", unowned);
            var duplicate = (JsonObject)battery.DeepClone();
            ((JsonArray)duplicate["_keys"]!).Add("power_source_item_energy");
            ((JsonArray)duplicate["_values"]!).Add(battery["_values"]![2]!.DeepClone());
            Check(SlotReadbackStatus.NativeInventoryMismatch, "ambiguous battery state", duplicate);
        }
        finally { directory.Delete(true); }
        return checks;
    }
}
