using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private readonly HashSet<int> _ndExpected = new();
    private readonly Dictionary<int, (string Id, int Count)> _ndIdentity = new();
    private readonly JArray _ndCreated = new(), _ndRetired = new(), _ndOperations = new(), _ndPreparation = new();
    private GameItem? _ndArchive;

    internal IEnumerable<object?> NativeDna()
    {
        R.phase = "nativedna-real-native-operations";
        R.environment["nativeDnaScope"] = "Ordinary APP; true factories/SlotMarkers/native installed onCycle callbacks. NativeCycle is not a full EndNight. All starting seeds, blank disks, modules and energy are explicit factory test grants, not economic acquisition. No WriteDisk/ApplyTrait/FinishOperation/RNG/genotype/progress/success field is invoked or assigned by the probe. Written disks come only from actual native completed extraction. Keep/eject uses owned/installed + MayToggleSlotItem/CanToggleSlotItem then public ToggleSlotItem API, not a physical mouse or visible context-menu test.";
        R.environment["nativeDnaCreated"] = _ndCreated; R.environment["nativeDnaRetirements"] = _ndRetired;
        R.environment["nativeDnaOperations"] = _ndOperations;
        R.environment["nativeDnaPreparation"] = _ndPreparation;
        _ndArchive = NdGrant("hypertech_bay", false); Retain(_ndArchive);
        GameItem machine = NdMachine(true), disk = NdGrant("plant_dna_disk"), firstSeed = NdGrant("fillerweed_seed");
        R.Check("nativedna-factory-disk-is-blank", "Factory creates only a blank disk", NdDisk(disk), PlantGeneticsHelper.IsBlankDisk(disk));
        NdPlace(MachineDnaManipulator.GetOutputDiskSlot(machine), disk, "blank-output-disk");
        NdNoop(machine, "missing-seed");
        NdArchive(disk);
        NdPlace(MachineDnaManipulator.GetSeedSlot(machine), firstSeed, "initial-native-seed");
        NdNoop(machine, "missing-disk");
        NdPlace(MachineDnaManipulator.GetOutputDiskSlot(machine), disk, "restore-blank-disk");
        var powerSlot = (GameSlotInventory)MachineHelper.GetBodyStorage(machine, 0);
        GameItem battery = powerSlot.childItem;
        NdArchive(battery);
        R.Check("nativedna-no-power-prerequisite", new { physicalBatteryAbsent = true, sharedGrid = 0 },
            new { physicalBatteryAbsent = powerSlot.childItem == null, sharedGrid = GridTotal }, powerSlot.childItem == null && GridTotal == 0,
            "This family grants no network upgrades. A genuine absent native battery and empty shared grid establish the refusal; no power ledger is edited.");
        NdNoop(machine, "no-power");
        NdPlace(powerSlot, battery, "restore-real-battery");
        NdSetEject(machine, false);

        bool extracted = false;
        for (int attempt = 0; attempt < 8 && !extracted; attempt++)
        {
            GameItem seed = attempt == 0 ? firstSeed : NdGrant("fillerweed_seed");
            if (MachineDnaManipulator.GetSeedSlot(machine).childItem is { } old && old != seed) NdArchive(old);
            if (seed.parentInventory != MachineDnaManipulator.GetSeedSlot(machine)) NdPlace(MachineDnaManipulator.GetSeedSlot(machine), seed, "extraction-seed-" + attempt);
            JObject before = NdGenes(seed); int heat = PlantGeneticsHelper.GetHeat(seed);
            var receipt = new JObject { ["stage"] = "blank-extraction", ["attempt"] = attempt, ["seedBefore"] = NdItem(seed), ["diskBefore"] = NdDisk(disk) };
            foreach (object? wait in NdOperate(machine, "extract", receipt)) yield return wait;
            extracted = NdExtraction(machine, seed, disk, before, heat, null, 0, receipt, true);
            R.Check("nativedna-keep-setting-retains-working-disk." + attempt, new { diskUid = disk.uniqueId, outputSlot = true }, NdDisk(disk),
                disk.parentInventory == MachineDnaManipulator.GetOutputDiskSlot(machine) && MachineDnaManipulator.GetEjectGrid(machine).childItems.Count == 0,
                "Actual native onToggleSlotItemFunc selected keep; actual FinishOperation must leave this same disk in its native output slot.");
        }
        R.Check("nativedna-real-written-extraction-required", "A genuinely extracted written disk within eight ordinary RNG attempts", NdDisk(disk), extracted && PlantGeneticsHelper.IsWrittenDisk(disk));
        if (!extracted) throw new InvalidOperationException("Eight native extraction attempts produced no written disk; later apply/stack cases were not reached and no factory-written replacement is allowed");
        PlantTrait trait = PlantGeneticsHelper.GetDiskTrait(disk);
        string cropId = HydroponicHelper.GetPlantId(firstSeed);
        if (MachineDnaManipulator.GetSeedSlot(machine).childItem is { } initialDonor) NdArchive(initialDonor);

        GameItem incompatible = NdGrant("nutrifruit_seed");
        JToken incompatibleBefore = NdItem(incompatible), diskBeforeReject = NdItem(disk); int energyBeforeReject = Energy(powerSlot.childItem);
        string reason;
        bool nativeCompatible = PlantGeneticsHelper.CanExtractOntoDisk(incompatible, disk, out reason);
        var rejectedSlot = MachineDnaManipulator.GetSeedSlot(machine).TryFindOneValidInventorySlot(incompatible);
        int accepted = rejectedSlot != null && rejectedSlot.IsValid() ? rejectedSlot.TryAcceptOnce() : 0;
        R.Check("nativedna-written-disk-refill-native-incompatible-seed-refusal", "The real written negative-trait disk refuses a native crop lacking that trait", new
            { nativeCompatible, reason, accepted, candidate = NdItem(incompatible), disk = NdItem(disk) },
            !nativeCompatible && reason.Length > 0 && accepted == 0 && JToken.DeepEquals(incompatibleBefore, NdItem(incompatible)) &&
                JToken.DeepEquals(diskBeforeReject, NdItem(disk)) && Energy(powerSlot.childItem) == energyBeforeReject,
            "Native CanExtractOntoDisk and actual seed SlotMarker admission are observed without replacing whitelist/parent callbacks. This is written-disk refill incompatibility, not a claim that all cross-species applications are forbidden.");
        if (accepted != 0) throw new InvalidOperationException("Expected native incompatibility fixture was admitted");

        bool stacked = false;
        for (int attempt = 0; attempt < 8 && !stacked; attempt++)
        {
            if (MachineDnaManipulator.GetSeedSlot(machine).childItem is { } old) NdArchive(old);
            GameItem donor = NdGrant("fillerweed_seed"); NdPlace(MachineDnaManipulator.GetSeedSlot(machine), donor, "written-disk-refill-seed");
            JObject genes = NdGenes(donor); int heat = PlantGeneticsHelper.GetHeat(donor), uses = PlantGeneticsHelper.GetDiskUses(disk);
            var receipt = new JObject { ["stage"] = "written-disk-refill", ["attempt"] = attempt, ["seedBefore"] = NdItem(donor), ["diskBefore"] = NdDisk(disk) };
            foreach (object? wait in NdOperate(machine, "extract", receipt)) yield return wait;
            stacked = NdExtraction(machine, donor, disk, genes, heat, trait, uses, receipt, true);
            R.Check("nativedna-written-extraction-stacking-same-disk." + attempt, new { uid = disk.uniqueId, trait = trait.id, sameSlot = true }, NdDisk(disk),
                PlantGeneticsHelper.GetDiskTrait(disk)?.id == trait.id && PlantGeneticsHelper.GetDiskRank(disk) == 1 && disk.parentInventory == MachineDnaManipulator.GetOutputDiskSlot(machine));
        }
        R.Check("nativedna-written-disk-refill-success-required", "Native extraction increases uses of the same genuinely written disk", NdDisk(disk), stacked);
        if (!stacked) throw new InvalidOperationException("Bounded native written-disk refill never succeeded");

        NdSetEject(machine, true);
        int successes = 0, applyAttempts = 0;
        while (_ndExpected.Contains(disk.uniqueId) && applyAttempts < 24)
        {
            if (MachineDnaManipulator.GetSeedSlot(machine).childItem is { } old) NdArchive(old);
            GameItem target = NdGrant("fillerweed_seed");
            NdPlace(MachineDnaManipulator.GetSeedSlot(machine), target, "same-crop-apply-target");
            NdPlace(MachineDnaManipulator.GetInputDiskSlot(machine), disk, "real-written-input-disk");
            JObject genes = NdGenes(target); int heat = PlantGeneticsHelper.GetHeat(target), uses = PlantGeneticsHelper.GetDiskUses(disk), rank = PlantGeneticsHelper.GetDiskRank(disk);
            int beforeRank = (int?)genes[trait.id] ?? 0;
            var receipt = new JObject { ["stage"] = "same-species-apply", ["attempt"] = applyAttempts++, ["targetBefore"] = NdItem(target),
                ["diskBefore"] = NdDisk(disk), ["diskUsesBefore"] = uses, ["sameCrop"] = HydroponicHelper.GetPlantId(target) == cropId };
            foreach (object? wait in NdOperate(machine, "apply", receipt)) yield return wait;
            int actualRank = PlantGeneticsHelper.GetTraitRank(target, trait.id), expectedRank = trait.IsUnlimited ? beforeRank + rank : Math.Min(trait.maxRank, beforeRank + rank);
            bool success = actualRank == expectedRank;
            JObject expectedGenes = (JObject)genes.DeepClone(); if (success) expectedGenes[trait.id] = expectedRank;
            bool liveDisk = Store.FindAllItem(false).Any(item => item.uniqueId == disk.uniqueId);
            bool diskExpected = success && uses == 1 ? !liveDisk && MachineDnaManipulator.GetInputDiskSlot(machine).childItem == null
                : liveDisk && PlantGeneticsHelper.GetDiskUses(disk) == uses - (success ? 1 : 0) && disk.parentInventory == MachineDnaManipulator.GetEjectGrid(machine);
            bool valid = (success || actualRank == beforeRank) && JToken.DeepEquals(expectedGenes, NdGenes(target)) &&
                PlantGeneticsHelper.GetHeat(target) == heat + 2 + Math.Abs(trait.complexity) && diskExpected && HydroponicHelper.GetPlantId(target) == cropId &&
                (success || liveDisk && JToken.DeepEquals(receipt["diskBefore"]?["rawDnaPayload"], NdDiskPayload(disk)));
            receipt["successObserved"] = success; receipt["targetAfter"] = NdItem(target); receipt["diskStillLive"] = liveDisk;
            R.Check("nativedna-actual-same-species-gene-application-and-disk-use." + applyAttempts,
                new { expectedGenes, expectedHeat = heat + 2 + Math.Abs(trait.complexity), oneUseOnSuccess = true, originalTargetUid = target.uniqueId }, receipt, valid,
                "Only the selected trait can gain the actual disk rank; every other registered gene remains identical. Native success spends exactly one use, failure spends none, and each attempt gains the published heat. Native plan/cycles run the real RNG; no ApplyTrait/SpendDiskUse call substitutes for it.");
            if (!valid) throw new InvalidOperationException("Native DNA application violated its independent genotype/use/UID ledger");
            if (success) successes++;
            NdArchive(target);
            if (!liveDisk) NdRetire(disk, "Native FinishApply spent the final original disk use after an independently observed gene increase", receipt, success && uses == 1);
        }
        R.Check("nativedna-application-success-and-final-disk-consumption-required", "At least one true same-species application and actual final-use consumption in at most 24 attempts",
            new { successes, applyAttempts, diskStillPromised = _ndExpected.Contains(disk.uniqueId) }, successes > 0 && !_ndExpected.Contains(disk.uniqueId));
        if (_ndExpected.Contains(disk.uniqueId)) throw new InvalidOperationException("Bounded native application did not consume the real disk; no missing UID may be silently removed");

        // A second native extraction explicitly exercises legitimate input
        // destruction and leaves a real written disk as the reload witness.
        GameItem destructive = NdMachine(false); GameItem? witnessDisk = null;
        for (int attempt = 0; attempt < 8 && witnessDisk == null; attempt++)
        {
            GameItem consumedSeed = NdGrant("fillerweed_seed"), candidateDisk = NdGrant("plant_dna_disk");
            NdPlace(MachineDnaManipulator.GetSeedSlot(destructive), consumedSeed, "non-stabilized-real-input");
            NdPlace(MachineDnaManipulator.GetOutputDiskSlot(destructive), candidateDisk, "non-stabilized-blank-output");
            JObject beforeGenes = NdGenes(consumedSeed); int beforeHeat = PlantGeneticsHelper.GetHeat(consumedSeed);
            var destructiveReceipt = new JObject { ["stage"] = "non-stabilized-extraction", ["attempt"] = attempt,
                ["seedBefore"] = NdItem(consumedSeed), ["diskBefore"] = NdDisk(candidateDisk) };
            foreach (object? wait in NdOperate(destructive, "extract", destructiveReceipt)) yield return wait;
            bool written = NdExtraction(destructive, consumedSeed, candidateDisk, beforeGenes, beforeHeat, null, 0, destructiveReceipt, false);
            NdRetire(consumedSeed, "Native FinishExtract without a Stabilizer destroys the original seed after completed native extraction, on either actual RNG outcome", destructiveReceipt,
                MachineDnaManipulator.GetSeedSlot(destructive).childItem == null);
            if (written) witnessDisk = candidateDisk;
            else NdArchive(candidateDisk);
        }
        R.Check("nativedna-actual-non-stabilized-written-witness", "A native completed extraction produces the written reload witness within eight ordinary RNG attempts", witnessDisk == null ? null : NdDisk(witnessDisk), witnessDisk != null);
        if (witnessDisk == null) throw new InvalidOperationException("Eight actual destructive extractions produced no written witness; no written factory replacement is permitted");
        R.Check("nativedna-default-eject-configuration-produces-real-eject-grid-disk", witnessDisk.uniqueId, NdDisk(witnessDisk),
            MachineDnaManipulator.IsEjectEnabled(destructive) && witnessDisk.parentInventory == MachineDnaManipulator.GetEjectGrid(destructive));
        NdArchive(witnessDisk);
        var branches = new JArray(); R.environment["nativeDnaObservedRngBranches"] = branches;
        foreach (string stage in new[] { "blank-extraction", "written-disk-refill", "same-species-apply", "non-stabilized-extraction" })
        {
            JObject[] attempts = _ndOperations.OfType<JObject>().Where(row => (string?)row["stage"] == stage).ToArray();
            int successesSeen = attempts.Count(row => (bool?)row["successObserved"] == true), failuresSeen = attempts.Count(row => (bool?)row["successObserved"] == false);
            branches.Add(new JObject { ["stage"] = stage, ["attempts"] = attempts.Length, ["successes"] = successesSeen, ["failures"] = failuresSeen });
            if (failuresSeen == 0) R.Skip("nativedna-rng-failure-not-observed." + stage, "This fixed native fixture has independently checked 99% success. No completed failure outcome occurred in this stage's actual bounded attempts; that failure branch is not-tested, not inferred covered.", false);
        }
        R.Skip("nativedna-remaining-variants", "Representative real negative-trait extraction/refill/same-crop apply, keep/eject, missing input/power, incompatible refill and final-use retirement are above. Every trait/crop/filter/precision combination, injected callback failure and other quality boundaries remain not-tested. Actual 99% RNG failure coverage is reported separately per stage. NativeCycle is not complete EndNight.", false);
        foreach (object? wait in CaptureEvidenceFrame("nativedna-native-extraction-application-and-written-witness")) yield return wait;
        R.persistence["nativeDna"] = new JObject { ["uids"] = new JArray(_ndExpected.OrderBy(uid => uid)), ["identities"] = NdIdentitySnapshot(), ["items"] = NdSnapshot("before-save"),
            ["retired"] = _ndRetired.DeepClone(), ["archivePages"] = NdArchiveSnapshot(), ["writtenWitnessUid"] = witnessDisk.uniqueId,
            ["scope"] = "Real native cycles, not full night; explicit factory economy fixtures" };
    }

    internal IEnumerable<object?> NativeDnaReload(JObject baseline)
    {
        R.phase = "nativedna-new-process-real-load";
        var expected = baseline["persistence"]?["nativeDna"] as JObject ?? throw new InvalidOperationException("Native DNA persistence is missing");
        int[] uids = ((JArray)expected["uids"]!).Values<int>().ToArray();
        int[] itemUids = ((JArray)expected["items"]!).Select(item => (int)item["uid"]!).ToArray();
        var identities = expected["identities"] as JArray ?? throw new InvalidOperationException("Original DNA UID identity map is missing");
        foreach (JObject row in identities.OfType<JObject>())
        {
            int uid = (int?)row["uid"] ?? 0, count = (int?)row["count"] ?? 0; string id = (string?)row["id"] ?? "";
            if (uid <= 0 || count <= 0 || id.Length == 0 || _ndIdentity.ContainsKey(uid)) throw new InvalidOperationException("Invalid original DNA identity map");
            _ndIdentity.Add(uid, (id, count));
        }
        int[] retiredUids = ((JArray)expected["retired"]!).Select(item => (int)item["uid"]!).ToArray();
        bool complete = uids.Length > 0 && uids.All(uid => uid > 0) && uids.Distinct().Count() == uids.Length && uids.OrderBy(uid => uid).SequenceEqual(itemUids.OrderBy(uid => uid)) &&
            identities.Count == _ndIdentity.Count && retiredUids.Distinct().Count() == retiredUids.Length && !uids.Intersect(retiredUids).Any() &&
            uids.Concat(retiredUids).OrderBy(uid => uid).SequenceEqual(_ndIdentity.Keys.OrderBy(uid => uid));
        R.Check("nativedna-reload-complete-promised-denominator", "Positive unique expected UIDs match every stored item", new { uids, itemUids }, complete);
        if (!complete) throw new InvalidOperationException("Native DNA baseline silently reduced or duplicated its promised denominator");
        foreach (int uid in uids) _ndExpected.Add(uid);
        foreach (JObject row in ((JArray)expected["retired"]!).OfType<JObject>()) _ndRetired.Add(row.DeepClone());
        _ndArchive = Store.FindAllItem(false).Single(item => item.uniqueId == (int)expected["archivePages"]!["uid"]!);
        JArray actual = NdSnapshot("reload");
        R.Check("nativedna-reload-all-uids-genes-disk-uses-power-and-native-tags", expected["items"], actual,
            JToken.DeepEquals(FlReloadProjection(expected["items"]!), FlReloadProjection(actual)),
            "Only native all-default disabled tag omission is canonicalized by the frozen readback helper; no temperature or gene/use/value payload is ignored. Common Reload already verifies fresh process, same candidates and true ES3 LoadGame.");
        JObject pages = NdArchiveSnapshot();
        R.Check("nativedna-reload-archive-pages", expected["archivePages"], pages, JToken.DeepEquals(expected["archivePages"], pages));
        foreach (JObject retired in ((JArray)expected["retired"]!).OfType<JObject>())
        {
            int uid = (int)retired["uid"]!; int count = Store.FindAllItem(false).Count(item => item.uniqueId == uid);
            R.Check("nativedna-reload-native-consumption-receipt." + uid, new { absent = true, receipt = retired }, new { count }, count == 0 && !_ndExpected.Contains(uid));
        }
        GameItem witness = Store.FindAllItem(false).Single(item => item.uniqueId == (int)expected["writtenWitnessUid"]!);
        R.Check("nativedna-reload-actual-written-extraction-witness", "The same genuinely extracted disk remains written", NdDisk(witness), PlantGeneticsHelper.IsWrittenDisk(witness));
        R.persistence["nativeDna"] = expected.DeepClone();
        yield return null;
    }

    private GameItem NdGrant(string id, bool archive = true)
    {
        GameItem item = Spawn(id); NdCommit(item, "native factory preparation grant");
        if (archive) NdArchive(item);
        return item;
    }
    private void NdCommit(GameItem item, string origin)
    {
        foreach (var member in new[] { item }.Concat(item.FindAllChildItems()).Distinct())
        {
            if (!_ndExpected.Add(member.uniqueId)) throw new InvalidOperationException("Duplicate native DNA UID promise " + member.uniqueId);
            _ndIdentity.Add(member.uniqueId, (member.identifier, member.unitCount));
            _ndCreated.Add(new JObject { ["uid"] = member.uniqueId, ["id"] = member.identifier, ["origin"] = origin, ["atCommit"] = FlItem(member) });
        }
    }
    private void NdRetire(GameItem item, string reason, JObject receipt, bool nativeOutcome)
    {
        int uid = item.uniqueId, matches = Store.FindAllItem(false).Count(candidate => candidate.uniqueId == uid);
        bool proven = _ndExpected.Contains(uid) && matches == 0 && nativeOutcome && (bool?)receipt["completed"] == true && (int?)receipt["cycles"] > 0;
        R.Check("nativedna-explicit-native-consumption-retirement." + uid, "Promised UID absent only after independently verified completed native consumption", new { uid, reason, matches, receipt }, proven);
        if (!proven) throw new InvalidOperationException("Native consumption receipt is insufficient to retire UID " + uid);
        _ndExpected.Remove(uid);
        _ndRetired.Add(new JObject { ["uid"] = uid, ["id"] = _ndIdentity[uid].Id, ["count"] = _ndIdentity[uid].Count, ["reason"] = reason, ["nativeReceipt"] = receipt.DeepClone() });
    }
    private JArray NdIdentitySnapshot()
    {
        int[] retired = _ndRetired.OfType<JObject>().Select(row => (int)row["uid"]!).ToArray();
        bool complete = retired.Distinct().Count() == retired.Length && !_ndExpected.Intersect(retired).Any() &&
            _ndExpected.Concat(retired).OrderBy(uid => uid).SequenceEqual(_ndIdentity.Keys.OrderBy(uid => uid));
        R.Check("nativedna-original-identity-denominator", "Every original UID remains either live-promised or explicitly retired exactly once", new
            { promised = _ndExpected.OrderBy(uid => uid).ToArray(), retired, original = _ndIdentity.Keys.OrderBy(uid => uid).ToArray() }, complete);
        if (!complete) throw new InvalidOperationException("Original DNA UID denominator changed without a native consumption receipt");
        return new JArray(_ndIdentity.OrderBy(pair => pair.Key).Select(pair => new JObject { ["uid"] = pair.Key, ["id"] = pair.Value.Id, ["count"] = pair.Value.Count }));
    }
    private static JObject NdGenes(GameItem seed) => new(PlantTraitRegistry.All.OrderBy(trait => trait.id, StringComparer.Ordinal)
        .Select(trait => new JProperty(trait.id, PlantGeneticsHelper.GetTraitRank(seed, trait.id))));
    private static JObject NdDiskPayload(GameItem disk)
    {
        var result = new JObject();
        foreach (string key in new[] { "DNA_DISK_TRAIT_STRING", "DNA_DISK_RANK_INT", "DNA_DISK_USES_INT" })
            result[key] = disk.state.dict.TryGetValue(key, out var value) ? JObject.FromObject(new { value.valueEnabled, value.valueInt, value.valueLong, value.valueFloat, value.valueDouble, value.valueBool, value.valueString }) : JValue.CreateNull();
        return result;
    }
    private static JObject NdDisk(GameItem disk) => new() { ["item"] = FlItem(disk), ["written"] = PlantGeneticsHelper.IsWrittenDisk(disk), ["rawDnaPayload"] = NdDiskPayload(disk),
        ["trait"] = PlantGeneticsHelper.GetDiskTrait(disk)?.id, ["rank"] = PlantGeneticsHelper.GetDiskRank(disk), ["uses"] = PlantGeneticsHelper.GetDiskUses(disk) };
    private static JObject NdItem(GameItem item)
    {
        var state = FlItem(item);
        if (item.IsTag("SEED_ITEM_TAG")) { state["genes"] = NdGenes(item); state["heat"] = PlantGeneticsHelper.GetHeat(item); state["crop"] = HydroponicHelper.GetPlantId(item); }
        if (PlantGeneticsHelper.IsDisk(item)) { state["diskTrait"] = PlantGeneticsHelper.GetDiskTrait(item)?.id; state["diskRank"] = PlantGeneticsHelper.GetDiskRank(item); state["diskUses"] = PlantGeneticsHelper.GetDiskUses(item); }
        if (item.identifier == "plant_dna_manipulator") state["moduleBay"] = NdModuleBayShape((GameGridInventory)MachineHelper.GetBodyStorage(item, 1));
        return state;
    }
    private static MachineryTabs NdTabs(GameItem archive) => ((MachineryPanelRow)archive.contentWindow.childElement).Panels.Select(panel => panel.content).OfType<MachineryTabs>().Single();
    private JObject NdArchiveSnapshot()
    {
        var tabs = NdTabs(_ndArchive!);
        return new JObject { ["uid"] = _ndArchive!.uniqueId, ["pages"] = new JArray(Enumerable.Range(0, tabs.count).Select(index => new JObject
            { ["index"] = index, ["uids"] = new JArray(tabs.GetTabInventory(index).childItems.Select(item => item.uniqueId).OrderBy(uid => uid)) })) };
    }
    private void NdArchive(GameItem item)
    {
        if (_ndArchive == null) throw new InvalidOperationException("Native DNA archive is not initialized");
        if (item.parentInventory?.GetParentItem() == _ndArchive) return;
        var tabs = NdTabs(_ndArchive); JToken before = FlPhysical(item); bool accepted = false;
        for (int page = 0; page < tabs.count && !accepted; page++)
        {
            var grid = (GameGridInventory)tabs.GetTabInventory(page);
            for (int y = 0; y < grid.inventoryShape.height && !accepted; y++)
            for (int x = 0; x < grid.inventoryShape.width && !accepted; x++) accepted = FlAt(grid, item, x, y);
        }
        R.Check("nativedna-native-empty-slot-archive." + item.uniqueId, before, NdItem(item), accepted && item.parentInventory?.GetParentItem() == _ndArchive && JToken.DeepEquals(before, FlPhysical(item)),
            "Real empty SlotMarker preserves the original UID/unit/value/tags, without merge, parent forcing or whitelist replacement.");
        if (!accepted) throw new InvalidOperationException("Native DNA archive admission failed");
    }
    private void NdPlace(GameInventory destination, GameItem item, string label) => Place(destination, item, "nativedna-" + label);
    private GameItem NdMachine(bool stabilizer)
    {
        GameItem machine = NdGrant("plant_dna_manipulator", false); Retain(machine);
        var modules = (GameGridInventory)MachineHelper.GetBodyStorage(machine, 1);
        NdExpandModuleBay(machine, modules);
        foreach (string id in (stabilizer ? new[] { "dna_manipulator_module_stabilizer", "dna_manipulator_module_negative_filter", "system_module_quality" }
            : new[] { "dna_manipulator_module_negative_filter", "system_module_quality" })) NdPlace(modules, NdGrant(id), "legal-module-" + id);
        NdFreshBattery(machine);
        R.Check("nativedna-native-machine-modules." + machine.uniqueId, stabilizer, MachineDnaManipulator.HasStabilizer(machine), MachineDnaManipulator.HasStabilizer(machine) == stabilizer);
        return machine;
    }
    private static JObject NdModuleBayShape(GameGridInventory modules)
    {
        var shape = modules.inventoryShape;
        return new JObject { ["width"] = shape.width, ["height"] = shape.height,
            ["cells"] = new string(Enumerable.Range(0, shape.width * shape.height).Select(index => (char)('0' + shape.GetLocal(index % shape.width, index / shape.width))).ToArray()) };
    }
    private void NdExpandModuleBay(GameItem machine, GameGridInventory modules)
    {
        GameItem kit = NdGrant("module_bay_expansion_kit");
        int uid = kit.uniqueId, stage = machine.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt(), cash = Store.playerCash;
        long value = machine.unitValue; JToken kitBefore = FlPhysical(kit);
        bool may = kit.MayTarget(machine), can = kit.CanTarget(machine), tinkerer = StartingPerk.IsTinkerer();
        var receipt = new JObject { ["action"] = "GameItem.Target", ["kitUid"] = uid, ["machineUid"] = machine.uniqueId,
            ["kitBefore"] = FlItem(kit), ["machineBefore"] = NdItem(machine), ["stageBefore"] = stage, ["cashBefore"] = cash,
            ["nativeMayTarget"] = may, ["nativeCanTarget"] = can, ["tinkerer"] = tinkerer,
            ["provenance"] = "Explicit native expansion-kit preparation grant. Public native MayTarget/CanTarget then Target; not mouse input, economic/achievement acquisition, direct ExpandModule or a shape/stage assignment." };
        _ndPreparation.Add(receipt);
        bool admitted = stage == 0 && modules.childItems.Count == 0 && may && can && GeneralHelper.IsItemOwned(machine) && GeneralHelper.IsItemOwned(kit) &&
            kit.identifier == "module_bay_expansion_kit" && kit.unitCount == 1 && _ndExpected.Contains(uid);
        R.Check("nativedna-real-module-expansion-target-admission." + machine.uniqueId,
            "Empty original native stage0 module bay, owned original kit and full public MayTarget/CanTarget admission", receipt, admitted);
        if (!admitted) throw new InvalidOperationException("Native module expansion fixture was not admitted");
        kit.Target(machine);
        GameItem[] remaining = Store.FindAllItem(false).Where(item => item.uniqueId == uid).ToArray();
        JObject afterShape = NdModuleBayShape(modules);
        bool consumed = remaining.Length == 0;
        bool retainedByNativePerk = tinkerer && remaining.Length == 1 && ReferenceEquals(remaining[0], kit) && GeneralHelper.IsItemOwned(kit) &&
            JToken.DeepEquals(kitBefore, FlPhysical(kit));
        bool upgraded = machine.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt() == 1 && machine.unitValue == value + 100 && Store.playerCash == cash &&
            (int?)afterShape["width"] == 7 && (int?)afterShape["height"] == 5 && (string?)afterShape["cells"] == "10000010000000000000000000001000001" &&
            (consumed || retainedByNativePerk);
        receipt["machineAfter"] = NdItem(machine); receipt["kitLiveMatches"] = remaining.Length; receipt["kitConsumed"] = consumed;
        receipt["retainedByNativeTinkerer"] = retainedByNativePerk; receipt["cashAfter"] = Store.playerCash; receipt["verified"] = upgraded;
        R.Check("nativedna-real-module-expansion-geometry-and-original-material." + machine.uniqueId,
            new { stage = 1, value = value + 100, cash, nativeMask = "10000010000000000000000000001000001", kitConsumptionOrNativeTinkererRetention = true }, receipt, upgraded,
            "Real native Target consumes the original kit except its unmodified Tinkerer keep-material branch. The original 7x5 center obstruction is removed by the native upgrade; later real module SlotMarkers must still admit each original module.");
        if (!upgraded) throw new InvalidOperationException("Native module-bay expansion did not satisfy its geometry/value/material ledger");
        if (consumed)
        {
            bool retirement = _ndExpected.Contains(uid) && _ndIdentity.TryGetValue(uid, out var identity) && identity.Id == "module_bay_expansion_kit" && identity.Count == 1;
            R.Check("nativedna-explicit-native-expansion-kit-retirement." + uid,
                "An originally committed kit UID disappears only after the independently verified native public target upgrade", receipt, retirement);
            if (!retirement) throw new InvalidOperationException("Native expansion receipt cannot retire an unpromised or mutated kit");
            _ndExpected.Remove(uid);
            _ndRetired.Add(new JObject { ["uid"] = uid, ["id"] = _ndIdentity[uid].Id, ["count"] = _ndIdentity[uid].Count,
                ["reason"] = "Original native module_bay_expansion_kit consumed by public GameItem.Target after exact stage0->1, mask, +100 value and unchanged cash proof", ["nativeReceipt"] = receipt.DeepClone() });
        }
    }
    private void NdFreshBattery(GameItem machine, int requiredEnergy = 100)
    {
        var slot = (GameSlotInventory)MachineHelper.GetBodyStorage(machine, 0);
        if (slot.childItem is { } old) NdArchive(old);
        GameItem battery = requiredEnergy <= 100 ? PreBuiltItemHelper.EnergyCredit(100) : PreBuiltItemHelper.EnergyCreditExt(Math.Max(250, requiredEnergy));
        NdCommit(battery, "Native PreBuiltItemHelper energy preparation grant, not grid ledger assignment");
        NdPlace(slot, battery, "real-funded-battery");
        R.Check("nativedna-real-complete-operation-power-budget." + _ndOperations.Count, new { requiredEnergy }, NdItem(battery), Energy(battery) >= requiredEnergy,
            "A real native battery with sufficient actual capacity is placed before work. Old battery UID and remaining energy stay in the archive; no energy tag or shared balance is assigned.");
        if (Energy(battery) < requiredEnergy) throw new InvalidOperationException("Actual native battery capacity cannot fund the declared whole DNA operation");
    }
    private void NdSetEject(GameItem machine, bool enabled)
    {
        bool owned = GeneralHelper.IsItemOwned(machine), installed = machine.parentInventory != null;
        bool may = installed && machine.MayToggleSlotItem(), can = installed && machine.CanToggleSlotItem();
        R.Check("nativedna-normal-toggle-admission." + _ndOperations.Count, "Owned installed machine passes native MayToggleSlotItem and CanToggleSlotItem", new { owned, installed, may, can }, owned && installed && may && can);
        if (!owned || !installed || !may || !can) throw new InvalidOperationException("Native keep/eject toggle admission rejected this fixture");
        if (MachineDnaManipulator.IsEjectEnabled(machine) != enabled)
        {
            machine.ToggleSlotItem();
        }
        R.Check("nativedna-normal-ui-keep-eject-toggle." + _ndOperations.Count, enabled, MachineDnaManipulator.IsEjectEnabled(machine), MachineDnaManipulator.IsEjectEnabled(machine) == enabled,
            "Programmatic public native ToggleSlotItem API after May/Can checks; dispatches item and parent inventory callbacks. This does not claim mouse input or visible context-menu coverage, and does not directly assign keep/progress/success tags.");
    }
    private int NdIndependentQuality(GameItem machine)
    {
        GameItem[] modules = ((GameInventory)MachineHelper.GetBodyStorage(machine, 1)).childItems.ToArray();
        GameItem[] quality = modules.Where(item => item.identifier == "system_module_quality").ToArray();
        int ratio = machine.GetTagReadonly("MACHINE_QUALITY_RATIO_INT").GetInt();
        bool valid = quality.Length == 1 && ratio == 2 && quality[0].GetTagReadonly("BONUS_PERCENTAGE_QUALITY_INT").GetInt() == 8 &&
            quality[0].GetTagReadonly("TEMP_PERCENTAGE_QUALITY_INT").GetInt() == 8 &&
            modules.Where(item => item.identifier != "system_module_quality").All(item => item.GetTagReadonly("BONUS_PERCENTAGE_QUALITY_INT").GetInt() == 0 && item.GetTagReadonly("TEMP_PERCENTAGE_QUALITY_INT").GetInt() == 0) &&
            machine.GetTagReadonly("TOTAL_PERCENTAGE_QUALITY_BONUS_INT").GetInt() == 8;
        R.Check("nativedna-independent-quality-eight-over-two." + _ndOperations.Count, new { baseModule = 8, ratio = 2, adjustedQuality = 4 },
            new { ratio, modules = modules.Select(FlItem).ToArray(), nativeTotal = machine.GetTagReadonly("TOTAL_PERCENTAGE_QUALITY_BONUS_INT").GetInt() }, valid,
            "Build26317870 SystemQualityModule supplies +8, the native DNA factory ratio is 2, and the other installed fixed modules supply zero quality. Expected adjusted +4 is independent of PlanOperation/GetAjustedQuality.");
        if (!valid) throw new InvalidOperationException("Native DNA fixed quality fixture changed");
        return 8 / 2;
    }
    private JObject NdInputs(GameItem machine) => new() { ["state"] = MachineProgressHelper.GetMachineState(machine),
        ["operation"] = machine.GetTagReadonly("DNA_OPERATION_STRING").GetString(), ["progress"] = machine.GetTagReadonly("MACHINE_PROGRESS_CURRENT_TAG").GetInt(),
        ["target"] = machine.GetTagReadonly("MACHINE_PROGRESS_TARGET_TAG").GetInt(), ["battery"] = (MachineHelper.GetBodyStorage(machine, 0) as GameSlotInventory)?.childItem is { } cell ? NdItem(cell) : null,
        ["seed"] = MachineDnaManipulator.GetSeedSlot(machine).childItem is { } seed ? NdItem(seed) : null,
        ["input"] = MachineDnaManipulator.GetInputDiskSlot(machine).childItem is { } input ? NdItem(input) : null,
        ["output"] = MachineDnaManipulator.GetOutputDiskSlot(machine).childItem is { } output ? NdItem(output) : null,
        ["eject"] = new JArray(MachineDnaManipulator.GetEjectGrid(machine).childItems.Select(NdItem)) };
    private void NdNoop(GameItem machine, string name)
    {
        JObject before = NdInputs(machine); long grid = GridTotal;
        NativeCycle(machine);
        R.Check("nativedna-native-refusal." + name, new { state = before, grid }, new { state = NdInputs(machine), grid = GridTotal },
            JToken.DeepEquals(before, NdInputs(machine)) && GridTotal == grid,
            "A genuine missing native input/battery is used. Real installed cycle callback must not spend energy, edit genes/disk uses or start/finish progress.");
    }
    private IEnumerable<object?> NdOperate(GameItem machine, string operation, JObject receipt)
    {
        R.phase = "nativedna-" + (string?)receipt["stage"] + "-" + operation;
        GameItem seed = MachineDnaManipulator.GetSeedSlot(machine).childItem ?? throw new InvalidOperationException("Native DNA operation has no real seed");
        var plan = MachineDnaManipulator.PlanOperation(machine) ?? throw new InvalidOperationException("Native DNA PlanOperation returned no operation");
        var plant = HydroponicHelper.GetPlant(seed); int complexity = PlantGeneticsHelper.GetHeat(seed);
        foreach (var trait in PlantTraitRegistry.All)
        {
            int rank = PlantGeneticsHelper.GetTraitRank(seed, trait.id), delta = rank - (plant?.GetInnateRank(trait.id) ?? 0);
            complexity += delta < 0 ? Math.Abs(trait.complexity) * -delta : trait.complexity * delta;
            complexity += trait.complexityOffsetPerRank * rank;
        }
        int expectedProgress = 20 + 4 * Math.Max(0, complexity), expectedChance = Math.Clamp(95 - 5 * Math.Max(0, complexity) + NdIndependentQuality(machine), 5, 100);
        int speed = machine.GetTagReadonly("CURRENT_PROCESSING_SPEED_TAG").GetInt(), quotedCost = MachineryHelper.GetMachinePowerUsage(machine);
        int expectedCycles = speed > 0 ? checked((expectedProgress + speed - 1) / speed) : 0;
        if (expectedCycles is < 1 or > 32 || quotedCost < 1) throw new InvalidOperationException("Native configured speed/power exceeds the finite DNA operation budget");
        int wholeEnergy = checked(expectedCycles * quotedCost);
        if ((MachineHelper.GetBodyStorage(machine, 0) as GameSlotInventory)?.childItem is not { } initialBattery || Energy(initialBattery) < wholeEnergy) NdFreshBattery(machine, wholeEnergy);
        receipt["kind"] = operation; receipt["machineUid"] = machine.uniqueId; receipt["seedUid"] = seed.uniqueId;
        receipt["preActionBudget"] = JObject.FromObject(new { speed, quotedCost, expectedCycles, wholeEnergy });
        receipt["plan"] = JObject.FromObject(new { plan.operation, trait = plan.trait?.id, plan.isStacking, plan.successChance, plan.progress, plan.blockedReason });
        _ndOperations.Add(receipt);
        R.Check("nativedna-independent-fresh-fixture-99-percent." + _ndOperations.Count, new { clippedComplexity = 0, successChance = 99 },
            new { complexity, expectedChance, observedChance = plan.successChance }, complexity <= 0 && expectedChance == 99 && plan.successChance == 99,
            "Fresh native fillerweed inputs have no positive effective complexity. +8/2 quality gives exactly 99%, so a regression to 100% cannot be accepted through a reused probability helper.");
        bool legal = plan.operation == operation && plan.blockedReason.Length == 0 && plan.progress == expectedProgress && plan.successChance == expectedChance &&
            (operation != "apply" || plan.trait != null && plan.isStacking == (PlantGeneticsHelper.GetTraitRank(seed, plan.trait.id) > 0));
        R.Check("nativedna-real-operation-plan." + _ndOperations.Count, new { operation, expectedProgress, expectedChance, complexity }, receipt["plan"], legal,
            "Independent complexity from pre-action registered ranks/innate offsets/heat, then published base 20+4C and clamp(95-5C+native configured quality). Quality is an actual installed module effect; RNG/success/progress fields are not patched.");
        if (!legal) throw new InvalidOperationException("Native DNA plan is blocked or disagrees with the pre-action operation contract");
        int cycles = 0, expectedEnergy = 0, actualEnergy = 0; var rows = new JArray(); receipt["cycleRows"] = rows;
        do
        {
            GameItem cell = ((GameSlotInventory)MachineHelper.GetBodyStorage(machine, 0)).childItem;
            int energy = Energy(cell), cost = MachineryHelper.GetMachinePowerUsage(machine); long grid = GridTotal;
            NativeCycle(machine); cycles++;
            int debit = checked(energy - Energy(cell) + (int)(grid - GridTotal)); expectedEnergy += cost; actualEnergy += debit;
            rows.Add(new JObject { ["cycle"] = cycles, ["quotedCostBefore"] = cost, ["actualDebit"] = debit,
                ["batteryUid"] = cell.uniqueId, ["state"] = MachineProgressHelper.GetMachineState(machine) });
            R.Check("nativedna-real-cycle-power." + _ndOperations.Count + "." + cycles, new { cost, batteryUid = cell.uniqueId, sharedGrid = grid },
                new { debit, batteryUid = cell.uniqueId, sharedGrid = GridTotal }, cost > 0 && debit == cost && GridTotal == grid,
                "Pre-action native configured whole-cycle cost is the energy invoice; independent physical battery and grid readbacks meter the actual installed callback.");
            if (debit == 0) throw new InvalidOperationException("Native DNA cycle stalled; no finish/progress shortcut is permitted");
            yield return null;
        } while (MachineProgressHelper.GetMachineState(machine) != "STATE_READY" && cycles < 32);
        bool completed = cycles == expectedCycles && MachineProgressHelper.GetMachineState(machine) == "STATE_READY" && expectedEnergy == actualEnergy && actualEnergy == wholeEnergy;
        receipt["completed"] = completed; receipt["cycles"] = cycles; receipt["expectedEnergy"] = expectedEnergy; receipt["actualEnergy"] = actualEnergy;
        R.Check("nativedna-native-operation-finished." + _ndOperations.Count, "Native progress reaches READY within 32 real cycles with every power invoice paid", receipt, completed);
        if (!completed) throw new InvalidOperationException("Native DNA operation did not finish inside the bounded cycle budget");
    }
    private bool NdExtraction(GameItem machine, GameItem seed, GameItem disk, JObject beforeGenes, int beforeHeat, PlantTrait? priorTrait, int priorUses, JObject receipt, bool stabilizer)
    {
        PlantTrait? resultTrait = PlantGeneticsHelper.GetDiskTrait(disk);
        bool success = priorTrait == null ? resultTrait != null : resultTrait != null && PlantGeneticsHelper.GetDiskUses(disk) > priorUses;
        var allowed = PlantTraitRegistry.All.Where(trait => !trait.locked && trait.IsNegative && ((int?)beforeGenes[trait.id] ?? 0) > 0).ToArray();
        PlantTrait? selected = priorTrait ?? resultTrait;
        bool liveSeed = Store.FindAllItem(false).Any(item => item.uniqueId == seed.uniqueId);
        JObject expectedGenes = (JObject)beforeGenes.DeepClone();
        if (success && selected != null) expectedGenes[selected.id] = Math.Max(0, (int)beforeGenes[selected.id]! - 1);
        string? cropId = (string?)receipt["seedBefore"]?["crop"];
        var sourcePlant = PlantRegistry.All.SingleOrDefault(plant => plant.id == cropId);
        int addUses = selected == null ? 0 : sourcePlant?.GetInnateRank(selected.id) > 0 ? Math.Max(1, (int)beforeGenes[selected.id]!) : 1;
        bool diskValid = selected == null ? PlantGeneticsHelper.IsBlankDisk(disk) : resultTrait?.id == selected.id && PlantGeneticsHelper.GetDiskRank(disk) == 1 &&
            PlantGeneticsHelper.GetDiskUses(disk) == (success ? priorUses + addUses : priorUses);
        bool genesValid = !stabilizer ? !liveSeed : liveSeed && JToken.DeepEquals(expectedGenes, NdGenes(seed)) &&
            (selected == null ? allowed.Any(trait => PlantGeneticsHelper.GetHeat(seed) == beforeHeat + 2 + Math.Abs(trait.complexity)) : PlantGeneticsHelper.GetHeat(seed) == beforeHeat + 2 + Math.Abs(selected.complexity));
        bool failurePayloadUnchanged = success || JToken.DeepEquals(receipt["diskBefore"]?["rawDnaPayload"], NdDiskPayload(disk));
        bool valid = (selected == null || allowed.Any(trait => trait.id == selected.id)) && diskValid && genesValid && failurePayloadUnchanged && _ndExpected.Contains(disk.uniqueId) && GeneralHelper.IsItemOwned(disk);
        receipt["failureRawPayloadUnchanged"] = failurePayloadUnchanged;
        receipt["successObserved"] = success; receipt["diskAfter"] = NdDisk(disk); receipt["seedStillLive"] = liveSeed;
        if (liveSeed) receipt["seedAfter"] = NdItem(seed);
        R.Check("nativedna-real-extraction-gene-disk-ledger." + _ndOperations.Count, new { expectedGenes, priorUses, addedUsesOnSuccess = addUses, stabilizer }, receipt, valid,
            "Only native FinishExtract writes/refills the same blank/written disk. Successful stabilized extraction removes exactly one selected rank; heat follows 2+abs(complexity). Native innate trait extraction earns the pre-action donor rank in uses. Non-stabilized original input must disappear and requires a separate explicit retirement receipt.");
        if (!valid) throw new InvalidOperationException("Native extraction violated the independent gene/disk/consumption contract");
        return success;
    }
    private JArray NdSnapshot(string phase)
    {
        GameItem[] live = Store.FindAllItem(false).ToArray(); var result = new JArray(); bool complete = _ndExpected.Count > 0;
        foreach (int uid in _ndExpected.OrderBy(uid => uid))
        {
            GameItem[] matches = live.Where(item => item.uniqueId == uid).ToArray();
            bool identityKnown = _ndIdentity.TryGetValue(uid, out var identity);
            bool valid = uid > 0 && identityKnown && matches.Length == 1 && matches[0].identifier == identity.Id && matches[0].unitCount == identity.Count && identity.Count > 0 && GeneralHelper.IsItemOwned(matches[0]);
            R.Check("nativedna-promised-uid-custody." + phase + "." + uid, new { uid, id = identity.Id, count = identity.Count, owned = true, unique = true }, new { items = matches.Select(NdItem).ToArray() }, valid);
            complete &= valid; if (valid) result.Add(NdItem(matches[0]));
        }
        foreach (JObject retired in _ndRetired.OfType<JObject>())
        {
            int uid = (int)retired["uid"]!; int count = live.Count(item => item.uniqueId == uid);
            bool absent = count == 0 && !_ndExpected.Contains(uid);
            R.Check("nativedna-retired-uid-stays-consumed." + phase + "." + uid, retired, new { count }, absent);
            complete &= absent;
        }
        if (!complete) throw new InvalidOperationException("Native DNA custody lost a committed UID; live intersection cannot reduce the denominator");
        return result;
    }
}
