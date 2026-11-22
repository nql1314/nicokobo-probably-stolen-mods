using HarmonyLib;

namespace AugMechanicalAscension.Official;
internal enum OfficialLifecyclePhase { BeforeLoad,AfterLoad,BeforeNight,AfterNight,BeforeNightServices,AfterNightServices,AfterSleep,AfterEndDay,NightAttemptFinished,AfterGameLoadedLate }
internal sealed record OfficialRunIdentity(string RunId,int SlotId,int Day);
internal sealed class OfficialLifecycleContext(PlayerStore store)
{
    internal PlayerStore Store => store;
    internal OfficialRunIdentity Run => new(store.runID,store.saveSlotId,StoreStation.GetDayCounter());
    internal IReadOnlyList<GameItem> Items => OfficialInventoryApi.CaptureRunItems(store);
}
internal static class OfficialLifecycleApi
{
    private sealed record Listener(string Owner,string Id,OfficialLifecyclePhase Phase,Action<OfficialLifecycleContext> Callback,int Order);
    private static readonly List<Listener> Listeners=new();
    private static readonly Harmony Hooks=new("nicokobo.aug.lifecycle");
    private static bool _attached;
    internal static IDisposable Subscribe(string owner,string id,OfficialLifecyclePhase phase,Action<OfficialLifecycleContext> callback,int order=0)
    {
        if (!_attached) Attach();
        var listener=new Listener(owner,id,phase,callback,order); Listeners.Add(listener);
        return new CallbackLease(() => Listeners.Remove(listener));
    }
    private static void Attach()
    {
        try
        {
            ModHook.OnGameLoadedInit += BeforeLoad;
            ModHook.OnGameLoadedLate += AfterLoad;
            ModHook.OnGoingSleepLate += AfterSleep;
            ModHook.OnHandlingNightlyServicesEarly += BeforeServices;
            ModHook.OnHandlingNightlyServicesLate += AfterServices;
            Hooks.Patch(AccessTools.DeclaredMethod(typeof(PlayerStore),nameof(PlayerStore.EndNight)),prefix:new HarmonyMethod(typeof(OfficialLifecycleApi),nameof(NightPrefix)),postfix:new HarmonyMethod(typeof(OfficialLifecycleApi),nameof(NightPostfix)));
            Hooks.Patch(AccessTools.DeclaredMethod(typeof(PlayerStore),nameof(PlayerStore.EndDay)),postfix:new HarmonyMethod(typeof(OfficialLifecycleApi),nameof(DayPostfix)));
            _attached=true;
        }
        catch
        {
            Uninstall();
            throw;
        }
    }
    private static void Dispatch(OfficialLifecyclePhase phase)
    {
        if (!PlayerStore.HasInstance) return;
        foreach(var listener in Listeners.Where(l=>l.Phase==phase).OrderBy(l=>l.Order).ThenBy(l=>l.Id,StringComparer.Ordinal).ToArray())
            listener.Callback(new(PlayerStore.Instance));
    }
    private static void BeforeLoad()=>Dispatch(OfficialLifecyclePhase.BeforeLoad);
    private static void AfterLoad(){Dispatch(OfficialLifecyclePhase.AfterLoad);Dispatch(OfficialLifecyclePhase.AfterGameLoadedLate);}
    private static void AfterSleep()=>Dispatch(OfficialLifecyclePhase.AfterSleep);
    private static void BeforeServices()=>Dispatch(OfficialLifecyclePhase.BeforeNightServices);
    private static void AfterServices()=>Dispatch(OfficialLifecyclePhase.AfterNightServices);
    private static void NightPrefix()=>Dispatch(OfficialLifecyclePhase.BeforeNight);
    private static void NightPostfix(bool __runOriginal){if(__runOriginal)Dispatch(OfficialLifecyclePhase.AfterNight);Dispatch(OfficialLifecyclePhase.NightAttemptFinished);}
    private static void DayPostfix(bool __runOriginal){if(__runOriginal)Dispatch(OfficialLifecyclePhase.AfterEndDay);}
    internal static void Uninstall()
    {
        // A failed Attach can leave subscriptions and patches before _attached is set.
        ModHook.OnGameLoadedInit-=BeforeLoad;ModHook.OnGameLoadedLate-=AfterLoad;ModHook.OnGoingSleepLate-=AfterSleep;
        ModHook.OnHandlingNightlyServicesEarly-=BeforeServices;ModHook.OnHandlingNightlyServicesLate-=AfterServices;
        Hooks.UnpatchSelf();Listeners.Clear();_attached=false;
    }
}
