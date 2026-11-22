using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion.Domain;

public static class MachineProcessingRules
{
    public static int NightlyBatchCount(MachineDefinition machine, int performance) =>
        machine.ItemId == SynthesisDefinitions.UniversalManufacturerItem.Id ? 1 :
            1 + Math.Max(0, performance) / SynthesisNumbers.Machines.PerformancePerExtraBatch;
}
