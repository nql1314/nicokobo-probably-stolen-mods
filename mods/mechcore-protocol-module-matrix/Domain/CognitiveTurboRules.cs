namespace MechcoreProtocol.ModuleMatrix.Domain;

internal static class CognitiveTurboRules
{
    internal static int Cap(int original, bool owned) => owned && original > 0
        ? (int)Math.Min(int.MaxValue, (long)original * 3 / 2) : original;
}
