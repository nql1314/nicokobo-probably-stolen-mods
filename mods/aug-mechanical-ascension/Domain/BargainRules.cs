namespace AugMechanicalAscension.Domain;

internal static class BargainRules
{
    // Mirrors the native fixed retail-markup stage: Aug customers receive the
    // perk's positive markup, while non-Aug customers apply the current
    // exposure tier as a fixed markdown. The voice changer offsets that
    // non-Aug modifier only. Values are whole percentage points.
    internal static int CalculateMarketModifier(bool actualAug, bool aiAssistantEnabled,
        int exposure, ConfigSnapshot config, int exposureThresholdShift = 0)
        => checked(CalculatePerkModifier(actualAug, exposure, config,
                       exposureThresholdShift) +
                   CalculateAiAssistantModifier(actualAug, aiAssistantEnabled, config));

    internal static int CalculatePerkModifier(bool actualAug, int exposure,
        ConfigSnapshot config, int exposureThresholdShift = 0)
    {
        if (exposure < 0 || exposure > config.MaxExposure)
            throw new ArgumentOutOfRangeException(nameof(exposure));
        if (actualAug)
            return (int)decimal.Round(config.SameKindBonusBp / 100m, 0,
                MidpointRounding.AwayFromZero);
        if (exposure < exposureThresholdShift) return 0;
        // Pricing balance applies to existing runs too; their saved tier fields
        // remain readable but do not override the current compiled rules.
        int modifierBp = exposure >= AugNumbers.OpeningAndSurvival.BargainThreshold2 + exposureThresholdShift
            ? -AugNumbers.OpeningAndSurvival.BargainPenalty2Bp
            : exposure >= AugNumbers.OpeningAndSurvival.BargainThreshold1 + exposureThresholdShift
                ? -AugNumbers.OpeningAndSurvival.BargainPenalty1Bp
                : -AugNumbers.OpeningAndSurvival.BargainBasePenaltyBp;
        return (int)decimal.Round(modifierBp / 100m, 0,
            MidpointRounding.AwayFromZero);
    }

    internal static int CalculateAiAssistantModifier(bool actualAug, bool enabled,
        ConfigSnapshot config)
        => !actualAug && enabled
            ? (int)decimal.Round(config.VoiceBonusBp / 100m, 0,
                MidpointRounding.AwayFromZero)
            : 0;
}
