namespace AugMechanicalAscension.Domain;

internal enum SettleStatus { Applied, AlreadySettled, DayGap, RunNotReady, RunEnded, OperationPending }

internal sealed record SettleResult(SettleStatus Status, RunState State);

internal static class SurvivalRules
{
    internal static (int HealthLoss, int ExposureGain) DailyChanges(ConfigSnapshot config,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false)
        => mechanicalAscension ? (0, 0) : (
            effects.ArtificialHeart
                ? (int)Math.Ceiling(config.DailyHealthLoss * config.CyberneticPrices.ArtificialHeartHealthLossMultiplierBp / 10_000m)
                : config.DailyHealthLoss,
            effects.MimeticSkin
                ? Math.Max(0, config.DailyExposureGain - config.CyberneticPrices.SkinDailyExposureReduction)
                : config.DailyExposureGain);

    internal static SettleResult ReconcileDay(RunState state, int nextDay,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false)
    {
        if (nextDay != state.LastObservedDay + 1)
            return new(SettleStatus.DayGap, state);
        var settled = SettleDay(state, state.LastObservedDay, effects, mechanicalAscension);
        return settled.Status == SettleStatus.Applied
            ? settled with
            { State = settled.State with { LastObservedDay = nextDay } }
            : settled;
    }

    internal static SettleResult SettleDay(RunState state, int completedDay,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false)
    {
        if (state.InitPhase != "complete") return new(SettleStatus.RunNotReady, state);
        if (state.FailureReason != "none") return new(SettleStatus.RunEnded, state);
        if (state.HasPendingExternalOperation)
            return new(SettleStatus.OperationPending, state);
        if (completedDay <= state.LastSettledDay) return new(SettleStatus.AlreadySettled, state);
        if (completedDay != state.LastSettledDay + 1 || completedDay != state.LastObservedDay)
            return new(SettleStatus.DayGap, state);

        if (mechanicalAscension)
            return new(SettleStatus.Applied, state with
            {
                Revision = checked(state.Revision + 1),
                LastSettledDay = completedDay
            });

        var config = state.ConfigSnapshot;
        var (healthLoss, naturalExposure) = DailyChanges(config, effects);
        int health = Math.Max(0, state.Health - healthLoss);
        int exposure = Math.Min(config.MaxExposure, state.Exposure + naturalExposure);
        int? deadline = exposure == config.MaxExposure ? completedDay : null;
        string failure = health == 0 ? "bodyCollapse"
            : exposure == config.MaxExposure ? "exposure" : "none";
        var updated = state with
        {
            Revision = checked(state.Revision + 1),
            LastSettledDay = completedDay,
            Health = health,
            Exposure = exposure,
            ExposureDeadlineDay = deadline,
            FailureReason = failure
        };
        return new(SettleStatus.Applied, updated);
    }
}
