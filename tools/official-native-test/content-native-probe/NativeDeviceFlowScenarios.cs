using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private readonly JArray _dfCases = new(), _dfDevices = new(), _dfNights = new();
    private readonly HashSet<int> _dfPersist = new();
    private readonly JObject _dfCustody = new();

    // Separate family: these observations never change an earlier logisticsfull
    // result. All materials/energy are declared factory grants for this fixture.
    internal IEnumerable<object?> NativeDeviceFlows()
    {
        R.phase = "deviceflows-real-entrypoints";
        R.environment["nativeDeviceFlowScope"] = "Build26317870; 29 native + 6 Mod devices receive actual FillOnce/EjectOnce calls. Idle/no-op invariants do not assert active production support. Two complete native RAS nights and actual Hopper OnMove/callback reentry use real factories, inventory slots and installed callbacks. Native upgrade Unlock is an explicit prerequisite fixture, not economic/achievement acquisition. Only normal player configuration is set; fish age, products, completion, day, logistics demand and ledger are never assigned. UI callbacks are programmatic native events, not physical mouse input.";
        R.environment["nativeDeviceFlowDevices"] = _dfDevices;
        R.environment["nativeDeviceFlowCases"] = _dfCases;
        R.environment["nativeDeviceFlowRasNights"] = _dfNights;
        R.environment["nativeDeviceFlowPersistenceCustody"] = _dfCustody;
        string[] ids = FlNativeIds.Concat(FlModIds).ToArray();
        R.Check("deviceflows.denominator", new { native = 29, mod = 6, registered = 35 },
            new { native = FlNativeIds.Length, mod = FlModIds.Length, missing = ids.Where(id => !DirectoryMaster.Has<GameItem>(id)).ToArray() },
            FlNativeIds.Length == 29 && FlModIds.Length == 6 && ids.All(DirectoryMaster.Has<GameItem>));
        PrepareLogisticsNetworks();
        float readyUntil = Time.realtimeSinceStartup + 5f;
        while (Reflect.Call(Logistics, "ResourceRegion") == null || Reflect.Call(Logistics, "PowerRegion") == null)
        {
            if (Time.realtimeSinceStartup >= readyUntil) throw new InvalidOperationException("Actual awarded device-flow network endpoints are unavailable");
            yield return null;
        }
        foreach (string id in ids)
        {
            var row = new JObject { ["id"] = id, ["origin"] = FlNativeIds.Contains(id) ? "native" : "mod",
                ["fill"] = "not-tested", ["eject"] = "not-tested", ["activeWork"] = "not-tested" };
            _dfDevices.Add(row);
            foreach (object? wait in DfCase("idle." + id, () => FlAction(() => DfIdleDevice(id, row)))) yield return wait;
        }
        foreach (object? wait in DfCase("hopper-on-move-cross-page-reentry", DfHopperMove)) yield return wait;
        foreach (object? wait in DfCase("ras-two-real-nights-and-collection", DfRasNights)) yield return wait;
        foreach (string id in new[] { FlOwner + ".void_container", FlOwner + ".void_tank", FlOwner + ".void_grid" })
            foreach (GameItem item in Store.FindAllItem().Where(item => item.identifier == id).Distinct()) DfKeep(item);
        yield return null;
        yield return null;
        JArray retained = DfRetainedSnapshot("before-save");
        R.persistence["nativeDeviceFlows"] = new JObject { ["uids"] = new JArray(_dfPersist.OrderBy(uid => uid)),
            ["roots"] = retained, ["logistics"] = LogisticsState.DeepClone(), ["nights"] = _dfNights.DeepClone() };
        R.Skip("deviceflows.remaining-active-device-workloads", "35 actual idle/unsupported FillOnce/EjectOnce pairs above do not execute all native work/sale/chilling/aging/chemistry/vacuum behaviors. RAS/feeder/butcher work is executed only by the two-night case; logisticsfull has its own independently reported recipe work. No all-machine active-flow pass is claimed.", false);
        R.Skip("deviceflows.remaining-ras-variants", "One native Tilapia and one Electric Eel, first-night feeding/growth, second-night automatic harvest and collection are bounded representatives. All species/sex/breeding/roe/caviar/predation/disease/upgrades/full-tray/blackout/death/water-grade and other geometry combinations, same-day generation replay and fault injection remain not-tested.", false);
        R.Skip("deviceflows.remaining-hopper-variants", "Actual OnMove, two native source/target pages and one bounded receiving-callback RunAndRecord reentry are tested. All inventory callback faults, Fill/Eject/Gather recursion, cross-scene/drag frames and every stock-machine-specific hopper route remain not-tested.", false);
        R.environment["nativeDeviceFlowsExecutedChecksPassed"] = !R.checks.Any(check => check.status == "failed");
        R.environment["nativeDeviceFlowsCoverageComplete"] = false;
    }

    internal IEnumerable<object?> NativeDeviceFlowsReload(JObject baseline)
    {
        R.phase = "deviceflows-new-process-readback";
        var expected = baseline["persistence"]?["nativeDeviceFlows"] as JObject
            ?? throw new InvalidOperationException("Device-flow baseline persistence is missing");
        R.environment["nativeDeviceFlowPersistenceCustody"] = _dfCustody;
        int[] promised = (expected["uids"] as JArray)?.Values<int>().ToArray()
            ?? throw new InvalidOperationException("Device-flow baseline promised UID denominator is missing");
        int[] recorded = (expected["roots"] as JArray)?.Select(item => (int?)item["uid"] ?? 0).ToArray() ?? Array.Empty<int>();
        bool denominator = promised.Length > 0 && promised.All(uid => uid > 0) && promised.Distinct().Count() == promised.Length &&
            promised.OrderBy(uid => uid).SequenceEqual(recorded.OrderBy(uid => uid));
        R.Check("deviceflows.reload-baseline-promised-uid-denominator", "Every positive, unique promised UID has exactly one baseline snapshot", new { promised, recorded }, denominator,
            "A baseline with an omitted or duplicated promised item cannot be accepted even when its reduced live intersection would match.");
        if (!denominator) throw new InvalidOperationException("Device-flow baseline does not preserve its complete promised UID denominator");
        foreach (int uid in promised) _dfPersist.Add(uid);
        JArray actual = DfRetainedSnapshot("reload");
        R.Check("deviceflows.reload-native-uids-content-pages-and-ras-ports", expected["roots"], actual,
            DfReloadPersistenceContract(baseline, (JArray)expected["roots"]!, actual),
            "Raw snapshots remain recorded. Original ES3 bytes/nondefault tags must be exact. Only the two baseline RAS water-port UIDs may project the temperature double after exact native-callback, original encoded value, full 36-component ledger and scaled-clock-bounded integer orbit proof. Native default-disabled zero tags use the shared strict readback rule; all other payloads, identity, geometry, pages, ports and ledgers remain exact.");
        R.Check("deviceflows.reload-network-and-aquaculture-receipts", expected["logistics"], LogisticsState,
            JToken.DeepEquals(expected["logistics"], LogisticsState),
            "Includes the saved grid balance, generation/consumption totals, last AquacultureDay and generator UIDs. This does not claim same-day GeneratePower replay coverage.");
        R.persistence["nativeDeviceFlows"] = expected.DeepClone();
        yield return null;
    }

    private bool DfReloadPersistenceContract(JObject baseline, JArray expected, JArray observed)
    {
        // Reuse the reviewed pure readback rules and their negative gates.
        // Runtime DfTree/FlPhysical comparisons never call this projection.
        FlReloadProjectionNegativeControls();
        var evidence = new JObject { ["rawExpected"] = expected.DeepClone(), ["rawObserved"] = observed.DeepClone(),
            ["rule"] = "Build26317870 SaveItemNode default-disabled zero omission only; thermal scope is exactly the original two RAS fixed water ports. No epsilon or runtime-transfer normalization." };
        R.environment["nativeDeviceFlowReloadPersistenceContract"] = evidence;
        string file = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId)), hash = Reflect.HashFile(file);
        bool originalBytes = hash.Equals((string?)baseline["persistence"]?["saveSha256"], StringComparison.OrdinalIgnoreCase);
        evidence["file"] = file; evidence["sha256"] = hash;
        R.Check("deviceflows.reload-original-es3-bytes", baseline["persistence"]?["saveSha256"], hash, originalBytes);
        if (!originalBytes) throw new InvalidOperationException("Original DeviceFlow ES3 bytes changed; refusing readback projection");
        var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", new ES3Settings(file, ES3.Location.File))!;
        string[] fields = { "mainInvJSON", "backInvJSON", "trashcanInvJSON", "docInvJSON", "cassettePlayerInvJSON", "showcaseInvJSON", "soldInvJSON", "hirelingInvJSON", "hiddenInvJSON", "vendingMachineInvJSON", "vendingFountainInvJSON" };
        SaveItemNode[] nodes = fields.Select(field => (string?)Reflect.Get(saved, field)).Where(json => !string.IsNullOrEmpty(json))
            .SelectMany(json => JsonUtility.FromJson<SaveState>(json!).saveItems).ToArray();
        JObject[] Items(JArray snapshots) => snapshots.OfType<JObject>().SelectMany(item => new[] { item }.Concat(
            (item["children"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())).ToArray();
        JObject[] Liquids(JArray snapshots, int uid) => snapshots.OfType<JObject>().SelectMany(item =>
            (item["liquids"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()).Where(item => (int?)item["uid"] == uid)
            .Select(item => item["liquid"] as JObject).Where(item => item != null).Cast<JObject>().ToArray();
        JObject[] beforeItems = Items(expected), afterItems = Items(observed);
        var encodedRows = new JArray(); evidence["encodedTags"] = encodedRows;
        bool tagsPreserved = saved.runID == Store.runID && saved.saveSlotId == Store.saveSlotId;
        foreach (var group in beforeItems.GroupBy(item => (int)item["uid"]!).OrderBy(group => group.Key))
        {
            SaveItemNode[] matches = nodes.Where(node => node.uniqueId == group.Key).ToArray();
            JObject? raw = matches.Length == 1 ? FlReloadNativeTags(matches[0].itemState) : null;
            bool valid = matches.Length == 1 && group.All(item => matches[0].identifier == (string?)item["id"] && matches[0].unitCount == (int?)item["count"] &&
                matches[0].unitValue == (long?)item["value"] && matches[0].unitBaseValue == (long?)item["baseValue"] &&
                JToken.DeepEquals(FlReloadProjection(new JObject { ["tags"] = item["tags"]!.DeepClone() }), FlReloadProjection(new JObject { ["tags"] = raw!.DeepClone() })));
            var row = new JObject { ["uid"] = group.Key, ["nodeMatches"] = matches.Length, ["rawTags"] = raw, ["valid"] = valid };
            encodedRows.Add(row);
            R.Check("deviceflows.reload-encoded-original-nondefault-tags." + group.Key, "Actual original ES3 retains ID/count/value and every nondefault scalar tag", row, valid,
                "ReadStoreForPreview and native SaveState/SaveItemNode parsing only; no replacement GameItem factories, DecodeNodes, tag writes or native cycle replay.");
            tagsPreserved &= valid;
        }
        JObject[] ras = expected.OfType<JObject>().Where(item => (string?)item["id"] == "ras").ToArray();
        int[] waterUids = ras.Select(item => (int?)item["rasPorts"]?["water"] ?? 0).ToArray();
        bool scope = ras.Length == 2 && ras.Select(item => (int)item["uid"]!).Distinct().Count() == 2 && waterUids.All(uid => uid > 0) && waterUids.Distinct().Count() == 2;
        R.Check("deviceflows.reload-thermal-scope-original-two-ras-water-ports", "Exactly two distinct original RAS machines and their two fixed water UIDs", new { ras, waterUids }, scope);
        float levelTime = Time.timeSinceLevelLoad;
        int maximum = !float.IsNaN(levelTime) && !float.IsInfinity(levelTime) && levelTime >= 0 && levelTime < int.MaxValue - 2 ? (int)Math.Ceiling(levelTime) + 1 : -1;
        bool sameDay = (int?)baseline["persistence"]?["day"] == StoreStation.GetDayCounter();
        evidence["clock"] = JObject.FromObject(new { scaledTimeSinceLevelLoad = levelTime, Time.timeScale, maximumAmbientTicks = maximum, sameDay,
            rule = "EmporiumEntry.Update accumulates Time.deltaTime from a zero oneSecondTimer. ceil(Time.timeSinceLevelLoad)+1 bounds native one-second dispatches conservatively. No wall-clock bound, time-scale assignment or explicit thermal callback invocation." });
        var nativeTick = (Action<GameItem, GameInventory, SlotMarker>)Reflect.Get(typeof(ChemicalHelper), "containerTempTick")!;
        GameItem[] live = Store.FindAllItem(false).ToArray(); var approved = new HashSet<int>();
        var thermalRows = new JArray(); evidence["thermalRows"] = thermalRows;
        foreach (int uid in waterUids.Distinct())
        {
            JObject[] before = beforeItems.Where(item => (int?)item["uid"] == uid).ToArray(), after = afterItems.Where(item => (int?)item["uid"] == uid).ToArray();
            JObject[] beforeLiquid = Liquids(expected, uid), afterLiquid = Liquids(observed, uid);
            JObject[] parent = ras.Where(item => (int?)item["rasPorts"]?["water"] == uid).ToArray();
            int parentUid = parent.Length == 1 ? (int)parent[0]["uid"]! : 0;
            GameItem[] currentWater = live.Where(item => item.uniqueId == uid).ToArray(), currentRas = live.Where(item => item.uniqueId == parentUid).ToArray();
            SaveItemNode[] encoded = nodes.Where(node => node.uniqueId == uid).ToArray();
            double initial = before.Length > 0 ? (double?)before[0]["tags"]?["CONTAINER_TEMP_TAG"]?["valueDouble"] ?? double.NaN : double.NaN;
            double current = after.Length > 0 ? (double?)after[0]["tags"]?["CONTAINER_TEMP_TAG"]?["valueDouble"] ?? double.NaN : double.NaN;
            int parts = beforeLiquid.Length > 0 ? (int?)beforeLiquid[0]["TotalParts"] ?? 0 : 0, ticks = -1;
            JObject[] components = (beforeLiquid.FirstOrDefault()?["Contents"] as JArray)?.OfType<JObject>().ToArray() ?? Array.Empty<JObject>();
            bool liquidsExact = beforeLiquid.Length > 0 && beforeLiquid.All(item => JToken.DeepEquals(item, beforeLiquid[0])) &&
                afterLiquid.Length == beforeLiquid.Length && afterLiquid.All(item => JToken.DeepEquals(item, beforeLiquid[0]));
            bool rawPartsExact = encoded.Length == 1 && components.Length == Liquid.Liquids.Count &&
                components.Select(item => (string?)item["LiquidId"]).Distinct().Count() == Liquid.Liquids.Count && Liquid.Liquids.All(liquid =>
                    components.Any(item => (string?)item["LiquidId"] == liquid.identifier && (int?)item["Parts"] ==
                        (encoded[0].itemState.dict.TryGetValue(liquid.currentPart, out var part) ? part.valueInt : 0))) &&
                parts == components.Sum(item => (int)item["Parts"]!);
            bool valid = scope && tagsPreserved && sameDay && maximum >= 0 && before.Length > 0 && after.Length == before.Length &&
                before.All(item => (int?)item["parentUid"] == parentUid && (bool?)item["tags"]?["CONTAINER_TEMP_TAG"]?["valueEnabled"] == true && (double?)item["tags"]?["CONTAINER_TEMP_TAG"]?["valueDouble"] == initial) &&
                after.All(item => (int?)item["parentUid"] == parentUid && (bool?)item["tags"]?["CONTAINER_TEMP_TAG"]?["valueEnabled"] == true && (double?)item["tags"]?["CONTAINER_TEMP_TAG"]?["valueDouble"] == current) &&
                currentWater.Length == 1 && currentRas.Length == 1 && currentRas[0].identifier == "ras" && GeneralHelper.IsItemOwned(currentRas[0]) &&
                currentWater[0].identifier == "ras_water_tank" && currentWater[0].unitCount > 0 && GeneralHelper.IsItemOwned(currentWater[0]) &&
                MachineRAS.GetWaterSlot(currentRas[0]).childItem == currentWater[0] && currentWater[0].onOneSecondItemFunc == nativeTick &&
                !currentWater[0].IsTag("HEATING_TAG") && encoded.Length == 1 && !encoded[0].itemState.IsTag("HEATING_TAG") &&
                encoded[0].itemState.dict.TryGetValue("CONTAINER_TEMP_TAG", out var rawTemperature) && rawTemperature.valueEnabled && rawTemperature.valueDouble == initial &&
                liquidsExact && rawPartsExact && FlReloadExactAmbientTicks(initial, current, parts, maximum, out ticks);
            var row = new JObject { ["uid"] = uid, ["rasUid"] = parentUid, ["initial"] = initial, ["observed"] = current, ["parts"] = parts,
                ["exactTicks"] = ticks, ["maximum"] = maximum, ["liquidCopiesExact"] = liquidsExact, ["rawPartsExact"] = rawPartsExact, ["valid"] = valid,
                ["rawBeforeTemperature"] = before.FirstOrDefault()?["tags"]?["CONTAINER_TEMP_TAG"]?.DeepClone(),
                ["rawAfterTemperature"] = after.FirstOrDefault()?["tags"]?["CONTAINER_TEMP_TAG"]?.DeepClone(),
                ["rawEncodedTemperature"] = encoded.Length == 1 && encoded[0].itemState.dict.TryGetValue("CONTAINER_TEMP_TAG", out var rawTag) ? FlReloadNativeTag(rawTag) : JValue.CreateNull() };
            if (valid)
            {
                var trajectory = new JArray(); double predicted = initial, decay = Math.Exp(-(0.05 / (Math.Sqrt(parts) + 11.0)) * 125.0);
                for (int step = 0; step <= ticks + 1; step++)
                {
                    trajectory.Add(new JObject { ["tick"] = step, ["predicted"] = predicted, ["exactObservedMatch"] = predicted == current, ["withinScaledClockBound"] = step <= maximum });
                    predicted = 26.0 + (predicted - 26.0) * decay;
                }
                row["exactTrajectoryThroughNextTick"] = trajectory; approved.Add(uid);
            }
            thermalRows.Add(row);
            R.Check("deviceflows.reload-original-ras-temperature-native-integer-orbit." + uid,
                "Exact original ES3 temperature and only the bounded native ambient trajectory; actual original port/callback/ownership and unchanged 36-component ledgers", row, valid);
        }
        bool bytesStillExact = Reflect.HashFile(file).Equals(hash, StringComparison.OrdinalIgnoreCase);
        R.Check("deviceflows.reload-original-es3-remains-unmodified", hash, Reflect.HashFile(file), bytesStillExact);
        evidence["approvedAmbientUids"] = new JArray(approved.OrderBy(uid => uid));
        JToken projectedExpected = FlReloadProjection(expected, approved), projectedObserved = FlReloadProjection(observed, approved);
        evidence["projectedExpected"] = projectedExpected; evidence["projectedObserved"] = projectedObserved;
        return tagsPreserved && scope && sameDay && maximum >= 0 && approved.Count == 2 && bytesStillExact && JToken.DeepEquals(projectedExpected, projectedObserved);
    }

    private IEnumerable<object?> DfCase(string name, Func<IEnumerable<object?>> action)
    {
        int first = R.checks.Count;
        var row = new JObject { ["name"] = name, ["firstCheck"] = first, ["status"] = "running" };
        _dfCases.Add(row);
        IEnumerator<object?>? work = null;
        try
        {
            try { work = action().GetEnumerator(); }
            catch (Exception error) { row["exception"] = error.ToString(); R.Check("deviceflows.case." + name, "Independent native scenario entered", null, false, error: error.ToString()); }
            while (work != null)
            {
                bool more = false; object? current = null; Exception? failure = null;
                try { more = work.MoveNext(); if (more) current = work.Current; }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    row["exception"] = failure.ToString();
                    R.Check("deviceflows.case." + name + ".exception", "Native scenario completes its required observations", null, false,
                        "Independent cases continue after a failed case; a thrown prerequisite/action never becomes a pass or optional skip.", failure.ToString());
                    break;
                }
                if (!more) break;
                yield return current;
            }
        }
        finally
        {
            try { work?.Dispose(); }
            catch (Exception error) { row["cleanupException"] = error.ToString(); R.Check("deviceflows.case." + name + ".cleanup", "Scenario finally completed", null, false, error: error.ToString()); }
            if (R.checks.Count == first) R.Check("deviceflows.case." + name + ".empty", "At least one actual observation", null, false);
            row["lastCheckExclusive"] = R.checks.Count;
            row["status"] = R.checks.Skip(first).Any(check => check.status == "failed") ? "failed"
                : R.checks.Skip(first).Any(check => check.status == "not-tested") ? "partial" : "passed";
        }
    }

    private JObject DfTree(GameItem item)
    {
        JObject state = FlItem(item);
        state["children"] = new JArray(item.FindAllChildItems().Distinct().OrderBy(child => child.uniqueId).Select(FlItem));
        state["liquids"] = new JArray(new[] { item }.Concat(item.FindAllChildItems()).Where(child => child.IsTag("LIQUID_CONTAINER_TAG"))
            .Distinct().OrderBy(child => child.uniqueId).Select(child => new JObject { ["uid"] = child.uniqueId, ["liquid"] = FlLiquid(child) }));
        return state;
    }
    private void DfKeep(GameItem item)
    {
        Retain(item);
        // Commit this family's actual root and current child UID denominator now,
        // before the later evidence/save frame yields. Global _retained contains
        // already disposed idle fixtures and is deliberately not this promise.
        foreach (GameItem member in new[] { item }.Concat(item.FindAllChildItems())) _dfPersist.Add(member.uniqueId);
        // Blend/harvest consumption completes and is independently checked before
        // DfKeep. Its consumed food/fish never enters this set. There is no path
        // that removes a promise just because a later lookup cannot find it.
    }
    private JArray DfRetainedSnapshot(string phase)
    {
        R.phase = "deviceflows-persistence-" + phase;
        var custody = new JArray(); _dfCustody[phase] = custody;
        // The native default filters out unowned items. Include them here so a
        // custody loss is reported as such instead of silently shrinking the set.
        GameItem[] live = Store.FindAllItem(false).ToArray();
        var resolved = new List<GameItem>();
        bool complete = _dfPersist.Count > 0;
        R.Check("deviceflows.persistence-nonempty-promised-denominator." + phase, "At least one family-promised UID", _dfPersist.OrderBy(uid => uid).ToArray(), complete);
        foreach (int uid in _dfPersist.OrderBy(uid => uid))
        {
            GameItem[] matches = live.Where(item => item.uniqueId == uid).ToArray();
            bool valid = uid > 0 && matches.Length == 1 && GeneralHelper.IsItemOwned(matches[0]) && matches[0].unitCount > 0;
            var observation = new JObject { ["uid"] = uid, ["matches"] = matches.Length,
                ["items"] = new JArray(matches.Select(FlItem)), ["valid"] = valid };
            custody.Add(observation);
            R.Check("deviceflows.persistence-promised-uid-custody." + phase + "." + uid,
                new { uid, matches = 1, owned = true, positiveCount = true }, observation, valid,
                "Resolve each committed family UID independently in the full native live graph. Missing, duplicate, unowned or nonpositive-count items remain required failures; live intersection and global idle-fixture retention cannot redefine this denominator.");
            complete &= valid;
            if (valid) resolved.Add(matches[0]);
        }
        if (!complete) throw new InvalidOperationException("Device-flow " + phase + " lost or invalidated a promised UID; refusing to create an incomplete snapshot/baseline");
        var result = new JArray();
        foreach (GameItem item in resolved)
        {
            JObject state = DfTree(item);
            if (item.identifier == "hypertech_bay")
            {
                var tabs = DfTabs(item);
                state["pages"] = new JArray(Enumerable.Range(0, tabs.count).Select(index => new JObject { ["index"] = index,
                    ["uids"] = new JArray(tabs.GetTabInventory(index).childItems.Select(child => child.uniqueId).OrderBy(childUid => childUid)) }));
            }
            if (item.identifier == "ras") state["rasPorts"] = DfRasPorts(item);
            result.Add(state);
        }
        return result;
    }
    private static MachineryTabs DfTabs(GameItem bay)
    {
        if (bay.contentWindow?.childElement is MachineryPanelRow row)
        {
            var tabs = row.Panels.Select(panel => panel.content).OfType<MachineryTabs>().ToArray();
            if (tabs.Length == 1) return tabs[0];
        }
        throw new InvalidOperationException("Native hypertech_bay does not expose its expected real MachineryTabs layout");
    }
    private static JObject DfRasPorts(GameItem ras) => new()
    {
        ["battery"] = (MachineHelper.GetBodyStorage(ras, 0) as GameSlotInventory)?.childItem?.uniqueId ?? 0,
        ["water"] = MachineRAS.GetWaterSlot(ras)?.childItem?.uniqueId ?? 0,
        ["dispenser"] = MachineRAS.GetDispenserSlot(ras)?.childItem?.uniqueId ?? 0,
        ["butcher"] = MachineRAS.GetButcherSlot(ras)?.childItem?.uniqueId ?? 0,
        ["stock"] = new JArray(MachineRAS.GetTankGrid(ras).childItems.Select(item => item.uniqueId).OrderBy(uid => uid)),
        ["filters"] = new JArray(Enumerable.Range(0, 4).Select(index => (MachineRAS.GetFilterGrid(ras).GetElement(index, 0) as GameSlotInventory)?.childItem?.uniqueId ?? 0))
    };

    private void DfIdleDevice(string id, JObject row)
    {
        GameItem? device = null, outside = null, battery = null;
        var grants = new List<GameItem>();
        try
        {
            device = Spawn(id); Retain(device); outside = Spawn("wire"); Retain(outside);
            bool expectedAdapter = FlNativeIds.Take(8).Contains(id) || FlModIds.Contains(id);
            var access = Reflect.Type("Nicokobo.AutomatedLogistics.Integration.MachineAccess", "Nicokobo.AutomatedLogistics");
            bool actualAdapter = (bool)Reflect.Call(access, "TryInventory", device, null)!;
            GameInventory? adaptedInput = null;
            // Reflect.Call does not expose an out value; read actual adapter ports
            // only when its returned support flag is true.
            R.Check("deviceflows.adapter-capability." + id, expectedAdapter, actualAdapter, actualAdapter == expectedAdapter);
            if (actualAdapter)
            {
                object ports = Reflect.Call(Logistics, "Inventory", device)!;
                adaptedInput = Reflect.Get(ports, "ItemInput") as GameInventory;
                if (Reflect.Get(ports, "Modules") is not GameInventory modules) throw new InvalidOperationException("Supported device has no actual module bay");
                var feederModule = Spawn(FlFeeder); grants.Add(feederModule); Place(modules, feederModule, "deviceflows-feeder:" + id, device);
                var ejectorModule = Spawn(FlEjector); grants.Add(ejectorModule); Place(modules, ejectorModule, "deviceflows-ejector:" + id, device);
                if (Reflect.Get(ports, "Battery") is GameSlotInventory adapterBatterySlot && adapterBatterySlot.childItem == null)
                { battery = PreBuiltItemHelper.EnergyCredit(0); Place(adapterBatterySlot, battery, "deviceflows-battery:" + id, device); }
            }
            else if (MachineHelper.GetBodyStorage(device, 0) is GameSlotInventory nativeFirstSlot && nativeFirstSlot.childItem == null)
            {
                battery = PreBuiltItemHelper.EnergyCredit(0);
                if (!FlTryPlace(nativeFirstSlot, battery)) { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(battery); battery = null; }
            }
            var contents = new JArray();
            foreach (string sampleId in new[] { "wire", "common_ore", "printer_plastic", "fish_fillet", "water_jug", "industrial_fish_feed" })
            {
                GameItem sample = Spawn(sampleId);
                if (sample.IsTag("LIQUID_CONTAINER_TAG")) WaterHelper.EmptyContainer(sample);
                bool placed = false;
                IEnumerable<GameInventory> inventories = actualAdapter
                    ? adaptedInput == null ? Enumerable.Empty<GameInventory>() : new[] { adaptedInput }
                    : GraphUtils.FindAllChildrenType<GameInventory>(device).Distinct();
                foreach (var inventory in inventories)
                {
                    if (!FlTryPlace(inventory, sample)) continue;
                    grants.Add(sample); contents.Add(FlItem(sample)); placed = true; break;
                }
                if (!placed) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(sample);
                else break;
            }
            Reflect.Call(Logistics, "ConfigureMachine", device, true, true);
            var before = new JArray(DfTree(device), FlItem(outside)); long grid = GridTotal;
            row["uid"] = device.uniqueId; row["adapter"] = actualAdapter; row["fixtureContents"] = contents;
            row["sampleAdmission"] = contents.Count > 0 ? "actual-native-direct-slot" : "six-native-candidates-rejected-or-no-restock-input; empty-port-control";
            row["reason"] = actualAdapter ? "Real module bay and enabled Fill/Eject; fresh device has no consumed-material debt. This is an idle invariant, not active recipe coverage."
                : "No current native logistics inventory adapter. Actual public calls must return without moving the legally admitted fixture contents or the external source UID.";
            Exception? fillError = null;
            try { Reflect.Call(Logistics, "FillOnce", device); }
            catch (Exception error) { fillError = error; }
            var filled = new JArray(DfTree(device), FlItem(outside));
            bool fillSame = fillError == null && JToken.DeepEquals(before, filled) && GridTotal == grid;
            R.Check("deviceflows.actual-fill-idle-or-unsupported." + id, new { state = before, grid }, new { state = filled, grid = GridTotal, exception = fillError?.ToString() }, fillSame,
                (string)row["reason"]! + " Factory sample contents are explicitly preparation grants, never claimed as produced output.", fillError?.ToString());
            row["fill"] = fillSame ? actualAdapter ? "actual-idle-noop" : "actual-unsupported-noop" : "failed";
            Exception? ejectError = null;
            try { Reflect.Call(Logistics, "EjectOnce", device); }
            catch (Exception error) { ejectError = error; }
            var ejected = new JArray(DfTree(device), FlItem(outside));
            bool ejectSame = ejectError == null && JToken.DeepEquals(filled, ejected) && GridTotal == grid;
            R.Check("deviceflows.actual-eject-idle-or-unsupported." + id, new { state = filled, grid }, new { state = ejected, grid = GridTotal, exception = ejectError?.ToString() }, ejectSame,
                "Actual EjectOnce; unchanged native UID/count/parent/value/tags/shape/contents and source UID. Unsupported does not mean the machine has no logistics need.", ejectError?.ToString());
            row["eject"] = ejectSame ? actualAdapter ? "actual-idle-noop" : "actual-unsupported-noop" : "failed";
            row["before"] = before; row["after"] = ejected;
        }
        finally
        {
            FlCleanup("deviceflows.idle." + id,
                () => { if (device != null) Reflect.Call(Logistics, "ConfigureMachine", device, false, false); },
                () => Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(outside),
                () => Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(device),
                () => { foreach (var grant in grants) if (grant.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(grant); },
                () => { if (battery?.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(battery); });
        }
    }

    private IEnumerable<object?> DfHopperMove()
    {
        GameItem? source = null, target = null, intake = null, outlet = null;
        GameGridInventory? destination = null;
        Action<GameItem, GameInventory, SlotMarker>? prior = null;
        var created = new List<GameItem>();
        GameItem Grant(string id) { var item = Spawn(id); created.Add(item); return item; }
        bool kept = false, callbackInstalled = false;
        try
        {
            source = Grant("hypertech_bay"); target = Grant("hypertech_bay"); Retain(source); Retain(target);
            var sourceTabs = DfTabs(source); var targetTabs = DfTabs(target);
            if (sourceTabs.count != 2 || targetTabs.count != 2) throw new InvalidOperationException("Expected the unexpanded native two-page bays");
            var sourceControl = (GameGridInventory)sourceTabs.GetTabInventory(0);
            var sourceLive = (GameGridInventory)sourceTabs.GetTabInventory(1);
            var targetControl = (GameGridInventory)targetTabs.GetTabInventory(0);
            destination = (GameGridInventory)targetTabs.GetTabInventory(1);
            intake = Grant("intake_hopper"); outlet = Grant("outlet_hopper");
            intake.ModifyTag(HopperHelper.ON_ADD_BOOL, tag => tag.Enable().SetBool(false));
            intake.ModifyTag(HopperHelper.ON_MOVE_BOOL, tag => tag.Enable().SetBool(false));
            Place(sourceControl, intake, "deviceflows-hopper-start-page0"); Place(destination, outlet, "deviceflows-hopper-outlet-page1");
            HopperHelper.Link(intake, outlet); HopperHelper.SetFilterList(intake, new List<string> { "wire" });
            intake.ModifyTag(HopperHelper.FILTER_MODE_INT, tag => tag.Enable().SetInt(HopperHelper.FILTER_ALLOW));
            intake.ModifyTag(HopperHelper.KEEP_INT, tag => tag.Enable().SetInt(0));
            var stock = Enumerable.Range(0, 3).Select(_ => Grant("wire")).ToArray();
            foreach (var item in stock) Place(sourceLive, item, "deviceflows-hopper-hidden-source");
            var control = Grant("wire"); Place(sourceControl, control, "deviceflows-hopper-visible-page-control");
            var excluded = Grant("common_ore"); Place(sourceLive, excluded, "deviceflows-hopper-filter-control");
            string[] materialIds = { "wire", "common_ore" };
            JArray invoice = FlInvoice(materialIds), stockBefore = new(stock.Select(FlPhysical));
            int[] uids = stock.Select(item => item.uniqueId).OrderBy(uid => uid).ToArray();
            JToken controlBefore = FlItem(control), excludedBefore = FlItem(excluded);
            int targetControlCount = targetControl.childItems.Count;
            int callbacks = 0, nestedAttempts = 0, nestedMoved = -1;
            GameItem linkedIntake = intake;
            prior = destination.onSlotAddItemEndFunc;
            destination.onSlotAddItemEndFunc = (item, inventory, marker) =>
            {
                prior?.Invoke(item, inventory, marker);
                if (item.identifier != "wire") return;
                callbacks++;
                // Explicit bound even if the native reentry guard regresses.
                if (nestedAttempts != 0) return;
                nestedAttempts++;
                nestedMoved = HopperHelper.RunAndRecord(linkedIntake);
            };
            callbackInstalled = true;
            sourceTabs.Select(0, true); targetTabs.Select(0, true);
            intake.ModifyTag(HopperHelper.ON_MOVE_BOOL, tag => tag.Enable().SetBool(true));
            Place(sourceLive, intake, "deviceflows-hopper-real-move-to-hidden-page1");
            float until = Time.realtimeSinceStartup + 5f;
            while (stock.Any(item => item.parentInventory != destination) && Time.realtimeSinceStartup < until) yield return null;
            var after = new JObject { ["sourcePageSelected"] = sourceTabs.selected, ["targetPageSelected"] = targetTabs.selected,
                ["callbacks"] = callbacks, ["nestedAttempts"] = nestedAttempts, ["nestedMoved"] = nestedMoved,
                ["stock"] = new JArray(stock.Select(FlItem)), ["sourcePage0"] = new JArray(sourceControl.childItems.Select(FlItem)),
                ["targetPage0"] = new JArray(targetControl.childItems.Select(FlItem)), ["targetPage1"] = new JArray(destination.childItems.Select(FlItem)), ["invoice"] = FlInvoice(materialIds) };
            R.environment["nativeDeviceFlowHopper"] = after;
            R.Check("deviceflows.hopper-real-OnMove-hidden-source-target-pages", new { sourcePage = 1, targetPage = 1, selectedPage = 0, uids, invoice }, after,
                sourceTabs.selected == 0 && targetTabs.selected == 0 && stock.All(item => item.parentInventory == destination) &&
                destination.childItems.Where(item => item.identifier == "wire").Select(item => item.uniqueId).OrderBy(uid => uid).SequenceEqual(uids) &&
                targetControl.childItems.Count == targetControlCount && JToken.DeepEquals(controlBefore, FlItem(control)) &&
                JToken.DeepEquals(excludedBefore, FlItem(excluded)) && JToken.DeepEquals(invoice, FlInvoice(materialIds)) && JToken.DeepEquals(stockBefore, new JArray(stock.Select(FlPhysical))),
                "Native SlotMarker moves the intake from page0 to page1; its installed OnMove schedules RunPending. No direct RunWindow/RunAndRecord starts the outer transfer, and the visible unrelated pages cannot change physical parent selection.");
            R.Check("deviceflows.hopper-receiving-callback-bounded-reentry", new { callbacks = 3, nestedAttempts = 1, nestedMoved = 0 },
                new { callbacks, nestedAttempts, nestedMoved }, callbacks == 3 && nestedAttempts == 1 && nestedMoved == 0,
                "Actual target onSlotAddItemEnd callback calls the real RunAndRecord exactly once. Native transferring guard must prevent recursion; no lower transfer result or guard field is mocked.");
            JArray settled = new(stock.Select(FlItem));
            float settleUntil = Time.realtimeSinceStartup + 0.35f;
            while (Time.realtimeSinceStartup < settleUntil) yield return null;
            R.Check("deviceflows.hopper-no-deferred-replay", settled, new JArray(stock.Select(FlItem)), callbacks == 3 && JToken.DeepEquals(settled, new JArray(stock.Select(FlItem))) && JToken.DeepEquals(invoice, FlInvoice(materialIds)));
            destination.onSlotAddItemEndFunc = prior; callbackInstalled = false;
            foreach (object? wait in CaptureEvidenceFrame("deviceflows-hopper-OnMove-hidden-pages")) yield return wait;
            DfKeep(source); DfKeep(target); kept = true;
        }
        finally
        {
            if (destination != null && callbackInstalled) destination.onSlotAddItemEndFunc = prior;
            if (!kept) FlCleanup("deviceflows.hopper", () => Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(source),
                () => Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(target), () => { if (intake?.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(intake); },
                () => { if (outlet?.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(outlet); },
                () => { foreach (var item in created) if (item.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); });
        }
    }

    private sealed class DfRasFixture
    {
        internal GameItem Machine = null!, Battery = null!, Water = null!, Feeder = null!, Fish = null!;
        internal GameItem? Butcher;
    }
    private static JObject DfFish(GameItem fish) => new()
    {
        ["item"] = FlItem(fish), ["age"] = fish.GetTagReadonly("FISH_AGE_TAG").GetInt(), ["weight"] = AquacultureHelper.GetWeightGrams(fish),
        ["hunger"] = AquacultureHelper.GetHunger(fish), ["health"] = AquacultureHelper.GetHealth(fish),
        ["kcalEaten"] = AquacultureHelper.GetKcalEaten(fish), ["dead"] = AquacultureHelper.IsDead(fish)
    };
    private static int DfFeed(GameItem feeder) => feeder.GetTagReadonly("STORED_KCAL_TAG").GetInt();
    private static long DfPowerTotal(JToken state, string field) => (long?)state["Power"]?[field] ?? 0;
    private DfRasFixture DfMakeRas(GameGridInventory stage, int x, int y, string fishId, int energy, bool butcher)
    {
        var created = new List<GameItem>();
        GameItem Grant(string id) { var item = Spawn(id); created.Add(item); return item; }
        var f = new DfRasFixture { Machine = Grant("ras") };
        try
        {
            if (!FlAt(stage, f.Machine, x, y)) throw new InvalidOperationException("Native stage refuses RAS at its requested actual geometry");
            f.Battery = PreBuiltItemHelper.EnergyCredit(energy);
            created.Add(f.Battery);
            Place((GameSlotInventory)MachineHelper.GetBodyStorage(f.Machine, 0), f.Battery, "deviceflows-ras-battery:" + fishId);
            f.Water = Grant("ras_water_tank"); WaterHelper.EmptyContainer(f.Water); WaterHelper.AddPureWater(f.Water, 12000000);
            Place(MachineRAS.GetWaterSlot(f.Machine), f.Water, "deviceflows-ras-fixed-12L-tank:" + fishId);
            Place((GameSlotInventory)MachineRAS.GetFilterGrid(f.Machine).GetElement(0, 0), Grant("water_filter"), "deviceflows-ras-filter:" + fishId);
            f.Feeder = Grant("livestock_feed_dispenser");
            Place(MachineRAS.GetDispenserSlot(f.Machine), f.Feeder, "deviceflows-ras-feeder:" + fishId);
            var food = (fishId == "tilapia" ? new[] { Grant("industrial_fish_feed") } : new[] { Grant("fish_fillet"), Grant("fish_fillet") });
            int physicalKcal = food.Sum(item => item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt()), stored = DfFeed(f.Feeder);
            int[] foodUids = food.Select(item => item.uniqueId).ToArray();
            foreach (var item in food) Place(MachineFeedDispenser.GetStorageGrid(f.Feeder), item, "deviceflows-ras-physical-feed:" + fishId);
            MachineFeedDispenser.Blend(f.Feeder, MachineFeedDispenser.GetStorageGrid(f.Feeder));
            R.Check("deviceflows.ras-native-physical-blend." + fishId, new { physicalKcal, storedAfter = stored + physicalKcal, consumedUids = foodUids },
                new { storedAfter = DfFeed(f.Feeder), remaining = MachineFeedDispenser.GetStorageGrid(f.Feeder).childItems.Select(FlItem).ToArray() },
                physicalKcal > 0 && DfFeed(f.Feeder) == stored + physicalKcal && !Store.FindAllItem().Any(item => foodUids.Contains(item.uniqueId)),
                "True calorie-bearing factory food is placed and native Blend consumes it; STORED_KCAL and fish completion fields are never assigned by the probe.");
            f.Fish = Grant(fishId); Place(MachineRAS.GetTankGrid(f.Machine), f.Fish, "deviceflows-ras-native-born-stock:" + fishId);
            if (butcher)
            {
                f.Butcher = Grant("auto_butcher"); Place(MachineRAS.GetButcherSlot(f.Machine), f.Butcher, "deviceflows-ras-host-butcher");
                R.Check("deviceflows.ras-native-butcher-host-and-off", new { host = f.Machine.uniqueId, harvest = 0 },
                    new { host = MachineAutoButcher.GetHost(f.Butcher)?.uniqueId ?? 0, harvest = f.Butcher.GetTagReadonly("AUTO_BUTCHER_TANK_HARVEST_INT").GetInt() },
                    MachineAutoButcher.GetHost(f.Butcher) == f.Machine && f.Butcher.GetTagReadonly("AUTO_BUTCHER_TANK_HARVEST_INT").GetInt() == 0);
            }
            return f;
        }
        catch
        {
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(f.Machine);
            foreach (var item in created) if (item.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
            throw;
        }
    }

    // Independent scan of usable inventory cells bordering the placed RAS.
    // It never invokes AquacultureGridCells / BeforeAquaculturePower for answers.
    private static int DfOutlineCells(GameItem ras)
    {
        if (ras.parentInventory is not GameGridInventory grid) return 0;
        var shape = ras.modifiedShape; var area = grid.inventoryShape; int count = 0;
        foreach (int y in Enumerable.Range(area.minY, area.height))
        foreach (int x in Enumerable.Range(area.minX, area.width))
        {
            if (area.Get(x, y) != 0 || shape.Get(x, y) == 1 ||
                !(shape.Get(x - 1, y) == 1 || shape.Get(x + 1, y) == 1 || shape.Get(x, y - 1) == 1 || shape.Get(x, y + 1) == 1)) continue;
            bool occupiedCharger = grid.childItems.Any(item => item != ras && (item.IsTag("power_source_item") || item.identifier is "powerblock" or "recharger" or "recharger_base") && item.modifiedShape.Get(x, y) == 1);
            if (!occupiedCharger) count++;
        }
        return count;
    }
    private void DfCheckFed(DfRasFixture f, JObject before, int feederBefore, string prefix, bool exactAge)
    {
        JObject after = DfFish(f.Fish);
        int debit = feederBefore - DfFeed(f.Feeder);
        double absorbed = (double)after["kcalEaten"]! - (double)before["kcalEaten"]!;
        double target = f.Fish.identifier == "tilapia" ? 595.0 : 1020.0;
        bool age = exactAge ? (int)after["age"]! == (int)before["age"]! + 1 : (int)after["age"]! >= (int)before["age"]!;
        R.Check(prefix + ".real-feed-growth-and-calorie-debit", new { original = before, feederBefore, target, positiveFeedDebit = true, ageIncreases = exactAge },
            new { stock = after, feederAfter = DfFeed(f.Feeder), debit, absorbed, roundingLoss = debit - absorbed },
            age && (int)after["weight"]! > (int)before["weight"]! && debit > 0 && absorbed > 0 && debit - absorbed >= -0.01 && debit - absorbed < 1.01 &&
                Math.Abs((double)after["hunger"]! - target) < 0.02 && (double)after["health"]! == (double)before["health"]! && (bool?)after["dead"] == false &&
                f.Fish.parentInventory == MachineRAS.GetTankGrid(f.Machine),
            "Real EndNight invokes native TickTank/age/hunger/feeding. Fish absorption and physical feeder debit are independent readbacks; native CeilToInt may discard less than one calorie while filling to the factory 85% target. No completed fish, age or feed ledger is injected.");
    }
    private void DfCheckRasPower(DfRasFixture local, DfRasFixture grid, JObject before, int localEnergy, int gridEnergy, int outline, int day, string prefix)
    {
        JToken after = LogisticsState;
        long gridBefore = (long)before["total"]!;
        long generated = DfPowerTotal(after, "Generated") - (long)before["generated"]!;
        long consumed = DfPowerTotal(after, "Consumed") - (long)before["consumed"]!;
        long transferred = DfPowerTotal(after, "Transferred") - (long)before["transferred"]!;
        int[] receipts = (after["Power"]?["AquacultureGenerators"] as JArray)?.Values<int>().OrderBy(uid => uid).ToArray() ?? Array.Empty<int>();
        R.Check(prefix + ".native-eel-charge-grid-generation-and-cycle-power", new { nativeEelOutput = 1, outline, generated = outline, gridAfter = gridBefore + outline - 5,
                gridDebit = 5, consumed = 10, localBatteryAfter = localEnergy - 5, eelBatteryAfter = gridEnergy + 1, receiptDay = day },
            new { generated, consumed, transferred, gridAfter = GridTotal, localBatteryAfter = Energy(local.Battery), eelBatteryAfter = Energy(grid.Battery),
                receiptDay = after["Power"]?["AquacultureDay"], receipts, powerState = after["Power"] },
            outline > 0 && generated == outline && consumed == 10 && transferred == 5 && GridTotal == gridBefore + outline - 5 && Energy(local.Battery) == localEnergy - 5 &&
                Energy(grid.Battery) == gridEnergy + 1 && (int?)after["Power"]?["AquacultureDay"] == day && receipts.Contains(local.Machine.uniqueId) && receipts.Contains(grid.Machine.uniqueId),
            "Native juvenile eel outputs one charge in the early callback for both observed nights. Independently scanned usable outline cells each contribute one to the grid. Unmodified RAS cost is 5: full local battery pays whole locally; low eel battery retains native charge while the grid pays the whole cycle. No sum of incompatible partial supplies or ledger assignment.");
    }
    private static JObject DfPowerBefore(long total, JToken state) => new()
    { ["total"] = total, ["generated"] = DfPowerTotal(state, "Generated"), ["consumed"] = DfPowerTotal(state, "Consumed"), ["transferred"] = DfPowerTotal(state, "Transferred") };

    private IEnumerable<object?> DfRasNights()
    {
        GameItem? stage = null, intake = null, outlet = null;
        DfRasFixture? tilapia = null, eel = null;
        bool kept = false;
        int firstCheck = R.checks.Count;
        try
        {
            var uidsBeforeUnlock = Store.FindAllItem().Select(item => item.uniqueId).ToHashSet();
            UnlockFixtureUpgrade("AQUACULTURE"); UnlockFixtureUpgrade("AQUACULTURE_II");
            var grants = Store.FindAllItem().Where(item => !uidsBeforeUnlock.Contains(item.uniqueId)).Distinct().ToArray();
            var grantedUids = grants.Select(item => item.uniqueId).ToHashSet();
            R.environment["nativeDeviceFlowRasPrerequisites"] = new JObject { ["unlocks"] = new JArray("AQUACULTURE", "AQUACULTURE_II"),
                ["factoryGrantDisposition"] = "Native Unlock rewards are recorded and only these newly created root UIDs are disposed before the separate RAS test fixture. Real queued customers and all other upgrade effects remain native.",
                ["nativeGrantedItems"] = new JArray(grants.Select(FlItem)) };
            foreach (var grant in grants.Where(item => item.parentInventory?.GetParentItem() is not { } parent || !grantedUids.Contains(parent.uniqueId)))
                Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(grant);
            R.Check("deviceflows.ras-native-prerequisites", "Both native aquaculture upgrades unlocked through their ordinary prerequisite/Unlock chain", new
                { aquaculture = Store.networkUpgrade["AQUACULTURE"].IsUnlocked(), aquacultureII = Store.networkUpgrade["AQUACULTURE_II"].IsUnlocked(), grantedUids = grantedUids.OrderBy(uid => uid).ToArray() },
                Store.networkUpgrade["AQUACULTURE"].IsUnlocked() && Store.networkUpgrade["AQUACULTURE_II"].IsUnlocked(),
                "Explicit test prerequisite fixture. No credits/achievement/economic acquisition is claimed; native queue and day state are not edited.");
            stage = Spawn("hypertech_bay"); Retain(stage); var stageTabs = DfTabs(stage); var stageGrid = (GameGridInventory)stageTabs.GetTabInventory(0);
            tilapia = DfMakeRas(stageGrid, 1, 1, "tilapia", 100, true);
            eel = DfMakeRas(stageGrid, 1, 16, "electric_eel", 0, false);
            int outline = DfOutlineCells(eel.Machine);
            R.Check("deviceflows.ras-legal-native-fixture", new { tilapiaAge = 3, eelAge = 0, cost = 5, waterPartsEach = 12000000, nativeBorn = true },
                new { tilapia = DfFish(tilapia.Fish), eel = DfFish(eel.Fish), tilapiaPorts = DfRasPorts(tilapia.Machine), eelPorts = DfRasPorts(eel.Machine), outline },
                tilapia.Fish.GetTagReadonly("FISH_AGE_TAG").GetInt() == 3 && eel.Fish.GetTagReadonly("FISH_AGE_TAG").GetInt() == 0 &&
                MachineryHelper.GetMachinePowerUsage(tilapia.Machine) == 5 && MachineryHelper.GetMachinePowerUsage(eel.Machine) == 5 &&
                WaterHelper.GetTotalVolume(tilapia.Water) == 12000000 && WaterHelper.GetTotalVolume(eel.Water) == 12000000 && !FlAdjacent(tilapia.Machine, eel.Machine),
                "Unmodified native factory fish and legal SlotMarker placements in a native hypertech page. Pure water and food are explicit preparation grants; no fish state or product is manufactured by the probe.");
            FlEnsureGrid(Math.Max(GridTotal, 200));
            Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
            int day = StoreStation.GetDayCounter();
            JObject firstTilapia = DfFish(tilapia.Fish), firstEel = DfFish(eel.Fish), power = DfPowerBefore(GridTotal, LogisticsState);
            int tilapiaFeed = DfFeed(tilapia.Feeder), eelFeed = DfFeed(eel.Feeder), tilapiaEnergy = Energy(tilapia.Battery), eelEnergy = Energy(eel.Battery);
            var first = new JObject { ["day"] = day, ["tilapiaBefore"] = firstTilapia, ["eelBefore"] = firstEel,
                ["tilapiaFeedBefore"] = tilapiaFeed, ["eelFeedBefore"] = eelFeed, ["powerBefore"] = power,
                ["tilapiaWaterBefore"] = FlLiquid(tilapia.Water), ["eelWaterBefore"] = FlLiquid(eel.Water) };
            _dfNights.Add(first);
            using (var observation = new NightObservation(this, Array.Empty<RecipeFixture>(), day))
            {
                first["nativeNight"] = observation.Evidence;
                foreach (object? wait in NightDay(day, "deviceflows.ras.night1", observation)) yield return wait;
            }
            DfCheckFed(tilapia, firstTilapia, tilapiaFeed, "deviceflows.ras.night1.tilapia", true);
            DfCheckFed(eel, firstEel, eelFeed, "deviceflows.ras.night1.eel", true);
            DfCheckRasPower(tilapia, eel, power, tilapiaEnergy, eelEnergy, outline, day, "deviceflows.ras.night1");
            first["tilapiaAfter"] = DfFish(tilapia.Fish); first["eelAfter"] = DfFish(eel.Fish);
            first["tilapiaWaterAfter"] = FlLiquid(tilapia.Water); first["eelWaterAfter"] = FlLiquid(eel.Water); first["powerAfter"] = LogisticsState["Power"]?.DeepClone();
            R.Check("deviceflows.ras.first-night-water-and-fixed-vessel-uids", "Native fixed water vessels remain installed and livestock remains healthy", new
                { tilapia = FlItem(tilapia.Water), eel = FlItem(eel.Water), tilapiaWater = first["tilapiaWaterAfter"], eelWater = first["eelWaterAfter"] },
                tilapia.Water.parentInventory == MachineRAS.GetWaterSlot(tilapia.Machine) && eel.Water.parentInventory == MachineRAS.GetWaterSlot(eel.Machine) &&
                WaterHelper.GetTotalVolume(tilapia.Water) > 0 && WaterHelper.GetTotalVolume(eel.Water) > 0 && AquacultureHelper.GetHealth(tilapia.Fish) == 100f && AquacultureHelper.GetHealth(eel.Fish) == 120f,
                "Actual waste/filter changes are retained as all-species liquid evidence, not described as a conserved external liquid transfer or all water-grade coverage.");

            GameItem butcher = tilapia.Butcher!;
            // Only the normal player harvest/keeper settings, exactly the keys
            // written by native UI controls. No LAST_COUNT/product/age edit.
            butcher.ModifyTag("AUTO_BUTCHER_TANK_HARVEST_INT", tag => tag.Enable().SetInt(3));
            butcher.ModifyTag("AUTO_BUTCHER_TANK_AGE_MIN_INT", tag => tag.Enable().SetInt(1));
            butcher.ModifyTag("AUTO_BUTCHER_TANK_KEEP_FEMALES_INT", tag => tag.Enable().SetInt(0));
            butcher.ModifyTag("AUTO_BUTCHER_TANK_KEEP_MALES_INT", tag => tag.Enable().SetInt(0));
            butcher.ModifyTag("AUTO_BUTCHER_TANK_SPARE_EGG_LAYERS_BOOL", tag => tag.Enable().SetBool(false));
            int fishUid = tilapia.Fish.uniqueId;
            double preHarvestKcal = AquacultureHelper.GetKcalEaten(tilapia.Fish);
            int expectedMeat = (int)Math.Floor(preHarvestKcal * 0.9);
            var tray = MachineAutoButcher.GetOutputGrid(butcher);
            int[] outputBefore = tray.childItems.Select(item => item.uniqueId).ToArray();
            day = StoreStation.GetDayCounter(); power = DfPowerBefore(GridTotal, LogisticsState);
            JObject secondEel = DfFish(eel.Fish);
            tilapiaFeed = DfFeed(tilapia.Feeder); eelFeed = DfFeed(eel.Feeder); tilapiaEnergy = Energy(tilapia.Battery); eelEnergy = Energy(eel.Battery);
            var second = new JObject { ["day"] = day, ["harvestConfiguration"] = FlItem(butcher), ["stockUid"] = fishUid,
                ["preHarvestKcal"] = preHarvestKcal, ["expectedMeatKcal"] = expectedMeat, ["eelBefore"] = secondEel,
                ["powerBefore"] = power, ["tilapiaFeedBefore"] = tilapiaFeed, ["eelFeedBefore"] = eelFeed };
            _dfNights.Add(second);
            using (var observation = new NightObservation(this, Array.Empty<RecipeFixture>(), day))
            {
                second["nativeNight"] = observation.Evidence;
                foreach (object? wait in NightDay(day, "deviceflows.ras.night2", observation)) yield return wait;
            }
            DfCheckFed(eel, secondEel, eelFeed, "deviceflows.ras.night2.eel", true);
            DfCheckRasPower(tilapia, eel, power, tilapiaEnergy, eelEnergy, outline, day, "deviceflows.ras.night2");
            GameItem[] products = tray.childItems.Where(item => !outputBefore.Contains(item.uniqueId)).ToArray();
            int meatKcal = products.Sum(item => item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt());
            second["actualProducts"] = new JArray(products.Select(FlItem)); second["eelAfter"] = DfFish(eel.Fish); second["powerAfter"] = LogisticsState["Power"]?.DeepClone();
            R.Check("deviceflows.ras.real-late-cycle-harvest-to-native-tray", new { fishUid, automaticHarvested = 1, expectedMeat, noSecondFeed = tilapiaFeed },
                new { products = second["actualProducts"], meatKcal, feedAfter = DfFeed(tilapia.Feeder), lastCount = butcher.GetTagReadonly("AUTO_BUTCHER_LAST_COUNT_INT").GetInt(),
                    stockUids = MachineRAS.GetTankGrid(tilapia.Machine).childItems.Select(item => item.uniqueId).ToArray() },
                products.Length > 0 && products.All(item => item.parentInventory == tray && GeneralHelper.IsItemOwned(item)) && meatKcal == expectedMeat &&
                DfFeed(tilapia.Feeder) == tilapiaFeed && !Store.FindAllItem().Any(item => item.uniqueId == fishUid) && butcher.GetTagReadonly("AUTO_BUTCHER_LAST_COUNT_INT").GetInt() == 1,
                "Native late-cycle AutoButcher applies its ordinary age/keeper settings after the real RAS cycle. Industrial diet's second-night partial growth does not create another age/feeding opportunity; independent meat expectation is floor(pre-harvest absorbed kcal * native Tilapia conversion .9), with native exact calorie pieces/scrap. No harvest/product/completion method is substituted.");
            if (products.Length == 0) throw new InvalidOperationException("No real harvested product exists for the collection chain");
            foreach (GameItem machine in new[] { tilapia.Machine, eel.Machine, butcher })
            {
                Reflect.Call(Logistics, "ConfigureMachine", machine, true, true);
                JObject contents = DfTree(machine);
                Reflect.Call(Logistics, "FillOnce", machine); Reflect.Call(Logistics, "EjectOnce", machine);
                R.Check("deviceflows.ras-populated-unsupported-fill-eject." + machine.uniqueId, contents, DfTree(machine), JToken.DeepEquals(contents, DfTree(machine)),
                    "RAS/AutoButcher currently lack a logistics material adapter. These are actual calls against populated, natively active devices, and unchanged contents must be observed. Native hopper collection below is a separate supported mechanism.");
                Reflect.Call(Logistics, "ConfigureMachine", machine, false, false);
            }
            var resources = (GameGridInventory)Reflect.Call(Logistics, "ResourceRegion")!;
            intake = Spawn("intake_hopper"); outlet = Spawn("outlet_hopper");
            intake.ModifyTag(HopperHelper.ON_MOVE_BOOL, tag => tag.Enable().SetBool(false));
            intake.ModifyTag(HopperHelper.ON_ADD_BOOL, tag => tag.Enable().SetBool(false));
            Place(stageGrid, intake, "deviceflows-ras-collection-staging"); Place(resources, outlet, "deviceflows-ras-collection-outlet");
            HopperHelper.Link(intake, outlet); HopperHelper.SetFilterList(intake, products.Select(item => item.identifier).Distinct().ToList());
            intake.ModifyTag(HopperHelper.FILTER_MODE_INT, tag => tag.Enable().SetInt(HopperHelper.FILTER_ALLOW));
            intake.ModifyTag(HopperHelper.KEEP_INT, tag => tag.Enable().SetInt(0));
            string[] productIds = products.Select(item => item.identifier).Distinct().ToArray();
            JArray invoice = FlInvoice(productIds), physical = new(products.Select(FlPhysical));
            intake.ModifyTag(HopperHelper.ON_MOVE_BOOL, tag => tag.Enable().SetBool(true));
            Place(tray, intake, "deviceflows-ras-real-OnMove-collector");
            float until = Time.realtimeSinceStartup + 5f;
            while (products.Any(item => item.parentInventory != resources) && Time.realtimeSinceStartup < until) yield return null;
            R.Check("deviceflows.ras-real-harvest-hopper-ejection-to-resources", new { invoice, physical, outputEmptyExceptIntake = true },
                new { invoice = FlInvoice(productIds), products = products.Select(FlItem).ToArray(), tray = tray.childItems.Select(FlItem).ToArray() },
                products.All(item => item.parentInventory == resources) && tray.childItems.All(item => item == intake) &&
                JToken.DeepEquals(invoice, FlInvoice(productIds)) && JToken.DeepEquals(physical, new JArray(products.Select(FlPhysical))),
                "Actual intake placement triggers native OnMove/Schedule/RunPending after genuine EndNight harvest. Same product UID/count/value/tags and complete global invoice reach resource storage; no factory output is substituted and unsupported logistics EjectOnce is not called a successful ejection.");
            second["collectedProducts"] = new JArray(products.Select(FlItem));
            foreach (object? wait in CaptureEvidenceFrame("deviceflows-RAS-native-night-harvest-and-collection")) yield return wait;
            DfKeep(stage); DfKeep(tilapia.Machine); DfKeep(eel.Machine); DfKeep(outlet);
            foreach (GameItem item in products) DfKeep(item);
            bool workPassed = !R.checks.Skip(firstCheck).Any(check => check.status == "failed");
            foreach (var activeRow in _dfDevices.OfType<JObject>().Where(deviceRow => (string?)deviceRow["id"] is "ras" or "livestock_feed_dispenser" or "auto_butcher"))
                activeRow["activeWork"] = workPassed ? "actual-two-night-RAS-feed-growth-harvest-and-hopper-collection" : "failed-two-night-RAS-chain";
            kept = true;
        }
        finally
        {
            FlCleanup("deviceflows.ras",
                () => { if (tilapia != null) Reflect.Call(Logistics, "ConfigureMachine", tilapia.Machine, false, false); },
                () => { if (eel != null) Reflect.Call(Logistics, "ConfigureMachine", eel.Machine, false, false); },
                () => { if (!kept) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(stage); },
                () => { if (!kept) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(outlet); },
                () => { if (!kept && intake?.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(intake); });
        }
    }
}
