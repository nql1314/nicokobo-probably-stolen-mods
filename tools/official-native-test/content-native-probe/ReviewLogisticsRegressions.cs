using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const string ReviewPerfumeId = "neuroactive_perfume";
    private const string ReviewLogisticsOwner = "nicokobo.automated_logistics";
    private static readonly string[] ReviewRootTypes = { ReviewLogisticsOwner + ".void_container", ReviewLogisticsOwner + ".void_tank", ReviewLogisticsOwner + ".void_grid" };
    private static readonly string[] ReviewRootFields = { "_resourceRoot", "_waterRoot", "_powerRoot" };

    internal IEnumerable<object?> ReviewLogisticsRegressions()
    {
        PrepareLogisticsNetworks();
        foreach (object? wait in ReviewPerfumeRestocking()) yield return wait;
        foreach (object? wait in ReviewNetworkRootReplacement()) yield return wait;
    }

    private static JObject ReviewPerfumeState(GameItem item) => new()
    {
        ["item"] = ItemState(item), ["useTag"] = item.IsTag(UseCountHelper.USE_COUNT_ITEM_TAG),
        ["maximum"] = item.GetTagReadonly(UseCountHelper.MAX_USE_COUNT_INT).GetInt(),
        ["current"] = item.GetTagReadonly(UseCountHelper.CURRENT_USE_COUNT_INT).GetInt(),
        ["purity"] = item.GetTagReadonly("INGOT_PURITY_TAG").GetString(),
        ["gradeEnabled"] = item.IsTag("nicokobo.mechcore.synthesis.component_grade"),
        ["grade"] = item.GetTagReadonly("nicokobo.mechcore.synthesis.component_grade").GetInt()
    };

    private JObject? ReviewPerfumeDebt(int machineUid) => LogisticsState["Machines"]?[machineUid.ToString()]?["PendingItems"]?
        .Children<JObject>().SingleOrDefault(item => (string?)item["ItemId"] == ReviewPerfumeId);

    private void ReviewFillPerfume(RecipeFixture fixture)
    {
        Reflect.Call(Logistics, "ConfigureMachine", fixture.Machine, true, false);
        try { Reflect.Call(Logistics, "FillOnce", fixture.Machine); }
        finally { Reflect.Call(Logistics, "ConfigureMachine", fixture.Machine, false, false); }
    }

    private RecipeFixture ReviewConsumePerfume(object view, string name)
    {
        var fixture = CreateRecipeFixture(view, retain: true);
        Retain(fixture.Machine);
        Place(fixture.Modules ?? throw new FixtureUnavailable("K05 has no real module bay"),
            Spawn(ReviewLogisticsOwner + ".auto_feeder"), "R05-feeder-" + name, fixture.Machine);
        GameItem consumed = fixture.Input.childItems.Single(item => item.identifier == ReviewPerfumeId);
        JObject before = ReviewPerfumeState(consumed); int consumedUid = consumed.uniqueId;
        R.Check("R05.native-full-perfume-fixture." + name, new { useTag = true, maximum = 5, current = 5 }, before,
            consumed.IsTag(UseCountHelper.USE_COUNT_ITEM_TAG) && consumed.GetTagReadonly(UseCountHelper.MAX_USE_COUNT_INT).GetInt() == 5 &&
                consumed.GetTagReadonly(UseCountHelper.CURRENT_USE_COUNT_INT).GetInt() == 5,
            "Current native factory fixture; a changed native default requires an explicit scenario update");
        Reflect.Call(Logistics, "ConfigureMachine", fixture.Machine, true, false);
        try { ObserveBatch(fixture, "R05-mimetic-skin-" + name, true); }
        finally { Reflect.Call(Logistics, "ConfigureMachine", fixture.Machine, false, false); }
        JObject? debt = ReviewPerfumeDebt(fixture.Machine.uniqueId);
        R.Check("R05.real-cycle-records-perfume-debt." + name, new { consumedUid, remaining = 1, targetCount = 1, purity = 0, quality = 0, nonemptyMaterialState = true }, debt,
            AllItems().All(item => item.uniqueId != consumedUid) && debt != null && (int?)debt["Remaining"] == 1 &&
                (int?)debt["TargetCount"] == 1 && (int?)debt["Purity"] == 0 && (int?)debt["Quality"] == 0 &&
                (bool?)debt["Manual"] == false && !string.IsNullOrEmpty((string?)debt["MaterialState"]),
            "Actual K05 onCycleEndSlotItemFunc -> installed Forge BeforeBatch/AfterBatch -> logistics consumption record; no fabricated new demand");
        if (debt == null) throw new InvalidOperationException("R05 actual K05 cycle did not produce the required perfume debt");
        return fixture;
    }

    private IEnumerable<object?> ReviewPerfumeRestocking()
    {
        R.phase = "R05-real-perfume-consumption-and-restock";
        object recipe = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", "nicokobo.forge.machines.universal_manufacturing_terminal"))
            .Single(view => Reflect.Str(view, "OutputItemId") == "nicokobo.aug.cybernetic.mimetic_skin");
        GameGridInventory resources = (GameGridInventory?)Reflect.Call(Logistics, "ResourceRegion")
            ?? throw new FixtureUnavailable("R05 resource network is unavailable");
        using var strict = ReviewConsumePerfume(recipe, "strict");
        var candidates = new List<GameItem>();
        GameItem Candidate(string label, Action<GameItem> arrange)
        {
            GameItem item = Spawn(ReviewPerfumeId); arrange(item);
            Place(resources, item, "R05-" + label); candidates.Add(item); return item;
        }
        Candidate("native-used-4-of-5", item => UseCountHelper.Use(item));
        Candidate("synthetic-empty-0-of-5", item => UseCountHelper.SetCurrentUseCount(item, 0));
        Candidate("synthetic-disabled-use-tag", item => item.DisableTag(UseCountHelper.USE_COUNT_ITEM_TAG));
        Candidate("synthetic-wrong-purity", item => item.ModifyTag("INGOT_PURITY_TAG", tag => tag.Enable().SetString("INGOT_PURITY_PERFECT")));
        Candidate("synthetic-wrong-grade", item => item.ModifyTag("nicokobo.mechcore.synthesis.component_grade", tag => tag.Enable().SetInt(1)));
        GameItem otherFull = Candidate("synthetic-full-6-of-6", item =>
        { UseCountHelper.ModMaxUseCount(item, 1); UseCountHelper.ReplenishUseCount(item, 1); });
        R.environment["R05FixtureProvenance"] = "Native factory 5/5 and native Use produces 4/5. Zero uses is deliberately set without invoking destroy-on-empty; missing tag, purity, grade and 6/6 are explicit boundary fixtures. They are not claimed to be naturally spawned perfume.";
        JArray rejectedBefore = new(candidates.Select(ReviewPerfumeState));
        JObject strictDebt = (JObject)ReviewPerfumeDebt(strict.Machine.uniqueId)!.DeepClone();
        ReviewFillPerfume(strict);
        R.Check("R05.new-debt-rejects-all-different-states", new { debt = strictDebt, candidates = rejectedBefore, perfumeInInput = 0 },
            new { debt = ReviewPerfumeDebt(strict.Machine.uniqueId), candidates = new JArray(candidates.Select(ReviewPerfumeState)), input = strict.Input.childItems.Select(ItemState).ToArray() },
            JToken.DeepEquals(strictDebt, ReviewPerfumeDebt(strict.Machine.uniqueId)) && JToken.DeepEquals(rejectedBefore, new JArray(candidates.Select(ReviewPerfumeState))) &&
                strict.Input.childItems.All(item => item.identifier != ReviewPerfumeId),
            "Actual FillOnce with real source inventories; 4/5, 0/5, disabled tag, wrong purity/grade and full 6/6 cannot repay a 5/5 debt");
        // Keep the different maximum outside every source until the legacy
        // scenario; legacy empty signatures intentionally accept any full bottle.
        if (otherFull.parentInventory == null || !otherFull.parentInventory.Expel(otherFull)) throw new InvalidOperationException("Cannot detach the R05 alternate-maximum fixture");
        GameItem exactFull = Candidate("native-full-5-of-5", _ => { });
        int exactUid = exactFull.uniqueId;
        ReviewFillPerfume(strict);
        R.Check("R05.new-debt-moves-only-exact-full-bottle", new { uid = exactUid, remaining = 0 },
            new { input = strict.Input.childItems.Where(item => item.identifier == ReviewPerfumeId).Select(ReviewPerfumeState).ToArray(), debt = ReviewPerfumeDebt(strict.Machine.uniqueId) },
            strict.Input.childItems.Any(item => item.uniqueId == exactUid && item.unitCount == 1) && ReviewPerfumeDebt(strict.Machine.uniqueId) == null,
            "Real source UID moved by FillOnce; the debt is repaid by the same native 5/5 state");

        using var pendingNew = ReviewConsumePerfume(recipe, "pending-for-reload");
        using var legacy = ReviewConsumePerfume(recipe, "legacy-seed");
        string currentJson = (string)Reflect.Call(Logistics, "StateJson")!;
        JObject legacyFixture = JObject.Parse(currentJson);
        JObject legacyDebt = legacyFixture["Machines"]![legacy.Machine.uniqueId.ToString()]!["PendingItems"]!.Children<JObject>()
            .Single(item => (string?)item["ItemId"] == ReviewPerfumeId);
        legacyDebt["MaterialState"] = ""; legacyDebt["Remaining"] = 3; legacyDebt["TargetCount"] = 3;
        object staged = Reflect.Call(Reflect.Type("Nicokobo.AutomatedLogistics.Native.NativeRunDataApi"), "Stage", Store,
            ReviewLogisticsOwner, ReviewLogisticsOwner + ".runtime_v6", Store.runID, Store.saveSlotId, currentJson, legacyFixture.ToString(Formatting.None))!;
        if (Reflect.Str(staged, "Status") != "StagedInMemory") throw new InvalidOperationException("R05 legacy fixture Stage failed: " + Reflect.Json(staged));
        Reflect.Call(LogisticsRuntime, "Load", Store);
        R.Check("R05.legacy-v6-fixture-loaded-with-original-boundaries", legacyDebt, ReviewPerfumeDebt(legacy.Machine.uniqueId),
            (int?)LogisticsState["Version"] == 6 && JToken.DeepEquals(legacyDebt, ReviewPerfumeDebt(legacy.Machine.uniqueId)),
            "Explicit historical-format fixture: only this actual consumed perfume record's signature and quantity are changed to empty/3; real RunData Stage and runtime Load parse schema 6. This is not a recovered historical player save.");
        var legacyRejected = candidates.Where(item => item != otherFull && item != exactFull).ToArray();
        JArray legacyBefore = new(legacyRejected.Select(ReviewPerfumeState));
        ReviewFillPerfume(legacy);
        R.Check("R05.legacy-debt-preserves-quantity-and-rejects-invalid-bottles", new { remaining = 3, targetCount = 3, purity = 0, quality = 0, candidates = legacyBefore },
            new { debt = ReviewPerfumeDebt(legacy.Machine.uniqueId), candidates = new JArray(legacyRejected.Select(ReviewPerfumeState)) },
            JToken.DeepEquals(legacyDebt, ReviewPerfumeDebt(legacy.Machine.uniqueId)) && JToken.DeepEquals(legacyBefore, new JArray(legacyRejected.Select(ReviewPerfumeState))) &&
                legacy.Input.childItems.All(item => item.identifier != ReviewPerfumeId),
            "Actual FillOnce rejects half/empty/disabled-use/purity/grade mismatches without discarding or rewriting the existing debt");
        Place(resources, otherFull, "R05-legacy-full-6-of-6");
        GameItem legacyNativeFull = Candidate("legacy-native-full-5-of-5", _ => { });
        ReviewFillPerfume(legacy);
        JObject? remainingLegacy = ReviewPerfumeDebt(legacy.Machine.uniqueId);
        int[] accepted = legacy.Input.childItems.Where(item => item.identifier == ReviewPerfumeId).Select(item => item.uniqueId).OrderBy(id => id).ToArray();
        R.Check("R05.legacy-debt-accepts-current-full-bottles-and-keeps-remainder", new { uids = new[] { otherFull.uniqueId, legacyNativeFull.uniqueId }.OrderBy(id => id).ToArray(), remaining = 1, targetCount = 3, purity = 0, quality = 0, materialState = "" },
            new { accepted, debt = remainingLegacy, bottles = legacy.Input.childItems.Where(item => item.identifier == ReviewPerfumeId).Select(ReviewPerfumeState).ToArray() },
            accepted.SequenceEqual(new[] { otherFull.uniqueId, legacyNativeFull.uniqueId }.OrderBy(id => id)) &&
                (int?)remainingLegacy?["Remaining"] == 1 && (int?)remainingLegacy?["TargetCount"] == 3 && (int?)remainingLegacy?["Purity"] == 0 &&
                (int?)remainingLegacy?["Quality"] == 0 && (string?)remainingLegacy?["MaterialState"] == "",
            "Real native transfer pays two of three historical-format debts with full 5/5 and 6/6 bottles; one original obligation remains for actual ES3 save/reload");
        int[] machineUids = { strict.Machine.uniqueId, pendingNew.Machine.uniqueId, legacy.Machine.uniqueId };
        int[] perfumeUids = candidates.Select(item => item.uniqueId).Distinct().OrderBy(id => id).ToArray();
        R.persistence["reviewLogisticsPerfume"] = new JObject
        {
            ["machineUids"] = JArray.FromObject(machineUids), ["perfumeUids"] = JArray.FromObject(perfumeUids),
            ["machines"] = new JObject(machineUids.Select(id => new JProperty(id.ToString(), LogisticsState["Machines"]![id.ToString()]!.DeepClone()))),
            ["perfumes"] = new JArray(perfumeUids.Select(id => ReviewPerfumeState(AllItems().Single(item => item.uniqueId == id))))
        };
        yield return null;
    }

    private int[] ReviewLiveRootIds() => ReviewRootFields.Select(field => (Reflect.Get(LogisticsRuntime, field) as GameItem)?.uniqueId ?? 0).ToArray();
    private int[] ReviewSavedRootIds()
    {
        JToken state = LogisticsState;
        return new[] { (int)state["ResourceInstanceId"]!, (int)state["WaterInstanceId"]!, (int)state["Power"]!["InstanceId"]! };
    }
    private JObject ReviewSharedNetworkState()
    {
        JToken state = LogisticsState;
        return new JObject
        {
            ["TerminalGranted"] = state["TerminalGranted"]!.DeepClone(), ["ResourcesUnlocked"] = state["ResourcesUnlocked"]!.DeepClone(),
            ["WaterUnlocked"] = state["WaterUnlocked"]!.DeepClone(), ["ResourcesEnabled"] = state["ResourcesEnabled"]!.DeepClone(),
            ["Resources"] = state["Resources"]!.DeepClone(), ["PowerUnlocked"] = state["Power"]!["Unlocked"]!.DeepClone(),
            ["PowerWidth"] = state["Power"]!["Width"]!.DeepClone(), ["PowerHeight"] = state["Power"]!["Height"]!.DeepClone(),
            ["StoredEnergy"] = state["Power"]!["StoredEnergy"]!.DeepClone()
        };
    }
    private JArray ReviewRootContents(IEnumerable<GameItem> devices) => new(devices.OrderBy(item => item.uniqueId).Select(item => new JObject
    {
        ["id"] = item.identifier, ["uid"] = item.uniqueId,
        ["contents"] = new JArray(GraphUtils.FindAllChildrenType<GameItem>(item).OrderBy(child => child.uniqueId).Select(ItemState)),
        ["grids"] = JArray.FromObject(Reflect.Each(Reflect.Call(LogisticsRuntime, "Grids", item)).Cast<GameGridInventory>()
            .Select(grid => new { grid.identifier, width = grid.inventoryShape.width, height = grid.inventoryShape.height })),
        ["liquids"] = item.IsTag("LIQUID_CONTAINER_TAG") ? LiquidState(item) : null,
        ["liquidCapacity"] = item.IsTag("LIQUID_CONTAINER_TAG") ? WaterHelper.GetFreeCapacity(item) + WaterHelper.GetTotalVolume(item) : 0
    }));
    private IEnumerable<object?> ReviewWaitForScan(string name, Func<bool> condition)
    {
        float previous = Convert.ToSingle(Reflect.Get(LogisticsRuntime, "_nextScan"));
        float deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline && (Convert.ToSingle(Reflect.Get(LogisticsRuntime, "_nextScan")) <= previous || !condition())) yield return null;
        bool scanned = Convert.ToSingle(Reflect.Get(LogisticsRuntime, "_nextScan")) > previous;
        R.Check(name, "Ordinary runtime Update scans and reaches the specified root/energy state within 8 seconds",
            new { scanned, liveRoots = ReviewLiveRootIds(), savedRoots = ReviewSavedRootIds(), shared = ReviewSharedNetworkState() }, scanned && condition(),
            "Frame yields observe the installed updater; no direct BindNetworkRoots/RebindNetworks call or assigned root fields");
    }
    private IEnumerable<object?> ReviewNetworkRootReplacement()
    {
        R.phase = "R06-normal-scan-root-replacement";
        int[] originalIds = ReviewSavedRootIds();
        GameItem[] originals = originalIds.Select(id => AllItems().Single(item => item.uniqueId == id)).ToArray();
        if (ReviewRootTypes.Any(type => AllItems().Count(item => item.identifier == type) != 1))
            throw new FixtureUnavailable("R06 requires the dedicated review run to contain exactly one granted device of each type before arranging replacements");
        GameItem[] replacements = ReviewRootTypes.Select(type => { var item = Spawn(type); Retain(item); return item; }).ToArray();
        GameItem[] allDevices = originals.Concat(replacements).ToArray();
        int[] replacementIds = replacements.Select(item => item.uniqueId).ToArray();
        try
        {
            GameGridInventory Grid(GameItem item) => Reflect.Each(Reflect.Call(LogisticsRuntime, "Grids", item)).Cast<GameGridInventory>().First();
            Place(Grid(originals[0]), Spawn("wire", 2), "R06-original-container-marker");
            Place(Grid(replacements[0]), Spawn("wire", 3), "R06-replacement-container-marker");
            WaterHelper.AddPureWater(originals[1], 40000); WaterHelper.AddLiquid(originals[1], "alcohol", 10000);
            WaterHelper.AddPureWater(replacements[1], 20000); WaterHelper.AddLiquid(replacements[1], "alcohol", 30000);
            long gridBefore = GridTotal;
            GameItem donor = PreBuiltItemHelper.EnergyCredit(37);
            Place(Grid(originals[2]), donor, "R06-real-grid-energy-donor");
            foreach (object? wait in ReviewWaitForScan("R06.original-roots-stay-preferred", () => ReviewLiveRootIds().SequenceEqual(originalIds) && GridTotal == gridBefore + 37 && Energy(donor) == 0)) yield return wait;
            JObject shared = ReviewSharedNetworkState(); JArray contents = ReviewRootContents(allDevices);
            R.environment["R06FixtureProvenance"] = "Native prerequisite unlock/grant, actual factory replacements and distinct physical/liquid fixture contents. A real 37-energy battery pays into the shared ledger. Native Expel temporarily removes a device from discoverable player inventories, and Retain returns it; this is not mouse selling/dropping.";
            void CheckPreserved(string stage, IEnumerable<int> expectedVisible)
            {
                int[] visible = AllItems().Where(item => ReviewRootTypes.Contains(item.identifier)).Select(item => item.uniqueId).OrderBy(id => id).ToArray();
                R.Check("R06.no-regrant-or-resource-copy." + stage, new { shared, contents, visible = expectedVisible.OrderBy(id => id).ToArray() },
                    new { shared = ReviewSharedNetworkState(), contents = ReviewRootContents(allDevices), visible },
                    JToken.DeepEquals(shared, ReviewSharedNetworkState()) && JToken.DeepEquals(contents, ReviewRootContents(allDevices)) && visible.SequenceEqual(expectedVisible.OrderBy(id => id)),
                    "Receipts/capacity/shared energy and each original/replacement inventory and liquid components are independently read back; known visible UID set excludes new gifts");
            }
            for (int index = 0; index < originals.Length; index++)
            {
                GameItem original = originals[index];
                if (original.parentInventory == null || !original.parentInventory.Expel(original)) throw new InvalidOperationException("R06 cannot detach original " + original.identifier);
                int[] expected = originalIds.Select((id, i) => i <= index ? replacementIds[i] : id).ToArray();
                foreach (object? wait in ReviewWaitForScan("R06.same-type-replacement." + ReviewRootTypes[index], () => ReviewLiveRootIds().SequenceEqual(expected) && ReviewSavedRootIds().SequenceEqual(expected))) yield return wait;
                CheckPreserved("replacement-" + index, originalIds.Skip(index + 1).Concat(replacementIds));
            }
            foreach (GameItem replacement in replacements)
                if (replacement.parentInventory == null || !replacement.parentInventory.Expel(replacement)) throw new InvalidOperationException("R06 cannot detach replacement " + replacement.identifier);
            foreach (object? wait in ReviewWaitForScan("R06.no-candidate-offline", () => ReviewLiveRootIds().All(id => id == 0) &&
                Reflect.Call(Logistics, "ResourceRegion") == null && Reflect.Call(Logistics, "ResourceWaterTank") == null && Reflect.Call(Logistics, "PowerRegion") == null &&
                !(bool)Reflect.Get(Logistics, "ResourceNetworkOnline")!)) yield return wait;
            R.Check("R06.missing-roots-keep-last-persisted-uids", replacementIds, ReviewSavedRootIds(), replacementIds.SequenceEqual(ReviewSavedRootIds()));
            CheckPreserved("no-candidates", Array.Empty<int>());
            foreach (GameItem replacement in replacements) Retain(replacement);
            foreach (object? wait in ReviewWaitForScan("R06.returned-candidates-recover", () => ReviewLiveRootIds().SequenceEqual(replacementIds) &&
                ReviewSavedRootIds().SequenceEqual(replacementIds) && (bool)Reflect.Get(Logistics, "ResourceNetworkOnline")!)) yield return wait;
            CheckPreserved("recovered", replacementIds);
            foreach (GameItem original in originals) Retain(original);
            foreach (object? wait in ReviewWaitForScan("R06.valid-current-root-kept-when-older-uids-return", () => ReviewLiveRootIds().SequenceEqual(replacementIds) && ReviewSavedRootIds().SequenceEqual(replacementIds))) yield return wait;
            CheckPreserved("older-roots-returned", originalIds.Concat(replacementIds));
            R.persistence["reviewLogisticsRoots"] = new JObject
            {
                ["deviceUids"] = JArray.FromObject(allDevices.Select(item => item.uniqueId).OrderBy(id => id)),
                ["rootUids"] = JArray.FromObject(replacementIds), ["shared"] = shared, ["contents"] = contents
            };
            R.Skip("R06.indeterminate-transaction-stop-latch", "No real indeterminate transaction fixture is available in this mode. The probe does not assign the stop flag and claim to have reproduced a production rollback failure; this path remains covered only by the prior focused implementation checks.", false);
        }
        finally
        {
            foreach (GameItem device in allDevices.Where(item => item.parentInventory == null)) Retain(device);
        }
    }

    internal IEnumerable<object?> ReviewLogisticsReload(JObject baseline)
    {
        R.phase = "R05-R06-independent-process-es3-readback";
        JToken perfume = baseline["persistence"]?["reviewLogisticsPerfume"] ?? throw new InvalidDataException("Missing R05 save baseline");
        JToken roots = baseline["persistence"]?["reviewLogisticsRoots"] ?? throw new InvalidDataException("Missing R06 save baseline");
        int[] machineUids = perfume["machineUids"]!.Values<int>().ToArray(), perfumeUids = perfume["perfumeUids"]!.Values<int>().ToArray();
        var machines = new JObject(machineUids.Select(id => new JProperty(id.ToString(), LogisticsState["Machines"]?[id.ToString()]?.DeepClone())));
        var bottles = new JArray(perfumeUids.Select(id => ReviewPerfumeState(AllItems().Single(item => item.uniqueId == id))));
        R.Check("R05.es3-retains-new-and-legacy-pending-quantity-and-real-bottle-state", new { machines = perfume["machines"], perfumes = perfume["perfumes"] }, new { machines, perfumes = bottles },
            JToken.DeepEquals(perfume["machines"], machines) && JToken.DeepEquals(perfume["perfumes"], bottles),
            "Normal LoadGame in the independent process; actual pending records, physical UIDs, use tags/counts, purity and grade compared to the saved baseline");
        int[] deviceUids = roots["deviceUids"]!.Values<int>().ToArray(), rootUids = roots["rootUids"]!.Values<int>().ToArray();
        GameItem[] devices = deviceUids.Select(id => AllItems().Single(item => item.uniqueId == id)).ToArray();
        JArray actualContents = ReviewRootContents(devices);
        R.Check("R06.es3-retains-replacement-roots-capacity-energy-and-independent-contents", roots,
            new { rootUids = ReviewLiveRootIds(), savedRootUids = ReviewSavedRootIds(), shared = ReviewSharedNetworkState(), contents = actualContents },
            ReviewLiveRootIds().SequenceEqual(rootUids) && ReviewSavedRootIds().SequenceEqual(rootUids) &&
                JToken.DeepEquals(roots["shared"], ReviewSharedNetworkState()) && JToken.DeepEquals(roots["contents"], actualContents),
            "Real ES3 LoadGame restores all six devices and binds saved replacements although the older original UIDs are also owned");
        yield return null;
    }
}
