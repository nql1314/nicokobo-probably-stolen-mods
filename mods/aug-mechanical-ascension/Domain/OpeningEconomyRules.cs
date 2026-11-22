namespace AugMechanicalAscension.Domain;

internal sealed record OpeningEconomySnapshot(int Credits, int Rent, int SecurityReputation,
    bool NativeReadVerified);

internal enum OpeningEconomyStatus { Ready, Unverified, InvalidConfig, InvalidSnapshot, DeltaOverflow }

internal sealed record OpeningEconomyPlan(OpeningEconomyStatus Status,
    int CashDelta = 0, int RentTarget = 0, int SecurityReputationDelta = 0,
    int SecurityReputationTarget = 0, int CashTarget = 0);

// Cash replaces the base allotment while preserving perk changes. Rent remains
// a fixed target and reputation is an adjustment to the native value.
internal static class OpeningEconomyRules
{
    internal static OpeningEconomyPlan Plan(ConfigSnapshot config, OpeningEconomySnapshot native,
        int? plannedCashTarget = null)
    {
        if (config.InitialCredits < 0 || config.InitialRent is < 0 or > AugNumbers.Validation.MaxRent ||
            config.InitialSecurityReputation is < -AugNumbers.Validation.MaxSecurityReputationMagnitude or > AugNumbers.Validation.MaxSecurityReputationMagnitude)
            return new(OpeningEconomyStatus.InvalidConfig);
        if (!native.NativeReadVerified) return new(OpeningEconomyStatus.Unverified);
        if (native.Rent is < 0 or > AugNumbers.Validation.MaxRent ||
            native.SecurityReputation is < -AugNumbers.Validation.MaxSecurityReputationMagnitude or > AugNumbers.Validation.MaxSecurityReputationMagnitude)
            return new(OpeningEconomyStatus.InvalidSnapshot);
        long cashTarget = plannedCashTarget ?? (long)native.Credits + config.InitialCredits -
            AugNumbers.OpeningAndSurvival.NativeStartingCredits;
        long cashDelta = cashTarget - native.Credits;
        long securityTarget = (long)native.SecurityReputation + config.InitialSecurityReputation;
        if (cashTarget is < int.MinValue or > int.MaxValue ||
            cashDelta is < int.MinValue or > int.MaxValue ||
            securityTarget is < -AugNumbers.Validation.MaxSecurityReputationMagnitude or > AugNumbers.Validation.MaxSecurityReputationMagnitude)
            return new(OpeningEconomyStatus.DeltaOverflow);
        return new(OpeningEconomyStatus.Ready, (int)cashDelta,
            config.InitialRent, config.InitialSecurityReputation, (int)securityTarget, (int)cashTarget);
    }

    internal static bool Matches(ConfigSnapshot config, OpeningEconomySnapshot observed,
        int? securityReputationTarget = null, int? cashTarget = null)
        => observed.NativeReadVerified && observed.Credits == (cashTarget ?? config.InitialCredits) &&
           observed.Rent == config.InitialRent &&
           observed.SecurityReputation == (securityReputationTarget ?? config.InitialSecurityReputation) &&
           observed.SecurityReputation is >= -AugNumbers.Validation.MaxSecurityReputationMagnitude and <= AugNumbers.Validation.MaxSecurityReputationMagnitude;

    // Older saved observations have no target and retain their fixed value.
    internal static bool Matches(ConfigSnapshot config, OpeningEconomyApplyResult result)
        => result.After is { } after &&
           (result.CashTarget == null || result.Before is { } before &&
               result.CashTarget == (long)before.Credits + config.InitialCredits -
                   AugNumbers.OpeningAndSurvival.NativeStartingCredits) &&
           Matches(config, after, result.SecurityReputationTarget, result.CashTarget);
}
