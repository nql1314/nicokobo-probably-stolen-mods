using MechcoreProtocol.ModuleMatrix.Definitions;

namespace MechcoreProtocol.ModuleMatrix.Domain;

internal static class ModulePriceRules
{
    // Shift the base once without discarding the item's random price premium
    // or manufacturing value. The persisted prior base prevents repeat shifts.
    internal static long Rebase(ModuleTierDefinition definition, long value, int? savedBase)
    {
        if (!definition.IsMatrix)
        {
            if (savedBase is not { } applied || definition.PreviousUnitValue is not { } original) return value;
            if (applied <= 0) throw new InvalidOperationException("Invalid saved module price basis");
            return checked(value + original - applied);
        }
        if (definition.UnitValue is not { } target) return value;
        int previous = savedBase ?? definition.PreviousUnitValue ?? target;
        if (previous <= 0) throw new InvalidOperationException("Invalid saved module price basis");
        return checked(value + target - previous);
    }
}
