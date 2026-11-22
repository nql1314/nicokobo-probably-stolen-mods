namespace DefinitelyStolen.Domain;

// Both modes share outcome partitioning; manual success uses its narrower difficulty curve.
internal readonly record struct TimingZones(double Success, double Exposure, double SuccessPosition = .5)
{
    internal double Failure => 1 - Success - Exposure;
    internal double LeftFailure => Failure * SuccessPosition;
    internal double RightFailure => Failure - LeftFailure;
    // Retain exact centered boundaries for attempts saved before relocation existed.
    internal double SuccessStart => SuccessPosition == .5 ? (1 - Success) / 2 : Exposure / 2 + LeftFailure;
    internal double SuccessEnd => SuccessPosition == .5 ? (1 + Success) / 2 : SuccessStart + Success;
    internal AttemptOutcome At(double position)
    {
        if (!Numbers.Finite(position) || position is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(position));
        if (Success > 0 && position >= SuccessStart && position <= SuccessEnd) return AttemptOutcome.Success;
        return Exposure > 0 && (position <= Exposure / 2 || position >= 1 - Exposure / 2) ? AttemptOutcome.Caught : AttemptOutcome.HiddenFailure;
    }
}

internal sealed class TimingAttempt
{
    public string Target { get; set; } = "";
    public long StartedUtcTicks { get; set; }
    public double SuccessWidth { get; set; }
    public double ExposureWidth { get; set; }
    public double SuccessPosition { get; set; } = .5;
    public bool RandomizeSuccessZoneAfterSuccess { get; set; }
    internal TimingZones Zones => new(SuccessWidth, ExposureWidth, SuccessPosition);
    public int Level { get; set; }
    public int AttemptBefore { get; set; }
    public bool Infinite { get; set; }
    public bool ContinueAfterCaught { get; set; }
    public double? StopPosition { get; set; }
    public bool TimedOut { get; set; }
    // Kept for an already-saved legacy attempt; new attempts never reroll the miss zone.
    public double MissExposure { get; set; }
    public AttemptOutcome? AutomaticOutcome { get; set; }
    public double? AutomaticPosition { get; set; }
    public double AutomaticSuccess { get; set; }
    public double AutomaticExposure { get; set; }
}

internal static class TimingRules
{
    internal const double Duration = 6;
    internal const double TraverseSeconds = 1;
    internal const double AutomaticDuration = 1;
    internal static TimingAttempt Practice(RunState run, NpcState npc, RoleDefinition role, long now, DebugTuning? debug = null, double manualDifficultyCoefficient = 1, bool randomizeSuccessZoneAfterSuccess = true)
    {
        if (!run.Qualified || run.ManualPracticeUsed || run.Night is not { Mode: TheftMode.Timing, Timing: null, Ended: false, Escaped: false })
            throw new InvalidOperationException("Practice is unavailable");
        var zones = Preview(run, npc, role, debug, manualDifficultyCoefficient, randomizeSuccessZoneAfterSuccess);
        return new() { Target = npc.Key, StartedUtcTicks = now, SuccessWidth = zones.Success, ExposureWidth = zones.Exposure, SuccessPosition = zones.SuccessPosition };
    }
    internal static TimingZones Preview(RunState run, NpcState npc, RoleDefinition role, DebugTuning? debug = null, double manualDifficultyCoefficient = 1, bool randomizeSuccessZoneAfterSuccess = true)
    {
        var chances = TheftRules.Preview(run, npc, role, debug);
        if (run.Night!.Mode != TheftMode.Timing) return new(chances.Success, chances.Caught);
        double coefficient = Numbers.Finite(manualDifficultyCoefficient) ? Math.Clamp(manualDifficultyCoefficient, 0, 2) : 1;
        // Scale the final baseline, including its clamps and target penalties.
        double success = chances.Success * coefficient;
        return new(success, (1 - success) * chances.Exposure, randomizeSuccessZoneAfterSuccess ? npc.ManualSuccessPosition : .5);
    }
    internal static double Elapsed(TimingAttempt attempt, long now) => Math.Max(0, (now - attempt.StartedUtcTicks) / (double)TimeSpan.TicksPerSecond);
    internal static double Position(double elapsed)
    { double phase = elapsed / TraverseSeconds % 2; return phase <= 1 ? phase : 2 - phase; }
    internal static double Position(TimingAttempt attempt, double elapsed)
    {
        if (attempt.AutomaticPosition is not double target) return Position(elapsed);
        if (elapsed >= AutomaticDuration) return target;
        return Position(Math.Max(0, elapsed) / AutomaticDuration * (2 + target) * TraverseSeconds);
    }
    internal static void Begin(RunState next, NpcState npc, RoleDefinition role, bool infinite, long now, DebugTuning? debug = null, double manualDifficultyCoefficient = 1, bool randomizeSuccessZoneAfterSuccess = true)
    {
        var night = next.Night ?? throw new InvalidOperationException("No night");
        if (!next.Qualified || night.Ended || night.Escaped || night.Timing != null || !TheftRules.CanSteal(npc) ||
            night.LockedRegion is int region && region != npc.Region || !infinite && TheftRules.Remaining(next) == 0)
            throw new InvalidOperationException("Attempt is unavailable");
        var zones = Preview(next, npc, role, debug, manualDifficultyCoefficient, randomizeSuccessZoneAfterSuccess);
        night.Timing = new() { Target = npc.Key, StartedUtcTicks = now, SuccessWidth = zones.Success, ExposureWidth = zones.Exposure,
            SuccessPosition = zones.SuccessPosition, RandomizeSuccessZoneAfterSuccess = night.Mode == TheftMode.Timing && randomizeSuccessZoneAfterSuccess,
            Level = TheftRules.Level(next.Experience), AttemptBefore = night.Attempts, Infinite = infinite,
            ContinueAfterCaught = debug != null, MissExposure = 0 };
        if (night.Mode == TheftMode.Probability)
        {
            var chances = TheftRules.Preview(next, npc, role, debug);
            var random = new StableRandom(night.RandomState);
            var outcome = random.Unit() < chances.Success ? AttemptOutcome.Success
                : random.Unit() < chances.Exposure ? AttemptOutcome.Caught : AttemptOutcome.HiddenFailure;
            // Pick an interior point, then travel one full round trip before stopping.
            double start = outcome == AttemptOutcome.Success ? zones.SuccessStart : outcome == AttemptOutcome.Caught ? 0 : zones.Exposure / 2;
            double width = outcome == AttemptOutcome.Success ? zones.Success : outcome == AttemptOutcome.Caught ? zones.Exposure / 2 : zones.Failure / 2;
            double position = start + width * (.2 + .6 * random.Unit());
            if (outcome != AttemptOutcome.Success && random.Unit() < .5) position = 1 - position;
            night.Timing.AutomaticOutcome = outcome; night.Timing.AutomaticPosition = position;
            night.Timing.AutomaticSuccess = chances.Success; night.Timing.AutomaticExposure = chances.Exposure;
            night.RandomState = random.State;
        }
        night.Started = true; night.LockedRegion = npc.Region; night.Attempts++; npc.Attempts++;
        if (!infinite) night.SpentActions++;
        next.Revision++;
    }
    internal static void Stop(RunState next, double position, long now)
    {
        var attempt = next.Night?.Timing ?? throw new InvalidOperationException("No running attempt");
        if (attempt.StopPosition != null) throw new InvalidOperationException("Already stopped");
        attempt.Zones.At(position);
        if (attempt.AutomaticPosition is double automatic)
        {
            if (Elapsed(attempt, now) < AutomaticDuration) throw new InvalidOperationException("Automatic animation is running");
            attempt.StopPosition = automatic;
        }
        else
        {
            attempt.TimedOut = Elapsed(attempt, now) >= Duration;
            attempt.StopPosition = attempt.TimedOut ? 0 : position;
        }
        next.Revision++;
    }
}
