using HarmonyLib;
using Nicokobo.AutomatedLogistics.Integration;

namespace Nicokobo.AutomatedLogistics.Native;

// InitialSave is also called when the player branches from a historical day.
// Observe that call before the game replaces its run/slot, without changing it.
[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.InitialSave))]
internal static class NativeRunForkApi
{
    [ThreadStatic] private static PlayerStore? _forking;
    internal static bool IsForking(PlayerStore store) => ReferenceEquals(_forking, store);
    [HarmonyPriority(Priority.First)]
    private static void Prefix(PlayerStore __instance)
    {
        if (string.IsNullOrEmpty(__instance.runID) || __instance.saveSlotId < 0) return;
        _forking = __instance;
        const string key = NativeRunDataApi.SaveKey;
        var state = __instance.modData.TryGetValue(key, out string json)
            ? Newtonsoft.Json.JsonConvert.DeserializeObject<Domain.AutomatedLogisticsState>(json)
            : new Domain.AutomatedLogisticsState();
        if (state?.IsValid() != true) throw new InvalidDataException("Cannot branch an invalid logistics state");
        // A historical continuation never creates a fresh opening reward.
        state.TerminalGranted = true;
        __instance.modData[key] = Newtonsoft.Json.JsonConvert.SerializeObject(state);
    }
    private static Exception? Finalizer(Exception? __exception) { _forking = null; return __exception; }
}

internal enum NativeLifecyclePhase { AfterGameLoadedLate, BeforeNight, AfterNight }
internal sealed record NativeLifecycleContext(PlayerStore Store, NativeRunDay Run);
internal sealed record NativeRunDay(int Day);
internal static class NativeLifecycleApi
{
    private sealed record Observer(NativeLifecyclePhase Phase, int Order, Action<NativeLifecycleContext> Callback);
    private static readonly List<Observer> Observers = new();
    internal static IDisposable Subscribe(string owner, string key, NativeLifecyclePhase phase, Action<NativeLifecycleContext> callback, int order = 0)
    {
        if (Observers.Count == 0)
        { ModHook.OnGameLoadedLate += Loaded; ModHook.OnGoingSleepEarly += BeforeNight; ModHook.OnGoingSleepLate += AfterNight; }
        var observer = new Observer(phase, order, callback); Observers.Add(observer);
        return new CallbackLease(() => { Observers.Remove(observer); if (Observers.Count != 0) return;
            ModHook.OnGameLoadedLate -= Loaded; ModHook.OnGoingSleepEarly -= BeforeNight; ModHook.OnGoingSleepLate -= AfterNight; });
    }
    private static void Loaded() { Dispatch(NativeLifecyclePhase.AfterGameLoadedLate); }
    private static void BeforeNight() { Dispatch(NativeLifecyclePhase.BeforeNight); }
    private static void AfterNight() => Dispatch(NativeLifecyclePhase.AfterNight);
    private static void Dispatch(NativeLifecyclePhase phase)
    {
        if (PlayerStore.Instance is not { } store) return;
        var context = new NativeLifecycleContext(store, new(StoreStation.GetDayCounter()));
        foreach (var observer in Observers.Where(observer => observer.Phase == phase).OrderBy(observer => observer.Order).ToArray()) observer.Callback(context);
    }
}
