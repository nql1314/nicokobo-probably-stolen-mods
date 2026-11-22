namespace AugMechanicalAscension.Domain;

internal static class IdentityDisplayRules
{
    internal static string AugTag => AugText.GetFor("aug.identity.aug", false);

    internal static string AppendAugTag(string? displayName, bool actualAug, bool? english = null)
    {
        string value = displayName ?? "";
        if (!actualAug) return value;
        string tag = AugText.GetFor("aug.identity.aug", english);
        if (value.Contains(tag, StringComparison.Ordinal))
            return value;
        return string.IsNullOrWhiteSpace(value) ? tag : $"{value} {tag}";
    }
}
