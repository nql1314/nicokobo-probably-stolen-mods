namespace AugMechanicalAscension.Domain;

internal sealed record LocalizedText(string Key)
{
    internal string Chinese => AugText.GetFor(Key, false);
    internal string English => AugText.GetFor(Key, true);
    internal string Display => AugText.Get(Key);
}

// The stable scenario ID is retained. The release loader uses MODDED plus that
// ID; the old Demo's private numeric start type 55 is not written into new saves.
internal static class StartDefinition
{
    internal const string Id = RunState.AugMechanicalAscensionId;
    internal const int NativeStartTypeId = (int)NewGameData.StartType.MODDED;
    internal const bool UsesPerkPoints = false;
    internal static readonly LocalizedText Name = new("aug.start.name");
    internal static readonly LocalizedText Description = new("aug.start.description");

    internal static bool MatchesNativeStartType(int value)
        => value == NativeStartTypeId && OfficialScenario.IsCurrent;
}
