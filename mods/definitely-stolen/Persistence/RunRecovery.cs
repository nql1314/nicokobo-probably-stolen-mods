using DefinitelyStolen.Domain;
using Newtonsoft.Json.Linq;

namespace DefinitelyStolen.Persistence;

internal static class RunRecovery
{
    private static readonly string[] OpeningFields = { nameof(RunState.OpeningGranted), nameof(RunState.OpeningCash),
        nameof(RunState.OpeningCashDelta), nameof(RunState.OpeningItems) };

    internal static RunState Decode(JObject payload, RunState fresh, Catalog catalog, Action<string> warn)
    {
        var core = (JObject)payload.DeepClone();
        foreach (var key in OpeningFields.Concat(new[] { nameof(RunState.Notes), nameof(RunState.Night), nameof(RunState.RecoveryCarry) }))
            core.Remove(key);
        core[nameof(RunState.RunId)] = fresh.RunId;
        core[nameof(RunState.SlotId)] = fresh.SlotId;

        RunState state;
        try { state = DecodeSection(core); }
        catch (Exception ex)
        {
            warn("Reset theft progression data: " + ex.Message);
            core = JObject.FromObject(fresh);
            state = fresh;
        }

        // Each section can fail independently. Decode with the original schema
        // so legacy targets and experience still go through the normal migrations.
        TrySection("opening receipt", OpeningFields);
        TrySection("intelligence", new[] { nameof(RunState.Notes) });
        if (!TrySection("night", new[] { nameof(RunState.Night) }, warn) && payload[nameof(RunState.Night)] is JObject night)
        {
            RecoverCarry(night[nameof(NightState.Carry)], state, warn);
            if (night[nameof(NightState.Day)] is { Type: JTokenType.Integer } day)
                InvalidateNotes(state, day.Value<long>());
        }
        RecoverCarry(payload[nameof(RunState.RecoveryCarry)], state, warn);
        return state;

        RunState DecodeSection(JObject section, Action<string>? repairNight = null) =>
            StateCodec.Decode(section.ToString(), fresh.RunId, fresh.SlotId, catalog, repairNight);
        bool TrySection(string name, string[] fields, Action<string>? repairNight = null)
        {
            var candidate = (JObject)core.DeepClone();
            foreach (string field in fields)
                if (payload[field] is { } value) candidate[field] = value.DeepClone();
            try { state = DecodeSection(candidate, repairNight); core = candidate; return true; }
            catch (Exception ex) { warn($"Reset theft {name}: {ex.Message}"); return false; }
        }
    }

    internal static void ResetNight(RunState state)
    {
        if (state.Night is not { } night) return;
        foreach (var carry in night.Carry) QueueCarry(state, carry);
        InvalidateNotes(state, night.Day);
        state.Night = null;
        state.LastAction = null;
        state.Revision++;
    }

    private static void InvalidateNotes(RunState state, long day)
    {
        foreach (var note in state.Notes.Where(note => note.Day == day)) note.Invalidated = true;
    }

    private static void RecoverCarry(JToken? values, RunState state, Action<string> warn)
    {
        if (values is not JArray entries) return;
        foreach (JToken entry in entries)
        {
            try
            {
                var carry = entry.ToObject<CarryState>() ?? throw new InvalidDataException("Missing carry entry");
                if (!GraphIdentity.Nodes(carry.Graph).ContainsKey(carry.UniqueId))
                    throw new InvalidDataException("Missing carry root");
                QueueCarry(state, carry);
            }
            catch (Exception ex) { warn("Skipped unreadable theft carry entry: " + ex.Message); }
        }
    }

    private static void QueueCarry(RunState state, CarryState carry)
    {
        var pending = state.RecoveryCarry ??= new();
        if (!pending.Any(saved => saved.UniqueId == carry.UniqueId)) pending.Add(carry);
    }
}
