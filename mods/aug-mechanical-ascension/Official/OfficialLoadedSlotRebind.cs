using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Serialization;

namespace AugMechanicalAscension.Official;

internal sealed record LoadedSlotBinding(string Key,
    Func<string, string, int, string, int, string> Rebind);
internal sealed record LoadedSlotWrite(string Key, string Before, string After);
internal sealed record LoadedSlotPlan(int SourceSlot, int TargetSlot, LoadedSlotWrite[] Writes);
internal sealed record LoadedSlotRebindResult(string Status, int SourceSlot, int TargetSlot,
    string[] Keys, string Reason);

/// <summary>Stages addressing changes from the game's already-loaded modData.
/// Never opens a save file, allocates a run, saves, grants, or replays receipts.</summary>
internal static class OfficialLoadedSlotRebind
{
    internal static LoadedSlotPlan PrepareLoaded(IReadOnlyDictionary<string, string> loaded,
        string runId, int currentSlot, int day, int startType, string scenario,
        IReadOnlyList<LoadedSlotBinding> bindings)
    {
        if (!loaded.TryGetValue(RunState.SaveKey, out var core))
        {
            if (bindings.Any(binding => loaded.ContainsKey(binding.Key) || loaded.ContainsKey("nicokobo.aug:" + binding.Key)))
                throw new InvalidDataException("Loaded Aug carriers have no core source identity");
            return new(currentSlot, currentSlot, []);
        }
        using var document = JsonDocument.Parse(core);
        int sourceSlot = OfficialSaveReadbackApi.RequireProperty(document.RootElement, "saveSlotId").GetInt32();
        if (sourceSlot < 0 || currentSlot < 0 || day < 0 ||
            OfficialSaveReadbackApi.RequireProperty(document.RootElement, "gameRunId").GetString() != runId)
            throw new InvalidDataException("Loaded Aug state belongs to another run");
        // Same-slot history stays byte-for-byte with the ordinary readers,
        // including unknown schemas. Only the loaded addressing above is read.
        if (sourceSlot == currentSlot) return new(sourceSlot, currentSlot, []);
        if (startType != StartDefinition.NativeStartTypeId || scenario != StartDefinition.Id)
            throw new InvalidDataException("Copied Aug state has a different native scenario");
        var writes = new List<LoadedSlotWrite>();
        foreach (var binding in bindings.OrderBy(binding => binding.Key, StringComparer.Ordinal))
        {
            if (loaded.ContainsKey("nicokobo.aug:" + binding.Key))
                throw new InvalidDataException("Ambiguous loaded state carrier: " + binding.Key);
            if (!loaded.TryGetValue(binding.Key, out var before)) continue;
            // Each registered production migration validates its real codec,
            // identity, schema and unfinished transaction before rebinding.
            writes.Add(new(binding.Key, before, binding.Rebind(before, runId, sourceSlot, runId, currentSlot)));
        }
        return new(sourceSlot, currentSlot, writes.ToArray());
    }

    internal static void Apply(LoadedSlotPlan plan, IDictionary<string, string> data, Func<bool> sameNativeAddress)
    {
        if (plan.Writes.Length == 0) return;
        var staged = new HashSet<string>(StringComparer.Ordinal);
        var attempted = new List<LoadedSlotWrite>();
        bool Unchanged() => plan.Writes.All(write =>
            !data.ContainsKey("nicokobo.aug:" + write.Key) &&
            data.TryGetValue(write.Key, out var value) &&
            value == (staged.Contains(write.Key) ? write.After : write.Before));
        if (!sameNativeAddress() || !Unchanged())
            throw new InvalidDataException("Loaded source changed before staging");
        try
        {
            foreach (var write in plan.Writes)
            {
                // A dictionary setter/native callback can reenter between
                // writes. Never overwrite another carrier based on old checks.
                if (!sameNativeAddress() || !Unchanged())
                    throw new InvalidDataException("Loaded source changed during staging");
                attempted.Add(write); // Includes a setter that mutates then throws.
                data[write.Key] = write.After;
                staged.Add(write.Key);
            }
            if (!sameNativeAddress() || !Unchanged())
                throw new InvalidDataException("Loaded-slot staging readback failed");
        }
        catch
        {
            foreach (var write in attempted.AsEnumerable().Reverse())
                if (data.TryGetValue(write.Key, out var value) && value == write.After)
                    data[write.Key] = write.Before;
            throw;
        }
    }
}
