using HarmonyLib;
namespace AugMechanicalAscension.Integration;

// Native saves include storeState but exclude clientStack and the active client.
// A daytime Mod save can therefore reload an open store with no customers and
// never enter OnShutterOpened, the normal customer-generation entry point.
internal sealed class SameDayClientRecovery(
    NativeSessionBridge bridge, Action<string> log)
{
    private static SameDayClientRecovery? _active;
    private PendingLoad? _pending;

    private sealed record PendingLoad(IntPtr StorePointer, string RunId,
        int SlotId, int Day);

    internal static SameDayClientRecovery? Install(HarmonyLib.Harmony harmony,
        NativeSessionBridge bridge, Action<string> log)
    {
        try
        {
            var load = AccessTools.Method(typeof(PlayerStore),
                nameof(PlayerStore.LoadGame), Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(PlayerStore),
                    nameof(PlayerStore.LoadGame));
            var flow = new SameDayClientRecovery(bridge, log);
            harmony.Patch(load,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(SameDayClientRecovery), nameof(BeforeLoad))),
                postfix: new HarmonyMethod(AccessTools.Method(
                    typeof(SameDayClientRecovery), nameof(AfterLoad)))
                { priority = Priority.Last });
            _active = flow;
            log("[P3] Same-day customer recovery installed; open-store loads only");
            return flow;
        }
        catch (Exception ex)
        {
            log($"[ERROR] [P3] Same-day customer recovery unavailable; " +
                $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void BeforeLoad()
    {
        if (_active != null) _active._pending = null;
    }

    private static void AfterLoad(PlayerStore __instance)
    {
        var flow = _active;
        if (flow == null) return;
        try
        {
            // Run after NativeSessionBridge's postfix has validated this load.
            if (!flow.IsOpenSession(__instance)) return;
            var queue = __instance.storeClientManager?.clientStack;
            if (queue == null || queue.Count != 0 ||
                __instance.currentClientInstance != null ||
                __instance.isClientArrived) return;
            flow._pending = new PendingLoad(NativeObject.Handle(__instance),
                __instance.runID, __instance.saveSlotId,
                StoreStation.GetDayCounter());
        }
        catch (Exception ex)
        {
            flow.LogFailure(ex);
        }
    }

    internal void Update()
    {
        var pending = _pending;
        if (pending == null) return;
        try
        {
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            if (store == null || NativeObject.Handle(store) != pending.StorePointer ||
                store.runID != pending.RunId || store.saveSlotId != pending.SlotId ||
                StoreStation.GetDayCounter() != pending.Day || !IsOpenSession(store))
            {
                _pending = null;
                return;
            }
            // LoadGame returns inside EmporiumEntry.Start. Wait for the native
            // visitor/UI objects before asking for the normal arrival animation.
            var ui = StoreUIManager.Instance;
            var clientMono = StoreClientMono.Instance;
            if (ui == null || NativeObject.Handle(ui) == IntPtr.Zero ||
                clientMono == null || NativeObject.Handle(clientMono) == IntPtr.Zero) return;
            var manager = store.storeClientManager;
            var queue = manager?.clientStack;
            if (queue == null) { _pending = null; return; }
            if (queue.Count != 0 || store.currentClientInstance != null ||
                store.isClientArrived)
            {
                _pending = null;
                return;
            }

            // Consume before invoking other Mods' generation hooks. A partial
            // failure must not repeatedly generate visitors or run their hooks.
            _pending = null;
            manager!.GenerateClient();
            // OnShutterOpened already advanced and consumed the saved future
            // client schedule today. Reloading must not advance it again.
            log($"[P3] Same-day customers regenerated; slot={pending.SlotId}; " +
                $"day={pending.Day}; queued={queue.Count}; future-client schedule retained");
            store.TryCallNextClient();
        }
        catch (Exception ex)
        {
            _pending = null;
            LogFailure(ex);
        }
    }

    private bool IsOpenSession(PlayerStore store)
    {
        var state = bridge.CurrentSession?.State;
        return state != null && bridge.NativeLoadedRevision != null &&
            NativeObject.Handle(store) != IntPtr.Zero &&
            NativeObject.Handle(store) == bridge.CurrentStorePointer &&
            store.runID == state.GameRunId && store.saveSlotId == state.SaveSlotId &&
            state.InitPhase == "complete" && state.FailureReason == "none" &&
            StoreStation.GetDayCounter() == state.LastObservedDay &&
            store.storeState == PlayerStore.StoreState.OPEN;
    }

    private void LogFailure(Exception ex)
        => log($"[ERROR] [P3] Same-day customer recovery failed; " +
            $"{ex.GetType().Name}: {ex.Message}");
}
