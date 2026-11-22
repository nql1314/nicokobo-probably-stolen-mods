

namespace ProbablyStolen.ModSupport;

// Content factories retain their verified native template's purpose categories.
// This explicitly normalizes every item declaration, then adds content-owned types.
internal static class ModItemTypeRules
{
    internal const string Cybernetic = "CYBERNETIC";
    internal static void ApplyTemplateTypes(GameItem item, bool cybernetic = false,
        string? requiredNativeType = null)
    {
        var types = item.GetGameItemType()?.ToArray() ?? Array.Empty<string>();
        if (requiredNativeType != null && !types.Contains(requiredNativeType, StringComparer.Ordinal))
            throw new InvalidOperationException("Native template category missing: " + requiredNativeType);
        var assigned = types.Where(type => !string.IsNullOrWhiteSpace(type))
            .Concat(cybernetic ? new[] { Cybernetic } : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal).ToArray();
        item.RemoveAllGameItemType();
        foreach (var type in assigned) item.SetGameItemType(type);
        foreach (var type in assigned)
            if (!item.IsGameItemType(type)) throw new InvalidOperationException("Item type readback failed: " + type);
    }
}
