using MechcoreProtocol.ModuleMatrix.Definitions;

namespace MechcoreProtocol.ModuleMatrix.Domain;

internal readonly record struct FixedModuleValidation(bool Valid, string Reason);

internal static class FixedModuleRules
{
    internal static FixedModuleValidation Validate(FixedModuleDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Code) ||
            string.IsNullOrWhiteSpace(definition.Id) ||
            string.IsNullOrWhiteSpace(definition.ChineseName) ||
            string.IsNullOrWhiteSpace(definition.EnglishName))
            return new(false, "identity or bilingual name is missing");
        if (!definition.Id.StartsWith(FixedModuleDefinitions.OwnerId + ".",
                StringComparison.Ordinal))
            return new(false, "ID is outside the owner namespace");
        if (definition.UnitValue < 0)
            return new(false, "unit value is negative");
        if (definition.Width <= 0 || definition.Height <= 0 ||
            definition.ShapeData.Length != definition.Width * definition.Height)
            return new(false, "shape dimensions do not match shape data");
        if (definition.ShapeData.Any(cell => cell is not ('0' or '1')))
            return new(false, "shape contains a value other than 0 or 1");
        if (definition.OccupiedCells == 0)
            return new(false, "shape has no occupied cells");
        return new(true, "ready");
    }
}
