using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DefinitelyStolen.Domain;

namespace DefinitelyStolen.Persistence;

internal static class CandidateState
{
    internal static bool IsWater(string id) => id is "mini_bottle" or "small_bottled_water" or "bottled_water" or "large_bottled_water";
    internal static bool IsFish(string id) => id is "tilapia" or "gutter_tilapia" or "graft_tilapia" or "eel" or "electric_eel" or "sturgeon";
    internal static bool NeedsNativeState(string id) => id is "pink_alt_injector" or "industrial_fish_feed" or "gutterflow_pellet" or
        "nutrifruit_seed" or "kotton_seed" or "fillerweed_seed" or "hydroreed_seed" or "bloomberry_seed" or
        "nutrifruit" or "hydroreed_filter" or "plant_dna_disk" or "metal_ingot";
    internal const string MissingChemicalState = "missing-chemical-state";
    internal const string MissingNativeState = "missing-native-state";

    internal static void PreserveNativeState(RunState state)
    {
        if (state.Night is not { } night) return;
        foreach (var item in night.Npcs.SelectMany(npc => npc.Items).Where(item => NeedsNativeState(item.Id)))
        {
            if (item.NativeStateVersion is not (null or 1)) throw new InvalidDataException("Unsupported native candidate state");
            // Old crop/ingot templates predate full-state candidates. Keep their
            // original supported semantics instead of inventing historical genes.
            if (item.NativeStateVersion == null && item.Graph.Length == 0 && item.Id is "nutrifruit" or "metal_ingot") continue;
            item.NativeStateVersion = 1;
            if (item.Available) continue;
            foreach (var note in state.Notes.Where(note => note.Day == night.Day && note.ItemUniqueId == item.UniqueId && !note.Invalidated))
            { note.Invalidated = true; note.InvalidationReason = item.Id == "pink_alt_injector" ? MissingChemicalState : MissingNativeState; }
        }
        if (night.Timing is { } timing && night.Npcs.Single(npc => npc.Key == timing.Target) is { } target &&
            !target.Items.Any(item => item.Available))
        {
            // The reserved attempt has no possible item now; release it without
            // rerolling the rest of this night or counting it as a failed theft.
            night.Timing = null;
            night.Attempts--;
            target.Attempts--;
            if (!timing.Infinite) night.SpentActions--;
        }
    }

    // Stateful candidates keep the game's graph rather than a second trait,
    // calorie or chemistry codec. Older supported templates remain lightweight.
    internal static void Compact(ItemState item)
    {
        if (NeedsNativeState(item.Id)) return;
        if (IsWater(item.Id) && item.LiquidParts == null)
        {
            var graph = JObject.Parse(item.Graph);
            var node = ((JArray?)graph["saveItems"] ?? throw new InvalidDataException("Missing legacy nodes"))
                .Single(x => x.Value<int>("uniqueId") == item.UniqueId);
            var keys = node["_keys"]?.Values<string>().ToArray() ?? throw new InvalidDataException("Missing legacy tags");
            var values = node["_values"]?.ToArray() ?? throw new InvalidDataException("Missing legacy tag values");
            if (keys.Length != values.Length) throw new InvalidDataException("Invalid legacy liquid tags");
            item.LiquidParts = new(StringComparer.Ordinal);
            for (int i = 0; i < keys.Length; i++)
                if (keys[i]!.StartsWith("CURRENT_PART_", StringComparison.Ordinal) && values[i].Value<int>("valueInt") is > 0 and var amount)
                    item.LiquidParts.Add(keys[i]!, amount);
        }
        item.Graph = "";
    }

    internal static IEnumerable<int> Ids(ItemState item) => item.Graph.Length == 0
        ? new[] { item.UniqueId } : GraphIdentity.Ids(item.Graph);

    internal static void Validate(ItemState item)
    {
        if (item.Id is "energy_credit" or "energy_credit_ext" or "energy_credit_breeder")
        {
            if (item.Charge == null || item.MaxCharge is not > 0)
                throw new InvalidDataException("Candidate battery charge is missing");
        }
        if (IsWater(item.Id))
        {
            if (item.VolumeMl is not > 0 || item.CapacityMl != item.VolumeMl || item.Purity == null ||
                item.LiquidParts is not { Count: > 0 } || item.LiquidParts.Any(x =>
                    !x.Key.StartsWith("CURRENT_PART_", StringComparison.Ordinal) || x.Value <= 0) ||
                item.LiquidParts.Sum(x => (long)x.Value) != (long)item.VolumeMl * 1000)
                throw new InvalidDataException("Candidate liquid composition is missing or inconsistent");
        }
        else if (item.LiquidParts != null) throw new InvalidDataException("Unexpected candidate liquid composition");
        if (item.Id == "packet_red_cigarette" && item.Condition is not ("genuine" or "counterfeit"))
            throw new InvalidDataException("Candidate cigarette authenticity is missing");
        if (IsFish(item.Id) && item.Condition is not ("MALE" or "FEMALE"))
            throw new InvalidDataException("Candidate fish sex is missing");
    }
}
