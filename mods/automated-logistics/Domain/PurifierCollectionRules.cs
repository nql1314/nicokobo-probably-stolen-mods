namespace Nicokobo.AutomatedLogistics.Domain;

internal enum PurifierCollectionState { Ready, Purifying, Unreachable, Unremovable }

internal static class PurifierCollectionRules
{
    internal static PurifierCollectionState Evaluate(int purity, int machineLimit, bool hasRemovableContaminants,
        PurifierCollectionTarget target)
    {
        int goal = target == PurifierCollectionTarget.PureWater ? 9990 : machineLimit;
        if (purity >= goal) return PurifierCollectionState.Ready;
        if (!hasRemovableContaminants) return PurifierCollectionState.Unremovable;
        if (goal > machineLimit) return PurifierCollectionState.Unreachable;
        return PurifierCollectionState.Purifying;
    }
}
