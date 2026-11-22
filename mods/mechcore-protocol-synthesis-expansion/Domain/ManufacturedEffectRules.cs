using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion.Domain;

internal readonly record struct ManufacturedEffectItem(string ItemId, int Units, bool PlayerOwned);
internal readonly record struct ManufacturedMachineBonuses(int Efficiency, int Performance, int Quality);

internal readonly record struct ManufacturedEffects(bool AiCore, bool SleepCompression,
    bool KineticRecovery, bool TitaniumExoskeleton, bool FineMotorAssistant, bool CyberEye)
{
    internal static bool IsRelevant(string id) => id == SynthesisDefinitions.AiCore.Id ||
        id == SynthesisDefinitions.SleepCompressionModule.Id || id == SynthesisDefinitions.KineticRecovery.Id ||
        id == SynthesisDefinitions.TitaniumExoskeleton.Id || id == SynthesisDefinitions.FineMotorAssistant.Id ||
        id == SynthesisDefinitions.CyberEye.Id;

    internal static ManufacturedEffects FromItems(IEnumerable<ManufacturedEffectItem> items)
    {
        bool ai = false, sleep = false, kinetic = false, exoskeleton = false, fineMotor = false, eye = false;
        foreach (var item in items)
        {
            if (!item.PlayerOwned || item.Units <= 0) continue;
            string id = item.ItemId;
            if (id == SynthesisDefinitions.AiCore.Id) ai = true;
            else if (id == SynthesisDefinitions.SleepCompressionModule.Id) sleep = true;
            else if (id == SynthesisDefinitions.KineticRecovery.Id) kinetic = true;
            else if (id == SynthesisDefinitions.TitaniumExoskeleton.Id) exoskeleton = true;
            else if (id == SynthesisDefinitions.FineMotorAssistant.Id) fineMotor = true;
            else if (id == SynthesisDefinitions.CyberEye.Id) eye = true;
            if (ai && sleep && kinetic && exoskeleton && fineMotor && eye) break;
        }
        return new(ai, sleep, kinetic, exoskeleton, fineMotor, eye);
    }

    internal ManufacturedMachineBonuses MachineBonuses => new(
        KineticRecovery ? SynthesisNumbers.Effects.KineticEfficiencyBonusPercent : 0,
        TitaniumExoskeleton ? SynthesisNumbers.Effects.ExoskeletonPerformanceBonusPercent : 0,
        FineMotorAssistant ? SynthesisNumbers.Effects.FineMotorQualityBonusPercent : 0);

    internal int NightActionBonus => SleepCompression ? SynthesisNumbers.Effects.SleepExtraNightActions : 0;
    internal int SaleBonus(bool augCustomer) => !augCustomer && AiCore ? SynthesisNumbers.Effects.AiCoreSaleBonusPercent : 0;
}
