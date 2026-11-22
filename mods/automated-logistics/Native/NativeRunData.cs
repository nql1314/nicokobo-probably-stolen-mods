using Nicokobo.ContentInterop;
using Nicokobo.AutomatedLogistics.Integration;
namespace Nicokobo.AutomatedLogistics.Native;
internal enum RunDataStatus { Disabled, Invalid, Unavailable, Missing, Present, StagedInMemory, Conflict, Indeterminate }
internal sealed record RunDataResult(RunDataStatus Status, string? Json = null, string? Reason = null);
internal static class NativeRunDataApi
{
    internal const string SaveKey = Content.Owner + ".runtime_v6";
    internal static RunDataResult Read(PlayerStore? store, string owner, string key)
    { var r = RunDataGate.Read(store, owner, key); return new((RunDataStatus)(int)r.Status, r.Json, r.Reason); }
    internal static RunDataResult Stage(PlayerStore? store, string owner, string key, string run, int slot, string? expected, string next)
    { var r = RunDataGate.Stage(store, owner, key, run, slot, expected, next); return new((RunDataStatus)(int)r.Status, r.Json, r.Reason); }
}
