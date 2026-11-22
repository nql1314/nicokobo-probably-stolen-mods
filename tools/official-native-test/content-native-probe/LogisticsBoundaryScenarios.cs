using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

// Root owns routing, build, installation and both isolated native processes.
internal sealed partial class ProbeContext
{
    private static readonly string[] LbSpecies =
    {
        "water", "heavy_metal", "organic_waste", "microplastic", "microbe", "chemical_contaminant", "physical_contaminant", "mineral",
        "dream_cap_mush", "dream_cap_extract", "plant_matter", "alcohol", "caffeine", "protein", "fat", "starch", "sugar", "coffee_mush",
        "contraceptive", "zyanide", "nightmare_extract", "nightmare_cap_mush", "chlorine", "synaprest", "fanta", "methylol", "reducing_agent",
        "acid", "base", "stabilizing_agent", "debris", "lipobond_agent", "livestock_steroid", "livestock_immunity", "wine_extract", "nomore"
    };
    private static readonly int[] LbFractions = { 10000, 9950, 9850, 9700, 9300, 5000 };
    private static readonly string[] LbGrades = { "pure", "high-quality", "base", "ghost", "rust", "gutterflow" };
    private readonly HashSet<int> _lbPersist = new();
    private readonly JArray _lbCases = new(), _lbWitnesses = new();
    private Func<string, object[], object?>? _lbService;
    private GameItem _lbCarrier = null!;
    private readonly JArray _lbStableWitnesses = new(), _lbNativeTicks = new();
    private GameItem _lbStableCarrier = null!;
    private Func<string, object[], object?> LbService
    {
        get
        {
            if (_lbService != null) return _lbService;
            var host = ModLoader.Mods.Single(mod => mod.Manifest.ID == "nicokobo.forge" && mod.IsEnabled);
            var provider = host.Instance as IServiceProvider ?? throw new InvalidOperationException("Enabled Forge lacks public IServiceProvider");
            return _lbService = provider.GetService(typeof(Func<string, object[], object>)) as Func<string, object[], object?>
                ?? throw new InvalidOperationException("Forge protocol delegate unavailable");
        }
    }
    private sealed class LbStopped(string reason) : Exception(reason) { }
    private void LbRequire(string name, object? expected, object? observed, bool passed, string evidence = "")
    {
        R.Check("logisticsboundaries." + name, expected, observed, passed, evidence);
        if (!passed) throw new LbStopped(name);
    }
    private void LbCase(string name, Action action)
    {
        int first = R.checks.Count;
        var row = new JObject { ["name"] = name, ["status"] = "running", ["firstCheck"] = first };
        _lbCases.Add(row); R.phase = "logisticsboundaries:" + name;
        try { action(); }
        catch (LbStopped) { }
        catch (FixtureUnavailable error)
        { row["exception"] = error.ToString(); R.Skip("logisticsboundaries." + name + ".fixture", error.Message, true); }
        catch (Exception error)
        {
            row["exception"] = error.ToString();
            R.Check("logisticsboundaries." + name + ".execution", "Actual native case and independent readback", null, false,
                "An exception cannot become optional success or a synthetic fallback", error.ToString());
        }
        finally
        {
            if (R.checks.Count == first) R.Check("logisticsboundaries." + name + ".empty", "At least one required check", null, false);
            row["lastCheckExclusive"] = R.checks.Count;
            row["status"] = R.checks.Skip(first).Any(check => check.status == "failed") ? "failed" :
                R.checks.Skip(first).Any(check => check.status == "not-tested") ? "not-tested" : "passed";
        }
    }
    internal IEnumerable<object?> LogisticsBoundaries()
    {
        R.phase = "logisticsboundaries-native-six-tier-admission";
        R.environment["logisticsBoundariesCases"] = _lbCases;
        R.environment["logisticsBoundariesScope"] = new
        {
            build = 26317870, family = "logisticsboundaries", matrix = "6 native source tiers x 6 nonempty target tiers, both network directions",
            source = "WaterFeatureHelper.WATER_PURITIES/GetPurityArrayIndex; AL LiquidFeeding.SameOutputQuality and public PourResource/WithdrawResourceLiquid",
            filter = "These APIs have no six-state filter/sourceGrade configuration. Native water_pitcher permits water_filter, water_filter_adv and hydroreed_filter, and rejects uv_filter. Physical installed filters are separate cases.",
            grants = "Native factories, real SlotMarker admission and WaterHelper Empty/Add. No purity, quality, gene, parent pointer or completion tag assignments.",
            valued = "All-36 reactive mixture is a SAME-FRAME PUBLIC API COMPATIBILITY FIXTURE and is explicitly destroyed after verified transfers. Persistence uses 36 isolated single-species native 1ml droppers with independent fixed Value/Basis, not a natural recipe.",
            oracle = "All component Parts/Value/QualityBasis and effective totals; raw unitValue is observational, never a cross-frame conserved quantity.",
            excludes = "No physical mouse drag/drain, forced write/callback fault, all rounding ties, filter purification throughput or natural acquisition chain is claimed."
        };
        LbRequire("live-six-native-tiers", new { thresholds = new[] { 9990, 9900, 9800, 9600, 9000, 3500 }, count = 6 },
            new { thresholds = WaterFeatureHelper.WATER_PURITIES, count = WaterFeatureHelper.WATER_DISPLAY.Length },
            WaterFeatureHelper.WATER_PURITIES.SequenceEqual(new[] { 9990, 9900, 9800, 9600, 9000, 3500 }) && WaterFeatureHelper.WATER_DISPLAY.Length == 6);
        LbRequire("live-36-species", LbSpecies.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Liquid.Liquids.Select(liquid => liquid.identifier).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            LbSpecies.Length == 36 && LbSpecies.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(
                Liquid.Liquids.Select(liquid => liquid.identifier).OrderBy(id => id, StringComparer.Ordinal)));
        object? protocol = LbService("protocol", Array.Empty<object>());
        LbRequire("public-forge-protocol", 1, protocol, protocol is 1);
        PrepareLogisticsNetworks();
        float deadline = Time.realtimeSinceStartup + 5f; GameItem? tank;
        while ((tank = Reflect.Call(Logistics, "ResourceWaterTank") as GameItem) == null || Reflect.Call(Logistics, "ResourceRegion") == null)
        {
            if (Time.realtimeSinceStartup >= deadline) throw new FixtureUnavailable("Actual awarded network endpoints did not become ready");
            yield return null;
        }
        LbRequire("initial-network-tank-empty", 0, WaterHelper.GetTotalVolume(tank), WaterHelper.GetTotalVolume(tank) == 0,
            "Fresh family refuses pre-filled awarded tank rather than destroying unknown contents");
        _lbCarrier = Spawn("smuggler_bay_mod"); LbPlaceTop(_lbCarrier, "retained-samples-carrier"); Retain(_lbCarrier);
        var carrierGrid = _lbCarrier.contentWindow?.childElement as GameGridInventory
            ?? throw new FixtureUnavailable("Native smuggler_bay_mod has no expected grid");
        for (int from = 0; from < 6; from++)
            for (int into = 0; into < 6; into++)
            {
                int sourceGrade = from, targetGrade = into;
                LbCase("grades." + from + "-to-" + into, () => LbGradePair(tank, carrierGrid, sourceGrade, targetGrade));
                yield return null;
            }
        for (int grade = 0; grade < 6; grade++)
        { int g = grade; LbCase("empty-target." + g, () => LbEmptyTarget(tank, g)); yield return null; }
        foreach (string variant in new[] { "partial-target", "empty-source", "full-target", "full-source", "self", "zero-maximum", "native-full-pour" })
        { LbCase("capacity." + variant, () => LbCapacity(tank, variant)); yield return null; }
        foreach (string filter in new[] { "water_filter", "water_filter_adv", "hydroreed_filter" })
        { LbCase("filter." + filter, () => LbFilter(tank, filter)); yield return null; }
        LbCase("native-uv-slot-rejection", LbUvAdmission); yield return null;
        LbCase("freeze-lock-and-legal-ancestor", () => LbAccess(tank)); yield return null;
        LbCase("public-api-valued-36-species-same-frame-compatibility", LbValued);
        yield return null;
        LbCase("native-single-species-fixed-witnesses", LbCreateStableWitnesses);
        R.environment["logisticsBoundariesNativeTicks"] = _lbNativeTicks;
        using var tick = new LbNativeTickObserver(O.RunId + ".save");
        foreach (object? wait in LbWaitForNativeTicks(tick, "save-single-species")) yield return wait;
        LbAssertStableWitnesses("valued-after-two-natural-ticks", _lbStableWitnesses);
        LbRequire("six-persisted-grade-witnesses", Enumerable.Range(0, 6).ToArray(), _lbWitnesses,
            _lbWitnesses.Count == 6 && _lbWitnesses.Select(w => (int)w["grade"]!).OrderBy(g => g).SequenceEqual(Enumerable.Range(0, 6)));
        _lbPersist.Add(tank.uniqueId); Retain(tank);
        LbAssertStableWitnesses("valued-before-save-baseline", _lbStableWitnesses);
        R.persistence["logisticsBoundaries"] = new JObject
        {
            ["schema"] = 2, ["steamBuild"] = 26317870, ["gameRunId"] = Store.runID, ["slotId"] = Store.saveSlotId,
            ["species"] = new JArray(LbSpecies), ["cash"] = Store.playerCash, ["network"] = LogisticsState.DeepClone(),
            ["carrier"] = LbIdentity(_lbCarrier), ["gradeWitnesses"] = _lbWitnesses.DeepClone(),
            ["stableCarrier"] = LbIdentity(_lbStableCarrier), ["stableWitnesses"] = _lbStableWitnesses.DeepClone(),
            ["nativeTicks"] = _lbNativeTicks.DeepClone(),
            ["retained"] = LbPersistedSnapshot(_lbPersist), ["cases"] = _lbCases.DeepClone(),
            ["provenance"] = "Factory composition plus public API-valued compatibility fixture; outer actual ES3 SaveGame and fresh-process LoadGame required"
        };
    }
    internal IEnumerable<object?> LogisticsBoundariesReload(JObject baseline)
    {
        R.phase = "logisticsboundaries-fresh-process-es3-readback";
        var expected = baseline["persistence"]?["logisticsBoundaries"] as JObject
            ?? throw new InvalidOperationException("Native logistics boundaries save baseline missing");
        LbRequire("reload-process-and-slot", new { mode = "logisticsboundaries-save", schema = 2, independent = true, Store.runID, Store.saveSlotId }, expected,
            (string?)baseline["mode"] == "logisticsboundaries-save" && (int?)expected["schema"] == 2 && (string?)expected["gameRunId"] == Store.runID &&
            (int?)expected["slotId"] == Store.saveSlotId && (int?)expected["steamBuild"] == 26317870 &&
            (int?)baseline["processId"] != R.processId && (string?)baseline["processToken"] != R.processToken && (string?)baseline["runId"] != O.RunId,
            "Schema2 uses stable isolated species, never adopts a reacted mixed-fluid baseline. Common Reload binds actual ES3 bytes, candidate binaries and fresh-process native LoadGame.");
        var fixedWitnesses = expected["stableWitnesses"] as JArray ?? throw new InvalidOperationException("Independent 36 single-species expectations missing");
        LbAssertStableWitnesses("reload-valued-independent-immediate", fixedWitnesses);
        R.environment["logisticsBoundariesNativeTicks"] = _lbNativeTicks;
        using var tick = new LbNativeTickObserver(O.RunId + ".reload");
        foreach (object? wait in LbWaitForNativeTicks(tick, "reload-single-species")) yield return wait;
        LbAssertStableWitnesses("reload-valued-after-two-natural-ticks", fixedWitnesses);
        int[] ids = ((JArray)expected["retained"]!).Select(item => (int)item["identity"]!["uid"]!).ToArray();
        JArray observed = LbPersistedSnapshot(ids);
        LbRequire("reload-components-values-bases-uids-parents", expected["retained"], observed, JToken.DeepEquals(expected["retained"], observed),
            "All36 Parts/Value/QualityBasis entries per isolated droplet, including zeros/nulls, compared after native chemistry really ran; raw unitValue is outside durable equality.");
        LbRequire("reload-cash-and-network-receipts", new { cash = (int)expected["cash"]!, network = expected["network"] },
            new { cash = Store.playerCash, network = LogisticsState }, Store.playerCash == (int)expected["cash"]! && JToken.DeepEquals(expected["network"], LogisticsState));
        foreach (string carrierKey in new[] { "carrier", "stableCarrier" })
        {
            var carrier = LbFind((int)expected[carrierKey]!["uid"]!);
            LbRequire("reload-" + carrierKey + "-ownership", expected[carrierKey], LbIdentity(carrier), JToken.DeepEquals(expected[carrierKey], LbIdentity(carrier)));
        }
        foreach (JToken witness in (JArray)expected["gradeWitnesses"]!)
        {
            int grade = (int)witness["grade"]!; var item = LbFind((int)witness["uid"]!);
            LbRequire("reload-native-tier." + grade, new { grade, fraction = LbFractions[grade], parts = 1000 },
                new { grade = WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(item)), purity = WaterHelper.GetWaterPurity(item), parts = WaterHelper.GetTotalVolume(item) },
                WaterHelper.GetTotalVolume(item) == 1000 && WaterHelper.GetWaterPurity(item) == LbFractions[grade] &&
                WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(item)) == grade);
        }
        var stableCarrier = LbFind((int)expected["stableCarrier"]!["uid"]!);
        var grid = stableCarrier.contentWindow?.childElement as GameGridInventory ?? throw new FixtureUnavailable("Reloaded native witness grid unavailable");
        string file = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId)), hash = Reflect.HashFile(file);
        foreach (JObject row in fixedWitnesses)
        {
            string id = (string)row["liquidId"]!;
            LbCase("reload-single-species-half-and-return." + id, () => LbSingleWitnessRoundTrip(row, grid));
            yield return null;
        }
        foreach (object? wait in LbWaitForNativeTicks(tick, "reload-after-real-roundtrips")) yield return wait;
        LbAssertStableWitnesses("reload-after-roundtrips-and-natural-ticks", fixedWitnesses);
        LbRequire("reload-no-implicit-save-after-transfer", hash, Reflect.HashFile(file), hash == Reflect.HashFile(file));
        R.persistence["logisticsBoundaries"] = expected.DeepClone();
        yield return null;
    }
    private GameItem LbFind(int uid)
    {
        var items = Store.FindAllItem().Where(item => item.uniqueId == uid).ToArray();
        if (items.Length != 1 || items[0].unitCount != 1 || !GeneralHelper.IsItemOwned(items[0]))
            throw new InvalidOperationException("Expected exactly one owned native item with UID " + uid);
        return items[0];
    }
    private static JObject LbIdentity(GameItem item) => new()
    {
        ["uid"] = item.uniqueId, ["id"] = item.identifier, ["count"] = item.unitCount,
        ["owned"] = GeneralHelper.IsItemOwned(item), ["parentUid"] = item.parentInventory?.GetParentItem()?.uniqueId ?? 0,
        ["storage"] = item.parentInventory?.identifier ?? ""
    };
    private JArray LbWorldIdentity() => new(Store.FindAllItem().Where(item => item.uniqueId > 0).Distinct().OrderBy(item => item.uniqueId).Select(LbIdentity));
    private JObject LbCapture(GameItem item)
    {
        var wire = LbService("liquid.capture", new object[] { item }) as object[]
            ?? throw new InvalidOperationException("Public liquid.capture unavailable for " + item.identifier);
        if (wire.Length != 2 || wire[1] is not object[][] parts || parts.Length != 36 ||
            parts.Select(part => (string)part[0]).Distinct(StringComparer.Ordinal).Count() != 36 ||
            !LbSpecies.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(parts.Select(part => (string)part[0]).OrderBy(id => id, StringComparer.Ordinal)))
            throw new InvalidOperationException("Liquid capture differs from the independent 36-species denominator");
        var contents = new JArray();
        foreach (var part in parts.OrderBy(part => (string)part[0], StringComparer.Ordinal))
        {
            string id = (string)part[0]; var native = Liquid.Liquids.Single(liquid => liquid.identifier == id);
            if ((int)part[1] != item.GetTagReadonly(native.currentPart).GetInt())
                throw new InvalidOperationException("Public liquid capture disagrees with native component " + id);
            contents.Add(new JObject { ["LiquidId"] = id, ["Parts"] = (int)part[1],
                ["Value"] = part[2] == null ? JValue.CreateNull() : JToken.FromObject((decimal)part[2]),
                ["QualityBasis"] = part[3] == null ? JValue.CreateNull() : JToken.FromObject((decimal)part[3]) });
        }
        int capacity = checked(WaterHelper.GetFreeCapacity(item) + WaterHelper.GetTotalVolume(item));
        if ((int)wire[0] != capacity || contents.Sum(part => (long)part["Parts"]!) != WaterHelper.GetTotalVolume(item))
            throw new InvalidOperationException("Native liquid capacity or all-component total disagrees");
        return new JObject { ["CapacityParts"] = capacity, ["Contents"] = contents };
    }
    private JArray LbPersistedSnapshot(IEnumerable<int> uids) => new(uids.Distinct().OrderBy(uid => uid).Select(uid =>
    {
        GameItem item = LbFind(uid);
        return new JObject { ["identity"] = LbIdentity(item), ["liquid"] = LbCapture(item) };
    }));
    private static JObject LbFixedValuedLiquid(int capacity, int partsPerSpecies) => new()
    {
        ["CapacityParts"] = capacity,
        ["Contents"] = new JArray(LbSpecies.OrderBy(id => id, StringComparer.Ordinal).Select(id =>
        {
            int index = Array.IndexOf(LbSpecies, id) + 1;
            return new JObject { ["LiquidId"] = id, ["Parts"] = partsPerSpecies,
                ["Value"] = partsPerSpecies == 0 ? JValue.CreateNull() : JToken.FromObject(3m * index * partsPerSpecies / 10000m),
                ["QualityBasis"] = partsPerSpecies == 0 ? JValue.CreateNull() : JToken.FromObject(1m * index * partsPerSpecies / 10000m) };
        }))
    };
    private void LbAssertFixedValued(string name, JObject fixedExpected, int emptyParts = 0, int fullParts = 10000)
    {
        var empty = LbFind((int)fixedExpected["emptyUid"]!); var full = LbFind((int)fixedExpected["fullUid"]!);
        int emptyCapacity = (int)fixedExpected["empty"]!["CapacityParts"]!, fullCapacity = (int)fixedExpected["full"]!["CapacityParts"]!;
        // Reconstruct the constants on every check, including in a new process.
        // A baseline that adopted null metadata cannot validate its own values.
        LbRequire(name + ".fixed-definition", "Original final invoice: empty source; each target component Parts10000/Value3*(index+1)/Basis(index+1)", fixedExpected,
            emptyCapacity > 0 && fullCapacity >= 360000 && empty.uniqueId != full.uniqueId &&
            JToken.DeepEquals(fixedExpected["empty"], LbFixedValuedLiquid(emptyCapacity, 0)) &&
            JToken.DeepEquals(fixedExpected["full"], LbFixedValuedLiquid(fullCapacity, 10000)));
        JObject expectedEmpty = LbFixedValuedLiquid(emptyCapacity, emptyParts), expectedFull = LbFixedValuedLiquid(fullCapacity, fullParts);
        JObject observedEmpty = LbCapture(empty), observedFull = LbCapture(full);
        LbRequire(name, new { empty = expectedEmpty, full = expectedFull }, new { empty = observedEmpty, full = observedFull, frame = Time.frameCount },
            JToken.DeepEquals(expectedEmpty, observedEmpty) && JToken.DeepEquals(expectedFull, observedFull),
            "Independent constants survive full Update frames, baseline construction and actual new-process load; component metadata may never be replaced by recaptured nulls. Raw unitValue remains outside this invariant.");
    }
    // Observation only: the original Update and every container's native
    // chemistry callback keep running. No timer, flag or reaction is replaced.
    private sealed class LbNativeTickObserver : IDisposable
    {
        private static long _entered, _completed;
        private readonly long _start, _enteredStart;
        private readonly Harmony _hooks;
        internal long Count => _completed - _start;
        internal long EnteredCount => _entered - _enteredStart;
        internal LbNativeTickObserver(string run)
        {
            _hooks = new Harmony("nicokobo.content_native_probe.liquid_tick." + run);
            try
            {
                var target = typeof(EmporiumEntry).GetMethod(nameof(EmporiumEntry.OneSecPrefixFunction), Type.EmptyTypes)
                    ?? throw new MissingMethodException("Native one-second Update entry is unavailable");
                var update = typeof(EmporiumEntry).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("Native EmporiumEntry.Update is unavailable");
                _hooks.Patch(target, postfix: new HarmonyMethod(typeof(LbNativeTickObserver), nameof(Observed)));
                _hooks.Patch(update, prefix: new HarmonyMethod(typeof(LbNativeTickObserver), nameof(UpdateStarting)),
                    postfix: new HarmonyMethod(typeof(LbNativeTickObserver), nameof(UpdateCompleted)));
                _start = _completed; _enteredStart = _entered;
            }
            catch { _hooks.UnpatchAll(_hooks.Id); throw; }
        }
        private static void Observed() => _entered++;
        private static void UpdateStarting(out long __state) => __state = _entered;
        private static void UpdateCompleted(long __state)
        {
            // Harmony postfixes run after normal return. A callback exception
            // aborting the native item loop must never count as a completed tick.
            if (_entered - __state == 1) _completed++;
        }
        public void Dispose() => _hooks.UnpatchAll(_hooks.Id);
    }
    private IEnumerable<object?> LbWaitForNativeTicks(LbNativeTickObserver observer, string stage)
    {
        long before = observer.Count, enteredBefore = observer.EnteredCount; int firstFrame = Time.frameCount;
        float deadline = Time.realtimeSinceStartup + 12f;
        while (observer.Count - before < 2)
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                LbRequire(stage + ".actual-native-tick-timeout", "At least two actual native one-second Update entries", new
                { before, after = observer.Count, enteredBefore, enteredAfter = observer.EnteredCount, firstFrame, frame = Time.frameCount, Time.timeScale }, false,
                    "No clock advance or synthetic tick substitutes for ordinary native chemistry; unavailable real time is a required failure");
            }
            yield return null;
        }
        // Keep an extra completed frame between the native loop and inspection.
        yield return null;
        var row = JObject.FromObject(new { stage, before, after = observer.Count, enteredBefore, enteredAfter = observer.EnteredCount, firstFrame, frame = Time.frameCount,
            source = "Read-only one-second entry counter plus native Update prefix/postfix. Count requires normal Update return and one actual native entry; no original callback, timer or result is replaced." });
        _lbNativeTicks.Add(row);
        LbRequire(stage + ".actual-native-ticks", "Two genuine native one-second entries and completed container loops", row,
            observer.Count - before >= 2 && observer.EnteredCount - enteredBefore == observer.Count - before && Time.frameCount > firstFrame,
            "Every observed native one-second entry in this window must finish its original item loop normally; aborted chemistry callbacks cannot become a successful wait.");
    }
    private static JObject LbSingleLiquid(string id, int parts)
    {
        int index = Array.IndexOf(LbSpecies, id) + 1;
        if (index <= 0 || parts is < 0 or > 1000) throw new InvalidOperationException("Invalid independent single-species expectation");
        return new JObject
        {
            ["CapacityParts"] = 1000,
            ["Contents"] = new JArray(LbSpecies.OrderBy(liquid => liquid, StringComparer.Ordinal).Select(liquid =>
                new JObject { ["LiquidId"] = liquid, ["Parts"] = liquid == id ? parts : 0,
                    ["Value"] = liquid == id && parts > 0 ? JToken.FromObject(3m * index * parts / 1000m) : JValue.CreateNull(),
                    ["QualityBasis"] = liquid == id && parts > 0 ? JToken.FromObject(1m * index * parts / 1000m) : JValue.CreateNull() }))
        };
    }
    private void LbAssertSingleState(string stage, JObject row, GameItem item, int parts)
    {
        string id = (string)row["liquidId"]!; int index = Array.IndexOf(LbSpecies, id) + 1;
        LbRequire(stage + ".independent-definition." + id, "One pure native component:1000parts/Value3*(index+1)/Basis(index+1); other35 zero/null", row,
            index > 0 && (int?)row["index"] == index && (int?)row["capacity"] == 1000 &&
            JToken.DeepEquals(row["expected"], LbSingleLiquid(id, 1000)),
            "Definition is rebuilt from explicit constants on every save/reload check, never copied from observed metadata");
        JObject expected = LbSingleLiquid(id, parts), observed = LbCapture(item);
        double temperature = item.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble();
        bool nativeCallback = item.onOneSecondItemFunc?.Method.DeclaringType?.Assembly == typeof(GameItem).Assembly;
        LbRequire(stage + ".physical-ledger." + id, expected, new { liquid = observed, temperature, nativeCallback, identity = LbIdentity(item) },
            JToken.DeepEquals(expected, observed) && item.identifier == "1ml_dropper" && item.unitCount == 1 && GeneralHelper.IsItemOwned(item) &&
            item.parentInventory != null && temperature >= 24 && temperature < 40 && nativeCallback && WaterHelper.GetFilterItem(item) == null &&
            EmporiumEntry.Instance.GetAllItems().Count(candidate => candidate.uniqueId == item.uniqueId) == 1,
            "Actual native chemistry callback remains installed and the real one-second item graph contains this vessel. At native ambient temperature, isolated species lack the co-reactants of the old unstable mixture.");
    }
    private void LbAssertStableWitnesses(string stage, JArray rows)
    {
        LbRequire(stage + ".all-36-denominator", LbSpecies.OrderBy(id => id, StringComparer.Ordinal).ToArray(), rows,
            rows.Count == 36 && rows.Select(row => (int)row["uid"]!).Distinct().Count() == 36 &&
            rows.Select(row => (string)row["liquidId"]!).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(LbSpecies.OrderBy(id => id, StringComparer.Ordinal)));
        foreach (JObject row in rows)
        {
            var item = LbFind((int)row["uid"]!);
            LbRequire(stage + ".identity." + (string)row["liquidId"]!, row["identity"], LbIdentity(item),
                JToken.DeepEquals(row["identity"], LbIdentity(item)));
            LbAssertSingleState(stage, row, item, 1000);
        }
    }
    private void LbCreateStableWitnesses()
    {
        var factory = DirectoryMaster.GetFactory<GameItem>("1ml_dropper");
        LbRequire("single-species-real-native-factory", "Unmodified native Assembly-CSharp factory", factory?.Method.DeclaringType?.Assembly.FullName,
            factory?.Method.DeclaringType?.Assembly == typeof(GameItem).Assembly);
        _lbStableCarrier = Spawn("storage_bay_large"); LbPlaceTop(_lbStableCarrier, "36-native-single-species-carrier"); Retain(_lbStableCarrier);
        var grid = _lbStableCarrier.contentWindow?.childElement as GameGridInventory ?? throw new FixtureUnavailable("Native large storage grid unavailable");
        LbRequire("single-species-native-storage-space", "Unchanged native17x10 grid;36 real1x2 droplets need72cells", new
            { width = grid.inventoryShape.width, height = grid.inventoryShape.height }, grid.inventoryShape.width == 17 && grid.inventoryShape.height == 10);
        foreach (string id in LbSpecies)
        {
            GameItem? item = Spawn("1ml_dropper");
            try
            {
                WaterHelper.EmptyContainer(item);
                if (!LbTryPlace(grid, item)) throw new FixtureUnavailable("Native SlotMarker cannot receive single-species witness " + id);
                WaterHelper.AddLiquid(item, id, 1000);
                var raw = LbService("liquid.capture", new object[] { item }) as object[] ?? throw new InvalidOperationException("Native single-species capture failed");
                int index = Array.IndexOf(LbSpecies, id) + 1;
                var nativeParts = (object[][])raw[1];
                LbRequire("single-species-native-parts." + id, "Exactly1000 native parts in the selected species; other35 zero", Reflect.Json(raw),
                    (int)raw[0] == 1000 && nativeParts.Length == 36 && nativeParts.All(part => (int)part[1] == ((string)part[0] == id ? 1000 : 0)));
                var valuedParts = nativeParts.Select(part => new object[] { part[0], part[1],
                    (string)part[0] == id ? (object)(3m * index) : null!, (string)part[0] == id ? (object)(1m * index) : null! }).ToArray();
                LbService("liquid.value", new object[] { item, new object[] { raw[0], valuedParts } });
                var row = new JObject { ["liquidId"] = id, ["index"] = index, ["uid"] = item.uniqueId,
                    ["capacity"] = 1000, ["identity"] = LbIdentity(item), ["expected"] = LbSingleLiquid(id, 1000),
                    ["provenance"] = "Native1ml_dropper + WaterHelper AddLiquid; public Forge-valued compatibility fixture, not a natural recipe" };
                LbAssertSingleState("single-species-before-yield", row, item, 1000);
                _lbStableWitnesses.Add(row); _lbPersist.Add(item.uniqueId); Retain(item); item = null;
            }
            finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); }
        }
        LbAssertStableWitnesses("single-species-all-created-before-yield", _lbStableWitnesses);
    }
    private void LbSingleWitnessRoundTrip(JObject row, GameGridInventory grid)
    {
        string id = (string)row["liquidId"]!; GameItem source = LbFind((int)row["uid"]!); GameItem? target = Spawn("1ml_dropper");
        try
        {
            WaterHelper.EmptyContainer(target);
            if (!LbTryPlace(grid, target)) throw new FixtureUnavailable("Native grid cannot receive temporary empty readback target " + id);
            LbAssertSingleState("reload-before-half", row, source, 1000);
            LbTransfer("reload-single-half." + id, source, target, 500,
                () => LbService("liquid.pour", new object[] { source, target, 500, null! }));
            LbAssertSingleState("reload-source-half-fixed", row, source, 500);
            LbAssertSingleState("reload-target-half-fixed", row, target, 500);
            LbTransfer("reload-single-return." + id, target, source, 500,
                () => LbService("liquid.pour", new object[] { target, source, 500, null! }));
            LbAssertSingleState("reload-source-restored-fixed", row, source, 1000);
            LbAssertSingleState("reload-target-empty-fixed", row, target, 0);
            WaterHelper.EmptyContainer(target); // already independently verified empty
        }
        finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(target); }
    }
    private static decimal LbEffective(JToken part, string field) => (decimal?)part[field] ??
        (field == "QualityBasis" ? (decimal?)part["Value"] : null) ?? (int)part["Parts"]! / 25000m;
    private static JObject LbTotals(JObject from, JObject into)
    {
        var result = new JObject();
        foreach (string id in LbSpecies)
        {
            JToken a = from["Contents"]!.Single(part => (string?)part["LiquidId"] == id);
            JToken b = into["Contents"]!.Single(part => (string?)part["LiquidId"] == id);
            result[id] = new JObject { ["Parts"] = (long)a["Parts"]! + (long)b["Parts"]!,
                ["Value"] = LbEffective(a, "Value") + LbEffective(b, "Value"),
                ["QualityBasis"] = LbEffective(a, "QualityBasis") + LbEffective(b, "QualityBasis") };
        }
        return result;
    }
    private static (JObject From, JObject Into) LbExpected(JObject from, JObject into, int moved)
    {
        var a = (JObject)from.DeepClone(); var b = (JObject)into.DeepClone();
        if (moved == 0) return (a, b);
        int total = checked((int)from["Contents"]!.Sum(part => (long)part["Parts"]!));
        foreach (JToken original in from["Contents"]!)
        {
            string id = (string)original["LiquidId"]!;
            JToken remaining = a["Contents"]!.Single(part => (string?)part["LiquidId"] == id);
            JToken received = b["Contents"]!.Single(part => (string?)part["LiquidId"] == id);
            int parts = (int)original["Parts"]!; long numerator = (long)parts * moved;
            if (numerator % total != 0) throw new InvalidOperationException("Independent fixture requires exact divisible draws; production rounding logic is never used by the oracle");
            int draw = (int)(numerator / total), targetBefore = (int)received["Parts"]!;
            decimal? value = (decimal?)original["Value"], basis = (decimal?)original["QualityBasis"];
            decimal? intoValue = (decimal?)received["Value"], intoBasis = (decimal?)received["QualityBasis"];
            if (draw > 0)
            {
                received["Value"] = value == null && intoValue == null ? JValue.CreateNull() :
                    JToken.FromObject((intoValue ?? targetBefore / 25000m) + (value == null ? draw / 25000m : value.Value * draw / parts));
                received["QualityBasis"] = basis == null && intoBasis == null ? JValue.CreateNull() :
                    JToken.FromObject((intoBasis ?? intoValue ?? targetBefore / 25000m) + (basis ?? value ?? parts / 25000m) * draw / parts);
            }
            remaining["Parts"] = parts - draw; received["Parts"] = targetBefore + draw;
            remaining["Value"] = value == null || parts == draw ? JValue.CreateNull() : JToken.FromObject(value.Value * (parts - draw) / parts);
            remaining["QualityBasis"] = basis == null || parts == draw ? JValue.CreateNull() : JToken.FromObject(basis.Value * (parts - draw) / parts);
        }
        return (a, b);
    }
    private void LbTransfer(string name, GameItem source, GameItem target, int expectedMoved, Func<object?> transfer)
    {
        JObject from = LbCapture(source), into = LbCapture(target), sourceId = LbIdentity(source), targetId = LbIdentity(target);
        LbRequire(name + ".owned-single-vessels", "Distinct live UID per physical vessel; count1, owned, actual parent", new { sourceId, targetId },
            source.uniqueId > 0 && target.uniqueId > 0 && source.unitCount == 1 && target.unitCount == 1 &&
            GeneralHelper.IsItemOwned(source) && GeneralHelper.IsItemOwned(target) && source.parentInventory != null && target.parentInventory != null &&
            Store.FindAllItem().Count(item => item.uniqueId == source.uniqueId) == 1 && Store.FindAllItem().Count(item => item.uniqueId == target.uniqueId) == 1);
        var expected = LbExpected(from, into, expectedMoved);
        JObject total = LbTotals(from, into); JArray world = LbWorldIdentity(); int cash = Store.playerCash;
        object? native = transfer();
        JToken receipt = native is object[] wire ? new JObject { ["PartsMoved"] = Convert.ToInt32(wire[0]),
            ["Indeterminate"] = Convert.ToBoolean(wire[1]), ["Status"] = (string)wire[2] } : Reflect.Json(native);
        JObject afterFrom = LbCapture(source), afterInto = LbCapture(target);
        LbRequire(name, new { moved = expectedMoved, indeterminate = false, source = expected.From, target = expected.Into, total, sourceId, targetId, cash },
            new { receipt, source = afterFrom, target = afterInto, total = LbTotals(afterFrom, afterInto), sourceId = LbIdentity(source), targetId = LbIdentity(target), cash = Store.playerCash,
                rawUnitValueObservation = new[] { source.unitValue, target.unitValue } },
            (int?)receipt["PartsMoved"] == expectedMoved && (bool?)receipt["Indeterminate"] == false &&
                JToken.DeepEquals(expected.From, afterFrom) && JToken.DeepEquals(expected.Into, afterInto) &&
                JToken.DeepEquals(total, LbTotals(afterFrom, afterInto)) && JToken.DeepEquals(sourceId, LbIdentity(source)) &&
                JToken.DeepEquals(targetId, LbIdentity(target)) && JToken.DeepEquals(world, LbWorldIdentity()) && cash == Store.playerCash,
            "Fixed pre-action amount and independent exact-divisible per-component invoice. All36 optional values/bases, UID/count/ownership/parent and global physical identity/cash checked; raw unitValue observational only.");
    }
    private bool LbTryPlace(GameInventory inventory, GameItem item)
    {
        SlotMarker? marker = inventory.TryFindOneValidInventorySlot(item);
        return marker != null && marker.IsValid() && marker.inventory == inventory && marker.numTransfer >= item.unitCount &&
            marker.TryAcceptOnce(item.unitCount) == item.unitCount && item.parentInventory == inventory;
    }
    private void LbPlaceTop(GameItem item, string name)
    {
        if (new GameInventory[] { EmporiumEntry.Instance.invElement, EmporiumEntry.Instance.showcaseElement, EmporiumEntry.Instance.backInvinvElement }
            .Any(inventory => LbTryPlace(inventory, item))) return;
        throw new FixtureUnavailable("Unchanged native SlotMarker cannot place " + name + ": " + item.identifier);
    }
    private GameItem LbVessel(string id = "water_jug")
    {
        var item = Spawn(id);
        try { LbPlaceTop(item, id); WaterHelper.EmptyContainer(item); return item; }
        catch { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); throw; }
    }
    private void LbGrade(GameItem vessel, int grade, int total)
    {
        WaterHelper.EmptyContainer(vessel);
        if (total <= 0 || total % 10000 != 0 || WaterHelper.GetFreeCapacity(vessel) < total)
            throw new FixtureUnavailable("Native vessel cannot hold the independent whole-fraction fixture");
        int water = checked(total / 10000 * LbFractions[grade]), dirt = total - water;
        WaterHelper.AddLiquid(vessel, "water", water);
        if (dirt > 0) WaterHelper.AddLiquid(vessel, "physical_contaminant", dirt);
        JObject captured = LbCapture(vessel);
        LbRequire("fixture-grade." + grade + ".uid-" + vessel.uniqueId, new { grade, water, dirt, total, everyOtherSpecies = 0, noLedger = true }, captured,
            WaterHelper.GetTotalVolume(vessel) == total && WaterHelper.GetWaterPurity(vessel) == LbFractions[grade] &&
            WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(vessel)) == grade && captured["Contents"]!.All(part =>
                (int)part["Parts"]! == ((string?)part["LiquidId"] == "water" ? water : (string?)part["LiquidId"] == "physical_contaminant" ? dirt : 0) &&
                part["Value"]!.Type == JTokenType.Null && part["QualityBasis"]!.Type == JTokenType.Null),
            "Native factory and WaterHelper Empty/Add only; purity and tier observed, never assigned");
    }
    private void LbGradePair(GameItem tank, GameGridInventory samples, int sourceGrade, int targetGrade)
    {
        GameItem? source = null, witness = null;
        try
        {
            source = LbVessel(); LbGrade(source, sourceGrade, 1000000); LbGrade(tank, targetGrade, 500000);
            int moved = sourceGrade == targetGrade ? 100000 : 0;
            LbTransfer("matrix-pour." + sourceGrade + "-" + targetGrade, source, tank, moved,
                () => Reflect.Call(Logistics, "PourResource", source, 100000));
            LbTransfer("matrix-withdraw." + targetGrade + "-" + sourceGrade, tank, source, moved,
                () => Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, source, 100000));
            if (sourceGrade == targetGrade)
            {
                witness = Spawn("1ml_dropper"); WaterHelper.EmptyContainer(witness);
                if (!LbTryPlace(samples, witness)) throw new FixtureUnavailable("Native carrier SlotMarker cannot retain grade sample");
                LbTransfer("matrix-native-witness-pour." + sourceGrade, source, witness, 1000,
                    () => LbService("liquid.pour", new object[] { source, witness, 1000, null! }));
                LbRequire("matrix-native-witness-grade." + sourceGrade, sourceGrade,
                    WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(witness)),
                    WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(witness)) == sourceGrade);
                _lbPersist.Add(witness.uniqueId); Retain(witness);
                _lbWitnesses.Add(new JObject { ["grade"] = sourceGrade, ["name"] = LbGrades[sourceGrade], ["uid"] = witness.uniqueId });
                witness = null;
            }
        }
        finally
        {
            // Explicit native disposal is fixture cleanup, outside conserved pours.
            WaterHelper.EmptyContainer(tank);
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(source); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(witness);
        }
    }
    private void LbEmptyTarget(GameItem tank, int grade)
    {
        GameItem? source = null;
        try
        {
            WaterHelper.EmptyContainer(tank); source = LbVessel(); LbGrade(source, grade, 1000000);
            LbTransfer("empty-target-accepts-native-source." + grade, source, tank, 100000,
                () => Reflect.Call(Logistics, "PourResource", source, 100000));
        }
        finally { WaterHelper.EmptyContainer(tank); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(source); }
    }
    private void LbCapacity(GameItem tank, string variant)
    {
        GameItem? source = null;
        try
        {
            source = LbVessel(); WaterHelper.EmptyContainer(tank); WaterHelper.AddLiquid(source, "water", 1000);
            LbRequire("capacity-source-setup." + variant, "1000 native pure-water parts", LbCapture(source),
                WaterHelper.GetTotalVolume(source) == 1000 && WaterHelper.GetWaterPurity(source) == 10000);
            if (variant == "partial-target")
            {
                int capacity = WaterHelper.GetFreeCapacity(tank); WaterHelper.AddLiquid(tank, "water", capacity - 37);
                LbRequire("partial-deposit-fixture", 37, WaterHelper.GetFreeCapacity(tank), WaterHelper.GetFreeCapacity(tank) == 37);
                LbTransfer("partial-target-deposit", source, tank, 37, () => Reflect.Call(Logistics, "PourResource", source, 1000));
                WaterHelper.EmptyContainer(source); WaterHelper.AddLiquid(source, "water", WaterHelper.GetFreeCapacity(source) - 37);
                LbRequire("partial-withdraw-fixture", 37, WaterHelper.GetFreeCapacity(source), WaterHelper.GetFreeCapacity(source) == 37);
                LbTransfer("partial-target-withdraw", tank, source, 37, () => Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, source, 1000));
            }
            else if (variant == "empty-source")
            {
                WaterHelper.EmptyContainer(source);
                LbRequire("empty-source-fixture", 0, WaterHelper.GetTotalVolume(source), WaterHelper.GetTotalVolume(source) == 0);
                LbTransfer("empty-source", source, tank, 0, () => Reflect.Call(Logistics, "PourResource", source, 1000));
            }
            else if (variant == "full-target")
            {
                WaterHelper.AddLiquid(tank, "water", WaterHelper.GetFreeCapacity(tank));
                LbRequire("full-target-fixture", 0, WaterHelper.GetFreeCapacity(tank), WaterHelper.GetFreeCapacity(tank) == 0 && WaterHelper.GetTotalVolume(tank) > 0);
                LbTransfer("full-target", source, tank, 0, () => Reflect.Call(Logistics, "PourResource", source, 1000));
            }
            else if (variant == "full-source")
            {
                WaterHelper.AddLiquid(source, "water", WaterHelper.GetFreeCapacity(source));
                int volume = WaterHelper.GetTotalVolume(source), moved = Math.Min(volume, WaterHelper.GetFreeCapacity(tank));
                LbRequire("full-source-fixture", "Source at actual native capacity; destination has positive observed free capacity",
                    new { volume, free = WaterHelper.GetFreeCapacity(source), intoFree = WaterHelper.GetFreeCapacity(tank) },
                    volume > 0 && WaterHelper.GetFreeCapacity(source) == 0 && moved > 0);
                LbTransfer("full-source-native-pour", source, tank, moved,
                    () => LbService("liquid.pour", new object[] { source, tank, int.MaxValue, null! }));
            }
            else if (variant == "self")
            {
                WaterHelper.AddLiquid(tank, "water", 1000);
                LbTransfer("self-withdraw", tank, tank, 0, () => Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, tank, 1000));
                LbTransfer("self-public-pour", source, source, 0, () => LbService("liquid.pour", new object[] { source, source, 500, null! }));
            }
            else if (variant == "zero-maximum")
                LbTransfer("zero-maximum", source, tank, 0, () => Reflect.Call(Logistics, "PourResource", source, 0));
            else if (variant == "native-full-pour")
                LbTransfer("native-full-pour", source, tank, 1000, () => LbService("liquid.pour", new object[] { source, tank, int.MaxValue, null! }));
            else throw new InvalidOperationException("Unknown boundary variant " + variant);
        }
        finally { WaterHelper.EmptyContainer(tank); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(source); }
    }
    private void LbFilter(GameItem tank, string filterId)
    {
        GameItem? pitcher = null, filter = null;
        try
        {
            pitcher = LbVessel("water_pitcher"); LbGrade(pitcher, 2, 500000); LbGrade(tank, 2, 1000000);
            filter = Spawn(filterId);
            var slot = pitcher.contentWindow?.childElement as GameSlotInventory ?? throw new FixtureUnavailable("Native pitcher filter slot unavailable");
            if (!LbTryPlace(slot, filter) || WaterHelper.GetFilterItem(pitcher)?.uniqueId != filter.uniqueId)
                throw new FixtureUnavailable("Unchanged native pitcher SlotMarker rejected supported filter factory " + filterId);
            int durability = DurabilityHelper.GetRealDurability(filter); JObject identity = LbIdentity(filter);
            LbTransfer("filtered-source-rejected." + filterId, pitcher, tank, 0,
                () => Reflect.Call(Logistics, "PourResource", pitcher, 100000));
            LbTransfer("filtered-target-rejected." + filterId, tank, pitcher, 0,
                () => Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, pitcher, 100000));
            LbRequire("filter-identity-durability-unchanged." + filterId, new { identity, durability },
                new { identity = LbIdentity(filter), durability = DurabilityHelper.GetRealDurability(filter) },
                JToken.DeepEquals(identity, LbIdentity(filter)) && DurabilityHelper.GetRealDurability(filter) == durability);
            if (!slot.Expel(filter) || filter.parentInventory != null) throw new InvalidOperationException("Native filter removal failed");
            LbTransfer("filter-removed-restores-access." + filterId, pitcher, tank, 100000,
                () => Reflect.Call(Logistics, "PourResource", pitcher, 100000));
        }
        finally
        {
            WaterHelper.EmptyContainer(tank);
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(new List<GameItem> { pitcher!, filter! });
        }
    }
    private void LbUvAdmission()
    {
        GameItem? pitcher = null, uv = null;
        try
        {
            pitcher = LbVessel("water_pitcher"); uv = Spawn("uv_filter");
            var inventory = pitcher.contentWindow?.childElement as GameSlotInventory ?? throw new FixtureUnavailable("Native pitcher slot missing");
            SlotMarker? marker = inventory.TryFindOneValidInventorySlot(uv);
            LbRequire("uv-native-slot-unsupported", "Native pitcher rejects uv_filter; no fake installed UV scenario", new { valid = marker?.IsValid() == true, parent = uv.parentInventory?.identifier },
                (marker == null || !marker.IsValid()) && uv.parentInventory == null && WaterHelper.GetFilterItem(pitcher) == null,
                "Build26317870 AmenitiesItemDirectory.WaterPitcher explicitly excludes uv_filter. UV is active item-target treatment, outside installed-flow-filter scope.");
        }
        finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(pitcher); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(uv); }
    }
    private void LbAccess(GameItem tank)
    {
        GameItem? source = null, ancestor = null; GameInventory? original = tank.parentInventory;
        bool nested = false;
        try
        {
            source = LbVessel(); LbGrade(source, 2, 1000000); LbGrade(tank, 2, 500000);
            GameInventory[] top = { EmporiumEntry.Instance.invElement, EmporiumEntry.Instance.showcaseElement, EmporiumEntry.Instance.backInvinvElement };
            GameInventory tankParent = tank.parentInventory ?? throw new FixtureUnavailable("Awarded tank has no actual native parent");
            if (!top.Any(inventory => ReferenceEquals(inventory, tankParent)))
                throw new FixtureUnavailable("Lock-isolation fixture requires the tank in an unchanged native top-level inventory");
            if (ReferenceEquals(source.parentInventory, tankParent) &&
                !top.Where(inventory => !ReferenceEquals(inventory, tankParent)).Any(inventory => LbTryPlace(inventory, source)))
                throw new FixtureUnavailable("No legal native SlotMarker in a different top-level inventory can isolate the source-side lock; not tested");
            GameInventory sourceParent = source.parentInventory ?? throw new FixtureUnavailable("Isolated source has no actual native parent");
            LbRequire("single-lock-distinct-top-level-parents", "Source and tank occupy different real top-level Mono inventory objects", new
            {
                source = sourceParent.identifier, target = tankParent.identifier,
                sourceTopIndex = Array.FindIndex(top, inventory => ReferenceEquals(inventory, sourceParent)),
                targetTopIndex = Array.FindIndex(top, inventory => ReferenceEquals(inventory, tankParent)),
                sameParent = ReferenceEquals(sourceParent, tankParent)
            }, !ReferenceEquals(sourceParent, tankParent) && top.Any(inventory => ReferenceEquals(inventory, sourceParent)) &&
                top.Any(inventory => ReferenceEquals(inventory, tankParent)));
            void LockGuards(string name, bool sourceRemove = false, bool sourceInsert = false, bool targetRemove = false, bool targetInsert = false)
            {
                LbRequire(name + ".isolated-guard-vector", new { sourceRemove, sourceInsert, targetRemove, targetInsert, distinctParents = true, frozen = false, grade = 2 },
                    new
                    {
                        sourceRemove = sourceParent.IsRemoveLocked(), sourceInsert = sourceParent.IsInsertLocked(),
                        targetRemove = tankParent.IsRemoveLocked(), targetInsert = tankParent.IsInsertLocked(),
                        sourceFrozen = LbService("inventory.frozen", new object[] { source }), targetFrozen = LbService("inventory.frozen", new object[] { tank })
                    }, ReferenceEquals(source.parentInventory, sourceParent) && ReferenceEquals(tank.parentInventory, tankParent) &&
                    !ReferenceEquals(sourceParent, tankParent) && sourceParent.IsRemoveLocked() == sourceRemove && sourceParent.IsInsertLocked() == sourceInsert &&
                    tankParent.IsRemoveLocked() == targetRemove && tankParent.IsInsertLocked() == targetInsert &&
                    LbService("inventory.frozen", new object[] { source }) is false && LbService("inventory.frozen", new object[] { tank }) is false &&
                    WaterHelper.GetFilterItem(source) == null && WaterHelper.GetFilterItem(tank) == null &&
                    GeneralHelper.IsItemOwned(source) && GeneralHelper.IsItemOwned(tank) && source.unitCount == 1 && tank.unitCount == 1 &&
                    WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(source)) == 2 &&
                    WaterFeatureHelper.GetPurityArrayIndex(WaterHelper.GetWaterPurity(tank)) == 2 &&
                    WaterHelper.GetTotalVolume(source) >= 100000 && WaterHelper.GetTotalVolume(tank) >= 100000 &&
                    WaterHelper.GetFreeCapacity(source) >= 100000 && WaterHelper.GetFreeCapacity(tank) >= 100000,
                    "Only the named flag may reject this action. Opposite-side locks, real freeze state, filters, ownership, tier and available volume/capacity are checked explicitly.");
            }
            void Pair(string name, int moved)
            {
                LbTransfer(name + ".deposit", source, tank, moved, () => Reflect.Call(Logistics, "PourResource", source, 100000));
                LbTransfer(name + ".withdraw", tank, source, moved, () => Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, source, 100000));
            }
            LockGuards("before-normal-control"); Pair("access-normal-control", 100000);
            using (LbService("inventory.acquire", new object[] { new[] { source }, "logistics-boundaries-source" }) as IDisposable
                ?? throw new FixtureUnavailable("Public source freeze lease unavailable")) Pair("source-frozen", 0);
            using (LbService("inventory.acquire", new object[] { new[] { tank }, "logistics-boundaries-tank" }) as IDisposable
                ?? throw new FixtureUnavailable("Public endpoint freeze lease unavailable")) Pair("endpoint-frozen", 0);
            LockGuards("before-source-remove-lock");
            bool sourceLock = sourceParent.overrideLockRemove;
            try
            {
                sourceParent.overrideLockRemove = true;
                LockGuards("actual-source-remove-lock", sourceRemove: true);
                LbTransfer("actual-source-remove-lock", source, tank, 0, () => Reflect.Call(Logistics, "PourResource", source, 100000));
            }
            finally { sourceParent.overrideLockRemove = sourceLock; }
            LockGuards("after-source-remove-lock"); Pair("source-remove-lock-restored-control", 100000);
            bool sourceInsert = sourceParent.overrideLockInsert;
            try
            {
                sourceParent.overrideLockInsert = true;
                LockGuards("actual-withdraw-receiver-insert-lock", sourceInsert: true);
                LbTransfer("actual-withdraw-receiver-insert-lock", tank, source, 0,
                    () => Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, source, 100000));
            }
            finally { sourceParent.overrideLockInsert = sourceInsert; }
            LockGuards("after-withdraw-receiver-insert-lock"); Pair("withdraw-receiver-lock-restored-control", 100000);
            GameInventory targetParent = tank.parentInventory!; bool targetInsert = targetParent.overrideLockInsert, targetRemove = targetParent.overrideLockRemove;
            try
            {
                targetParent.overrideLockInsert = true;
                LockGuards("actual-target-insert-lock", targetInsert: true);
                LbTransfer("actual-target-insert-lock", source, tank, 0, () => Reflect.Call(Logistics, "PourResource", source, 100000));
            }
            finally { targetParent.overrideLockInsert = targetInsert; }
            LockGuards("after-target-insert-lock"); Pair("target-insert-lock-restored-control", 100000);
            try
            {
                targetParent.overrideLockRemove = true;
                LockGuards("actual-network-source-remove-lock", targetRemove: true);
                LbTransfer("actual-network-source-remove-lock", tank, source, 0, () => Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, source, 100000));
            }
            finally { targetParent.overrideLockRemove = targetRemove; }
            LockGuards("after-network-source-remove-lock"); Pair("after-top-level-lock-release", 100000);
            ancestor = Spawn("smuggler_bay_mod"); LbPlaceTop(ancestor, "native-endpoint-ancestor");
            var inside = ancestor.contentWindow?.childElement as GameGridInventory ?? throw new FixtureUnavailable("Native ancestor grid missing");
            nested = LbTryPlace(inside, tank);
            if (!nested)
            {
                LbRequire("native-ancestor-rejection-preserves-custody", original?.identifier, tank.parentInventory?.identifier, tank.parentInventory == original);
                throw new FixtureUnavailable("Unchanged native SlotMarker refused tank hierarchy; ancestor variants not fabricated");
            }
            Pair("legally-nested-endpoint", 100000);
            using (LbService("inventory.acquire", new object[] { new[] { ancestor }, "logistics-boundaries-ancestor" }) as IDisposable
                ?? throw new FixtureUnavailable("Public ancestor freeze lease unavailable")) Pair("actual-ancestor-frozen", 0);
            bool prior = inside.overrideLockRemove;
            try { inside.overrideLockRemove = true; Pair("actual-ancestor-remove-lock", 0); }
            finally { inside.overrideLockRemove = prior; }
            Pair("after-ancestor-unlock", 100000);
        }
        finally
        {
            if (nested && tank.parentInventory != original && (original == null || !LbTryPlace(original, tank)))
                throw new InvalidOperationException("Endpoint cannot restore through original SlotMarker; stop dependent work");
            WaterHelper.EmptyContainer(tank);
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(source); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(ancestor);
        }
    }
    private void LbValued()
    {
        GameItem? source = null, target = null;
        int firstFrame = Time.frameCount;
        try
        {
            source = LbVessel(); target = LbVessel();
            foreach (string id in LbSpecies) WaterHelper.AddLiquid(source, id, 10000);
            var original = LbService("liquid.capture", new object[] { source }) as object[] ?? throw new InvalidOperationException("Public compatibility capture failed");
            var finalExpected = new JObject
            {
                ["emptyUid"] = source.uniqueId, ["fullUid"] = target.uniqueId,
                ["empty"] = LbFixedValuedLiquid((int)original[0], 0),
                ["full"] = LbFixedValuedLiquid(checked(WaterHelper.GetFreeCapacity(target) + WaterHelper.GetTotalVolume(target)), 10000)
            };
            var parts = ((object[][])original[1]).Select(part =>
            {
                int index = Array.IndexOf(LbSpecies, (string)part[0]);
                if (index < 0 || (int)part[1] != 10000) throw new InvalidOperationException("Native all-species fixture is invalid");
                return new object[] { part[0], part[1], (index + 1) * 3m, (index + 1) * 1m };
            }).ToArray();
            object[] valued = { original[0], parts };
            LbService("liquid.value", new object[] { source, valued });
            JObject capture = LbCapture(source);
            LbRequire("public-api-valued-compatibility-provenance", "36 native species x10000 parts; value=3*(explicit index+1), basis=(index+1)", capture,
                capture["Contents"]!.All(part => (int)part["Parts"]! == 10000 &&
                    (decimal?)part["Value"] == (Array.IndexOf(LbSpecies, (string)part["LiquidId"]!) + 1) * 3m &&
                    (decimal?)part["QualityBasis"] == (Array.IndexOf(LbSpecies, (string)part["LiquidId"]!) + 1) * 1m),
                "Public Forge liquid.value compatibility fixture, NOT natural production; every physical component already added with native WaterHelper");
            LbTransfer("valued-all-species-half-pour", source, target, 180000,
                () => LbService("liquid.pour", new object[] { source, target, 180000, null! }));
            LbTransfer("valued-all-species-quarter-return", target, source, 90000,
                () => LbService("liquid.pour", new object[] { target, source, 90000, null! }));
            LbTransfer("valued-all-species-full-native-pour", source, target, 270000,
                () => LbService("liquid.pour", new object[] { source, target, int.MaxValue, null! }));
            LbAssertFixedValued("valued-final-same-frame", finalExpected);
            LbRequire("valued-mixture-three-transfers-one-frame", firstFrame, Time.frameCount, firstFrame == Time.frameCount);
            R.environment["logisticsBoundariesReactiveMixture"] = new JObject
            {
                ["firstFrame"] = firstFrame, ["lastFrame"] = Time.frameCount, ["fixedFinalInvoice"] = finalExpected.DeepClone(),
                ["observedEmpty"] = LbCapture(source), ["observedFull"] = LbCapture(target),
                ["persistenceWitness"] = false,
                ["cleanupReason"] = "Acid/base, chlorine/water and sedimentation co-reactants are deliberately combined only for synchronous transfer compatibility. Ordinary native one-second chemistry changes this mixture; destroy these own test vessels after the verified invoice instead of adopting reacted metadata. Stable isolated witnesses are saved separately."
            };
        }
        finally
        {
            int[] ownUids = new[] { source?.uniqueId ?? 0, target?.uniqueId ?? 0 }.Where(uid => uid > 0).ToArray();
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(new List<GameItem> { source!, target! });
            if (ownUids.Length > 0)
                R.Check("logisticsboundaries.reactive-mixture-explicit-native-disposal", "Only this compatibility fixture's own vessels removed after observation", ownUids,
                    Store.FindAllItem().All(item => !ownUids.Contains(item.uniqueId)),
                    "Native cleanup is explicit disposal outside the conserved pours. No reaction is disabled and no changed mixture becomes an immutable save baseline.");
        }
    }
}
