using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
namespace AugMechanicalAscension.Integration;

internal static class RunForkIntegration
{
    private static readonly List<IDisposable> Leases = [];
    internal static void Install(NativeSessionBridge bridge)
    {
        try
        {
            Add(RunState.SaveKey, RunForkMigration.Run, bridge.RebindAfterFork);
            Add(StoryState.SaveKey, RunForkMigration.Story);
            Add(GraylineSupplyState.SaveKey, RunForkMigration.Supply);
            Add(WorkshopAchievements.SaveKey, RunForkMigration.Workshop);
        }
        catch { Uninstall(); throw; }
    }
    private static void Add(string key, Func<string, string, int, string, int, string> rebind,
        Action<PlayerStore>? rebound = null) => Leases.Add(OfficialRunForkApi.RegisterState("nicokobo.aug", key,
            (json, source, destination) => rebind(json, source.RunId, source.SlotId, destination.RunId, destination.SlotId), rebound,
            _ => !StoryRuntimeFlow.ForkBusy && !NicoWorkshop.ForkBusy));
    internal static void Uninstall() { foreach (var lease in Leases) lease.Dispose(); Leases.Clear(); }
}
