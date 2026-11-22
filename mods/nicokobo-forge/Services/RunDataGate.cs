using System.Text.RegularExpressions;
namespace Nicokobo.ContentInterop;
internal static class RunDataGate
{
    private static readonly Regex NamespacedId = new(
        "^[a-z][a-z0-9]*(\\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant);
    private const int MaxJsonChars = 4_000_000;
    private static bool _enabled = true;

    internal static void SetEnabled(bool enabled) => _enabled = enabled;

    public static RunDataValue Read(PlayerStore? store, string ownerId, string key)
    {
        if (!_enabled) return new(RunDataState.Disabled);
        if (!ValidKey(ownerId, key)) return new(RunDataState.Invalid, Reason: "Invalid owner or key");
        if (store == null) return new(RunDataState.Unavailable);
        try
        {
            var data = store.modData;
            if (data == null || string.IsNullOrWhiteSpace(store.runID) || store.saveSlotId < 0)
                return new(RunDataState.Unavailable);
            if (!data.ContainsKey(key)) return new(RunDataState.Missing);
            string json = data[key];
            return ValidJson(json)
                ? new(RunDataState.Present, json)
                : new(RunDataState.Invalid, Reason: "Stored JSON is malformed or too large");
        }
        catch (Exception ex)
        {
            return new(RunDataState.Unavailable, Reason: ex.GetType().Name);
        }
    }

    /// <param name="expectedJson">Null means the key must not exist. A present
    /// value must match byte-for-byte, even when JSON is semantically equivalent.</param>
    public static RunDataValue Stage(PlayerStore? store, string ownerId, string key,
        string runId, int slotId, string? expectedJson, string nextJson)
    {
        if (!_enabled) return new(RunDataState.Disabled);
        if (!ValidKey(ownerId, key) || string.IsNullOrWhiteSpace(runId) || slotId < 0 ||
            !ValidJson(nextJson) || expectedJson != null && !ValidJson(expectedJson))
            return new(RunDataState.Invalid, Reason: "Invalid identity, key or JSON");
        if (store == null) return new(RunDataState.Unavailable);
        if (!PlayerStore.HasInstance || !ReferenceEquals(store, PlayerStore.Instance))
            return new(RunDataState.Conflict, Reason: "Active store changed");
        bool attempted = false;
        try
        {
            var data = store.modData;
            if (data == null) return new(RunDataState.Unavailable);
            if (store.runID != runId || store.saveSlotId != slotId)
                return new(RunDataState.Conflict, Reason: "Run identity changed");
            bool present = data.ContainsKey(key);
            if (present != (expectedJson != null) ||
                present && data[key] != expectedJson)
                return new(RunDataState.Conflict, Reason: "Stored revision changed");
            if (store.runID != runId || store.saveSlotId != slotId ||
                data.ContainsKey(key) != present ||
                present && data[key] != expectedJson)
                return new(RunDataState.Conflict, Reason: "Preflight changed");
            attempted = true;
            if (present) data[key] = nextJson;
            else data.Add(key, nextJson);
            return data.ContainsKey(key) && data[key] == nextJson &&
                store.runID == runId && store.saveSlotId == slotId
                ? new(RunDataState.StagedInMemory, nextJson)
                : new(RunDataState.Indeterminate);
        }
        catch (Exception ex)
        {
            return new(attempted ? RunDataState.Indeterminate : RunDataState.Unavailable,
                Reason: ex.GetType().Name);
        }
    }

    private static bool ValidKey(string ownerId, string key) =>
        !string.IsNullOrWhiteSpace(ownerId) && NamespacedId.IsMatch(ownerId) &&
        !string.IsNullOrWhiteSpace(key) && NamespacedId.IsMatch(key) &&
        key.StartsWith(ownerId + ".", StringComparison.Ordinal);

    private static bool ValidJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonChars) return false;
        try
        {
            return Newtonsoft.Json.Linq.JToken.Parse(json) is Newtonsoft.Json.Linq.JObject;
        }
        catch (Newtonsoft.Json.JsonException) { return false; }
    }
}
