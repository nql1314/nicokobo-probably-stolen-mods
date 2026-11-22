using System.Diagnostics;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const string PerfLedger = "NICOKOBO_FORGE_LIQUID_VALUES_V1";
    private readonly JArray _performanceRows = new();
    private bool _performanceBudgetExpired;
    private bool _performanceInvalidated;

    internal IEnumerable<object?> NativePerformance()
    {
        R.phase = "performance-real-mono-public-read-paths";
        if (O.Reload) throw new InvalidOperationException("Performance sampling requires a fresh performance-save fixture");
        foreach (object? wait in PerfPrepareStatic()) yield return wait;
        var total = Stopwatch.StartNew(); // Preparation is complete; its waiting time is excluded from the reading budget.
        var allocation = PerfAllocationCounter();
        bool mono = Type.GetType("Mono.Runtime") != null;
        R.environment["nativePerformance"] = new { mono, runtime = Environment.Version.ToString(),
            timerFrequency = Stopwatch.Frequency, Stopwatch.IsHighResolution, allocation.status,
            warmup = 128, groups = 5, iterationsPerGroup = 1000, preparationBudgetSeconds = 120, groupBudgetSeconds = 15, totalBudgetSeconds = 120,
            totalBudgetScope = "Reading phase starts only after catalog/static preparation completes; preparation waiting is excluded",
            samplingDesign = "Atomic public-getter microbenchmark: five contiguous groups in one Unity frame per workload", updatesBetweenGroups = false,
            rows = _performanceRows, sourceScanCount = "N/A: no production instrumentation; FindAllItem is measured separately",
            scope = "Native Mono public getters including installed patches; validation/deadline overhead included. No forced GC, timescale or player-setting changes. API ledger fixture is compatibility input, not a natural recipe/economic acquisition chain. Atomic steady-read cost only; FPS, Update throughput and cross-frame raw-value stability are not measured." };
        PerfRequire("performance.native-mono", true, mono, mono);
        object recipe = Reflect.Each(Reflect.Call(MachineCatalog, "MachineIds")).Cast<string>()
            .SelectMany(id => Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", id)))
            .Single(view => Reflect.Str(view, "Id") == "nicokobo.mechcore.synthesis.recipe.nutrifruit_juice");
        using var f = CreateRecipeFixture(recipe);
        int checksBefore = R.checks.Count;
        ObserveBatch(f, "performance-F03-real-production", false);
        PerfRequire("performance.real-batch-qualified", true, R.checks.Skip(checksBefore).Select(c => new { c.name, c.passed }).ToArray(),
            R.checks.Count > checksBefore && R.checks.Skip(checksBefore).All(c => c.passed));
        GameItem product = f.Output.childItems.Single();
        PerfRequire("performance.F03-control-no-liquid-ledger", new { productionBasis = true, liquidLedger = false },
            new { product.identifier, product.uniqueId, product.unitValue, productionBasis = product.IsTag("NICOKOBO_FORGE_PRODUCTION_BASIS_V1"),
                liquidLedger = !string.IsNullOrEmpty(product.GetTagReadonly(PerfLedger).GetString()) },
            product.unitValue > 0 && product.IsTag("NICOKOBO_FORGE_PRODUCTION_BASIS_V1") && string.IsNullOrEmpty(product.GetTagReadonly(PerfLedger).GetString()));
        R.environment["nativePerformanceRecipe"] = new { id = Reflect.Str(recipe, "Id"), machineId = f.Machine.identifier,
            outputId = product.identifier, outputCount = Reflect.Int(recipe, "OutputCount"), waterParts = Reflect.Int(recipe, "WaterParts"),
            inputs = Reflect.Each(Reflect.Get(recipe, "Ingredients")).Select(rule => new { id = Reflect.Str(rule, "ItemId"), amount = Reflect.Int(rule, "Amount") }).ToArray() };
        foreach (object? wait in PerfReadWorkload("F03-no-ledger-control", () => (product.GetRefreshedValue(), 0L, 0L),
            (product.GetRefreshedValue(), 0L, 0L), total, allocation.read)) yield return wait;
        if (_performanceBudgetExpired || _performanceInvalidated) yield break;

        var provider = ModLoader.Mods.Single(mod => mod.Manifest.ID == "nicokobo.forge").Instance as IServiceProvider
            ?? throw new InvalidOperationException("Enabled Forge does not expose its public IServiceProvider");
        var api = provider.GetService(typeof(Func<string, object[], object>)) as Func<string, object[], object?>
            ?? throw new InvalidOperationException("Public Forge protocol delegate unavailable");
        object? protocol = api("protocol", Array.Empty<object>());
        PerfRequire("performance.forge-protocol", 1, protocol, protocol is 1);
        var vessel = Spawn("water_jug"); Retain(vessel);
        WaterHelper.EmptyContainer(vessel);
        WaterHelper.AddLiquid(vessel, "water", 500000); WaterHelper.AddLiquid(vessel, "sugar", 250000);
        var raw = api("liquid.capture", new object[] { vessel }) as object[] ?? throw new InvalidOperationException("Public liquid capture unavailable");
        var rawParts = (object[][])raw[1];
        PerfRequire("performance.api-liquid-components", new { water = 500000, sugar = 250000, everyOtherSpecies = 0 }, Reflect.Json(raw),
            rawParts.Length == Liquid.Liquids.Count && rawParts.Select(part => (string)part[0]).Distinct(StringComparer.Ordinal).Count() == rawParts.Length &&
            rawParts.All(part => (int)part[1] == ((string)part[0] == "water" ? 500000 : (string)part[0] == "sugar" ? 250000 : 0)));
        var parts = rawParts.Select(part => new object[] { part[0], part[1],
            (string)part[0] == "water" ? (object)100m : (string)part[0] == "sugar" ? 20m : null!,
            (string)part[0] == "water" ? (object)40m : (string)part[0] == "sugar" ? 10m : null! }).ToArray();
        var wire = new object[] { raw[0], parts };
        bool written = api("liquid.write", new object[] { vessel, wire }) is true;
        JToken expected = Reflect.Json(wire), observed = Reflect.Json(api("liquid.capture", new object[] { vessel }));
        PerfRequire("performance.api-ledger-fixture", expected, observed,
            written && JToken.DeepEquals(expected, observed) && WaterHelper.GetTotalVolume(vessel) == 750000 && vessel.unitValue == 120 &&
            !string.IsNullOrEmpty(vessel.GetTagReadonly(PerfLedger).GetString()) && GeneralHelper.IsItemOwned(vessel) && vessel.parentInventory != null);
        foreach (object? wait in PerfReadWorkload("public-API-ledger-compatibility", () => (vessel.GetRefreshedValue(), 0L, 0L),
            (vessel.GetRefreshedValue(), 0L, 0L), total, allocation.read)) yield return wait;
        if (_performanceBudgetExpired || _performanceInvalidated) yield break;

        const string prefix = "nicokobo.mechcore.synthesis.";
        string[] related = { prefix + "kinetic_recovery", prefix + "titanium_exoskeleton", prefix + "fine_motor_assistant" };
        string unrelated = prefix + "cyber_eye";
        var present = Store.FindAllItem().Where(item => related.Contains(item.identifier) || item.identifier == unrelated).Select(ItemState).ToArray();
        int basePower = f.Machine.GetTagReadonly(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).GetInt();
        PerfRequire("performance.machine-clean-fixture", "No preexisting selected implants, installed modules or scaling-ratio tags", present,
            present.Length == 0 && f.Modules != null && f.Modules.childItems.Count == 0 && basePower > 0 &&
            !f.Machine.IsTag("MACHINE_EFFICIENCY_RATIO_INT") && !f.Machine.IsTag("MACHINE_PERFORMANCE_REDUCES_POWER_TAG"));
        foreach (string stage in new[] { "none", "unrelated-eye", "three-related-and-eye" })
        {
            if (stage == "unrelated-eye") Retain(Spawn(unrelated));
            if (stage == "three-related-and-eye") foreach (string id in related) Retain(Spawn(id));
            var selected = Store.FindAllItem().Where(item => related.Contains(item.identifier) || item.identifier == unrelated).ToArray();
            int count = stage == "none" ? 0 : stage == "unrelated-eye" ? 1 : 4;
            PerfRequire("performance.implant-placement." + stage, count, selected.Select(ItemState).ToArray(),
                selected.Length == count && selected.All(item => item.unitCount == 1 && item.parentInventory != null && GeneralHelper.IsItemOwned(item)));
            long bonus = stage == "three-related-and-eye" ? 50 : 0;
            var expectedReads = (Math.Max(1L, basePower - basePower * bonus / 100), bonus, bonus);
            foreach (object? wait in PerfReadWorkload("machinery-three-public-reads." + stage,
                () => (MachineryHelper.GetMachinePowerUsage(f.Machine), MachineryHelper.GetCurrentPerformanceBonus(f.Machine), MachineryHelper.GetCurrentQualityBonus(f.Machine)),
                expectedReads, total, allocation.read)) yield return wait;
            if (_performanceBudgetExpired || _performanceInvalidated) yield break;
            int inventoryCount = Store.FindAllItem().Count;
            foreach (object? wait in PerfReadWorkload("FindAllItem-independent-control." + stage,
                () => ((long)Store.FindAllItem().Count, 0L, 0L), ((long)inventoryCount, 0L, 0L), total, allocation.read)) yield return wait;
            if (_performanceBudgetExpired || _performanceInvalidated) yield break;
        }
        Retain(f.Machine); f.Retained = true;
    }

    private void PerfRequire(string name, object expected, object? observed, bool passed)
    {
        R.Check(name, expected, observed, passed, "Real native fixture/read; correctness never depends on a timing threshold");
        if (!passed) throw new InvalidOperationException("Native performance fixture/read mismatch: " + name);
    }
    private JObject PerfCatalogState()
    {
        object owner = ModLoader.Mods.Single(mod => mod.Manifest.ID == "nicokobo.item-manager").Instance
            ?? throw new InvalidOperationException("Required item-manager instance unavailable");
        object catalog = Reflect.Get(owner, "Catalog")!;
        return JObject.FromObject(new { ready = (bool)Reflect.Get(catalog, "Ready")!, scanning = (bool)Reflect.Get(owner, "IsScanning")!,
            scanned = Reflect.Int(catalog, "Scanned"), total = Reflect.Int(catalog, "Total") });
    }
    private bool PerfShopSafe() => (Store.storeState is PlayerStore.StoreState.MORNING or PlayerStore.StoreState.CLOSED) &&
        Store.currentClientInstance == null && !Store.isClientArrived && !Store.isClientBeingArrested && !Store.isHandlingShootout && Store.lockShutterButtonFlag == 0;
    private IEnumerable<object?> PerfPrepareStatic()
    {
        R.phase = "performance-preparation-catalog-and-static-shop";
        PerfRequire("performance.static-shop", "Already MORNING/CLOSED; no client/arrest/shootout/shutter transition", Store.storeState.ToString(), PerfShopSafe());
        var events = new JArray(); var transitions = new JArray();
        var record = new JObject { ["uidEvents"] = events, ["startupTransitions"] = transitions,
            ["budgetSeconds"] = 120,
            ["shopAction"] = "Already closed morning/closed shop; no shutter/state assignment needed",
            ["uidObservationScope"] = "Public PlayerStore.GetUniqueID postfix observes preparation only; removed before every measured workload" };
        R.environment["nativePerformancePreparation"] = record;
        using var trace = new PerfUidTrace(uid =>
        {
            if (events.Count < 64) events.Add(JObject.FromObject(new { uid, frame = UnityEngine.Time.frameCount,
                phase = R.phase, stack = new StackTrace(1, false).ToString() }));
            else record["uidTraceTruncated"] = true;
        }, record);
        var deadline = Stopwatch.StartNew(); var quiet = Stopwatch.StartNew();
        JToken? previous = null; int stableFrames = 0;
        while (deadline.Elapsed.TotalSeconds < 120)
        {
            var current = PerfSnapshot(); record["after"] = current;
            if (!PerfShopSafe()) PerfRequire("performance.static-shop", "Shop remains safely MORNING/CLOSED throughout preparation", current, false);
            if (previous == null) record["before"] = current.DeepClone();
            bool ready = (bool)current["catalog"]!["ready"]! && !(bool)current["catalog"]!["scanning"]! &&
                (int)current["catalog"]!["total"]! > 0 && (int)current["catalog"]!["scanned"]! == (int)current["catalog"]!["total"]!;
            bool unchanged = previous != null && JToken.DeepEquals(previous, current);
            if (ready && unchanged) stableFrames++;
            else
            {
                if (previous != null && !unchanged) transitions.Add(new JObject { ["frame"] = UnityEngine.Time.frameCount, ["before"] = previous, ["after"] = current });
                stableFrames = 0; quiet.Restart();
            }
            if (ready && stableFrames >= 4 && quiet.Elapsed.TotalSeconds >= 1.25)
            {
                record["stableFrames"] = stableFrames; record["quietSeconds"] = quiet.Elapsed.TotalSeconds;
                PerfRequire("performance.static-qualified", "Catalog Ready and not scanning; complete snapshot unchanged for >=4 frames and >=1.25 seconds", current, true);
                yield break;
            }
            previous = current; yield return null;
        }
        PerfRequire("performance.static-qualified", "Catalog complete and static snapshot within 120-second preparation budget", record, false);
    }
    private sealed class PerfUidTrace : IDisposable
    {
        private static Action<int>? _record;
        private readonly JObject _state;
        private readonly Harmony _hooks = new("nicokobo.content_native_probe.performance_uid_observation");
        private readonly System.Reflection.MethodInfo _target = AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.GetUniqueID), Type.EmptyTypes);
        internal PerfUidTrace(Action<int> record, JObject state)
        {
            if (_record != null) throw new InvalidOperationException("Concurrent performance UID observation");
            _record = record; _state = state;
            try { _hooks.Patch(_target, postfix: new HarmonyMethod(typeof(PerfUidTrace), nameof(Allocated))); _state["uidObserverInstalled"] = true; }
            catch { _record = null; throw; }
        }
        private static void Allocated(int __result) => _record?.Invoke(__result);
        public void Dispose() { try { _hooks.Unpatch(_target, HarmonyPatchType.Postfix, _hooks.Id); _state["uidObserverRemoved"] = true; } finally { _record = null; } }
    }
    private JToken PerfSnapshot()
    {
        var items = Store.FindAllItem();
        return JObject.FromObject(new { Store.runID, Store.saveSlotId, Store.playerCash, Store.currentUniqueId, findAllItemCount = items.Count,
            storeState = Store.storeState.ToString(), currentClient = Store.currentClientInstance?.GetClientBlueprint()?.identifier,
            Store.isClientArrived, Store.isClientBeingArrested, Store.isHandlingShootout, Store.lockShutterButtonFlag, catalog = PerfCatalogState(),
            items = items.OrderBy(item => item.uniqueId).Select(item => new { state = ItemState(item), item.unitValue,
                ledger = item.GetTagReadonly(PerfLedger).GetString(),
                liquid = item.IsTag("LIQUID_CONTAINER_TAG") ? Reflect.Json(Reflect.Call(Reflect.Type("Nicokobo.ContentInterop.NativeLiquidApi"), "Capture", item)) : null }).ToArray() });
    }
    private static (Func<long>? read, string status) PerfAllocationCounter()
    {
        try
        {
            var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null || method.ReturnType != typeof(long)) return (null, "N/A: Mono does not expose current-thread allocation bytes");
            var read = (Func<long>)method.CreateDelegate(typeof(Func<long>));
            long before = read(); var calibration = new byte[8192]; long after = read(); GC.KeepAlive(calibration);
            if (after - before < calibration.Length) return (null, "N/A: Mono counter did not count an 8192-byte managed calibration allocation");
            return (read, "GC.GetAllocatedBytesForCurrentThread; current Unity thread only; calibrated with an 8192-byte array outside timing");
        }
        catch (Exception error) { return (null, "N/A: " + error.GetType().Name + ": " + error.Message); }
    }
    private IEnumerable<object?> PerfReadWorkload(string name, Func<(long A, long B, long C)> read,
        (long A, long B, long C) expected, Stopwatch total, Func<long>? allocation)
    {
        R.phase = "performance:" + name;
        int workloadFrame = UnityEngine.Time.frameCount;
        var groups = new JArray();
        var row = new JObject { ["name"] = name, ["warmup"] = 128, ["iterationsPerGroup"] = 1000,
            ["expected"] = new JArray(expected.A, expected.B, expected.C), ["groups"] = groups,
            ["samplingFrame"] = workloadFrame, ["samplingDesign"] = "Five contiguous groups in one Unity frame; no inter-group coroutine yield",
            ["managedThreadId"] = System.Threading.Thread.CurrentThread.ManagedThreadId };
        _performanceRows.Add(row);
        var warmup = Stopwatch.StartNew(); int warmupsDone = 0;
        for (int i = 0; i < 128; i++)
        {
            var actual = read(); warmupsDone++;
            if (actual != expected) PerfRequire("performance.return." + name, expected, actual, false);
            if (warmup.Elapsed.TotalSeconds > 15 || total.Elapsed.TotalSeconds > 120)
            { _performanceBudgetExpired = true; break; }
        }
        row["warmupsCompleted"] = warmupsDone;
        JToken? previousAfter = null;
        var boundaries = new JArray(); row["interGroupBoundaries"] = boundaries;
        for (int group = 0; group < 5 && !_performanceBudgetExpired; group++)
        {
            int frame = UnityEngine.Time.frameCount;
            JToken before = PerfSnapshot();
            if (group == 0) row["before"] = before;
            if (previousAfter != null)
            {
                bool stable = frame == workloadFrame && JToken.DeepEquals(previousAfter, before);
                boundaries.Add(new JObject { ["nextGroup"] = group + 1, ["nextFrame"] = frame, ["unchanged"] = stable,
                    ["sameFrameAsWorkload"] = frame == workloadFrame,
                    ["before"] = stable ? null : previousAfter, ["after"] = stable ? null : before });
                if (!stable)
                {
                    row["after"] = before; row["complete"] = false; row["invalidReason"] = "Full state or Unity frame changed between contiguous atomic getter groups";
                    _performanceInvalidated = true;
                    R.Skip("performance.inter-group-environment-change." + name,
                        "Atomic workload frame or complete between-group snapshot changed; samples invalid/incomplete. No resource/UID field is excluded.");
                    yield break;
                }
            }
            long? allocatedBefore = allocation?.Invoke();
            long start = Stopwatch.GetTimestamp();
            int done = 0;
            while (done < 1000)
            {
                var actual = read(); done++;
                if (actual != expected) PerfRequire("performance.return." + name, expected, new { group, done, actual }, false);
                if ((done & 31) == 0 && ((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency > 15 || total.Elapsed.TotalSeconds > 120))
                { _performanceBudgetExpired = true; break; }
            }
            long elapsed = Stopwatch.GetTimestamp() - start;
            long? allocated = allocation?.Invoke() - allocatedBefore;
            if (elapsed / (double)Stopwatch.Frequency > 15 || total.Elapsed.TotalSeconds > 120) _performanceBudgetExpired = true;
            JToken after = PerfSnapshot(); row["after"] = after;
            bool sameFrame = frame == UnityEngine.Time.frameCount && frame == workloadFrame;
            bool unchanged = sameFrame && JToken.DeepEquals(before, after);
            groups.Add(new JObject { ["group"] = group + 1, ["iterations"] = done, ["elapsedTicks"] = elapsed,
                ["elapsedMs"] = elapsed * 1000d / Stopwatch.Frequency, ["allocatedBytes"] = allocated == null ? JValue.CreateNull() : new JValue(allocated.Value),
                ["frameBefore"] = frame, ["frameAfter"] = UnityEngine.Time.frameCount, ["sameFrame"] = sameFrame, ["stateUnchanged"] = unchanged });
            PerfRequire("performance.readonly." + name + "." + (group + 1), "Full before/after composition, Value, QualityBasis, ledger, UID/count/ownership, cash and UID generator unchanged",
                unchanged ? (object)true : new { before, after }, unchanged);
            previousAfter = after;
            // All five groups remain in this Unity frame; ordinary Update work is outside this atomic getter microbenchmark.
        }
        row["complete"] = !_performanceBudgetExpired && groups.Count == 5;
        double[] times = groups.Select(group => (double)group["elapsedMs"]!).OrderBy(ms => ms).ToArray();
        if (times.Length > 0) row["groupMsSummary"] = JObject.FromObject(new { min = times[0], p50 = times[(times.Length - 1) / 2],
            p95 = times[(int)Math.Ceiling(times.Length * .95) - 1], max = times[times.Length - 1], samples = times.Length, percentileMethod = "nearest-rank group totals" });
        if (_performanceBudgetExpired)
            R.Skip("performance.measurement-budget." + name, "Measurement incomplete under 15s/group or 120s total safety budget; no product speed/correctness failure inferred", false);
        else R.Check("performance.samples." + name, "5 groups x 1000 exact stable reads after 128 warmups", row["groupMsSummary"], true,
            "Timing is observational: five contiguous groups at one Unity frame, including installed public-getter patches. Allocation is current-thread only, null/N/A when unsupported; snapshots/setup excluded. Three-read iteration invokes all three public getters once. Update throughput, FPS and cross-frame raw-value stability are outside this microbenchmark.");
    }
}
