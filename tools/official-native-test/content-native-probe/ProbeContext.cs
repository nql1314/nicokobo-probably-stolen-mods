using System.Collections;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private readonly ProbeOptions O;
    private readonly ProbeResult R;
    private readonly List<GameItem> _retained = new();
    private string[] _preLoadEs3Files = Array.Empty<string>();
    private PlayerStore Store => PlayerStore.Instance;
    private Type Logistics => Reflect.Type("Nicokobo.AutomatedLogistics.AutomatedLogisticsApi");
    private Type MachineCatalog => Reflect.Type("Nicokobo.ContentInterop.MachineCatalog");
    internal ProbeContext(ProbeOptions options, ProbeResult result) { O = options; R = result; }
    internal void PrepareStart()
    {
        if (SceneManager.GetActiveScene().name == "InventoryScene") throw new InvalidOperationException("Probe requires a fresh process at the main menu");
        var data = NewGameData.Instance;
        data.startingPerks.Clear(); data.skipIntro = true; data.disableTutorialHint = true; data.hardMode = false;
        data.difficultyId = "normal"; data.endlessMode = false; data.isShowcase = false;
        data.isLoadFromMainMenu = O.Reload; data.saveFile = null;
        if (O.Reload)
        {
            if (!File.Exists(SaveFiles.AbsolutePath(SaveFiles.Slot(O.Slot)))) throw new FileNotFoundException("ES3 reload slot does not exist");
            var source = JObject.Parse(File.ReadAllText(O.Baseline));
            string expectedHash = (string?)(O.UseCopy ? source["persistence"]?["duplicateSaveSha256"] : source["persistence"]?["saveSha256"]) ?? "";
            string actualHash = Reflect.HashFile(SaveFiles.AbsolutePath(SaveFiles.Slot(O.Slot)));
            R.Check("reload.preload-file-binding", expectedHash, actualHash, expectedHash.Length == 64 && expectedHash == actualHash);
            if (expectedHash.Length != 64 || expectedHash != actualHash) throw new InvalidOperationException("Actual selected ES3 differs from save baseline before LoadGame");
            _preLoadEs3Files = System.IO.Directory.GetFiles(O.Persistent, "*.es3").Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray()!;
            data.saveSlot = O.Slot;
        }
        else
        {
            if (File.Exists(SaveFiles.AbsolutePath(SaveFiles.Slot(O.Slot)))) throw new InvalidOperationException("Fresh save mode refuses an existing slot");
            // Fixture selects the next legitimate slot; InitialSave still owns
            // creation, index registration and the real run identity.
            SaveFiles.IndexSave("slotIdGenerator", O.Slot);
            data.saveSlot = -1;
            data.startScenarioId = O.Family switch
            {
                "aug" or "grayline" or "dossieraug" or "storylegal" or "storysettled" or "storylost" or "storybeyond" or "achievements" or "achievementsgaps" or "augprotection" => "nicokobo.aug.mechanical_ascension.start",
                "theft" or "theftui" or "dossiertheft" => "nicokobo.definitely_stolen.pickpocket",
                _ => ""
            };
            data.startType = data.startScenarioId.Length == 0 ? NewGameData.StartType.APPRENTICE : NewGameData.StartType.MODDED;
            if (O.Family == "reputation") data.startType = NewGameData.StartType.FENCE;
            if (O.Family == "breeding") data.startType = NewGameData.StartType.RANCHER;
            if (data.startType == NewGameData.StartType.MODDED && StartingScenario.Get(data.startScenarioId) == null)
                throw new InvalidOperationException("Normal starting scenario is not registered: " + data.startScenarioId);
        }
    }
    internal bool Ready(out string missing)
    {
        missing = "";
        if (SceneManager.GetActiveScene().name != "InventoryScene") missing = "InventoryScene";
        else if (!PlayerStore.HasInstance || EmporiumEntry.Instance?.invElement == null || StoreStation.Instance == null || MapUIManager.Instance == null || CustomUIManager.Instance == null) missing = "native scene singletons/inventories";
        else if (string.IsNullOrWhiteSpace(Store.runID) || Store.saveSlotId != O.Slot) missing = "native InitialSave/LoadGame slot identity";
        else if (!File.Exists(SaveFiles.AbsolutePath(SaveFiles.Slot(O.Slot)))) missing = "actual ES3 slot file";
        else if ((O.FullStory || O.Family is "aug" or "grayline" or "dossieraug" or "achievements" or "achievementsgaps" or "augprotection") && !AugReady()) missing = "Aug committed opening/session";
        else if ((O.TheftRun || O.Family == "dossiertheft") && TheftState() == null) missing = "theft normal initialized Run.State";
        return missing.Length == 0;
    }
    internal IEnumerable<object?> Catalog()
    {
        R.phase = "all-registered-factories-and-assets";
        var candidateAssemblies = ModLoader.Mods.Where(m => ProbeRunner.RequiredMods.Contains(m.Manifest.ID)).Select(m => m.Assembly).ToArray();
        var ids = DirectoryMaster.GetIdentifierList<GameItem>("ModItemDirectory").OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var control in new[] { (Id: "water_jug", Feature: "CATEGORY_WATER_PURITY"), (Id: "metal_ingot", Feature: "INGOT_PURITY_TAG") })
        {
            CheckCatalogFactory(control.Id, () => DirectoryMaster.Create<GameItem>(control.Id), "native-control", control.Feature);
            yield return null;
        }
        foreach (string id in ids)
        {
            var factory = DirectoryMaster.GetFactory<GameItem>(id);
            if (factory == null) { R.Check("factory." + id, "Registered factory", null, false); continue; }
            if (!candidateAssemblies.Contains(factory.Method.DeclaringType!.Assembly)) continue;
            string provider = factory.Method.DeclaringType.Assembly.GetName().Name!;
            counts[provider] = counts.TryGetValue(provider, out int count) ? count + 1 : 1;
            CheckCatalogFactory(id, factory, provider);
            yield return null;
        }
        R.environment["factoryCountsByAssembly"] = counts;
        var contentAssemblies = ModLoader.Mods.Where(m => ProbeRunner.ContentMods.Contains(m.Manifest.ID)).Select(m => m.Assembly).ToArray();
        R.Check("factory.nonempty-four-content-providers", contentAssemblies.Select(a => a.GetName().Name).ToArray(), counts,
            contentAssemblies.All(a => counts.TryGetValue(a.GetName().Name!, out int count) && count > 0));
        foreach (var mod in ModLoader.Mods.Where(m => ProbeRunner.RequiredMods.Contains(m.Manifest.ID)))
        {
            string[] badSprites = mod.Sprites.Where(p => p.Value == null).Select(p => p.Key).ToArray();
            string[] badTextures = mod.Textures.Where(p => p.Value == null).Select(p => p.Key).ToArray();
            R.Check("assets." + mod.Manifest.ID, "Every loaded sprite/texture non-null; four content providers have sprites", new { sprites = mod.Sprites.Count, textures = mod.Textures.Count, badSprites, badTextures }, badSprites.Length == 0 && badTextures.Length == 0 && (!ProbeRunner.ContentMods.Contains(mod.Manifest.ID) || mod.Sprites.Count > 0));
            yield return null;
        }
        foreach (object? wait in TargetedRegressions()) yield return wait;
    }
    private void CheckCatalogFactory(string id, Func<GameItem> factory, string provider, string? requiredNativeFeature = null)
    {
        int generator = Store.currentUniqueId;
        string dataBefore = JsonConvert.SerializeObject(Store.ModData());
        string inventoryBefore = JsonConvert.SerializeObject(SnapshotItems());
        var liveUids = AllItems().Select(item => item.uniqueId).Where(uid => uid > 0).ToHashSet();
        GameItem? prototype = null;
        try
        {
            prototype = factory();
            // Native AddItemFeature binds its owner through GetUniqueID(). Both
            // water_jug and metal_ingot therefore allocate before Item() runs.
            bool identityValid = prototype != null && Store.currentUniqueId >= generator &&
                (prototype.uniqueId == 0 || (prototype.uniqueId > generator && prototype.uniqueId <= Store.currentUniqueId && !liveUids.Contains(prototype.uniqueId))) &&
                prototype.itemFeatures != null && prototype.itemFeatures.All(feature => feature != null && prototype.uniqueId > 0 && feature.parentItemUniqueId == prototype.uniqueId);
            bool nativeControlValid = requiredNativeFeature == null || (prototype != null && prototype.uniqueId > 0 &&
                prototype.itemFeatures.Any(feature => feature.identifier == requiredNativeFeature) &&
                DirectoryMaster.GetFactory<GameItem>(id)?.Method.DeclaringType?.Assembly == typeof(GameItem).Assembly);
            bool valid = identityValid && nativeControlValid && prototype!.identifier == id && prototype.parentInventory == null &&
                prototype.unitCount == 1 && !GraphUtils.FindAllChildrenType<GameItem>(prototype).Any() && prototype.modifiedShape != null &&
                prototype.modifiedShape.width > 0 && prototype.modifiedShape.height > 0;
            R.Check("factory." + id, new { id, identity = "UID zero or newly allocated; every feature bound to its owner; no live UID collision", requiredNativeFeature, count = 1, parent = "null", children = 0, shape = "positive" },
                prototype == null ? null : new { prototype.identifier, prototype.uniqueId, prototype.unitCount, generatorBefore = generator, generatorAfter = Store.currentUniqueId,
                    features = prototype.itemFeatures?.Select(feature => new { feature.identifier, feature.parentItemUniqueId }).ToArray(),
                    parent = prototype.parentInventory?.identifier, children = GraphUtils.FindAllChildrenType<GameItem>(prototype).Count,
                    width = prototype.modifiedShape?.width, height = prototype.modifiedShape?.height, provider }, valid,
                "Actual registered factory; native GameItem.AddItemFeature -> GetUniqueID and DirectoryMaster.Item accepting an existing UID");
            if (valid && id == "nicokobo.automated_logistics.void_tank")
            {
                var sprite = RenderHandler.LoadFromAtlas(prototype!.spriteAtlasPath, prototype.spritePath);
                float alpha = sprite.texture.GetPixelBilinear(.5f, .5f).a;
                R.Check("factory.void-tank-native-alpha-pixel-read", "Readable dynamic sprite pixels for native Image alpha hit testing",
                    new { sprite.texture.isReadable, alpha }, sprite.texture.isReadable,
                    "The previous RAS run failed in native UI pixel reads. Non-null sprite checks do not cover readability; this calls the actual factory sprite texture.");
            }
        }
        catch (Exception error) { R.Check("factory." + id, "Valid detached prototype with native feature ownership", null, false, provider, error.ToString()); }
        finally
        {
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(prototype!);
            bool dataUnchanged = JsonConvert.SerializeObject(Store.ModData()) == dataBefore;
            bool inventoryUnchanged = JsonConvert.SerializeObject(SnapshotItems()) == inventoryBefore;
            R.Check("factory.mod-data." + id, "ModData unchanged", new { unchanged = dataUnchanged }, dataUnchanged);
            R.Check("factory.live-inventory." + id, "Live inventory state/ownership unchanged", new { unchanged = inventoryUnchanged, generatorBefore = generator, generatorAfter = Store.currentUniqueId }, inventoryUnchanged);
        }
    }
    internal GameItem Spawn(string id, int count = 1)
    {
        if (!DirectoryMaster.Has<GameItem>(id)) throw new InvalidOperationException("Fixture factory is not registered: " + id);
        GameItem item = DirectoryMaster.Item(id);
        if (item.identifier != id || item.uniqueId <= 0 || !GeneralHelper.IsItemOwned(item)) throw new InvalidOperationException("Invalid native spawned fixture " + id);
        if (count != 1) item.SetUnitCount(count);
        return item;
    }
    internal void Place(GameInventory destination, GameItem item, string name, GameItem? throughMachine = null)
    {
        SlotMarker slot = throughMachine == null ? destination.TryFindOneValidInventorySlot(item) : throughMachine.TryFindOneValidInventorySlot(item);
        int before = destination.childItems.Where(i => i.identifier == item.identifier).Sum(i => i.unitCount);
        bool valid = slot != null && slot.IsValid() && slot.inventory == destination;
        int transferred = valid ? slot!.TryAcceptOnce() : 0;
        int after = destination.childItems.Where(i => i.identifier == item.identifier).Sum(i => i.unitCount);
        R.Check("native-placement." + name, new { destination = destination.identifier, valid = true, transferred = item.unitCount },
            new { destination = slot?.inventory?.identifier, valid, transferred, actualAdded = after - before, uid = item.uniqueId }, valid && transferred > 0 && after - before == transferred,
            "GameItem.TryFindOneValidInventorySlot -> SlotMarker.TryAcceptOnce -> destination inventory readback");
        if (!valid || transferred <= 0 || after - before != transferred) throw new InvalidOperationException("Native fixture placement failed: " + name);
    }
    internal void Retain(GameItem item)
    {
        if (item.parentInventory == null)
        {
            ModHelper.MoveItemToInventory(item);
            if (item.parentInventory == null) ModHelper.MoveItemToBackCounter(item);
            if (item.parentInventory == null) throw new InvalidOperationException("No native storage for retained fixture " + item.identifier);
        }
        _retained.Add(item);
    }
    internal object Ports(GameItem machine) => Reflect.Call(MachineCatalog, "Ports", machine) ?? throw new InvalidOperationException("No registered real ports " + machine.identifier);
    internal void NativeCycle(GameItem machine)
    {
        if (machine.parentInventory == null || machine.onCycleEndSlotItemFunc == null) throw new InvalidOperationException("Native cycle fixture is not installed in a real inventory");
        machine.onCycleEndSlotItemFunc.Invoke(machine, machine.parentInventory, machine.GetIncompleteSlotInInventory());
    }
    internal static int Energy(GameItem? battery) => battery?.GetTagReadonly("power_source_item_energy").GetInt() ?? 0;
    internal static JToken LiquidState(GameItem item) => new JObject(Liquid.Liquids.OrderBy(l => l.identifier, StringComparer.Ordinal)
        .Select(l => new JProperty(l.identifier, item.GetTagReadonly(l.currentPart).GetInt())));
    internal static JToken ItemState(GameItem item) => JObject.FromObject(new { id = item.identifier, uid = item.uniqueId, count = item.unitCount,
        owned = GeneralHelper.IsItemOwned(item), parentUid = item.parentInventory?.GetParentItem()?.uniqueId ?? 0,
        parentStorage = item.parentInventory?.identifier ?? "", energy = Energy(item), liquids = item.IsTag("LIQUID_CONTAINER_TAG") ? LiquidState(item) : null,
        basis = item.GetTagReadonly("NICOKOBO_FORGE_PRODUCTION_BASIS_V1").GetString(),
        alcoholTagEnabled = item.IsTag("ALCOHOL_VALUE"), alcoholValue = item.GetTagReadonly("ALCOHOL_VALUE").GetInt(),
        wineState = item.GetTagReadonly("WINE_CONTAINER_STATE").GetInt(),
        workDay = item.GetTagReadonly("nicokobo.mechcore_protocol.module_matrix.continuous_last_work_day.v1").GetInt(),
        decayDay = item.GetTagReadonly("nicokobo.mechcore_protocol.module_matrix.continuous_last_decay_day.v1").GetInt(),
        performance = item.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt(),
        efficiency = item.GetTagReadonly("BONUS_PERCENTAGE_EFFICIENCY_INT").GetInt(),
        quality = item.GetTagReadonly("BONUS_PERCENTAGE_QUALITY_INT").GetInt(),
        width = item.modifiedShape.width, height = item.modifiedShape.height });
    private IEnumerable<GameItem> AllItems() => O.TheftRun && TheftState() != null
        ? Reflect.Each(Reflect.Call(TheftRun(), "CaptureItems")).Cast<GameItem>() : Store.FindAllItem();
    internal JArray SnapshotItems() => new(AllItems().Where(i => i.uniqueId > 0).Distinct().OrderBy(i => i.uniqueId).Select(ItemState));
    internal void Save()
    {
        R.phase = "real-es3-save";
        ModHelper.SetModData("nicokobo.content_native_probe", "save-receipt", O.RunId);
        Store.SaveGame();
        string file = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId));
        bool exists = File.Exists(file) && new FileInfo(file).Length > 0 && ES3.KeyExists("playerStore", SaveFiles.Slot(Store.saveSlotId));
        R.Check("persistence.real-es3-file", "Native SaveGame wrote playerStore and index in isolated directory", new { file, exists, length = exists ? new FileInfo(file).Length : 0L }, exists && File.Exists(SaveFiles.AbsolutePath(SaveFiles.Index)));
        // Reuse the game's own read-only detached preview reader. It loads
        // actual ES3 bytes into an uninitialized carrier without constructing
        // or replacing the live player store, matching native DuplicateSlot.
        var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", new ES3Settings(file, ES3.Location.File))!;
        string receiptKey = "nicokobo.content_native_probe:save-receipt";
        bool persistedReceipt = saved.ModData().TryGetValue(receiptKey, out string receipt) && receipt == O.RunId;
        R.Check("persistence.actual-es3-carrier-readback", new { Store.runID, Store.saveSlotId, Store.playerCash, receipt = O.RunId, modData = Store.ModData() },
            new { saved.runID, saved.saveSlotId, saved.playerCash, receipt, modData = saved.ModData() },
            saved.runID == Store.runID && saved.saveSlotId == Store.saveSlotId && saved.playerCash == Store.playerCash && persistedReceipt &&
                JToken.DeepEquals(Reflect.Json(Store.ModData()), Reflect.Json(saved.ModData())),
            "Native SaveFiles.ReadStoreForPreview -> actual ES3.LoadInto; full inventory reload is still a separate process requirement");
        R.persistence["slotId"] = Store.saveSlotId; R.persistence["gameRunId"] = Store.runID;
        R.persistence["persistentDataPath"] = O.Persistent; R.persistence["saveFile"] = file;
        R.persistence["saveSha256"] = exists ? Reflect.HashFile(file) : null;
        R.persistence["items"] = SnapshotItems(); R.persistence["modData"] = new Dictionary<string, string>(Store.ModData());
        R.persistence["logistics"] = JToken.Parse((string)Reflect.Call(Logistics, "StateJson")!);
        R.persistence["cash"] = Store.playerCash; R.persistence["day"] = StoreStation.GetDayCounter();
        R.persistence["retainedUids"] = _retained.Select(i => i.uniqueId).Distinct().OrderBy(i => i).ToArray();
        if (O.DuplicateSlot)
        {
            string sourceHash = Reflect.HashFile(file);
            int duplicate = SaveFiles.DuplicateSlot(Store.saveSlotId, "NicoKobo native probe " + O.RunId);
            string copyFile = SaveFiles.AbsolutePath(SaveFiles.Slot(duplicate));
            bool copied = duplicate >= 0 && duplicate != Store.saveSlotId && File.Exists(copyFile) && Reflect.HashFile(copyFile) == sourceHash && Reflect.HashFile(file) == sourceHash;
            R.Check("aug.native-duplicate-slot", new { distinctSlot = true, sameSourceBytes = true, sourceUnchanged = true }, new { source = Store.saveSlotId, duplicate, copied }, copied,
                "Actual SaveFiles.DuplicateSlot; no InitialSave/SaveGame follows copy creation");
            R.persistence["duplicateSlotId"] = duplicate; R.persistence["duplicateSaveSha256"] = copied ? Reflect.HashFile(copyFile) : null;
            R.persistence["duplicateSourceSha256"] = sourceHash;
        }
    }
    internal IEnumerable<object?> Reload()
    {
        R.phase = "cross-process-native-es3-load";
        JObject baseline = JObject.Parse(File.ReadAllText(O.Baseline));
        string expectedMode = O.Family + "-save";
        bool breedingCheckpoint = O.Family == "breeding" && ProbeContinuations.BreedingCheckpoint(baseline);
        bool sameFamily = (string?)baseline["mode"] == expectedMode || (O.Family == "breeding" && (string?)baseline["mode"] == "breeding-reload");
        bool source = sameFamily && ((bool?)baseline["passed"] == true || breedingCheckpoint) && (bool?)baseline["executedChecksPassed"] == true &&
            (string?)baseline["runId"] != O.RunId && (string?)baseline["processToken"] != R.processToken &&
            (int?)baseline["processId"] != R.processId && (string?)baseline["persistence"]?["persistentDataPath"] == O.Persistent &&
            (int?)(O.UseCopy ? baseline["persistence"]?["duplicateSlotId"] : baseline["persistence"]?["slotId"]) == O.Slot;
        R.Check("reload.fresh-process-baseline", new { mode = expectedMode, independentProcess = true, path = O.Persistent, slot = O.Slot },
            new { mode = (string?)baseline["mode"], runId = (string?)baseline["runId"], pid = (int?)baseline["processId"], source }, source);
        if (!source) throw new InvalidOperationException("Save baseline did not pass executed checks or is from same process/wrong slot/path/family");
        foreach (var candidate in R.candidates)
        {
            JToken observedCandidate = Reflect.Json(candidate.Value);
            JToken? expectedCandidate = baseline["candidates"]?[candidate.Key];
            R.Check("reload.identical-candidate." + candidate.Key, expectedCandidate?["sha256"], observedCandidate["sha256"],
                (string?)expectedCandidate?["sha256"] == (string?)observedCandidate["sha256"] && (string?)expectedCandidate?["version"] == (string?)observedCandidate["version"]);
        }
        JToken loadedInterop = Reflect.Json(R.environment["loadedContentInterop"]);
        R.Check("reload.identical-shared-interop", baseline["environment"]?["loadedContentInterop"]?["sha256"], loadedInterop["sha256"],
            (string?)baseline["environment"]?["loadedContentInterop"]?["sha256"] == (string?)loadedInterop["sha256"]);
        JToken persistence = baseline["persistence"]!;
        if (O.AugPersistence)
        {
            object rebind = Reflect.Get(Reflect.Type("AugMechanicalAscension.Official.OfficialRunForkApi"), "LastLoadedRebind")!;
            string[] keys = Reflect.Each(Reflect.Get(rebind, "Keys")).Cast<string>().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            string[] expectedKeys = O.UseCopy ? ((JObject)persistence["modData"]!).Properties().Select(p => p.Name)
                .Where(k => k is "nicokobo.aug.state" or "nicokobo.aug.story.lost_and_found" or "nicokobo.aug.grayline.supply" or "nicokobo.aug.workshop.achievements")
                .OrderBy(x => x, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
            string expectedStatus = O.UseCopy ? "ReboundInMemory" : "SameSlot";
            R.Check("aug.loaded-slot-production-diagnostic", new { status = expectedStatus, sourceSlot = (int)persistence["slotId"]!, targetSlot = O.Slot, keys = expectedKeys }, Reflect.Json(rebind),
                Reflect.Str(rebind, "Status") == expectedStatus && Reflect.Int(rebind, "SourceSlot") == (int)persistence["slotId"]! &&
                    Reflect.Int(rebind, "TargetSlot") == O.Slot && expectedKeys.SequenceEqual(keys),
                "Read-only production LastLoadedRebind; persistent state and actual ES3 bytes are independently checked below");
        }
        R.Check("reload.game-run-identity", persistence["gameRunId"], Store.runID, (string?)persistence["gameRunId"] == Store.runID);
        var expectedItems = (JArray)persistence["items"]!;
        var observed = SnapshotItems();
        R.Check("reload.stable-ids-resources-ports-and-growth", expectedItems, observed, JToken.DeepEquals(expectedItems, observed), "Normal InventoryScene -> PlayerStore.LoadGame -> ES3.LoadInto -> native inventory DecodeSaveItem");
        foreach (JProperty key in ((JObject)persistence["modData"]!).Properties())
        {
            Store.ModData().TryGetValue(key.Name, out string value);
            JToken expectedValue = ParseJsonOrText((string?)key.Value), observedValue = ParseJsonOrText(value);
            if (O.UseCopy) RebindExpectedAugKey(key.Name, expectedValue, O.Slot);
            R.Check("reload.mod-data." + key.Name, expectedValue, observedValue, JToken.DeepEquals(expectedValue, observedValue));
        }
        var logisticsBefore = JToken.Parse((string)Reflect.Call(Logistics, "StateJson")!);
        R.Check("reload.logistics-receipts-and-uids", persistence["logistics"], logisticsBefore, JToken.DeepEquals(persistence["logistics"], logisticsBefore));
        // Observe a complete ordinary runtime scan interval, without calling
        // reward grant methods or mutating receipts, then verify no regrant.
        int[] beforeIds = Store.FindAllItem().Select(i => i.uniqueId).OrderBy(i => i).ToArray();
        float until = Time.realtimeSinceStartup + 2.5f;
        while (Time.realtimeSinceStartup < until) yield return null;
        int[] afterIds = Store.FindAllItem().Select(i => i.uniqueId).OrderBy(i => i).ToArray();
        R.Check("reload.no-duplicate-rewards", beforeIds, afterIds, beforeIds.SequenceEqual(afterIds));
        string[] loadAfterFiles = System.IO.Directory.GetFiles(O.Persistent, "*.es3").Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray()!;
        string expectedLoadedHash = (string?)(O.UseCopy ? persistence["duplicateSaveSha256"] : persistence["saveSha256"]) ?? "";
        R.Check("reload.no-implicit-save-or-new-slot", new { files = _preLoadEs3Files, selectedFileSha256 = expectedLoadedHash },
            new { files = loadAfterFiles, selectedFileSha256 = Reflect.HashFile(SaveFiles.AbsolutePath(SaveFiles.Slot(O.Slot))) },
            _preLoadEs3Files.SequenceEqual(loadAfterFiles) && Reflect.HashFile(SaveFiles.AbsolutePath(SaveFiles.Slot(O.Slot))) == expectedLoadedHash);
        R.persistence["sourceBaseline"] = O.Baseline; R.persistence["sourceBaselineSha256"] = Reflect.HashFile(O.Baseline);
        R.persistence["slotId"] = Store.saveSlotId; R.persistence["gameRunId"] = Store.runID; R.persistence["items"] = observed;
        if (O.Family is "manager" or "manageren") foreach (object? wait in ItemManagerReload(baseline)) yield return wait;
        if (O.Family == "night") foreach (object? wait in NightReload(baseline)) yield return wait;
        if (O.Family == "grayline") foreach (object? wait in GraylineReload(baseline)) yield return wait;
        if (O.FullStory) foreach (object? wait in FullStoryReload(baseline)) yield return wait;
        if (O.Family == "logisticsfull") foreach (object? wait in FullLogisticsReload(baseline)) yield return wait;
        if (O.Family is "achievements" or "ratfixture") foreach (object? wait in ForgeAchievementsReload(baseline)) yield return wait;
        if (O.Family == "achievementsgaps") foreach (object? wait in NativeAchievementGapsReload(baseline)) yield return wait;
        if (O.Family == "reputation") foreach (object? wait in NativeReputationReload(baseline)) yield return wait;
        if (O.Family == "breeding") foreach (object? wait in NativeBreedingReload(baseline)) yield return wait;
        if (O.Family == "augprotection") foreach (object? wait in AugProtectionReload(baseline)) yield return wait;
        if (O.Family == "logisticsboundaries") foreach (object? wait in LogisticsBoundariesReload(baseline)) yield return wait;
        if (O.Family == "deviceflows") foreach (object? wait in NativeDeviceFlowsReload(baseline)) yield return wait;
        if (O.Family == "containerproduction") foreach (object? wait in NativeContainerProductionReload(baseline)) yield return wait;
        if (O.Family == "bottleproduction") foreach (object? wait in NativeContainerProductionReload(baseline)) yield return wait;
        if (O.Family == "extendedproduction") foreach (object? wait in NativeExtendedProductionReload(baseline)) yield return wait;
        if (O.Family == "vanillachemistry") foreach (object? wait in VanillaChemistryReload(baseline)) yield return wait;
        if (O.Family == "chemicalreactions") foreach (object? wait in NativeChemicalReactionsReload(baseline)) yield return wait;
        if (O.Family == "nightdevices") foreach (object? wait in NativeNightDevicesReload(baseline)) yield return wait;
        if (O.Family == "businessdevices") foreach (object? wait in NativeBusinessDevicesReload(baseline)) yield return wait;
        if (O.Family == "nativedna") foreach (object? wait in NativeDnaReload(baseline)) yield return wait;
        if (O.Family is "dossier" or "dossieraug" or "dossiertheft") foreach (object? wait in DossierReload(baseline)) yield return wait;
        if (O.Family == "review")
        {
            foreach (object? wait in ReviewProductionReload(baseline)) yield return wait;
            foreach (object? wait in ReviewLogisticsReload(baseline)) yield return wait;
        }
        if (O.UseCopy)
        {
            int sourceSlot = (int)persistence["slotId"]!;
            string sourceFile = SaveFiles.AbsolutePath(SaveFiles.Slot(sourceSlot));
            string hash = Reflect.HashFile(sourceFile);
            R.Check("aug.copy-reload-source-byte-preservation", persistence["duplicateSourceSha256"], hash, hash == (string?)persistence["duplicateSourceSha256"]);
            string[] afterFiles = System.IO.Directory.GetFiles(O.Persistent, "*.es3").Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray()!;
            R.Check("aug.copy-reload-no-save-or-extra-slot", new { originalFiles = _preLoadEs3Files, expectedTarget = O.Slot, expectedRun = (string?)persistence["gameRunId"] },
                new { afterFiles, sourceSlot, targetSlot = Store.saveSlotId, Store.runID },
                Store.saveSlotId == O.Slot && Store.runID == (string?)persistence["gameRunId"] && _preLoadEs3Files.SequenceEqual(afterFiles) &&
                    Reflect.HashFile(SaveFiles.AbsolutePath(SaveFiles.Slot(O.Slot))) == (string?)persistence["duplicateSaveSha256"]);
        }
    }
    private static JToken ParseJsonOrText(string? text)
    {
        if (text == null) return JValue.CreateNull();
        try { return JToken.Parse(text); } catch { return new JValue(text); }
    }
    private static void RebindExpectedAugKey(string key, JToken value, int targetSlot)
    {
        // Exact source-owned RunForkMigration contract supplied by the Aug
        // migration owner. No other field/receipt/UID/revision may change.
        if (key == "nicokobo.aug.state")
        {
            if (value is JObject state) state["saveSlotId"] = targetSlot;
            if (value["opening"] is JObject opening) opening["saveSlotId"] = targetSlot;
        }
        if (key is "nicokobo.aug.story.lost_and_found" or "nicokobo.aug.grayline.supply")
            if (value is JObject state) state["saveSlotId"] = targetSlot;
        if (key == "nicokobo.aug.workshop.achievements" && value is JObject workshop) workshop["slotId"] = targetSlot;
        if (key == "nicokobo.forge.native_achievements" && value is JObject forge) forge["SlotId"] = targetSlot;
    }
}
