using System.Text;
using AugMechanicalAscension.Serialization;
using AugMechanicalAscension.Domain;
namespace AugMechanicalAscension.Persistence;

// Read-only comparison of the native ES3 slot. The current game's ES3 writer
// emits numeric object keys, non-JSON escapes and raw controls inside strings;
// normalize only those syntax differences, then require unique structural keys.
// A match proves bytes read back from the file, not an OS flush or a successful
// future LoadGame. Never use this to deserialize a live PlayerStore.
internal static class Es3SlotReadback
{
    private const int MaxBytes = AugNumbers.Es3SlotReadbackLimits.MaxBytes;

    // Candidate comparison for the first committed opening save only. Items
    // can legitimately be spent later, and the in-memory inventory-to-file
    // mapping has not yet been verified by native reload.
    internal static IReadOnlyDictionary<string, int>? CandidateOpeningRootUnits(
        RunState? state, int? nativeDay)
    {
        if (state is not { InitPhase: "complete", Opening: { Phase: OpeningJournalRules.Committed } journal } ||
            journal.Grants == null || state.Revision != 4L + journal.Grants.Length ||
            nativeDay != state.StartDay || !OpeningJournalRules.Valid(state))
            return null;
        return journal.Grants.ToDictionary(x => x.Request.ItemId,
            x => x.Result!.OwnedAfter!.Value, StringComparer.Ordinal);
    }

    internal static SlotReadbackResult Compare(string? directory, int slotId,
        string? expectedRunId = null, string? expectedStateJson = null,
        NativeSaveSnapshot? expectedNative = null)
    {
        if (string.IsNullOrWhiteSpace(directory) || slotId < 0)
            return new(SlotReadbackStatus.Unavailable);
        var path = Path.Combine(directory, $"save_{slotId}.es3");
        try
        {
            using var read = OfficialSaveReadbackApi.ReadFile(path, MaxBytes,
                AugNumbers.Validation.NativeSaveJsonDepth);
            if (read.Status != OfficialSaveReadStatus.Readable)
                return new(read.Status switch
                {
                    OfficialSaveReadStatus.Missing => SlotReadbackStatus.Missing,
                    OfficialSaveReadStatus.TooLarge => SlotReadbackStatus.TooLarge,
                    OfficialSaveReadStatus.ChangedDuringRead => SlotReadbackStatus.ChangedDuringRead,
                    OfficialSaveReadStatus.Malformed => SlotReadbackStatus.Malformed,
                    _ => SlotReadbackStatus.Unavailable
                }, read.ByteCount);
            int byteCount = read.ByteCount;
            var root = read.Root;
            if (!Unique(root, "playerStore", out var wrapper) ||
                !Unique(root, "storeStation", out var stationWrapper) ||
                !Unique(wrapper, "__type", out var type) ||
                type.ValueKind != JsonValueKind.String ||
                type.GetString() != "PlayerStore,Assembly-CSharp" ||
                !Unique(wrapper, "value", out var store) ||
                !Unique(store, "runID", out var runId) ||
                runId.ValueKind != JsonValueKind.String ||
                !Unique(store, "saveSlotId", out var nativeSlot) ||
                nativeSlot.ValueKind != JsonValueKind.Number ||
                !nativeSlot.TryGetInt32(out int recordedSlot) || recordedSlot != slotId ||
                !Guid.TryParse(runId.GetString(), out var recordedRunId) ||
                recordedRunId == Guid.Empty ||
                !Unique(store, "playerCash", out var cash) ||
                cash.ValueKind != JsonValueKind.Number ||
                !cash.TryGetInt32(out int savedCash) ||
                !Unique(store, "rentValue", out var rent) ||
                rent.ValueKind != JsonValueKind.Number ||
                !rent.TryGetInt32(out int savedRent) ||
                !Unique(store, "storeReputations", out var reputations) ||
                reputations.ValueKind != JsonValueKind.Array ||
                !Unique(store, "modData", out var modData) ||
                modData.ValueKind != JsonValueKind.Object ||
                !Unique(stationWrapper, "__type", out var stationType) ||
                stationType.ValueKind != JsonValueKind.String ||
                stationType.GetString() != "StoreStation,Assembly-CSharp" ||
                !Unique(stationWrapper, "value", out var station) ||
                !Unique(station, "dayCounter", out var dayCounter) ||
                dayCounter.ValueKind != JsonValueKind.Number ||
                !dayCounter.TryGetInt32(out int savedNativeDay) || savedNativeDay < 0)
                return new(SlotReadbackStatus.Malformed, byteCount);
            int? savedSecurityReputation = null;
            foreach (var faction in reputations.EnumerateArray())
            {
                if (!Unique(faction, "factionId", out var factionId) ||
                    factionId.ValueKind != JsonValueKind.String)
                    return new(SlotReadbackStatus.Malformed, byteCount);
                if (factionId.GetString() != "FACTION_SECURITY") continue;
                if (savedSecurityReputation != null ||
                    !Unique(faction, "amount", out var amount) ||
                    amount.ValueKind != JsonValueKind.Number ||
                    !amount.TryGetDouble(out double rawSecurityAmount) ||
                    !double.IsFinite(rawSecurityAmount) ||
                    Math.Truncate(rawSecurityAmount) < int.MinValue ||
                    Math.Truncate(rawSecurityAmount) > int.MaxValue)
                    return new(SlotReadbackStatus.Malformed, byteCount);
                // Native GetSecReputation converts the double amount to int.
                savedSecurityReputation = (int)Math.Truncate(rawSecurityAmount);
            }
            if (savedSecurityReputation == null)
                return new(SlotReadbackStatus.Malformed, byteCount);
            if (expectedRunId != null &&
                (!Guid.TryParse(expectedRunId, out var expectedId) || expectedId != recordedRunId))
                return new(SlotReadbackStatus.IdentityMismatch, byteCount);
            if (expectedNative?.StartType is { } expectedStartType)
            {
                if (!Unique(store, "startType", out var nativeStartType) ||
                    nativeStartType.ValueKind != JsonValueKind.Number ||
                    !nativeStartType.TryGetInt32(out int savedStartType))
                    return new(SlotReadbackStatus.Malformed, byteCount);
                if (savedStartType != expectedStartType)
                    return new(SlotReadbackStatus.NativeStartTypeMismatch, byteCount);
            }
            if (expectedNative?.Day != null && expectedNative.Day != savedNativeDay)
                return new(SlotReadbackStatus.NativeDayMismatch, byteCount);
            if (expectedNative?.Cash != null && expectedNative.Cash != savedCash ||
                expectedNative?.Rent != null && expectedNative.Rent != savedRent)
                return new(SlotReadbackStatus.NativeEconomyMismatch, byteCount);
            if (expectedNative?.SecurityReputation != null &&
                expectedNative.SecurityReputation != savedSecurityReputation)
                return new(SlotReadbackStatus.NativeReputationMismatch, byteCount);
            if (expectedNative?.MainRootUnitCounts is { } expectedUnits)
            {
                var inventoryStatus = CompareMainRootUnits(store, expectedUnits);
                if (inventoryStatus != null)
                    return new(inventoryStatus.Value, byteCount);
            }
            if (expectedNative?.OwnedItem is { } expectedItem)
            {
                var inventoryStatus = CompareOwnedItem(store, expectedItem);
                if (inventoryStatus != null)
                    return new(inventoryStatus.Value, byteCount);
            }
            if (expectedNative?.OwnedItems is { } expectedItems)
            {
                foreach (var listedItem in expectedItems)
                {
                    var inventoryStatus = CompareOwnedItem(store, listedItem);
                    if (inventoryStatus != null)
                        return new(inventoryStatus.Value, byteCount);
                }
            }
            if (expectedNative?.ExtraModData is { } expectedExtras)
            {
                foreach (var (key, expectedValue) in expectedExtras)
                {
                    if (!UniqueOptional(modData, key, out var savedValue, out bool found))
                        return new(SlotReadbackStatus.Malformed, byteCount);
                    if (!found || savedValue.ValueKind != JsonValueKind.String ||
                        savedValue.GetString() != expectedValue)
                        return new(SlotReadbackStatus.NativeModDataMismatch, byteCount);
                }
            }

            if (!UniqueOptional(modData, Domain.RunState.SaveKey, out var stateJson, out bool present))
                return new(SlotReadbackStatus.Malformed, byteCount);
            if (!present) return new(SlotReadbackStatus.StateMissing, byteCount);
            if (stateJson.ValueKind != JsonValueKind.String)
                return new(SlotReadbackStatus.Malformed, byteCount);
            if (expectedStateJson == null)
                return new(SlotReadbackStatus.StructureReadable, byteCount);
            return new(stateJson.GetString() == expectedStateJson
                ? SlotReadbackStatus.StateMatched : SlotReadbackStatus.StateMismatch,
                byteCount);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or
            InvalidDataException or OverflowException)
        {
            return new(SlotReadbackStatus.Malformed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return new(SlotReadbackStatus.Unavailable);
        }
    }

    private static bool Unique(JsonElement parent, string name, out JsonElement value)
        => UniqueOptional(parent, name, out value, out bool present) && present;

    // The first saveItems entry is the main inventory's root node in the
    // observed build. Count only its direct child indices, not every serialized
    // item (which may also include nested containers). This remains a file-byte
    // comparison; the native LoadGame ownership mapping still needs validation.
    private static SlotReadbackStatus? CompareMainRootUnits(JsonElement store,
        IReadOnlyDictionary<string, int> expected)
    {
        if (expected.Count == 0 || expected.Any(x =>
            string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > AugNumbers.Validation.MaxIdentifierChars || x.Value < 0))
            return SlotReadbackStatus.Unavailable;
        if (!Unique(store, "mainInvJSON", out var encoded) ||
            encoded.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(encoded.GetString()))
            return SlotReadbackStatus.Malformed;
        using var inventory = OfficialSaveReadbackApi.Parse(encoded.GetString()!,
            AugNumbers.Validation.NativeSaveJsonDepth);
        if (!Unique(inventory.RootElement, "saveItems", out var items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0 ||
            !Unique(items[0], "childItems", out var children) ||
            children.ValueKind != JsonValueKind.Array)
            return SlotReadbackStatus.Malformed;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<int>();
        foreach (var child in children.EnumerateArray())
        {
            if (child.ValueKind != JsonValueKind.Number ||
                !child.TryGetInt32(out int index) || index <= 0 ||
                index >= items.GetArrayLength() || !seen.Add(index) ||
                !Unique(items[index], "identifier", out var identifier) ||
                identifier.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(identifier.GetString()) ||
                !Unique(items[index], "unitCount", out var units) ||
                units.ValueKind != JsonValueKind.Number ||
                !units.TryGetInt32(out int count) || count < 0)
                return SlotReadbackStatus.Malformed;
            string itemId = identifier.GetString()!;
            if (!expected.ContainsKey(itemId)) continue;
            int previous = counts.GetValueOrDefault(itemId);
            counts[itemId] = checked(previous + count);
        }
        return expected.Any(x => counts.GetValueOrDefault(x.Key) != x.Value)
            ? SlotReadbackStatus.NativeInventoryMismatch : null;
    }

    // Match the exact native item identity in a reachable saved inventory.
    // A detached entry in saveItems does not count as owned. Consumables may
    // reside in the player's back room, trashcan, or other saved inventory;
    // other callers retain their main-inventory-only contract by default.
    private static SlotReadbackStatus? CompareOwnedItem(JsonElement store,
        NativeItemIdentitySnapshot expected)
    {
        if (string.IsNullOrWhiteSpace(expected.Identifier) ||
            expected.Identifier.Length > AugNumbers.Validation.MaxIdentifierChars || expected.UniqueId <= 0)
            return SlotReadbackStatus.Unavailable;
        if (!Unique(store, "mainInvJSON", out var encoded) ||
            encoded.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(encoded.GetString()))
            return SlotReadbackStatus.Malformed;
        bool found = false;
        var fields = expected.SearchScope == NativeItemSearchScope.MainInventory
            ? new[] { new KeyValuePair<string, JsonElement>("mainInvJSON", encoded) }
            : store.EnumerateObject()
                .Where(x => x.Name.EndsWith("InvJSON", StringComparison.Ordinal) ||
                            x.Name.EndsWith("InventoryJSON", StringComparison.Ordinal))
                .Select(x => new KeyValuePair<string, JsonElement>(x.Name, x.Value))
                .ToArray();
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!fieldNames.Add(field.Key)) return SlotReadbackStatus.Malformed;
            if (expected.RequirePlayerOwned && field.Key == "soldInvJSON") continue;
            if (field.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
                field.Value.ValueKind == JsonValueKind.String &&
                string.IsNullOrWhiteSpace(field.Value.GetString()))
                continue;
            if (field.Value.ValueKind != JsonValueKind.String)
                return SlotReadbackStatus.Malformed;
            using var inventory = OfficialSaveReadbackApi.Parse(field.Value.GetString()!,
                AugNumbers.Validation.NativeSaveJsonDepth);
            if (!Unique(inventory.RootElement, "saveItems", out var items) ||
                items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                return SlotReadbackStatus.Malformed;
            var visited = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(0);
            while (pending.Count > 0)
            {
                int index = pending.Pop();
                if (!visited.Add(index) || index < 0 || index >= items.GetArrayLength())
                    return SlotReadbackStatus.Malformed;
                var item = items[index];
                if (index != 0)
                {
                    if (!Unique(item, "identifier", out var identifier) ||
                        identifier.ValueKind != JsonValueKind.String ||
                        !Unique(item, "uniqueId", out var uniqueId) ||
                        uniqueId.ValueKind != JsonValueKind.Number ||
                        !uniqueId.TryGetInt32(out int itemId))
                        return SlotReadbackStatus.Malformed;
                    if (itemId == expected.UniqueId)
                    {
                        if (found || identifier.GetString() != expected.Identifier)
                            return SlotReadbackStatus.NativeInventoryMismatch;
                        if (expected.UnitCount is { } expectedCount &&
                            (!Unique(item, "unitCount", out var units) ||
                             units.ValueKind != JsonValueKind.Number ||
                             !units.TryGetInt32(out int count) || count != expectedCount))
                            return SlotReadbackStatus.NativeInventoryMismatch;
                        if (expected.RequirePlayerOwned &&
                            !SavedTag(item, "IS_OWNED_TAG", out _))
                            return SlotReadbackStatus.NativeInventoryMismatch;
                        if (expected.FullEnergy is { } energy && (energy <= 0 ||
                            !SavedTag(item, "power_source_item", out _) ||
                            !SavedTag(item, "power_source_item_energy", out var current) ||
                            !SavedTag(item, "power_source_item_max_energy", out var maximum) ||
                            !Unique(current, "valueInt", out var currentValue) ||
                            !Unique(maximum, "valueInt", out var maximumValue) ||
                            currentValue.ValueKind != JsonValueKind.Number || maximumValue.ValueKind != JsonValueKind.Number ||
                            !currentValue.TryGetInt32(out int currentEnergy) || currentEnergy != energy ||
                            !maximumValue.TryGetInt32(out int capacity) || capacity != energy))
                            return SlotReadbackStatus.NativeInventoryMismatch;
                        found = true;
                    }
                }
                if (!UniqueOptional(item, "childItems", out var children, out bool hasChildren))
                    return SlotReadbackStatus.Malformed;
                if (!hasChildren) continue;
                if (children.ValueKind != JsonValueKind.Array)
                    return SlotReadbackStatus.Malformed;
                foreach (var child in children.EnumerateArray())
                {
                    if (child.ValueKind != JsonValueKind.Number ||
                        !child.TryGetInt32(out int childIndex) || childIndex <= 0 ||
                        childIndex >= items.GetArrayLength())
                        return SlotReadbackStatus.Malformed;
                    pending.Push(childIndex);
                }
            }
        }
        return found == expected.MustBePresent
            ? null : SlotReadbackStatus.NativeInventoryMismatch;
    }

    // Native saved GameItem states use aligned _keys/_values arrays, not a tag dictionary.
    private static bool SavedTag(JsonElement item, string name, out JsonElement value)
    {
        value = default;
        if (!Unique(item, "_keys", out var keys) || !Unique(item, "_values", out var values) ||
            keys.ValueKind != JsonValueKind.Array || values.ValueKind != JsonValueKind.Array ||
            keys.GetArrayLength() != values.GetArrayLength()) return false;
        int index = -1;
        for (int i = 0; i < keys.GetArrayLength(); i++)
        {
            if (keys[i].ValueKind != JsonValueKind.String) return false;
            if (keys[i].GetString() != name) continue;
            if (index >= 0) return false;
            index = i;
        }
        if (index < 0) return false;
        value = values[index];
        return Unique(value, "<identifier>k__BackingField", out var identifier) &&
            identifier.ValueKind == JsonValueKind.String && identifier.GetString() == name &&
            Unique(value, "valueEnabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
    }

    private static bool UniqueOptional(JsonElement parent, string name,
        out JsonElement value, out bool present) =>
        OfficialSaveReadbackApi.TryGetUniqueProperty(parent, name, out value, out present);

    internal static string Normalize(string source) => OfficialSaveReadbackApi.Normalize(source);
}
