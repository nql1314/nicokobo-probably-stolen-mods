namespace DefinitelyStolen.Domain;

// This PRNG is persisted with the run. It never consumes Unity's global random
// stream; reopening UI and loading the same revision cannot reroll an attempt.
internal sealed class StableRandom
{
    internal ulong State { get; private set; }
    internal StableRandom(ulong state) => State = state != 0 ? state : 0x9e3779b97f4a7c15UL;
    internal ulong Next()
    {
        ulong value = State;
        value ^= value >> 12; value ^= value << 25; value ^= value >> 27;
        State = value;
        return unchecked(value * 2685821657736338717UL);
    }
    internal double Unit() => (Next() >> 11) * (1.0 / (1UL << 53));
    internal int Index(int count) => count > 0 ? Math.Min(count - 1, (int)(Unit() * count))
        : throw new ArgumentOutOfRangeException(nameof(count));
    internal T Weighted<T>(IReadOnlyList<T> values, Func<T, double> weight)
    {
        if (values.Count == 0) throw new InvalidOperationException("Empty pool");
        double total = values.Sum(weight);
        if (!Numbers.Finite(total) || total <= 0) throw new InvalidOperationException("Invalid weights");
        double roll = Unit() * total;
        foreach (var value in values) { roll -= weight(value); if (roll < 0) return value; }
        return values[^1];
    }
}

internal readonly record struct Probabilities(double Success, double Exposure, double Rare)
{
    internal double Failure => (1 - Success) * (1 - Exposure);
    internal double Caught => (1 - Success) * Exposure;
}

internal static class TheftRules
{
    internal const int InitialItems = 5;
    internal const int TargetLimit = 3;
    internal static bool CanSteal(NpcState npc) => npc.Items.Any(item => item.Available) && npc.SuccessfulThefts < TargetLimit;
    internal static (double Exposure, double Rare) RegionFactors(int region) => region switch
    {
        0 => (.875, .849),
        1 => (1.215, .887),
        2 => (1.44, 1.00),
        _ => throw new ArgumentOutOfRangeException(nameof(region))
    };
    internal static (double Automatic, double Growth, double Manual) FactionSuccess(string faction) => faction switch
    {
        "FACTION_LOWER_LEVEL" => (.75, .02, .15),
        "FACTION_MIDDLE" => (.65, .025, .10),
        "FACTION_SECURITY" => (.65, .025, .10),
        "FACTION_TOURIST" => (.80, .02, .20),
        "FACTION_UPPER_LEVEL" => (.55, .03, .05),
        _ => throw new ArgumentOutOfRangeException(nameof(faction))
    };
    internal static bool SelectMode(RunState next, TheftMode mode)
    {
        if (!Enum.IsDefined(typeof(TheftMode), mode) || next.Night is not { Ended: false, Escaped: false, Timing: null } night)
            throw new InvalidOperationException("Mode is locked during an attempt");
        if (night.Mode == mode && next.PreferredMode == mode) return false;
        next.PreferredMode = night.Mode = mode; next.Revision++; return true;
    }
    internal static readonly double[] TokenAdmission =
        { .166, .160, .140, .094, .070, .057, .050, .048, .043, .037, .036 };
    private static readonly int[] ExperienceThresholds = { 0, 30, 75, 140, 230, 350, 505, 700, 940, 1235, 1600 };
    internal static int Threshold(int level) => ExperienceThresholds[level];
    internal static int Level(int experience)
    {
        int level = 0;
        while (level < 10 && experience >= Threshold(level + 1)) level++;
        return level;
    }
    internal static int Actions(int level) => level switch { < 3 => 3, < 5 => 4, < 7 => 5, < 9 => 6, 9 => 7, _ => 9 };
    internal static int TargetReputationPenalty(int region) => region is >= 0 and <= 2
        ? region + 1 : throw new ArgumentOutOfRangeException(nameof(region));
    internal static (string Faction, int Points)[] ReputationPenalties(string? target, int targetPenalty, IEnumerable<string> available)
    {
        const string security = "FACTION_SECURITY";
        var ids = available.ToHashSet(StringComparer.Ordinal);
        if (!ids.Contains(security))
            throw new InvalidOperationException("Security reputation is required");
        // Some native identities (notably tourists) have no reputation entry.
        // Keep their faction identity without inventing a reputation or blocking return.
        var securityPenalty = (Faction: security, Points: 1);
        // The two deductions can both apply to Security.
        return target != null && ids.Contains(target)
            ? new[] { securityPenalty, (Faction: target, Points: targetPenalty) }
            : new[] { securityPenalty };
    }
    internal static int Remaining(RunState run) => run.Night is { Ended: false, Escaped: false } night
        ? Math.Max(0, Actions(Level(run.Experience)) - night.SpentActions) : 0;
    internal static double ExposureBase(int completed) => Math.Min(1,
        .10 + .05 * completed + .05 * Math.Max(0, completed - 2) + .05 * Math.Max(0, completed - 5));
    internal static double WindPenalty(int wind) => wind switch { < 20 => 0, < 40 => .05, < 70 => .10, _ => .20 };
    internal static double RareWeight(int tier, int level) => (tier switch { 1 => .10, 2 => .15, _ => .25 }) + .02 * level;
    internal static double ItemWeight(string id, long value) => value < 100 ? 1 : id switch
    {
        "scav_token" or "system_capped_neural_core" => .1,
        "skincare_cream" => .5,
        "crypto_module_cmd" or "crypto_module_eng" or "crypto_module_med" or "crypto_module_sec" => 2,
        "crypto_module_ser" or "crypto_module_sup" => 3,
        "neuroactive_perfume" or "pheromone_perfume" => 3,
        _ => value < 150 ? 8 : value < 250 ? 5 : 3
    };
    internal static void Classify(ItemState item)
    {
        if (item.Value < 0) throw new InvalidDataException("Negative true value");
        item.Rare = item.Value >= 100;
        item.Weight = ItemWeight(item.Id, item.Value);
        item.Heat = item.Value <= 50 ? 25 : item.Value <= 150 ? 50 : 75;
    }
    internal static double RareChance(IEnumerable<ItemState> items, int tier, int level, double rareFactor = 1)
    {
        bool rare = items.Any(x => x.Available && x.Rare), common = items.Any(x => x.Available && !x.Rare);
        return rare ? common ? Math.Clamp(RareWeight(tier, level) * rareFactor, 0, 1) : 1 : 0;
    }
    internal static ItemState Draw(IReadOnlyList<ItemState> items, int tier, int level, StableRandom random, double rareFactor = 1,
        ISet<string>? coreItems = null)
    {
        if (!items.Any(item => item.Available)) throw new InvalidOperationException("Target is empty");
        bool rare = random.Unit() < RareChance(items, tier, level, rareFactor);
        var pool = items.Where(x => x.Available && x.Rare == rare).ToArray();
        if (rare) return random.Weighted(pool, x => x.Weight);
        // This preference belongs to roster generation. Actual thefts omit it.
        return coreItems is { Count: > 0 } ? random.Weighted(pool, x => coreItems.Contains(x.Id) ? 2 : 1) : pool[random.Index(pool.Length)];
    }
    internal static Probabilities Preview(RunState run, NpcState npc, RoleDefinition role, DebugTuning? debug = null)
    {
        var night = run.Night ?? throw new InvalidOperationException("No current night");
        int level = Level(run.Experience);
        var factors = RegionFactors(npc.Region);
        var faction = FactionSuccess(npc.Faction);
        bool manual = night.Mode == TheftMode.Timing;
        double success = manual
            ? Math.Clamp(debug?.Success ?? (faction.Manual + .015 * level - .5 * night.WindPenalty), .05, .30)
            : debug?.Success ?? Math.Clamp(faction.Automatic + faction.Growth * level - night.WindPenalty, .10, .95);
        success *= Math.Pow(.85, npc.SuccessfulThefts);
        if (npc.Alert) success *= .5;
        if (manual) success = Math.Clamp(success, .05, .30);
        double eventPercent = night.RegionEvents.SingleOrDefault(x => x.Region == npc.Region)?.ExposurePercent ?? 0;
        double exposure = debug?.Exposure ?? Math.Clamp(ExposureBase(npc.Attempts) * Math.Pow(.9, level) * (run.Habitual ? .5 : 1) * factors.Exposure * (1 + eventPercent / 100), 0, 1);
        return new(success, exposure, RareChance(npc.Items, role.Tier, level, factors.Rare));
    }

    // Call only on a detached next revision, after every possible outcome has
    // a capacity preflight. The adapter commits native custody and this state.
    internal static ActionReceipt Attempt(RunState next, NpcState npc, RoleDefinition role, bool infinite, bool continueAfterCaught = false, int escapeItemUniqueId = 0)
    {
        var night = next.Night ?? throw new InvalidOperationException("No night");
        var timing = night.Timing;
        if (!next.Qualified || night.Ended || night.Escaped || !CanSteal(npc) ||
            night.LockedRegion is int region && region != npc.Region ||
            (timing != null ? timing.Target != npc.Key || timing.StopPosition == null || infinite != timing.Infinite
                : night.Mode == TheftMode.Timing || !infinite && Remaining(next) == 0))
            throw new InvalidOperationException("Attempt is unavailable");
        var random = new StableRandom(night.RandomState);
        bool debugContinuation = timing?.ContinueAfterCaught ?? continueAfterCaught;
        var chances = timing == null ? Preview(next, npc, role) : timing.AutomaticOutcome != null
            ? new Probabilities(timing.AutomaticSuccess, timing.AutomaticExposure, 0) : new Probabilities(timing.SuccessWidth, timing.SuccessWidth < 1 ? (timing.ExposureWidth + (1 - timing.SuccessWidth - timing.ExposureWidth) * timing.MissExposure) / (1 - timing.SuccessWidth) : 0, 0);
        var outcome = timing == null ? random.Unit() < chances.Success ? AttemptOutcome.Success
            : random.Unit() < chances.Exposure ? AttemptOutcome.Caught : AttemptOutcome.HiddenFailure
            : timing.TimedOut ? AttemptOutcome.Caught : timing.AutomaticOutcome ?? timing.Zones.At(timing.StopPosition!.Value);
        if (timing != null && timing.AutomaticOutcome == null && outcome == AttemptOutcome.HiddenFailure && random.Unit() < timing.MissExposure)
            outcome = AttemptOutcome.Caught;
        var receipt = new ActionReceipt { Revision = next.Revision + 1, Day = night.Day,
            Mode = night.Mode, StopPosition = timing?.StopPosition,
            AttemptBefore = timing?.AttemptBefore ?? night.Attempts, LevelBefore = timing?.Level ?? Level(next.Experience), Target = npc.Key,
            Outcome = outcome, Success = chances.Success, Exposure = chances.Exposure,
            Debug = infinite || npc.DebugGenerated || debugContinuation, Faction = npc.Faction };
        if (timing == null)
        {
            night.Started = true; night.LockedRegion = npc.Region; night.Attempts++; npc.Attempts++;
            if (!infinite) night.SpentActions++;
        }
        night.Timing = null;
        bool rareSuccess = false;
        if (outcome == AttemptOutcome.Success)
        {
            var item = Draw(npc.Items, role.Tier, receipt.LevelBefore, random, RegionFactors(npc.Region).Rare);
            rareSuccess = item.Rare;
            npc.Items.Remove(item); npc.SuccessfulThefts++;
            night.Loot.Add(item);
            receipt.ItemUniqueId = item.UniqueId;
            if (timing is { RandomizeSuccessZoneAfterSuccess: true, AutomaticOutcome: null })
                npc.ManualSuccessPosition = (timing.SuccessPosition + .25 + .5 * random.Unit()) % 1;
            night.PendingWind += 2;
        }
        else if (outcome == AttemptOutcome.HiddenFailure) { npc.Alert = true; night.PendingWind += 1; }
        else
        {
            // Caught/Ended mark a forced end to the outing. A debug exposure
            // remains in the receipt while leaving the live outing available.
            if (escapeItemUniqueId > 0)
            {
                receipt.EscapeItemUniqueId = escapeItemUniqueId;
                night.Escaped = true;
            }
            else
            {
                night.Caught = night.Ended = !debugContinuation;
                night.Abandoned.AddRange(night.Loot); night.Loot.Clear();
            }
            night.PendingWind += 10;
            receipt.TargetReputationPenalty = TargetReputationPenalty(npc.Region);
        }
        int experienceBefore = next.Experience;
        int change = infinite ? 0 : outcome == AttemptOutcome.Success ? (rareSuccess ? 10 : 5) : outcome == AttemptOutcome.HiddenFailure ? -2 : -5;
        next.Experience = Math.Clamp(experienceBefore + change, Threshold(Level(experienceBefore)), Threshold(10));
        receipt.ExperienceAward = next.Experience - experienceBefore;
        night.RandomState = random.State;
        next.LastAction = receipt;
        next.Revision++;
        return receipt;
    }

    internal static bool Settle(RunState next, int day)
    {
        if (next.Night?.Timing != null) throw new InvalidOperationException("Timing attempt is unresolved");
        if (next.LastSettledDay >= day) return false;
        var night = next.Night;
        if (night != null && night.Day > day) throw new InvalidOperationException("Night date moves backwards");
        next.Wind = Math.Clamp(next.Wind + (night?.Day == day && night.Attempts > 0 ? night.PendingWind : -12), 0, 100);
        next.LastSettledDay = day;
        if (night?.Day == day) { night.Ended = true; night.PendingWind = 0; }
        foreach (var note in next.Notes.Where(x => x.Day <= day)) note.Expired = true;
        next.Revision++;
        return true;
    }
}
