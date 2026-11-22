using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion.Domain;

public static class MiningDrillRules
{
    public const string UpgradedTag = "NICOKOBO_MINING_DRILL_UPGRADED_V1";

    public static int ChancePercent(bool upgraded) => upgraded
        ? SynthesisNumbers.MiningDrill.UpgradedOreChancePercent
        : SynthesisNumbers.MiningDrill.ExtraOreChancePercent;

    public static bool FindsOre(int roll, bool upgraded = false) =>
        roll >= 0 && roll < ChancePercent(upgraded);

    public static bool CanUpgrade(bool upgraded, IngotPurityGrade titaniumPurity) =>
        !upgraded && titaniumPurity == IngotPurityGrade.Perfect;

    public static string SelectOre(int roll) => roll switch
    {
        < 0 or >= 100 => throw new ArgumentOutOfRangeException(nameof(roll)),
        < SynthesisNumbers.MiningDrill.MetalOreWeight => "common_ore",
        < SynthesisNumbers.MiningDrill.MetalOreWeight + SynthesisNumbers.MiningDrill.TitaniumOreWeight =>
            SynthesisDefinitions.TitaniumOre.Id,
        _ => SynthesisDefinitions.Quartz.Id
    };
}
