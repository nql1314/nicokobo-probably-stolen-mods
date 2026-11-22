using System.Globalization;

namespace AugMechanicalAscension.Domain;

internal sealed record SupplyOwnedStack(long NativeId, int Units);

// The caller must supply snapshots from the same verified owned root, with only the
// exact purchased item ID included. This compares a synchronous native transfer;
// it does not establish save/reload durability or globally stable item IDs.
internal static class SupplyDeliveryProofRules
{
    internal static string? Evidence(string requestId,
        IReadOnlyDictionary<IntPtr, SupplyOwnedStack> before,
        IReadOnlyDictionary<IntPtr, SupplyOwnedStack> after)
    {
        if (!Guid.TryParse(requestId, out var parsed) || parsed == Guid.Empty ||
            parsed.ToString("D") != requestId || before == null || after == null ||
            !Valid(before) || !Valid(after))
            return null;
        long changedId = 0;
        int changes = 0;
        foreach (var (pointer, stack) in after)
        {
            if (before.TryGetValue(pointer, out var old))
            {
                if (stack == old) continue;
                if (stack.NativeId != old.NativeId ||
                    stack.Units != old.Units + 1) return null;
            }
            else if (stack.Units != 1) return null;
            changedId = stack.NativeId;
            changes++;
        }
        if (changes != 1 || before.Keys.Except(after.Keys).Any()) return null;
        string evidence = $"supply:{requestId}:{changedId.ToString(CultureInfo.InvariantCulture)}";
        return evidence.Length <= AugNumbers.Validation.MaxIdentifierChars ? evidence : null;
    }

    internal static bool IsEvidenceForRequest(string? requestId, string? evidence)
    {
        if (!Guid.TryParse(requestId, out var parsed) || parsed == Guid.Empty ||
            parsed.ToString("D") != requestId || string.IsNullOrEmpty(evidence) ||
            evidence.Length > AugNumbers.Validation.MaxIdentifierChars)
            return false;
        string prefix = $"supply:{requestId}:";
        if (!evidence.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var nativeIdText = evidence.AsSpan(prefix.Length);
        return long.TryParse(nativeIdText, NumberStyles.None,
                   CultureInfo.InvariantCulture, out long nativeId) &&
               nativeId > 0 &&
               nativeId.ToString(CultureInfo.InvariantCulture).AsSpan()
                   .SequenceEqual(nativeIdText);
    }

    private static bool Valid(IReadOnlyDictionary<IntPtr, SupplyOwnedStack> stacks)
    {
        var ids = new HashSet<long>();
        foreach (var (pointer, stack) in stacks)
            if (pointer == IntPtr.Zero || stack == null || stack.NativeId <= 0 ||
                stack.Units < 1 || !ids.Add(stack.NativeId))
                return false;
        return true;
    }
}
