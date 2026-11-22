namespace DefinitelyStolen.Domain;

// Session-only overrides; attempts snapshot effective values before saving.
internal sealed class DebugTuning
{
    internal double? Success, Exposure;
}
