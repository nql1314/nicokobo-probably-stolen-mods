using System.Text.Json.Nodes;

using System.Text.Json;
using AugMechanicalAscension;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using AugMechanicalAscension.Services;

try {
if (args is ["workshop"]) { Console.WriteLine($"Workshop checks passed: {WorkshopAchievementChecks.Run()}"); return; }
if(args is ["json"]){JsonMigrationChecks.Run();return;}
if (args is ["runtime"]) { RuntimeBoundaryChecks.Run(); return; }
if (args is ["grayline"])
{
    int graylineChecks = 0;
    GraylineSupplyChecks.Run((condition, message) => { graylineChecks++; if (!condition) throw new Exception(message); });
    Console.WriteLine($"Grayline supply checks passed: {graylineChecks}");
    return;
}

if (args is ["story-schedule"] or ["story"])
{
    int count = 0;
    var storyFailures = new List<string>();
    void CheckStory(bool ok, string message) { count++; if (!ok) storyFailures.Add(message); }
    if (args[0] == "story") StoryChecks.Run(CheckStory);
    else StoryChecks.RunSchedule(CheckStory);
    if (storyFailures.Count != 0) throw new Exception(string.Join(Environment.NewLine, storyFailures));
    Console.WriteLine($"Story checks passed: {count}");
    return;
}

if (args.Length == 4 && args[0] is "--inspect-owned-item" or
    "--inspect-saved-consumable")
{
    string directory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
    string name = Path.GetFileNameWithoutExtension(args[1]);
    if (!name.StartsWith("save_", StringComparison.Ordinal) ||
        !int.TryParse(name[5..], out int slotId) ||
        !int.TryParse(args[3], out int uniqueId))
        throw new ArgumentException("Expected save_<slot>.es3 and a native uniqueId");
    var scope = args[0] == "--inspect-saved-consumable"
        ? NativeItemSearchScope.AllSavedInventories
        : NativeItemSearchScope.MainInventory;
    var present = Es3SlotReadback.Compare(directory, slotId,
        expectedNative: new NativeSaveSnapshot(OwnedItem:
            new NativeItemIdentitySnapshot(args[2], uniqueId, true,
                SearchScope: scope)));
    var absent = Es3SlotReadback.Compare(directory, slotId,
        expectedNative: new NativeSaveSnapshot(OwnedItem:
            new NativeItemIdentitySnapshot(args[2], uniqueId, false,
                SearchScope: scope)));
    Console.WriteLine($"ownedItem={args[2]}; nativeId={uniqueId}; " +
        $"present={present.Status}; absent={absent.Status}");
    return;
}

if (args.Length == 2 && args[0] == "--inspect-neural-item")
{
    using var save = JsonDocument.Parse(Es3SlotReadback.Normalize(File.ReadAllText(args[1])),
        new JsonDocumentOptions { MaxDepth = 256 });
    var inventoryJson = save.RootElement.GetProperty("playerStore").GetProperty("value")
        .GetProperty("mainInvJSON").GetString()!;
    using var inventory = JsonDocument.Parse(Es3SlotReadback.Normalize(inventoryJson),
        new JsonDocumentOptions { MaxDepth = 256 });
    PrintNeuralItems(inventory.RootElement);
    return;

    static void PrintNeuralItems(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("identifier", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                id.GetString() == NeuralModuleRules.ItemId)
            {
                string? TagString(string name)
                {
                    var keys = element.GetProperty("_keys");
                    var values = element.GetProperty("_values");
                    for (int i = 0; i < keys.GetArrayLength(); i++)
                        if (keys[i].GetString() == name)
                            return values[i].GetProperty("internalValueString").GetString();
                    return null;
                }
                int? TagInt(string name)
                {
                    var keys = element.GetProperty("_keys");
                    var values = element.GetProperty("_values");
                    for (int i = 0; i < keys.GetArrayLength(); i++)
                        if (keys[i].GetString() == name)
                            return values[i].GetProperty("valueInt").GetInt32();
                    return null;
                }
                Console.WriteLine($"neuralItem={id.GetString()}; " +
                    $"performance={TagInt("BONUS_PERCENTAGE_PERFORMANCE_INT")}; " +
                    $"efficiency={TagInt("BONUS_PERCENTAGE_EFFICIENCY_INT")}; " +
                    $"quality={TagInt("BONUS_PERCENTAGE_QUALITY_INT")}; " +
                    $"effect1={TagString("MODULE_EFFECT1_TAG")}; " +
                    $"effect2={TagString("MODULE_EFFECT2_TAG") ?? "none"}");
                return;
            }
            foreach (var property in element.EnumerateObject())
                PrintNeuralItems(property.Value);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                PrintNeuralItems(child);
    }
}

if (args.Length == 3 && args[0] == "--inspect-slot" && int.TryParse(args[2], out int inspectedSlot))
{
    var result = Es3SlotReadback.Compare(args[1], inspectedSlot);
    Console.WriteLine($"slotReadback={result.Status}; bytes={result.ByteCount}");
    return;
}

if (args.Length == 2 && args[0] == "--inspect-state")
{
    using var outer = JsonDocument.Parse(Es3SlotReadback.Normalize(File.ReadAllText(args[1])),
        new JsonDocumentOptions { MaxDepth = 256 });
    var savedStateJson = outer.RootElement.GetProperty("playerStore").GetProperty("value")
        .GetProperty("modData").GetProperty(RunState.SaveKey).GetString();
    var savedStateRead = RunStateCodec.Read(savedStateJson);
    Console.WriteLine($"stateRead={savedStateRead.Status}; phase={savedStateRead.State?.InitPhase}; " +
        $"revision={savedStateRead.State?.Revision}; opening={savedStateRead.State?.Opening?.Phase}");
    if (savedStateRead.State?.Opening is { } savedOpening)
        foreach (var grant in savedOpening.Grants)
            Console.WriteLine($"grant={grant.Kind}; result={grant.Result?.Status}");
    return;
}

if (args.Length == 5 && args[0] == "--inspect-main-item" &&
    int.TryParse(args[2], out int inventorySlot) &&
    int.TryParse(args[4], out int expectedUnits))
{
    var expected = new NativeSaveSnapshot(MainRootUnitCounts:
        new Dictionary<string, int>(StringComparer.Ordinal) { [args[3]] = expectedUnits });
    var result = Es3SlotReadback.Compare(args[1], inventorySlot,
        expectedNative: expected);
    Console.WriteLine($"slotReadback={result.Status}; bytes={result.ByteCount}");
    return;
}

if (args.Length == 2 && args[0] == "--inspect-inventory-shape")
{
    using var outer = JsonDocument.Parse(Es3SlotReadback.Normalize(File.ReadAllText(args[1])),
        new JsonDocumentOptions { MaxDepth = 256 });
    var store = outer.RootElement.GetProperty("playerStore").GetProperty("value");
    foreach (var field in store.EnumerateObject().Where(x =>
        x.Name.EndsWith("InvJSON", StringComparison.Ordinal) ||
        x.Name.EndsWith("InventoryJSON", StringComparison.Ordinal)))
    {
        if (field.Value.ValueKind != JsonValueKind.String) continue;
        using var inventory = JsonDocument.Parse(Es3SlotReadback.Normalize(field.Value.GetString()!),
            new JsonDocumentOptions { MaxDepth = 256 });
        var root = inventory.RootElement;
        string keys = string.Join(',', root.EnumerateObject().Select(x => x.Name));
        int items = root.TryGetProperty("saveItems", out var savedItems) &&
                    savedItems.ValueKind == JsonValueKind.Array ? savedItems.GetArrayLength() : -1;
        Console.WriteLine($"{field.Name}: keys={keys}; saveItems={items}");
        if (items > 0)
            Console.WriteLine("saveItemFields=" +
                string.Join(',', savedItems[0].EnumerateObject().Select(x => x.Name)));
        if (field.Name == "mainInvJSON")
            foreach (var item in savedItems.EnumerateArray().Take(20))
            {
                Console.WriteLine($"item={item.GetProperty("identifier").GetString()}; " +
                    $"units={item.GetProperty("unitCount").GetInt32()}; " +
                    $"children={item.GetProperty("childItems").GetArrayLength()}");
                var identifier = item.GetProperty("identifier").GetString() ?? "";
                if (identifier.Contains("module", StringComparison.OrdinalIgnoreCase) ||
                    identifier.Contains("node", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"  itemType={item.GetProperty("itemType")}; " +
                        $"itemTypes={item.GetProperty("itemTypes")}; " +
                        $"keys={item.GetProperty("_keys")}; " +
                        $"values={item.GetProperty("_values")}");
            }
    }
    return;
}

if (args.Length == 3 && args[0] == "--inspect-medical-values")
{
    if (!args[2].EndsWith("InvJSON", StringComparison.Ordinal) ||
        new FileInfo(args[1]).Length > 16 * 1024 * 1024)
        throw new InvalidDataException("Expected a bounded native inventory field");
    using var outer = JsonDocument.Parse(Es3SlotReadback.Normalize(File.ReadAllText(args[1])),
        new JsonDocumentOptions { MaxDepth = 256 });
    var store = outer.RootElement.GetProperty("playerStore").GetProperty("value");
    string? inventoryText = store.GetProperty(args[2]).GetString();
    if (inventoryText == null || inventoryText.Length > 16 * 1024 * 1024)
        throw new InvalidDataException("Expected bounded native inventory JSON");
    using var inventory = JsonDocument.Parse(Es3SlotReadback.Normalize(inventoryText),
        new JsonDocumentOptions { MaxDepth = 256 });
    var items = inventory.RootElement.GetProperty("saveItems");
    if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 256)
        throw new InvalidDataException("Unexpected inventory node count");
    foreach (var item in items.EnumerateArray())
    {
        string? id = item.GetProperty("identifier").GetString();
        if (id is not ("bandage_item" or "hemostatic_bandage_item" or
            "topical_bandage_item" or "large_purple_injector")) continue;
        Console.WriteLine($"item={id}; nativeId={item.GetProperty("uniqueId").GetInt32()}; "
            + $"units={item.GetProperty("unitCount").GetInt32()}; "
            + $"unitBaseValue={item.GetProperty("unitBaseValue").GetInt64()}; "
            + $"unitValue={item.GetProperty("unitValue").GetInt64()}; "
            + $"lateUnitValue={item.GetProperty("lateUnitValue").GetInt64()}; "
            + $"backupUnitValue={item.GetProperty("backupUnitValue").GetInt64()}");
    }
    return;
}

if (args.Length == 3 && args[0] == "--inspect-inventory-graph")
{
    if (!args[2].EndsWith("InvJSON", StringComparison.Ordinal) ||
        new FileInfo(args[1]).Length > 16 * 1024 * 1024)
        throw new InvalidDataException("Expected a bounded native inventory field");
    using var outer = JsonDocument.Parse(Es3SlotReadback.Normalize(File.ReadAllText(args[1])),
        new JsonDocumentOptions { MaxDepth = 256 });
    var store = outer.RootElement.GetProperty("playerStore").GetProperty("value");
    var field = store.GetProperty(args[2]);
    if (field.ValueKind != JsonValueKind.String)
        throw new InvalidDataException("Expected a native inventory JSON field");
    using var inventory = JsonDocument.Parse(Es3SlotReadback.Normalize(field.GetString()!),
        new JsonDocumentOptions { MaxDepth = 256 });
    var items = inventory.RootElement.GetProperty("saveItems");
    if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 256)
        throw new InvalidDataException("Unexpected inventory node count");
    Console.WriteLine($"{args[2]}: nodes={items.GetArrayLength()}");
    var uniqueIds = new Dictionary<int, int>();
    int zeroUniqueIds = 0;
    for (int index = 0; index < items.GetArrayLength(); index++)
    {
        var item = items[index];
        int uniqueId = item.GetProperty("uniqueId").GetInt32();
        if (uniqueId == 0) zeroUniqueIds++;
        else uniqueIds[uniqueId] = uniqueIds.GetValueOrDefault(uniqueId) + 1;
        var children = item.GetProperty("childItems");
        var childSlots = item.GetProperty("childItemInventoryNode");
        var tagKeys = item.GetProperty("_keys");
        var tagValues = item.GetProperty("_values");
        if (tagKeys.GetArrayLength() != tagValues.GetArrayLength())
            throw new InvalidDataException("Tag key and value counts differ");
        var relevantTags = new List<string>();
        for (int tagIndex = 0; tagIndex < tagKeys.GetArrayLength(); tagIndex++)
        {
            string? key = tagKeys[tagIndex].GetString();
            if (key is not ("AUG_SCANNER_TAG" or "NOT_FOR_RESALE" or
                "IS_OWNED_TAG" or "power_source_item" or "ENERGY_CELL" or
                "power_source_item_energy" or "power_source_item_max_energy" or
                "power_source_item_bandwidth"))
                continue;
            bool enabled = tagValues[tagIndex].GetProperty("valueEnabled").GetBoolean();
            string numeric = key is "power_source_item_energy" or
                "power_source_item_max_energy" or "power_source_item_bandwidth"
                ? $":{tagValues[tagIndex].GetProperty("valueInt").GetInt32()}" : "";
            relevantTags.Add($"{key}:{enabled}{numeric}");
        }
        Console.WriteLine($"node={index}; id={item.GetProperty("identifier").GetString()}; " +
            $"uniqueId={uniqueId}; " +
            $"units={item.GetProperty("unitCount").GetInt32()}; " +
            $"children=[{string.Join(',', children.EnumerateArray().Select(x => x.GetInt32()))}]; " +
            $"childSlots=[{string.Join(',', childSlots.EnumerateArray().Select(x => x.GetInt32()))}]; " +
            $"tagCount={tagKeys.GetArrayLength()}; relevantTags=[{string.Join(',', relevantTags)}]");
    }
    Console.WriteLine($"uniqueIdSummary: zero={zeroUniqueIds}; " +
        $"duplicates=[{string.Join(',', uniqueIds.Where(x => x.Value > 1)
            .Select(x => $"{x.Key}:{x.Value}"))}]");
    return;
}

int checks = 0;

void Check(bool condition, string name)
{
    checks++;
    if (!condition) throw new Exception($"Failed: {name}");
}

// Synthetic receipt for downstream pure-rule checks. It does not represent
// a native inventory transfer or a verified game save.
RunState ReadyRun(RunState state)
{
    var id = Guid.NewGuid().ToString("D");
    OpeningGrantRecord Grant(string kind, int units)
    {
        var itemId = kind switch
        {
            "neuralModule" => NeuralModuleRules.ItemId,
            "materialRefiner" => OpeningJournalRules.MaterialRefinerId,
            "battery" => "energy_credit",
            "medical" => "topical_bandage_item",
            "electronic" => "common_electronic",
            "scrapMetal" => "scrap_metal",
            "materialSign" => "sign_material",
            _ => throw new InvalidOperationException("Unknown opening item")
        };
        int ownedBefore = kind == "medical" ? 1 : 0;
        var request = new OpeningGrantRequest(id, itemId, units, ownedBefore);
        var result = new OpeningGrantResult(OpeningGrantStatus.Applied,
            ownedBefore, ownedBefore + units, units);
        if (kind == "neuralModule")
            result = result with
            {
                NativeItemInstanceId = "test-opening-module",
                OwnershipEvidenceId = "test-owned-opening-module"
            };
        return new(kind, request, result);
    }
    return state with
    {
        Revision = 5,
        InitPhase = "complete",
        Opening = new(id, state.GameRunId, state.SaveSlotId, state.StartDay,
            OpeningJournalRules.Committed,
            [Grant("neuralModule", 1), Grant("materialRefiner", 1),
             Grant("battery", 1), Grant("medical", 1),
             Grant("electronic", 1), Grant("scrapMetal", AugNumbers.OpeningItems.ScrapMetalCount),
             Grant("materialSign", AugNumbers.OpeningItems.MaterialSignCount)],
            new(OpeningEconomyApplyStatus.Applied,
                After: new(state.ConfigSnapshot.InitialCredits, state.ConfigSnapshot.InitialRent,
                    state.ConfigSnapshot.InitialSecurityReputation, true))),
        NeuralModules = new NeuralModuleState
        {
            Receipts = [new NeuralModuleReceipt(NeuralModuleRules.Opening,
                id, "test-opening-module", state.StartDay,
                "test-owned-opening-module")]
        }
    };
}

if (args is ["protected-items"])
{
    ProtectedItemChecks.Run(Check);
    Console.WriteLine($"Protected-item checks passed: {checks}");
    return;
}
if (args is ["opening-config"])
{
    OpeningConfigChecks.Run(Check);
    Console.WriteLine($"Opening configuration checks passed: {checks}");
    return;
}
if (args is ["loaded-copy"])
{
    LoadedSlotChecks.Run(ReadyRun(RunStateCodec.CreateNew("steam-release-26274380",
        "10000000-0000-0000-0000-000000000001", 7, 0, new())), Check);
    Console.WriteLine($"Loaded-slot checks passed: {checks}");
    return;
}

var gameRunId = Guid.NewGuid().ToString("D");
Check(StartDefinition.Id == "nicokobo.aug.mechanical_ascension.start" &&
      StartDefinition.Id == RunState.AugMechanicalAscensionId &&
      RunState.SaveKey == "nicokobo.aug.state" &&
      StartDefinition.Name.English == "Aug: Mechanical Ascension" &&
      StartDefinition.NativeStartTypeId == (int)NewGameData.StartType.MODDED &&
      !StartDefinition.UsesPerkPoints &&
      !string.IsNullOrWhiteSpace(StartDefinition.Name.Chinese) &&
      !string.IsNullOrWhiteSpace(StartDefinition.Name.English) &&
      !string.IsNullOrWhiteSpace(StartDefinition.Description.Chinese) &&
      !string.IsNullOrWhiteSpace(StartDefinition.Description.English),
    "the independent start has a stable bilingual identity and costs no perk points");
var loadedConfig = ConfigSnapshotLoader.Parse("""
    {"initialCredits":650,"initialRent":275,"initialBatteryCount":1,
     "cyberneticPrices":{"cyberEye":950}}
    """);
Check(loadedConfig.Status == ConfigLoadStatus.Loaded && loadedConfig.Config.InitialCredits == 650 &&
      loadedConfig.Config.InitialRent == 275 && loadedConfig.Config.InitialSecurityReputation == -15 &&
      loadedConfig.Config.InitialBatteryCount == 1 &&
      loadedConfig.Config.InitialMedicalCount == 1 &&
      loadedConfig.Config.InitialElectronicCount == 1 &&
      loadedConfig.Config.NeuralModuleTestPrice == 100 &&
      loadedConfig.Config.CyberneticPrices.CyberEye == 950 &&
      loadedConfig.Config.CyberneticPrices.AiAssistantCore == 700,
    "partial configuration overrides only supplied new-run defaults");
Check(ConfigSnapshotLoader.Parse("\uFEFF{\"initialCredits\":650}").Status ==
      ConfigLoadStatus.Loaded, "UTF-8 BOM configuration is accepted");
var configuredRun = RunStateCodec.CreateNew("steam-demo-25382790", gameRunId, 23, 1,
    loadedConfig.Config);
Check(configuredRun.ConfigSnapshot.InitialCredits == 650 &&
      configuredRun.ConfigSnapshot.InitialBatteryCount == 1 &&
      RunStateCodec.Read(RunStateCodec.Write(configuredRun)).Status == ReadStatus.Ready,
    "loaded configuration becomes a valid persisted run snapshot");
Check(ConfigSnapshotLoader.Parse("{\"initialRent\":-1}").Status == ConfigLoadStatus.Invalid &&
      ConfigSnapshotLoader.Parse("{\"initialRent\":-1}").Config.InitialRent == 250,
    "invalid numeric configuration falls back to defaults");
Check(ConfigSnapshotLoader.Parse("{\"initialMedicalCount\":0}").Status ==
      ConfigLoadStatus.Invalid &&
      ConfigSnapshotLoader.Parse("{\"initialElectronicCount\":1001}").Status ==
      ConfigLoadStatus.Invalid,
    "opening grant quantities reject zero and out-of-range values");
Check(ConfigSnapshotLoader.Parse("{\"neuralModuleTestPrice\":0}").Status ==
      ConfigLoadStatus.Invalid,
    "neural interface base price cannot be free");
Check(ConfigSnapshotLoader.Parse("{\"neuralModuleTestPrice\":725}").Status ==
      ConfigLoadStatus.Invalid,
    "neural interface base price stays at 100");
var legacyPriceRun = RunStateCodec.CreateNew("steam-demo-25382790",
    Guid.NewGuid().ToString("D"), 47, 1,
    new ConfigSnapshot { NeuralModuleTestPrice = 500 });
Check(RunStateCodec.Read(RunStateCodec.Write(legacyPriceRun)).Status ==
      ReadStatus.Ready &&
      NeuralModuleRules.TryPurchasePrice(legacyPriceRun with
      {
          NeuralModules = legacyPriceRun.NeuralModules with
          {
              Receipts = [new NeuralModuleReceipt(NeuralModuleRules.Opening,
                  Guid.NewGuid().ToString("D"), "legacy-module", 1,
                  "legacy-evidence")]
          }
      }, out long legacyNextPrice) && legacyNextPrice == 200,
    "schema-9 snapshots keep the fixed interface test price readable");
Check(ConfigSnapshotLoader.Parse("{\"initialCredits\":600,\"initialCredits\":700}").Status ==
      ConfigLoadStatus.Invalid, "duplicate configuration keys are rejected");
Check(ConfigSnapshotLoader.Parse("{\"unknownSetting\":1}").Status == ConfigLoadStatus.Invalid &&
      ConfigSnapshotLoader.Parse("{\"cyberneticPrices\":{\"unknownSetting\":1}}").Status ==
      ConfigLoadStatus.Invalid, "unknown root and nested settings are rejected");
Check(ConfigSnapshotLoader.Parse("{\"machineProfiles\":[{\"modelId\":\"fake\",\"effectiveAttributes\":[\"efficiency\"],\"completionKind\":\"day\",\"adapterEvidenceId\":\"fake\"}]}").Status ==
      ConfigLoadStatus.Invalid, "user configuration cannot claim unverified native machine adapters");
Check(ConfigSnapshotLoader.Parse(new string(' ', ConfigSnapshotLoader.MaxBytes + 1)).Status ==
      ConfigLoadStatus.Invalid, "oversized configuration is rejected");
Check(ConfigSnapshotLoader.ParseBytes(null).Status == ConfigLoadStatus.Defaults,
      "missing scoped configuration uses defaults");
Check(ConfigSnapshotLoader.ParseBytes(new byte[] { 0xff }).Status == ConfigLoadStatus.Invalid,
      "invalid UTF-8 configuration is rejected");
var first = RunStateCodec.CreateNew("steam-demo-25382790", gameRunId, 24, 1, new ConfigSnapshot());
Check(first.Health == 100 && first.Exposure == 20 &&
      first.LastSettledDay == 0 &&
      first.Machines.Connections.Length == 0,
    "initial health, exposure, and disconnected neural interface");
Check(first.ConfigSnapshot.InitialCredits == 500 && first.ConfigSnapshot.InitialRent == 250 &&
      first.ConfigSnapshot.InitialSecurityReputation == -15 &&
      first.ConfigSnapshot.InitialBatteryCount == 1 &&
      first.ConfigSnapshot.InitialMedicalCount == 1 &&
      first.ConfigSnapshot.InitialElectronicCount == 1,
    "opening economy and grant targets are persisted in the run configuration");
var openingConfig = first.ConfigSnapshot;
var syntheticCarrierEconomy = new OpeningEconomySnapshot(750, 200, 20, true);
var openingEconomy = OpeningEconomyRules.Plan(openingConfig, syntheticCarrierEconomy);
Check(openingEconomy is { Status: OpeningEconomyStatus.Ready, CashDelta: -100, CashTarget: 650,
      RentTarget: 250, SecurityReputationDelta: -15, SecurityReputationTarget: 5 },
    "opening economy preserves the saving perk's 150 credits and adjusts the native reputation baseline");
Check(OpeningEconomyRules.Plan(openingConfig, syntheticCarrierEconomy with
      { NativeReadVerified = false }).Status == OpeningEconomyStatus.Unverified,
    "opening economy refuses an unverified native snapshot");
Check(!OpeningEconomyRules.Matches(openingConfig, syntheticCarrierEconomy,
          openingEconomy.SecurityReputationTarget, openingEconomy.CashTarget) &&
      OpeningEconomyRules.Matches(openingConfig,
          new OpeningEconomySnapshot(650, 250, 5, true),
          openingEconomy.SecurityReputationTarget, openingEconomy.CashTarget),
    "opening economy completion requires all three native targets");
Check(OpeningEconomyRules.Plan(openingConfig with { InitialCredits = -1 },
      syntheticCarrierEconomy).Status == OpeningEconomyStatus.InvalidConfig,
    "opening economy rejects an invalid target");
Check(OpeningEconomyRules.Plan(openingConfig,
      new OpeningEconomySnapshot(int.MinValue, 200, 0, true)).Status ==
      OpeningEconomyStatus.DeltaOverflow,
    "opening economy rejects a cash target outside the native integer range");
var openingPort = new FakeOpeningEconomyPort(syntheticCarrierEconomy);
var openingApplied = OpeningEconomyTransaction.Apply(openingPort, openingConfig);
Check(openingApplied.Status == OpeningEconomyApplyStatus.Applied &&
      OpeningEconomyRules.Matches(openingConfig, openingApplied) &&
      openingPort.Snapshot?.Credits == 650 &&
      openingPort.Snapshot?.SecurityReputation == 5 &&
      openingApplied.CashAttempted && openingApplied.RentAttempted &&
      openingApplied.ReputationAttempted,
    "opening economy adapter applies and rereads all three native targets");
Check(OpeningEconomyTransaction.Apply(openingPort,
          openingConfig with { InitialCredits = 600, InitialSecurityReputation = 0 }).Status ==
      OpeningEconomyApplyStatus.AlreadyMatched && openingPort.Writes == 3,
    "a zero reputation adjustment leaves an otherwise matched native economy untouched");
foreach (int baseline in new[] { 0, -10, -15, -99 })
{
    var reputationPort = new FakeOpeningEconomyPort(new(500, 250, baseline, true));
    var reputationResult = OpeningEconomyTransaction.Apply(reputationPort,
        openingConfig with { InitialCredits = 600 });
    Check(reputationResult.Status == OpeningEconomyApplyStatus.Applied &&
          reputationResult.SecurityReputationTarget == baseline - 15 &&
          reputationPort.Snapshot?.SecurityReputation == baseline - 15 &&
          reputationPort.Writes == 1,
        $"opening subtracts fifteen from native reputation {baseline}, even when it already equals minus fifteen");
}
Check(OpeningEconomyRules.Plan(openingConfig,
          new(500, 250, -990, true)).Status == OpeningEconomyStatus.DeltaOverflow,
    "opening rejects a reputation target outside the saved native range");
var partialOpeningPort = new FakeOpeningEconomyPort(syntheticCarrierEconomy)
    { ThrowAfterCash = true };
var partialOpening = OpeningEconomyTransaction.Apply(partialOpeningPort, openingConfig);
Check(partialOpening.Status == OpeningEconomyApplyStatus.Partial &&
      partialOpening.CashAttempted && !partialOpening.RentAttempted &&
      partialOpening.After is { Credits: 650, Rent: 200 } &&
      partialOpeningPort.Writes == 1,
    "opening economy records partial native mutation without retry or rollback");
var indeterminateOpeningPort = new FakeOpeningEconomyPort(syntheticCarrierEconomy)
    { LoseReadAfterCash = true };
Check(OpeningEconomyTransaction.Apply(indeterminateOpeningPort, openingConfig).Status ==
      OpeningEconomyApplyStatus.Indeterminate && indeterminateOpeningPort.Writes == 1,
    "opening economy stops when a native reread becomes unavailable");
var sideEffectOpeningPort = new FakeOpeningEconomyPort(syntheticCarrierEconomy)
    { SecurityChangeOnRent = -3 };
Check(OpeningEconomyTransaction.Apply(sideEffectOpeningPort, openingConfig).Status ==
      OpeningEconomyApplyStatus.Applied &&
      sideEffectOpeningPort.Snapshot?.SecurityReputation == 2,
    "opening reputation adjustment preserves an earlier native side effect");
var openingGrant = new OpeningGrantRequest(Guid.NewGuid().ToString("D"),
    "verified_battery_id", 2, 0);
var fullGrantPort = new FakeOpeningGrantPort { SpaceLeft = 2 };
var fullGrant = OpeningGrantTransaction.Apply(fullGrantPort, openingGrant);
Check(fullGrant.Status == OpeningGrantStatus.Applied && fullGrant.OwnedAfter == 2 &&
      fullGrant.GrantCalls == 2 && fullGrantPort.Owned == 2,
    "opening grant confirms every transferred unit in the owned destination");
Check(OpeningGrantTransaction.Apply(fullGrantPort, openingGrant).Status ==
      OpeningGrantStatus.SnapshotConflict && fullGrantPort.GrantCalls == 2,
    "opening grant rejects a replay against a changed owned-unit snapshot");
Check(OpeningGrantTransaction.Apply(new FakeOpeningGrantPort(), openingGrant).Status ==
      OpeningGrantStatus.NoSpace,
    "opening grant refuses mutation when the first unit has no valid slot");
var failedPrecheckPort = new FakeOpeningGrantPort
    { SpaceLeft = 2, ThrowOnPrecheck = true };
Check(OpeningGrantTransaction.Apply(failedPrecheckPort, openingGrant).Status ==
      OpeningGrantStatus.Unavailable && failedPrecheckPort.GrantCalls == 0,
    "opening grant reports an unavailable precheck without claiming a partial transfer");
var oneSlotPort = new FakeOpeningGrantPort { SpaceLeft = 1 };
var oneSlotGrant = OpeningGrantTransaction.Apply(oneSlotPort, openingGrant);
Check(oneSlotGrant.Status == OpeningGrantStatus.Partial &&
      oneSlotGrant.OwnedAfter == 1 && oneSlotGrant.GrantCalls == 1,
    "opening grant reports exact partial delivery when later capacity is missing");
var throwingGrantPort = new FakeOpeningGrantPort { SpaceLeft = 2, ThrowAfterGrant = true };
Check(OpeningGrantTransaction.Apply(throwingGrantPort, openingGrant).Status ==
      OpeningGrantStatus.Partial && throwingGrantPort.Owned == 1 &&
      throwingGrantPort.GrantCalls == 1,
    "opening grant rereads ownership after a native transfer throws");
var unreadableGrantPort = new FakeOpeningGrantPort
    { SpaceLeft = 2, LoseReadAfterGrant = true };
Check(OpeningGrantTransaction.Apply(unreadableGrantPort, openingGrant).Status ==
      OpeningGrantStatus.Indeterminate && unreadableGrantPort.GrantCalls == 1,
    "opening grant freezes when post-transfer ownership cannot be read");
Check(OpeningGrantTransaction.Apply(fullGrantPort,
      openingGrant with { Units = 0 }).Status == OpeningGrantStatus.InvalidRequest &&
      fullGrantPort.GrantCalls == 2,
    "opening grant rejects invalid quantities before invoking the native port");
var openingModule = new OpeningModuleIdentityItem(NeuralModuleRules.ItemId,
    80, Units: 1, Owned: true, Value: 100, Width: 1, Height: 1);
var openingWageBox = new OpeningModuleIdentityItem("custom_storage_box", 0);
var openingVoidStorage = new OpeningModuleIdentityItem("void_bead_storage", 0);
Check(OpeningModuleIdentityRules.FindModule([openingWageBox, openingModule], 80) == 80,
    "Wage's zero-ID gift box does not block our opening module");
Check(OpeningModuleIdentityRules.FindModule(
          [openingWageBox, openingVoidStorage, openingModule], 80) == 80,
    "additional Luck Scout zero-ID storage does not stop initialization");
Check(OpeningModuleIdentityRules.FindModule(
          [openingModule, openingVoidStorage, openingWageBox], 80) == 80,
    "foreign gift order does not change our module proof");
Check(OpeningModuleIdentityRules.FindModule(
          [new("unknown_gift", 0), new("another_gift", -1), openingModule], 80) == 80,
    "foreign item numbering is outside our opening validation");
Check(OpeningModuleIdentityRules.FindModule(
          [new("foreign_a", 44), openingModule, new("foreign_b", 44)], 80) == 80,
    "duplicate foreign IDs do not affect a distinct opening module");
Check(OpeningModuleIdentityRules.FindModule(
          [new("foreign", 80), openingModule], 80) == null &&
      OpeningModuleIdentityRules.FindModule(
          [openingModule, new("foreign", 80)], 80) == null,
    "our module identity must remain unambiguous in either inventory order");
Check(OpeningModuleIdentityRules.FindModule(
          [openingWageBox, openingModule with { UniqueId = 0 }], 0) == null &&
      OpeningModuleIdentityRules.FindModule(
          [openingModule with { UniqueId = -1 }], -1) == null,
    "our own module still requires a positive native ID");
Check(OpeningModuleIdentityRules.FindModule(
          [openingModule with { Owned = false }], 80) == null,
    "an unowned module cannot prove our grant");
Check(OpeningModuleIdentityRules.FindModule(
          [openingModule with { Units = 2 }], 80) == null,
    "stacked modules cannot prove the single opening grant");
Check(OpeningModuleIdentityRules.FindModule(
          [openingModule with { Value = 101 }], 80) == null &&
      OpeningModuleIdentityRules.FindModule(
          [openingModule with { Value = (1L << 32) + 100 }], 80) == null,
    "opening module value is verified without narrowing native Int64 values");
Check(OpeningModuleIdentityRules.FindModule(
          [openingModule with { Width = 2 }], 80) == null &&
      OpeningModuleIdentityRules.FindModule(
          [openingModule with { Height = 0 }], 80) == null,
    "our opening module must retain its one-by-one shape");
Check(OpeningModuleIdentityRules.FindModule(
          [openingModule with { UniqueId = 81 }, openingModule], 80) == 80 &&
      OpeningModuleIdentityRules.FindModule(
          [openingModule with { UniqueId = 81 }], 80) == null,
    "a perk's module can coexist with the opening module but cannot prove its delivery");
Check(OpeningModuleIdentityRules.FindModule(
          [openingWageBox, openingVoidStorage], 80) == null,
    "foreign gifts cannot stand in for our missing module");
Check(OpeningModuleIdentityRules.FindModule([openingModule, null], 80) == null,
    "unreadable inventory cannot establish a complete identity proof");
bool invalidOpeningConfigRejected = false;
try
{
    RunStateCodec.CreateNew("steam-demo-25382790", gameRunId, 24, 1,
        openingConfig with { InitialRent = -1 });
}
catch (InvalidOperationException) { invalidOpeningConfigRejected = true; }
Check(invalidOpeningConfigRejected, "invalid opening economy cannot enter a new run snapshot");
Check(first.GameRunId == gameRunId && first.SaveSlotId == 24, "native run and slot binding");
var encoded = RunStateCodec.Write(first);
var decoded = RunStateCodec.Read(encoded);
Check(decoded.Status == ReadStatus.Ready && decoded.State?.RunId == first.RunId, "round trip");
var retiredStartId = JsonNode.Parse(encoded)!.AsObject();
retiredStartId["startId"] = "nicokobo.retired.mechanical_ascension.start";
Check(RunStateCodec.Read(retiredStartId.ToJsonString()).Status == ReadStatus.Corrupt,
    "previous start ID is rejected without reinitializing the old save");
string retiredBaseline = "";
foreach (int retiredSchema in new[] { 5, 6, 7 })
{
    var retiredState = JsonNode.Parse(encoded)!.AsObject();
    retiredState["schemaVersion"] = retiredSchema;
    retiredBaseline = retiredState.ToJsonString();
    Check(RunStateCodec.Read(retiredBaseline).Status == ReadStatus.LegacySchema,
        $"schema-{retiredSchema} reports a retired schema instead of migrating");
}
Check(RunStateCodec.ReadForGame(encoded, gameRunId, 24).Status == ReadStatus.Ready, "matching game run");
Check(RunStateCodec.ReadForGame(encoded, Guid.NewGuid().ToString("D"), 24).Status == ReadStatus.RunMismatch, "different game run");
Check(RunStateCodec.ReadForGame(encoded, gameRunId, 23).Status == ReadStatus.RunMismatch, "different save slot");
Check(RunStateCodec.Read(null).Status == ReadStatus.Missing, "missing is not a new run");
Check(RunStateCodec.Read("{").Status == ReadStatus.Corrupt, "malformed JSON");
Check(RunStateCodec.Read("{\"schemaVersion\":99}").Status == ReadStatus.FutureVersion, "future schema");
foreach (string missingReceiptPhase in new[] { "applying", "needsRecovery", "complete" })
{
    var forgedOpening = JsonNode.Parse(encoded)!;
    forgedOpening["initPhase"] = missingReceiptPhase;
    Check(RunStateCodec.Read(forgedOpening.ToJsonString()).Status == ReadStatus.Corrupt,
        $"{missingReceiptPhase} without opening evidence is corrupt on load");
}
var obsoleteOpeningSchema = JsonNode.Parse(encoded)!;
obsoleteOpeningSchema["schemaVersion"] = 1;
Check(RunStateCodec.Read(obsoleteOpeningSchema.ToJsonString()).Status == ReadStatus.LegacySchema,
    "old opening schema reports a retired schema without migrating");

var writeBinding = new BoundStart(Guid.NewGuid().ToString("D"), 1, gameRunId, 24);
var newWrite = ModDataWriteGate.New(false, null, writeBinding, first, gameRunId, 24);
Check(newWrite.Status == WriteGateStatus.Ready && newWrite.Plan?.ExpectedJson == null &&
      newWrite.Plan?.NextJson == encoded, "new state can stage only on an empty Mod key");
Check(ModDataWriteGate.New(true, encoded, writeBinding, first, gameRunId, 24).Status == WriteGateStatus.ExistingState,
    "new state cannot overwrite an existing Mod key");
Check(ModDataWriteGate.New(true, null, writeBinding, first, gameRunId, 24).Status == WriteGateStatus.ExistingState,
    "null value in an existing key is not an empty Mod slot");
Check(ModDataWriteGate.New(false, encoded, writeBinding, first, gameRunId, 24).Status ==
      WriteGateStatus.InvalidState, "inconsistent key presence cannot stage a new run");
Check(ModDataWriteGate.New(false, null, writeBinding, first, gameRunId, 25).Status == WriteGateStatus.IdentityMismatch,
    "new state cannot write into another native slot");
Check(ModDataWriteGate.New(false, null, writeBinding, first with { Revision = 1 }, gameRunId, 24).Status ==
      WriteGateStatus.InvalidTransition, "new state must begin at revision zero");
Check(ModDataWriteGate.Update(true, encoded, first, first with { Revision = 2 }, gameRunId, 24).Status ==
      WriteGateStatus.InvalidTransition, "update cannot skip revisions");
Check(ModDataWriteGate.Update(true, encoded, first,
      first with { Revision = 1 }, gameRunId, 24).Status ==
      WriteGateStatus.InvalidTransition,
    "revision-only opening update cannot strand a new Mod run");
Check(RunStateCodec.Read(InvalidHistory(first, json =>
      json["revision"] = 1).ToJsonString()).Status == ReadStatus.Corrupt,
    "pending opening without a journal exists only at initial revision zero");
Check(ModDataWriteGate.Update(true, encoded, first,
      first with { Revision = 1, InitPhase = "complete" }, gameRunId, 24).Status ==
      WriteGateStatus.InvalidTransition, "opening cannot complete without a persisted operation");

// Retain the previous six-grant fixture to cover existing saved journals.
var openingOperationId = Guid.NewGuid().ToString("D");
OpeningGrantRecord[] openingManifest =
[
    new("neuralModule", new(openingOperationId, NeuralModuleRules.ItemId, 1, 0)),
    new("materialRefiner", new(openingOperationId, OpeningJournalRules.MaterialRefinerId, 1, 0)),
    new("battery", new(openingOperationId, "energy_credit", 1, 0)),
    new("medical", new(openingOperationId, "topical_bandage_item", 1, 1)),
    new("electronic", new(openingOperationId, "common_electronic", 1, 0)),
    new("oxymore", new(openingOperationId, "pink_injector", 2, 0))
];
var verifiedOpeningSnapshot = new OpeningNativeSnapshot(gameRunId, 24,
    first.StartDay, true, true, openingManifest.Select(x =>
        new OpeningItemSnapshot(x.Kind, x.Request.ItemId, x.Request.Units,
            x.Request.ExpectedOwnedBefore, true, true)).ToArray());
var preparedOpeningPlan = OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot,
    true, encoded);
Check(preparedOpeningPlan is { Status: OpeningSavePlanStatus.Ready,
      State.Opening.Phase: OpeningJournalRules.Prepared } &&
      preparedOpeningPlan.Write?.ExpectedJson == encoded &&
      RunStateCodec.Read(preparedOpeningPlan.Write.NextJson).Status == ReadStatus.Ready &&
      first.Opening == null && first.Revision == 0,
    "verified inventory snapshot plans a prepared opening without changing the source state");
var productionOpeningSnapshot = verifiedOpeningSnapshot with
{
    Items = [verifiedOpeningSnapshot.Items[0] with { OwnedBefore = 1 },
        verifiedOpeningSnapshot.Items[1] with { OwnedBefore = 1 },
        verifiedOpeningSnapshot.Items[2] with { OwnedBefore = 2 },
        verifiedOpeningSnapshot.Items[3],
        verifiedOpeningSnapshot.Items[4] with { OwnedBefore = 3 },
        new("scrapMetal", "scrap_metal", AugNumbers.OpeningItems.ScrapMetalCount, 2, true, true),
        new("materialSign", "sign_material", AugNumbers.OpeningItems.MaterialSignCount, 1, true, true)]
};
var productionOpeningPlan = OpeningSavePlanner.Prepare(first,
    productionOpeningSnapshot, true, encoded);
Check(productionOpeningPlan is { Status: OpeningSavePlanStatus.Ready, State: not null } &&
      productionOpeningPlan.State.Opening!.Grants.Length == 7,
    "production opening retains overlapping perk items as its seven-grant baseline");
Check(OpeningSavePlanner.Prepare(first, productionOpeningSnapshot with
      { Items = [.. productionOpeningSnapshot.Items.Take(5),
          productionOpeningSnapshot.Items[5] with { Units = 9 },
          productionOpeningSnapshot.Items[6]] }, true, encoded).Status ==
      OpeningSavePlanStatus.InvalidManifest,
    "production opening rejects a short scrap grant");
var productionOpeningState = productionOpeningPlan.State!;
string productionOpeningOperation = productionOpeningState.Opening!.OperationId;
productionOpeningState = OpeningProgressRules.BeginApplying(productionOpeningState,
    productionOpeningOperation, productionOpeningState.Revision, true).State!;
productionOpeningState = OpeningProgressRules.RecordEconomy(productionOpeningState,
    productionOpeningOperation, productionOpeningState.Revision,
    openingApplied).State!;
foreach (var grant in productionOpeningState.Opening!.Grants)
{
    var request = grant.Request;
    var result = new OpeningGrantResult(OpeningGrantStatus.Applied,
        request.ExpectedOwnedBefore, request.ExpectedOwnedBefore + request.Units,
        request.Units);
    if (grant.Kind == "neuralModule")
        result = result with
        {
            NativeItemInstanceId = "production-opening-module",
            OwnershipEvidenceId = "production-owned-opening-module"
        };
    var progress = OpeningProgressRules.RecordGrant(productionOpeningState,
        request, productionOpeningState.Revision, result);
    Check(progress is { Status: OpeningProgressStatus.Advanced, State: not null } &&
          ModDataWriteGate.Update(true, RunStateCodec.Write(productionOpeningState),
              productionOpeningState, progress.State, gameRunId, 24).Status == WriteGateStatus.Ready,
        $"production opening stages the {grant.Kind} grant once");
    productionOpeningState = progress.State!;
}
var productionOpeningCommit = OpeningProgressRules.PrepareCommit(productionOpeningState,
    productionOpeningOperation, productionOpeningState.Revision, true, true);
var productionOpeningReload = RunStateCodec.Read(RunStateCodec.Write(productionOpeningCommit.State!));
var productionOpeningRoot = Es3SlotReadback.CandidateOpeningRootUnits(
    productionOpeningReload.State, first.StartDay);
Check(productionOpeningCommit is { Status: OpeningProgressStatus.Advanced, State.InitPhase: "complete" } &&
      productionOpeningReload.Status == ReadStatus.Ready &&
      productionOpeningReload.State?.Opening?.Economy?.After?.Credits == 650 &&
      productionOpeningRoot is not null &&
      productionOpeningRoot[NeuralModuleRules.ItemId] == 2 &&
      productionOpeningRoot[OpeningJournalRules.MaterialRefinerId] == 2 &&
      productionOpeningRoot["energy_credit"] == 3 &&
      productionOpeningRoot["common_electronic"] == 4 &&
      productionOpeningRoot["scrap_metal"] == 12 &&
      productionOpeningRoot["sign_material"] == 2 &&
      !productionOpeningRoot.ContainsKey("pink_injector"),
    "production opening commit and codec reload preserve perk items plus the full opening grant");
var basicBandageOpeningPlan = OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot with
      { Items = [.. verifiedOpeningSnapshot.Items.Take(3),
          verifiedOpeningSnapshot.Items[3] with { OwnedBefore = 0 },
          .. verifiedOpeningSnapshot.Items.Skip(4)] }, true, encoded);
Check(basicBandageOpeningPlan is { Status: OpeningSavePlanStatus.Ready,
          State.Opening.Phase: OpeningJournalRules.Prepared } &&
      basicBandageOpeningPlan.State.Opening.Grants[3].Request.ExpectedOwnedBefore == 0 &&
      RunStateCodec.Read(basicBandageOpeningPlan.Write!.NextJson).Status == ReadStatus.Ready,
    "a native start with only a basic bandage can persist its opening without a pre-existing topical bandage");
var narcoticOpeningSnapshot = verifiedOpeningSnapshot with
{
    Items = [.. verifiedOpeningSnapshot.Items.Take(5),
        verifiedOpeningSnapshot.Items[5] with { OwnedBefore = 1 }]
};
var narcoticOpeningPlan = OpeningSavePlanner.Prepare(first, narcoticOpeningSnapshot,
    true, encoded);
Check(narcoticOpeningPlan is { Status: OpeningSavePlanStatus.Ready,
          State.Opening.Phase: OpeningJournalRules.Prepared } &&
      narcoticOpeningPlan.State.Opening.Grants[5].Request.ExpectedOwnedBefore == 1,
    "the narcotic-addiction perk's native injector is retained as the opening baseline");
foreach (int invalidCount in new[] { -1, int.MaxValue })
    Check(OpeningSavePlanner.Prepare(first, narcoticOpeningSnapshot with
          { Items = [.. narcoticOpeningSnapshot.Items.Take(5),
              narcoticOpeningSnapshot.Items[5] with { OwnedBefore = invalidCount }] },
          true, encoded).Status == OpeningSavePlanStatus.InvalidManifest,
        $"opening rejects invalid observed injector counts: {invalidCount}");
foreach (int invalidCount in new[] { -1, int.MaxValue })
    Check(OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot with
          { Items = [.. verifiedOpeningSnapshot.Items.Take(3),
              verifiedOpeningSnapshot.Items[3] with { OwnedBefore = invalidCount },
              .. verifiedOpeningSnapshot.Items.Skip(4)] }, true, encoded).Status ==
          OpeningSavePlanStatus.InvalidManifest,
        $"opening rejects invalid observed medical counts: {invalidCount}");
Check(OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot with
      { Items = [.. verifiedOpeningSnapshot.Items.Take(5),
          verifiedOpeningSnapshot.Items[5] with { Units = 1 }] },
      true, encoded).Status == OpeningSavePlanStatus.InvalidManifest,
    "opening refuses a one-injector manifest");
Check(OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot with
      { Items = [.. verifiedOpeningSnapshot.Items.Take(2),
          verifiedOpeningSnapshot.Items[2] with { Units = 2 },
          .. verifiedOpeningSnapshot.Items.Skip(3)] }, true, encoded).Status ==
      OpeningSavePlanStatus.InvalidManifest,
    "opening refuses a second battery");
Check(ModDataWriteGate.RecheckPreparedOpening(preparedOpeningPlan,
      true, encoded, gameRunId, 24).Status == WriteGateStatus.Ready,
    "prepared opening write is rechecked against the exact native slot and Mod JSON");
Check(ModDataWriteGate.Update(true, retiredBaseline,
      preparedOpeningPlan.Previous!, preparedOpeningPlan.State!,
      gameRunId, 24).Status == WriteGateStatus.ContentConflict,
    "a retired-schema baseline can never be rewritten into the current format");
Check(ModDataWriteGate.RecheckPreparedOpening(preparedOpeningPlan,
      true, encoded, gameRunId, 25).Status == WriteGateStatus.IdentityMismatch,
    "prepared opening write cannot move to another native slot");
Check(ModDataWriteGate.RecheckPreparedOpening(preparedOpeningPlan,
      true, encoded + " ", gameRunId, 24).Status == WriteGateStatus.ContentConflict,
    "prepared opening write rejects an intervening Mod JSON change");
Check(ModDataWriteGate.RecheckPreparedOpening(preparedOpeningPlan with
      { Write = preparedOpeningPlan.Write! with { NextJson = "changed" } },
      true, encoded, gameRunId, 24).Status == WriteGateStatus.ContentConflict,
    "prepared opening write rejects a changed precomputed payload");
Check(OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot with
      { InitialStateReady = false }, true, encoded).Status ==
      OpeningSavePlanStatus.UnverifiedNative,
    "opening refuses an initial save without durable evidence");
Check(OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot with
      { GameRunId = Guid.NewGuid().ToString("D") }, true, encoded).Status ==
      OpeningSavePlanStatus.IdentityMismatch,
    "opening preparation cannot bind another native run");
Check(OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot with
      { Items = [verifiedOpeningSnapshot.Items[0] with { BlueprintVerified = false },
          .. verifiedOpeningSnapshot.Items.Skip(1)] }, true, encoded).Status ==
      OpeningSavePlanStatus.UnverifiedNative,
    "opening preparation requires every blueprint to be verified");
Check(OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot with
      { Items = [.. verifiedOpeningSnapshot.Items.Take(4)] }, true, encoded).Status ==
      OpeningSavePlanStatus.InvalidManifest,
    "opening preparation requires the complete six-item manifest");
Check(OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot,
      true, encoded + " ").GateStatus == WriteGateStatus.ContentConflict,
    "opening preparation cannot stage against changed Mod JSON");
Check(OpeningSavePlanner.Prepare(first, verifiedOpeningSnapshot,
      false, null).GateStatus == WriteGateStatus.MissingState,
    "opening preparation cannot silently recreate a missing initial state");
Check(OpeningSavePlanner.Prepare(first with { Revision = 1 },
      verifiedOpeningSnapshot, true, encoded).Status == OpeningSavePlanStatus.InvalidState,
    "opening preparation rejects a revision without a journal");
var openingPrepared = first with
{
    Revision = 1,
    InitPhase = "applying",
    Opening = new(openingOperationId, gameRunId, 24, first.StartDay,
        OpeningJournalRules.Prepared, openingManifest)
};
var updateWrite = ModDataWriteGate.Update(true, encoded, first,
    openingPrepared, gameRunId, 24);
Check(updateWrite.Status == WriteGateStatus.Ready &&
      updateWrite.Plan?.ExpectedJson == encoded &&
      RunStateCodec.Read(updateWrite.Plan!.NextJson).State?.Opening?.Phase ==
          OpeningJournalRules.Prepared,
    "one-revision update stages a validated prepared opening");
Check(ModDataWriteGate.Update(false, null, first, openingPrepared,
      gameRunId, 24).Status == WriteGateStatus.MissingState &&
      ModDataWriteGate.Update(true, null, first, openingPrepared,
          gameRunId, 24).Status == WriteGateStatus.InvalidState,
    "opening update cannot recreate a missing or null Mod key");
Check(ModDataWriteGate.Update(true, encoded + " ", first, openingPrepared,
      gameRunId, 24).Status == WriteGateStatus.ContentConflict &&
      ModDataWriteGate.Update(true, updateWrite.Plan!.NextJson, first,
          openingPrepared, gameRunId, 24).Status == WriteGateStatus.ContentConflict,
    "prepared opening rejects changed or already-advanced raw JSON");
Check(ModDataWriteGate.Update(true, encoded, first, openingPrepared,
      Guid.NewGuid().ToString("D"), 24).Status == WriteGateStatus.IdentityMismatch &&
      ModDataWriteGate.Update(true, encoded, first,
          openingPrepared with { GameRunId = Guid.NewGuid().ToString("D") },
          gameRunId, 24).Status == WriteGateStatus.IdentityMismatch &&
      ModDataWriteGate.Update(true, encoded, first,
          openingPrepared with { ConfigHash = "bad" }, gameRunId, 24).Status ==
          WriteGateStatus.InvalidTransition,
    "opening update cannot cross native identity or replace its configuration");
Check(ModDataWriteGate.Update(true, encoded, first, openingPrepared, gameRunId, 24).Status ==
      WriteGateStatus.Ready, "prepared opening journal binds the native run and slot");
var openingPreparedJson = RunStateCodec.Write(openingPrepared);
var openingReloaded = RunStateCodec.Read(openingPreparedJson).State!;
Check(openingReloaded.Opening?.OperationId == openingOperationId &&
      openingReloaded.Opening.Grants.All(x => x.Result == null),
    "prepared grant intents survive JSON round trip without claiming delivery");
Check(ModDataWriteGate.Update(true, openingPreparedJson, openingReloaded,
      openingReloaded with { Revision = 2, InitPhase = "complete" }, gameRunId, 24).Status ==
      WriteGateStatus.InvalidTransition, "reload cannot treat prepared intents as completed grants");
Check(ModDataWriteGate.Update(true, openingPreparedJson, openingReloaded,
      openingReloaded with { Revision = 2, Opening = openingReloaded.Opening! with
          { OperationId = Guid.NewGuid().ToString("D") } }, gameRunId, 24).Status ==
      WriteGateStatus.InvalidTransition, "opening operation ID cannot be swapped");
Check(RunStateCodec.Read(openingPreparedJson.Replace("\"opening\":{\"operationId\":" +
      "\"" + openingOperationId + "\",\"gameRunId\":\"" + gameRunId + "\",\"saveSlotId\":24",
      "\"opening\":{\"operationId\":\"" + openingOperationId +
      "\",\"gameRunId\":\"" + gameRunId + "\",\"saveSlotId\":25")).Status ==
      ReadStatus.Corrupt, "opening journal cannot bind another native slot");

var journalApplying = openingReloaded with
{
    Revision = 2,
    Opening = openingReloaded.Opening! with { Phase = OpeningJournalRules.Applying }
};
Check(OpeningProgressRules.BeginApplying(openingReloaded,
      openingOperationId, openingReloaded.Revision, false).Status ==
      OpeningProgressStatus.UnverifiedBoundary,
    "prepared opening cannot execute before journal durability evidence");
var plannedApplying = OpeningProgressRules.BeginApplying(openingReloaded,
    openingOperationId, openingReloaded.Revision, true);
Check(plannedApplying is { Status: OpeningProgressStatus.Advanced,
      State: { Revision: 2, Opening.Phase: OpeningJournalRules.Applying } } &&
      ModDataWriteGate.Update(true, openingPreparedJson, openingReloaded,
          plannedApplying.State, gameRunId, 24).Status == WriteGateStatus.Ready,
    "durable prepared opening creates a writable applying intent");
Check(OpeningJournalRules.MayStartEconomyAttempt(journalApplying),
    "durable opening journal authorizes the first economy attempt");
Check(!OpeningJournalRules.MayStartGrantAttempt(journalApplying, openingManifest[0].Request),
    "inventory delivery waits for a verified economy result");
var economyObservedForGrant = journalApplying with
{
    Revision = 3,
    Opening = journalApplying.Opening! with
    {
        Economy = new(OpeningEconomyApplyStatus.Applied,
            new(600, 200, 0, true), new(500, 250, -15, true),
            true, true, true)
    }
};
Check(OpeningJournalRules.Valid(economyObservedForGrant) &&
      OpeningJournalRules.MayStartGrantAttempt(economyObservedForGrant,
          openingManifest[0].Request) &&
      !OpeningJournalRules.MayStartGrantAttempt(economyObservedForGrant,
          openingManifest[1].Request),
    "module is first and the material refiner cannot skip it");
Check(!OpeningJournalRules.Valid(openingPrepared with
      { Opening = openingPrepared.Opening! with
          { Grants = [openingManifest[1], openingManifest[0],
              .. openingManifest.Skip(2)] } }) &&
      !OpeningJournalRules.Valid(openingPrepared with
      { Opening = openingPrepared.Opening! with
          { Grants = [.. openingManifest.Take(5),
              openingManifest[5] with
              { Request = openingManifest[5].Request with { Units = 1 } }] } }),
    "opening requires the exact grant order and two Oxymore injectors");
Check(!OpeningJournalRules.Valid(openingPrepared with
      { Opening = openingPrepared.Opening! with
          { Grants = [.. openingManifest.Take(4),
              openingManifest[4] with
              { Request = openingManifest[4].Request with { ItemId = "test-tool" } },
              openingManifest[5]] } }),
    "obsolete tool cannot replace the electronic item");
var openingApplyingJson = RunStateCodec.Write(journalApplying);
var economyObservedJson = RunStateCodec.Write(economyObservedForGrant);
Check(!JsonNode.Parse(economyObservedJson)!["opening"]!["economy"]!.AsObject()
          .ContainsKey("securityReputationTarget") &&
      RunStateCodec.Read(economyObservedJson).Status == ReadStatus.Ready,
    "older fixed-reputation receipts keep their original JSON shape and remain readable");
var relativeEconomyObserved = OpeningProgressRules.RecordEconomy(journalApplying,
    openingOperationId, journalApplying.Revision, openingApplied);
Check(relativeEconomyObserved.Status == OpeningProgressStatus.Advanced,
    "relative opening reputation advances the saved opening journal");
var relativeEconomyState = relativeEconomyObserved.State!;
var economyReloaded = RunStateCodec.Read(RunStateCodec.Write(relativeEconomyState)).State;
Check(economyReloaded?.Opening?.Economy?.SecurityReputationTarget == 5 &&
      economyReloaded.Opening.Economy.After?.SecurityReputation == 5 &&
      !OpeningJournalRules.MayStartEconomyAttempt(relativeEconomyState) &&
      !OpeningJournalRules.MayStartEconomyAttempt(economyReloaded),
    "relative opening reputation survives save readback and cannot be applied again");
Check(!OpeningJournalRules.MayStartGrantAttempt(relativeEconomyState with
      { Opening = relativeEconomyState.Opening! with
          { Economy = openingApplied with { SecurityReputationTarget = -15 } } },
          openingManifest[0].Request),
    "opening journal rejects the old fixed target when the relative result is five");
Check(ModDataWriteGate.Update(true, openingApplyingJson, journalApplying,
      economyObservedForGrant, gameRunId, 24).Status == WriteGateStatus.Ready,
    "economy result advances one persisted revision");
var firstGrant = new OpeningGrantResult(OpeningGrantStatus.Applied, 0, 1, 1,
    NativeItemInstanceId: "test-opening-module",
    OwnershipEvidenceId: "test-owned-opening-module");
var moduleProgress = OpeningProgressRules.RecordGrant(economyObservedForGrant,
    openingManifest[0].Request, economyObservedForGrant.Revision, firstGrant);
Check(moduleProgress is { Status: OpeningProgressStatus.Advanced,
      State.NeuralModules.Receipts.Length: 1 } &&
      ModDataWriteGate.Update(true, economyObservedJson, economyObservedForGrant,
          moduleProgress.State, gameRunId, 24).Status == WriteGateStatus.Ready,
    "first grant records a verified module identity and ownership receipt");
Check(OpeningProgressRules.RecordGrant(economyObservedForGrant,
      openingManifest[0].Request, economyObservedForGrant.Revision,
      firstGrant with { NativeItemInstanceId = null }).Status ==
      OpeningProgressStatus.NeedsRecovery,
    "module without instance identity cannot be accepted");
Check(OpeningProgressRules.RecordGrant(economyObservedForGrant,
      openingManifest[0].Request, economyObservedForGrant.Revision,
      firstGrant, "obsolete-eye-tool").Status == OpeningProgressStatus.InvalidEvidence,
    "opening refuses an unexpected opening eye-tool identity");
Check(OpeningProgressRules.RecordGrant(economyObservedForGrant,
      openingManifest[0].Request, economyObservedForGrant.Revision,
      new(OpeningGrantStatus.NoSpace, 0, 0)).Status ==
      OpeningProgressStatus.NoJournalObservation,
    "no-space result does not invent a delivered module");
var partialGrant = OpeningProgressRules.RecordGrant(economyObservedForGrant,
    openingManifest[0].Request, economyObservedForGrant.Revision,
    new(OpeningGrantStatus.Partial, 0, 1, 1));
Check(partialGrant is { Status: OpeningProgressStatus.NeedsRecovery,
      State.InitPhase: "needsRecovery" } &&
      !OpeningJournalRules.MayStartGrantAttempt(partialGrant.State,
          openingManifest[0].Request),
    "partial delivery enters recovery without a second grant attempt");
RunState openingProgress = moduleProgress.State!;
string openingProgressJson = RunStateCodec.Write(openingProgress);
for (int grantIndex = 1; grantIndex < openingManifest.Length; grantIndex++)
{
    var request = openingManifest[grantIndex].Request;
    Check(OpeningJournalRules.MayStartGrantAttempt(openingProgress, request),
        $"ordered grant {grantIndex} is authorized exactly once");
    var result = new OpeningGrantResult(OpeningGrantStatus.Applied,
        request.ExpectedOwnedBefore,
        request.ExpectedOwnedBefore + request.Units, request.Units);
    var next = OpeningProgressRules.RecordGrant(openingProgress, request,
        openingProgress.Revision, result);
    Check(next.Status == OpeningProgressStatus.Advanced &&
          next.State != null &&
          ModDataWriteGate.Update(true, openingProgressJson, openingProgress,
              next.State, gameRunId, 24).Status == WriteGateStatus.Ready,
        $"grant {grantIndex} advances a single saved revision");
    openingProgress = next.State!;
    openingProgressJson = RunStateCodec.Write(openingProgress);
}
var journalApplied = openingProgress;
Check(journalApplied.Opening!.Phase == OpeningJournalRules.Applied &&
      journalApplied.Opening.Grants[3].Result?.OwnedAfter == 2 &&
      journalApplied.Opening.Grants[5].Result?.OwnedAfter == 2 &&
      journalApplied.EyeToolInstanceId == null,
    "native bandage is retained and two injectors are added without an eye tool");
var journalCommitted = journalApplied with
{
    Revision = journalApplied.Revision + 1,
    InitPhase = "complete",
    Opening = journalApplied.Opening with { Phase = OpeningJournalRules.Committed }
};
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(journalApplied), journalApplied,
      journalCommitted, gameRunId, 24).Status == WriteGateStatus.Ready &&
      RunStateCodec.Read(RunStateCodec.Write(journalCommitted)).Status == ReadStatus.Ready,
    "six verified grants and target economy survive commit and reload");
RunForkMigrationChecks.Run(journalCommitted, Check);
Check(OpeningProgressRules.PrepareCommit(journalApplied,
      openingOperationId, journalApplied.Revision, false, true).Status ==
      OpeningProgressStatus.UnverifiedBoundary &&
      OpeningProgressRules.PrepareCommit(journalApplied,
          openingOperationId, journalApplied.Revision, true, true).Status ==
      OpeningProgressStatus.Advanced,
    "commit requires a durable applied journal and native reconciliation");
var candidateRoot = Es3SlotReadback.CandidateOpeningRootUnits(journalCommitted,
    journalCommitted.StartDay);
Check(candidateRoot != null &&
      candidateRoot[OpeningJournalRules.MaterialRefinerId] == 1 &&
      !candidateRoot.ContainsKey("furnace") &&
      !candidateRoot.ContainsKey("security_alarm") &&
      candidateRoot["energy_credit"] == 1 &&
      candidateRoot["topical_bandage_item"] == 2 &&
      candidateRoot["pink_injector"] == 2 &&
      !candidateRoot.ContainsKey("test-eye-tool"),
    "saved-root expectation includes the material refiner, native bandage and two injectors");
var legacyFurnaceGrants = journalCommitted.Opening!.Grants.ToArray();
legacyFurnaceGrants[1] = legacyFurnaceGrants[1] with
{
    Kind = "furnace",
    Request = legacyFurnaceGrants[1].Request with { ItemId = "furnace" }
};
var legacyFurnaceCommitted = journalCommitted with
{
    Opening = journalCommitted.Opening with { Grants = legacyFurnaceGrants }
};
var legacyFurnaceJson = RunStateCodec.Write(legacyFurnaceCommitted);
var legacyFurnaceRead = RunStateCodec.Read(legacyFurnaceJson);
var legacyFurnaceRoot = legacyFurnaceRead.State is { } legacyFurnaceState
    ? Es3SlotReadback.CandidateOpeningRootUnits(legacyFurnaceState,
        legacyFurnaceCommitted.StartDay)
    : null;
Check(legacyFurnaceRead.Status == ReadStatus.Ready &&
      legacyFurnaceRead.State?.Opening?.Grants[1].Request.ItemId == "furnace" &&
      legacyFurnaceRoot != null && legacyFurnaceRoot["furnace"] == 1 &&
      !legacyFurnaceRoot.ContainsKey(OpeningJournalRules.MaterialRefinerId),
    "an existing furnace opening remains readable without rewriting its saved inventory expectation");
Check(ModDataWriteGate.Update(true, legacyFurnaceJson, legacyFurnaceCommitted,
      journalCommitted with { Revision = legacyFurnaceCommitted.Revision + 1 },
      gameRunId, 24).Status == WriteGateStatus.InvalidTransition,
    "a committed furnace grant cannot be replaced or reissued as a material refiner");
var legacyAlarmGrants = journalCommitted.Opening!.Grants.ToArray();
legacyAlarmGrants[1] = legacyAlarmGrants[1] with
{
    Kind = "alarm",
    Request = legacyAlarmGrants[1].Request with { ItemId = "security_alarm" }
};
var legacyAlarmCommitted = journalCommitted with
{
    Opening = journalCommitted.Opening with { Grants = legacyAlarmGrants }
};
var legacyAlarmJson = RunStateCodec.Write(legacyAlarmCommitted);
var legacyAlarmRead = RunStateCodec.Read(legacyAlarmJson);
var legacyAlarmRoot = legacyAlarmRead.State is { } legacyAlarmState
    ? Es3SlotReadback.CandidateOpeningRootUnits(legacyAlarmState,
        legacyAlarmCommitted.StartDay)
    : null;
Check(legacyAlarmRead.Status == ReadStatus.Ready &&
      legacyAlarmRead.State?.Opening?.Grants[1].Request.ItemId == "security_alarm" &&
      legacyAlarmRoot != null && legacyAlarmRoot["security_alarm"] == 1 &&
      !legacyAlarmRoot.ContainsKey("furnace") &&
      !legacyAlarmRoot.ContainsKey(OpeningJournalRules.MaterialRefinerId),
    "an existing alarm opening remains readable without rewriting its saved inventory expectation");
Check(ModDataWriteGate.Update(true, legacyAlarmJson, legacyAlarmCommitted,
      journalCommitted with { Revision = legacyAlarmCommitted.Revision + 1 },
      gameRunId, 24).Status == WriteGateStatus.InvalidTransition,
    "a committed alarm grant cannot be replaced or reissued as a material refiner");
Check(!OpeningJournalRules.Valid(journalCommitted with
      { Opening = journalCommitted.Opening with
          { Grants = [journalCommitted.Opening.Grants[0],
              journalCommitted.Opening.Grants[1] with { Kind = "alarm" },
              .. journalCommitted.Opening.Grants.Skip(2)] } }),
    "opening item identity must match its recorded material refiner, furnace or alarm kind");
var basicBandageGrants = journalCommitted.Opening!.Grants.ToArray();
basicBandageGrants[3] = basicBandageGrants[3] with
{
    Request = basicBandageGrants[3].Request with { ExpectedOwnedBefore = 0 },
    Result = basicBandageGrants[3].Result! with { OwnedBefore = 0, OwnedAfter = 1 }
};
var basicBandageCommitted = journalCommitted with
{
    Opening = journalCommitted.Opening with { Grants = basicBandageGrants }
};
Check(RunStateCodec.Read(RunStateCodec.Write(basicBandageCommitted)).Status == ReadStatus.Ready &&
      Es3SlotReadback.CandidateOpeningRootUnits(basicBandageCommitted,
          basicBandageCommitted.StartDay)?["topical_bandage_item"] == 1,
    "committing the basic-bandage native start expects only the one newly granted topical bandage");
var narcoticProgress = narcoticOpeningPlan.State!;
string narcoticOperationId = narcoticProgress.Opening!.OperationId;
narcoticProgress = OpeningProgressRules.BeginApplying(narcoticProgress,
    narcoticOperationId, narcoticProgress.Revision, true).State!;
narcoticProgress = OpeningProgressRules.RecordEconomy(narcoticProgress,
    narcoticOperationId, narcoticProgress.Revision,
    economyObservedForGrant.Opening!.Economy!).State!;
foreach (var grant in narcoticProgress.Opening!.Grants)
{
    var request = grant.Request;
    var port = new FakeOpeningGrantPort
        { Owned = request.ExpectedOwnedBefore, SpaceLeft = request.Units };
    var result = OpeningGrantTransaction.Apply(port, request);
    if (grant.Kind == "neuralModule")
        result = result with
        {
            NativeItemInstanceId = "narcotic-opening-module",
            OwnershipEvidenceId = "narcotic-owned-opening-module"
        };
    var progress = OpeningProgressRules.RecordGrant(narcoticProgress,
        request, narcoticProgress.Revision, result);
    Check(progress is { Status: OpeningProgressStatus.Advanced, State: not null } &&
          ModDataWriteGate.Update(true, RunStateCodec.Write(narcoticProgress),
              narcoticProgress, progress.State, gameRunId, 24).Status == WriteGateStatus.Ready,
        $"narcotic-addiction opening persists the {grant.Kind} grant");
    narcoticProgress = progress.State!;
    if (grant.Kind == "oxymore")
        Check(result.OwnedBefore == 1 && result.OwnedAfter == 3 && port.GrantCalls == 2 &&
              OpeningGrantTransaction.Apply(port, request).Status ==
                  OpeningGrantStatus.SnapshotConflict && port.GrantCalls == 2 &&
              !OpeningJournalRules.MayStartGrantAttempt(narcoticProgress, request),
            "narcotic-addiction opening adds two injectors once while retaining the native one");
}
var narcoticCommit = OpeningProgressRules.PrepareCommit(narcoticProgress,
    narcoticOperationId, narcoticProgress.Revision, true, true);
Check(narcoticCommit is { Status: OpeningProgressStatus.Advanced, State.InitPhase: "complete" } &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(narcoticProgress),
          narcoticProgress, narcoticCommit.State, gameRunId, 24).Status == WriteGateStatus.Ready,
    "narcotic-addiction opening reaches the saved complete phase");
var narcoticReload = RunStateCodec.Read(RunStateCodec.Write(narcoticCommit.State!));
Check(narcoticReload.Status == ReadStatus.Ready &&
      Es3SlotReadback.CandidateOpeningRootUnits(narcoticReload.State,
          first.StartDay)?["pink_injector"] == 3 &&
      StatusPanelRules.Build(narcoticReload.State!, first.StartDay, false) is
          { Current: true } narcoticStatus &&
      narcoticStatus.Lines.Any(x => x.Contains("健康 100/100")) &&
      narcoticStatus.Lines.Any(x => x.Contains("暴露 20/100")),
    "narcotic-addiction opening survives reload with health and exposure ready to display");
var narcoticNextDay = SurvivalRules.ReconcileDay(narcoticReload.State!, first.StartDay + 1);
Check(narcoticNextDay.Status == SettleStatus.Applied &&
      narcoticNextDay.State.Health == 100 - first.ConfigSnapshot.DailyHealthLoss &&
      narcoticNextDay.State.Exposure == 20 + first.ConfigSnapshot.DailyExposureGain &&
      StatusPanelRules.Build(narcoticNextDay.State, first.StartDay + 1, false).Current &&
      SurvivalRules.ReconcileDay(narcoticNextDay.State, first.StartDay + 1).Status !=
          SettleStatus.Applied,
    "narcotic-addiction opening enables next-day vitals and prevents duplicate settlement");
Check(Es3SlotReadback.CandidateOpeningRootUnits(journalCommitted,
      journalCommitted.StartDay + 1) == null,
    "later days cannot reuse first-opening inventory expectations");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(journalApplied), journalApplied,
      journalCommitted with { Opening = journalCommitted.Opening! with
          { Grants = journalCommitted.Opening.Grants.Take(5).ToArray() } },
      gameRunId, 24).Status == WriteGateStatus.InvalidTransition,
    "opening cannot commit without the Oxymore receipt");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(journalApplied), journalApplied,
      journalCommitted with { Health = journalCommitted.Health - 1 },
      gameRunId, 24).Status == WriteGateStatus.InvalidTransition,
    "commit cannot hide a survival-attribute change");
var tampered = encoded.Replace("\"initialHealth\":100", "\"initialHealth\":99");
Check(RunStateCodec.Read(tampered).Status == ReadStatus.Corrupt, "snapshot hash mismatch");

Check(SurvivalRules.SettleDay(first, 1).Status == SettleStatus.RunNotReady &&
      RunStateCodec.Read(InvalidHistory(first, json =>
          json["lastObservedDay"] = 2).ToJsonString()).Status == ReadStatus.Corrupt &&
      RunStateCodec.Read(InvalidHistory(first, json =>
          json["lastSettledDay"] = 1).ToJsonString()).Status == ReadStatus.Corrupt,
    "unfinished opening cannot settle or advance its persisted day markers");
var readyToSettle = ReadyRun(first);
var settled = SurvivalRules.SettleDay(readyToSettle, 1);
Check(settled.Status == SettleStatus.Applied && settled.State.Health == 90 &&
      settled.State.Exposure == 25, "first day");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(readyToSettle),
      readyToSettle, settled.State, gameRunId, 24).Status ==
          WriteGateStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(readyToSettle),
          readyToSettle, readyToSettle with
          { Revision = readyToSettle.Revision + 1, Health = 99 },
          gameRunId, 24).Status == WriteGateStatus.InvalidTransition &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(readyToSettle),
          readyToSettle, settled.State with { Health = 89 },
          gameRunId, 24).Status == WriteGateStatus.InvalidTransition,
    "write gate accepts exact daily settlement but rejects unearned survival changes");
Check(SurvivalRules.SettleDay(settled.State, 1).Status == SettleStatus.AlreadySettled, "duplicate settlement");
Check(SurvivalRules.SettleDay(settled.State with { LastObservedDay = 3 }, 3).Status == SettleStatus.DayGap, "missing day is not auto charged");
var reconciledDay = SurvivalRules.ReconcileDay(readyToSettle, 2);
Check(reconciledDay.Status == SettleStatus.Applied &&
      reconciledDay.State.LastObservedDay == 2 &&
      reconciledDay.State.LastSettledDay == 1 &&
      reconciledDay.State.Health == 90 &&
      reconciledDay.State.Exposure == 25 &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(readyToSettle),
          readyToSettle, reconciledDay.State, gameRunId, 24).Status ==
      WriteGateStatus.Ready,
    "day reconciliation settles survival and advances the saved day in one revision");
Check(SurvivalRules.ReconcileDay(readyToSettle, 3).Status ==
      SettleStatus.DayGap,
    "day reconciliation refuses to invent a missing day");

var nearLimit = ReadyRun(RunStateCodec.CreateNew("steam-demo-25382790", gameRunId, 24, 1, new ConfigSnapshot
{
    InitialExposure = 99,
    DailyExposureGain = 1,
    DailyHealthLoss = 0
}));
var full = SurvivalRules.SettleDay(nearLimit, 1);
Check(full.State.Exposure == 100 && full.State.ExposureDeadlineDay == 1 &&
      full.State.FailureReason == "exposure", "full exposure ends the run immediately");
Check(SurvivalRules.SettleDay(full.State with { LastObservedDay = 2 }, 2).Status ==
      SettleStatus.RunEnded, "ended run cannot settle another day");

Check(ConfigSnapshotLoader.Parse("{\"initialExposure\":100}").Status ==
      ConfigLoadStatus.Invalid,
    "a new run cannot begin at the immediate failure threshold");

var defaultConfig = new ConfigSnapshot();
Check(ExposureVisitRules.Gain(ExposureVisitRules.Security) == 5 &&
      ExposureVisitRules.Gain(ExposureVisitRules.Upper) == 0 &&
      ExposureVisitRules.Gain("FACTION_LOWER") == 0 &&
      ExposureVisitRules.InspectionChancePercent(74) == 0 &&
      ExposureVisitRules.InspectionChancePercent(75) == 5 &&
      ExposureVisitRules.InspectionChancePercent(99) == 29 &&
      ExposureVisitRules.InspectionChancePercent(100) == 0,
    "visit exposure and inspection probability boundaries");
var securityVisitSource = ReadyRun(RunStateCodec.CreateNew(
    "steam-demo-25382790", gameRunId, 24, 1,
    new ConfigSnapshot { InitialExposure = 74 }));
var securityVisit = ExposureVisitRules.ApplyVisit(securityVisitSource,
    ExposureVisitRules.Security);
Check(securityVisit is { Exposure: 79, FailureReason: "none" } &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(securityVisitSource),
          securityVisitSource, securityVisit, gameRunId, 24).Status ==
      WriteGateStatus.Ready,
    "one Security arrival advances exposure through the saved write gate");
var upperVisit = ExposureVisitRules.ApplyVisit(securityVisitSource,
    ExposureVisitRules.Upper);
Check(upperVisit == null &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(securityVisitSource),
          securityVisitSource, securityVisitSource with
          {
              Revision = securityVisitSource.Revision + 1,
              Exposure = securityVisitSource.Exposure + 2
          }, gameRunId, 24).Status == WriteGateStatus.InvalidTransition,
    "Upper arrivals do not change exposure and the former gain is rejected");
var terminalVisitSource = securityVisitSource with { Exposure = 99 };
var terminalVisit = ExposureVisitRules.ApplyVisit(terminalVisitSource,
    ExposureVisitRules.Security);
Check(terminalVisit is { Exposure: 100, FailureReason: "exposure" } &&
      SurvivalTransitionRules.Valid(terminalVisitSource, terminalVisit),
    "Security arrival reaching 100 records immediate exposure failure");

var ascensionSource = securityVisitSource with { Health = 1, Exposure = 99 };
foreach (var effects in new[] { default(OwnedCyberneticEffects),
             OwnedCyberneticEffects.FromItems([
                 new(CyberneticProductDefinitions.ItemId("artificial_heart"), 1, true),
                 new(CyberneticProductDefinitions.ItemId("mimetic_skin"), 1, true)]) })
{
    var ascendedDay = SurvivalRules.ReconcileDay(ascensionSource, 2, effects,
        mechanicalAscension: true);
    Check(ascendedDay.Status == SettleStatus.Applied &&
          ascendedDay.State is { Health: 1, Exposure: 99, FailureReason: "none",
              ExposureDeadlineDay: null, LastObservedDay: 2, LastSettledDay: 1 } &&
          ModDataWriteGate.Update(true, RunStateCodec.Write(ascensionSource),
              ascensionSource, ascendedDay.State, gameRunId, 24, effects,
              mechanicalAscension: true).Status == WriteGateStatus.Ready &&
          ModDataWriteGate.Update(true, RunStateCodec.Write(ascensionSource),
              ascensionSource, ascendedDay.State, gameRunId, 24, effects).Status ==
              WriteGateStatus.InvalidTransition,
        "ascension advances the day without losing health or gaining exposure, independently of owned cybernetics");
    var reloaded = RunStateCodec.Read(RunStateCodec.Write(ascendedDay.State)).State!;
    Check(SurvivalRules.ReconcileDay(reloaded, 3, effects,
              mechanicalAscension: true).State is { Health: 1, Exposure: 99,
                  LastObservedDay: 3, LastSettledDay: 2, FailureReason: "none" } &&
          SurvivalRules.ReconcileDay(reloaded, 2, effects,
              mechanicalAscension: true).Status != SettleStatus.Applied &&
          SurvivalRules.ReconcileDay(reloaded, 4, effects,
              mechanicalAscension: true).Status == SettleStatus.DayGap,
        "ascended day markers survive reload, reject duplicates and keep the day-gap gate");
}
var ascendedVisit = ExposureVisitRules.ApplyVisit(ascensionSource,
    ExposureVisitRules.Security, mechanicalAscension: true);
Check(ascendedVisit is { Health: 1, Exposure: 99, FailureReason: "none" } &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(ascensionSource),
          ascensionSource, ascendedVisit, gameRunId, 24,
          mechanicalAscension: true).Status == WriteGateStatus.Ready &&
      !SurvivalTransitionRules.Valid(terminalVisitSource, terminalVisit!,
          mechanicalAscension: true) &&
      ExposureVisitRules.InspectionChancePercent(ascendedVisit.Exposure) == 29,
    "ascension cancels Security exposure gain and rejects the former gain while preserving inspection probability");
foreach (var action in new[] { AugAction.Threat, AugAction.Kill })
{
    var ascendedAction = AugInteractionRules.ApplyAction(ascensionSource, action,
        mechanicalAscension: true);
    var ordinaryAction = AugInteractionRules.ApplyAction(ascensionSource, action)!;
    Check(ascendedAction is { Health: 1, Exposure: 99, FailureReason: "none" } &&
          ModDataWriteGate.Update(true, RunStateCodec.Write(ascensionSource),
              ascensionSource, ascendedAction, gameRunId, 24,
              mechanicalAscension: true).Status == WriteGateStatus.Ready &&
          !SurvivalTransitionRules.Valid(ascensionSource, ordinaryAction,
              mechanicalAscension: true),
        $"ascension prevents {action} exposure and preserves the receipt revision path");
}
Check(SurvivalRules.SettleDay(first, 1, mechanicalAscension: true).Status ==
          SettleStatus.RunNotReady &&
      SurvivalRules.SettleDay(full.State, 1, mechanicalAscension: true).Status ==
          SettleStatus.RunEnded,
    "ascension cannot bypass an unfinished opening or resurrect an ended run");

var threatenedAug = AugInteractionRules.ApplyAction(securityVisitSource, AugAction.Threat);
var killedAug = AugInteractionRules.ApplyAction(securityVisitSource, AugAction.Kill);
Check(threatenedAug is { Exposure: 79, FailureReason: "none" } &&
      killedAug is { Exposure: 84, FailureReason: "none" } &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(securityVisitSource),
          securityVisitSource, threatenedAug, gameRunId, 24).Status == WriteGateStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(securityVisitSource),
          securityVisitSource, killedAug, gameRunId, 24).Status == WriteGateStatus.Ready,
    "threat and kill exposure pass the normal saved transition gate");
var killedAfterThreat = AugInteractionRules.ApplyAction(threatenedAug!, AugAction.Kill);
Check(killedAfterThreat is { Exposure: 89 } &&
      killedAfterThreat.Revision == securityVisitSource.Revision + 2 &&
      killedAfterThreat.Health == securityVisitSource.Health &&
      killedAfterThreat.LastObservedDay == securityVisitSource.LastObservedDay,
    "threatening and then killing the same Aug costs fifteen exposure without a daily settlement");
foreach (var action in new[] { AugAction.Threat, AugAction.Kill })
{
    var actionSource = securityVisitSource with { Exposure = 96 };
    var terminalAction = AugInteractionRules.ApplyAction(actionSource, action);
    Check(terminalAction is { Exposure: 100, FailureReason: "exposure" } &&
          terminalAction.ExposureDeadlineDay == actionSource.LastObservedDay &&
          ModDataWriteGate.Update(true, RunStateCodec.Write(actionSource),
              actionSource, terminalAction, gameRunId, 24).Status == WriteGateStatus.Ready &&
          AugInteractionRules.ApplyAction(terminalAction, action) == null,
        $"{action} retains immediate full-exposure failure and cannot settle an ended run");
}
Check(AugInteractionRules.ApplyAction(securityVisitSource with
      { InitPhase = "pending" }, AugAction.Threat) == null &&
      AugInteractionRules.ApplyAction(securityVisitSource with
      { FailureReason = "bodyCollapse" }, AugAction.Kill) == null,
    "Aug action exposure does not bypass initialization or failure gates");
var threatReceipt = AugInteractionRules.RecordAction(null, AugAction.Threat,
    gameRunId, 1, 3, "aug_material");
var restoredThreatReceipt = JsonSerializer.Deserialize<AugInteractionReceipt>(
    JsonSerializer.Serialize(threatReceipt));
var killReceipt = AugInteractionRules.RecordAction(restoredThreatReceipt,
    AugAction.Kill, gameRunId, 1, 3, "aug_material");
Check(threatReceipt is { Threatened: true, Killed: false } &&
      AugInteractionRules.RecordAction(restoredThreatReceipt, AugAction.Threat,
          gameRunId, 1, 3, "aug_material") == null &&
      killReceipt is { Threatened: true, Killed: true } &&
      AugInteractionRules.RecordAction(killReceipt, AugAction.Kill,
          gameRunId, 1, 3, "aug_material") == null &&
      AugInteractionRules.RecordAction(killReceipt, AugAction.Threat,
          gameRunId, 1, 3, "aug_material") == null,
    "saved visitor receipts permit threat then kill and reject duplicate action callbacks after reload");
Check(AugInteractionRules.RecordAction(killReceipt, AugAction.Threat,
          gameRunId, 1, 2, "aug_material") is { Threatened: true, Killed: false } &&
      AugInteractionRules.RecordAction(killReceipt, AugAction.Threat,
          Guid.NewGuid().ToString(), 1, 3, "aug_material") is { Threatened: true, Killed: false } &&
      AugInteractionRules.RecordAction(killReceipt, AugAction.Threat,
          gameRunId, 2, 3, "aug_material") is { Threatened: true, Killed: false },
    "visitor action marks do not leak across repeated blueprints, runs or days");
Check(AugInteractionRules.ClampPresence(int.MinValue) == 1 &&
      AugInteractionRules.ClampPresence(0) == 1 &&
      AugInteractionRules.ClampPresence(15) == 15 &&
      AugInteractionRules.ClampPresence(99) == 99 &&
      AugInteractionRules.ClampPresence(100) == 99 &&
      AugInteractionRules.ClampPresence(int.MaxValue) == 99,
    "Aug presence keeps a positive spawning floor and stays below the native ending threshold");
Check(BargainRules.CalculateMarketModifier(true, false, 100, defaultConfig) == 25,
    "same-kind fixed markup ignores exposure");
Check(BargainRules.CalculateMarketModifier(true, true, 100, defaultConfig) == 25,
    "voice and exposure do not affect an Aug customer");
Check(BargainRules.CalculateMarketModifier(false, false, 0, defaultConfig) == -10 &&
      BargainRules.CalculateMarketModifier(false, false, 34, defaultConfig) == -10,
    "non-Aug exposure 0 through 34 subtracts ten percentage points");
Check(BargainRules.CalculateMarketModifier(false, false, 35, defaultConfig) == -25,
    "non-Aug first exposure markdown");
Check(BargainRules.CalculateMarketModifier(false, false, 74, defaultConfig) == -25,
    "non-Aug below second exposure threshold");
Check(BargainRules.CalculateMarketModifier(false, false, 75, defaultConfig) == -50,
    "non-Aug second exposure markdown");
Check(BargainRules.CalculateMarketModifier(false, false, 99, defaultConfig) == -50,
    "non-Aug above second exposure threshold");
Check(BargainRules.CalculateMarketModifier(false, false, 100, defaultConfig) == -50,
    "full exposure retains the final markdown at the failure boundary");
Check(BargainRules.CalculateMarketModifier(false, true, 75, defaultConfig) == -40,
    "voice fixed markup offsets a non-Aug exposure markdown");
var oldBargainSnapshotRun = ReadyRun(RunStateCodec.CreateNew(
    "steam-demo-25382790", gameRunId, 24, 1, defaultConfig with
    {
        BargainThreshold1 = 25, BargainThreshold2 = 50, BargainThreshold3 = 75
    }));
var savedOldBargain = RunStateCodec.Write(oldBargainSnapshotRun);
var restoredOldBargain = RunStateCodec.Read(savedOldBargain).State!;
Check(restoredOldBargain.ConfigSnapshot is
          { BargainThreshold1: 25, BargainThreshold2: 50, BargainThreshold3: 75 } &&
      BargainRules.CalculateMarketModifier(false, false, 26,
          restoredOldBargain.ConfigSnapshot) == -10 &&
      BargainRules.CalculateMarketModifier(false, false, 35,
          restoredOldBargain.ConfigSnapshot) == -25 &&
      BargainRules.CalculateMarketModifier(false, false, 75,
          restoredOldBargain.ConfigSnapshot) == -50 &&
      RunStateCodec.Write(restoredOldBargain) == savedOldBargain,
    "existing saved pricing thresholds cannot override current tiers or rewrite the save");

var moduleRun = ReadyRun(RunStateCodec.CreateNew("steam-demo-25382790",
    Guid.NewGuid().ToString("D"), 31, 1, defaultConfig));
for (int owned = 0; owned <= 5; owned++)
    Check(NeuralModuleRules.BaseStats(owned) == (8 * owned, 8 * owned, 4 * owned),
        $"{owned} owned interfaces set every interface base from ownership count");
Check(NeuralModuleRules.PreparePurchase(moduleRun, moduleRun.RunId,
      moduleRun.Revision, Guid.NewGuid().ToString("D"), 1,
      2_000, true).Status == NeuralModuleStatus.Applied,
    "brain-computer interfaces are purchasable without any narrative gate");

RunState BuyOneModule(RunState source, long credits)
{
    string request = Guid.NewGuid().ToString("D");
    var prepared = NeuralModuleRules.PreparePurchase(source, source.RunId,
        source.Revision, request, source.LastObservedDay, credits, true);
    if (prepared.Status != NeuralModuleStatus.Applied)
        throw new Exception($"Cannot prepare module purchase: {prepared.Status}");
    Check(ModDataWriteGate.Update(true, RunStateCodec.Write(source), source,
          prepared.State, source.GameRunId, source.SaveSlotId).Status ==
          WriteGateStatus.Ready, "module reservation passes the write gate");
    var applying = NeuralModuleRules.MarkApplying(prepared.State, request,
        source.LastObservedDay, credits, true);
    if (applying.Status != NeuralModuleStatus.Applied)
        throw new Exception("Cannot apply module purchase");
    Check(NeuralModuleRules.TryPurchasePrice(source, out long purchasePrice),
        "neural interface purchase price is available");
    var confirmed = NeuralModuleRules.ConfirmExternal(applying.State, request,
        $"module-{request}", credits - purchasePrice,
        $"owned-{request}", true);
    if (confirmed.Status != NeuralModuleStatus.Applied)
        throw new Exception("Cannot confirm module purchase");
    var committed = NeuralModuleRules.CommitPurchase(confirmed.State, request);
    if (committed.Status != NeuralModuleStatus.Applied)
        throw new Exception("Cannot commit module purchase");
    Check(ModDataWriteGate.Update(true, RunStateCodec.Write(confirmed.State),
          confirmed.State, committed.State, source.GameRunId, source.SaveSlotId)
          .Status == WriteGateStatus.Ready &&
          RunStateCodec.Read(RunStateCodec.Write(committed.State)).Status ==
          ReadStatus.Ready, "module purchase receipt survives JSON and write gate");
    return committed.State;
}

var purchaseRun = BuyOneModule(moduleRun, 10_000);
Check(purchaseRun.NeuralModules.Receipts.Length == 2 &&
      purchaseRun.NeuralModules.Receipts[1].Price == 200 &&
      NeuralModuleRules.CommitPurchase(purchaseRun,
          purchaseRun.NeuralModules.Receipts[1].RequestId).Status ==
          NeuralModuleStatus.AlreadyCommitted,
    "the first shop module is distinct and a repeated callback cannot buy again");
for (int owned = 1; owned <= 5; owned++)
{
    var priceState = purchaseRun with
    {
        NeuralModules = purchaseRun.NeuralModules with
        {
            Receipts = Enumerable.Repeat(purchaseRun.NeuralModules.Receipts[0], owned).ToArray()
        }
    };
    Check(NeuralModuleRules.TryPurchasePrice(priceState, out long quote) &&
          quote == 100L << owned,
        $"neural interface quote doubles after {owned} owned interfaces");
}
var forgedModule = purchaseRun with
{
    Revision = purchaseRun.Revision + 1,
    NeuralModules = purchaseRun.NeuralModules with
    {
        Receipts = [.. purchaseRun.NeuralModules.Receipts,
            new NeuralModuleReceipt(NeuralModuleRules.Shop,
                Guid.NewGuid().ToString("D"), "forged-module", 60,
                "forged-evidence", 500, 10_000, 9_500)]
    }
};
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(purchaseRun),
      purchaseRun, forgedModule, purchaseRun.GameRunId,
      purchaseRun.SaveSlotId).Status == WriteGateStatus.InvalidTransition,
    "a forged module receipt cannot bypass the purchase phases");
var machineConfig = defaultConfig with
{
    MachineProfiles =
    [
        new MachineModelProfile("water", "water-adapter")
    ]
};
var machineRun = ReadyRun(RunStateCodec.CreateNew(
    "steam-demo-25382790", Guid.NewGuid().ToString("D"), 32, 1,
    machineConfig));
machineRun = BuyOneModule(machineRun, 2_000);
string firstModule = machineRun.NeuralModules.Receipts[0].InstanceId;
string secondModule = machineRun.NeuralModules.Receipts[1].InstanceId;
var firstConnection = MachineRules.Connect(machineRun, machineRun.RunId,
    machineRun.Revision, new MachineConnectionSnapshot(machineRun.LastObservedDay,
        firstModule, "machine-a", "water", true, true, "owned-a"));
Check(firstConnection.Status == MachineStatus.Applied &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(machineRun), machineRun,
          firstConnection.State, machineRun.GameRunId, machineRun.SaveSlotId).Status ==
          WriteGateStatus.Ready &&
      MachineRules.Connect(firstConnection.State, firstConnection.State.RunId,
          firstConnection.State.Revision, new MachineConnectionSnapshot(firstConnection.State.LastObservedDay,
              firstModule, "machine-b", "water", true, true, "owned-b"))
          .Status == MachineStatus.ModuleAlreadyConnected,
    "one module can connect only one machine");
var forgedConnection = machineRun with
{
    Revision = machineRun.Revision + 1,
    Machines = machineRun.Machines with
    {
        NextConnectionEpoch = machineRun.Machines.NextConnectionEpoch + 1,
        Connections = [firstConnection.State.Machines.Connections[0] with
        { Epoch = machineRun.Machines.NextConnectionEpoch + 1 }]
    }
};
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(machineRun),
      machineRun, forgedConnection, machineRun.GameRunId,
      machineRun.SaveSlotId).Status == WriteGateStatus.InvalidTransition,
    "connection epochs cannot be invented outside the next native link step");
var secondConnection = MachineRules.Connect(firstConnection.State,
    firstConnection.State.RunId, firstConnection.State.Revision,
    new MachineConnectionSnapshot(firstConnection.State.LastObservedDay, secondModule, "machine-b",
        "water", true, true, "owned-b"));
Check(secondConnection.Status == MachineStatus.Applied &&
      secondConnection.State.Machines.Connections.Length == 2,
    "two owned module instances may connect separate machines");
JsonNode InvalidHistory(RunState state, Action<JsonNode> mutate)
{
    var json = JsonNode.Parse(RunStateCodec.Write(state))!;
    mutate(json);
    return json;
}
var intent = new StartIntentCoordinator();
var menu1 = intent.OpenMenu();
Check(intent.SelectAug(menu1), "select Aug in current menu");
intent.Cancel(menu1);
Check(intent.Confirm(menu1) == null, "cancelled selection cannot confirm");
var menu2 = intent.OpenMenu();
Check(!intent.SelectAug(menu1), "stale menu cannot select");
Check(intent.SelectAug(menu2), "select in reopened menu");
intent.SelectOther(menu2);
Check(intent.Confirm(menu2) == null, "another start clears Aug intent");
Check(intent.SelectAug(menu2), "reselect Aug");
var reservation = intent.Confirm(menu2);
Check(reservation != null && intent.Confirm(menu2) == null, "confirmation reserves exactly once");
Check(intent.Bind(new PendingStart(Guid.NewGuid().ToString("D"), menu2), gameRunId, 25) == null,
    "unrelated token cannot bind");
var bound = intent.Bind(reservation!, gameRunId, 25);
Check(bound?.GameRunId == gameRunId && bound.SaveSlotId == 25, "bind actual native identity");
Check(intent.Bind(reservation!, gameRunId, 25) == null, "reservation cannot bind twice");
var menu3 = intent.OpenMenu();
intent.SelectAug(menu3);
var invalidReservation = intent.Confirm(menu3)!;
Check(intent.Bind(invalidReservation, "not-a-run-id", 25) == null, "invalid native identity fails closed");
Check(intent.Bind(invalidReservation, gameRunId, 25) == null, "failed binding cannot be retried on another run");
var menu4 = intent.OpenMenu();
intent.SelectAug(menu4);
var staleReservation = intent.Confirm(menu4)!;
var menu5 = intent.OpenMenu();
Check(intent.Bind(staleReservation, gameRunId, 26) == null, "reopened menu invalidates pending start");
Check(intent.SelectAug(menu5), "new menu still works after invalidation");
var cancelledReservation = intent.Confirm(menu5)!;
intent.SelectOther(menu5);
Check(intent.Bind(cancelledReservation, gameRunId, 27) == null,
    "switching to another start after confirmation invalidates the token");

var openingIntents = new StartIntentCoordinator();
var openingEpoch = openingIntents.OpenMenu();
Check(openingIntents.SelectAug(openingEpoch), "opening planner selects current menu");
var openingReservation = openingIntents.Confirm(openingEpoch)!;
var openingPlan = InitialSavePlanner.Prepare(openingIntents, openingReservation,
    gameRunId, 28, 1, "steam-demo-25382790", defaultConfig, false, null);
Check(openingPlan.Status == InitialSavePlanStatus.Ready && openingPlan.Binding != null &&
      openingPlan.State != null && openingPlan.Write != null &&
      openingPlan.Write.NextJson == RunStateCodec.Write(openingPlan.State) &&
      openingPlan.Write.SaveSlotId == 28,
    "initial save planner binds native identity to one serializable state");
Check(ModDataWriteGate.RecheckPreparedNew(openingPlan, false, null, gameRunId, 28).Status ==
      WriteGateStatus.Ready,
    "prepared initial state remains writable when native identity and key are unchanged");
Check(ModDataWriteGate.RecheckPreparedNew(openingPlan, true, "occupied", gameRunId, 28).Status ==
      WriteGateStatus.ExistingState,
    "prepared initial state refuses a newly occupied native key");
Check(ModDataWriteGate.RecheckPreparedNew(openingPlan, false, null, gameRunId, 29).Status ==
      WriteGateStatus.IdentityMismatch,
    "prepared initial state refuses a changed native slot");
Check(ModDataWriteGate.RecheckPreparedNew(openingPlan with
    { State = openingPlan.State! with { RunId = Guid.NewGuid().ToString("D") } },
    false, null, gameRunId, 28).Status == WriteGateStatus.ContentConflict,
    "prepared initial state refuses a changed state after planning");
Check(InitialSavePlanner.Prepare(openingIntents, openingReservation, gameRunId, 28, 1,
    "steam-demo-25382790", defaultConfig, false, null).Status == InitialSavePlanStatus.IntentMismatch,
    "initial save plan cannot reuse confirmed intent");

var occupiedIntents = new StartIntentCoordinator();
var occupiedEpoch = occupiedIntents.OpenMenu();
occupiedIntents.SelectAug(occupiedEpoch);
var occupiedReservation = occupiedIntents.Confirm(occupiedEpoch)!;
var occupiedPlan = InitialSavePlanner.Prepare(occupiedIntents, occupiedReservation,
    gameRunId, 29, 1, "steam-demo-25382790", defaultConfig, true, "existing");
Check(occupiedPlan.Status == InitialSavePlanStatus.WriteRejected &&
      occupiedPlan.GateStatus == WriteGateStatus.ExistingState && occupiedPlan.Write == null,
    "initial save planner refuses an occupied Mod state key");

var invalidDayIntents = new StartIntentCoordinator();
var invalidDayEpoch = invalidDayIntents.OpenMenu();
invalidDayIntents.SelectAug(invalidDayEpoch);
var invalidDayReservation = invalidDayIntents.Confirm(invalidDayEpoch)!;
Check(InitialSavePlanner.Prepare(invalidDayIntents, invalidDayReservation,
    gameRunId, 30, -1, "steam-demo-25382790", defaultConfig, false, null).Status ==
      InitialSavePlanStatus.InvalidNewRun &&
      invalidDayIntents.Bind(invalidDayReservation, gameRunId, 30) == null,
    "invalid native day consumes the opening reservation without a write plan");

var invalidConfigIntents = new StartIntentCoordinator();
var invalidConfigEpoch = invalidConfigIntents.OpenMenu();
invalidConfigIntents.SelectAug(invalidConfigEpoch);
var invalidConfigReservation = invalidConfigIntents.Confirm(invalidConfigEpoch)!;
Check(InitialSavePlanner.Prepare(invalidConfigIntents, invalidConfigReservation,
    gameRunId, 32, 1, "steam-demo-25382790",
    defaultConfig with { InitialHealth = 0 }, false, null).Status ==
      InitialSavePlanStatus.InvalidNewRun &&
      invalidConfigIntents.Bind(invalidConfigReservation, gameRunId, 32) == null,
    "invalid opening configuration consumes reservation without a write plan");

var mismatchIntents = new StartIntentCoordinator();
var mismatchEpoch = mismatchIntents.OpenMenu();
mismatchIntents.SelectAug(mismatchEpoch);
var mismatchReservation = mismatchIntents.Confirm(mismatchEpoch)!;
Check(InitialSavePlanner.Prepare(mismatchIntents,
    new PendingStart(Guid.NewGuid().ToString("D"), mismatchEpoch), gameRunId, 31, 1,
    "steam-demo-25382790", defaultConfig, false, null).Status ==
      InitialSavePlanStatus.IntentMismatch &&
      mismatchIntents.Bind(mismatchReservation, gameRunId, 31) == null,
    "mismatched initial save token invalidates the pending start");

var sessions = new SessionCoordinator();
Check(sessions.Restore(null, gameRunId, 24).Status == ReadStatus.Missing && sessions.Current == null,
    "missing save data does not create a Mod session");
Check(sessions.Restore("{", gameRunId, 24).Status == ReadStatus.Corrupt && sessions.Current == null,
    "corrupt save data does not create a Mod session");
var newSession = sessions.BeginNew(bound!, "steam-demo-25382790", 1, defaultConfig);
Check(newSession != null && sessions.IsCurrent(newSession.Generation, newSession.State.RunId),
    "bound start creates one in-memory session");
Check(sessions.BeginNew(bound!, "steam-demo-25382790", 1, defaultConfig) == null,
    "cannot start again while active");
sessions.Clear();
Check(!sessions.IsCurrent(newSession!.Generation, newSession.State.RunId), "old callback invalid after clear");
Check(sessions.BeginNew(bound!, "steam-demo-25382790", 1, defaultConfig) == null,
    "consumed start token cannot create a second run");
Check(sessions.Restore(RunStateCodec.Write(newSession.State), gameRunId, 25).Status == ReadStatus.Ready &&
      sessions.Current?.State.RunId == newSession.State.RunId, "matching save restores the Mod session");
var restoredGeneration = sessions.Current!.Generation;
Check(restoredGeneration != newSession.Generation && !sessions.IsCurrent(newSession.Generation, newSession.State.RunId),
    "restore invalidates callbacks from the earlier session");
Check(sessions.Restore(RunStateCodec.Write(newSession.State), gameRunId, 26).Status == ReadStatus.RunMismatch &&
      sessions.Current == null, "slot switch cannot retain another slot's session");
Check(sessions.RestoreReadResult(new ReadResult(ReadStatus.Ready, null, null), gameRunId, 25).Status ==
      ReadStatus.Corrupt && sessions.Current == null,
    "native restore refuses a ready result without state");
Check(sessions.RestoreReadResult(new ReadResult(ReadStatus.Ready, newSession.State, null),
      gameRunId, 26).Status == ReadStatus.RunMismatch && sessions.Current == null,
    "native restore rechecks the loaded slot before adopting state");
Check(sessions.RestoreReadResult(new ReadResult(ReadStatus.Ready, newSession.State, null),
      gameRunId, 25).Status == ReadStatus.Ready &&
      sessions.Current?.State.RunId == newSession.State.RunId,
    "native restore adopts a validated matching state");

var readySupply = ReadyRun(first);
var maintenanceEffects = new OwnedCyberneticEffects(true, true, false, false, false, false);
var maintenanceChanges = SurvivalRules.DailyChanges(readySupply.ConfigSnapshot, maintenanceEffects);
var maintenanceSettled = SurvivalRules.SettleDay(readySupply, readySupply.LastObservedDay, maintenanceEffects);
Check(maintenanceChanges == (5, 2) && maintenanceSettled.State.Health == readySupply.Health - maintenanceChanges.HealthLoss &&
    maintenanceSettled.State.Exposure == readySupply.Exposure + maintenanceChanges.ExposureGain &&
    StatusPanelRules.DailyChangeLine(readySupply, false, maintenanceEffects).Contains("健康 -5／暴露 +2"),
    "daily maintenance display follows the exact settlement used for owned heart and skin");
Check(SurvivalRules.DailyChanges(readySupply.ConfigSnapshot, maintenanceEffects, true) == (0, 0) &&
    StatusPanelRules.DailyChangeLine(readySupply, true, maintenanceEffects, true).Contains("Ascended"),
    "ascension maintenance display does not promise native wound immunity");
Check(StatusPanelRules.Build(readySupply, 1, false) is { Current: true } liveStatus &&
      liveStatus.Lines.Any(x => x.Contains("健康 100/100")) &&
      liveStatus.Lines.Any(x => x.Contains("暴露 20/100")) &&
      StatusPanelRules.Build(readySupply, 2, false) is
          { Current: false } staleStatus &&
      !staleStatus.Lines.Any(x => x.Contains("健康")) &&
      StatusPanelRules.Build(readySupply, 1, true).Lines.Any(x =>
          x.Contains("Health 100/100")),
    "status display localizes current survival values but hides them when game day drifts");
Check(StatusPanelRules.Build(readySupply, 1, false).Lines.Any(x =>
          x.Contains("伪人", StringComparison.Ordinal) &&
          x.Contains("0 点 / 0 格", StringComparison.Ordinal)),
    "status display exposes the built-in zero-cost Aug perk");
Check(!StatusPanelRules.Build(readySupply, 1, false).Lines.Any(x =>
          x.Contains("角色被动：同类识别", StringComparison.Ordinal)),
    "status display does not duplicate recognition outside the Aug perk");
Check(IdentityDisplayRules.AppendAugTag("顾客甲", true, false) == "顾客甲 [伪人]" &&
      IdentityDisplayRules.AppendAugTag("顾客甲 [伪人]", true, false) == "顾客甲 [伪人]" &&
      IdentityDisplayRules.AppendAugTag("顾客甲", false) == "顾客甲",
    "character passive labels only actual Aug names and is idempotent");
var medicine = new MedicalSupplyInput("verified_medical_item", "stable_effective_unit", 8, 5);
var medicalDay1 = SupplyRules.RefreshMedical(readySupply, 1, medicine);
Check(medicalDay1.Status == SupplyRefreshStatus.Applied && medicalDay1.State.MedicalSupply is
    { PeriodDay: 1, Stock: 3, RecoveryPerUnit: 8, UnitPrice: 6 }, "daily medical target and fixed price");
Check(SupplyRules.RefreshMedical(medicalDay1.State, 1, medicine with { StandardUnitPrice = 100 }).Status ==
      SupplyRefreshStatus.AlreadyCurrent, "same-day medical refresh cannot reprice or add stock");
Check(RunStateCodec.Read(RunStateCodec.Write(medicalDay1.State)).State?.MedicalSupply?.Stock == 3,
    "medical quote and stock survive save round trip");
var day2Supply = medicalDay1.State with
{
    LastObservedDay = 2,
    MedicalSupply = medicalDay1.State.MedicalSupply! with { Stock = 1 }
};
var medicalDay2 = SupplyRules.RefreshMedical(day2Supply, 2, medicine);
Check(medicalDay2.State.MedicalSupply?.Stock == 3 && medicalDay2.State.MedicalSupply?.PeriodDay == 2,
    "next day replenishes to target without accumulation");
Check(SupplyRules.RefreshMedical(day2Supply, 2, medicine with { SpecificationKey = "different" }).Status ==
      SupplyRefreshStatus.SpecificationChanged, "medical specification cannot silently change");
Check(SupplyRules.RefreshMedical(day2Supply, 2, medicine with { RecoveryPerUnit = 9 }).Status ==
      SupplyRefreshStatus.SpecificationChanged, "recovery strength cannot silently change for carried stock");
var electronicDay1 = SupplyRules.RefreshElectronic(readySupply, 1, 10);
Check(electronicDay1.State.ElectronicSupply is { PeriodId: 0, Stock: 7, UnitPrice: 12 },
    "electronic opening stock and price");
var electronicDay7 = SupplyRules.RefreshElectronic(electronicDay1.State with { LastObservedDay = 7 }, 7, 99);
Check(electronicDay7.Status == SupplyRefreshStatus.AlreadyCurrent &&
      electronicDay7.State.ElectronicSupply?.UnitPrice == 12, "electronic stock does not refresh before day eight");
var electronicDay8 = SupplyRules.RefreshElectronic(electronicDay1.State with
{
    LastObservedDay = 8,
    ElectronicSupply = electronicDay1.State.ElectronicSupply! with { Stock = 2 }
}, 8, 10);
Check(electronicDay8.State.ElectronicSupply is { PeriodId: 1, Stock: 7 },
    "electronic stock replenishes once at the next seven-day period");
Check(SupplyRules.RefreshMedical(first, 1, medicine).Status == SupplyRefreshStatus.RunNotReady,
    "supply unavailable until initialization completes");
Check(SupplyRules.RefreshMedical(readySupply, 2, medicine).Status == SupplyRefreshStatus.DayMismatch,
    "supply cannot refresh for an unobserved day");
bool rejectedOversizedSupplyQuote = false;
try { SupplyRules.RefreshMedical(readySupply, 1,
    medicine with { StandardUnitPrice = int.MaxValue }); }
catch (OverflowException) { rejectedOversizedSupplyQuote = true; }
Check(rejectedOversizedSupplyQuote,
    "supply refresh refuses a quote outside the native credit range");
var supplyShop = electronicDay1.State with { MedicalSupply = medicalDay1.State.MedicalSupply };
var supplyRequest = Guid.NewGuid().ToString("D");
var emptyStacks = new Dictionary<IntPtr, SupplyOwnedStack>();
var oneStack = new Dictionary<IntPtr, SupplyOwnedStack>
{ [new IntPtr(1)] = new(44, 1) };
Check(SupplyDeliveryProofRules.Evidence(supplyRequest, emptyStacks,
      oneStack) == $"supply:{supplyRequest}:44" &&
      SupplyDeliveryProofRules.Evidence(supplyRequest,
          new Dictionary<IntPtr, SupplyOwnedStack>
          { [new IntPtr(1)] = new(44, 2) },
          new Dictionary<IntPtr, SupplyOwnedStack>
          { [new IntPtr(1)] = new(44, 3) }) ==
      $"supply:{supplyRequest}:44",
    "owned delivery proof accepts exactly one new or merged native item unit");
Check(SupplyDeliveryProofRules.Evidence(supplyRequest, oneStack,
      new Dictionary<IntPtr, SupplyOwnedStack>
      { [new IntPtr(1)] = new(45, 2) }) == null &&
      SupplyDeliveryProofRules.Evidence(supplyRequest, oneStack,
      new Dictionary<IntPtr, SupplyOwnedStack>()) == null &&
      SupplyDeliveryProofRules.Evidence(supplyRequest, emptyStacks,
      new Dictionary<IntPtr, SupplyOwnedStack>
      { [new IntPtr(1)] = new(44, 1), [new IntPtr(2)] = new(45, 1) }) == null,
    "owned delivery proof rejects identity replacement, lost stacks, and two grants");
string supplyEvidence = $"supply:{supplyRequest}:44";
Check(SupplyDeliveryProofRules.IsEvidenceForRequest(supplyRequest, supplyEvidence) &&
      !SupplyDeliveryProofRules.IsEvidenceForRequest(supplyRequest,
          $"supply:{Guid.NewGuid():D}:44") &&
      !SupplyDeliveryProofRules.IsEvidenceForRequest(supplyRequest,
          $"supply:{supplyRequest}:044") &&
      !SupplyDeliveryProofRules.IsEvidenceForRequest(supplyRequest,
          "paid-and-delivered"),
    "supply proof is tied to one request and canonical native item ID");
Check(SupplyPurchaseRules.Preview(supplyShop, SupplyPurchaseRules.Medical, 1, 20, 0, true)
      is { Status: SupplyPurchaseStatus.Ready, Price: 6 } &&
      SupplyPurchaseRules.Preview(supplyShop, SupplyPurchaseRules.Medical, 1, 5, 0, true)
          .Status == SupplyPurchaseStatus.InsufficientCredits &&
      SupplyPurchaseRules.Preview(supplyShop, SupplyPurchaseRules.Medical, 1, 20, 0, false)
          .Status == SupplyPurchaseStatus.DestinationUnavailable,
    "medical supply quote requires current cash and a verified destination");
var supplyPrepared = SupplyPurchaseRules.Prepare(supplyShop, supplyShop.RunId,
    supplyShop.Revision, supplyRequest, SupplyPurchaseRules.Medical, 1, 20, 0, true);
Check(supplyPrepared.Status == SupplyPurchaseStatus.Applied &&
      supplyPrepared.State.MedicalSupply?.Stock == 3 &&
      SupplyPurchaseRules.Prepare(supplyPrepared.State, supplyShop.RunId,
          supplyShop.Revision, supplyRequest, SupplyPurchaseRules.Medical,
          1, 20, 0, true).Status == SupplyPurchaseStatus.Duplicate &&
      SurvivalRules.SettleDay(supplyPrepared.State, 1).Status == SettleStatus.OperationPending,
    "reserved supply purchase neither spends stock nor permits day settlement");
var supplyApplying = SupplyPurchaseRules.MarkApplying(supplyPrepared.State,
    supplyRequest, 20, 0, true);
Check(supplyApplying.Status == SupplyPurchaseStatus.Applied &&
      SupplyPurchaseRules.MarkApplying(supplyPrepared.State,
          supplyRequest, 19, 0, true).Status == SupplyPurchaseStatus.SnapshotChanged &&
      RunStateCodec.Read(RunStateCodec.Write(supplyApplying.State)).Status == ReadStatus.Ready,
    "supply purchase freezes the pre-debit native cash and owned-unit counts");
var supplyMismatch = SupplyPurchaseRules.ConfirmExternal(supplyApplying.State,
    supplyRequest, 14, 0, medicine.ItemId, "delivery-missing");
Check(supplyMismatch.Status == SupplyPurchaseStatus.NeedsRecovery &&
      supplyMismatch.State.MedicalSupply?.Stock == 3 &&
      supplyMismatch.State.SupplyPurchases.Pending?.RecoveryObservation?.OwnedUnitsAfter == 0 &&
      SupplyPurchaseRules.Commit(supplyMismatch.State, supplyRequest).Status ==
          SupplyPurchaseStatus.NeedsRecovery &&
      RunStateCodec.Read(RunStateCodec.Write(supplyMismatch.State)).Status == ReadStatus.Ready,
    "payment without one delivered unit freezes purchase for reconciliation");
Check(StatusPanelRules.Build(supplyMismatch.State, 1, false).Lines.Any(x =>
      x.Contains("供货购买等待核对")),
    "visible Mod status reports an unresolved supply purchase");
Check(StatusPanelRules.Build(supplyMismatch.State, 2, false) is
          { Current: false } mismatchedDayStatus &&
      mismatchedDayStatus.Lines.Any(x => x.Contains("供货购买等待核对")) &&
      !mismatchedDayStatus.Lines.Any(x => x.Contains("健康")) &&
      StatusPanelRules.Build(supplyMismatch.State with
      { InitPhase = "needsRecovery" }, 1, true) is
          { Current: false } openingRecoveryStatus &&
      openingRecoveryStatus.Lines.Any(x => x.Contains("Opening needs reconciliation")) &&
      openingRecoveryStatus.Lines.Any(x => x.Contains("Supply purchase needs reconciliation")),
    "status preserves recovery warnings while hiding stale survival values");
var supplyConfirmed = SupplyPurchaseRules.ConfirmExternal(supplyApplying.State,
    supplyRequest, 14, 1, medicine.ItemId, supplyEvidence);
Check(supplyConfirmed.Status == SupplyPurchaseStatus.Applied &&
      supplyConfirmed.State.MedicalSupply?.Stock == 3 &&
      RunStateCodec.Read(RunStateCodec.Write(supplyConfirmed.State)).Status == ReadStatus.Ready,
    "exact native payment and delivery are recorded before Mod stock decreases");
Check(SupplyPurchaseRules.ConfirmExternal(supplyApplying.State, supplyRequest,
          14, 1, medicine.ItemId, "paid-and-delivered").Status ==
          SupplyPurchaseStatus.NeedsRecovery &&
      RunStateCodec.Read(InvalidHistory(supplyConfirmed.State, json =>
          json["supplyPurchases"]!["pending"]!["evidenceId"] =
              "paid-and-delivered").ToJsonString()).Status == ReadStatus.Corrupt,
    "arbitrary delivery text cannot confirm or survive a purchased state");
var supplyCommitted = SupplyPurchaseRules.Commit(supplyConfirmed.State, supplyRequest);
Check(supplyCommitted.Status == SupplyPurchaseStatus.Applied &&
      supplyCommitted.State.MedicalSupply?.Stock == 2 &&
      supplyCommitted.State.SupplyPurchases.Receipts.Length == 1 &&
      SupplyPurchaseRules.Commit(supplyCommitted.State, supplyRequest).Status ==
          SupplyPurchaseStatus.AlreadyCommitted &&
      RunStateCodec.Read(RunStateCodec.Write(supplyCommitted.State)).Status == ReadStatus.Ready,
    "confirmed purchase decrements exactly one unit and cannot replay after save");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(supplyConfirmed.State),
      supplyConfirmed.State, supplyCommitted.State, supplyShop.GameRunId,
      supplyShop.SaveSlotId).Status == WriteGateStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(supplyConfirmed.State),
          supplyConfirmed.State, supplyCommitted.State with
          { MedicalSupply = supplyConfirmed.State.MedicalSupply },
          supplyShop.GameRunId, supplyShop.SaveSlotId).Status ==
          WriteGateStatus.InvalidTransition,
    "write gate binds purchase receipt to the matching one-unit stock decrease");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(supplyShop),
      supplyShop, supplyShop with
      {
          Revision = supplyShop.Revision + 1,
          MedicalSupply = supplyShop.MedicalSupply! with { Stock = 2 }
      }, supplyShop.GameRunId, supplyShop.SaveSlotId).Status ==
      WriteGateStatus.InvalidTransition,
    "same-day stock cannot disappear without a committed purchase receipt");
Check(RunStateCodec.Read(InvalidHistory(supplyCommitted.State, json =>
      json["medicalSupply"]!["stock"] = 3).ToJsonString()).Status == ReadStatus.Corrupt,
    "saved purchase ledger rejects restored stock without a new period");
Check(RunStateCodec.Read(InvalidHistory(supplyCommitted.State, json =>
      json["medicalSupply"] = null).ToJsonString()).Status == ReadStatus.Corrupt,
    "saved purchase ledger rejects a missing medical supply source");
Check(RunStateCodec.Read(InvalidHistory(supplyCommitted.State, json =>
      json["supplyPurchases"]!["receipts"]![0]!["evidenceId"] =
          "paid-and-delivered").ToJsonString()).Status == ReadStatus.Corrupt,
    "saved purchase ledger rejects a delivery proof unrelated to its request");
var secondSupplyRequest = Guid.NewGuid().ToString("D");
var secondSupplyPrepared = SupplyPurchaseRules.Prepare(supplyCommitted.State,
    supplyShop.RunId, supplyCommitted.State.Revision, secondSupplyRequest,
    SupplyPurchaseRules.Medical, 1, 14, 1, true);
var secondSupplyApplying = SupplyPurchaseRules.MarkApplying(secondSupplyPrepared.State,
    secondSupplyRequest, 14, 1, true);
var secondSupplyConfirmed = SupplyPurchaseRules.ConfirmExternal(secondSupplyApplying.State,
    secondSupplyRequest, 8, 2, medicine.ItemId,
    $"supply:{secondSupplyRequest}:45");
var secondSupplyCommitted = SupplyPurchaseRules.Commit(secondSupplyConfirmed.State,
    secondSupplyRequest);
Check(secondSupplyCommitted.Status == SupplyPurchaseStatus.Applied &&
      secondSupplyCommitted.State.MedicalSupply?.Stock == 1 &&
      secondSupplyCommitted.State.SupplyPurchases.Receipts.Length == 2 &&
      RunStateCodec.Read(RunStateCodec.Write(secondSupplyCommitted.State)).Status ==
          ReadStatus.Ready,
    "same-day repeat purchase keeps a descending, non-replayable stock ledger");
Check(RunStateCodec.Read(InvalidHistory(secondSupplyCommitted.State, json =>
      json["supplyPurchases"]!["receipts"]![1]!["stockBefore"] = 3)
      .ToJsonString()).Status == ReadStatus.Corrupt,
    "save reader rejects an invented restock between same-day purchases");
var electronicRequest = Guid.NewGuid().ToString("D");
var electronicPrepared = SupplyPurchaseRules.Prepare(supplyShop, supplyShop.RunId,
    supplyShop.Revision, electronicRequest, SupplyPurchaseRules.Electronic, 1, 20, 2, true);
var electronicApplying = SupplyPurchaseRules.MarkApplying(electronicPrepared.State,
    electronicRequest, 20, 2, true);
var electronicConfirmed = SupplyPurchaseRules.ConfirmExternal(electronicApplying.State,
    electronicRequest, 8, 3, SupplyRules.ElectronicItemId,
    $"supply:{electronicRequest}:46");
var electronicCommitted = SupplyPurchaseRules.Commit(electronicConfirmed.State,
    electronicRequest);
Check(electronicCommitted.Status == SupplyPurchaseStatus.Applied &&
      electronicCommitted.State.ElectronicSupply?.Stock == 6 &&
      electronicCommitted.State.MedicalSupply?.Stock == 3 &&
      RunStateCodec.Read(RunStateCodec.Write(electronicCommitted.State)).Status == ReadStatus.Ready,
    "electronic purchase uses its fixed weekly quote without touching medical stock");
bool rejectedUnsafeSupply = false;
try { RunStateCodec.CreateNew("steam-demo-25382790", gameRunId, 25, 1, new ConfigSnapshot { MedicalRecoveryBudget = 5 }); }
catch (InvalidOperationException) { rejectedUnsafeSupply = true; }
Check(rejectedUnsafeSupply, "configuration cannot understock daily natural health loss");

var medicalRun = readySupply with { Health = 90 };
var medicalRequest = Guid.NewGuid().ToString("D");
var medicalPrepared = OperationJournal.PrepareMedical(medicalRun, medicalRun.RunId, medicalRun.Revision,
    medicalRequest, "medical-instance-1", 18);
Check(medicalPrepared.Status == OperationStatus.Applied && medicalPrepared.State.Health == 90 &&
      medicalPrepared.State.PendingOperation?.Phase == OperationJournal.Prepared,
    "medical preparation records intent without restoring health");
Check(OperationJournal.PrepareMedical(medicalPrepared.State, medicalRun.RunId, medicalRun.Revision,
    medicalRequest, "medical-instance-1", 18).Status == OperationStatus.Duplicate,
    "same medical request reuses pending operation");
Check(OperationJournal.PrepareMedical(medicalPrepared.State, medicalRun.RunId, medicalPrepared.State.Revision,
    Guid.NewGuid().ToString("D"), "medical-instance-2", 18).Status == OperationStatus.OperationPending,
    "second external mutation cannot overlap");
Check(SurvivalRules.SettleDay(medicalPrepared.State, 1).Status == SettleStatus.OperationPending &&
      SupplyRules.RefreshMedical(medicalPrepared.State, 1, medicine).Status == SupplyRefreshStatus.OperationPending,
    "unresolved consumption blocks day settlement and supply refresh");
var medicalApplying = OperationJournal.MarkApplying(medicalPrepared.State, medicalPrepared.OperationId!);
Check(medicalApplying.Status == OperationStatus.Applied &&
      RunStateCodec.Read(RunStateCodec.Write(medicalApplying.State)).State?.PendingOperation?.Phase ==
      OperationJournal.Applying, "applying phase survives save and requires external reconciliation");
Check(OperationJournal.Commit(medicalApplying.State, medicalPrepared.OperationId!).Status == OperationStatus.InvalidPhase,
    "medical recovery cannot commit before consumption evidence");
var medicalConfirmed = OperationJournal.ConfirmExternal(medicalApplying.State, medicalPrepared.OperationId!,
    "medical-instance-1", 18, "consumption-receipt-1");
Check(medicalConfirmed.Status == OperationStatus.Applied && medicalConfirmed.State.Health == 90 &&
      medicalConfirmed.State.PendingOperation?.Phase == OperationJournal.ExternalConfirmed,
    "confirmed consumption is recorded before Mod health changes");
Check(OperationJournal.ConfirmExternal(medicalConfirmed.State, medicalPrepared.OperationId!,
    "medical-instance-1", 18, "consumption-receipt-1").Status == OperationStatus.Duplicate,
    "same consumption evidence cannot be applied twice");
var medicalCommitted = OperationJournal.Commit(medicalConfirmed.State, medicalPrepared.OperationId!);
Check(medicalCommitted.Status == OperationStatus.Applied && medicalCommitted.State.Health == 100 &&
      medicalCommitted.State.PendingOperation == null && medicalCommitted.State.LastCommittedOperationSequence == 1,
    "medical recovery caps at maximum and commits once");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(medicalConfirmed.State),
      medicalConfirmed.State, medicalCommitted.State, gameRunId, 24).Status ==
          WriteGateStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(medicalConfirmed.State),
          medicalConfirmed.State, medicalCommitted.State with { Health = 99 },
          gameRunId, 24).Status == WriteGateStatus.InvalidTransition,
    "write gate binds health recovery to the exact confirmed medical operation");
Check(OperationJournal.Commit(medicalCommitted.State, medicalPrepared.OperationId!).Status ==
      OperationStatus.AlreadyCommitted, "committed medical operation cannot repeat");
Check(OperationJournal.PrepareMedical(medicalCommitted.State, medicalRun.RunId, medicalCommitted.State.Revision,
    medicalRequest, "medical-instance-1", 18).Status == OperationStatus.AlreadyCommitted,
    "repeated callback cannot prepare the last committed request again");
var restoredCommitted = RunStateCodec.Read(RunStateCodec.Write(medicalCommitted.State));
Check(restoredCommitted.Status == ReadStatus.Ready &&
      OperationJournal.PrepareMedical(restoredCommitted.State!, medicalRun.RunId, restoredCommitted.State!.Revision,
          medicalRequest, "medical-instance-1", 18).Status == OperationStatus.AlreadyCommitted,
    "committed request remains idempotent after save round trip");

var exposureRun = ReadyRun(RunStateCodec.CreateNew("steam-demo-25382790", gameRunId, 25, 1,
    new ConfigSnapshot { InitialExposure = 99, DailyHealthLoss = 0 }));
Check(OperationJournal.PrepareExposureRepair(exposureRun, exposureRun.RunId, exposureRun.Revision,
    Guid.NewGuid().ToString("D"), "electronic-instance-1", true).Status == OperationStatus.InvalidPhase,
    "inspection blocks exposure repair before consumption");
var repairPrepared = OperationJournal.PrepareExposureRepair(exposureRun, exposureRun.RunId, exposureRun.Revision,
    Guid.NewGuid().ToString("D"), "electronic-instance-1", false);
var repairApplying = OperationJournal.MarkApplying(repairPrepared.State, repairPrepared.OperationId!);
var repairConfirmed = OperationJournal.ConfirmExternal(repairApplying.State, repairPrepared.OperationId!,
    "electronic-instance-1", 1, "consumption-receipt-2");
var repairCommitted = OperationJournal.Commit(repairConfirmed.State, repairPrepared.OperationId!);
Check(repairCommitted.State.Exposure == 74 && repairCommitted.State.ExposureDeadlineDay == null,
    "confirmed electronic use repairs exposure and clears deadline");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(repairConfirmed.State),
      repairConfirmed.State, repairCommitted.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(repairConfirmed.State),
          repairConfirmed.State, repairCommitted.State with { Exposure = 73 },
          gameRunId, 25).Status == WriteGateStatus.InvalidTransition,
    "write gate binds exposure repair to one confirmed electronic consumption");
Check(RunStateCodec.Read(RunStateCodec.Write(repairCommitted.State)).Status == ReadStatus.Ready,
    "repaired exposure remains valid after save round trip");
var mismatch = OperationJournal.ConfirmExternal(medicalApplying.State, medicalPrepared.OperationId!,
    "wrong-medical-instance", 18, "unexpected-receipt");
Check(mismatch.Status == OperationStatus.NeedsRecovery && mismatch.State.Health == 90 &&
      mismatch.State.PendingOperation?.Phase == OperationJournal.NeedsRecovery &&
      OperationJournal.Commit(mismatch.State, medicalPrepared.OperationId!).Status == OperationStatus.NeedsRecovery,
    "unmatched external consumption freezes operation without granting health");
Check(RunStateCodec.Read(RunStateCodec.Write(mismatch.State)).Status == ReadStatus.Ready,
    "recovery state is preserved for later inspection");
Check(OperationJournal.PrepareMedical(medicalRun, Guid.NewGuid().ToString("D"), medicalRun.Revision,
    Guid.NewGuid().ToString("D"), "medical-instance-1", 18).Status == OperationStatus.RunMismatch,
    "operation cannot cross Mod run identities");
Check(OperationJournal.CancelPrepared(medicalPrepared.State, medicalPrepared.OperationId!).State.PendingOperation == null,
    "prepared operation can be cancelled before external mutation");

var debugBase = ReadyRun(RunStateCodec.CreateNew("steam-demo-25382790",
    gameRunId, 25, 1, new ConfigSnapshot()));
var debugChanged = debugBase with
{
    Revision = debugBase.Revision + 1,
    Health = 37,
    Exposure = 55,
    ExposureDeadlineDay = null
};
Check(ModDataWriteGate.DebugUpdate(true,
          RunStateCodec.Write(debugBase), debugBase,
          debugChanged, gameRunId, 25).Status == WriteGateStatus.Ready,
    "debug write gate accepts bounded health and exposure changes");
var debugFullExposure = debugChanged with
{
    Exposure = 100,
    ExposureDeadlineDay = debugChanged.LastObservedDay,
    FailureReason = "exposure"
};
Check(ModDataWriteGate.DebugUpdate(true,
          RunStateCodec.Write(debugBase), debugBase,
          debugFullExposure, gameRunId, 25).Status == WriteGateStatus.Ready &&
      ModDataWriteGate.DebugUpdate(true,
          RunStateCodec.Write(debugBase), debugBase,
          debugFullExposure with { Exposure = 99 }, gameRunId, 25).Status !=
      WriteGateStatus.Ready,
    "debug full exposure ends the run, but cannot forge an earlier failure");
Check(ModDataWriteGate.DebugUpdate(true,
          RunStateCodec.Write(debugBase), debugBase,
          debugChanged with { StartDay = debugBase.StartDay + 1 },
          gameRunId, 25).Status != WriteGateStatus.Ready,
    "debug write gate rejects unrelated state changes");
Check(ModDataWriteGate.DebugUpdate(true,
          RunStateCodec.Write(debugBase) + " ",
          debugBase, debugChanged, gameRunId, 25).Status ==
      WriteGateStatus.ContentConflict,
    "debug write gate requires exact saved-state content");
var debugDead = debugBase with { Health = 0, FailureReason = "bodyCollapse" };
var debugRecovered = debugDead with
{
    Revision = debugDead.Revision + 1, Health = 33, FailureReason = "none"
};
Check(ModDataWriteGate.DebugUpdate(true, RunStateCodec.Write(debugDead),
          debugDead, debugRecovered, gameRunId, 25).Status == WriteGateStatus.Ready &&
      ModDataWriteGate.DebugUpdate(true, RunStateCodec.Write(debugDead),
          debugDead, debugRecovered with { FailureReason = "bodyCollapse" },
          gameRunId, 25).Status == WriteGateStatus.InvalidTransition,
    "debug health recovery clears the failure instead of persisting a hidden ended run");
Check(ModDataWriteGate.DebugUpdate(true, RunStateCodec.Write(debugFullExposure),
          debugFullExposure, debugFullExposure with
          { Revision = debugFullExposure.Revision + 1, Exposure = 70,
            ExposureDeadlineDay = null, FailureReason = "none" },
          gameRunId, 25).Status == WriteGateStatus.Ready &&
      ModDataWriteGate.DebugUpdate(true, RunStateCodec.Write(debugDead), debugDead,
          debugRecovered with { Health = 0 }, gameRunId, 25).Status ==
          WriteGateStatus.InvalidTransition,
    "debug exposure recovery resumes play but cannot resume at zero health");
foreach (var playable in new[]
         { debugBase with { Health = 10 }, debugBase with { Exposure = 99 } })
{
    var failureSession = new SessionCoordinator();
    string playableJson = RunStateCodec.Write(playable);
    failureSession.Restore(playableJson, gameRunId, 25);
    var live = failureSession.Current!.State;
    var failure = SurvivalRules.ReconcileDay(live, live.LastObservedDay + 1).State;
    Check(failureSession.AcceptRuntimeFailure(live, failure,
              failure.LastObservedDay) is
              { Status: RuntimeSessionAdvanceStatus.Advanced, ReadbackStatus: null } &&
          failureSession.Current!.State.FailureReason != "none",
        "failure enters the current session without claiming saved readback");
    Check(failureSession.AcceptRuntimeFailure(live, failure,
              failure.LastObservedDay).Status == RuntimeSessionAdvanceStatus.NoCurrentSession,
        "a delayed failure cannot replace a newer session revision");
    failureSession.Restore(playableJson, gameRunId, 25);
    Check(failureSession.Current!.State is { FailureReason: "none" } &&
          failureSession.Current.State.Health == playable.Health &&
          failureSession.Current.State.Exposure == playable.Exposure &&
          failureSession.AcceptRuntimeFailure(failureSession.Current.State,
              failure with { Health = 55 }, failure.LastObservedDay).Status !=
              RuntimeSessionAdvanceStatus.Advanced,
        "loading the playable state restores vitals and rejects a forged failure");
}
var debugAllAppraisals = debugBase with
{
    Revision = debugBase.Revision + 1,
    Appraisals = debugBase.Appraisals with
    { DebugAllCategoriesUnlocked = true }
};
Check(!RunStateCodec.Write(debugBase).Contains(
          "debugAllCategoriesUnlocked", StringComparison.Ordinal) &&
      ModDataWriteGate.DebugUpdate(true,
          RunStateCodec.Write(debugBase), debugBase,
          debugAllAppraisals, gameRunId, 25).Status == WriteGateStatus.Ready &&
      RunStateCodec.Read(RunStateCodec.Write(debugAllAppraisals)).Status ==
          ReadStatus.Ready &&
      AppraisalRules.IsUnlocked(debugAllAppraisals, AppraisalRules.BionicHand) &&
      AppraisalRules.IsUnlocked(debugAllAppraisals, AppraisalRules.RibBurger),
    "debug unlock persists all eye appraisal categories without changing old JSON");
Check(ModDataWriteGate.Update(true,
          RunStateCodec.Write(debugBase), debugBase,
          debugAllAppraisals, gameRunId, 25).Status ==
          WriteGateStatus.InvalidTransition,
    "ordinary update cannot create a debug appraisal unlock");
var medicalSource = debugBase with { LastObservedDay = 8, LastSettledDay = 7 };
Check(CyberneticRules.Preview(debugBase, CyberneticRules.CyberEye, 1, 450)
      .Status == CyberneticStatus.Ready,
    "cybernetic catalog is available before Wilde's chapter");
Check(CyberneticRules.Preview(medicalSource, CyberneticRules.CyberEye, 8, 449) is
      { Status: CyberneticStatus.InsufficientCredits, Price: 450 },
    "cybernetic price and current cash gate use the run's configuration");
Check(CyberneticRules.Prepare(medicalSource, Guid.NewGuid().ToString("D"),
      medicalSource.Revision, Guid.NewGuid().ToString("D"),
       CyberneticRules.CyberEye, 8, 450).Status == CyberneticStatus.RunMismatch,
    "purchase reservation cannot cross Mod run identities");
var eyeRequest = Guid.NewGuid().ToString("D");
var eyePrepared = CyberneticRules.Prepare(medicalSource, medicalSource.RunId,
    medicalSource.Revision, eyeRequest, CyberneticRules.CyberEye, 8, 450);
Check(eyePrepared.Status == CyberneticStatus.Applied &&
      eyePrepared.State.Cybernetics.PendingPurchase is
      { ItemId: CyberneticRules.CyberEye, Price: 450, Phase: CyberneticRules.Prepared } &&
      RunStateCodec.Read(RunStateCodec.Write(eyePrepared.State)).Status == ReadStatus.Ready,
    "eye purchase reserves an exact quote without marking the item purchased");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(medicalSource),
      medicalSource, eyePrepared.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready,
    "cybernetic write gate accepts a quoted purchase reservation");
Check(CyberneticRules.Prepare(eyePrepared.State, medicalSource.RunId,
       medicalSource.Revision, eyeRequest, CyberneticRules.CyberEye, 8, 450).Status ==
      CyberneticStatus.Duplicate,
    "replayed purchase reservation does not reserve a second payment");
Check(SurvivalRules.SettleDay(eyePrepared.State with { LastSettledDay = 7 }, 8).Status ==
      SettleStatus.OperationPending,
    "pending purchase blocks day settlement");
Check(CyberneticRules.MarkApplying(eyePrepared.State, eyeRequest, 449).Status ==
      CyberneticStatus.BalanceChanged &&
      eyePrepared.State.Cybernetics.PendingPurchase?.Phase == CyberneticRules.Prepared,
    "cash is rechecked before native purchase execution");
var eyeApplying = CyberneticRules.MarkApplying(eyePrepared.State, eyeRequest, 450);
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(eyePrepared.State),
      eyePrepared.State, eyeApplying.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready,
    "cybernetic write gate accepts payment intent after balance recheck");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(eyeApplying.State),
      eyeApplying.State, eyeApplying.State with
      {
          Revision = eyeApplying.State.Revision + 1,
          LastObservedDay = eyeApplying.State.LastObservedDay + 1
      }, gameRunId, 25).Status == WriteGateStatus.InvalidTransition,
    "pending cybernetic payment cannot advance its saved game day");
var eyeWrongCash = CyberneticRules.ConfirmExternal(eyeApplying.State,
    eyeRequest, 1, "wrong-cash-evidence");
Check(eyeWrongCash.Status == CyberneticStatus.NeedsRecovery &&
      eyeWrongCash.State.Cybernetics.Purchases.Length == 0 &&
      eyeWrongCash.State.Cybernetics.PendingPurchase?.Phase ==
      CyberneticRules.NeedsRecovery &&
      eyeWrongCash.State.Cybernetics.PendingPurchase?.RecoveryReason ==
      "externalResultMismatch" &&
      eyeWrongCash.State.Cybernetics.PendingPurchase?.RecoveryObservation?
          .CreditsAfter == 1 &&
      RunStateCodec.Read(RunStateCodec.Write(eyeWrongCash.State)).Status == ReadStatus.Ready,
    "cash mismatch freezes purchase without granting the eye");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(eyeApplying.State),
      eyeApplying.State, eyeWrongCash.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready,
    "cybernetic write gate persists uncertain payment for reconciliation");
var eyeConfirmed = CyberneticRules.ConfirmExternal(eyeApplying.State,
    eyeRequest, 0, "native-eye-payment");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(eyeApplying.State),
      eyeApplying.State, eyeConfirmed.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready,
    "cybernetic write gate records an exact external payment observation");
var eyePurchased = CyberneticRules.Commit(eyeConfirmed.State, eyeRequest);
Check(eyePurchased.Status == CyberneticStatus.Applied &&
       eyePurchased.State.Cybernetics.Purchased(CyberneticRules.CyberEye) &&
       eyePurchased.State.Cybernetics.Enabled(CyberneticRules.CyberEye) &&
      eyePurchased.State.Cybernetics.PendingPurchase == null &&
      RunStateCodec.Read(RunStateCodec.Write(eyePurchased.State)).Status == ReadStatus.Ready,
    "confirmed cash payment persists the purchased and enabled eye");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(eyeConfirmed.State),
      eyeConfirmed.State, eyePurchased.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(medicalSource),
          medicalSource, eyePurchased.State with
          { Revision = medicalSource.Revision + 1 },
          gameRunId, 25).Status == WriteGateStatus.InvalidTransition,
    "cybernetic write gate requires payment phases before a purchase receipt");
var removedPurchase = eyePurchased.State with
{
    Revision = eyePurchased.State.Revision + 1,
    Cybernetics = eyePurchased.State.Cybernetics with
    { Purchases = [], EnabledIds = [] }
};
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(eyePurchased.State),
      eyePurchased.State, removedPurchase, eyePurchased.State.GameRunId,
      eyePurchased.State.SaveSlotId).Status == WriteGateStatus.InvalidTransition,
    "ordinary state update cannot erase an existing cybernetic purchase receipt");
Check(CyberneticRules.Commit(eyePurchased.State, eyeRequest).Status ==
      CyberneticStatus.AlreadyCommitted &&
      CyberneticRules.Prepare(eyePurchased.State, eyePurchased.State.RunId,
           eyePurchased.State.Revision, eyeRequest, CyberneticRules.CyberEye, 8, 450).Status ==
      CyberneticStatus.AlreadyCommitted,
    "completed purchase cannot charge a repeated request after reload");
var eyeDisabled = CyberneticRules.SetEnabled(eyePurchased.State,
    eyePurchased.State.RunId, eyePurchased.State.Revision, CyberneticRules.CyberEye, false);
Check(eyeDisabled.Status == CyberneticStatus.Applied &&
       eyeDisabled.State.Cybernetics.Purchased(CyberneticRules.CyberEye) &&
       !eyeDisabled.State.Cybernetics.Enabled(CyberneticRules.CyberEye) &&
      RunStateCodec.Read(RunStateCodec.Write(eyeDisabled.State)).Status == ReadStatus.Ready,
    "disabling an eye preserves its permanent purchase receipt");
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(eyePurchased.State),
      eyePurchased.State, eyeDisabled.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready,
    "cybernetic write gate accepts one purchased eye toggle");
var reloadedEyePurchase = RunStateCodec.Read(
    RunStateCodec.Write(eyePurchased.State)).State!;
var disabledReloadedEye = CyberneticRules.SetEnabled(reloadedEyePurchase,
    reloadedEyePurchase.RunId, reloadedEyePurchase.Revision,
    CyberneticRules.CyberEye, false);
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(reloadedEyePurchase),
      reloadedEyePurchase, disabledReloadedEye.State,
      gameRunId, 25).Status == WriteGateStatus.Ready,
    "cybernetic enable transition compares a reloaded purchase receipt by value");
Check(CyberneticRules.SetEnabled(medicalSource, medicalSource.RunId,
       medicalSource.Revision, CyberneticRules.CyberEye, true).Status ==
      CyberneticStatus.InvalidPhase,
    "unbought cybernetic cannot be enabled through the toggle rule");
Check(RunStateCodec.Read(InvalidHistory(eyePurchased.State, json =>
      json["cybernetics"]!["purchases"]![0]!["price"] = 899)
      .ToJsonString()).Status == ReadStatus.Corrupt,
    "purchase receipt cannot silently lower its snapshotted catalog price");
Check(RunStateCodec.Read(InvalidHistory(eyePurchased.State, json =>
       json["cybernetics"]!["enabledIds"]![0] = CyberneticRules.AiAssistantCore)
      .ToJsonString()).Status == ReadStatus.Corrupt,
    "unbought cybernetic cannot be smuggled into enabled state");
Check(RunStateCodec.Read(InvalidHistory(eyePurchased.State, json =>
      json["cybernetics"]!["purchases"]![0]!["creditsAfter"] = 1)
      .ToJsonString()).Status == ReadStatus.Corrupt,
    "purchase receipt must preserve the exact native cash difference");

var networkAnkle = CyberneticRules.GrantNetworkUnlock(medicalSource,
    medicalSource.RunId, medicalSource.Revision, CyberneticRules.ArtificialAnkle,
    CyberneticRules.NetworkUpgradeId(CyberneticRules.ArtificialAnkle), 300, 150, 8);
Check(networkAnkle.Status == CyberneticStatus.Applied &&
      networkAnkle.State.Cybernetics.Purchased(CyberneticRules.ArtificialAnkle) &&
      networkAnkle.State.Cybernetics.Enabled(CyberneticRules.ArtificialAnkle) &&
      networkAnkle.State.Cybernetics.NetworkUnlocks is
          [{ WildFavorCost: 300, WildFavorAfter: 150 }] &&
      RunStateCodec.Read(RunStateCodec.Write(networkAnkle.State)).Status == ReadStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(medicalSource),
          medicalSource, networkAnkle.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready,
    "artificial-ankle network unlock is persisted as one exact native receipt");
Check(CyberneticRules.GrantNetworkUnlock(networkAnkle.State,
      networkAnkle.State.RunId, networkAnkle.State.Revision, CyberneticRules.ArtificialAnkle,
      CyberneticRules.NetworkUpgradeId(CyberneticRules.ArtificialAnkle), 300, 150, 8).Status ==
      CyberneticStatus.AlreadyPurchased &&
      CyberneticRules.GrantNetworkUnlock(medicalSource,
          medicalSource.RunId, medicalSource.Revision, CyberneticRules.ArtificialAnkle,
          CyberneticRules.NetworkUpgradeId(CyberneticRules.MimeticSkin), 300, 150, 8).Status ==
      CyberneticStatus.InvalidInput &&
      CyberneticRules.GrantNetworkUnlock(medicalSource,
          medicalSource.RunId, medicalSource.Revision, CyberneticRules.ArtificialAnkle,
          CyberneticRules.NetworkUpgradeId(CyberneticRules.ArtificialAnkle), 299, 150, 8).Status ==
      CyberneticStatus.InvalidInput,
    "network unlock replay, item mapping, and snapshotted price are guarded");
var networkSkin = CyberneticRules.GrantNetworkUnlock(networkAnkle.State,
    networkAnkle.State.RunId, networkAnkle.State.Revision, CyberneticRules.MimeticSkin,
    CyberneticRules.NetworkUpgradeId(CyberneticRules.MimeticSkin), 500, 25, 8);
Check(networkSkin.Status == CyberneticStatus.Applied &&
      networkSkin.State.Cybernetics.Purchased(CyberneticRules.MimeticSkin) &&
      networkSkin.State.Cybernetics.NetworkUnlocks?.Length == 2 &&
      SurvivalRules.SettleDay(networkSkin.State with { LastSettledDay = 7 }, 8)
          .State.Exposure == 25 &&
      RunStateCodec.Read(RunStateCodec.Write(networkSkin.State)).Status == ReadStatus.Ready,
    "mimetic-skin unlock is progression only and cannot enable an absent product");
Check(RunStateCodec.Read(InvalidHistory(networkAnkle.State, json =>
      json["cybernetics"]!["networkUnlocks"]![0]!["wildFavorCost"] = 199)
      .ToJsonString()).Status == ReadStatus.Corrupt,
    "network cybernetic receipt cannot change its snapshotted Wild Favor price");

var creditAnkle = CyberneticRules.GrantWorkshopUnlock(medicalSource,
    medicalSource.RunId, medicalSource.Revision, CyberneticRules.ArtificialAnkle,
    500, 200, "common_electronic", 2468, 2, 8);
Check(creditAnkle.Status == CyberneticStatus.Applied &&
      creditAnkle.State.Cybernetics.NetworkUnlocks is
          [{ CreditsCost: 300, CreditsBefore: 500, CreditsAfter: 200,
             MaterialItemId: "common_electronic", MaterialUniqueId: 2468,
             MaterialUnitsBefore: 2, WildFavorCost: 0 }] &&
      RunStateCodec.Read(RunStateCodec.Write(creditAnkle.State)).Status ==
          ReadStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(medicalSource),
          medicalSource, creditAnkle.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready,
    "workshop unlock records exact credit and configured item payment without favor");
Check(CyberneticRules.GrantWorkshopUnlock(medicalSource,
      medicalSource.RunId, medicalSource.Revision, CyberneticRules.ArtificialAnkle,
      500, 200, "advanced_electronic", 2468, 2, 8).Status ==
      CyberneticStatus.InvalidInput &&
      RunStateCodec.Read(InvalidHistory(creditAnkle.State, json =>
          json["cybernetics"]!["networkUnlocks"]![0]!["creditsAfter"] = 201)
          .ToJsonString()).Status == ReadStatus.Corrupt,
    "workshop payment rejects wrong material and altered credit receipt");

var networkEye = CyberneticRules.GrantNetworkUnlock(medicalSource,
    medicalSource.RunId, medicalSource.Revision, CyberneticRules.CyberEye,
    CyberneticRules.NetworkUpgradeId(CyberneticRules.CyberEye), 450, 50, 8);
var eyeDelivered = CyberneticRules.RecordCyberEyeDelivery(networkEye.State,
    networkEye.State.RunId, networkEye.State.Revision, 31415, 8,
    "native-item:31415:owned");
Check(networkEye.Status == CyberneticStatus.Applied &&
      eyeDelivered.Status == CyberneticStatus.Applied &&
      eyeDelivered.State.Cybernetics.CyberEyeDelivery is
          { ItemId: CyberneticRules.CyberEye, NativeUniqueId: 31415, Day: 8 } &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(networkEye.State),
          networkEye.State, eyeDelivered.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready &&
      RunStateCodec.Read(RunStateCodec.Write(eyeDelivered.State)).Status ==
          ReadStatus.Ready,
    "cyber-eye network unlock records one saved native-item delivery");
Check(CyberneticRules.RecordCyberEyeDelivery(eyeDelivered.State,
      eyeDelivered.State.RunId, eyeDelivered.State.Revision, 31416, 8,
      "native-item:31416:owned").Status == CyberneticStatus.AlreadyCommitted &&
      CyberneticRules.RecordCyberEyeDelivery(medicalSource,
          medicalSource.RunId, medicalSource.Revision, 31415, 8,
          "native-item:31415:owned").Status == CyberneticStatus.InvalidPhase &&
      RunStateCodec.Read(InvalidHistory(eyeDelivered.State, json =>
          json["cybernetics"]!["cyberEyeDelivery"]!["evidenceId"] =
              "native-item:999:owned").ToJsonString()).Status == ReadStatus.Corrupt,
    "cyber-eye delivery is unlock-gated, idempotent, and identity-bound");

var configuredMobility = ConfigSnapshotLoader.Parse(
    "{\"cyberneticPrices\":{\"ankleExtraScavAttempts\":2," +
    "\"infraredWoundChanceMultiplierBp\":5000," +
    "\"unlockMaterialItemIds\":[\"common_electronic\",\"advanced_electronic\"]}}");
Check(configuredMobility.Status == ConfigLoadStatus.Loaded &&
      configuredMobility.Config.CyberneticPrices.AnkleExtraScavAttempts == 2 &&
      configuredMobility.Config.CyberneticPrices.InfraredWoundChanceMultiplierBp == 5000 &&
      configuredMobility.Config.CyberneticPrices.UnlockMaterialItemIds.Length == 2 &&
      ConfigSnapshotLoader.Parse(
          "{\"cyberneticPrices\":{\"ankleExtraScavAttempts\":11}}")
          .Status == ConfigLoadStatus.Invalid &&
      ConfigSnapshotLoader.Parse(
          "{\"cyberneticPrices\":{\"infraredWoundChanceMultiplierBp\":10001}}")
          .Status == ConfigLoadStatus.Invalid,
    "mobility effects and configurable material candidates are snapshotted and bounded");
var configuredMaterials = ConfigSnapshotLoader.Parse(
    "{\"cyberneticPrices\":{\"unlockMaterials\":{" +
    "\"artificialAnkle\":\"advanced_electronic\"," +
    "\"mechanicalAscension\":\"restricted_medical\"}}}");
Check(configuredMaterials.Status == ConfigLoadStatus.Loaded &&
      CyberneticRules.MaterialItemId(configuredMaterials.Config.CyberneticPrices,
          CyberneticRules.ArtificialAnkle) == "advanced_electronic" &&
      CyberneticRules.MaterialItemId(configuredMaterials.Config.CyberneticPrices,
          CyberneticRules.SleepControl) == "common_electronic" &&
      configuredMaterials.Config.CyberneticPrices.UnlockMaterials?.MechanicalAscension ==
          "restricted_medical" &&
      ConfigSnapshotLoader.Parse(
          "{\"cyberneticPrices\":{\"unlockMaterials\":{\"artificialAnkle\":\"\"}}}")
          .Status == ConfigLoadStatus.Invalid &&
      ConfigSnapshotLoader.Parse(
          "{\"cyberneticPrices\":{\"unlockMaterials\":{\"unknown\":\"x\"}}}")
          .Status == ConfigLoadStatus.Invalid,
    "each workshop unlock has a bounded independently configured item ID");
var configuredRecipes = ConfigSnapshotLoader.Parse("""
    {"cyberneticPrices":{"unlockRecipes":{
      "artificialAnkle":[
        {"itemId":"common_electronic","quantity":3},
        {"itemId":"metal_ingot","quantity":2}],
      "mechanicalAscension":[{"itemId":"advanced_electronic","quantity":4}]
    }}}
    """);
Check(configuredRecipes.Status == ConfigLoadStatus.Loaded &&
      CyberneticRules.Materials(configuredRecipes.Config.CyberneticPrices,
          CyberneticRules.ArtificialAnkle) is
          [{ ItemId: "common_electronic", Quantity: 3 },
           { ItemId: "metal_ingot", Quantity: 2 }] &&
      CyberneticRules.Materials(configuredRecipes.Config.CyberneticPrices,
          CyberneticRules.SleepControl) is
          [{ ItemId: "common_electronic", Quantity: 1 }] &&
      CyberneticRules.Materials(configuredRecipes.Config.CyberneticPrices,
          "nicokobo.aug.mechanical_ascension") is
          [{ ItemId: "advanced_electronic", Quantity: 4 }] &&
      ConfigSnapshotLoader.Parse("""
          {"cyberneticPrices":{"unlockRecipes":{"artificialAnkle":
            [{"itemId":"common_electronic","quantity":0}]}}}
          """).Status == ConfigLoadStatus.Invalid &&
      ConfigSnapshotLoader.Parse("""
          {"cyberneticPrices":{"unlockRecipes":{"artificialAnkle":
            [{"itemId":"common_electronic","quantity":1,"extra":1}]}}}
          """).Status == ConfigLoadStatus.Invalid,
    "workshop recipes show per-item quantities and reject invalid ingredients");
var recipeSnapshot = RunStateCodec.CreateNew("test", gameRunId, 0, 8,
    configuredRecipes.Config);
var recipeSource = medicalSource with
{
    ConfigSnapshot = recipeSnapshot.ConfigSnapshot,
    ConfigHash = recipeSnapshot.ConfigHash
};
WorkshopMaterialReceipt[] paidRecipe =
[
    new("common_electronic", 3101, 2, 2),
    new("common_electronic", 3102, 3, 1),
    new("metal_ingot", 3103, 2, 2)
];
var recipePurchase = CyberneticRules.GrantWorkshopUnlockWithMaterials(recipeSource,
    recipeSource.RunId, recipeSource.Revision, CyberneticRules.ArtificialAnkle,
    500, 200, paidRecipe, 8);
Check(recipePurchase.Status == CyberneticStatus.Applied &&
      recipePurchase.State.Cybernetics.NetworkUnlocks?[0].Materials?.Length == 3 &&
      RunStateCodec.Read(RunStateCodec.Write(recipePurchase.State)).Status ==
          ReadStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(recipeSource),
          recipeSource, recipePurchase.State, gameRunId, 25).Status ==
          WriteGateStatus.Ready &&
      CyberneticRules.GrantWorkshopUnlockWithMaterials(recipeSource,
          recipeSource.RunId, recipeSource.Revision,
          CyberneticRules.ArtificialAnkle, 500, 200,
          paidRecipe[..^1], 8).Status == CyberneticStatus.InvalidInput &&
      RunStateCodec.Read(InvalidHistory(recipePurchase.State, json =>
          json["cybernetics"]!["networkUnlocks"]![0]!["materials"]![0]!["quantity"] = 1)
          .ToJsonString()).Status == ReadStatus.Corrupt,
    "multi-item workshop receipt validates split stacks and exact quantities");

Check(!AppraisalRules.IsUnlocked(eyePurchased.State,
          AppraisalRules.Cigarette) &&
      !AppraisalRules.IsUnlocked(eyePurchased.State,
          AppraisalRules.FoodCoupon) &&
      !AppraisalRules.IsUnlocked(medicalSource,
          AppraisalRules.Cigarette),
    "eye purchase requires three perfect manual appraisals for every category");
var couponSubmission = new ManualAppraisalInput("coupon-instance-1",
    AppraisalRules.FoodCoupon, Guid.NewGuid().ToString("D"), 8,
    true, true, false, "stable-coupon-id-1", "coupon-answer-1",
    InspectionComplete: true,
    ActualFindings: ["face:10", "seal:valid"],
    SubmittedFindings: ["seal:valid", "face:10"]);
var trainedBeforeEye = AppraisalRules.Submit(medicalSource,
    medicalSource.RunId, medicalSource.Revision,
    couponSubmission with
    {
        ItemInstanceId = "pre-eye-coupon",
        RequestId = Guid.NewGuid().ToString("D")
    });
Check(trainedBeforeEye.Status == AppraisalStatus.Applied &&
      AppraisalRules.PerfectCount(trainedBeforeEye.State,
          AppraisalRules.FoodCoupon) == 1 &&
      !AppraisalRules.IsUnlocked(trainedBeforeEye.State,
          AppraisalRules.FoodCoupon),
    "manual training starts before purchase but scanning waits for the eye");
Check(AppraisalRules.Submit(eyePurchased.State,
      eyePurchased.State.RunId, eyePurchased.State.Revision,
      couponSubmission with
      {
          ItemInstanceId = "unchecked-coupon",
          RequestId = Guid.NewGuid().ToString("D"),
          InspectionComplete = false,
          ActualFindings = [], SubmittedFindings = []
      }).Status == AppraisalStatus.InvalidEvidence,
    "empty findings need explicit completed inspection evidence");
var firstCoupon = AppraisalRules.Submit(eyePurchased.State,
    eyePurchased.State.RunId, eyePurchased.State.Revision,
    couponSubmission);
Check(firstCoupon.Status == AppraisalStatus.Applied &&
      firstCoupon.Receipt?.Perfect == true &&
      AppraisalRules.PerfectCount(firstCoupon.State,
          AppraisalRules.FoodCoupon) == 1 &&
      RunStateCodec.Read(RunStateCodec.Write(firstCoupon.State)).Status ==
      ReadStatus.Ready,
    "first unrevealed exact manual answer credits one stable food coupon");
Check(ModDataWriteGate.Update(true,
      RunStateCodec.Write(eyePurchased.State), eyePurchased.State,
      firstCoupon.State, eyePurchased.State.GameRunId,
      eyePurchased.State.SaveSlotId).Status == WriteGateStatus.Ready,
    "first manual training receipt stages in one validated Mod revision");
var removedTraining = firstCoupon.State with
{
    Revision = firstCoupon.State.Revision + 1,
    Appraisals = firstCoupon.State.Appraisals with { Submissions = [] }
};
Check(ModDataWriteGate.Update(true,
      RunStateCodec.Write(firstCoupon.State), firstCoupon.State,
      removedTraining, firstCoupon.State.GameRunId,
      firstCoupon.State.SaveSlotId).Status ==
      WriteGateStatus.InvalidTransition,
    "ordinary state update cannot erase an item's first appraisal answer");
Check(AppraisalRules.Submit(firstCoupon.State, firstCoupon.State.RunId,
      firstCoupon.State.Revision, couponSubmission).Status ==
      AppraisalStatus.Duplicate &&
      AppraisalRules.Submit(firstCoupon.State, firstCoupon.State.RunId,
          firstCoupon.State.Revision, couponSubmission with
          { RequestId = Guid.NewGuid().ToString("D") }).Status ==
      AppraisalStatus.AlreadyHandled,
    "one item cannot contribute a second manual training result");
var wrongCoupon = AppraisalRules.Submit(firstCoupon.State,
    firstCoupon.State.RunId, firstCoupon.State.Revision,
    couponSubmission with
    {
        ItemInstanceId = "coupon-instance-2",
        RequestId = Guid.NewGuid().ToString("D"),
        SubmittedFindings = ["face:10", "seal:valid", "chip:fake"]
    });
Check(wrongCoupon.Status == AppraisalStatus.Applied &&
      wrongCoupon.Receipt?.Perfect == false &&
      AppraisalRules.PerfectCount(wrongCoupon.State,
          AppraisalRules.FoodCoupon) == 1 &&
      AppraisalRules.Submit(wrongCoupon.State, wrongCoupon.State.RunId,
          wrongCoupon.State.Revision, couponSubmission with
          {
              ItemInstanceId = "coupon-instance-2",
              RequestId = Guid.NewGuid().ToString("D")
          }).Status == AppraisalStatus.AlreadyHandled,
    "false positive answer consumes the item's first training attempt");
Check(AppraisalRules.Submit(wrongCoupon.State, wrongCoupon.State.RunId,
      wrongCoupon.State.Revision, couponSubmission with
      {
          ItemInstanceId = "split-coupon",
          RequestId = Guid.NewGuid().ToString("D"),
          IdentityStable = false
      }).Status == AppraisalStatus.UnstableIdentity &&
      AppraisalRules.Submit(wrongCoupon.State, wrongCoupon.State.RunId,
          wrongCoupon.State.Revision, couponSubmission with
          {
              ItemInstanceId = "revealed-coupon",
              RequestId = Guid.NewGuid().ToString("D"),
              RevealedBeforeSubmission = true
          }).Status == AppraisalStatus.InvalidEvidence,
    "unsupported split identity and revealed answers cannot train the eye");
var nativeFuel = AppraisalRules.Submit(wrongCoupon.State,
    wrongCoupon.State.RunId, wrongCoupon.State.Revision,
    new ManualAppraisalInput("fuel-instance-1",
        AppraisalRules.PlasmaFuelCell, Guid.NewGuid().ToString("D"),
        8, true, true, false, "stable-fuel-id", "native-perfect-1",
        NativePerfect: true));
Check(nativeFuel.Status == AppraisalStatus.Applied &&
      nativeFuel.Receipt?.VerificationKind == AppraisalRules.NativePerfect &&
      AppraisalRules.PerfectCount(nativeFuel.State,
          AppraisalRules.PlasmaFuelCell) == 1,
    "fuel-cell training uses a verified native perfect result");
var threeCoupons = wrongCoupon.State;
for (int i = 3; i <= 4; i++)
{
    threeCoupons = AppraisalRules.Submit(threeCoupons, threeCoupons.RunId,
        threeCoupons.Revision, couponSubmission with
        {
            ItemInstanceId = $"coupon-instance-{i}",
            RequestId = Guid.NewGuid().ToString("D")
        }).State;
    if (i == 3)
        Check(!AppraisalRules.IsUnlocked(threeCoupons,
            AppraisalRules.FoodCoupon),
            "two perfect food-coupon submissions do not unlock the category");
}
Check(AppraisalRules.PerfectCount(threeCoupons,
          AppraisalRules.FoodCoupon) == 3 &&
      AppraisalRules.IsUnlocked(threeCoupons,
          AppraisalRules.FoodCoupon) &&
      RunStateCodec.Read(RunStateCodec.Write(threeCoupons)).Status ==
      ReadStatus.Ready,
    "third perfect food-coupon submission unlocks only that category");
var scanRequest = Guid.NewGuid().ToString("D");
var cigaretteResult = new AppraisalScanResult(
    [new AppraisalFact("authentic", "true"),
     new AppraisalFact("anomaly", "none")], null);
var cigaretteScan = new EyeScanInput(scanRequest,
    "cigarette-instance-1", AppraisalRules.Cigarette,
    "cigarette-fingerprint-v1", "cigarette-adapter-v1",
    cigaretteResult, 8, true, true, "stable-cigarette-id",
    "eye-tool-instance-1", true, "owned-eye-tool-1", true,
    "battery-instance-1", true, "owned-battery-1", 15);
Check(new ConfigSnapshot().EyeScanBatteryCost == 15,
    "new runs default to fifteen battery power for the cyber eye");
var trainedCigarettes = eyePurchased.State;
for (int index = 0; index < 3; index++)
{
    var trained = AppraisalRules.Submit(trainedCigarettes,
        trainedCigarettes.RunId, trainedCigarettes.Revision,
        new ManualAppraisalInput($"trained-cigarette-{index}",
            AppraisalRules.Cigarette, Guid.NewGuid().ToString("D"), 8,
            true, true, false, $"cigarette-identity-{index}",
            $"cigarette-outcome-{index}", InspectionComplete: true,
            ActualFindings: ["authentic:true"],
            SubmittedFindings: ["authentic:true"]));
    Check(trained.Status == AppraisalStatus.Applied,
        "cigarette manual appraisal training is accepted");
    trainedCigarettes = trained.State;
}
Check(AppraisalRules.IsUnlocked(trainedCigarettes,
        AppraisalRules.Cigarette),
    "three perfect cigarette appraisals unlock eye automation");
var eyeWithTool = trainedCigarettes with
{ EyeToolInstanceId = "eye-tool-instance-1" };
var disabledEyeWithTool = CyberneticRules.SetEnabled(eyeWithTool,
    eyeWithTool.RunId, eyeWithTool.Revision,
    CyberneticRules.CyberEye, false).State;
Check(AppraisalScanRules.Prepare(eyePurchased.State,
      eyePurchased.State.RunId, eyePurchased.State.Revision,
      cigaretteScan).Status == EyeScanStatus.StateChanged &&
      AppraisalScanRules.Prepare(eyeWithTool,
          eyeWithTool.RunId, eyeWithTool.Revision,
          cigaretteScan with { BatteryAttachedToEyeTool = false })
          .Status == EyeScanStatus.StateChanged,
    "scan needs the saved eye tool and a verified attached battery slot");
Check(AppraisalScanRules.Prepare(disabledEyeWithTool,
      disabledEyeWithTool.RunId,
      disabledEyeWithTool.Revision,
      cigaretteScan).Status == EyeScanStatus.EyeDisabled &&
      AppraisalScanRules.Prepare(eyeWithTool,
          eyeWithTool.RunId, eyeWithTool.Revision,
          cigaretteScan with { BatteryCharge = 4 }).Status ==
      EyeScanStatus.NoPower &&
      AppraisalScanRules.Prepare(eyeWithTool,
          eyeWithTool.RunId, eyeWithTool.Revision,
          cigaretteScan with
          { CategoryId = AppraisalRules.FoodCoupon }).Status ==
      EyeScanStatus.EyeLocked,
    "unbought training category, disabled eye and low battery block scanning");
var scanPrepared = AppraisalScanRules.Prepare(eyeWithTool,
    eyeWithTool.RunId, eyeWithTool.Revision,
    cigaretteScan);
Check(scanPrepared.Status == EyeScanStatus.Applied &&
      scanPrepared.State.Appraisals.PendingScan?.Phase ==
      AppraisalScanRules.Prepared &&
      scanPrepared.State.Appraisals.ScanReceipts.Length == 0 &&
      RunStateCodec.Read(RunStateCodec.Write(scanPrepared.State)).Status ==
      ReadStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(eyeWithTool),
          eyeWithTool, scanPrepared.State,
          eyeWithTool.GameRunId,
          eyeWithTool.SaveSlotId).Status == WriteGateStatus.Ready,
    "scan preparation persists intent without charging or revealing cache");
var swappedEyeTool = scanPrepared.State with
{
    Revision = scanPrepared.State.Revision + 1,
    EyeToolInstanceId = "eye-tool-instance-2"
};
Check(ModDataWriteGate.Update(true,
      RunStateCodec.Write(scanPrepared.State), scanPrepared.State,
      swappedEyeTool, scanPrepared.State.GameRunId,
      scanPrepared.State.SaveSlotId).Status ==
      WriteGateStatus.InvalidTransition &&
      RunStateCodec.Read(InvalidHistory(scanPrepared.State, json =>
          json["appraisals"]!["pendingScan"]!["eyeToolInstanceId"] =
              "eye-tool-instance-2").ToJsonString()).Status ==
      ReadStatus.Corrupt,
    "saved scan cannot switch the bound eye tool instance");
Check(AppraisalScanRules.Prepare(scanPrepared.State,
      scanPrepared.State.RunId, eyeWithTool.Revision,
      cigaretteScan).Status == EyeScanStatus.Duplicate,
    "replayed scan request does not reserve another battery debit");
var scanFresh = new EyeScanFreshSnapshot(8,
    "cigarette-instance-1", "cigarette-fingerprint-v1", true,
    true, "eye-tool-instance-1", true,
    "owned-eye-tool-1", true,
    "battery-instance-1", true, 15,
    "stable-cigarette-id", "owned-battery-1");
Check(AppraisalScanRules.MarkApplying(scanPrepared.State,
      scanRequest, scanPrepared.State.Revision,
          scanFresh with { BatteryCharge = 14 }).Status ==
      EyeScanStatus.StateChanged &&
      AppraisalScanRules.MarkApplying(scanPrepared.State,
          scanRequest, scanPrepared.State.Revision,
          scanFresh with { BatteryAttachedToEyeTool = false })
          .Status == EyeScanStatus.StateChanged,
    "battery charge and eye-tool battery slot are rechecked before the debit");
var scanApplying = AppraisalScanRules.MarkApplying(scanPrepared.State,
    scanRequest, scanPrepared.State.Revision, scanFresh);
Check(scanApplying.Status == EyeScanStatus.Applied &&
      scanApplying.State.Appraisals.PendingScan?.Phase ==
      AppraisalScanRules.Applying,
    "scan enters applying before calling the native battery debit");
var debitRequest = new BatteryDebitRequest(scanRequest, 15, 15);
var fullDebitPort = new FakeBatteryDebitPort(15, 15);
var fullDebit = BatteryDebitTransaction.Apply(fullDebitPort, debitRequest);
Check(fullDebit.Status == BatteryDebitStatus.Applied &&
      fullDebit.Before?.Charge == 15 && fullDebit.After?.Charge == 0 &&
      fullDebit.NativeReturned == 15 && fullDebitPort.Calls == 1 &&
      BatteryDebitTransaction.Apply(fullDebitPort, debitRequest).Status ==
      BatteryDebitStatus.SnapshotConflict && fullDebitPort.Calls == 1,
    "exact native battery debit is observed and an old request cannot debit twice");
var lowBandwidthPort = new FakeBatteryDebitPort(15, 4);
Check(BatteryDebitTransaction.Apply(lowBandwidthPort, debitRequest).Status ==
      BatteryDebitStatus.NoPower && lowBandwidthPort.Calls == 0,
    "battery bandwidth below the scan cost prevents any native debit");
var changedBatteryPort = new FakeBatteryDebitPort(15, 15)
    { ChangeOnSecondRead = true };
Check(BatteryDebitTransaction.Apply(changedBatteryPort, debitRequest).Status ==
      BatteryDebitStatus.SnapshotConflict && changedBatteryPort.Calls == 0,
    "battery charge drift at the final precheck prevents native debit");
var partialDebitPort = new FakeBatteryDebitPort(15, 15)
    { FixedRemoved = 3 };
var partialDebit = BatteryDebitTransaction.Apply(partialDebitPort, debitRequest);
var partialRecovery = AppraisalScanRules.RecordUncertainNativeDebit(
    scanApplying.State, partialDebit, "native-partial-battery-1");
Check(partialDebit.Status == BatteryDebitStatus.Partial &&
      partialDebit.After?.Charge == 12 && partialDebitPort.Calls == 1 &&
      partialRecovery.Status == EyeScanStatus.NeedsRecovery &&
      partialRecovery.State.Appraisals.PendingScan?.Observation?.ChargeAfter == 12 &&
      RunStateCodec.Read(RunStateCodec.Write(partialRecovery.State)).Status ==
      ReadStatus.Ready,
    "partial native debit persists an observed recovery state without a scan result");
var misleadingDebitPort = new FakeBatteryDebitPort(15, 15)
    { FixedRemoved = 3, ReportedRemoved = 5 };
var misleadingDebit = BatteryDebitTransaction.Apply(misleadingDebitPort,
    debitRequest);
Check(misleadingDebit.Status == BatteryDebitStatus.Partial &&
      misleadingDebit.NativeReturned == 5 &&
      misleadingDebit.After?.Charge == 12 &&
      AppraisalScanRules.ConfirmNativeDebit(scanApplying.State,
          misleadingDebit, "misleading-debit").Status ==
      EyeScanStatus.InvalidEvidence,
    "native return value alone cannot confirm an exact fifteen-point debit");
var throwingDebitPort = new FakeBatteryDebitPort(15, 15)
    { ThrowAfterCall = true };
var throwingDebit = BatteryDebitTransaction.Apply(throwingDebitPort,
    debitRequest);
Check(throwingDebit.Status == BatteryDebitStatus.Partial &&
      throwingDebit.NativeCallAttempted && throwingDebit.After?.Charge == 0 &&
      throwingDebitPort.Calls == 1,
    "native exception after power mutation is observed without retry");
var unreadableDebitPort = new FakeBatteryDebitPort(15, 15)
    { LoseReadAfterCall = true };
var unreadableDebit = BatteryDebitTransaction.Apply(unreadableDebitPort,
    debitRequest);
var unknownRecovery = AppraisalScanRules.RecordUncertainNativeDebit(
    scanApplying.State, unreadableDebit, "native-unknown-battery-1");
Check(unreadableDebit.Status == BatteryDebitStatus.Indeterminate &&
      unreadableDebit.NativeCallAttempted && unreadableDebitPort.Calls == 1 &&
      unknownRecovery.Status == EyeScanStatus.NeedsRecovery &&
      unknownRecovery.State.Appraisals.PendingScan?.Observation == null &&
      RunStateCodec.Read(RunStateCodec.Write(unknownRecovery.State)).Status ==
      ReadStatus.Ready &&
      AppraisalScanRules.ProgressDoesNotRegress(scanApplying.State.Appraisals,
          unknownRecovery.State.Appraisals) &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(scanApplying.State),
          scanApplying.State, unknownRecovery.State,
          scanApplying.State.GameRunId,
          scanApplying.State.SaveSlotId).Status == WriteGateStatus.Ready,
    "missing post-debit readback remains a valid persisted recovery state");
Check(AppraisalScanRules.RecordUncertainNativeDebit(scanApplying.State,
          BatteryDebitTransaction.Apply(new FakeBatteryDebitPort(15, 4),
              debitRequest), "no-native-debit").Status ==
      EyeScanStatus.InvalidEvidence &&
      AppraisalScanRules.Commit(unknownRecovery.State,
          scanRequest).Status == EyeScanStatus.NeedsRecovery,
    "precheck failure cannot forge recovery and unknown debit cannot reveal a scan");
var wrongBattery = AppraisalScanRules.ConfirmBattery(scanApplying.State,
    scanRequest, new AppraisalBatteryObservation("battery-instance-1",
        15, 11, "native-battery-mismatch"));
Check(wrongBattery.Status == EyeScanStatus.NeedsRecovery &&
      wrongBattery.State.Appraisals.PendingScan?.Phase ==
      AppraisalScanRules.NeedsRecovery &&
      wrongBattery.State.Appraisals.ScanReceipts.Length == 0 &&
      RunStateCodec.Read(RunStateCodec.Write(wrongBattery.State)).Status ==
      ReadStatus.Ready &&
      SurvivalRules.SettleDay(wrongBattery.State with
          { LastSettledDay = 7 }, 8).Status ==
      SettleStatus.OperationPending,
    "wrong native power delta freezes scan without revealing a result");
Check(AppraisalScanRules.ConfirmBattery(scanApplying.State,
      scanRequest, new AppraisalBatteryObservation("battery-instance-1",
          15, -1, "invalid-battery-count")).Status ==
      EyeScanStatus.InvalidEvidence,
    "impossible battery observations cannot become recovery records");
var scanConfirmed = AppraisalScanRules.ConfirmNativeDebit(scanApplying.State,
    fullDebit, "native-battery-debit-1");
var scanCommitted = AppraisalScanRules.Commit(scanConfirmed.State,
    scanRequest);
Check(scanCommitted.Status == EyeScanStatus.Applied &&
      scanCommitted.State.Appraisals.PendingScan == null &&
      scanCommitted.State.Appraisals.ScanReceipts.Length == 1 &&
      scanCommitted.State.Appraisals.ScannedItems.Length == 1 &&
      AppraisalScanRules.Cached(scanCommitted.State,
          "cigarette-instance-1", "cigarette-fingerprint-v1",
          "cigarette-adapter-v1") != null &&
      RunStateCodec.Read(RunStateCodec.Write(scanCommitted.State)).Status ==
      ReadStatus.Ready &&
      ModDataWriteGate.Update(true,
          RunStateCodec.Write(scanConfirmed.State),
          scanConfirmed.State, scanCommitted.State,
          scanConfirmed.State.GameRunId,
          scanConfirmed.State.SaveSlotId).Status ==
      WriteGateStatus.Ready,
    "confirmed fifteen-point debit commits result, scan marker and replay receipt");
var reloadedScan = RunStateCodec.Read(
    RunStateCodec.Write(scanCommitted.State)).State!;
Check(AppraisalScanRules.Prepare(reloadedScan, reloadedScan.RunId,
      reloadedScan.Revision, cigaretteScan).Status ==
      EyeScanStatus.AlreadyCommitted &&
      AppraisalScanRules.Prepare(reloadedScan, reloadedScan.RunId,
          reloadedScan.Revision, cigaretteScan with
          { RequestId = Guid.NewGuid().ToString("D") }).Status ==
      EyeScanStatus.Cached &&
      AppraisalRules.Submit(reloadedScan, reloadedScan.RunId,
          reloadedScan.Revision,
          new ManualAppraisalInput("cigarette-instance-1",
              AppraisalRules.Cigarette,
              Guid.NewGuid().ToString("D"), 8, true, true,
              false, "stable-cigarette-id", "late-manual",
              InspectionComplete: true,
              ActualFindings: [], SubmittedFindings: [])).Status ==
      AppraisalStatus.AlreadyScanned,
    "reload, repeated view and later manual answer cannot debit or train again");
Check(AppraisalScanRules.Cached(reloadedScan,
      "cigarette-instance-1", "changed-fingerprint",
      "cigarette-adapter-v1") == null,
    "item state change invalidates the old visible scan result");
Check(AppraisalScanRules.Prepare(reloadedScan, reloadedScan.RunId,
      reloadedScan.Revision, cigaretteScan with
      {
          RequestId = Guid.NewGuid().ToString("D"),
          ItemOwned = false
      }).Status == EyeScanStatus.NotOwned,
    "cached result cannot be exposed through an unowned item snapshot");
var rescanRequest = Guid.NewGuid().ToString("D");
var rescanInput = cigaretteScan with
{
    RequestId = rescanRequest,
    Fingerprint = "cigarette-fingerprint-v2",
    BatteryCharge = 15,
    Result = new AppraisalScanResult(
        [new AppraisalFact("authentic", "false"),
         new AppraisalFact("anomaly", "filter")], null)
};
var rescanPrepared = AppraisalScanRules.Prepare(reloadedScan,
    reloadedScan.RunId, reloadedScan.Revision, rescanInput);
var rescanApplying = AppraisalScanRules.MarkApplying(
    rescanPrepared.State, rescanRequest, rescanPrepared.State.Revision,
    scanFresh with
    {
        Fingerprint = "cigarette-fingerprint-v2",
        BatteryCharge = 15
    });
var rescanConfirmed = AppraisalScanRules.ConfirmBattery(
    rescanApplying.State, rescanRequest,
    new AppraisalBatteryObservation("battery-instance-1",
        15, 0, "native-battery-debit-2"));
var rescanCommitted = AppraisalScanRules.Commit(rescanConfirmed.State,
    rescanRequest);
Check(rescanCommitted.Status == EyeScanStatus.Applied &&
      rescanCommitted.State.Appraisals.ScannedItems.Length == 1 &&
      rescanCommitted.State.Appraisals.ScanReceipts.Length == 2 &&
      AppraisalScanRules.Cached(rescanCommitted.State,
          "cigarette-instance-1", "cigarette-fingerprint-v1",
          "cigarette-adapter-v1") == null &&
      AppraisalScanRules.Cached(rescanCommitted.State,
          "cigarette-instance-1", "cigarette-fingerprint-v2",
          "cigarette-adapter-v1")?.Result.Facts[0].Value == "false" &&
      RunStateCodec.Read(RunStateCodec.Write(rescanCommitted.State))
          .Status == ReadStatus.Ready &&
      ModDataWriteGate.Update(true,
          RunStateCodec.Write(rescanConfirmed.State),
          rescanConfirmed.State, rescanCommitted.State,
          rescanConfirmed.State.GameRunId,
          rescanConfirmed.State.SaveSlotId).Status ==
      WriteGateStatus.Ready,
    "changed item fingerprint costs one new verified scan and replaces cached result");
Check(AppraisalScanRules.Prepare(rescanCommitted.State,
      rescanCommitted.State.RunId, rescanCommitted.State.Revision,
      cigaretteScan).Status == EyeScanStatus.AlreadyCommitted,
    "older scan request remains idempotent after a later rescan");
var removedScanReceipt = reloadedScan with
{
    Revision = reloadedScan.Revision + 1,
    Appraisals = reloadedScan.Appraisals with
    { ScanReceipts = [], ScannedItems = [], ScanCache = [] }
};
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(reloadedScan),
      reloadedScan, removedScanReceipt, reloadedScan.GameRunId,
      reloadedScan.SaveSlotId).Status ==
      WriteGateStatus.InvalidTransition,
    "ordinary state update cannot erase a paid scan and training exclusion");
Check(RunStateCodec.Read(InvalidHistory(reloadedScan, json =>
      json["appraisals"]!["scanReceipts"]![0]!["chargeAfter"] = 6)
      .ToJsonString()).Status == ReadStatus.Corrupt,
    "saved eye scan receipt requires the exact configured battery cost");
Check(RunStateCodec.Read(InvalidHistory(reloadedScan, json =>
      json["cybernetics"]!["purchases"] = new JsonArray())
      .ToJsonString()).Status == ReadStatus.Corrupt,
    "saved scans cannot exist without an eye purchase in the same run");

RunState BuyCybernetic(RunState source, string itemId, long credits)
{
    var requestId = Guid.NewGuid().ToString("D");
    var prepared = CyberneticRules.Prepare(source, source.RunId, source.Revision,
        requestId, itemId, source.LastObservedDay, credits);
    if (prepared.Status != CyberneticStatus.Applied)
        throw new Exception($"Cannot reserve cybernetic {itemId}: {prepared.Status}");
    var applying = CyberneticRules.MarkApplying(prepared.State, requestId, credits);
    var confirmed = CyberneticRules.ConfirmExternal(applying.State, requestId,
        credits - CyberneticRules.Price(source.ConfigSnapshot, itemId),
        $"verified-payment-{itemId}");
    var committed = CyberneticRules.Commit(confirmed.State, requestId);
    if (committed.Status != CyberneticStatus.Applied)
        throw new Exception($"Cannot commit cybernetic {itemId}: {committed.Status}");
    return committed.State;
}
var aiPurchased = BuyCybernetic(medicalSource, CyberneticRules.AiAssistantCore, 700);
Check(BargainRules.CalculateMarketModifier(true,
          aiPurchased.Cybernetics.Enabled(CyberneticRules.AiAssistantCore), 50,
          aiPurchased.ConfigSnapshot) == 25 &&
      BargainRules.CalculateMarketModifier(true,
          medicalSource.Cybernetics.Enabled(CyberneticRules.AiAssistantCore), 50,
          medicalSource.ConfigSnapshot) == 25,
    "AI assistant fixed markup does not affect an Aug customer");
Check(BargainRules.CalculateMarketModifier(false,
      aiPurchased.Cybernetics.Enabled(CyberneticRules.AiAssistantCore), 50,
          aiPurchased.ConfigSnapshot) == -15 &&
      BargainRules.CalculateMarketModifier(false,
      medicalSource.Cybernetics.Enabled(CyberneticRules.AiAssistantCore), 50,
          medicalSource.ConfigSnapshot) == -25,
    "AI assistant fixed markup comes from this run for a non-Aug customer");
var aiDisabled = CyberneticRules.SetEnabled(aiPurchased,
    aiPurchased.RunId, aiPurchased.Revision, CyberneticRules.AiAssistantCore, false);
Check(BargainRules.CalculateMarketModifier(true,
      aiDisabled.State.Cybernetics.Enabled(CyberneticRules.AiAssistantCore), 50,
      aiDisabled.State.ConfigSnapshot) == 25,
    "disabled AI assistant stops adding fixed markup");
Check(BargainRules.CalculateMarketModifier(false, false, 9,
          medicalSource.ConfigSnapshot, 10) == 0 &&
      BargainRules.CalculateMarketModifier(false, false, 10,
          medicalSource.ConfigSnapshot, 10) == -10,
    "exposure threshold shift suppresses pricing below 10 and starts at 10");
var skinPurchased = BuyCybernetic(medicalSource, CyberneticRules.MimeticSkin, 500);
var skinOwned = OwnedCyberneticEffects.FromItems([
    new(CyberneticProductDefinitions.ItemId("mimetic_skin"), 1, true)]);
Check(SurvivalRules.SettleDay(medicalSource with { LastSettledDay = 7 }, 8, skinOwned)
      .State.Exposure == 22 &&
      SurvivalRules.SettleDay(skinPurchased with { LastSettledDay = 7 }, 8)
      .State.Exposure == 25,
    "owned skin reduces exposure without an unlock and an unlock without skin does not");
var skinDisabled = CyberneticRules.SetEnabled(skinPurchased,
    skinPurchased.RunId, skinPurchased.Revision, CyberneticRules.MimeticSkin, false);
Check(SurvivalRules.SettleDay(skinDisabled.State with { LastSettledDay = 7 }, 8)
      .State.Exposure == 25,
    "disabled skin does not reduce the next day's natural exposure");
var heartPurchased = BuyCybernetic(medicalSource, CyberneticRules.ArtificialHeart, 600);
var heartOwned = OwnedCyberneticEffects.FromItems([
    new(CyberneticProductDefinitions.ItemId("artificial_heart"), 1, true)]);
Check(SurvivalRules.SettleDay(medicalSource with { LastSettledDay = 7 }, 8, heartOwned)
      .State.Health == 95 &&
      SurvivalRules.SettleDay(heartPurchased with { LastSettledDay = 7 }, 8)
      .State.Health == 90,
    "owned artificial heart halves health loss independently of purchases");
var allOwnedProducts = CyberneticProductDefinitions.Products
    .Select(product => new CyberneticEffectItem(product.Id, 1, true)).ToArray();
var allOwnedEffects = OwnedCyberneticEffects.FromItems(allOwnedProducts);
Check(OwnedCyberneticEffects.FromOwnedCounts(allOwnedProducts.ToDictionary(item => item.ItemId, item => item.Units)) == allOwnedEffects &&
      OwnedCyberneticEffects.FromOwnedCounts(allOwnedProducts.ToDictionary(item => item.ItemId, _ => 0)) == default &&
      OwnedCyberneticEffects.FromOwnedCounts(new Dictionary<string, int> { ["ordinary_item"] = 100 }) == default,
    "coalesced ownership counts preserve effects and ignore exhausted or unrelated products");
Check(allOwnedEffects == new OwnedCyberneticEffects(true, true, true, true, true, true),
    "all six manufactured Aug products have a live ownership effect");
Check(OwnedCyberneticEffects.FromItems(allOwnedProducts.Concat(allOwnedProducts)) == allOwnedEffects &&
      OwnedCyberneticEffects.FromItems(allOwnedProducts.Select(item => item with { Units = 2 })) == allOwnedEffects,
    "duplicate products and stacks never multiply cybernetic effects");
Check(OwnedCyberneticEffects.FromItems(allOwnedProducts.Select(item => item with { PlayerOwned = false })) == default &&
      OwnedCyberneticEffects.FromItems(allOwnedProducts.Select(item => item with { Units = 0 })) == default &&
      OwnedCyberneticEffects.FromItems([new(CyberneticRules.ArtificialHeart, 1, true)]) == default,
    "unowned, exhausted and upgrade-ID entries cannot activate product effects");
var effectCases = new (string Suffix, OwnedCyberneticEffects Expected)[]
{
    ("artificial_heart", new(true, false, false, false, false, false)),
    ("mimetic_skin", new(false, true, false, false, false, false)),
    ("artificial_ankle", new(false, false, true, false, false, false)),
    ("infrared_vision", new(false, false, false, true, false, false)),
    ("communication_receiver", new(false, false, false, false, true, false)),
    ("synthetic_vocal_cords", new(false, false, false, false, false, true))
};
foreach (var (suffix, expected) in effectCases)
{
    Check(OwnedCyberneticEffects.FromItems([new(CyberneticProductDefinitions.ItemId(suffix), 1, true)]) == expected &&
          OwnedCyberneticEffects.FromItems([]) == default,
        $"{suffix} independently enables its own effect and removal disables it");
    Check(OwnedCyberneticEffects.FromOwnedCounts(new Dictionary<string, int> { [CyberneticProductDefinitions.ItemId(suffix)] = 2 }) == expected &&
          OwnedCyberneticEffects.FromOwnedCounts(new Dictionary<string, int> { [CyberneticProductDefinitions.ItemId(suffix)] = -1 }) == default,
        $"{suffix} keeps single-product and non-stacking rules in the count snapshot");
}
var cyberConfig = medicalSource.ConfigSnapshot.CyberneticPrices;
Check(allOwnedEffects.ScavengingMaximum(5, cyberConfig) == 8 &&
      allOwnedEffects.ScavengingMaximum(int.MaxValue, cyberConfig) == int.MaxValue &&
      default(OwnedCyberneticEffects).ScavengingMaximum(5, cyberConfig) == 5,
    "ankle adds exactly three attempts without overflow or duplicate contribution");
Check(allOwnedEffects.ScavengingMaximum(0, cyberConfig) == 0 &&
      allOwnedEffects.ScavengingMaximum(-1, cyberConfig) == -1,
    "ankle preserves native disabilities instead of reopening scavenging");
Check(allOwnedEffects.ScavengingWoundChance(20, cyberConfig) == 15 &&
      allOwnedEffects.ScavengingWoundChance(0, cyberConfig) == 0 &&
      default(OwnedCyberneticEffects).ScavengingWoundChance(20, cyberConfig) == 20,
    "infrared scales the native wound chance once and preserves native immunity");
Check(BargainRules.CalculatePerkModifier(false, 19, medicalSource.ConfigSnapshot,
          allOwnedEffects.ExposureThresholdShift(cyberConfig)) == 0 &&
      BargainRules.CalculatePerkModifier(false, 20, medicalSource.ConfigSnapshot,
          allOwnedEffects.ExposureThresholdShift(cyberConfig)) == -10 &&
      BargainRules.CalculatePerkModifier(false, 54, medicalSource.ConfigSnapshot,
          allOwnedEffects.ExposureThresholdShift(cyberConfig)) == -10 &&
      BargainRules.CalculatePerkModifier(false, 55, medicalSource.ConfigSnapshot,
          allOwnedEffects.ExposureThresholdShift(cyberConfig)) == -25 &&
      BargainRules.CalculatePerkModifier(false, 94, medicalSource.ConfigSnapshot,
          allOwnedEffects.ExposureThresholdShift(cyberConfig)) == -25 &&
      BargainRules.CalculatePerkModifier(false, 95, medicalSource.ConfigSnapshot,
          allOwnedEffects.ExposureThresholdShift(cyberConfig)) == -50 &&
      default(OwnedCyberneticEffects).ExposureThresholdShift(cyberConfig) == 0,
    "owned vocal cords shift every native retail exposure tier by twenty and removal restores it");
Check(StatusPanelRules.Build(medicalSource, 8, false, allOwnedEffects).Lines.Any(line => line.Contains("检查事件概率")) &&
      StatusPanelRules.Build(medicalSource, 8, true, allOwnedEffects).Lines.Any(line => line.Contains("Security inspection")) &&
      !StatusPanelRules.Build(medicalSource, 8, false).Lines.Any(line => line.Contains("检查事件概率")),
    "communicator information is localized and visible only with the owned product");
var settlementSource = medicalSource with { LastSettledDay = 7 };
var ownedSettlement = SurvivalRules.ReconcileDay(settlementSource, 9, allOwnedEffects).State;
Check(ModDataWriteGate.Update(true, RunStateCodec.Write(settlementSource), settlementSource,
          ownedSettlement, gameRunId, 25, allOwnedEffects).Status == WriteGateStatus.Ready &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(settlementSource), settlementSource,
          ownedSettlement, gameRunId, 25).Status == WriteGateStatus.InvalidTransition &&
      ModDataWriteGate.Update(true, RunStateCodec.Write(settlementSource), settlementSource,
          ownedSettlement with { Health = ownedSettlement.Health + 1 }, gameRunId, 25,
          allOwnedEffects).Status == WriteGateStatus.InvalidTransition &&
      RunStateCodec.Read(RunStateCodec.Write(ownedSettlement)).Status == ReadStatus.Ready &&
      ReferenceEquals(ownedSettlement.Cybernetics, settlementSource.Cybernetics),
    "ownership settlement passes the exact save gate and roundtrip without changing unlock history");
var slotFixtureDirectory = System.IO.Directory.CreateTempSubdirectory("aug-slot-check-");
try
{
    string slotFile = Path.Combine(slotFixtureDirectory.FullName, "save_24.es3");
    string expectedModJson = RunStateCodec.Write(first);
    string encodedModJson = System.Text.Json.JsonSerializer.Serialize(expectedModJson);
    string es3Fixture = "{\"playerStore\":{\"__type\":\"PlayerStore,Assembly-CSharp\",\"value\":{"
        + $"\"runID\":\"{gameRunId}\",\"saveSlotId\":24,\"startType\":14,"
        + "\"playerCash\":500,\"rentValue\":250,"
        + "\"storeReputations\":[{\"factionId\":\"FACTION_SECURITY\",\"amount\":-15}],"
        + $"\"modData\":{{\"{RunState.SaveKey}\":{encodedModJson}}},"
        + "\"es3Quirks\":{12:\"bad\\“\0\"}}},\"storeStation\":{"
        + "\"__type\":\"StoreStation,Assembly-CSharp\",\"value\":{\"dayCounter\":1}}}";
    File.WriteAllText(slotFile, es3Fixture);
    const string visitFixtureKey = "visit-receipt-test";
    string withVisitReceipt = es3Fixture.Replace("\"modData\":{",
        "\"modData\":{\"visit-receipt-test\":\"queued\",");
    var expectedVisitReceipt = new NativeSaveSnapshot(ExtraModData:
        new Dictionary<string, string> { [visitFixtureKey] = "queued" });
    File.WriteAllText(slotFile, withVisitReceipt);
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, expectedVisitReceipt).Status ==
          SlotReadbackStatus.StateMatched &&
          Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(ExtraModData:
              new Dictionary<string, string> { [visitFixtureKey] = "selected" })).Status ==
          SlotReadbackStatus.NativeModDataMismatch,
        "ES3 visit receipt readback requires the exact saved value");
    File.WriteAllText(slotFile, es3Fixture);
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, expectedVisitReceipt).Status ==
          SlotReadbackStatus.NativeModDataMismatch,
        "ES3 visit receipt readback rejects a missing key");
    File.WriteAllText(slotFile, withVisitReceipt.Replace(
        "\"visit-receipt-test\":\"queued\"",
        "\"visit-receipt-test\":\"queued\",\"visit-receipt-test\":\"queued\""));
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, expectedVisitReceipt).Status == SlotReadbackStatus.Malformed,
        "ES3 visit receipt readback rejects duplicate keys");
    File.WriteAllText(slotFile, es3Fixture);
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(1, 500, 250, -15,
              StartType: StartDefinition.NativeStartTypeId)).Status ==
          SlotReadbackStatus.StateMatched,
        "ES3 readback tolerates native numeric keys and string controls while matching exact Mod state");
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24,
          expectedNative: new NativeSaveSnapshot(
              StartType: StartDefinition.NativeStartTypeId)).Status ==
          SlotReadbackStatus.StructureReadable,
        "a valid 55 slot with a Mod state is recognizable before its exact JSON is known");
    File.WriteAllText(slotFile, es3Fixture.Replace("\"startType\":14", "\"startType\":2"));
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(
              StartType: StartDefinition.NativeStartTypeId)).Status ==
          SlotReadbackStatus.NativeStartTypeMismatch,
        "a Mod slot cannot claim the Aug start when the native type is not 55");
    File.WriteAllText(slotFile, es3Fixture);

    var adoptIntents = new StartIntentCoordinator();
    var adoptEpoch = adoptIntents.OpenMenu();
    adoptIntents.SelectAug(adoptEpoch);
    var adoptReservation = adoptIntents.Confirm(adoptEpoch)!;
    var adoptPlan = InitialSavePlanner.Prepare(adoptIntents, adoptReservation,
        gameRunId, 24, 1, "steam-demo-25382790", defaultConfig, false, null);
    var adoptSessions = new SessionCoordinator();
    File.WriteAllText(slotFile, es3Fixture.Replace(encodedModJson,
        JsonSerializer.Serialize(RunStateCodec.Write(adoptPlan.State!))));
    var adopted = adoptSessions.AdoptReadbackNew(adoptPlan,
        slotFixtureDirectory.FullName);
    Check(adopted is { Status: InitialSessionAdoptionStatus.Adopted,
          ReadbackStatus: SlotReadbackStatus.StateMatched } &&
          ReferenceEquals(adopted.Session?.State, adoptPlan.State) &&
          adoptSessions.IsCurrent(adopted.Session!.Generation,
              adoptPlan.State!.RunId),
        "initial slot readback adopts the exact planned Mod RunId");
    adoptSessions.Clear();
    Check(adoptSessions.AdoptReadbackNew(adoptPlan,
          slotFixtureDirectory.FullName).Status ==
          InitialSessionAdoptionStatus.TokenConsumed,
        "one initial-save token cannot recreate its session after clearing");

    var missingAdoptIntents = new StartIntentCoordinator();
    var missingAdoptEpoch = missingAdoptIntents.OpenMenu();
    missingAdoptIntents.SelectAug(missingAdoptEpoch);
    var missingAdoptPlan = InitialSavePlanner.Prepare(missingAdoptIntents,
        missingAdoptIntents.Confirm(missingAdoptEpoch)!, gameRunId, 24, 1,
        "steam-demo-25382790", defaultConfig, false, null);
    var missingAdoptSessions = new SessionCoordinator();
    File.WriteAllText(slotFile, es3Fixture);
    Check(missingAdoptSessions.AdoptReadbackNew(missingAdoptPlan,
          slotFixtureDirectory.FullName) is
          { Status: InitialSessionAdoptionStatus.ReadbackMismatch,
            ReadbackStatus: SlotReadbackStatus.StateMismatch } &&
          missingAdoptSessions.Current == null,
        "first session is not adopted when the native slot lacks its exact state");
    File.WriteAllText(slotFile, es3Fixture.Replace(encodedModJson,
        JsonSerializer.Serialize(RunStateCodec.Write(missingAdoptPlan.State!))));
    Check(missingAdoptSessions.AdoptReadbackNew(missingAdoptPlan,
          slotFixtureDirectory.FullName).Status ==
          InitialSessionAdoptionStatus.TokenConsumed,
        "uncertain initial save cannot be silently adopted on a later retry");
    var invalidAdoptPlan = missingAdoptPlan with
        { Write = missingAdoptPlan.Write! with { NextJson = "{}" } };
    var invalidAdoptSessions = new SessionCoordinator();
    Check(invalidAdoptSessions.AdoptReadbackNew(invalidAdoptPlan,
          slotFixtureDirectory.FullName).Status ==
          InitialSessionAdoptionStatus.InvalidPlan &&
          invalidAdoptSessions.AdoptReadbackNew(missingAdoptPlan,
              slotFixtureDirectory.FullName).Status ==
              InitialSessionAdoptionStatus.Adopted,
        "a forged initial write plan is rejected without consuming the valid token");
    File.WriteAllText(slotFile, es3Fixture);

    var leaseSessions = new SessionCoordinator();
    Check(leaseSessions.Restore(openingPreparedJson, gameRunId, 24).Status ==
          ReadStatus.Ready, "prepared journal can restore without granting an attempt");
    var leasePrepared = leaseSessions.Current!.State;
    var leaseApplying = leasePrepared with
    {
        Revision = 2,
        Opening = leasePrepared.Opening! with { Phase = OpeningJournalRules.Applying }
    };
    Check(leaseSessions.AcceptSavedOpeningRevision(leasePrepared, leaseApplying,
          slotFixtureDirectory.FullName).Status == OpeningSessionAdvanceStatus.ReadbackMismatch,
        "a stale slot cannot arm a native opening attempt");
    File.WriteAllText(slotFile, es3Fixture.Replace(encodedModJson,
        JsonSerializer.Serialize(RunStateCodec.Write(leaseApplying))));
    var economyAdvance = leaseSessions.AcceptSavedOpeningRevision(leasePrepared,
        leaseApplying, slotFixtureDirectory.FullName);
    Check(economyAdvance is { Status: OpeningSessionAdvanceStatus.Advanced,
          Lease: not null }, "fresh saved applying revision arms one economy attempt");
    var reloadSessions = new SessionCoordinator();
    reloadSessions.Restore(openingPreparedJson, gameRunId, 24);
    var reloadPrepared = reloadSessions.Current!.State;
    var reloadApplying = reloadPrepared with
    {
        Revision = 2,
        Opening = reloadPrepared.Opening! with { Phase = OpeningJournalRules.Applying }
    };
    var unconsumedOnLoad = reloadSessions.AcceptSavedOpeningRevision(
        reloadPrepared, reloadApplying, slotFixtureDirectory.FullName).Lease!;
    reloadSessions.Restore(RunStateCodec.Write(reloadApplying), gameRunId, 24);
    Check(!unconsumedOnLoad.TryConsumeEconomy(reloadApplying) &&
          reloadSessions.AcceptSavedOpeningRevision(reloadApplying,
              reloadApplying, slotFixtureDirectory.FullName).Status ==
          OpeningSessionAdvanceStatus.NoCurrentSession,
        "loading applying state revokes an unused attempt without rearming it");
    var economyLease = economyAdvance.Lease!;
    Check(economyLease.PlanUnobservedEconomyRecovery(
          new(OpeningEconomyApplyStatus.Unavailable)).Status ==
          OpeningProgressStatus.UnverifiedBoundary,
        "unconsumed economy lease cannot claim an attempted operation");
    Check(!economyLease.TryConsumeGrant(leaseApplying, openingManifest[0].Request) &&
          !economyLease.TryConsumeEconomy(journalApplying) &&
          economyLease.TryConsumeEconomy(leaseApplying) &&
          !economyLease.TryConsumeEconomy(leaseApplying),
        "economy lease binds the exact state and is consumed once");
    Check(economyLease.PlanUnobservedEconomyRecovery(
          new(OpeningEconomyApplyStatus.Unavailable)).Status ==
          OpeningProgressStatus.NeedsRecovery,
        "consumed economy lease can plan an explicit recovery revision");
    var recoverySessions = new SessionCoordinator();
    recoverySessions.Restore(openingPreparedJson, gameRunId, 24);
    var recoveryPrepared = recoverySessions.Current!.State;
    var recoveryApplying = recoveryPrepared with
    {
        Revision = 2,
        Opening = recoveryPrepared.Opening! with
            { Phase = OpeningJournalRules.Applying }
    };
    var recoveryLease = recoverySessions.AcceptSavedOpeningRevision(
        recoveryPrepared, recoveryApplying, slotFixtureDirectory.FullName).Lease!;
    Check(recoveryLease.TryConsumeEconomy(recoveryApplying),
        "recovery fixture consumes its own saved economy attempt");
    var recoveryPlan = recoveryLease.PlanUnobservedEconomyRecovery(
        new(OpeningEconomyApplyStatus.Unavailable));
    File.WriteAllText(slotFile, es3Fixture.Replace(encodedModJson,
        JsonSerializer.Serialize(RunStateCodec.Write(recoveryPlan.State!))));
    var savedRecovery = recoverySessions.AcceptSavedOpeningRevision(
        recoveryApplying, recoveryPlan.State!, slotFixtureDirectory.FullName);
    Check(savedRecovery is { Status: OpeningSessionAdvanceStatus.Advanced,
          Lease: null } &&
          recoverySessions.Current?.State.InitPhase == "needsRecovery" &&
          !recoveryLease.TryConsumeEconomy(recoveryApplying),
        "persisted uncertain attempt advances to recovery without a new lease");
    var leaseEconomyObserved = leaseApplying with
    {
        Revision = 3,
        Opening = leaseApplying.Opening! with
            { Economy = economyObservedForGrant.Opening!.Economy }
    };
    File.WriteAllText(slotFile, es3Fixture.Replace(encodedModJson,
        JsonSerializer.Serialize(RunStateCodec.Write(leaseEconomyObserved))));
    var moduleAdvance = leaseSessions.AcceptSavedOpeningRevision(leaseApplying,
        leaseEconomyObserved, slotFixtureDirectory.FullName);
    Check(moduleAdvance is { Status: OpeningSessionAdvanceStatus.Advanced,
          Lease: not null } && !economyLease.TryConsumeEconomy(leaseApplying),
        "saved economy receipt retires its lease and arms the module");
    var moduleLease = moduleAdvance.Lease!;
    Check(!moduleLease.TryConsumeGrant(leaseEconomyObserved,
              openingManifest[1].Request) &&
          moduleLease.TryConsumeGrant(leaseEconomyObserved,
              openingManifest[0].Request) &&
          !moduleLease.TryConsumeGrant(leaseEconomyObserved,
              openingManifest[0].Request),
        "module lease rejects another manifest step and duplicate calls");
    Check(moduleLease.PlanUnobservedGrantRecovery(
          new(OpeningGrantStatus.NoSpace, 0, 0)).Status ==
          OpeningProgressStatus.NeedsRecovery,
        "consumed grant lease can plan recovery after capacity preflight");
    leaseSessions.Restore(RunStateCodec.Write(leaseEconomyObserved), gameRunId, 24);
    Check(!moduleLease.TryConsumeGrant(leaseEconomyObserved,
              openingManifest[0].Request) &&
          moduleLease.PlanUnobservedGrantRecovery(
              new(OpeningGrantStatus.NoSpace, 0, 0)).Status ==
              OpeningProgressStatus.UnverifiedBoundary &&
          leaseSessions.AcceptSavedOpeningRevision(leaseEconomyObserved,
              leaseEconomyObserved, slotFixtureDirectory.FullName).Status ==
          OpeningSessionAdvanceStatus.NoCurrentSession,
        "reload invalidates old leases and cannot rearm an applying revision");

    var resumed = leaseSessions.Current!.State;
    var savedModule = OpeningProgressRules.RecordGrant(resumed,
        openingManifest[0].Request, resumed.Revision,
        new(OpeningGrantStatus.Applied, 0, 1, 1,
            NativeItemInstanceId: "test-opening-module",
            OwnershipEvidenceId: "test-owned-opening-module")).State!;
    File.WriteAllText(slotFile, es3Fixture.Replace(encodedModJson,
        JsonSerializer.Serialize(RunStateCodec.Write(savedModule))));
    Check(leaseSessions.AcceptSavedOpeningRevision(resumed, savedModule,
          slotFixtureDirectory.FullName).ReadbackStatus ==
          SlotReadbackStatus.Malformed,
        "grant receipt alone cannot arm the next step without saved inventory");
    string moduleInventory = "{\"saveItems\":[" +
        "{\"identifier\":\"save_bag\",\"childItems\":[1]}," +
        "{\"identifier\":\"" + NeuralModuleRules.ItemId + "\",\"unitCount\":1}]}";
    File.WriteAllText(slotFile, es3Fixture.Replace(encodedModJson,
        JsonSerializer.Serialize(RunStateCodec.Write(savedModule))).Replace(
            "\"es3Quirks\"", "\"mainInvJSON\":" +
            JsonSerializer.Serialize(moduleInventory) + ",\"es3Quirks\""));
    var refinerAdvance = leaseSessions.AcceptSavedOpeningRevision(resumed,
        savedModule, slotFixtureDirectory.FullName);
    Check(refinerAdvance is { Status: OpeningSessionAdvanceStatus.Advanced,
          Lease: not null } && !refinerAdvance.Lease.TryConsumeGrant(savedModule,
          openingManifest[2].Request),
        "saved module ownership arms only the next material refiner step");
    leaseSessions.Clear();
    Check(!refinerAdvance.Lease!.TryConsumeGrant(savedModule,
          openingManifest[1].Request), "menu clear retires outstanding attempt lease");

    string SlotWithState(RunState state) => es3Fixture.Replace(encodedModJson,
        JsonSerializer.Serialize(RunStateCodec.Write(state)));
    var savingSessions = new SessionCoordinator();
    savingSessions.Restore(openingPreparedJson, gameRunId, 24);
    var savingPrevious = savingSessions.Current!.State;
    var savingNext = savingPrevious with
    {
        Revision = 2,
        Opening = savingPrevious.Opening! with
            { Phase = OpeningJournalRules.Applying }
    };
    File.WriteAllText(slotFile, SlotWithState(savingPrevious));
    var savePort = new FakeOpeningRevisionSavePort(slotFixtureDirectory.FullName)
    {
        OnSave = () => File.WriteAllText(slotFile, SlotWithState(savingNext))
    };
    var savedRevision = OpeningRevisionSaveTransaction.Apply(savingSessions,
        savePort, savingPrevious, savingNext);
    Check(savedRevision is { Status: OpeningRevisionSaveStatus.Advanced,
          ReadbackStatus: SlotReadbackStatus.StateMatched } &&
          savePort.StageCalls == 1 && savePort.SaveCalls == 1 &&
          savingSessions.Current?.State == savingNext &&
          OpeningRevisionSaveTransaction.Apply(savingSessions, savePort,
              savingPrevious, savingNext).Status ==
              OpeningRevisionSaveStatus.NoCurrentSession &&
          savePort.SaveCalls == 1 &&
          !savingSessions.ClearIfCurrent(savingPrevious) &&
          savingSessions.Current?.State == savingNext,
        "opening save stages once, reads back, advances once, and preserves newer state");

    var staleSessions = new SessionCoordinator();
    staleSessions.Restore(openingPreparedJson, gameRunId, 24);
    var stalePrevious = staleSessions.Current!.State;
    var staleNext = stalePrevious with
    {
        Revision = 2,
        Opening = stalePrevious.Opening! with
            { Phase = OpeningJournalRules.Applying }
    };
    var stalePort = new FakeOpeningRevisionSavePort(slotFixtureDirectory.FullName);
    Check(OpeningRevisionSaveTransaction.Apply(staleSessions, stalePort,
          stalePrevious, staleNext) is
          { Status: OpeningRevisionSaveStatus.BaselineMismatch,
            ReadbackStatus: SlotReadbackStatus.StateMismatch } &&
          stalePort.StageCalls == 0 && stalePort.SaveCalls == 0 &&
          staleSessions.Current == null,
        "a slot already containing the next revision cannot justify a new save attempt");

    File.WriteAllText(slotFile, SlotWithState(stalePrevious));
    var rejectedSessions = new SessionCoordinator();
    rejectedSessions.Restore(openingPreparedJson, gameRunId, 24);
    var rejectedPrevious = rejectedSessions.Current!.State;
    var rejectedNext = rejectedPrevious with
    {
        Revision = 2,
        Opening = rejectedPrevious.Opening! with
            { Phase = OpeningJournalRules.Applying }
    };
    var rejectedPort = new FakeOpeningRevisionSavePort(slotFixtureDirectory.FullName)
        { StageOutcome = ModDataWriteStatus.Rejected };
    Check(OpeningRevisionSaveTransaction.Apply(rejectedSessions, rejectedPort,
          rejectedPrevious, rejectedNext).Status ==
          OpeningRevisionSaveStatus.StageFailed &&
          rejectedPort.SaveCalls == 0 && rejectedSessions.Current == null,
        "rejected modData staging never invokes native SaveGame");

    var changedStoreSessions = new SessionCoordinator();
    changedStoreSessions.Restore(openingPreparedJson, gameRunId, 24);
    var changedStorePrevious = changedStoreSessions.Current!.State;
    var changedStoreNext = changedStorePrevious with
    {
        Revision = 2,
        Opening = changedStorePrevious.Opening! with
            { Phase = OpeningJournalRules.Applying }
    };
    var changedStorePort = new FakeOpeningRevisionSavePort(
        slotFixtureDirectory.FullName) { LoseIdentityAfterStage = true };
    Check(OpeningRevisionSaveTransaction.Apply(changedStoreSessions,
          changedStorePort, changedStorePrevious, changedStoreNext).Status ==
          OpeningRevisionSaveStatus.IdentityMismatch &&
          changedStorePort.StageCalls == 1 && changedStorePort.SaveCalls == 0 &&
          changedStoreSessions.Current == null,
        "native store drift after staging blocks SaveGame and revokes the session");

    var missingWriteSessions = new SessionCoordinator();
    missingWriteSessions.Restore(openingPreparedJson, gameRunId, 24);
    var missingWritePrevious = missingWriteSessions.Current!.State;
    var missingWriteNext = missingWritePrevious with
    {
        Revision = 2,
        Opening = missingWritePrevious.Opening! with
            { Phase = OpeningJournalRules.Applying }
    };
    var missingWritePort = new FakeOpeningRevisionSavePort(slotFixtureDirectory.FullName);
    Check(OpeningRevisionSaveTransaction.Apply(missingWriteSessions,
          missingWritePort, missingWritePrevious, missingWriteNext) is
          { Status: OpeningRevisionSaveStatus.ReadbackFailed,
            ReadbackStatus: SlotReadbackStatus.StateMismatch } &&
          missingWritePort.SaveCalls == 1 && missingWriteSessions.Current == null,
        "a normal SaveGame return without changed slot bytes remains uncertain");

    var thrownSaveSessions = new SessionCoordinator();
    thrownSaveSessions.Restore(openingPreparedJson, gameRunId, 24);
    var thrownPrevious = thrownSaveSessions.Current!.State;
    var thrownNext = thrownPrevious with
    {
        Revision = 2,
        Opening = thrownPrevious.Opening! with
            { Phase = OpeningJournalRules.Applying }
    };
    var thrownPort = new FakeOpeningRevisionSavePort(slotFixtureDirectory.FullName)
    {
        OnSave = () => File.WriteAllText(slotFile, SlotWithState(thrownNext)),
        ThrowAfterSave = true
    };
    Check(OpeningRevisionSaveTransaction.Apply(thrownSaveSessions, thrownPort,
          thrownPrevious, thrownNext).Status == OpeningRevisionSaveStatus.SaveFailed &&
          thrownPort.SaveCalls == 1 && thrownSaveSessions.Current == null &&
          Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
              RunStateCodec.Write(thrownNext)).Status == SlotReadbackStatus.StateMatched,
        "a save exception after writing bytes still revokes the session without retry");

    string mainInventory = "{\"saveItems\":[" +
        "{\"identifier\":\"save_bag\",\"childItems\":[1,2]}," +
        "{\"identifier\":\"test-battery\",\"uniqueId\":7,\"unitCount\":2}," +
        "{\"identifier\":\"test-tool\",\"uniqueId\":8,\"unitCount\":1}]}";
    string withInventory = es3Fixture.Replace("\"es3Quirks\"",
        "\"mainInvJSON\":" + JsonSerializer.Serialize(mainInventory) + ",\"es3Quirks\"");
    File.WriteAllText(slotFile, withInventory);
    var expectedMainItems = new Dictionary<string, int>(StringComparer.Ordinal)
        { ["test-battery"] = 2, ["test-tool"] = 1 };
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(MainRootUnitCounts: expectedMainItems)).Status ==
          SlotReadbackStatus.StateMatched,
        "ES3 main inventory root child units match the expected item counts");
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(OwnedItem:
              new("test-battery", 7, true, 2))).Status ==
          SlotReadbackStatus.StateMatched &&
          Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(OwnedItem:
              new("test-battery", 7, false))).Status ==
          SlotReadbackStatus.NativeInventoryMismatch &&
          Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(OwnedItem:
              new("test-battery", 999, false))).Status ==
          SlotReadbackStatus.StateMatched,
        "ES3 consumable readback distinguishes the exact owned item from an absent instance");
    string backInventory = "{\"saveItems\":[" +
        "{\"identifier\":\"back_bag\",\"childItems\":[1]}," +
        "{\"identifier\":\"test-medical\",\"uniqueId\":55,\"unitCount\":1}]}";
    string withBackInventory = withInventory.Replace("\"es3Quirks\"",
        "\"backInvJSON\":" + JsonSerializer.Serialize(backInventory) +
        ",\"es3Quirks\"");
    File.WriteAllText(slotFile, withBackInventory);
    var backItem = new NativeItemIdentitySnapshot("test-medical", 55, true, 1,
        NativeItemSearchScope.AllSavedInventories);
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(OwnedItem: backItem)).Status ==
          SlotReadbackStatus.StateMatched &&
          Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(OwnedItem:
              backItem with { SearchScope = NativeItemSearchScope.MainInventory })).Status ==
          SlotReadbackStatus.NativeInventoryMismatch,
        "consumable readback finds a reachable item outside the main inventory without widening other callers");
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(OwnedItem:
              backItem with { MustBePresent = false })).Status ==
          SlotReadbackStatus.NativeInventoryMismatch &&
          Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(OwnedItem:
              backItem with { UnitCount = 2 })).Status ==
          SlotReadbackStatus.NativeInventoryMismatch,
        "consumable readback rejects a still-present or incorrectly counted item in another saved inventory");
    File.WriteAllText(slotFile, withInventory);
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(OwnedItem:
              backItem with { MustBePresent = false })).Status ==
          SlotReadbackStatus.StateMatched,
        "consumable readback confirms the exact item is absent from every saved inventory");
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(MainRootUnitCounts:
              new Dictionary<string, int> { ["test-battery"] = 3 })).Status ==
          SlotReadbackStatus.NativeInventoryMismatch,
        "ES3 readback reports a saved opening item count mismatch");
    File.WriteAllText(slotFile, es3Fixture.Replace("\"es3Quirks\"",
        "\"mainInvJSON\":" + JsonSerializer.Serialize(mainInventory.Replace("[1,2]", "[1,1]")) +
        ",\"es3Quirks\""));
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(MainRootUnitCounts: expectedMainItems)).Status ==
          SlotReadbackStatus.Malformed,
        "ES3 readback rejects a repeated child index in the inventory root");
    File.WriteAllText(slotFile, es3Fixture);
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(Day: 2)).Status ==
          SlotReadbackStatus.NativeDayMismatch,
        "ES3 readback rejects a different native game day");
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(Cash: 501)).Status ==
          SlotReadbackStatus.NativeEconomyMismatch &&
          Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(Rent: 251)).Status ==
          SlotReadbackStatus.NativeEconomyMismatch,
        "ES3 readback rejects a different native cash or rent value");
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(SecurityReputation: -14)).Status ==
          SlotReadbackStatus.NativeReputationMismatch,
        "ES3 readback rejects a different security faction reputation");
    File.WriteAllText(slotFile, es3Fixture.Replace("\"amount\":-15",
        "\"amount\":-15.75"));
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          expectedModJson, new NativeSaveSnapshot(SecurityReputation: -15)).Status ==
          SlotReadbackStatus.StateMatched,
        "ES3 readback uses the native integer conversion of fractional reputation");
    File.WriteAllText(slotFile, es3Fixture);
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24,
          Guid.NewGuid().ToString("D"), expectedModJson).Status ==
          SlotReadbackStatus.IdentityMismatch,
        "ES3 readback rejects another native run ID");
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24, gameRunId,
          "different").Status == SlotReadbackStatus.StateMismatch,
        "ES3 readback distinguishes a different serialized Mod state");
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 25).Status ==
          SlotReadbackStatus.Missing,
        "ES3 readback does not infer a missing slot from another file");
    File.WriteAllText(slotFile, es3Fixture.Replace("\"modData\":",
        "\"modData\":{},\"modData\":"));
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24).Status ==
          SlotReadbackStatus.Malformed,
        "ES3 readback rejects duplicate playerStore modData keys");
    File.WriteAllText(slotFile, es3Fixture.Replace(
        "\"storeReputations\":[{\"factionId\":\"FACTION_SECURITY\",\"amount\":-15}]",
        "\"storeReputations\":[{\"factionId\":\"FACTION_SECURITY\",\"amount\":-15},{\"factionId\":\"FACTION_SECURITY\",\"amount\":-15}]"));
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24).Status ==
          SlotReadbackStatus.Malformed,
        "ES3 readback rejects ambiguous duplicate security faction records");
    File.WriteAllText(slotFile, es3Fixture[..^1]);
    Check(Es3SlotReadback.Compare(slotFixtureDirectory.FullName, 24).Status ==
          SlotReadbackStatus.Malformed,
        "ES3 readback rejects a truncated native slot");
}
finally
{
    string target = Path.GetFullPath(slotFixtureDirectory.FullName);
    string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
    if (!target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(target).StartsWith("aug-slot-check-", StringComparison.Ordinal))
        throw new InvalidOperationException("Unexpected temporary slot fixture path");
    System.IO.Directory.Delete(target, recursive: true);
}

Check(ConsumableRules.IsMaterialMedical("common_medical") &&
      !ConsumableRules.IsMaterialMedical("restricted_medical") &&
      ConsumableRules.IsMaterialMedical("topical_bandage_item") &&
      !ConsumableRules.IsMaterialMedical("common_electronic"),
    "material medical whitelist is exact");
Check(ConsumableRules.IsMaterialMedical(BionicRepairGelDefinition.Id) &&
      ConsumableRules.IsMaterialMedical("nicokobo.mechcore.synthesis.compressed_nutrient_liquid") &&
      !ConsumableRules.IsMaterialMedical("nicokobo.mechcore.synthesis.neon_berry_soda") &&
      !ConsumableRules.IsMaterialMedical("nicokobo.mechcore.synthesis.glowberry_special"),
    "manufactured medical use excludes ordinary beverages");
Check(ConsumableRules.IsManufacturedMedical(BionicRepairGelDefinition.Id) &&
      ConsumableRules.IsManufacturedMedical("nicokobo.mechcore.synthesis.compressed_nutrient_liquid") &&
      !ConsumableRules.IsManufacturedMedical("common_medical"),
    "manufactured medicines read their batch basis while native medicine keeps its appraisal value path");
Check(ConsumableRules.MedicalRecovery(9999) == 9999 &&
      ConsumableRules.MedicalRecovery(1) == 1 &&
      ConsumableRules.MedicalRecovery(12) == 12 &&
      ConsumableRules.MedicalRecovery(-1) == 0 &&
      ConsumableRules.MedicalRecovery(0) == 0 &&
      ConsumableRules.MedicalRecovery((long)int.MaxValue + 1) == 0,
    "medical recovery values are explicit and bounded");
checks += WorkshopAchievementChecks.Run();
Check(new CyberneticConfig().ExposureThresholdShift == 20 &&
    BargainRules.CalculatePerkModifier(false, 19, new ConfigSnapshot(), 20) == 0 &&
    BargainRules.CalculatePerkModifier(false, 20, new ConfigSnapshot(), 20) == -10 &&
    BargainRules.CalculatePerkModifier(false, 55, new ConfigSnapshot(), 20) == -25,
    "vocal cords alone shift the default exposure pricing tiers by 20");
var products = CyberneticProductDefinitions.Products;
Check(products.Count == 7 && products.Select(product => product.Id).Distinct().Count() == 7,
    "seven unique manufactured products");
Check(products.Select(product => product.BaseValue).SequenceEqual([2332, 354, 366, 322, 729, 1683, 3045]),
    "recalculated product defaults use the universal terminal markup");
Check(products.Single(product => product.Suffix == "artificial_heart").Ingredients
    .Single(ingredient => ingredient.ItemId == "blue_blood_bag").Quantity == 1,
    "artificial heart consumes one native blue-blood bag");
Check(products.Single(product => product.Suffix == "mimetic_skin").Ingredients
    .Select(ingredient => (ingredient.ItemId, ingredient.Quantity))
    .SequenceEqual([("tilapia_skin_graft", 2), ("kotton_fabric", 1), ("neuroactive_perfume", 1)]),
    "mimetic skin consumes exactly two skin grafts, one kotton fabric and one SYNC perfume");
Check(products.Count(product => product.MusicCassetteCount > 0) == 1 &&
    products.Single(product => product.Suffix == "synthetic_vocal_cords").MusicCassetteCount == 1,
    "vocal cords retain the existing one music cassette");
var cognitiveTurbo = products.Single(product => product.Suffix == "cognitive_turbo");
Check(cognitiveTurbo.Width == 2 && cognitiveTurbo.Height == 2 &&
    !WorkshopAchievements.Relevant(cognitiveTurbo.Id) && WorkshopAchievements.Definitions.Length == 13,
    "the 2x2 cognitive turbo does not add an achievement or change the twelve-item ascension goal");
Check(cognitiveTurbo.Ingredients.Count(ingredient => ingredient.ItemId == "turbo_booster_adv" && ingredient.Quantity == 1) == 1 &&
    cognitiveTurbo.Ingredients.All(ingredient => ingredient.ItemId != "turbo_booster"),
    "cognitive turbo uses one permanent advanced booster in place of the disposable one");
Check(CyberneticProductDefinitions.IsMusicCassette(true, true, "any-track") &&
    !CyberneticProductDefinitions.IsMusicCassette(true, true, "") &&
    !CyberneticProductDefinitions.IsMusicCassette(true, false, "recording") &&
    !CyberneticProductDefinitions.IsMusicCassette(false, true, "any-track"),
    "music category accepts all tracks and rejects blank or non-music cassettes");

Check(OfficialMachineTarget.MachineId ==
      OfficialManufacturingTerminal.ItemId,
    "recipe target is the Forge universal manufacturing terminal");
Check(OfficialMachineTarget.IsOwnedByForge,
    "recipe target machine belongs to Forge");
Check(!OfficialMachineTarget.MachineId.StartsWith("nicokobo.aug.",
        StringComparison.Ordinal),
    "this mod contributes recipes to the shared Forge machine");

StoryChecks.Run(Check);
GraylineSupplyChecks.Run(Check);
Console.WriteLine($"Passed {checks} domain checks.");


}catch(Exception failure){Console.Error.WriteLine(failure);Environment.ExitCode=1;}
internal sealed class FakeOpeningEconomyPort(OpeningEconomySnapshot snapshot)
    : IOpeningEconomyPort
{
    internal OpeningEconomySnapshot? Snapshot = snapshot;
    internal bool ThrowAfterCash;
    internal bool LoseReadAfterCash;
    internal int SecurityChangeOnRent;
    internal int Writes;

    public OpeningEconomySnapshot? Read()
        => LoseReadAfterCash && Writes > 0 ? null : Snapshot;

    public void AddCash(int delta)
    {
        Writes++;
        Snapshot = Snapshot! with { Credits = checked(Snapshot!.Credits + delta) };
        if (ThrowAfterCash) throw new IOException("injected after cash mutation");
    }

    public void SetRent(int value)
    {
        Writes++;
        Snapshot = Snapshot! with
        {
            Rent = value,
            SecurityReputation = checked(Snapshot!.SecurityReputation + SecurityChangeOnRent)
        };
    }

    public void AddSecurityReputation(int delta)
    {
        Writes++;
        Snapshot = Snapshot! with
        { SecurityReputation = checked(Snapshot!.SecurityReputation + delta) };
    }
}

internal sealed class FakeOpeningGrantPort : IOpeningGrantPort
{
    internal int Owned;
    internal int SpaceLeft;
    internal int GrantCalls;
    internal bool ThrowAfterGrant;
    internal bool ThrowOnPrecheck;
    internal bool LoseReadAfterGrant;

    public int? ReadOwnedUnits(string itemId)
        => LoseReadAfterGrant && GrantCalls > 0 ? null : Owned;

    public bool? CanGrantOne(string itemId)
    {
        if (ThrowOnPrecheck) throw new IOException("injected before item transfer");
        return SpaceLeft > 0;
    }

    public void GrantOne(string itemId)
    {
        GrantCalls++;
        SpaceLeft--;
        Owned++;
        if (ThrowAfterGrant) throw new IOException("injected after item transfer");
    }
}

internal sealed class FakeOpeningRevisionSavePort(string directory)
    : IOpeningRevisionSavePort
{
    internal int StageCalls;
    internal int SaveCalls;
    internal bool IdentityMatches = true;
    internal bool ThrowAfterSave;
    internal bool LoseIdentityAfterStage;
    internal ModDataWriteStatus StageOutcome = ModDataWriteStatus.StagedInMemory;
    internal Action? OnSave;
    internal RunState? StagedState;

    public bool Matches(RunState state)
        => IdentityMatches && (StagedState == null ||
            ReferenceEquals(StagedState, state));

    public ModDataWriteResult Stage(RunState previous, RunState next)
    {
        StageCalls++;
        if (StageOutcome == ModDataWriteStatus.StagedInMemory)
            StagedState = next;
        if (LoseIdentityAfterStage) IdentityMatches = false;
        return new(StageOutcome);
    }

    public void SaveOnce()
    {
        SaveCalls++;
        OnSave?.Invoke();
        if (ThrowAfterSave) throw new IOException("injected after slot write");
    }

    public string? SaveDirectory => directory;
}

internal sealed class FakeBatteryDebitPort(int charge, int available)
    : IBatteryDebitPort
{
    internal BatteryDebitSnapshot Snapshot = new(charge, available);
    internal int Calls;
    internal int Reads;
    internal int? FixedRemoved;
    internal int? ReportedRemoved;
    internal bool ChangeOnSecondRead;
    internal bool LoseReadAfterCall;
    internal bool ThrowAfterCall;

    public BatteryDebitSnapshot? Read()
    {
        Reads++;
        if (ChangeOnSecondRead && Reads == 2)
            Snapshot = Snapshot with { Charge = Snapshot.Charge - 1,
                Available = Snapshot.Available - 1 };
        return LoseReadAfterCall && Calls > 0 ? null : Snapshot;
    }

    public int Remove(int cost)
    {
        Calls++;
        int removed = FixedRemoved ?? cost;
        int remaining = Snapshot.Charge - removed;
        Snapshot = Snapshot with { Charge = remaining,
            Available = Math.Min(Snapshot.Available, remaining) };
        if (ThrowAfterCall) throw new IOException("injected after power mutation");
        return ReportedRemoved ?? removed;
    }
}


