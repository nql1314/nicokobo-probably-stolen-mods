namespace AugMechanicalAscension.Domain;

internal static class ExposureVisitRules
{
    internal const string Security = "FACTION_SECURITY";
    internal const string Upper = "FACTION_UPPER_LEVEL";

    internal static int Gain(string? factionId) => factionId switch
    {
        Security => AugNumbers.ExposureVisits.SecurityExposureGain,
        _ => 0
    };

    // Called once when a Security visit actually begins, after that visit's
    // exposure gain has been applied. A successful roll queues an inspection.
    internal static int InspectionChancePercent(int exposure)
        => exposure >= AugNumbers.ExposureVisits.InspectionMinimumExposure &&
            exposure < AugNumbers.ExposureVisits.InspectionMaximumExposureExclusive
                ? exposure - AugNumbers.ExposureVisits.InspectionChanceOffset : 0;

    internal static RunState? ApplyVisit(RunState state, string? factionId,
        bool mechanicalAscension = false)
        => ApplyGain(state, Gain(factionId), mechanicalAscension);

    internal static RunState? ApplyGain(RunState state, int gain,
        bool mechanicalAscension = false)
    {
        if (gain <= 0 || state.InitPhase != "complete" ||
            state.FailureReason != "none" || state.HasPendingExternalOperation) return null;
        if (mechanicalAscension)
            return state with { Revision = checked(state.Revision + 1) };
        int exposure = Math.Min(state.ConfigSnapshot.MaxExposure,
            checked(state.Exposure + gain));
        bool failed = exposure == state.ConfigSnapshot.MaxExposure;
        return state with
        {
            Revision = checked(state.Revision + 1),
            Exposure = exposure,
            ExposureDeadlineDay = failed ? state.LastObservedDay : null,
            FailureReason = failed ? "exposure" : "none"
        };
    }
}
