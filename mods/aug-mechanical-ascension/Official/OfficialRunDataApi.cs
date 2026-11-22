namespace AugMechanicalAscension.Official;
internal enum RunDataStatus { Disabled, Invalid, Unavailable, Missing, Present, StagedInMemory, Conflict, Indeterminate }
internal sealed record RunDataResult(RunDataStatus Status, string? Json = null, string? Reason = null);
internal static class OfficialRunDataApi
{
    public static RunDataResult Read(PlayerStore? store, string owner, string key)
    { var r = Nicokobo.ContentInterop.RunDataGate.Read(store, owner, key); return new((RunDataStatus)(int)r.Status, r.Json, r.Reason); }
    public static RunDataResult Stage(PlayerStore? store, string owner, string key, string run, int slot, string? expected, string next)
    { var r = Nicokobo.ContentInterop.RunDataGate.Stage(store, owner, key, run, slot, expected, next); return new((RunDataStatus)(int)r.Status, r.Json, r.Reason); }
}
