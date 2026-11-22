using System.Collections;
using System.Globalization;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const string FlOwner = "nicokobo.automated_logistics";
    private const string FlFeeder = FlOwner + ".auto_feeder", FlEjector = FlOwner + ".output_ejector";
    private static readonly string[] FlNativeIds =
    {
        "furnace", "water_purifier", "moisture_farm", "desequencer", "printer", "evaporator", "hydroponic", "plant_dna_manipulator",
        "deal_maker", "recharger", "powerblock", "makeshift_reactor", "security_alarm", "storage_coordinator", "mirage_projector",
        "bottle_printer", "box_dispenser", "heating_plate", "blender", "chem_finisher", "cassette_player", "vending_machine",
        "vending_fountain", "wine_rack", "fridge", "livestock_feed_dispenser", "auto_butcher", "ras", "vacuum_robot"
    };
    private static readonly string[] FlModIds =
    {
        "nicokobo.mechcore.synthesis.material_refiner", "nicokobo.mechcore.synthesis.synthesizer",
        "nicokobo.mechcore.synthesis.precision_assembler", "nicokobo.mechcore.synthesis.liquid_infusion_tower",
        "nicokobo.mechcore.synthesis.food_processor", "nicokobo.forge.machines.universal_manufacturing_terminal"
    };
    private static readonly string[] FlLiquidIds =
    {
        "water", "heavy_metal", "organic_waste", "microplastic", "microbe", "chemical_contaminant", "physical_contaminant", "mineral",
        "dream_cap_mush", "dream_cap_extract", "plant_matter", "alcohol", "caffeine", "protein", "fat", "starch", "sugar", "coffee_mush",
        "contraceptive", "zyanide", "nightmare_extract", "nightmare_cap_mush", "chlorine", "synaprest", "fanta", "methylol", "reducing_agent",
        "acid", "base", "stabilizing_agent", "debris", "lipobond_agent", "livestock_steroid", "livestock_immunity", "wine_extract", "nomore"
    };
    private readonly JObject _flCoverage = new();
    private readonly JArray _flRecipeCoverage = new(), _flLiquidCoverage = new();
    private readonly JArray _flCases = new();
    private readonly HashSet<int> _flPersist = new();
    private GameItem? _flPlantArchive;
    private int _flUiAction;
    private Type FlFreeze => Reflect.Type("Nicokobo.ContentInterop.NativeInventoryFreezeApi");
    private Type FlLiquidApi => Reflect.Type("Nicokobo.ContentInterop.NativeLiquidApi");
    private Type FlPower => Reflect.Type("Nicokobo.ContentInterop.ForgePower");
    private JObject FlMachine(string id) => (JObject)_flCoverage[id]!;

    // This family deliberately keeps optional not-tested rows. A successful save
    // baseline means executed checks passed, not that the entire review matrix did.
    internal IEnumerable<object?> FullLogistics()
    {
        R.phase = "logisticsfull-current-runtime-denominator";
        R.environment["fullLogisticsScope"] = "Build26317870: explicit 29 native + 6 Mod device denominator, live recipe enumeration, real native slot/cycle/transfer entry points. All fixture materials are factory-created test grants, not an economic acquisition or unlock achievement chain. No production algorithm, debt or completion state is fabricated. UI button callbacks are programmatic native UI events, not mouse gestures. Optional not-tested rows remain visible and coverageComplete must be read separately from passed.";
        R.environment["fullLogisticsMachines"] = _flCoverage;
        R.environment["fullLogisticsRecipes"] = _flRecipeCoverage;
        R.environment["fullLogisticsLiquids"] = _flLiquidCoverage;
        R.environment["fullLogisticsCases"] = _flCases;
        foreach (string id in FlNativeIds.Concat(FlModIds))
            _flCoverage[id] = new JObject { ["id"] = id, ["origin"] = FlNativeIds.Contains(id) ? "native" : "mod",
                ["registered"] = DirectoryMaster.Has<GameItem>(id), ["fill"] = "not-tested", ["eject"] = "not-tested",
                ["power"] = "not-tested", ["nativeWork"] = "not-tested", ["reason"] = "No workload has run yet" };
        string[] liveIds = DirectoryMaster.GetIdentifierList<GameItem>().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        R.environment["fullLogisticsRuntimeItemIds"] = liveIds;
        R.Check("logisticsfull.denominator-29-native-6-mod", new { native = 29, mod = 6, allRegistered = true },
            new { native = FlNativeIds.Length, mod = FlModIds.Length, missing = FlNativeIds.Concat(FlModIds).Where(id => !liveIds.Contains(id)).ToArray() },
            FlNativeIds.Length == 29 && FlModIds.Length == 6 && FlNativeIds.Concat(FlModIds).All(liveIds.Contains));
        R.Check("logisticsfull.denominator-36-liquid-species", FlLiquidIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Liquid.Liquids.Select(liquid => liquid.identifier).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            FlLiquidIds.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(Liquid.Liquids.Select(liquid => liquid.identifier).OrderBy(id => id, StringComparer.Ordinal)));
        PrepareLogisticsNetworks();
        float readyUntil = Time.realtimeSinceStartup + 5f;
        while (Reflect.Call(Logistics, "ResourceRegion") == null || Reflect.Call(Logistics, "ResourceWaterTank") == null || Reflect.Call(Logistics, "PowerRegion") == null)
        { if (Time.realtimeSinceStartup >= readyUntil) throw new FixtureUnavailable("Real awarded network endpoints are unavailable"); yield return null; }
        foreach (object? wait in FlCase("L01-target-admission", () => FlAction(() => FlRun("L01", FlTargetAdmission)))) yield return wait;
        foreach (object? wait in FlCase("L02-api-fill-eject", FlStatisticsRefresh)) yield return wait;
        foreach (string variant in new[] { "uid", "stack", "focus" })
            foreach (object? wait in FlCase("L02-manual-" + variant, () => FlManualStatistics(variant))) yield return wait;
        foreach (object? wait in FlCase("power-devices", FlPowerDevices)) yield return wait;
        foreach (object? wait in FlCase("all-liquid-species", FlAllLiquids)) yield return wait;
        foreach (object? wait in FlCase("all-runtime-recipes", FlAllRecipes)) yield return wait;
        foreach (object? wait in FlCase("native-workloads", FlNativeWorkloads)) yield return wait;
        foreach (object? wait in FlCase("native-hoppers", FlHoppers)) yield return wait;
        foreach (object? wait in FlCase("S06-two-farm-night", FlS06Recovery)) yield return wait;
        FlRemainingCoverage();
        foreach (string id in new[] { FlOwner + ".void_container", FlOwner + ".void_tank", FlOwner + ".void_grid" })
            foreach (GameItem item in Store.FindAllItem().Where(item => item.identifier == id)) FlRetain(item);
        R.persistence["fullLogistics"] = new JObject
        {
            ["steamBuild"] = 26317870, ["gameRunId"] = Store.runID, ["slotId"] = Store.saveSlotId,
            ["machineCoverage"] = _flCoverage.DeepClone(), ["recipeCoverage"] = _flRecipeCoverage.DeepClone(),
            ["liquidCoverage"] = _flLiquidCoverage.DeepClone(), ["retained"] = FlRetainedSnapshot(),
            ["network"] = LogisticsState.DeepClone(), ["gridEnergy"] = GridTotal,
            ["plantHarvestArchive"] = _flPlantArchive == null ? JValue.CreateNull() : FlPlantArchiveSnapshot(_flPlantArchive),
            ["setupProvenance"] = "Factory-granted fixtures and actual native prerequisite Unlock, no purchase/payment acquisition coverage"
        };
    }

    internal IEnumerable<object?> FullLogisticsReload(JObject baseline)
    {
        R.phase = "logisticsfull-independent-process-es3-readback";
        var expected = baseline["persistence"]?["fullLogistics"] as JObject
            ?? throw new InvalidOperationException("Full logistics baseline is missing");
        R.Check("logisticsfull.reload-identity", new { mode = "logisticsfull-save", Store.runID, Store.saveSlotId }, expected,
            (string?)baseline["mode"] == "logisticsfull-save" && (string?)expected["gameRunId"] == Store.runID &&
            (int?)expected["slotId"] == Store.saveSlotId && (int?)expected["steamBuild"] == 26317870);
        foreach (JToken item in (JArray)expected["retained"]!) _flPersist.Add((int)item["uid"]!);
        var observed = FlRetainedSnapshot();
        bool retainedEqual = FlReloadPersistenceContract(baseline, (JArray)expected["retained"]!, observed);
        R.Check("logisticsfull.reload-native-uids-values-all-tags-ports-and-liquid-ledgers", expected["retained"], observed,
            retainedEqual,
            "Raw before/after remain recorded. Reload-only native SaveItemNode default-disabled tag omission is canonicalized. Only the two proven S06 target UIDs may project CONTAINER_TEMP_TAG.valueDouble after exact original ES3 tag/ledger proof and a bounded, exact native ambient-tick orbit. All other tags/payloads, ownership, UID, count, value, ports, 36 Parts/Value/QualityBasis remain strict. Runtime transfer comparisons never call this projection.");
        R.Check("logisticsfull.reload-network-debt-samples-pages-energy-receipts", expected["network"], LogisticsState,
            JToken.DeepEquals(expected["network"], LogisticsState) && (long?)expected["gridEnergy"] == GridTotal);
        if (expected["plantHarvestArchive"] is JObject archiveExpected)
        {
            GameItem archive = Store.FindAllItem(false).Single(item => item.uniqueId == (int)archiveExpected["uid"]!);
            JObject archiveActual = FlPlantArchiveSnapshot(archive);
            R.Check("logisticsfull.reload-real-harvest-archive-pages", archiveExpected, archiveActual, JToken.DeepEquals(archiveExpected, archiveActual),
                "Native two-page archive membership for all retained actual plant outputs and returned seeds; no replacement factory or reconstruction.");
        }
        R.persistence["fullLogistics"] = expected.DeepClone();
        yield return null;
    }

    private static bool FlReloadDefaultDisabledTag(JToken value)
    {
        string[] fields = { "valueEnabled", "valueInt", "valueLong", "valueFloat", "valueDouble", "valueBool", "valueString" };
        if (value is not JObject tag || tag.Count != fields.Length || fields.Any(field => tag.Property(field) == null) ||
            tag["valueEnabled"]?.Type != JTokenType.Boolean || (bool)tag["valueEnabled"]! ||
            tag["valueBool"]?.Type != JTokenType.Boolean || (bool)tag["valueBool"]!) return false;
        foreach (string field in new[] { "valueInt", "valueLong", "valueFloat", "valueDouble" })
            if (tag[field]!.Type is not (JTokenType.Integer or JTokenType.Float) || (double)tag[field]! != 0.0) return false;
        return tag["valueString"]!.Type == JTokenType.Null || tag["valueString"]!.Type == JTokenType.String && (string?)tag["valueString"] == "";
    }
    // Readback projection only. In-memory production/transfer ledgers continue
    // to use raw FlItem/FlPhysical/FlInvoice and exact JToken.DeepEquals.
    private static JToken FlReloadProjection(JToken value, ISet<int>? ambientUids = null)
    {
        JToken copy = value.DeepClone();
        void Visit(JToken node)
        {
            if (node is JObject item && item["tags"] is JObject tags)
            {
                foreach (var tag in tags.Properties().ToArray()) if (FlReloadDefaultDisabledTag(tag.Value)) tag.Remove();
                if (ambientUids?.Contains((int?)item["uid"] ?? 0) == true && tags["CONTAINER_TEMP_TAG"] is JObject temperature)
                    temperature["valueDouble"] = 0.0;
            }
            foreach (JToken child in node.Children().ToArray()) Visit(child);
        }
        Visit(copy); return copy;
    }
    private static JToken FlReloadNativeTag(TagState value) => JObject.FromObject(new
    { value.valueEnabled, value.valueInt, value.valueLong, valueFloat = (double)value.valueFloat, value.valueDouble, value.valueBool, value.valueString });
    private static bool FlReloadOriginalTemperature(JArray nodes, int uid, double initial, double decoded, out JToken? rawTag)
    {
        JObject[] matches = nodes.OfType<JObject>().Where(node => (int?)node["uniqueId"] == uid).ToArray();
        rawTag = null;
        if (matches.Length == 1 && matches[0]["_keys"] is JArray keys && matches[0]["_values"] is JArray values)
        {
            int[] indexes = keys.Select((key, index) => new { key, index }).Where(pair => (string?)pair.key == "CONTAINER_TEMP_TAG").Select(pair => pair.index).ToArray();
            if (indexes.Length == 1 && indexes[0] < values.Count) rawTag = values[indexes[0]];
        }
        // decoded comes from the original full inventory SaveState, exactly as
        // LoadGame reads it. Standalone TagState JSON has a different rounding
        // path. Only the observed native zero/one-ULP decode change is admitted;
        // the live item's subsequent temperature still needs an exact orbit.
        return rawTag?["valueDouble"]?.Type is JTokenType.Float or JTokenType.Integer &&
            BitConverter.DoubleToInt64Bits((double)rawTag["valueDouble"]!) == BitConverter.DoubleToInt64Bits(initial) &&
            !double.IsNaN(decoded) && !double.IsInfinity(decoded) &&
            Math.Abs((decimal)BitConverter.DoubleToInt64Bits(decoded) - BitConverter.DoubleToInt64Bits(initial)) <= 1m;
    }
    private static JObject FlReloadNativeTags(TagSystem state) => new(state.dict.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => new JProperty(pair.Key, FlReloadNativeTag(pair.Value))));
    private static bool FlReloadExactAmbientTicks(double initial, double observed, int parts, int maximum, out int ticks)
    {
        ticks = -1;
        if (parts <= 0 || maximum < 0 || double.IsNaN(initial) || double.IsInfinity(initial) || double.IsNaN(observed) || double.IsInfinity(observed) ||
            observed < Math.Min(initial, 26.0) || observed > Math.Max(initial, 26.0)) return false;
        double temperature = initial;
        // Independent arithmetic from Build26317870's native contract. Do not
        // call GetCooledTemp/HandleAmbientTempDrop to manufacture the answer.
        double decay = Math.Exp(-(0.05 / (Math.Sqrt(parts) + 11.0)) * 125.0);
        for (int step = 0; step <= maximum; step++)
        {
            if (temperature == observed) { ticks = step; return true; }
            temperature = 26.0 + (temperature - 26.0) * decay;
        }
        return false;
    }
    private void FlReloadProjectionNegativeControls()
    {
        var zero = JObject.FromObject(new { valueEnabled = false, valueInt = 0, valueLong = 0L, valueFloat = 0f, valueDouble = 0.0, valueBool = false, valueString = "" });
        var emptyItem = new JObject { ["uid"] = 1, ["tags"] = new JObject() };
        var defaultItem = new JObject { ["uid"] = 1, ["tags"] = new JObject { ["sample"] = zero.DeepClone() } };
        bool zeroEquivalent = JToken.DeepEquals(FlReloadProjection(emptyItem), FlReloadProjection(defaultItem));
        var retained = new JObject();
        foreach (string field in new[] { "valueEnabled", "valueInt", "valueLong", "valueFloat", "valueDouble", "valueBool", "valueString" })
        {
            var changed = (JObject)zero.DeepClone();
            changed[field] = field == "valueString" ? new JValue("nondefault") : field is "valueEnabled" or "valueBool" ? new JValue(true) : new JValue(1);
            var item = new JObject { ["uid"] = 1, ["tags"] = new JObject { ["sample"] = changed } };
            retained[field] = !JToken.DeepEquals(FlReloadProjection(item), FlReloadProjection(emptyItem));
        }
        var unknown = (JObject)zero.DeepClone(); unknown["futurePayload"] = 1;
        bool unknownRetained = !FlReloadDefaultDisabledTag(unknown);
        R.Check("logisticsfull.reload-projection-default-tag-negative-controls", "Only all-seven-field default disabled state equals absence; each enabled/nondefault/unknown payload survives", new { zeroEquivalent, retained, unknownRetained },
            zeroEquivalent && retained.Properties().All(property => (bool)property.Value) && unknownRetained,
            "Pure comparison-contract controls, not game-state mutation or simulated production. Native SaveItemNode.OnBeforeSerialize tests the same seven payload fields.");
        const double initial = 25.09179478365556, target = 25.16117002390363;
        bool exact = FlReloadExactAmbientTicks(initial, target, 6680000, 33, out int ticks);
        bool tooFew = FlReloadExactAmbientTicks(initial, target, 6680000, 32, out _);
        bool reset = FlReloadExactAmbientTicks(initial, 24.0, 6680000, 100, out _);
        bool overshoot = FlReloadExactAmbientTicks(initial, 26.000001, 6680000, 100, out _);
        bool nearby = FlReloadExactAmbientTicks(initial, target + 0.000000001, 6680000, 100, out _);
        bool empty = FlReloadExactAmbientTicks(initial, target, 0, 100, out _);
        var temperatureTag = (JObject)zero.DeepClone(); temperatureTag["valueEnabled"] = true; temperatureTag["valueDouble"] = initial;
        var before = new JObject { ["uid"] = 1, ["tags"] = new JObject { ["CONTAINER_TEMP_TAG"] = temperatureTag } };
        var after = (JObject)before.DeepClone(); after["tags"]!["CONTAINER_TEMP_TAG"]!["valueDouble"] = target;
        bool unapprovedRetained = !JToken.DeepEquals(FlReloadProjection(before, new HashSet<int>()), FlReloadProjection(after, new HashSet<int>()));
        after["tags"]!["CONTAINER_TEMP_TAG"]!["valueInt"] = 1;
        bool otherPayloadRetained = !JToken.DeepEquals(FlReloadProjection(before, new HashSet<int> { 1 }), FlReloadProjection(after, new HashSet<int> { 1 }));
        R.Check("logisticsfull.reload-projection-ambient-negative-controls", "Exact 33-step orbit only; reset/overshoot/nearby/off-bound/empty/unapproved/other-payload changes rejected",
            new { exact, ticks, tooFew, reset, overshoot, nearby, empty, unapprovedRetained, otherPayloadRetained },
            exact && ticks == 33 && !tooFew && !reset && !overshoot && !nearby && !empty && unapprovedRetained && otherPayloadRetained);
    }
    private bool FlReloadPersistenceContract(JObject baseline, JArray expected, JArray observed)
    {
        FlReloadProjectionNegativeControls();
        var evidence = new JObject { ["build"] = 26317870, ["rawExpected"] = expected.DeepClone(), ["rawObserved"] = observed.DeepClone(),
            ["defaultTagRule"] = "SaveItemNode.OnBeforeSerialize omits only disabled + every numeric zero + bool false + null/empty string; OnAfterDeserialize rebuilds only stored keys",
            ["thermalRule"] = "Only baseline S06 target UIDs; original ES3 temperature/tags exact, unchanged full liquid ledger/ownership and native callback; exact discrete ambient orbit bounded by scaled level time, no epsilon" };
        R.environment["fullLogisticsReloadPersistenceContract"] = evidence;
        string file = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId));
        string hash = Reflect.HashFile(file);
        bool source = hash.Equals((string?)baseline["persistence"]?["saveSha256"], StringComparison.OrdinalIgnoreCase);
        evidence["file"] = file; evidence["sha256"] = hash;
        R.Check("logisticsfull.reload-original-es3-bytes-for-tag-and-temperature-proof", baseline["persistence"]?["saveSha256"], hash, source,
            "Read-only native SaveFiles.ReadStoreForPreview and SaveState node parsing below use the original unmodified ES3 bytes, never a replacement save.");
        if (!source) throw new InvalidOperationException("Original FullLog ES3 bytes changed; no readback normalization is permitted");
        var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", new ES3Settings(file, ES3.Location.File))!;
        string[] jsonFields = { "mainInvJSON", "backInvJSON", "trashcanInvJSON", "docInvJSON", "cassettePlayerInvJSON", "showcaseInvJSON", "soldInvJSON", "hirelingInvJSON", "hiddenInvJSON", "vendingMachineInvJSON", "vendingFountainInvJSON" };
        SaveItemNode[] nodes = jsonFields.Select(field => (string?)Reflect.Get(saved, field)).Where(json => !string.IsNullOrEmpty(json))
            .SelectMany(json => JsonUtility.FromJson<SaveState>(json!).saveItems).ToArray();
        var rawRows = new JArray(); evidence["serializedTagRows"] = rawRows;
        bool tagsPreserved = saved.runID == Store.runID && saved.saveSlotId == Store.saveSlotId;
        var expectedItems = expected.OfType<JObject>().SelectMany(item => new[] { item }.Concat((item["children"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()))
            .GroupBy(item => (int)item["uid"]!).OrderBy(group => group.Key).ToArray();
        foreach (var group in expectedItems)
        {
            var matches = nodes.Where(node => node.uniqueId == group.Key).ToArray();
            JObject? raw = matches.Length == 1 ? FlReloadNativeTags(matches[0].itemState) : null;
            bool valid = matches.Length == 1 && group.All(item => matches[0].identifier == (string?)item["id"] && matches[0].unitCount == (int?)item["count"] &&
                matches[0].unitValue == (long?)item["value"] && matches[0].unitBaseValue == (long?)item["baseValue"] &&
                JToken.DeepEquals(FlReloadProjection(new JObject { ["tags"] = item["tags"]!.DeepClone() }), FlReloadProjection(new JObject { ["tags"] = raw!.DeepClone() })));
            rawRows.Add(new JObject { ["uid"] = group.Key, ["nodeMatches"] = matches.Length, ["rawTags"] = raw, ["valid"] = valid });
            R.Check("logisticsfull.reload-native-encoded-nondefault-tags." + group.Key, "Actual ES3 node preserves identifier/count/value and every nondefault tag payload", rawRows.Last, valid,
                "Native SaveItemNode.OnAfterDeserialize executes on actual saved JSON; SaveManager.DecodeNodesInternal clones that itemState into state/modifiedState before native callbacks. No detached GameItem factory or DecodeNodes is invoked by this reader.");
            tagsPreserved &= valid;
        }
        int[] targetUids = ((baseline["persistence"]?["fullLogisticsS06"]?["targetBefore"] as JArray)?.Select(item => (int?)item["uid"] ?? 0) ?? Enumerable.Empty<int>()).ToArray();
        bool targetDenominator = targetUids.Length == 2 && targetUids.All(uid => uid > 0) && targetUids.Distinct().Count() == 2;
        R.Check("logisticsfull.reload-ambient-scope-two-S06-targets", "Exactly two distinct original S06 target UIDs", targetUids, targetDenominator);
        float levelTime = Time.timeSinceLevelLoad;
        bool sameDay = (int?)baseline["persistence"]?["day"] == StoreStation.GetDayCounter();
        int maximum = !float.IsNaN(levelTime) && !float.IsInfinity(levelTime) && levelTime >= 0 && levelTime < int.MaxValue - 2 ? (int)Math.Ceiling(levelTime) + 1 : -1;
        evidence["clock"] = JObject.FromObject(new { scaledTimeSinceLevelLoad = levelTime, Time.timeScale, maximumAmbientTicks = maximum, sameDay,
            rule = "EmporiumEntry.Update starts oneSecondTimer at zero and accumulates Time.deltaTime, dispatching at most once per accumulated scaled second; ceil(level time)+1 is a conservative finite bound. No wall-clock substitution or time-scale assignment." });
        var thermalRows = new JArray(); evidence["thermalRows"] = thermalRows;
        var approved = new HashSet<int>();
        var nativeTick = (Action<GameItem, GameInventory, SlotMarker>)Reflect.Get(typeof(ChemicalHelper), "containerTempTick")!;
        var rawMainNodes = (JArray)JObject.Parse(saved.mainInvJSON)["saveItems"]!;
        foreach (int uid in targetUids.Distinct())
        {
            JObject[] before = expected.OfType<JObject>().Where(item => (int?)item["uid"] == uid).ToArray();
            JObject[] after = observed.OfType<JObject>().Where(item => (int?)item["uid"] == uid).ToArray();
            GameItem[] live = Store.FindAllItem(false).Where(item => item.uniqueId == uid).ToArray();
            SaveItemNode[] encoded = nodes.Where(node => node.uniqueId == uid).ToArray();
            double initial = before.Length == 1 ? (double?)before[0]["tags"]?["CONTAINER_TEMP_TAG"]?["valueDouble"] ?? double.NaN : double.NaN;
            double current = after.Length == 1 ? (double?)after[0]["tags"]?["CONTAINER_TEMP_TAG"]?["valueDouble"] ?? double.NaN : double.NaN;
            // Unity's native JSON decoder can round a double by one ULP. Prove
            // the original disk scalar first, then use that same native decoder;
            // never admit an epsilon or substitute the current live temperature.
            double decodedInitial = encoded.Length == 1 && encoded[0].itemState.dict.TryGetValue("CONTAINER_TEMP_TAG", out var decodedTag) ? decodedTag.valueDouble : double.NaN;
            bool originalScalarExact = FlReloadOriginalTemperature(rawMainNodes, uid, initial, decodedInitial, out JToken? rawTag);
            int parts = before.Length == 1 ? (int?)before[0]["liquid"]?["TotalParts"] ?? 0 : 0;
            int ticks = -1;
            bool valid = targetDenominator && tagsPreserved && sameDay && maximum >= 0 && before.Length == 1 && after.Length == 1 && live.Length == 1 && encoded.Length == 1 &&
                live[0].identifier == "water_tank" && GeneralHelper.IsItemOwned(live[0]) && live[0].unitCount > 0 && live[0].onOneSecondItemFunc == nativeTick &&
                !live[0].IsTag("HEATING_TAG") && !encoded[0].itemState.IsTag("HEATING_TAG") &&
                (bool?)before[0]["tags"]?["CONTAINER_TEMP_TAG"]?["valueEnabled"] == true && (bool?)after[0]["tags"]?["CONTAINER_TEMP_TAG"]?["valueEnabled"] == true &&
                originalScalarExact && encoded[0].itemState.dict.TryGetValue("CONTAINER_TEMP_TAG", out var originalTemperature) && originalTemperature.valueEnabled && originalTemperature.valueDouble == decodedInitial &&
                JToken.DeepEquals(before[0]["liquid"], after[0]["liquid"]) &&
                parts == Liquid.Liquids.Sum(liquid => encoded[0].itemState.dict.TryGetValue(liquid.currentPart, out var tag) ? tag.valueInt : 0) &&
                FlReloadExactAmbientTicks(decodedInitial, current, parts, maximum, out ticks);
            var row = new JObject { ["uid"] = uid, ["initial"] = initial, ["observed"] = current, ["parts"] = parts,
                ["exactTicks"] = ticks, ["maximum"] = maximum, ["valid"] = valid,
                ["rawDiskTag"] = rawTag?.DeepClone(), ["originalScalarExact"] = originalScalarExact, ["decodedInitial"] = decodedInitial,
                ["rawEncodedTemperature"] = encoded.Length == 1 && encoded[0].itemState.dict.TryGetValue("CONTAINER_TEMP_TAG", out var rawTemperature) ? FlReloadNativeTag(rawTemperature) : JValue.CreateNull(),
                ["rawBeforeTemperature"] = before.Length == 1 ? before[0]["tags"]?["CONTAINER_TEMP_TAG"]?.DeepClone() : null,
                ["rawAfterTemperature"] = after.Length == 1 ? after[0]["tags"]?["CONTAINER_TEMP_TAG"]?.DeepClone() : null };
            if (valid)
            {
                var trajectory = new JArray(); double predicted = decodedInitial;
                double decay = Math.Exp(-(0.05 / (Math.Sqrt(parts) + 11.0)) * 125.0);
                for (int step = 0; step <= ticks + 1; step++)
                {
                    trajectory.Add(new JObject { ["tick"] = step, ["predicted"] = predicted, ["withinScaledClockBound"] = step <= maximum,
                        ["exactObservedMatch"] = predicted == current });
                    predicted = 26.0 + (predicted - 26.0) * decay;
                }
                row["exactTrajectoryThroughNextTick"] = trajectory;
            }
            thermalRows.Add(row);
            R.Check("logisticsfull.reload-original-S06-temperature-and-exact-native-tick-trajectory." + uid, "Exact saved temperature and only a clock-bounded integer native ambient trajectory; full unchanged 36-part/value/basis ledger and ownership", row, valid);
            if (valid) approved.Add(uid);
        }
        JToken projectedExpected = FlReloadProjection(expected, approved), projectedObserved = FlReloadProjection(observed, approved);
        evidence["approvedAmbientUids"] = new JArray(approved.OrderBy(uid => uid));
        evidence["projectedExpected"] = projectedExpected; evidence["projectedObserved"] = projectedObserved;
        return JToken.DeepEquals(projectedExpected, projectedObserved);
    }

    private void FlRun(string name, Action action)
    {
        R.phase = "logisticsfull:" + name;
        try { action(); }
        catch (FixtureUnavailable error) { R.Skip("logisticsfull." + name, error.Message, false); }
        catch (Exception error) { R.Check("logisticsfull." + name, "Real native action and independent readback", null, false, "No synthetic fallback is used", error.ToString()); }
    }
    private sealed class FlCaseStopped(string message) : Exception(message) { }
    private static IEnumerable<object?> FlAction(Action action) { action(); yield return null; }
    private IEnumerable<object?> FlCase(string name, Func<IEnumerable<object?>> create)
    {
        int firstCheck = R.checks.Count; var row = new JObject { ["name"] = name, ["status"] = "running", ["firstCheck"] = firstCheck };
        _flCases.Add(row); IEnumerator<object?>? work = null;
        R.phase = "logisticsfull-case:" + name;
        try
        {
            Exception? openingError = null;
            try { work = create().GetEnumerator(); }
            catch (Exception error) { openingError = error; }
            if (openingError != null)
            {
                row["exception"] = openingError.ToString();
                R.Check("logisticsfull.case." + name + ".open", "Independent native scenario opened", null, false, error: openingError.ToString());
            }
            else while (work != null)
            {
                bool moved = false; object? current = null; Exception? actionError = null;
                try { moved = work.MoveNext(); if (moved) current = work.Current; }
                catch (Exception error) { actionError = error; }
                if (actionError != null)
                {
                    row["exception"] = actionError.ToString(); row["stoppedAtPhase"] = R.phase;
                    if (actionError is not FlCaseStopped)
                        R.Check("logisticsfull.case." + name + ".execution", "Independent native scenario completed its observed actions", null, false,
                            "Unexpected scenario exceptions stay failed. The iterator is disposed before the next independent case; this is not a skip or a successful fallback.", actionError.ToString());
                    else if (!R.checks.Skip(firstCheck).Any(check => check.status == "failed"))
                        R.Check("logisticsfull.case." + name + ".stop-without-failure", "A required failed check precedes an intentional subcase stop", null, false, error: actionError.Message);
                    break;
                }
                if (!moved) break;
                yield return current;
            }
        }
        finally
        {
            try { work?.Dispose(); }
            catch (Exception error)
            {
                row["cleanupException"] = error.ToString();
                R.Check("logisticsfull.case." + name + ".cleanup", "Native scenario iterator/finally cleanup completed", null, false, error: error.ToString());
            }
            var checks = R.checks.Skip(firstCheck).ToArray();
            if (checks.Length == 0) R.Check("logisticsfull.case." + name + ".empty", "At least one explicit observation or declared unavailable prerequisite", null, false);
            row["lastCheckExclusive"] = R.checks.Count;
            row["status"] = R.checks.Skip(firstCheck).Any(check => check.status == "failed") ? "failed" : R.checks.Skip(firstCheck).Any(check => check.status == "not-tested") ? "partial" : "passed";
        }
    }
    private void FlCleanup(string name, params Action[] actions)
    {
        for (int index = 0; index < actions.Length; index++)
        {
            try { actions[index](); }
            catch (Exception error) { R.Check("logisticsfull.cleanup." + name + "." + index, "This fixture cleanup action completed", null, false, error: error.ToString()); }
        }
    }
    private void FlRetain(GameItem item) { Retain(item); _flPersist.Add(item.uniqueId); }
    private static GameGridInventory FlGrid(GameItem item) => item.contentWindow?.childElement as GameGridInventory
        ?? GraphUtils.FindAllChildrenType<GameGridInventory>(item).FirstOrDefault()
        ?? throw new FixtureUnavailable("No legal native grid in " + item.identifier);
    private static bool FlTryPlace(GameInventory destination, GameItem item)
    {
        var marker = destination.TryFindOneValidInventorySlot(item);
        return marker != null && marker.IsValid() && marker.inventory == destination && marker.numTransfer >= item.unitCount &&
            marker.TryAcceptOnce(item.unitCount) > 0 && item.parentInventory == destination;
    }
    private static JObject FlItem(GameItem item)
    {
        var tags = new JObject();
        foreach (var pair in item.state.dict.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var tag = pair.Value;
            tags[pair.Key] = JObject.FromObject(new { tag.valueEnabled, tag.valueInt, tag.valueLong, valueFloat = (double)tag.valueFloat,
                tag.valueDouble, tag.valueBool, tag.valueString });
        }
        return new JObject { ["uid"] = item.uniqueId, ["id"] = item.identifier, ["count"] = item.unitCount, ["value"] = item.unitValue,
            ["baseValue"] = item.unitBaseValue, ["owned"] = GeneralHelper.IsItemOwned(item), ["parentUid"] = item.parentInventory?.GetParentItem()?.uniqueId ?? 0,
            ["parentStorage"] = item.parentInventory?.identifier ?? "", ["tags"] = tags,
            ["shape"] = JObject.FromObject(new { item.modifiedShape.minX, item.modifiedShape.minY, item.modifiedShape.width, item.modifiedShape.height, item.modifiedShape.orientation, item.modifiedShape.flipped }) };
    }
    private JObject FlLiquid(GameItem vessel) => Reflect.Json(Reflect.Call(FlLiquidApi, "Capture", vessel)) as JObject
        ?? throw new FixtureUnavailable("Native liquid capture failed: " + vessel.identifier);
    private JArray FlRetainedSnapshot()
    {
        GameItem[] live = Store.FindAllItem(false).ToArray();
        var promised = new List<GameItem>(); bool complete = true;
        foreach (int uid in _flPersist.OrderBy(uid => uid))
        {
            GameItem[] matches = live.Where(item => item.uniqueId == uid).ToArray();
            bool valid = uid > 0 && matches.Length == 1 && matches[0].unitCount > 0 && GeneralHelper.IsItemOwned(matches[0]);
            R.Check("logisticsfull.persistence-promised-uid-custody." + uid, "Exactly one live, owned, positive-count promised UID", new { uid, matches = matches.Select(FlItem).ToArray() }, valid,
                "Only this family's explicit _flPersist promise is checked. Post-harvest products/seeds cannot disappear from a later baseline through live-set intersection; cleared temporary fixtures are not included.");
            complete &= valid;
            if (valid) promised.Add(matches[0]);
        }
        if (!complete) throw new InvalidOperationException("A promised FullLog UID disappeared or lost custody; refusing a reduced snapshot");
        GameItem[] items = promised.ToArray();
        return new JArray(items.Select(item =>
        {
            JObject state = FlItem(item);
            state["children"] = new JArray(item.FindAllChildItems().OrderBy(child => child.uniqueId).Select(FlItem));
            if (item.IsTag("LIQUID_CONTAINER_TAG")) state["liquid"] = FlLiquid(item);
            state["childLiquids"] = new JArray(item.FindAllChildItems().Where(child => child.IsTag("LIQUID_CONTAINER_TAG"))
                .OrderBy(child => child.uniqueId).Select(child => new JObject { ["uid"] = child.uniqueId, ["liquid"] = FlLiquid(child) }));
            if (FlNativeIds.Take(8).Contains(item.identifier) || FlModIds.Contains(item.identifier))
            {
                object ports = Reflect.Call(Logistics, "Inventory", item)!; var entries = new JObject();
                foreach (string port in new[] { "Battery", "Modules", "ItemInput", "Output" })
                    entries[port] = Reflect.Get(ports, port) is GameInventory inventory ? new JArray(inventory.childItems.Select(child => child.uniqueId).OrderBy(uid => uid)) : JValue.CreateNull();
                var liquids = new JObject();
                foreach (DictionaryEntry pair in (IDictionary)Reflect.Get(ports, "LiquidInputs")!)
                    liquids[(string)pair.Key] = new JArray(((GameInventory)pair.Value!).childItems.Select(child => child.uniqueId).OrderBy(uid => uid));
                entries["LiquidInputs"] = liquids; state["ports"] = entries;
            }
            return state;
        }));
    }
    private static JToken FlPhysical(GameItem item)
    { var state = FlItem(item); state.Remove("parentUid"); state.Remove("parentStorage"); state.Remove("shape"); return state; }

    private void FlTargetAdmission()
    {
        var grid = (GameGridInventory)Reflect.Call(Logistics, "ResourceRegion")!;
        var root = grid.GetParentItem() ?? throw new FixtureUnavailable("L01 awarded resource grid has no physical root");
        GameInventory original = root.parentInventory!;
        GameItem box = Spawn("storage_bay"), backpack = Spawn("backpack_small"), machine = Spawn("blender");
        Retain(box); Retain(backpack); Retain(machine);
        var boxGrid = FlGrid(box);
        var attempts = new JArray(); R.environment["L01LegalPlacements"] = attempts;
        void Refusal(string name, Action? arrange = null, Action? restore = null, Func<bool>? stillAllowed = null)
        {
            GameItem source = Spawn("wire"); Retain(source);
            JObject before = FlItem(source); JToken destinationBefore = new JArray(grid.childItems.OrderBy(item => item.uniqueId).Select(FlItem));
            IDisposable? lease = null;
            try
            {
                arrange?.Invoke();
                JToken result = Reflect.Json(stillAllowed == null ? Reflect.Call(Logistics, "DepositResource", source, 1) :
                    Reflect.Call(LogisticsRuntime, "DepositResource", source, 1, stillAllowed));
                R.Check("logisticsfull.L01." + name, new { moved = 0, source = before, destination = destinationBefore },
                    new { result, source = FlItem(source), destination = new JArray(grid.childItems.OrderBy(item => item.uniqueId).Select(FlItem)) },
                    (int?)result["Moved"] == 0 && (bool?)result["Indeterminate"] == false && JToken.DeepEquals(before, FlItem(source)) &&
                    JToken.DeepEquals(destinationBefore, new JArray(grid.childItems.OrderBy(item => item.uniqueId).Select(FlItem))),
                    "Real DepositResource and native UID/count/value/all-tag/parent readback; the root is either the original awarded endpoint or was inserted through a valid unmodified native SlotMarker.");
            }
            finally { lease?.Dispose(); restore?.Invoke(); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(source); }
        }
        void Frozen(string name, GameItem held)
        {
            using var lease = (IDisposable?)Reflect.Call(FlFreeze, "TryAcquire", new[] { held }, "full-logistics-L01")
                ?? throw new FixtureUnavailable("L01 real inventory freeze lease unavailable");
            Refusal(name);
        }
        void Legal(string name)
        {
            GameItem source = Spawn("wire"); Retain(source); JToken state = FlPhysical(source);
            JToken result = Reflect.Json(Reflect.Call(Logistics, "DepositResource", source, 1));
            R.Check("logisticsfull.L01." + name, new { moved = 1, uid = source.uniqueId, physical = state }, new { result, item = FlItem(source) },
                (int?)result["Moved"] == 1 && source.parentInventory == grid && JToken.DeepEquals(state, FlPhysical(source)),
                "Unlocking must restore a genuine native move into the same target root.");
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(source);
        }
        try
        {
            // The awarded root is already a legal top-level endpoint. Exercise
            // its own freeze before trying nesting, so a native nesting refusal
            // cannot make every admission check unreachable.
            Legal("legal-awarded-top-level-root"); Frozen("frozen-top-level-root", root);
            IDisposable? commitFreeze = null; int directCalls = 0;
            try
            {
                Refusal("top-level-freezes-between-selection-and-commit", stillAllowed: () =>
                {
                    if (++directCalls == 3)
                        commitFreeze = (IDisposable?)Reflect.Call(FlFreeze, "TryAcquire", new[] { root }, "full-logistics-L01-commit")
                            ?? throw new FixtureUnavailable("The top-level target could not acquire its real freeze lease at commit");
                    return true;
                });
                R.Check("logisticsfull.L01.top-level-commit-freeze-fixture", "The third caller check acquired a real target freeze lease", new { directCalls, frozen = commitFreeze != null }, directCalls >= 3 && commitFreeze != null);
            }
            finally { commitFreeze?.Dispose(); }
            Legal("top-level-after-freeze-release");
            bool legal = FlTryPlace(boxGrid, root); attempts.Add(new JObject { ["parent"] = box.identifier, ["accepted"] = legal });
            if (!legal)
            {
                // Native storage_bay forbids ordinary container nesting in
                // Build26317870. The native smuggler bay's unchanged admission
                // permits these device IDs; still demand a real valid slot.
                if (root.parentInventory != original && !FlTryPlace(original, root)) throw new InvalidOperationException("L01 rejected native placement changed root custody");
                Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(box);
                box = Spawn("smuggler_bay_mod"); Retain(box); boxGrid = FlGrid(box);
                legal = FlTryPlace(boxGrid, root); attempts.Add(new JObject { ["parent"] = box.identifier, ["accepted"] = legal });
            }
            if (legal)
            {
                Legal("legal-owned-container"); Frozen("frozen-root", root); Frozen("frozen-ancestor", box);
                bool prior = boxGrid.overrideLockRemove;
                Refusal("ancestor-remove-lock", () => boxGrid.overrideLockRemove = true, () => boxGrid.overrideLockRemove = prior);
                Legal("unlocked-root");
                int calls = 0;
                Refusal("ancestor-changes-between-selection-and-commit", restore: () => boxGrid.overrideLockRemove = prior,
                    stillAllowed: () => { if (++calls == 3) boxGrid.overrideLockRemove = true; return true; });
                R.Check("logisticsfull.L01.commit-recheck-fixture", "Actual ancestor lock changed on third caller condition check", calls, calls >= 3);
                Legal("after-commit-lock-release");
                if (!FlTryPlace(original, root)) throw new InvalidOperationException("L01 cannot restore the awarded root to native storage");
            }
            else
            {
                foreach (string caseName in new[] { "legal-owned-container", "frozen-root-in-container", "frozen-ancestor", "ancestor-remove-lock", "ancestor-changes-between-selection-and-commit" })
                    R.Skip("logisticsfull.L01." + caseName, "The unchanged native storage_bay and smuggler_bay_mod admission layers both reject this hierarchy. This nested variant is not applicable through those player entries; the top-level checks ran independently", false);
                if (root.parentInventory != original && !FlTryPlace(original, root)) throw new InvalidOperationException("L01 refused nesting changed root custody");
            }
            bool inBackpack = FlTryPlace(FlGrid(backpack), root); attempts.Add(new JObject { ["parent"] = backpack.identifier, ["accepted"] = inBackpack });
            if (inBackpack) { Refusal("ordinary-backpack"); if (!FlTryPlace(original, root)) throw new InvalidOperationException("Cannot restore L01 backpack root"); }
            else R.Skip("logisticsfull.L01.ordinary-backpack", "Native slot refuses this hierarchy; it was not fabricated", false);
            bool inMachine = false;
            foreach (string machineId in new[] { "blender", FlModIds[5], FlModIds[1] })
            {
                if (machine.identifier != machineId)
                { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(machine); machine = Spawn(machineId); Retain(machine); }
                foreach (GameGridInventory inside in GraphUtils.FindAllChildrenType<GameGridInventory>(machine))
                    if (FlTryPlace(inside, root)) { inMachine = true; break; }
                attempts.Add(new JObject { ["parent"] = machine.identifier, ["accepted"] = inMachine });
                if (inMachine) break;
            }
            if (inMachine) { Refusal("machine-internal-endpoint"); if (!FlTryPlace(original, root)) throw new InvalidOperationException("Cannot restore L01 machine root"); }
            else R.Skip("logisticsfull.L01.machine-internal-endpoint", "The unchanged blender, universal manufacturing terminal and synthesizer native admission layers all rejected the 3x3 root. This hierarchy is not applicable through those player entries; no fake tree or acceptance predicate was installed", false);
        }
        finally
        {
            boxGrid.overrideLockRemove = false;
            if (root.parentInventory != original && !FlTryPlace(original, root))
                throw new InvalidOperationException("L01 root restoration failed; retain evidence and stop dependent transfers");
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(box); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(backpack);
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(machine);
        }
    }

    private object FlWindow()
    {
        var open = (Delegate?)Reflect.Get(Reflect.Type("Nicokobo.AutomatedLogistics.Integration.NativeHooks"), "Open");
        return open?.Target ?? throw new FixtureUnavailable("The ordinary terminal window is not installed");
    }
    private static GameObject FlWindowRoot(object window) => (GameObject?)Reflect.Get(window, "_root")
        ?? throw new FixtureUnavailable("The actual logistics Canvas did not open");
    private static string[] FlTexts(object window) => FlWindowRoot(window).GetComponentsInChildren<TMP_Text>(false).Where(text => text.gameObject.activeInHierarchy).Select(text => text.text).ToArray();
    private void FlClick(object window, Func<string, bool> label, string requested = "Expected terminal control")
    {
        var all = FlWindowRoot(window).GetComponentsInChildren<Button>(false).Where(button => button.IsInteractable()).ToArray();
        var buttons = all.Where(button => label(button.gameObject.name)).ToArray();
        string name = "logisticsfull.L02.required-visible-button." + ++_flUiAction;
        R.Check(name, new { requested, matchingButtons = 1 },
            new { matchingButtons = buttons.Select(button => button.gameObject.name).ToArray(), visibleButtons = all.Select(button => button.gameObject.name).ToArray(), texts = FlTexts(window) },
            buttons.Length == 1,
            "A missing expected row after a real inventory change is a required regression failure, including stale-page consequences. Stop this UI subcase and clean its fixtures; later independent cases still run.");
        if (buttons.Length != 1) throw new FlCaseStopped(name + ": expected one actual terminal button; matched " + buttons.Length);
        buttons[0].onClick.Invoke(); Reflect.Call(window, "Update");
    }
    private JObject FlUiExpected(string search = "")
    {
        var views = Reflect.Each(Reflect.Call(Logistics, "ResourceContents")).Where(view => !(bool)Reflect.Get(view, "LiquidEndpoint")!).ToArray();
        var all = views.Select(view => (GameItem)Reflect.Get(view, "Item")!).ToArray();
        long cells = all.Sum(item => (long)Enumerable.Range(item.modifiedShape.minY, item.modifiedShape.maxY - item.modifiedShape.minY + 1)
            .Sum(y => Enumerable.Range(item.modifiedShape.minX, item.modifiedShape.maxX - item.modifiedShape.minX + 1).Count(x => item.modifiedShape.Get(x, y) == 1)));
        long capacity = Reflect.Each(Reflect.Call(Logistics, "ResourceContainers")).Where(view => !(bool)Reflect.Get(view, "Liquid")!)
            .SelectMany(view => Reflect.Each(Reflect.Get(view, "Inventories"))).Cast<GameGridInventory>().Sum(g => (long)g.inventoryShape.width * g.inventoryShape.height);
        var inspect = Reflect.Type("Nicokobo.AutomatedLogistics.Native.NativeItemInspectionApi", "Nicokobo.AutomatedLogistics");
        R.environment["fullLogisticsUiInspectionType"] = inspect.AssemblyQualifiedName;
        decimal value = 0; bool known = true;
        foreach (var item in all.Where(item => item.identifier.Contains(search, StringComparison.OrdinalIgnoreCase) || item.GetDisplayName(false).Contains(search, StringComparison.OrdinalIgnoreCase)))
        { object? unit = Reflect.Call(inspect, "CurrentUnitValue", item); if (unit == null) known = false; else value += Convert.ToDecimal(unit, CultureInfo.InvariantCulture) * item.unitCount; }
        return new JObject { ["types"] = all.Select(item => item.identifier).Distinct().Count(), ["entries"] = all.Length,
            ["items"] = all.Sum(item => (long)item.unitCount), ["occupied"] = cells, ["capacity"] = capacity,
            ["value"] = known ? value.ToString("0.###", CultureInfo.InvariantCulture) : "unknown", ["uids"] = new JArray(all.Select(item => item.uniqueId).OrderBy(uid => uid)) };
    }
    private void FlCheckStatistics(object window, string action, JObject before, string[] beforeText)
    {
        Reflect.Call(window, "Update"); Canvas.ForceUpdateCanvases();
        JObject expected = FlUiExpected((string)Reflect.Get(window, "_storageSearch")!); string[] texts = FlTexts(window);
        string zh = $"库存统计 · {expected["types"]} 种 / {expected["entries"]} 条 / {expected["items"]} 件";
        string en = $"Inventory · {expected["types"]} types / {expected["entries"]} entries / {expected["items"]} items";
        string cellsZh = $"虚空容器占格 {expected["occupied"]} / {expected["capacity"]}";
        string cellsEn = $"Void Container cells {expected["occupied"]} / {expected["capacity"]}";
        string value = (string)expected["value"]!;
        bool valueVisible = texts.Any(text => text.Contains("\n总价值 " + value, StringComparison.Ordinal) || text.Contains("\nTotal value " + value, StringComparison.Ordinal));
        R.Check("logisticsfull.L02." + action, expected, new { before, beforeText, texts, statisticsPage = Reflect.Get(window, "_statisticsPage") },
            (bool)Reflect.Get(window, "Visible")! && (bool)Reflect.Get(window, "_statisticsPage")! &&
            texts.Any(text => text == zh || text == en) && texts.Any(text => text == cellsZh || text == cellsEn) && valueVisible &&
            !JToken.DeepEquals(before, expected) && !beforeText.SequenceEqual(texts),
            "The real Canvas stays on inventory; ordinary TerminalWindow.Update renders actual TMP text after the native transaction. No event-only oracle, Render call or dirty flag assignment.");
    }
    private IEnumerable<object?> FlStatisticsRefresh()
    {
        object? window = null; RecipeFixture? fixture = null;
        GameItem? probeWire = null; var caseSources = new List<GameItem>(); var caseProducts = new List<GameItem>(); bool completed = false;
        R.phase = "logisticsfull-L02-open-real-statistics-canvas";
        try
        {
            object view = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", FlModIds[1])).Single(recipe => Reflect.Str(recipe, "Id") == FlOwner + ".make_ejector");
            fixture = CreateRecipeFixture(view); var f = fixture;
            Place(f.Modules!, Spawn(FlFeeder), "L02-feeder", f.Machine); Place(f.Modules!, Spawn(FlEjector), "L02-ejector", f.Machine);
            Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false); ObserveBatch(f, "L02-real-consumption", true);
            caseProducts.AddRange(f.Output.childItems);
            Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false);
            var sourceItems = caseSources;
            foreach (object rule in Reflect.Each(Reflect.Get(view, "Ingredients")))
                for (int i = 0; i < Reflect.Int(rule, "Amount"); i++)
                {
                    var item = Ingredient(rule); Retain(item); sourceItems.Add(item);
                    JToken result = Reflect.Json(Reflect.Call(Logistics, "DepositResource", item, 1));
                    if ((int?)result["Moved"] != 1) throw new FixtureUnavailable("L02 source cannot be deposited into the actual resource grid");
                }
            GameItem terminal = Store.FindAllItem().FirstOrDefault(item => item.identifier == FlOwner + ".network_terminal")
                ?? throw new FixtureUnavailable("The normal opening terminal was not awarded");
            window = FlWindow(); Reflect.Call(window, "OpenMachine", terminal, f.Machine); Reflect.Call(window, "Update");
            FlClick(window, label => label.StartsWith("补货模组 · ", StringComparison.Ordinal) || label.StartsWith("Restocking · ", StringComparison.Ordinal));
            FlClick(window, label => label.StartsWith("弹出模组 · ", StringComparison.Ordinal) || label.StartsWith("Ejection · ", StringComparison.Ordinal));
            FlClick(window, label => label is "库存统计" or "Inventory");
            GameItem wire = probeWire = Spawn("wire"); Retain(wire);
            JObject before = FlUiExpected(); string[] text = FlTexts(window); JToken physical = FlPhysical(wire);
            JToken deposit = Reflect.Json(Reflect.Call(Logistics, "DepositResource", wire, 1));
            R.Check("logisticsfull.L02.deposit-ledger", new { moved = 1, physical }, new { deposit, physical = FlPhysical(wire) },
                (int?)deposit["Moved"] == 1 && JToken.DeepEquals(physical, FlPhysical(wire)));
            FlCheckStatistics(window, "deposit-while-inventory-open", before, text);
            before = FlUiExpected(); text = FlTexts(window);
            JToken withdraw = Reflect.Json(Reflect.Call(Logistics, "WithdrawResource", wire.uniqueId, 1, EmporiumEntry.Instance.invElement));
            R.Check("logisticsfull.L02.withdraw-ledger", new { moved = 1, physical }, new { withdraw, physical = FlPhysical(wire) },
                (int?)withdraw["Moved"] == 1 && JToken.DeepEquals(physical, FlPhysical(wire)));
            FlCheckStatistics(window, "withdraw-last-wire-while-inventory-open", before, text);
            before = FlUiExpected(); text = FlTexts(window); var sourcesBefore = sourceItems.Select(FlPhysical).ToArray();
            Reflect.Call(Logistics, "FillOnce", f.Machine);
            R.Check("logisticsfull.L02.fill-native-source-uids", "Each source UID remains owned with identical material state in the machine input",
                sourceItems.Select(FlItem).ToArray(), sourceItems.Select((item, i) => item.parentInventory == f.Input && JToken.DeepEquals(sourcesBefore[i], FlPhysical(item))).All(ok => ok));
            FlCheckStatistics(window, "machine-fill-while-inventory-open", before, text);
            before = FlUiExpected(); text = FlTexts(window); GameItem[] outputs = f.Output.childItems.ToArray(); var outputsBefore = outputs.Select(FlPhysical).ToArray();
            Reflect.Call(Logistics, "EjectOnce", f.Machine);
            R.Check("logisticsfull.L02.eject-native-output-uids", "Each real produced UID leaves output once with value/tags conserved", outputs.Select(FlItem).ToArray(),
                outputs.Length > 0 && f.Output.childItems.Count == 0 && outputs.Select((item, i) => item.parentInventory != f.Output && JToken.DeepEquals(outputsBefore[i], FlPhysical(item))).All(ok => ok));
            FlCheckStatistics(window, "machine-eject-while-inventory-open", before, text);
            // A real group button exposes UIDs; the oracle uses the actual row
            // labels, not the window's cached _resourceItems array.
            var first = outputs.FirstOrDefault();
            if (first != null)
            {
                var names = Reflect.Type("Nicokobo.AutomatedLogistics.Native.NativePresentationApi", "Nicokobo.AutomatedLogistics");
                string? registered = (string?)Reflect.Call(names, "GetRegisteredItemName", first.identifier, (bool)Reflect.Get(window, "_english")!);
                string display = string.IsNullOrWhiteSpace(registered) ? first.GetDisplayName(false) : registered!;
                FlClick(window, label => label.StartsWith(display + " × ", StringComparison.Ordinal), display + " × … (actual produced item group)");
                string[] rows = FlTexts(window);
                R.Check("logisticsfull.L02.real-instance-row", "Visible actual UID row", rows, outputs.Any(item => rows.Any(row => row.StartsWith("#" + item.uniqueId + " × ", StringComparison.Ordinal))));
            }
            Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false);
            FlRetain(f.Machine); f.Retained = true;
            completed = true;
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(wire);
            R.Skip("logisticsfull.L02.physical-mouse-and-ime", "Programmatic real tab/toggle buttons, native SlotMarker splits and actual TMP focus/caret are separate cases; physical mouse gestures and IME input are not exercised", false);
        }
        catch (FlCaseStopped) { }
        catch (FixtureUnavailable error) { R.Skip("logisticsfull.L02", error.Message, false); }
        catch (Exception error) { R.Check("logisticsfull.L02", "Real statistics text after all four moves", null, false, error: error.ToString()); }
        finally
        {
            FlCleanup("L02-api-fill-eject",
                () => { if (window != null) Reflect.Call(window, "Close"); },
                () => { if (fixture != null) Reflect.Call(Logistics, "ConfigureMachine", fixture.Machine, false, false); },
                () => Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(probeWire),
                () =>
                {
                    if (!completed)
                        foreach (var item in caseSources.Concat(caseProducts).Distinct()) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
                },
                () => fixture?.Dispose());
        }
        yield return null;
    }

    private IEnumerable<object?> FlManualStatistics(string variant)
    {
        object window = FlWindow(); var resources = (GameGridInventory)Reflect.Call(Logistics, "ResourceRegion")!;
        GameItem? first = null, replacement = null, stack = null, background = null;
        var createdSplitUids = new HashSet<int>();
        try
        {
            var terminal = Store.FindAllItem().First(item => item.identifier == FlOwner + ".network_terminal");
            Reflect.Call(Logistics, "OpenTerminal", terminal); Reflect.Call(window, "Update"); FlClick(window, label => label is "库存统计" or "Inventory");
            if (variant == "uid")
            {
            first = Spawn("wire"); Retain(first);
            JObject expected = FlUiExpected(); string[] texts = FlTexts(window);
            Place(resources, first, "L02-manual-native-slot-in");
            float deadline = Time.realtimeSinceStartup + .4f; while (Time.realtimeSinceStartup < deadline) yield return null;
            FlCheckStatistics(window, "manual-native-slot-in", expected, texts);
            // Select the actual wire group so a same-price UID replacement has a
            // visible independent oracle even though summary totals do not move.
            var presentation = Reflect.Type("Nicokobo.AutomatedLogistics.Native.NativePresentationApi", "Nicokobo.AutomatedLogistics");
            R.environment["fullLogisticsUiPresentationType"] = presentation.AssemblyQualifiedName;
            var name = (string?)Reflect.Call(presentation, "GetRegisteredItemName", first.identifier, (bool)Reflect.Get(window, "_english")!);
            string display = string.IsNullOrWhiteSpace(name) ? first.GetDisplayName(false) : name!;
            FlClick(window, label => label.StartsWith(display + " × ", StringComparison.Ordinal), display + " × … (actual manually inserted wire group)");
            expected = FlUiExpected(); texts = FlTexts(window);
            replacement = Spawn("wire"); Retain(replacement);
            int oldUid = first.uniqueId, newUid = replacement.uniqueId; long value = first.unitValue;
            Place(EmporiumEntry.Instance.invElement, first, "L02-manual-old-instance-out"); Place(resources, replacement, "L02-manual-same-price-new-instance-in");
            deadline = Time.realtimeSinceStartup + .4f; while (Time.realtimeSinceStartup < deadline) yield return null;
            FlCheckStatistics(window, "same-amount-same-price-different-uid", expected, texts);
            string[] instanceText = FlTexts(window);
            R.Check("logisticsfull.L02.uid-replacement-is-visible", new { oldUidAbsent = oldUid, newUidVisible = newUid, unitValue = value }, instanceText,
                replacement.unitValue == value && instanceText.Any(text => text.StartsWith("#" + newUid + " × ", StringComparison.Ordinal)) &&
                instanceText.All(text => !text.StartsWith("#" + oldUid + " × ", StringComparison.Ordinal)),
                "Two actual native SlotMarker moves in one frame leave amounts/value unchanged; the live displayed instance must still refresh.");
            expected = FlUiExpected(); texts = FlTexts(window); Place(EmporiumEntry.Instance.invElement, replacement, "L02-manual-native-slot-out");
            deadline = Time.realtimeSinceStartup + .4f; while (Time.realtimeSinceStartup < deadline) yield return null;
            FlCheckStatistics(window, "manual-native-slot-out", expected, texts);
            }
            if (variant == "stack")
            {
            JObject expected; string[] texts; float deadline;
            if (DirectoryMaster.Has<GameItem>("steel_sheet"))
            {
                stack = Spawn("steel_sheet", 3);
                if (stack.mayStackItemFunc == null) R.Skip("logisticsfull.L02.native-stack-split", "Registered steel_sheet no longer supports real native stacking; no stack delegate is fabricated", false);
                else
                {
                    Place(resources, stack, "L02-native-stack-in");
                    deadline = Time.realtimeSinceStartup + .4f; while (Time.realtimeSinceStartup < deadline) yield return null;
                    Reflect.Call(window, "Update"); expected = FlUiExpected(); texts = FlTexts(window);
                    int uid = stack.uniqueId, global = Store.FindAllItem().Where(item => item.identifier == "steel_sheet").Sum(item => item.unitCount);
                    var existingUids = Store.FindAllItem().Where(item => item.identifier == "steel_sheet").Select(item => item.uniqueId).ToHashSet();
                    var warehouse = EmporiumEntry.Instance.invElement;
                    SlotMarker? marker = null;
                    // Choose an actual empty native slot. Merging this one-unit
                    // fixture into an unrelated existing stack would complicate
                    // custody cleanup and change a preexisting fixture's count.
                    for (int y = warehouse.inventoryShape.minY; y <= warehouse.inventoryShape.maxY && marker == null; y++)
                    for (int x = warehouse.inventoryShape.minX; x <= warehouse.inventoryShape.maxX && marker == null; x++)
                    {
                        var shape = new GridShapeBuilder(stack.shape).SetPosition(x, y).Build();
                        var candidate = warehouse.TryInventorySlot(stack, 1, shape);
                        if (candidate != null && candidate.IsValid() && candidate.inventory == warehouse && candidate.targetItem == null && candidate.numTransfer >= 1) marker = candidate;
                    }
                    int moved = marker != null && marker.IsValid() ? marker.TryAcceptOnce(1) : 0;
                    foreach (var split in Store.FindAllItem().Where(item => item.identifier == "steel_sheet" && !existingUids.Contains(item.uniqueId)))
                        if (split.parentInventory == warehouse) createdSplitUids.Add(split.uniqueId);
                    deadline = Time.realtimeSinceStartup + .4f; while (Time.realtimeSinceStartup < deadline) yield return null;
                    R.Check("logisticsfull.L02.native-stack-split-ledger", new { moved = 1, sourceCount = 2, uid, global },
                        new { moved, source = FlItem(stack), global = Store.FindAllItem().Where(item => item.identifier == "steel_sheet").Sum(item => item.unitCount) },
                        moved == 1 && stack.unitCount == 2 && stack.uniqueId == uid && Store.FindAllItem().Where(item => item.identifier == "steel_sheet").Sum(item => item.unitCount) == global,
                        "The actual registered legacy native factory supports stacking; source count changes through SlotMarker.TryAcceptOnce(1), not SetUnitCount during the observed action.");
                    FlCheckStatistics(window, "native-stack-count-change", expected, texts);
                    foreach (var split in Store.FindAllItem().Where(item => createdSplitUids.Contains(item.uniqueId)).ToArray())
                        if (split.parentInventory == warehouse && split.identifier == "steel_sheet") Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(split);
                }
            }
            else R.Skip("logisticsfull.L02.native-stack-split", "No native registered steel_sheet stack fixture in this candidate", false);
            }
            if (variant == "focus")
            {
            JObject expected; string[] texts; float deadline;
            var field = FlWindowRoot(window).GetComponentsInChildren<TMP_InputField>(false).SingleOrDefault(input => input.name == "StorageSearch");
            R.Check("logisticsfull.RF03.required-visible-search-field", "Actual visible StorageSearch TMP input", new { present = field != null, texts = FlTexts(window) }, field != null);
            if (field == null) throw new FlCaseStopped("The required actual StorageSearch field is missing");
            field.Select(); field.ActivateInputField(); yield return null;
            field.text = "wire"; field.caretPosition = 2; field.selectionAnchorPosition = field.selectionFocusPosition = 2;
            int fieldId = field.GetInstanceID(), caret = field.caretPosition;
            background = Spawn("wire"); Retain(background); expected = FlUiExpected("wire"); texts = FlTexts(window);
            JToken deposit = Reflect.Json(Reflect.Call(Logistics, "DepositResource", background, 1));
            deadline = Time.realtimeSinceStartup + .4f; while (Time.realtimeSinceStartup < deadline) yield return null;
            Reflect.Call(window, "Update");
            bool alive = field != null && field.gameObject != null && field.gameObject.activeInHierarchy;
            R.Check("logisticsfull.RF03.background-stock-change-keeps-real-search-field", new { fieldId, query = "wire", caret, focused = true, moved = 1 },
                new { alive, fieldId = alive ? field!.GetInstanceID() : 0, query = alive ? field!.text : "destroyed", caret = alive ? field!.caretPosition : -1, focused = alive && field!.isFocused, deposit },
                (int?)deposit["Moved"] == 1 && alive && field!.GetInstanceID() == fieldId && field.text == "wire" && field.caretPosition == caret && field.isFocused,
                "Actual Select/ActivateInputField and live caret, followed by a real background Deposit and more than one polling interval. This does not claim physical IME/key-input coverage.");
            foreach (object? wait in CaptureEvidenceFrame("logisticsfull-RF03-focused-statistics")) yield return wait;
            if (alive) { field!.DeactivateInputField(); field.onEndEdit.Invoke(field.text); }
            Reflect.Call(window, "Update");
            FlCheckStatistics(window, "RF03-end-edit-renders-latest-stock", expected, texts);
            R.Check("logisticsfull.RF03.query-survives-end-edit", "wire", Reflect.Get(window, "_storageSearch"), (string?)Reflect.Get(window, "_storageSearch") == "wire");
            foreach (object? wait in CaptureEvidenceFrame("logisticsfull-statistics-after-end-edit")) yield return wait;
            }
        }
        finally
        {
            FlCleanup("L02-manual-" + variant,
                () => Reflect.Call(window, "Close"),
                () => Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(first), () => Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(replacement),
                () => Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(stack), () => Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(background),
                () =>
                {
                    foreach (var split in Store.FindAllItem().Where(item => createdSplitUids.Contains(item.uniqueId)).ToArray())
                        if (split.parentInventory == EmporiumEntry.Instance.invElement && split.identifier == "steel_sheet") Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(split);
                });
        }
    }

    private sealed class FlLocks : IDisposable
    {
        private readonly List<(GameInventory Grid, bool Insert, bool Remove)> _states = new();
        internal void Add(GameInventory grid, bool insert, bool remove)
        {
            if (_states.Any(state => state.Grid == grid)) return;
            _states.Add((grid, grid.overrideLockInsert, grid.overrideLockRemove));
            if (insert) grid.overrideLockInsert = true;
            if (remove) grid.overrideLockRemove = true;
        }
        public void Dispose()
        { foreach (var state in _states) { state.Grid.overrideLockInsert = state.Insert; state.Grid.overrideLockRemove = state.Remove; } }
    }
    private FlLocks FlOtherContainers(GameItem machine, GameItem? keep = null, bool insert = true, bool remove = true)
    {
        var locks = new FlLocks();
        bool online = (bool)Reflect.Get(Logistics, "ResourceNetworkOnline")!;
        foreach (var container in Reflect.Each(Reflect.Call(LogisticsRuntime.GetType(), "Containers", machine, null)).Cast<GameItem>())
            if (container != keep && !(online && (container.identifier == FlOwner + ".void_container" || container.identifier == FlOwner + ".void_tank")))
                foreach (var grid in GraphUtils.FindAllChildrenType<GameGridInventory>(container)) locks.Add(grid, insert, remove);
        return locks;
    }
    private static bool FlAutoSolid(GameItem item) => !item.IsGameItemType("MACHINE") && !item.IsGameItemType("STORAGE") &&
        !item.IsTag("CONTAINER_TAG") && !item.IsTag("LIQUID_CONTAINER_TAG") &&
        !GraphUtils.FindAllChildrenType<GameInventory>(item).Any();
    private JArray FlInvoice(IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var invoice = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var item in Store.FindAllItem().Where(item => selected.Contains(item.identifier)).Distinct())
        {
            var material = FlItem(item); material.Remove("uid"); material.Remove("count"); material.Remove("parentUid"); material.Remove("parentStorage"); material.Remove("shape");
            string key = material.ToString(Newtonsoft.Json.Formatting.None);
            invoice[key] = invoice.GetValueOrDefault(key) + item.unitCount;
        }
        return new JArray(invoice.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new JObject { ["material"] = JObject.Parse(pair.Key), ["units"] = pair.Value }));
    }
    private IEnumerable<object?> FlAllRecipes()
    {
        string[] machineIds = Reflect.Each(Reflect.Call(MachineCatalog, "MachineIds")).Cast<string>().OrderBy(id => id, StringComparer.Ordinal).ToArray();
        object[] recipes = machineIds.SelectMany(id => Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", id))).ToArray();
        string[] recipeIds = recipes.Select(recipe => Reflect.Str(recipe, "Id")).ToArray();
        R.Check("logisticsfull.runtime-all-recipe-ids-unique", "Nonempty actual registered catalog with no duplicate IDs", recipeIds,
            recipes.Length > 0 && recipeIds.Distinct(StringComparer.Ordinal).Count() == recipes.Length);
        R.environment["fullLogisticsRuntimeRecipeIds"] = recipeIds;
        foreach (object view in recipes)
        {
            string recipeId = Reflect.Str(view, "Id"), machineId = Reflect.Str(view, "MachineId");
            var row = new JObject { ["id"] = recipeId, ["machineId"] = machineId, ["source"] = "live MachineCatalog.Recipes",
                ["declaredOutput"] = Reflect.Str(view, "OutputItemId"), ["outputCount"] = Reflect.Int(view, "OutputCount"),
                ["waterParts"] = Reflect.Int(view, "WaterParts"), ["routes"] = new JArray(), ["nativeFailures"] = "not-tested" };
            _flRecipeCoverage.Add(row);
            for (int tier = 0; tier <= 2; tier++)
            {
                int route = tier;
                var routeRow = new JObject { ["tier"] = tier, ["name"] = new[] { "resource-network", "same-parent-container", "warehouse" }[tier], ["status"] = "not-tested" };
                ((JArray)row["routes"]!).Add(routeRow);
                int start = R.checks.Count;
                try
                {
                    R.phase = "logisticsfull-recipe:" + recipeId + ":tier" + route;
                    FlRecipeRoute(view, route, routeRow);
                    routeRow["status"] = R.checks.Skip(start).Any(check => check.status == "failed") ? "failed" : R.checks.Skip(start).Any(check => check.status == "not-tested") ? "partial" : "passed";
                }
                catch (FixtureUnavailable error) { routeRow["reason"] = error.Message; R.Skip("logisticsfull.recipe." + recipeId + ".tier" + tier, error.Message, false); }
                catch (Exception error) { routeRow["status"] = "failed"; routeRow["reason"] = error.ToString(); R.Check("logisticsfull.recipe." + recipeId + ".tier" + tier, "Real cycle, consumption-ledger restock and conserved ejection", null, false, error: error.ToString()); }
                finally { Reflect.Call(Logistics, "SetResourceNetworkEnabled", true); }
                yield return null;
            }
            int failureStart = R.checks.Count;
            try
            {
                FlRecipeRefusal(view, recipeId, "no-power", noPower: true);
                FlRecipeRefusal(view, recipeId, "missing-input", noPower: false);
                row["nativeFailures"] = R.checks.Skip(failureStart).Any(check => check.status == "failed") ? "failed" : "passed";
            }
            catch (FixtureUnavailable error) { row["nativeFailureReason"] = error.Message; R.Skip("logisticsfull.recipe-failure." + recipeId, error.Message, false); }
            catch (Exception error) { row["nativeFailures"] = "failed"; R.Check("logisticsfull.recipe-failure." + recipeId, "Real native rejection preserves inputs/power/liquid/output", null, false, error: error.ToString()); }
            if (_flCoverage[machineId] is JObject machine)
            {
                machine["fill"] = "see-each-runtime-recipe-route"; machine["eject"] = "see-each-runtime-recipe-route";
                machine["nativeWork"] = "see-each-runtime-recipe-route";
                machine["reason"] = "Every live registered recipe is enumerated independently; a failed/not-tested recipe must not be summarized as machine coverage passed";
            }
            yield return null;
        }
    }
    private void FlRecipeRoute(object view, int tier, JObject row)
    {
        using var f = CreateRecipeFixture(view);
        string name = Reflect.Str(view, "Id") + ".tier" + tier;
        if (f.Modules == null) throw new FixtureUnavailable("Live recipe machine has no native module inventory");
        var available = (Func<GameItem, bool>)Reflect.Get(view, "IsAvailableFor")!;
        if (!available(f.Machine)) throw new FixtureUnavailable("Actual recipe IsAvailableFor rejects the fully supplied legal factory fixture; no unlock/completion flag is injected");
        Place(f.Modules, Spawn(FlFeeder), "full-feeder:" + name, f.Machine); Place(f.Modules, Spawn(FlEjector), "full-ejector:" + name, f.Machine);
        Reflect.Call(Logistics, "SetResourceNetworkEnabled", tier == 0);
        Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false);
        int beforeCheck = R.checks.Count;
        ObserveBatch(f, "logisticsfull:" + name, true);
        if (R.checks.Skip(beforeCheck).Any(check => check.status == "failed")) throw new InvalidOperationException("Real native seed cycle failed its independent production ledger");
        Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false);
        JObject? debt = LogisticsState["Machines"]?[f.Machine.uniqueId.ToString()] as JObject;
        row["observedConsumption"] = debt?.DeepClone();
        var sources = new List<GameItem>(); GameItem? sourceBox = null, liquidSource = null;
        GameGridInventory? local = null;
        try
        {
            if (tier == 1)
            {
                sourceBox = Spawn("storage_bay");
                if (!FlTryPlace(f.Machine.parentInventory!, sourceBox)) throw new FixtureUnavailable("Native parent has no room for a same-parent storage_bay fixture");
                local = FlGrid(sourceBox);
            }
            using var other = FlOtherContainers(f.Machine, sourceBox);
            var inputIds = Reflect.Each(Reflect.Get(view, "Ingredients")).Select(rule => Reflect.Str(rule, "ItemId")).ToArray();
            var autoExpected = new Dictionary<string, int>(StringComparer.Ordinal);
            var manual = new JArray();
            foreach (object rule in Reflect.Each(Reflect.Get(view, "Ingredients")))
            {
                int amount = Reflect.Int(rule, "Amount");
                for (int i = 0; i < amount; i++)
                {
                    GameItem item = Ingredient(rule);
                    if (!FlAutoSolid(item)) { manual.Add(item.identifier); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); continue; }
                    autoExpected[item.identifier] = autoExpected.GetValueOrDefault(item.identifier) + 1;
                    sources.Add(item);
                    if (tier == 0)
                    {
                        Retain(item); JToken moved = Reflect.Json(Reflect.Call(Logistics, "DepositResource", item, 1));
                        if ((int?)moved["Moved"] != 1) throw new FixtureUnavailable("Native network cannot accept replenishment fixture " + item.identifier);
                    }
                    else if (!FlTryPlace(tier == 1 ? local! : EmporiumEntry.Instance.invElement, item))
                        throw new FixtureUnavailable("Native source placement rejected " + item.identifier + " at tier " + tier);
                }
            }
            row["manualInputs"] = manual;
            if (manual.Count > 0) R.Skip("logisticsfull.recipe-manual-inputs." + name, "Containers/machines/liquid shells intentionally require manual loading: " + string.Join(",", manual.Values<string>().Distinct()), false);
            int water = Reflect.Int(view, "WaterParts");
            GameItem? targetWater = f.Water;
            GameItem? tank = null;
            if (water > 0)
            {
                liquidSource = Spawn("water_jug"); WaterHelper.EmptyContainer(liquidSource); WaterHelper.AddPureWater(liquidSource, water);
                if (WaterHelper.GetTotalVolume(liquidSource) != water) throw new FixtureUnavailable("Recipe restock water exceeds a real native jug's capacity");
                if (tier == 0)
                {
                    Retain(liquidSource); tank = (GameItem)Reflect.Call(Logistics, "ResourceWaterTank")!;
                    if (WaterHelper.GetTotalVolume(tank) != 0) throw new FixtureUnavailable("Recipe water network was not empty; source-tier isolation is unavailable");
                    JToken pour = Reflect.Json(Reflect.Call(Logistics, "PourResource", liquidSource, water));
                    if ((int?)pour["PartsMoved"] != water) throw new FixtureUnavailable("Actual network rejected the recipe water replenishment source");
                }
                else if (!FlTryPlace(tier == 1 ? local! : EmporiumEntry.Instance.invElement, liquidSource))
                    throw new FixtureUnavailable("No legal native inventory position for the liquid source");
            }
            string[] ids = sources.Select(item => item.identifier).Concat(f.Output.childItems.Select(item => item.identifier)).Distinct().ToArray();
            JArray before = FlInvoice(ids); JObject? waterBefore = targetWater == null ? null : FlLiquidTotals(targetWater, tank ?? liquidSource!);
            JToken beforeDebt = LogisticsState["Machines"]?[f.Machine.uniqueId.ToString()]?.DeepClone() ?? JValue.CreateNull();
            Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false);
            Reflect.Call(Logistics, "FillOnce", f.Machine);
            Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false);
            var actual = f.Input.childItems.GroupBy(item => item.identifier).ToDictionary(group => group.Key, group => group.Sum(item => item.unitCount));
            bool filled = autoExpected.All(pair => actual.GetValueOrDefault(pair.Key) == pair.Value);
            R.Check("logisticsfull.recipe-fill." + name, new { input = autoExpected, conserved = before, waterParts = water },
                new { input = actual, conserved = FlInvoice(ids), sourceItems = sources.Select(FlItem).ToArray(), beforeDebt,
                    afterDebt = LogisticsState["Machines"]?[f.Machine.uniqueId.ToString()], liquid = targetWater == null ? null : FlLiquid(targetWater) },
                filled && JToken.DeepEquals(before, FlInvoice(ids)) && (targetWater == null || WaterHelper.GetTotalVolume(targetWater) == water) &&
                    (targetWater == null || JToken.DeepEquals(waterBefore, FlLiquidTotals(targetWater, tank ?? liquidSource!))),
                "Debt originates in the actual native seed cycle; input expectations come from the pre-action live recipe. Whole owned material invoice is independent of Move.Moved; stack splitting may allocate native UIDs but cannot duplicate units/value/state.");
            if (!filled) throw new InvalidOperationException("Actual FillOnce did not restore all automatically eligible inputs");
            GameItem[] products = f.Output.childItems.ToArray();
            JArray outputBefore = new(products.Select(FlItem)); JArray worldBefore = FlInvoice(ids);
            using (var blocked = new FlLocks())
            {
                foreach (var endpoint in Reflect.Each(Reflect.Call(Logistics, "ResourceContainers")))
                    foreach (var g in Reflect.Each(Reflect.Get(endpoint, "Inventories")).Cast<GameGridInventory>()) blocked.Add(g, true, false);
                if (local != null) blocked.Add(local, true, false);
                blocked.Add(EmporiumEntry.Instance.invElement, true, false);
                Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, true); Reflect.Call(Logistics, "EjectOnce", f.Machine);
                R.Check("logisticsfull.recipe-output-backpressure." + name, new { output = outputBefore, invoice = worldBefore },
                    new { output = new JArray(f.Output.childItems.Select(FlItem)), invoice = FlInvoice(ids) },
                    JToken.DeepEquals(outputBefore, new JArray(f.Output.childItems.Select(FlItem))) && JToken.DeepEquals(worldBefore, FlInvoice(ids)),
                    "Every current destination has a real insertion lock. No fake output item is injected, and the machine's produced items/UIDs/values stay in their original output.");
            }
            // A same-parent storage bay has native admission rules of its own.
            // Container/device products are not necessarily nestable there, so
            // prove its actual direct slot before requiring a local destination.
            // This is the same non-mutating slot search used by the transfer API;
            // no admission callback or item/parent state is replaced by the probe.
            var admission = new JArray();
            var rejectedLocal = new HashSet<int>();
            var fallbackPhysical = new Dictionary<int, JToken>();
            foreach (var product in products)
            {
                JObject SlotProof(GameGridInventory target)
                {
                    bool mayHaveSlot = target.MayHaveValidInventorySlot(product);
                    var slot = target.TryFindOneValidInventorySlot(product, false);
                    bool direct = slot != null && slot.IsValid() && slot.item == product && slot.inventory == target && slot.numTransfer > 0;
                    return new JObject { ["targetUid"] = target.GetParentItem()?.uniqueId ?? 0, ["targetStorage"] = target.identifier,
                        ["nativeMayHaveSlot"] = mayHaveSlot, ["directSlot"] = direct, ["numTransfer"] = slot?.numTransfer ?? 0,
                        ["slotTargetUid"] = slot?.inventory?.GetParentItem()?.uniqueId ?? 0, ["slotTargetStorage"] = slot?.inventory?.identifier ?? "" };
                }
                var proof = new JObject { ["uid"] = product.uniqueId, ["id"] = product.identifier, ["count"] = product.unitCount };
                if (local != null)
                {
                    JObject localProof = SlotProof(local); proof["local"] = localProof;
                    if ((bool?)localProof["directSlot"] != true)
                    {
                        rejectedLocal.Add(product.uniqueId);
                        fallbackPhysical[product.uniqueId] = FlPhysical(product);
                        proof["expectedDestination"] = "warehouse-after-native-local-rejection";
                    }
                    else proof["expectedDestination"] = "same-parent-container";
                }
                proof["warehouse"] = SlotProof(EmporiumEntry.Instance.invElement);
                admission.Add(proof);
            }
            row["outputAdmissionBeforeEject"] = admission;
            R.Check("logisticsfull.recipe-output-admission-readonly." + name, new { output = outputBefore, invoice = worldBefore },
                new { admission, output = new JArray(f.Output.childItems.Select(FlItem)), invoice = FlInvoice(ids) },
                JToken.DeepEquals(outputBefore, new JArray(f.Output.childItems.Select(FlItem))) && JToken.DeepEquals(worldBefore, FlInvoice(ids)),
                "Actual native MayHaveValidInventorySlot and direct TryFindOneValidInventorySlot(false), before EjectOnce. A local rejection is evidence for the documented warehouse fallback, never evidence that a missing product is acceptable.");
            Reflect.Call(Logistics, "EjectOnce", f.Machine); Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false);
            bool networkEligible = products.All(FlAutoSolid);
            bool destination = f.Output.childItems.Count == 0 && products.All(item => item.parentInventory != null && item.parentInventory != f.Output && GeneralHelper.IsItemOwned(item));
            if (tier == 0 && networkEligible)
                destination &= products.All(item => item.parentInventory?.GetParentItem()?.identifier == FlOwner + ".void_container");
            if (tier == 1) destination &= products.All(item => rejectedLocal.Contains(item.uniqueId)
                ? item.parentInventory == EmporiumEntry.Instance.invElement && JToken.DeepEquals(fallbackPhysical[item.uniqueId], FlPhysical(item))
                : item.parentInventory == local);
            if (tier == 2) destination &= products.All(item => item.parentInventory == EmporiumEntry.Instance.invElement);
            row["networkOutputEligible"] = networkEligible; row["outputAfter"] = new JArray(products.Select(FlItem));
            R.Check("logisticsfull.recipe-eject." + name, new { invoice = worldBefore, tier, networkEligible, outputEmpty = true, admission },
                new { output = new JArray(products.Select(FlItem)), invoice = FlInvoice(ids), bayCount = f.Output.childItems.Count },
                products.Length > 0 && destination && JToken.DeepEquals(worldBefore, FlInvoice(ids)),
                "Real cycle outputs, true destination readback and independently conserved complete material invoice. Tier1 still requires the local grid whenever its actual direct slot exists; a proven native local rejection requires the warehouse fallback with the same UID/count/value/tags. Resource storage deliberately rejects container/machine/liquid-shell products.");
            foreach (var product in products) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(product);
        }
        finally
        {
            Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false);
            foreach (var item in sources) if (item.parentInventory != f.Input) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(liquidSource);
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(sourceBox);
        }
    }
    private void FlRecipeRefusal(object view, string id, string name, bool noPower)
    {
        using var f = CreateRecipeFixture(view, battery: !noPower);
        if (!noPower && f.Inputs.Count == 0) throw new FixtureUnavailable("This live recipe has no removable solid input for a missing-input control");
        GameItem? removed = null;
        try
        {
            if (!noPower)
            {
                removed = f.Inputs[0].Item;
                if (removed.parentInventory != f.Input || !f.Input.Expel(removed)) throw new FixtureUnavailable("Native input fixture cannot be removed for missing-input control");
            }
            JToken before = TransactionState(f); long grid = GridTotal;
            NativeCycle(f.Machine);
            R.Check("logisticsfull.recipe-native-refusal." + id + "." + name, new { state = before, grid },
                new { state = TransactionState(f), grid = GridTotal }, JToken.DeepEquals(before, TransactionState(f)) && GridTotal == grid,
                "Actual installed onCycleEndSlotItemFunc with a genuinely absent native battery/input. No Process surrogate, produced flag or fabricated pending demand.");
        }
        finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(removed); }
    }

    private JObject FlLiquidTotals(params GameItem[] vessels)
    {
        var captures = vessels.Where(vessel => vessel != null).Distinct().Select(FlLiquid).ToArray(); var result = new JObject();
        foreach (string id in FlLiquidIds)
        {
            var parts = captures.SelectMany(capture => capture["Contents"]!.Children<JObject>()).Where(part => (string?)part["LiquidId"] == id && (int?)part["Parts"] > 0).ToArray();
            result[id] = new JObject { ["Parts"] = parts.Sum(part => (long)part["Parts"]!),
                ["Value"] = parts.Any(part => part["Value"]?.Type != JTokenType.Null) ? parts.Sum(part => (decimal?)part["Value"] ?? 0m) : JValue.CreateNull(),
                ["QualityBasis"] = parts.Any(part => part["QualityBasis"]?.Type != JTokenType.Null) ? parts.Sum(part => (decimal?)part["QualityBasis"] ?? 0m) : JValue.CreateNull() };
        }
        return result;
    }
    private IEnumerable<object?> FlAllLiquids()
    {
        var tank = (GameItem)Reflect.Call(Logistics, "ResourceWaterTank")!;
        if (WaterHelper.GetTotalVolume(tank) != 0) throw new FixtureUnavailable("All-liquid network fixture requires an initially empty awarded tank");
        foreach (string id in FlLiquidIds)
        {
            var row = new JObject { ["id"] = id, ["status"] = "not-tested" }; _flLiquidCoverage.Add(row);
            GameItem? source = null, target = null; int start = R.checks.Count;
            try
            {
                source = Spawn("water_jug"); target = Spawn("water_jug");
                WaterHelper.EmptyContainer(source); WaterHelper.EmptyContainer(target); WaterHelper.AddLiquid(source, "water", 100003);
                WaterHelper.AddLiquid(source, id, 17003); Retain(source); Retain(target);
                JObject before = FlLiquidTotals(source, tank, target);
                int sourceUid = source.uniqueId, tankUid = tank.uniqueId, targetUid = target.uniqueId;
                int sourceBefore = WaterHelper.GetTotalVolume(source);
                JToken pour = Reflect.Json(Reflect.Call(Logistics, "PourResource", source, 77777));
                R.Check("logisticsfull.liquid-pour." + id, new { moved = 77777, total = before, sourceUid, tankUid },
                    new { result = pour, source = FlLiquid(source), tank = FlLiquid(tank), total = FlLiquidTotals(source, tank, target) },
                    (int?)pour["PartsMoved"] == 77777 && (bool?)pour["Indeterminate"] == false &&
                    sourceBefore - WaterHelper.GetTotalVolume(source) == 77777 && WaterHelper.GetTotalVolume(tank) == 77777 &&
                    JToken.DeepEquals(before, FlLiquidTotals(source, tank, target)) && source.uniqueId == sourceUid && tank.uniqueId == tankUid,
                    "Odd-part native factory mixture containing water plus this actual registered species. Independently sum all 36 components and optional value/quality basis before and after.");
                JToken withdraw = Reflect.Json(Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, target, 77777));
                R.Check("logisticsfull.liquid-withdraw." + id, new { moved = 77777, total = before, targetUid, tankUid },
                    new { result = withdraw, target = FlLiquid(target), tank = FlLiquid(tank), total = FlLiquidTotals(source, tank, target) },
                    (int?)withdraw["PartsMoved"] == 77777 && (bool?)withdraw["Indeterminate"] == false && WaterHelper.GetTotalVolume(tank) == 0 &&
                    WaterHelper.GetTotalVolume(target) == 77777 && JToken.DeepEquals(before, FlLiquidTotals(source, tank, target)) && target.uniqueId == targetUid && tank.uniqueId == tankUid);
                row["before"] = before; row["after"] = FlLiquidTotals(source, tank, target);
                row["status"] = R.checks.Skip(start).Any(check => check.status == "failed") ? "failed" : "passed";
            }
            catch (FixtureUnavailable error) { row["reason"] = error.Message; R.Skip("logisticsfull.liquid." + id, error.Message, false); }
            catch (Exception error) { row["status"] = "failed"; row["reason"] = error.ToString(); R.Check("logisticsfull.liquid." + id, "Real water-mixture API round trip", null, false, error: error.ToString()); }
            finally
            {
                // Emptying is an explicit native disposal of this factory-granted
                // test mixture. It is not counted as a conserved pour.
                if (WaterHelper.GetTotalVolume(tank) != 0) WaterHelper.EmptyContainer(tank);
                Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(source); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(target);
            }
            yield return null;
        }
        R.Skip("logisticsfull.liquid-6x6-grades-filter-faults-and-manual-drain", "36 registered species round trips above do not cover every six-grade source/target combination, filter, partial write/rollback failure, or physical drag/drain gesture", false);
    }

    private void FlEnsureGrid(long requested, GameItem? emptySink = null)
    {
        var power = (GameGridInventory)Reflect.Call(Logistics, "PowerRegion")!;
        for (int attempts = 0; GridTotal != requested && attempts < 1000; attempts++)
        {
            long before = GridTotal;
            if (before > requested)
            {
                if (emptySink == null) throw new FixtureUnavailable("A real empty local battery is required to drain the grid through actual power payments");
                int cost = (int)Math.Min(100, before - requested);
                if (!PowerHelper.DrawPowerSource(emptySink, cost) || GridTotal != before - cost || Energy(emptySink) != 0)
                    throw new InvalidOperationException("Actual power payment could not establish the grid fixture");
            }
            else
            {
                int add = (int)Math.Min(100, requested - before);
                GameItem donor = PreBuiltItemHelper.EnergyCredit(add);
                try
                {
                    int energy = Energy(donor);
                    if (!FlTryPlace(power, donor)) throw new FixtureUnavailable("Native power inventory rejects the donor fixture");
                    Reflect.Call(LogisticsRuntime, "CollectGridEnergy");
                    R.Check("logisticsfull.power-native-grid-funding", new { gridAdded = add, batteryDebit = add },
                        new { gridAdded = GridTotal - before, batteryDebit = energy - Energy(donor), donor = FlItem(donor) },
                        GridTotal - before == add && energy - Energy(donor) == add,
                        "PreBuiltItemHelper creates the declared test energy; true native slot placement and CollectGridEnergy transfer it. No ledger assignment.");
                    if (GridTotal != before + add) throw new InvalidOperationException("Grid fixture failed physical energy conservation");
                }
                finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(donor); }
            }
        }
        if (GridTotal != requested) throw new FixtureUnavailable("Grid fixture could not reach its bounded target");
    }
    private IEnumerable<object?> FlPowerDevices()
    {
        var supported = new HashSet<string>(FlNativeIds.Take(8).Concat(FlModIds).Concat(new[]
        { "ras", "wine_rack", "mirage_projector", "security_alarm", "bottle_printer", "box_dispenser", "fridge", "vacuum_robot", "recharger", "powerblock" }), StringComparer.Ordinal);
        FlEnsureGrid(Math.Max(GridTotal, 200));
        foreach (string id in FlNativeIds.Concat(FlModIds))
        {
            string current = id;
            FlRun("power-device." + id, () =>
            {
                GameItem device = Spawn(current);
                try
                {
                    Retain(device); bool expected = supported.Contains(current);
                    bool actual = (bool)Reflect.Call(FlPower, "CanSupplyDevice", device)!;
                    R.Check("logisticsfull.power-support-classification." + current, expected, actual, expected == actual,
                        "Runtime capability is compared with the full static review denominator. Unsupported power is distinct from nonproduction logistics.");
                    if (current is "recharger" or "powerblock")
                    { FlMachine(current)["power"] = "generation-tested-separately"; return; }
                    if (!expected && current is not ("deal_maker" or "vending_machine" or "vending_fountain"))
                    { FlMachine(current)["power"] = "not-applicable-no-shared-power-contract"; return; }
                    var battery = (GameItem?)Reflect.Call(FlPower, "GetDevicePowerSource", device);
                    GameSlotInventory? slot = battery?.parentInventory as GameSlotInventory;
                    slot ??= MachineHelper.GetBodyStorage(device, 0) as GameSlotInventory;
                    if (slot == null)
                    {
                        FlMachine(current)["power"] = expected ? "not-tested" : "not-applicable-no-local-battery";
                        if (expected) throw new FixtureUnavailable("Expected powered device has no audited legal native battery slot");
                        return;
                    }
                    if (slot.childItem != null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(slot.childItem);
                    battery = PreBuiltItemHelper.EnergyCredit(0);
                    if (!FlTryPlace(slot, battery))
                    {
                        Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(battery);
                        FlMachine(current)["power"] = expected ? "not-tested" : "unsupported";
                        if (expected) throw new FixtureUnavailable("The actual power slot rejects a normal native energy credit");
                        return;
                    }
                    long grid = GridTotal; int uid = battery.uniqueId;
                    bool paid = PowerHelper.DrawPowerSource(battery, 1);
                    R.Check("logisticsfull.power-real-native-debit." + current,
                        new { success = expected, gridDebit = expected ? 1 : 0, local = 0, uid },
                        new { success = paid, gridDebit = grid - GridTotal, local = Energy(battery), uid = battery.uniqueId },
                        paid == expected && grid - GridTotal == (expected ? 1 : 0) && Energy(battery) == 0 && battery.uniqueId == uid,
                        "Actual PowerHelper.DrawPowerSource on this device's genuinely installed native battery; an unsupported consumer cannot borrow the shared grid.");
                    FlMachine(current)["power"] = expected ? "native-one-credit-grid-debit-tested" : "unsupported-native-negative-control-tested";
                }
                finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(device); }
            });
            yield return null;
        }
        GameItem sink = Spawn(FlModIds[1]); Retain(sink);
        var sinkSlot = (GameSlotInventory)MachineHelper.GetBodyStorage(sink, 0);
        GameItem sinkCell = PreBuiltItemHelper.EnergyCredit(0); Place(sinkSlot, sinkCell, "full-grid-real-sink", sink);
        try
        {
            (string Name, int Local, long Grid, int Cost, int LocalAfter, long GridAfter, bool Success)[] cases =
            {
                ("above-quarter-whole-local",26,40,20,6,40,true), ("exact-quarter-whole-grid",25,40,20,25,20,true),
                ("grid-short-whole-local",25,19,20,5,19,true), ("no-split-two-insufficient",2,3,4,2,3,false),
                ("cost-over-local-capacity",100,200,101,100,200,false), ("zero-cost",0,0,0,0,0,true)
            };
            foreach (var c in cases)
            {
                if (sinkCell.parentInventory != sinkSlot) throw new InvalidOperationException("Power fixture sink changed identity");
                if (Energy(sinkCell) != 0)
                { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(sinkCell); sinkCell = PreBuiltItemHelper.EnergyCredit(0); Place(sinkSlot, sinkCell, "full-grid-empty-sink", sink); }
                FlEnsureGrid(c.Grid, sinkCell);
                Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(sinkCell); sinkCell = PreBuiltItemHelper.EnergyCredit(c.Local); Place(sinkSlot, sinkCell, "full-grid-boundary", sink);
                int uid = sinkCell.uniqueId; bool paid = PowerHelper.DrawPowerSource(sinkCell, c.Cost);
                R.Check("logisticsfull.power-payment." + c.Name, new { success = c.Success, local = c.LocalAfter, grid = c.GridAfter, uid },
                    new { success = paid, local = Energy(sinkCell), grid = GridTotal, uid = sinkCell.uniqueId },
                    paid == c.Success && Energy(sinkCell) == c.LocalAfter && GridTotal == c.GridAfter && sinkCell.uniqueId == uid,
                    "Explicit independent 25%/whole-source expectations through the real PowerHelper overload; combined insufficient balances may not pay by splitting.");
                yield return null;
            }
            if (Energy(sinkCell) != 0) { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(sinkCell); sinkCell = PreBuiltItemHelper.EnergyCredit(0); Place(sinkSlot, sinkCell, "full-grid-donor-control", sink); }
            FlEnsureGrid(200, sinkCell);
            foreach (string id in new[] { "energy_credit", "energy_credit_ext", "energy_credit_breeder" })
            {
                GameItem cell = Spawn(id); var region = (GameGridInventory)Reflect.Call(Logistics, "PowerRegion")!;
                try
                {
                    int initial = Energy(cell), uid = cell.uniqueId; long grid = GridTotal;
                    Place(region, cell, "full-native-cell." + id); Reflect.Call(LogisticsRuntime, "CollectGridEnergy");
                    R.Check("logisticsfull.power-grid-physical-cell." + id, new { conserved = true, uid },
                        new { cell = FlItem(cell), gridCredit = GridTotal - grid, cellDebit = initial - Energy(cell) },
                        GridTotal - grid == initial - Energy(cell) && initial >= Energy(cell) && cell.uniqueId == uid,
                        "Actual three native battery IDs; grid cannot create more energy than the observed physical debit.");
                }
                finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(cell); }
                yield return null;
            }
        }
        finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(sink); }
        FlRun("recharger-native-cycle", () =>
        {
            GameItem charger = Spawn("recharger");
            try
            {
                var region = (GameGridInventory)Reflect.Call(Logistics, "PowerRegion")!;
                Place(region, charger, "full-grid-recharger");
                GameItem cell = Spawn("energy_credit_ext");
                int initial = Energy(cell);
                if (initial > 0) PowerHelper.TryRemoveEnergy(cell, initial, false);
                var storage = MachineHelper.GetBodyStorage(charger, 0) as GameInventory ?? throw new FixtureUnavailable("Native recharger storage missing");
                Place(storage, cell, "full-recharger-native-cell");
                long grid = GridTotal; int before = Energy(cell), uid = cell.uniqueId;
                NativeCycle(charger); Reflect.Call(LogisticsRuntime, "CollectGridEnergy");
                int expected = NetworkUpgrade.IsCommercialPowerUnlocked() ? 6 : 3;
                R.Check("logisticsfull.recharger-real-cycle-to-grid", new { generated = expected, retainedUid = uid },
                    new { gridCredit = GridTotal - grid, physicalDelta = Energy(cell) - before, uid = cell.uniqueId, Store.isPowerOn },
                    Store.isPowerOn && GridTotal - grid + Energy(cell) - before == expected && uid == cell.uniqueId,
                    "Actual recharger onCycle -> PowerHelper.ChargeBattery -> normal grid collection, with separate grid and physical battery accounts.");
                FlMachine("recharger")["nativeWork"] = "native-cycle-generated-energy-tested";
                FlMachine("recharger")["power"] = "native-cycle-to-grid-tested";
            }
            finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(charger); }
        });
        R.Skip("logisticsfull.power-tools-hypertech-ras-geometry-and-faults", "The 35-device power denominator and explicit payment cases are above. Separate tools, Hypertech limits, RAS daily generation/rotation/receipt replay and injected transfer faults are not exercised by these one-credit checks", false);
    }

    private GameItem FlNative(string id, out GameItem battery, bool feeder = false, bool ejector = false)
    {
        GameItem machine = Spawn(id);
        try
        {
            Retain(machine);
            var power = MachineHelper.GetBodyStorage(machine, 0) as GameSlotInventory ?? throw new FixtureUnavailable("Native battery slot missing: " + id);
            var modules = MachineHelper.GetBodyStorage(machine, 1) as GameInventory ?? throw new FixtureUnavailable("Native module inventory missing: " + id);
            battery = PreBuiltItemHelper.EnergyCredit(100); Place(power, battery, "full-native-power." + id, machine);
            if (feeder) Place(modules, Spawn(FlFeeder), "full-native-feeder." + id, machine);
            if (ejector) Place(modules, Spawn(FlEjector), "full-native-ejector." + id, machine);
            Reflect.Call(Logistics, "ConfigureMachine", machine, feeder, false);
            return machine;
        }
        catch { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(machine); throw; }
    }
    private IEnumerable<object?> FlNativeWorkloads()
    {
        FlRun("native-purifier-production-feed-and-collection", FlPurifierRoute);
        FlRun("native-evaporator-distillate", FlEvaporatorRoute);
        var progress = new JArray(); R.environment["fullLogisticsNativeProgressRecipes"] = progress;
        string[] chips = DirectoryMaster.GetIdentifierList<GameItem>("ModuleDirectory")
            .Where(id => id.StartsWith("crypto_module_", StringComparison.Ordinal) || id.StartsWith("printer_chip_", StringComparison.Ordinal)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        foreach (string chip in chips)
        {
            string machine = chip.StartsWith("crypto_", StringComparison.Ordinal) ? "desequencer" : "printer";
            var row = new JObject { ["id"] = chip, ["machine"] = machine, ["status"] = "not-tested" }; progress.Add(row);
            int before = R.checks.Count;
            FlRun("native-progress." + chip, () => { FlProgressRoute(machine, chip, row); row["status"] = "executed"; });
            if (R.checks.Skip(before).Any(check => check.status == "failed")) row["status"] = "failed";
            else if (R.checks.Skip(before).Any(check => check.status == "not-tested")) row["status"] = "not-tested";
            else row["status"] = "passed";
            yield return null;
        }
        foreach (string machine in new[] { "desequencer", "printer" })
        {
            FlMachine(machine)["fill"] = "network-route-per-native-chip-record";
            FlMachine(machine)["eject"] = "native-completed-output-per-chip-record";
            FlMachine(machine)["nativeWork"] = "see-native-progress-recipe-records";
            FlMachine(machine)["reason"] = "Native ModuleDirectory chips are enumerated; source/target tiers 1/2 and additional Mod-defined chip factories are not covered by this native-chip adapter";
            R.Skip("logisticsfull.native-progress-other-tiers." + machine, "Only the resource network replenishment route is executed for each stock ModuleDirectory chip; local/warehouse tiers, all purity/CNC/melee-batch variants and external chip additions need additional cases", false);
        }
        var plants = new JArray(); R.environment["fullLogisticsNativePlants"] = plants;
        FlEnsurePlantArchive();
        foreach (var plant in PlantRegistry.All.OrderBy(plant => plant.id, StringComparer.Ordinal))
        {
            var row = new JObject { ["id"] = plant.id, ["harvestId"] = plant.harvestItemId, ["status"] = "not-tested" }; plants.Add(row);
            int before = R.checks.Count;
            FlRun("native-hydroponic." + plant.id, () => FlHydroponicRoute(plant.id, row));
            row["status"] = R.checks.Skip(before).Any(check => check.status == "failed") ? "failed" : R.checks.Skip(before).Any(check => check.status == "not-tested") ? "not-tested" : "passed";
            yield return null;
        }
        int verifiedPlants = plants.OfType<JObject>().Count(plantRow => (string?)plantRow["status"] == "passed");
        bool allPlants = plants.Count > 0 && verifiedPlants == plants.Count;
        FlMachine("hydroponic")["fill"] = allPlants ? "native-growth-consumption-and-recorded-sample-per-plant" : "not-all-live-plant-fill-routes-verified";
        FlMachine("hydroponic")["eject"] = allPlants ? "native-harvest-conservation-per-plant" : "not-all-live-plant-harvests-verified";
        FlMachine("hydroponic")["nativeWork"] = "live-PlantRegistry-passed:" + verifiedPlants + "/" + plants.Count;
        FlMachine("hydroponic")["reason"] = "Read each live plant status: " + verifiedPlants + "/" + plants.Count + " actually passed. Variant genes, extra farm modules, replacement-vessel and out-of-band mixture invalidation remain separate not-tested cases";
        FlRun("native-dna-extraction-and-ejected-disk", FlDnaRoute);
    }
    private void FlPurifierRoute()
    {
        GameItem machine = FlNative("water_purifier", out GameItem cell, feeder: true, ejector: true);
        GameItem? input = null, collected = null, raw = null;
        try
        {
            input = Spawn("water_jug"); WaterHelper.EmptyContainer(input); WaterHelper.AddLiquid(input, "water", 980000); WaterHelper.AddLiquid(input, "physical_contaminant", 20000);
            var slot = (GameSlotInventory)MachineHelper.GetBodyStorage(machine, 2); Place(slot, input, "full-purifier-input");
            int uid = input.uniqueId, power = MachineryHelper.GetMachinePowerUsage(machine), initial = Energy(cell); long grid = GridTotal;
            NativeCycle(machine);
            var expected = new JObject { ["water"] = 990000, ["physical_contaminant"] = 10000 };
            R.Check("logisticsfull.purifier-native-production", new { parts = expected, volume = 1000000, debit = power, uid },
                new { parts = PositiveLiquidParts(input), volume = WaterHelper.GetTotalVolume(input), debit = initial - Energy(cell) + grid - GridTotal, uid = input.uniqueId },
                JToken.DeepEquals(expected, PositiveLiquidParts(input)) && WaterHelper.GetTotalVolume(input) == 1000000 && initial - Energy(cell) + grid - GridTotal == power && input.uniqueId == uid,
                "Independent one-litre 98% raw feed and native 99% base purity cap. Actual installed cycle earns the observed original-feed record.");
            var tank = (GameItem)Reflect.Call(Logistics, "ResourceWaterTank")!;
            if (WaterHelper.GetTotalVolume(tank) != 0) throw new FixtureUnavailable("Purifier collection route needs an empty awarded tank");
            JObject conserved = FlLiquidTotals(input, tank);
            Reflect.Call(Logistics, "ConfigureMachine", machine, true, true); Reflect.Call(Logistics, "EjectOnce", machine);
            R.Check("logisticsfull.purifier-completed-liquid-collection", new { sourceParts = 0, targetParts = 1000000, uid, total = conserved },
                new { source = FlLiquid(input), target = FlLiquid(tank), total = FlLiquidTotals(input, tank), debt = LogisticsState["Machines"]?[machine.uniqueId.ToString()] },
                WaterHelper.GetTotalVolume(input) == 0 && WaterHelper.GetTotalVolume(tank) == 1000000 && JToken.DeepEquals(conserved, FlLiquidTotals(input, tank)) && input.uniqueId == uid);
            collected = Spawn("water_jug"); WaterHelper.EmptyContainer(collected); Retain(collected);
            JToken taken = Reflect.Json(Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, collected, 1000000));
            if ((int?)taken["PartsMoved"] != 1000000) throw new FixtureUnavailable("Cannot empty the real tank into the collected-output fixture");
            raw = Spawn("water_jug"); WaterHelper.EmptyContainer(raw); WaterHelper.AddLiquid(raw, "water", 980000); WaterHelper.AddLiquid(raw, "physical_contaminant", 20000); Retain(raw);
            JToken poured = Reflect.Json(Reflect.Call(Logistics, "PourResource", raw, 1000000));
            if ((int?)poured["PartsMoved"] != 1000000) throw new FixtureUnavailable("Cannot load actual raw source into the empty resource tank");
            conserved = FlLiquidTotals(input, tank); Reflect.Call(Logistics, "ConfigureMachine", machine, true, false); Reflect.Call(Logistics, "FillOnce", machine);
            R.Check("logisticsfull.purifier-replenishes-only-real-withdrawal", new { targetParts = 1000000, uid, total = conserved },
                new { source = FlLiquid(tank), target = FlLiquid(input), total = FlLiquidTotals(input, tank), debt = LogisticsState["Machines"]?[machine.uniqueId.ToString()] },
                WaterHelper.GetTotalVolume(input) == 1000000 && input.uniqueId == uid && input.parentInventory == slot && WaterHelper.GetTotalVolume(tank) == 0 &&
                JToken.DeepEquals(conserved, FlLiquidTotals(input, tank)) && input.GetTagReadonly(Liquid.GetCurrentPartFromId("physical_contaminant")).GetInt() == 20000,
                "Only the actual automatically collected amount creates repayment. Native mixture/UID and original feed are read back; in-place purification itself did not fabricate consumption.");
            FlMachine("water_purifier")["fill"] = "actual-collection-debt-network-repayment-tested"; FlMachine("water_purifier")["eject"] = "native-cap-output-to-network-tested";
            FlMachine("water_purifier")["nativeWork"] = "98-to-99-percent-native-cycle-tested"; FlMachine("water_purifier")["reason"] = "One defined native purity case; other source tiers, unreachable targets and purifier-incompatible species remain not-tested";
        }
        finally
        {
            Reflect.Call(Logistics, "ConfigureMachine", machine, false, false);
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(machine); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(collected); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(raw);
        }
    }
    private void FlEvaporatorRoute()
    {
        GameItem machine = FlNative("evaporator", out GameItem cell, ejector: true);
        GameItem? feed = null, output = null, witness = null;
        try
        {
            var modules = (GameInventory)MachineHelper.GetBodyStorage(machine, 1);
            Place(modules, Spawn("system_module_quality"), "full-evaporator-quality", machine);
            feed = Spawn("water_jug"); output = Spawn("water_jug"); WaterHelper.EmptyContainer(feed); WaterHelper.EmptyContainer(output);
            WaterHelper.AddLiquid(feed, "alcohol", 300000); WaterHelper.AddLiquid(feed, "protein", 200000);
            Place((GameSlotInventory)MachineHelper.GetBodyStorage(machine, 2), feed, "full-evaporator-feed");
            Place((GameSlotInventory)MachineHelper.GetBodyStorage(machine, 3), output, "full-evaporator-output");
            int quality = Math.Clamp(MachineryHelper.GetCurrentQualityBonus(machine), 0, 100), cost = MachineryHelper.GetMachinePowerUsage(machine);
            int recovered = (int)Math.Round(300000 * (quality / 100.0)); int before = Energy(cell); long grid = GridTotal;
            NativeCycle(machine);
            R.Check("logisticsfull.evaporator-native-distillation", new { feedAlcohol = 0, feedProtein = 200000, recovered, energyDebit = cost },
                new { feed = FlLiquid(feed), output = FlLiquid(output), energyDebit = before - Energy(cell) + grid - GridTotal, quality },
                feed.GetTagReadonly(Liquid.GetCurrentPartFromId("alcohol")).GetInt() == 0 && feed.GetTagReadonly(Liquid.GetCurrentPartFromId("protein")).GetInt() == 200000 &&
                output.GetTagReadonly(Liquid.GetCurrentPartFromId("alcohol")).GetInt() == recovered && before - Energy(cell) + grid - GridTotal == cost,
                "Native evaporation emits an explicit unrecovered vapor loss of 300000-recovered parts; that production loss is separate from the following conserved logistics move.");
            if (recovered <= 0) throw new FixtureUnavailable("Legal native quality module produced no recoverable distillate");
            var tank = (GameItem)Reflect.Call(Logistics, "ResourceWaterTank")!;
            if (WaterHelper.GetTotalVolume(tank) != 0) throw new FixtureUnavailable("Pure chemical collection fixture requires an empty resource tank");
            JObject total = FlLiquidTotals(feed, output, tank); int feedUid = feed.uniqueId, outUid = output.uniqueId;
            Reflect.Call(Logistics, "ConfigureMachine", machine, false, true); Reflect.Call(Logistics, "EjectOnce", machine);
            R.Check("logisticsfull.evaporator-only-distillate-ejected", new { feedParts = 200000, outputParts = 0, tankParts = recovered, feedUid, outUid, total },
                new { feed = FlLiquid(feed), output = FlLiquid(output), tank = FlLiquid(tank), total = FlLiquidTotals(feed, output, tank) },
                WaterHelper.GetTotalVolume(feed) == 200000 && WaterHelper.GetTotalVolume(output) == 0 && WaterHelper.GetTotalVolume(tank) == recovered &&
                feed.uniqueId == feedUid && output.uniqueId == outUid && JToken.DeepEquals(total, FlLiquidTotals(feed, output, tank)),
                "Actual native distilled non-water liquid uses the internal exact-composition ejection path. Source residue and both physical shells remain.");
            witness = Spawn("water_jug"); WaterHelper.EmptyContainer(witness); Retain(witness);
            Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, witness, recovered);
            JObject prior = FlLiquidTotals(witness, tank);
            JToken rejected = Reflect.Json(Reflect.Call(Logistics, "PourResource", witness, recovered));
            R.Check("logisticsfull.public-pour-does-not-claim-arbitrary-chemicals", new { moved = 0, total = prior },
                new { result = rejected, total = FlLiquidTotals(witness, tank) }, (int?)rejected["PartsMoved"] == 0 && JToken.DeepEquals(prior, FlLiquidTotals(witness, tank)));
            FlMachine("evaporator")["fill"] = "not-applicable-feed-remains-manual"; FlMachine("evaporator")["eject"] = "native-alcohol-distillate-network-route-tested";
            FlMachine("evaporator")["nativeWork"] = "native-cycle-with-real-quality-module-tested"; FlMachine("evaporator")["reason"] = "Concentrated residue stays in its original shell; other source compositions and local output tiers are not-tested";
        }
        finally { Reflect.Call(Logistics, "ConfigureMachine", machine, false, false); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(machine); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(witness); }
    }

    private void FlProgressRoute(string machineId, string chipId, JObject row)
    {
        GameItem machine = FlNative(machineId, out GameItem cell, feeder: true, ejector: true);
        var chip = Spawn(chipId); var requirements = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var modules = (GameInventory)MachineHelper.GetBodyStorage(machine, 1);
            var chipSlot = (GameSlotInventory)MachineHelper.GetBodyStorage(machine, machineId == "printer" ? 3 : 2);
            var input = (GameInventory)MachineHelper.GetBodyStorage(machine, machineId == "printer" ? 2 : 3);
            var output = (GameInventory)MachineHelper.GetBodyStorage(machine, 4);
            if (machineId == "printer")
            {
                foreach (string tag in MachineProgressHelper.GetRequiredMachineTags(chip).Distinct())
                {
                    string id = tag switch { "Metal Printing Module" => "printer_module_metal", "CNC Module" => "printer_module_cnc", _ => throw new FixtureUnavailable("Unmapped actual printer prerequisite: " + tag) };
                    Place(modules, Spawn(id), "full-printer-prerequisite." + chipId, machine);
                }
                int count = chip.GetTagReadonly("REQUIRED_ITEM_TYPE_COUNT_TAG").GetInt();
                for (int i = 0; i < count; i++) requirements[chip.GetTagReadonly("REQUIRED_ITEM_ID_" + i).GetString()] = chip.GetTagReadonly("REQUIRED_ITEM_COUNT_" + i).GetInt();
            }
            else requirements["blank_keycard"] = 1;
            Place(chipSlot, chip, "full-native-chip." + chipId);
            foreach (var rule in requirements) for (int i = 0; i < rule.Value; i++)
            {
                var material = Spawn(rule.Key);
                if (material.identifier == "metal_ingot") IngotPurityHelper.SetPurity(material, "INGOT_PURITY_PERFECT");
                Place(input, material, "full-native-progress-material." + chipId);
            }
            string expectedId = chip.GetTagReadonly(machineId == "printer" ? "PROGRESS_ITEM_TARGET_ITEM_ID_TAG" : "DESEQUENCER_TARGET_ID_TAG").GetString();
            int expectedCount = machineId == "printer" ? chip.GetTagReadonly("PROGRESS_ITEM_TARGET_ITEM_COUNT_TAG").GetInt() : 1;
            if (chipId == "printer_chip_gun_part")
            {
                string purity = IngotPurityHelper.GetPurity(input.childItems.First(item => item.identifier == "metal_ingot"));
                expectedCount = purity switch { "INGOT_PURITY_VERY_LOW" => 2, "INGOT_PURITY_HIGH" or "INGOT_PURITY_VERY_HIGH" => 4, "INGOT_PURITY_PERFECT" => 5, _ => 3 };
            }
            JToken chipPhysical = FlPhysical(chip); int uid = chip.uniqueId, initial = Energy(cell), cycles = 0; long grid = GridTotal; int invoice = 0;
            while (output.childItems.Count == 0 && cycles < 32)
            {
                int cost = MachineryHelper.GetMachinePowerUsage(machine), energyBeforeCycle = Energy(cell); long beforeGrid = GridTotal;
                NativeCycle(machine); cycles++;
                int debit = checked(energyBeforeCycle - Energy(cell) + (int)(beforeGrid - GridTotal));
                if (debit == 0) throw new FixtureUnavailable("Native progress stopped before a finished output (no forced progress/state change): " + chipId);
                R.Check("logisticsfull.native-progress-cycle." + chipId + "." + cycles, cost, debit, debit == cost,
                    "Only the actual wrapped onCycle callback advances native progress and draws its complete real cost.");
                invoice += cost;
            }
            var products = output.childItems.ToArray();
            row["cycles"] = cycles; row["requirements"] = JObject.FromObject(requirements); row["outputs"] = new JArray(products.Select(FlItem));
            row["expectedOutput"] = expectedId; row["expectedCount"] = expectedCount;
            R.Check("logisticsfull.native-progress-finished." + chipId, new { expectedId, expectedCount, chipUid = uid, energyDebit = invoice },
                new { products = products.Select(FlItem).ToArray(), chipUid = chip.uniqueId, input = input.childItems.Select(FlItem).ToArray(), energyDebit = initial - Energy(cell) + grid - GridTotal },
                products.Sum(item => item.unitCount) == expectedCount && products.All(item => item.identifier == expectedId && item.parentInventory == output) &&
                input.childItems.Count == 0 && chip.uniqueId == uid && chip.parentInventory == chipSlot && initial - Energy(cell) + grid - GridTotal == invoice,
                "Native chip requirements/count are captured before work; CNC gun-part count uses an explicit independent purity table. Final product quality/value is observed only after the native finish callback.");
            Reflect.Call(Logistics, "ConfigureMachine", machine, false, false);
            var stock = new List<GameItem>();
            foreach (var rule in requirements) for (int i = 0; i < rule.Value; i++)
            {
                var item = Spawn(rule.Key);
                if (item.identifier == "metal_ingot") IngotPurityHelper.SetPurity(item, "INGOT_PURITY_PERFECT");
                Retain(item); stock.Add(item);
                JToken moved = Reflect.Json(Reflect.Call(Logistics, "DepositResource", item, 1));
                if ((int?)moved["Moved"] != 1) throw new FixtureUnavailable("Native progress input is not a resource-network solid: " + rule.Key);
            }
            JArray beforeFill = FlInvoice(requirements.Keys);
            Reflect.Call(Logistics, "ConfigureMachine", machine, true, false); Reflect.Call(Logistics, "FillOnce", machine);
            R.Check("logisticsfull.native-progress-replenishment." + chipId, new { input = requirements, invoice = beforeFill },
                new { input = input.childItems.Select(FlItem).ToArray(), invoice = FlInvoice(requirements.Keys), debt = LogisticsState["Machines"]?[machine.uniqueId.ToString()] },
                requirements.All(rule => input.childItems.Where(item => item.identifier == rule.Key).Sum(item => item.unitCount) == rule.Value) &&
                JToken.DeepEquals(beforeFill, FlInvoice(requirements.Keys)), "Only actual start/finish consumption earned this pending demand; the chip is retained as configuration.");
            JArray total = FlInvoice(products.Select(item => item.identifier)); var originals = products.Select(FlPhysical).ToArray();
            Reflect.Call(Logistics, "ConfigureMachine", machine, false, true); Reflect.Call(Logistics, "EjectOnce", machine);
            R.Check("logisticsfull.native-progress-final-quality-eject." + chipId, new { total, outputEmpty = true },
                new { total = FlInvoice(products.Select(item => item.identifier)), products = products.Select(FlItem).ToArray() },
                output.childItems.Count == 0 && JToken.DeepEquals(total, FlInvoice(products.Select(item => item.identifier))) &&
                products.Select((item, i) => item.parentInventory != output && JToken.DeepEquals(originals[i], FlPhysical(item))).All(ok => ok),
                "The actual finished UIDs retain their final native quality/value through ejection; no pre-finish or factory-produced fake output is used.");
            foreach (var item in products) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
            foreach (var item in stock) if (item.parentInventory != input) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
        }
        finally { Reflect.Call(Logistics, "ConfigureMachine", machine, false, false); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(machine); if (chip.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(chip); }
    }

    private GameItem FlEnsurePlantArchive()
    {
        if (_flPlantArchive != null) return _flPlantArchive;
        _flPlantArchive = Spawn("hypertech_bay"); FlRetain(_flPlantArchive);
        return _flPlantArchive;
    }
    private static MachineryTabs FlPlantArchiveTabs(GameItem archive)
    {
        if (archive.identifier == "hypertech_bay" && archive.contentWindow?.childElement is MachineryPanelRow row)
        {
            var tabs = row.Panels.Select(panel => panel.content).OfType<MachineryTabs>().ToArray();
            if (tabs.Length == 1 && tabs[0].count == 2) return tabs[0];
        }
        throw new InvalidOperationException("Actual plant archive is not the unmodified native two-page hypertech layout");
    }
    private static JObject FlPlantArchiveSnapshot(GameItem archive)
    {
        var tabs = FlPlantArchiveTabs(archive);
        return new JObject { ["uid"] = archive.uniqueId, ["pages"] = new JArray(Enumerable.Range(0, tabs.count).Select(index => new JObject
            { ["index"] = index, ["uids"] = new JArray(tabs.GetTabInventory(index).childItems.Select(item => item.uniqueId).OrderBy(uid => uid)) })) };
    }
    private void FlArchiveHarvest(GameItem[] products, string seedId, JObject row)
    {
        GameItem archive = FlEnsurePlantArchive(); var tabs = FlPlantArchiveTabs(archive);
        string[] ids = products.Select(item => item.identifier).Distinct().ToArray();
        JArray invoice = FlInvoice(ids), physical = new(products.Select(FlPhysical));
        var placements = new JArray(); row["archivePlacements"] = placements;
        foreach (var item in products)
        {
            bool placed = false;
            for (int page = 0; page < tabs.count && !placed; page++)
            {
                var grid = (GameGridInventory)tabs.GetTabInventory(page);
                for (int y = 0; y < grid.inventoryShape.height && !placed; y++)
                for (int x = 0; x < grid.inventoryShape.width && !placed; x++)
                    if (FlAt(grid, item, x, y)) { placed = true; placements.Add(new JObject { ["uid"] = item.uniqueId, ["archiveUid"] = archive.uniqueId, ["page"] = page, ["x"] = x, ["y"] = y }); }
            }
            if (!placed) throw new InvalidOperationException("Native empty-slot archive admission failed for real harvest UID " + item.uniqueId);
        }
        R.Check("logisticsfull.hydroponic-real-output-archive." + seedId, new { invoice, physical, everyUidRetained = true },
            new { invoice = FlInvoice(ids), physical = new JArray(products.Select(FlPhysical)), placements, archive = FlPlantArchiveSnapshot(archive) },
            JToken.DeepEquals(invoice, FlInvoice(ids)) && JToken.DeepEquals(physical, new JArray(products.Select(FlPhysical))) &&
                products.All(item => item.parentInventory?.GetParentItem() == archive && GeneralHelper.IsItemOwned(item)),
            "After actual EjectOnce, native empty SlotMarkers archive the original UIDs without merging. This frees the 100-cell resource fixture for the next plant; the six cleaned test machines are distinct from the preserved real harvest/seed objects.");
    }
    private void FlHydroponicRoute(string seedId, JObject row)
    {
        if (!DirectoryMaster.Has<GameItem>(seedId)) throw new FixtureUnavailable("Live plant definition has no actual seed item factory: " + seedId);
        GameItem machine = FlNative("hydroponic", out GameItem cell, feeder: true, ejector: true);
        GameItem? donor = null; bool retained = false;
        var tank = (GameItem)Reflect.Call(Logistics, "ResourceWaterTank")!;
        try
        {
            if (WaterHelper.GetTotalVolume(tank) != 0) throw new FixtureUnavailable("Hydroponic route requires an empty awarded network tank");
            GameItem seed = Spawn(seedId), vessel = Spawn("water_jug"); WaterHelper.EmptyContainer(vessel);
            int level = HydroponicHelper.GetNutrimentLevel(seed);
            const int mixtureParts = 2000000;
            int minerals = checked((int)(mixtureParts * (long)Math.Clamp(level, 0, 5000) / 10000));
            // Explicit native preparation grants: two components with exact
            // integer ratios. AddPureWater also adds trace contaminants; its
            // former truncated mixture ceased matching the recorded exact
            // specification after the first indivisible source draw.
            WaterHelper.AddLiquid(vessel, "water", mixtureParts - minerals); if (minerals > 0) WaterHelper.AddLiquid(vessel, "mineral", minerals);
            Place((GameSlotInventory)MachineHelper.GetBodyStorage(machine, 2), vessel, "full-hydroponic-feed." + seedId);
            Place((GameSlotInventory)MachineHelper.GetBodyStorage(machine, 3), seed, "full-hydroponic-seed." + seedId);
            int concentration = WaterHelper.GetContaminantPercentage(vessel, "mineral"), range = HydroponicHelper.GetNutrimentRange(seed);
            if (concentration < level - range || concentration > level + range) throw new FixtureUnavailable("Native nutrient fixture is outside the live seed's required range");
            row["initialSeed"] = FlItem(seed); row["initialVessel"] = FlLiquid(vessel);
            row["recordFeedResult"] = Reflect.Json(Reflect.Call(LogisticsRuntime, "RecordHydroponicFeed", machine));
            int initialParts = WaterHelper.GetTotalVolume(vessel), vesselUid = vessel.uniqueId;
            var originalFeedParts = Liquid.Liquids.ToDictionary(liquid => liquid.identifier, liquid => vessel.GetTagReadonly(liquid.currentPart).GetInt());
            row["nutrientPreparation"] = new JObject { ["totalParts"] = mixtureParts, ["waterParts"] = mixtureParts - minerals, ["mineralParts"] = minerals,
                ["nativeRequiredLevel"] = level, ["actualConcentration"] = concentration, ["provenance"] = "Explicit native AddLiquid factory preparation grant; economic acquisition is not tested. No growth, demand or completed-state fields are assigned." };
            bool SourceSpecificationExact()
            {
                int total = WaterHelper.GetTotalVolume(tank);
                return total > 0 && Liquid.Liquids.All(liquid => (long)originalFeedParts[liquid.identifier] * total ==
                    (long)tank.GetTagReadonly(liquid.currentPart).GetInt() * initialParts);
            }
            var recorded = LogisticsState["Machines"]?[machine.uniqueId.ToString()]?["Hydroponic"];
            R.Check("logisticsfull.hydroponic-real-recorded-feed-specification." + seedId,
                new { vesselUid, targetParts = initialParts, noConsumptionYet = 0 }, recorded,
                (int?)recorded?["Supply"]?["VesselId"] == vesselUid && (int?)recorded?["Supply"]?["TargetParts"] == initialParts && (int?)recorded?["Supply"]?["Remaining"] == 0,
                "The ordinary RecordHydroponicFeed operation records the actual manually supplied vessel. No pending demand or plant progress is assigned by the probe.");
            donor = Spawn("water_tank"); WaterHelper.EmptyContainer(donor);
            int tankFree = WaterHelper.GetFreeCapacity(tank), donorFree = WaterHelper.GetFreeCapacity(donor);
            int batches = initialParts > 0 ? Math.Min(4, Math.Min(tankFree, donorFree) / initialParts) : 0;
            if (batches < 1) throw new FixtureUnavailable("The actual network/donor capacity cannot hold one complete original nutrient-vessel specification");
            // The former 16L fixture exceeded the real default 10L network tank.
            // Fund only complete proportional batches that fit both native
            // capacities; no expansion, admission override or partial-pour skip.
            foreach (var liquid in Liquid.Liquids)
            {
                int amount = vessel.GetTagReadonly(liquid.currentPart).GetInt();
                if (amount > 0) WaterHelper.AddLiquid(donor, liquid.identifier, checked(amount * batches));
            }
            Retain(donor); int sourceParts = WaterHelper.GetTotalVolume(donor);
            JObject sourceInvoice = FlLiquidTotals(donor, tank);
            row["sourcePreparation"] = new JObject { ["batches"] = batches, ["vesselParts"] = initialParts, ["tankFree"] = tankFree,
                ["donorFree"] = donorFree, ["requestedParts"] = sourceParts, ["sourceBefore"] = FlLiquid(donor), ["tankBefore"] = FlLiquid(tank) };
            JToken poured = Reflect.Json(Reflect.Call(Logistics, "PourResource", donor, sourceParts));
            bool sourceAccepted = sourceParts == checked(initialParts * batches) && sourceParts <= tankFree && sourceParts <= donorFree &&
                (int?)poured["PartsMoved"] == sourceParts && (bool?)poured["Indeterminate"] == false && WaterHelper.GetTotalVolume(donor) == 0 &&
                WaterHelper.GetTotalVolume(tank) == sourceParts && JToken.DeepEquals(sourceInvoice, FlLiquidTotals(donor, tank));
            R.Check("logisticsfull.hydroponic-real-capacity-bounded-nutrient-source." + seedId,
                new { batches, sourceParts, tankFree, donorFree, conserved = sourceInvoice },
                new { result = poured, donor = FlLiquid(donor), tank = FlLiquid(tank), conserved = FlLiquidTotals(donor, tank) }, sourceAccepted,
                "Whole recorded nutrient-mixture batches are created as explicit preparation grants, then transferred through the real PourResource path into the native 10L-capacity endpoint. The independent 36-species/value/basis ledger and empty original donor prove actual receipt; rejection remains a required failure.");
            if (!sourceAccepted) throw new InvalidOperationException("Capacity-bounded native nutrient transfer failed its actual source/target conservation check");
            var output = (GameInventory)MachineHelper.GetBodyStorage(machine, 4); int cycles = 0;
            var generated = new List<GameItem>();
            var cycleRows = new JArray(); row["cycles"] = cycleRows;
            while (output.childItems.Count == 0 && cycles < 32)
            {
                int volumeBefore = WaterHelper.GetTotalVolume(vessel), before = Energy(cell), cost = MachineryHelper.GetMachinePowerUsage(machine);
                long grid = GridTotal; JToken beforeComponents = LiquidState(vessel);
                int use = HydroponicHelper.GetWaterUsePerCycle(seed) * 1000;
                var beforeUids = Store.FindAllItem(false).Select(item => item.uniqueId).ToHashSet();
                NativeCycle(machine); cycles++;
                GameItem[] afterCycle = Store.FindAllItem(false).ToArray();
                generated.AddRange(afterCycle.Where(item => !beforeUids.Contains(item.uniqueId) && GeneralHelper.IsItemOwned(item)));
                bool seedStillLive = afterCycle.Any(item => ReferenceEquals(item, seed));
                int removed = volumeBefore - WaterHelper.GetTotalVolume(vessel), debit = checked(before - Energy(cell) + (int)(grid - GridTotal));
                cycleRows.Add(new JObject { ["cycle"] = cycles, ["before"] = beforeComponents, ["after"] = LiquidState(vessel), ["removedParts"] = removed, ["energyDebit"] = debit });
                R.Check("logisticsfull.hydroponic-native-growth." + seedId + "." + cycles, new { minimumWaterUse = use, energyDebit = cost, vesselUid },
                    new { removedParts = removed, energyDebit = debit, uid = vessel.uniqueId, seedStillLive, health = seedStillLive ? (int?)HydroponicHelper.GetHealthPercent(seed) : null },
                    removed >= use && debit == cost && vessel.uniqueId == vesselUid,
                    "Actual native growth consumes water and may consume/transform nutrients; that production change is logged separately from the subsequent exact conserved replenishment.");
                if (debit == 0 || removed == 0) throw new FixtureUnavailable("Native growth stopped before a real harvest; no progress or harvest completion is injected");
                JObject beforeFill = FlLiquidTotals(vessel, tank);
                JObject sourceBeforeFill = FlLiquid(tank); bool sourceExactBefore = SourceSpecificationExact();
                int? recordedDebit = (int?)LogisticsState["Machines"]?[machine.uniqueId.ToString()]?["Hydroponic"]?["Supply"]?["Remaining"];
                Reflect.Call(Logistics, "FillOnce", machine);
                JObject sourceAfterFill = FlLiquid(tank); bool sourceExactAfter = SourceSpecificationExact();
                int? remainingAfterFill = (int?)LogisticsState["Machines"]?[machine.uniqueId.ToString()]?["Hydroponic"]?["Supply"]?["Remaining"];
                R.Check("logisticsfull.hydroponic-recorded-feed-repayment." + seedId + "." + cycles,
                    new { volume = initialParts, vesselUid, total = beforeFill, recordedDebit = removed, remainingAfterFill = 0, exactSourceSpecificationBeforeAndAfter = true },
                    new { volume = WaterHelper.GetTotalVolume(vessel), uid = vessel.uniqueId, total = FlLiquidTotals(vessel, tank), recordedDebit, remainingAfterFill,
                        sourceExactBefore, sourceExactAfter, sourceBeforeFill, sourceAfterFill, state = LogisticsState["Machines"]?[machine.uniqueId.ToString()] },
                    WaterHelper.GetTotalVolume(vessel) == initialParts && vessel.uniqueId == vesselUid && JToken.DeepEquals(beforeFill, FlLiquidTotals(vessel, tank)) &&
                        recordedDebit == removed && remainingAfterFill == 0 && sourceExactBefore && sourceExactAfter,
                    "Real observed growth earns the exact independently metered deficit. The explicit two-component source must match the recorded sample by strict integer cross-products before and after the real FillOnce; every registered component/value/quality basis remains exactly conserved across the original vessel and source tank.");
            }
            GameItem[] products = output.childItems.Concat(generated).Distinct().ToArray(); row["harvest"] = new JArray(products.Select(FlItem));
            row["nativeSpillUids"] = new JArray(products.Where(item => item.parentInventory != output).Select(item => item.uniqueId));
            R.Check("logisticsfull.hydroponic-real-harvest." + seedId, "Native cycle produced actual owned harvest/returned seed instances", row["harvest"],
                products.Length > 0 && output.childItems.Count > 0 && products.All(item => item.uniqueId > 0 && item.parentInventory != null && GeneralHelper.IsItemOwned(item)),
                "No FinishGrowing call, harvest factory shortcut or progress tag write. Before/after real cycle UID inventories also capture native PlantHarvest.Deliver overflow to the ship inventory. This logistics check does not claim independent validation of every genetics/yield formula.");
            JArray invoice = FlInvoice(products.Select(item => item.identifier)); var physical = products.Select(FlPhysical).ToArray();
            Reflect.Call(Logistics, "ConfigureMachine", machine, false, true); Reflect.Call(Logistics, "EjectOnce", machine);
            R.Check("logisticsfull.hydroponic-harvest-and-returned-seed-ejection." + seedId, new { invoice, outputEmpty = true },
                new { invoice = FlInvoice(products.Select(item => item.identifier)), products = products.Select(FlItem).ToArray() },
                output.childItems.Count == 0 && JToken.DeepEquals(invoice, FlInvoice(products.Select(item => item.identifier))) &&
                products.Select((item, i) => item.parentInventory != output && JToken.DeepEquals(physical[i], FlPhysical(item))).All(ok => ok));
            var retainedHarvest = new JArray(); row["retainedHarvestUids"] = retainedHarvest;
            foreach (var item in products)
            {
                GameItem[] actual = Store.FindAllItem(false).Where(candidate => candidate.uniqueId == item.uniqueId).ToArray();
                bool custody = actual.Length == 1 && ReferenceEquals(actual[0], item) && item.unitCount > 0 && GeneralHelper.IsItemOwned(item) && item.parentInventory != null;
                R.Check("logisticsfull.hydroponic-real-harvest-uid-retained." + seedId + "." + item.uniqueId,
                    "The actual harvested/ejected UID remains uniquely live, owned and positive before the persistence promise", new { matches = actual.Length, item = FlItem(item) }, custody);
                if (!custody) throw new InvalidOperationException("Harvest UID custody is not proven; no missing item is recreated or removed from the persistence denominator");
                FlRetain(item); retainedHarvest.Add(item.uniqueId);
            }
            FlArchiveHarvest(products, seedId, row);
            if (seedId == "nutrifruit_seed")
            {
                // Ejecting the completed harvest paused filling above. Resume
                // through the normal configuration API before testing the
                // actual stale-sample refusal, otherwise FillOnce returns at
                // the disabled-setting guard before inspecting the vessel.
                Reflect.Call(Logistics, "ConfigureMachine", machine, true, false);
                JToken? drainSettings = LogisticsState["Machines"]?[machine.uniqueId.ToString()];
                R.Check("logisticsfull.hydroponic-real-manual-drain-fill-enabled-prerequisite",
                    new { fillEnabled = true, requiresRecording = false, remaining = 0 }, drainSettings,
                    (bool?)drainSettings?["FillEnabled"] == true && (bool?)drainSettings?["Hydroponic"]?["RequiresRecording"] == false &&
                        (int?)drainSettings?["Hydroponic"]?["Supply"]?["Remaining"] == 0,
                    "The normal ConfigureMachine operation restores the installed feeder after output ejection. This required gate prevents a paused FillOnce no-op from masquerading as a stale-sample refusal.");
                // A deliberately dry original-vessel witness remains thermally
                // stable across the later full night and reload. This is actual
                // disposal of factory-granted test solution after all conserved
                // growth/refill/harvest checks, never an ignored temperature.
                JObject discarded = FlLiquid(vessel), networkBefore = FlLiquid(tank);
                long quoteBeforeDisposal = vessel.GetCurrentValue();
                WaterHelper.EmptyContainer(vessel);
                long quoteAfterDisposal = vessel.GetCurrentValue();
                row["disposal"] = new JObject { ["discarded"] = discarded.DeepClone(), ["discardedParts"] = (int)discarded["TotalParts"]!,
                    ["nativeVesselQuoteBefore"] = quoteBeforeDisposal, ["nativeVesselQuoteAfter"] = quoteAfterDisposal,
                    ["nativeQuoteDifference"] = quoteBeforeDisposal - quoteAfterDisposal,
                    ["valueMeaning"] = "Observed native GetCurrentValue vessel quote difference, not a sale or replacement liquid endpoint. Every optional per-species Value/QualityBasis remains raw, including null." };
                Reflect.Call(Logistics, "FillOnce", machine);
                JToken? feedAfterDisposal = LogisticsState["Machines"]?[machine.uniqueId.ToString()]?["Hydroponic"];
                R.Check("logisticsfull.hydroponic-real-manual-drain-invalidates-recorded-feed", new { discarded, vesselUid, empty = true, requiresRecording = true, networkBefore },
                    new { vessel = FlLiquid(vessel), feed = feedAfterDisposal, network = FlLiquid(tank), disposal = row["disposal"] },
                    vessel.uniqueId == vesselUid && WaterHelper.GetTotalVolume(vessel) == 0 && (bool?)feedAfterDisposal?["RequiresRecording"] == true && JToken.DeepEquals(networkBefore, FlLiquid(tank)),
                    "True EmptyContainer disposes this factory fixture after harvest. Actual FillOnce must refuse an out-of-band changed solution until the player records it again; it cannot fabricate consumption. The original empty vessel/UID and real invalidated sample are retained for persistence.");
                row["persistenceWitness"] = "Original UID dry vessel after explicit native disposal and real re-record-required rejection; no thermal field mutation";
                Reflect.Call(Logistics, "ConfigureMachine", machine, false, false); FlRetain(machine); retained = true;
            }
        }
        finally
        {
            Reflect.Call(Logistics, "ConfigureMachine", machine, false, false);
            WaterHelper.EmptyContainer(tank); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(donor);
            if (!retained) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(machine);
        }
    }
    private void FlDnaRoute()
    {
        GameItem machine = FlNative("plant_dna_manipulator", out GameItem cell, ejector: true);
        GameItem? seed = null;
        try
        {
            var seedSlot = (GameSlotInventory)MachineHelper.GetBodyStorage(machine, 3); var diskSlot = (GameSlotInventory)MachineHelper.GetBodyStorage(machine, 4);
            var eject = (GameInventory)MachineHelper.GetBodyStorage(machine, 6); GameItem disk = Spawn("plant_dna_disk"); Place(diskSlot, disk, "full-dna-native-blank-disk");
            MachineDnaManipulator.OperationPlan? plan = null;
            foreach (var plant in PlantRegistry.All)
            {
                if (!DirectoryMaster.Has<GameItem>(plant.id)) continue;
                GameItem candidate = Spawn(plant.id);
                if (!FlTryPlace(seedSlot, candidate)) { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(candidate); continue; }
                plan = MachineDnaManipulator.PlanOperation(machine);
                if (plan != null && plan.blockedReason.Length == 0) { seed = candidate; break; }
                seedSlot.Expel(candidate); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(candidate);
            }
            if (seed == null || plan == null) throw new FixtureUnavailable("No unmodified native plant seed factory offers an extractable trait; no genotype/operation/progress is fabricated");
            int diskUid = disk.uniqueId, cycles = 0, energyBefore = Energy(cell); long grid = GridTotal; int invoice = 0;
            while (eject.childItems.Count == 0 && cycles < 32)
            {
                int cost = MachineryHelper.GetMachinePowerUsage(machine), energyBeforeCycle = Energy(cell); long beforeGrid = GridTotal;
                NativeCycle(machine); cycles++;
                int debit = checked(energyBeforeCycle - Energy(cell) + (int)(beforeGrid - GridTotal));
                if (debit == 0) break;
                invoice += cost;
                R.Check("logisticsfull.dna-native-cycle." + cycles, cost, debit, debit == cost);
                if (MachineProgressHelper.GetMachineState(machine) == "STATE_READY") break;
            }
            if (eject.childItems.Count == 0)
                throw new FixtureUnavailable("Actual RNG extraction completed without a native ejected disk, or lacked resources; no random retry/result patch is used. Observed disk=" + FlItem(disk));
            GameItem[] products = eject.childItems.ToArray();
            R.Check("logisticsfull.dna-native-ejected-disk", new { diskUid, physicalCount = 1, energyDebit = invoice },
                new { products = products.Select(FlItem).ToArray(), energyDebit = energyBefore - Energy(cell) + grid - GridTotal },
                products.Length == 1 && products[0].uniqueId == diskUid && energyBefore - Energy(cell) + grid - GridTotal == invoice,
                "Only actual native extraction/finish/eject created this completed disk state. Its success/failure roll is recorded, not replaced.");
            JArray beforeMove = FlInvoice(products.Select(item => item.identifier)); var before = products.Select(FlPhysical).ToArray();
            Reflect.Call(Logistics, "ConfigureMachine", machine, false, true); Reflect.Call(Logistics, "EjectOnce", machine);
            R.Check("logisticsfull.dna-only-native-eject-grid-collected", new { total = beforeMove, ejectEmpty = true },
                new { total = FlInvoice(products.Select(item => item.identifier)), products = products.Select(FlItem).ToArray(), workingInput = ((GameInventory)MachineHelper.GetBodyStorage(machine, 2)).childItems.Select(FlItem).ToArray() },
                eject.childItems.Count == 0 && JToken.DeepEquals(beforeMove, FlInvoice(products.Select(item => item.identifier))) &&
                products.Select((item, i) => item.parentInventory != eject && JToken.DeepEquals(before[i], FlPhysical(item))).All(ok => ok));
            foreach (var item in products) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
            FlMachine("plant_dna_manipulator")["fill"] = "not-applicable-work-slots-manual";
            FlMachine("plant_dna_manipulator")["eject"] = "actual-extracted-disk-network-ejection-tested";
            FlMachine("plant_dna_manipulator")["nativeWork"] = "one-native-extraction-attempt-tested";
            FlMachine("plant_dna_manipulator")["reason"] = "Every trait/apply/extract/keep-disk combination and RNG outcomes are not-tested";
        }
        finally { Reflect.Call(Logistics, "ConfigureMachine", machine, false, false); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(machine); }
    }

    private IEnumerable<object?> FlHoppers()
    {
        RecipeFixture? fixture = null; GameItem? box = null, intake = null, outlet = null;
        var stock = new List<GameItem>();
        try
        {
            object view = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", FlModIds[1])).Single(recipe => Reflect.Str(recipe, "Id") == FlOwner + ".make_ejector");
            fixture = CreateRecipeFixture(view);
            Place(fixture.Modules!, Spawn(FlFeeder), "full-hopper-feeder", fixture.Machine); Place(fixture.Modules!, Spawn(FlEjector), "full-hopper-ejector", fixture.Machine);
            Reflect.Call(Logistics, "ConfigureMachine", fixture.Machine, true, false); ObserveBatch(fixture, "full-hopper-real-consumption", true);
            Reflect.Call(Logistics, "ConfigureMachine", fixture.Machine, false, false);
            box = Spawn("storage_bay"); Retain(box); intake = Spawn("intake_hopper"); outlet = Spawn("outlet_hopper");
            Place(FlGrid(box), intake, "full-hopper-native-intake"); Place((GameGridInventory)Reflect.Call(Logistics, "ResourceRegion")!, outlet, "full-hopper-native-outlet");
            HopperHelper.Link(intake, outlet); HopperHelper.SetFilterList(intake, Reflect.Each(Reflect.Get(view, "Ingredients")).Select(rule => Reflect.Str(rule, "ItemId")).ToList());
            intake.ModifyTag(HopperHelper.FILTER_MODE_INT, tag => tag.Enable().SetInt(HopperHelper.FILTER_ALLOW));
            intake.ModifyTag(HopperHelper.KEEP_INT, tag => tag.Enable().SetInt(1));
            intake.ModifyTag(HopperHelper.ORDER_INT, tag => tag.Enable().SetInt(6));
            foreach (object rule in Reflect.Each(Reflect.Get(view, "Ingredients")))
                for (int i = 0; i <= Reflect.Int(rule, "Amount"); i++) { var item = Ingredient(rule); Place(FlGrid(box), item, "full-hopper-source"); stock.Add(item); }
            GameItem excluded = Spawn("common_ore"); Place(FlGrid(box), excluded, "full-hopper-filter-negative"); stock.Add(excluded);
            string[] ids = stock.Select(item => item.identifier).Concat(fixture.Output.childItems.Select(item => item.identifier)).Distinct().ToArray();
            JArray total = FlInvoice(ids); JArray before = new(FlGrid(box).childItems.Select(FlItem));
            HopperHelper.RunWindow(1);
            R.Check("logisticsfull.hopper-order-window-negative", before, new JArray(FlGrid(box).childItems.Select(FlItem)), JToken.DeepEquals(before, new JArray(FlGrid(box).childItems.Select(FlItem))),
                "Real RunWindow(1) must not execute the native order-6 intake.");
            HopperHelper.RunWindow(2);
            var requirements = Reflect.Each(Reflect.Get(view, "Ingredients")).ToDictionary(rule => Reflect.Str(rule, "ItemId"), rule => Reflect.Int(rule, "Amount"));
            R.Check("logisticsfull.hopper-native-keep-filter-to-resource-network", new { oneKeptPerId = true, total },
                new { source = FlGrid(box).childItems.Select(FlItem).ToArray(), total = FlInvoice(ids), actualLink = HopperHelper.FindOutlet(intake)?.uniqueId },
                requirements.All(rule => FlGrid(box).childItems.Where(item => item.identifier == rule.Key).Sum(item => item.unitCount) == 1) &&
                excluded.parentInventory == FlGrid(box) && HopperHelper.FindOutlet(intake) == outlet && JToken.DeepEquals(total, FlInvoice(ids)),
                "Actual native Link/filter/keep/order configuration and hopper RunWindow. Original source/target parent inventories and physical material invoice are observed independently.");
            Reflect.Call(Logistics, "ConfigureMachine", fixture.Machine, true, false); Reflect.Call(Logistics, "FillOnce", fixture.Machine);
            R.Check("logisticsfull.hopper-to-mod-machine-actual-debt-fill", new { requirements, total },
                new { inputs = fixture.Input.childItems.Select(FlItem).ToArray(), total = FlInvoice(ids) },
                requirements.All(rule => fixture.Input.childItems.Where(item => item.identifier == rule.Key).Sum(item => item.unitCount) == rule.Value) && JToken.DeepEquals(total, FlInvoice(ids)),
                "Native hopper supplies the network, then actual FillOnce repays the demand earned by this machine's real prior batch.");
            Reflect.Call(Logistics, "ConfigureMachine", fixture.Machine, false, true); Reflect.Call(Logistics, "EjectOnce", fixture.Machine);
            R.Check("logisticsfull.hopper-chain-produced-output-eject", new { outputEmpty = true, total },
                new { outputCount = fixture.Output.childItems.Count, total = FlInvoice(ids) }, fixture.Output.childItems.Count == 0 && JToken.DeepEquals(total, FlInvoice(ids)));
            Reflect.Call(Logistics, "ConfigureMachine", fixture.Machine, false, false);
            intake.ModifyTag(HopperHelper.ON_ADD_BOOL, tag => tag.Enable().SetBool(true));
            GameItem newItem = Ingredient(Reflect.Each(Reflect.Get(view, "Ingredients")).First());
            int newUid = newItem.uniqueId; string id = newItem.identifier;
            Place(FlGrid(box), newItem, "full-hopper-on-add-trigger"); stock.Add(newItem);
            R.environment["fullLogisticsHopperOnAdd"] = new JObject { ["uid"] = newUid, ["id"] = id, ["intake"] = intake.uniqueId, ["outlet"] = outlet.uniqueId,
                ["invoiceIds"] = new JArray(ids), ["invoice"] = FlInvoice(ids) };
            FlRetain(box); FlRetain(fixture.Machine); fixture.Retained = true; FlRetain(outlet);
        }
        catch (FixtureUnavailable error) { R.Skip("logisticsfull.hopper-chain", error.Message, false); }
        catch (Exception error) { R.Check("logisticsfull.hopper-chain", "Real hopper -> network -> actual debt Fill/Eject", null, false, error: error.ToString()); }
        // The native OnAdd path is a queued coroutine. Wait on its actual state,
        // independently of RunWindow, without writing its queue/busy fields.
        if (fixture?.Retained == true && intake != null && box != null && R.environment.TryGetValue("fullLogisticsHopperOnAdd", out object? onAdd))
        {
            var expected = (JObject)onAdd!; string id = (string)expected["id"]!; float deadline = Time.realtimeSinceStartup + 4f;
            while (FlGrid(box).childItems.Where(item => item.identifier == id).Sum(item => item.unitCount) > 1 && Time.realtimeSinceStartup < deadline) yield return null;
            R.Check("logisticsfull.hopper-real-on-add-callback", "Native queued OnAdd transfer preserves one kept unit", FlGrid(box).childItems.Select(FlItem).ToArray(),
                FlGrid(box).childItems.Where(item => item.identifier == id).Sum(item => item.unitCount) == 1 &&
                JToken.DeepEquals(expected["invoice"], FlInvoice(((JArray)expected["invoiceIds"]!).Values<string>()!)) &&
                Store.FindAllItem().Where(item => item.identifier == id).Select(item => item.uniqueId).Distinct().Count() == Store.FindAllItem().Where(item => item.identifier == id).Count(),
                "Native Watch/onSlotAddItemEndFunc -> Schedule -> RunPending; no direct Run call or pending flag assignment.");
            intake.ModifyTag(HopperHelper.ON_ADD_BOOL, tag => tag.Enable().SetBool(false));
            JArray before = new(FlGrid(box).childItems.Select(FlItem)); HopperHelper.RunWindow(3);
            R.Check("logisticsfull.hopper-later-window-no-duplicate", before, new JArray(FlGrid(box).childItems.Select(FlItem)), JToken.DeepEquals(before, new JArray(FlGrid(box).childItems.Select(FlItem))));
        }
        else
        {
            fixture?.Dispose(); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(box); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(outlet);
        }
        R.Skip("logisticsfull.hopper-on-move-reentry-multipage-and-stock-machine", "The actual order/filter/keep/OnAdd Mod-machine chain is above; OnMove, source/target callback reentry, multipage parent selection and stock-machine-specific chains remain not-tested", false);
        yield return null;
    }

    private static bool FlAdjacent(GameItem a, GameItem b)
    {
        if (a.parentInventory != b.parentInventory) return false;
        for (int y = a.modifiedShape.minY; y <= a.modifiedShape.maxY; y++)
        for (int x = a.modifiedShape.minX; x <= a.modifiedShape.maxX; x++)
            if (a.modifiedShape.Get(x, y) == 1 && (b.modifiedShape.Get(x - 1, y) == 1 || b.modifiedShape.Get(x + 1, y) == 1 || b.modifiedShape.Get(x, y - 1) == 1 || b.modifiedShape.Get(x, y + 1) == 1)) return true;
        return false;
    }
    private static bool FlAt(GameGridInventory grid, GameItem item, int x, int y)
    {
        var shape = new GridShapeBuilder(item.shape).SetOrientation(0).SetPosition(x, y).Build();
        var marker = grid.TryInventorySlot(item, item.unitCount, shape);
        return marker != null && marker.IsValid() && marker.inventory == grid && marker.targetItem == null && marker.TryAcceptOnce(item.unitCount) == item.unitCount && item.parentInventory == grid;
    }
    private IEnumerable<object?> FlS06Recovery()
    {
        GameItem? stage = null; var farms = new List<GameItem>(); var cells = new List<GameItem>(); var bottles = new List<GameItem>(); var targets = new List<GameItem>();
        bool prepared = false, retained = false; JObject? evidence = null;
        try
        {
            R.phase = "logisticsfull-S06-two-full-native-farms";
            stage = Spawn("hypertech_bay"); Retain(stage); var grid = FlGrid(stage);
            for (int i = 0; i < 2; i++)
            {
                GameItem farm = FlNative("moisture_farm", out GameItem cell); farms.Add(farm); cells.Add(cell);
                var modules = (GameInventory)MachineHelper.GetBodyStorage(farm, 1);
                Place(modules, Spawn("nicokobo.mechcore_protocol.module_matrix.auto_water_supply_module"), "full-S06." + i, farm);
                if (!FlAt(grid, farm, 0, i * 15)) throw new FixtureUnavailable("Native hypertech page cannot legally receive the separated S06 farm at its real slot");
                GameItem bottle = Spawn("water_jug"); WaterHelper.EmptyContainer(bottle); WaterHelper.AddPureWater(bottle, WaterHelper.GetFreeCapacity(bottle));
                Place((GameSlotInventory)MachineHelper.GetBodyStorage(farm, 2), bottle, "full-S06-full-source." + i); bottles.Add(bottle);
                int energy = Energy(cell), parts = WaterHelper.GetTotalVolume(bottle);
                NativeCycle(farm);
                R.Check("logisticsfull.S06.paused-full-source-with-no-recipient." + i, new { energy, parts, free = 0 },
                    new { energy = Energy(cell), parts = WaterHelper.GetTotalVolume(bottle), free = WaterHelper.GetFreeCapacity(bottle) },
                    Energy(cell) == energy && WaterHelper.GetTotalVolume(bottle) == parts && WaterHelper.GetFreeCapacity(bottle) == 0,
                    "Actual native callback runs while the real source bottle is full and there is no adjacent recipient. It must pause without spending power.");
            }
            for (int i = 0; i < 2; i++)
            {
                var target = Spawn("water_tank"); WaterHelper.EmptyContainer(target); targets.Add(target);
                if (!FlAt(grid, target, farms[i].modifiedShape.maxX + 1, i * 15) || !FlAdjacent(farms[i], target) || FlAdjacent(farms[1 - i], target))
                    throw new FixtureUnavailable("No legal independent adjacent tank placement for two-farm S06 air-sharing regression");
                if (WaterHelper.GetFreeCapacity(target) < WaterHelper.GetTotalVolume(bottles[i]) + 680000)
                    throw new FixtureUnavailable("Native recipient lacks capacity for the original full source plus independently expected generation");
            }
            evidence = new JObject { ["sourceBefore"] = new JArray(bottles.Select(FlItem)), ["targetBefore"] = new JArray(targets.Select(FlItem)),
                ["sourcePartsBefore"] = new JArray(bottles.Select(WaterHelper.GetTotalVolume)), ["targetPartsBefore"] = new JArray(targets.Select(WaterHelper.GetTotalVolume)),
                ["energyBefore"] = new JArray(cells.Select(Energy)), ["cost"] = new JArray(farms.Select(MachineryHelper.GetMachinePowerUsage)),
                ["expectedGeneratedPerFarm"] = 680000, ["independentFormula"] = "850000 parts / (1 + 0.25 * (2 - 1)) = 680000; no performance modules",
                ["gridBefore"] = GridTotal, ["dayBefore"] = StoreStation.GetDayCounter(), ["farmUids"] = new JArray(farms.Select(farm => farm.uniqueId)) };
            R.environment["fullLogisticsS06"] = evidence;
            R.Check("logisticsfull.S06.two-native-full-source-fixture", "Two zero-performance full-source farms and disjoint new adjacent empty 20L native tanks", evidence,
                farms.All(farm => MachineryHelper.GetCurrentPerformanceBonus(farm) == 0) && bottles.All(bottle => WaterHelper.GetFreeCapacity(bottle) == 0) &&
                targets.All(target => WaterHelper.GetTotalVolume(target) == 0) && !FlAdjacent(farms[0], farms[1]));
            Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
            prepared = true;
        }
        catch (FixtureUnavailable error) { R.Skip("logisticsfull.S06.two-farm-native-night", error.Message, false); }
        catch (Exception error) { R.Check("logisticsfull.S06.fixture", "Legal native full-source/late-recipient two-farm fixture", null, false, error: error.ToString()); }
        if (!prepared)
        {
            foreach (var item in targets.Concat(farms)) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(stage); yield break;
        }
        try
        {
            int day = StoreStation.GetDayCounter();
            using var observation = new NightObservation(this, Array.Empty<RecipeFixture>(), day);
            evidence!["nightObservation"] = observation.Evidence;
            foreach (object? wait in NightDay(day, "logisticsfull.S06", observation)) yield return wait;
            for (int i = 0; i < 2; i++)
            {
                int initialSource = (int)evidence["sourcePartsBefore"]![i]!, initialTarget = (int)evidence["targetPartsBefore"]![i]!;
                int generated = WaterHelper.GetTotalVolume(bottles[i]) + WaterHelper.GetTotalVolume(targets[i]) - initialSource - initialTarget;
                int debit = (int)evidence["energyBefore"]![i]! - Energy(cells[i]);
                R.Check("logisticsfull.S06.real-EndNight-recovers-and-counts-both-full-farms." + i,
                    new { generated = 680000, energyDebit = (int)evidence["cost"]![i]!, sourceUid = (int)evidence["sourceBefore"]![i]!["uid"]!, targetUid = (int)evidence["targetBefore"]![i]!["uid"]! },
                    new { generated, energyDebit = debit, source = FlItem(bottles[i]), target = FlItem(targets[i]), runningFarmsAfter = MachineMoistureFarm.CountRunningFarms() },
                    generated == 680000 && debit == (int)evidence["cost"]![i]! && bottles[i].uniqueId == (int)evidence["sourceBefore"]![i]!["uid"]! &&
                    targets[i].uniqueId == (int)evidence["targetBefore"]![i]!["uid"]! && WaterHelper.GetTotalVolume(targets[i]) > initialTarget,
                    "Real shutter/bedroll/End Day button -> EndNight. Both sources were full before either recipient existed; counting only the first drained farm would incorrectly generate 850000 per farm and fail this independent 680000 oracle.");
            }
            evidence["sourceAfter"] = new JArray(bottles.Select(FlItem)); evidence["targetAfter"] = new JArray(targets.Select(FlItem));
            evidence["dayAfter"] = StoreStation.GetDayCounter(); evidence["gridAfter"] = GridTotal;
            R.Check("logisticsfull.S06.two-farm-total-generation", 1360000,
                bottles.Concat(targets).Sum(item => (long)WaterHelper.GetTotalVolume(item)) - ((JArray)evidence["sourcePartsBefore"]!).Values<long>().Sum(),
                bottles.Concat(targets).Sum(item => (long)WaterHelper.GetTotalVolume(item)) - ((JArray)evidence["sourcePartsBefore"]!).Values<long>().Sum() == 1360000);
            foreach (object? wait in CaptureEvidenceFrame("logisticsfull-S06-two-farm-native-night-report")) yield return wait;
            foreach (var item in farms.Concat(targets)) FlRetain(item); FlRetain(stage!); retained = true;
            R.persistence["fullLogisticsS06"] = evidence.DeepClone();
            FlMachine("moisture_farm")["fill"] = "not-applicable-native-air-generation";
            FlMachine("moisture_farm")["eject"] = "S06-native-neighbor-supply-tested; logistics-EjectOnce-tiers-not-tested";
            FlMachine("moisture_farm")["nativeWork"] = "two-full-source-farms-real-EndNight-and-independent-shared-air-ledger";
            FlMachine("moisture_farm")["reason"] = "S06 real night covers late adjacency and two-farm recovery; direct logistics collection/source variants and non-S06 mixed-air control remain not-tested";
        }
        finally
        {
            if (!retained)
            { foreach (var item in targets.Concat(farms)) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(stage); }
        }
    }

    private void FlRemainingCoverage()
    {
        var classifications = new Dictionary<string, (string Fill, string Eject, string Reason)>(StringComparer.Ordinal)
        {
            ["deal_maker"] = ("not-applicable", "not-applicable", "Negotiates with actual customers; no production material ports. Shared power is unsupported, distinct from material non-applicability."),
            ["recharger"] = ("not-applicable", "not-applicable", "Native charging device, not a consumable production machine."),
            ["powerblock"] = ("not-applicable", "not-applicable", "Native adjacent generator/charger; its early-cycle geometry workload remains not-tested."),
            ["makeshift_reactor"] = ("not-applicable", "not-applicable", "Native adjacent generation, not conventional production; grid membership unsupported and real radiation/power workload not-tested."),
            ["security_alarm"] = ("not-applicable", "not-applicable", "No material recipe/output; power-only native device. Actual alarm behavior not-tested."),
            ["storage_coordinator"] = ("not-applicable", "not-applicable", "Storage management UI, no material recipe/output. Native preset UI behavior not-tested."),
            ["mirage_projector"] = ("not-applicable", "not-applicable", "No material recipe/output; actual projected-customer behavior not-tested."),
            ["bottle_printer"] = ("not-applicable-no-raw-material", "unsupported", "Has actual native button-generated container output, but no adapted logistics ejector port. Button behavior not-tested."),
            ["box_dispenser"] = ("not-applicable-no-raw-material", "unsupported", "Has actual ammo_box button output but no adapted logistics ejector port. Button behavior not-tested."),
            ["heating_plate"] = ("not-applicable-current-batch-model", "not-applicable-current-batch-model", "Heats the same vessel in place; no independent finished output port. Actual heating workload not-tested."),
            ["blender"] = ("unsupported", "unsupported", "Has true solid ingredients/liquid output, but chemical module slots are unadapted; not non-applicable. Actual Blend button not-tested."),
            ["chem_finisher"] = ("unsupported", "unsupported", "Has actual liquid inputs and graded solid output, but chemical module slots are unadapted. Actual finish button not-tested."),
            ["cassette_player"] = ("not-applicable", "not-applicable", "Tape is configuration, no consumable production/output. Native playback UI not-tested."),
            ["vending_machine"] = ("unsupported", "not-applicable-sale-is-not-finished-output", "Goods have real restocking demand; unsupported power/restocking must not be described as non-applicable. Actual sales not-tested."),
            ["vending_fountain"] = ("unsupported", "not-applicable-sale-is-not-finished-output", "Three water tiers have real restocking demand. Actual sales/refill and shared power are not adapted."),
            ["wine_rack"] = ("not-applicable-current-batch-model", "not-applicable-current-batch-model", "Wine ages in place; player chooses when to remove it. Native aging workload not-tested."),
            ["fridge"] = ("not-applicable-current-batch-model", "not-applicable-current-batch-model", "Cold storage has no independent finished output. Native chill/freeze workload not-tested."),
            ["livestock_feed_dispenser"] = ("unsupported", "not-applicable-no-physical-output", "Food consumption becomes a kcal ledger with real refill need; current logistics does not adapt it. Native Blend not-tested."),
            ["auto_butcher"] = ("not-applicable-live-host-remains-manual", "unsupported", "Has native tray products; output collection is unadapted. Real animal/butcher workload not-tested."),
            ["ras"] = ("unsupported", "unsupported-live-fish-not-blindly-ejected", "Shared power is separate from fish/feed/water/filter roles. Real aquaculture generation and day receipts not-tested."),
            ["vacuum_robot"] = ("not-applicable-native-intake", "unsupported", "Has actual manure output storage; collection is unadapted. Real cleanup/incineration not-tested.")
        };
        foreach (var pair in classifications)
        {
            var row = FlMachine(pair.Key); row["fill"] = pair.Value.Fill; row["eject"] = pair.Value.Eject; row["reason"] = pair.Value.Reason;
            if ((string?)row["nativeWork"] == "not-tested") R.Skip("logisticsfull.native-work." + pair.Key, pair.Value.Reason, false);
        }
        foreach (var pair in _flCoverage.Properties())
        {
            var row = (JObject)pair.Value;
            foreach (string path in new[] { "fill", "eject", "power", "nativeWork" })
                if ((string?)row[path] == "not-tested") R.Skip("logisticsfull.remaining." + pair.Name + "." + path, (string?)row["reason"] ?? "No actual workload evidence", false);
        }
        foreach (string missing in new[]
        {
            "all-shape-merge-partial-capacity-and-multipage-route-variants", "source-uid-owner-run-slot-generation-races", "fault-injection-native-reject-detach-throw-after-accept-and-rollback",
            "hydroponic-out-of-band-change-new-vessel-and-valued-specification-variants", "raw-purifier-target-reachability-and-liquid-grade-cross-product",
            "native-dna-all-traits-apply-extract-keep-disk-and-rng-outcomes", "native-progress-no-power-missing-material-and-full-output-controls",
            "native-liquid-value-and-quality-basis-non-null-valued-fixtures", "capacity-upgrade-kit-page-names-durable-receipts", "duplicate-slot-and-historical-InitialSave-branches",
            "UI-chinese-english-scale-drag-release-ime-and-gather-menu-scene-transition", "performance-100-1000-5000-items-1-10-50-machines"
        }) R.Skip("logisticsfull.remaining." + missing, "Not exercised by the current native family; do not infer coverage from registration, a source review, or a passed subset", false);
    }
}
