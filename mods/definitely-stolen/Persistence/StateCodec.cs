using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DefinitelyStolen.Domain;

namespace DefinitelyStolen.Persistence;

internal static class StateCodec
{
    private static readonly JsonSerializerSettings Options = new() { TypeNameHandling = TypeNameHandling.None, FloatParseHandling = FloatParseHandling.Double };
    internal static string Encode(RunState state) => JsonConvert.SerializeObject(state, Options);
    internal static RunState Clone(RunState state)
    {
        var copy = JsonConvert.DeserializeObject<RunState>(Encode(state), Options)!;
        copy.LastAction = state.LastAction;
        return copy;
    }
    internal static void Validate(RunState state, Catalog catalog) => Decode(Encode(state), state.RunId, state.SlotId, catalog);
    internal static RunState Decode(string json, string runId, int slotId, Catalog catalog, Action<string>? repairNight = null)
    {
        RequireUnique(json);
        var state = JsonConvert.DeserializeObject<RunState>(json, Options) ?? throw new InvalidDataException("Missing state");
        if (state.Schema == 1)
        {
            if (state.Experience is < 0 or > 1400) throw new InvalidDataException("Invalid legacy experience");
            int level = 0;
            int OldThreshold(int value) => 10 * value * value + 40 * value;
            while (level < 10 && state.Experience >= OldThreshold(level + 1)) level++;
            state.Experience = level == 10 ? TheftRules.Threshold(10) : TheftRules.Threshold(level)
                + (state.Experience - OldThreshold(level)) * (TheftRules.Threshold(level + 1) - TheftRules.Threshold(level))
                / (OldThreshold(level + 1) - OldThreshold(level));
            state.Schema = 2;
        }
        if (state.Schema == 2)
        {
            // Existing nights had three items; keep their exact inventory and consumed thefts.
            if (state.Night != null)
                foreach (var npc in state.Night.Npcs)
                {
                    if (npc.Items.Count > 3) throw new InvalidDataException("Invalid legacy target inventory");
                    npc.SuccessfulThefts = 3 - npc.Items.Count;
                }
            state.Schema = 3;
        }
        if (!Enum.IsDefined(typeof(TheftMode), state.PreferredMode) || state.Schema is not (3 or 4 or 5) || state.RunId != runId || state.SlotId != slotId || state.Revision < 0 ||
            state.Experience < 0 || state.Experience > TheftRules.Threshold(10) || state.Wind is < 0 or > 100 || state.RandomState == 0 ||
            state.QualificationSource is not ("none" or "perk" or "career" or "config") ||
            state.Qualified != (state.QualificationSource != "none") || !state.Qualified && state.Habitual)
            throw new InvalidDataException("Unsupported or inconsistent run state");
        if (state.Night is { } night)
        {
            // A previously generated night has no event effects, including an
            // unfinished attempt. Only a later night's generation uses events.
            if (state.Schema < 5) night.RegionEvents = NoEvents();
            try { NightEvents.Validate(night.RegionEvents, catalog); }
            catch (Exception ex) when (repairNight != null)
            {
                night.RegionEvents = NoEvents();
                repairNight("Reset saved night event modifiers: " + ex.Message);
            }
            if (!Enum.IsDefined(typeof(TheftMode), night.Mode) || night.Day < 0 || night.GenerationLevel is < 0 or > 10 || night.RandomState == 0 ||
                night.Attempts < 0 || night.SpentActions < 0 || night.SpentActions > night.Attempts ||
                night.SpentActions > TheftRules.Actions(TheftRules.Level(state.Experience)) ||
                night.Npcs.Count != 9 || night.Caught && !night.Ended ||
                night.Escaped && (!night.Started || night.Attempts == 0 || night.Caught) || night.Started != night.LockedRegion.HasValue ||
                night.LockedRegion is < 0 or > 2 || night.PendingWind < 0 || !Numbers.Finite(night.WindPenalty) ||
                night.WindPenalty is < 0 or > .20)
                throw new InvalidDataException("Invalid night state");
            foreach (var npc in night.Npcs)
            {
                var role = catalog.Role(npc.RoleId);
                if (npc.Region is < 0 or > 2 || npc.Position is < 0 or > 2 || npc.Key != $"{night.Day}:{npc.Region}:{npc.Position}" ||
                    npc.Attempts < 0 || !role.Enabled || npc.Faction != role.Faction || npc.Items.Count > TheftRules.InitialItems || npc.SuccessfulThefts is < 0 or > TheftRules.TargetLimit || npc.TokenEligible && npc.RoleId != "scavGeneral" ||
                    !Numbers.Finite(npc.ManualSuccessPosition) || npc.ManualSuccessPosition is < 0 or > 1 ||
                    !Numbers.Finite(npc.TokenAdmission) || npc.TokenAdmission is < 0 or > 1)
                    throw new InvalidDataException("Invalid saved target");
                var candidates = SpecialNpcs.Items(state.RunId, npc, role).ToHashSet(StringComparer.Ordinal);
                foreach (var item in npc.Items)
                    if (!candidates.Contains(item.Id) || item.Id == "scav_token" && !npc.TokenEligible)
                        throw new InvalidDataException("Candidate escaped its identity");
            }
            if (night.Npcs.Select(x => x.Key).Distinct().Count() != 9)
                throw new InvalidDataException("Duplicate target slots");
            if (night.Timing is { } timing && (night.Ended || night.Escaped || !night.Started ||
                !night.Npcs.Any(x => x.Key == timing.Target && x.Region == night.LockedRegion && x.Items.Count > 0 && x.SuccessfulThefts < TheftRules.TargetLimit) ||
                timing.TimedOut && (night.Mode != TheftMode.Timing || timing.StopPosition != 0) ||
                timing.StartedUtcTicks <= 0 || timing.StartedUtcTicks > DateTime.MaxValue.Ticks ||
                timing.Level != TheftRules.Level(state.Experience) || timing.AttemptBefore != night.Attempts - 1 ||
                !Numbers.Finite(timing.SuccessWidth) || !Numbers.Finite(timing.ExposureWidth) ||
                timing.SuccessWidth is < 0 or > 1 || timing.ExposureWidth is < 0 or > 1 ||
                timing.SuccessWidth + timing.ExposureWidth > 1 ||
                !Numbers.Finite(timing.SuccessPosition) || timing.SuccessPosition is < 0 or > 1 ||
                night.Mode == TheftMode.Probability && (timing.SuccessPosition != .5 || timing.RandomizeSuccessZoneAfterSuccess) ||
                !Numbers.Finite(timing.MissExposure) || timing.MissExposure is < 0 or > 1 ||
                (night.Mode == TheftMode.Probability) != timing.AutomaticOutcome.HasValue ||
                timing.AutomaticOutcome.HasValue != timing.AutomaticPosition.HasValue ||
                timing.AutomaticOutcome is { } automatic && (!Enum.IsDefined(typeof(AttemptOutcome), automatic) ||
                    !Numbers.Finite(timing.AutomaticPosition!.Value) || timing.AutomaticPosition is < 0 or > 1 ||
                    timing.Zones.At(timing.AutomaticPosition.Value) != automatic ||
                    timing.StopPosition is double locked && locked != timing.AutomaticPosition.Value) ||
                !Numbers.Finite(timing.AutomaticSuccess) || timing.AutomaticSuccess is < 0 or > 1 ||
                !Numbers.Finite(timing.AutomaticExposure) || timing.AutomaticExposure is < 0 or > 1 ||
                timing.StopPosition is double stop && (!Numbers.Finite(stop) || stop is < 0 or > 1)))
            {
                if (repairNight == null) throw new InvalidDataException("Invalid timing attempt");
                night.Timing = null;
                repairNight("Reset invalid saved timing attempt");
            }
            var items = night.Npcs.SelectMany(x => x.Items).Concat(night.Loot).Concat(night.Abandoned).ToArray();
            if (items.Select(x => x.UniqueId).Distinct().Count() != items.Length)
                throw new InvalidDataException("Duplicate custody");
            foreach (var item in night.Npcs.SelectMany(x => x.Items)) ValidateItem(item, catalog.DropIds, candidate: state.Schema >= 4);
            foreach (var item in night.Loot.Concat(night.Abandoned)) ValidateItem(item, catalog.DropIds);
            var targetIds = new HashSet<int>();
            var ownedIds = new HashSet<int>();
            foreach (var item in items)
                foreach (int id in CandidateState.Ids(item))
                    if (!ownedIds.Add(id)) throw new InvalidDataException("Duplicate custody graph node");
            foreach (var item in night.Npcs.SelectMany(x => x.Items).Concat(night.Abandoned))
                targetIds.UnionWith(CandidateState.Ids(item));
            var carryIds = new HashSet<int>();
            foreach (var carry in night.Carry)
            {
                var graphIds = GraphIdentity.Ids(carry.Graph);
                if (carry.Inventory is not ("grid" or "left" or "right" or "backpack") ||
                    !graphIds.Contains(carry.UniqueId) || graphIds.Overlaps(targetIds) || graphIds.Overlaps(carryIds)) throw new InvalidDataException("Invalid carry graph custody");
                carryIds.UnionWith(graphIds);
            }
            if (night.Outside && night.Loot.SelectMany(x => GraphIdentity.Ids(x.Graph)).Any(id => !carryIds.Contains(id)))
                throw new InvalidDataException("Saved outing is missing its loot graph");
            if (night.Ended && (night.Outside || night.Carry.Count != 0))
                throw new InvalidDataException("Ended outing retains an outside inventory");
        }
        bool invalidOpeningCash = state.OpeningCashDelta is int delta
            ? state.OpeningCash != 0 || state.OpeningItems.Where(x => !OpeningRules.Fixed.Contains(x.Id))
                .Sum(x => x.Value) + delta != OpeningRules.GoodsAndCashBudget
            : state.OpeningCash is < 400 or > 600;
        if (state.OpeningGranted && (invalidOpeningCash ||
            state.OpeningItems.Select(x => x.Id).Distinct().Count() != state.OpeningItems.Count))
            throw new InvalidDataException("Invalid opening receipt");
        if (state.Notes.GroupBy(x => x.Day).Any(g => g.Count() != 1) || state.Notes.Any(x => x.Day < 0 || x.Delivered && x.Paper == null))
            throw new InvalidDataException("Invalid intelligence receipt");
        if (state.Schema == 3)
        {
            if (state.Night != null)
                foreach (var item in state.Night.Npcs.SelectMany(x => x.Items))
                { CandidateState.Compact(item); CandidateState.Validate(item); }
            state.Schema = 4;
        }
        CandidateState.PreserveNativeState(state);
        state.Schema = 5;
        TrimReceipts(state);
        return state;
    }
    private static List<RegionEventState> NoEvents() =>
        Enumerable.Range(0, 3).Select(region => new RegionEventState { Region = region }).ToList();
    internal static void TrimReceipts(RunState state)
    {
        // These items are already owned by native inventories. Keep receipt
        // identity for grants/intelligence, without duplicating their graphs.
        foreach (var item in state.OpeningItems) item.Graph = "";
        foreach (var note in state.Notes)
            if (note.Paper != null && (note.Delivered || note.Expired || note.Invalidated)) note.Paper.Graph = "";
    }
    private static void ValidateItem(ItemState item, HashSet<string> allowed, bool candidate = false)
    {
        // Preserve saved rarity and draw weights across balance changes.
        if (!allowed.Contains(item.Id) || item.UniqueId <= 0 || item.Graph == null || item.Value < 0 ||
            !Numbers.Finite(item.Weight) || item.Weight <= 0 ||
            item.Heat is not (0 or 25 or 50 or 75) || item.Charge is < 0 || item.Charge > item.MaxCharge ||
            item.VolumeMl is < 0 || item.VolumeMl > item.CapacityMl || item.Purity is < 0 or > 10000)
            throw new InvalidDataException($"Invalid saved item state: id={item.Id}, uid={item.UniqueId}, value={item.Value}, " +
                $"rare={item.Rare}, weight={item.Weight}, heat={item.Heat}, charge={item.Charge}/{item.MaxCharge}, " +
                $"volume={item.VolumeMl}/{item.CapacityMl}, purity={item.Purity}");
        if (item.Graph.Length == 0)
        {
            if (!candidate) throw new InvalidDataException("Saved physical item has no native graph");
            CandidateState.Validate(item); return;
        }
        var nodes = GraphIdentity.Nodes(item.Graph);
        if (!nodes.TryGetValue(item.UniqueId, out var root) || root.Id != item.Id || root.Units != 1)
            throw new InvalidDataException("Saved graph root identity or units changed");
    }
    private static void RequireUnique(string json)
    {
        using var input = new StringReader(json);
        using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
        var scopes = new Stack<HashSet<string>?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.StartObject) scopes.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonToken.StartArray) scopes.Push(null);
            else if (reader.TokenType is JsonToken.EndObject or JsonToken.EndArray) scopes.Pop();
            else if (reader.TokenType == JsonToken.PropertyName)
            {
                string name = (string)reader.Value!;
                if (scopes.Count == 1 && name == nameof(RunState.LastAction))
                {
                    // Historical animation receipts are session-only display
                    // data, excluded from both deserialization and validation.
                    if (!reader.Read()) throw new InvalidDataException("Truncated display receipt");
                    reader.Skip(); continue;
                }
                if (scopes.Count == 0 || scopes.Peek() is not { } names || !names.Add(name)) throw new InvalidDataException("Duplicate state property");
            }
        }
    }
}
