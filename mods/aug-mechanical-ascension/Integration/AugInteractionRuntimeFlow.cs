using AugMechanicalAscension.Serialization;
using HarmonyLib;
using AugMechanicalAscension.Domain;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace AugMechanicalAscension.Integration;

internal sealed class AugInteractionRuntimeFlow(
    NativeSessionBridge bridge, Action<string> log)
{
    private const string ReceiptKey = "nicokobo.aug.interaction_v1";
    private static AugInteractionRuntimeFlow? _active;
    private readonly Queue<PendingAction> _pending = new();
    private bool _installationAttempted;
    private bool _processing;

    private sealed record PendingAction(AugAction Action, IntPtr StorePointer,
        string RunId, int SaveSlotId, int Day, int VisitsLeft, string ClientId);

    private bool TryInstallWhenLocalizationReady()
    {
        if (_installationAttempted) return ReferenceEquals(_active, this);
        HarmonyLib.Harmony? harmony = null;
        try
        {
            // Patching AugHelper initializes its native class, which reads
            // localized dialogue. Wait without forcing synchronous loading.
            if (!LocalizationSettings.HasSettings) return false;
            var initialization = LocalizationSettings.InitializationOperation;
            if (!initialization.IsValid() || !initialization.IsDone) return false;
            _installationAttempted = true;
            if (initialization.Status != AsyncOperationStatus.Succeeded)
            {
                log("[ERROR] [P3] Aug interactions unavailable: localization initialization failed");
                return false;
            }
            harmony = new HarmonyLib.Harmony("nicokobo.aug.interactions");
            Patch(typeof(BargainUIManager), nameof(BargainUIManager.ThreatenAug),
                Type.EmptyTypes, nameof(BeforeThreat), nameof(AfterThreat));
            Patch(typeof(AugHelper), nameof(AugHelper.OnGoodKill),
                Type.EmptyTypes, nameof(BeforeKill), nameof(AfterKill));
            Patch(typeof(AugHelper), nameof(AugHelper.OnAugLeftAlive),
                Type.EmptyTypes, null, nameof(AfterAugLeftAlive));
            Patch(typeof(StoreClientManager), nameof(StoreClientManager.HandleAugClient),
                [typeof(bool)], nameof(BeforeAugGeneration), null);
            Patch(typeof(PlayerStore), nameof(PlayerStore.BeginDay),
                Type.EmptyTypes, nameof(BeforeBeginDay), null);
            Patch(typeof(PlayerStore), nameof(PlayerStore.SaveGame),
                Type.EmptyTypes, nameof(BeforeSave), null);
            Patch(typeof(PlayerStore), nameof(PlayerStore.LoadGame),
                Type.EmptyTypes, null, nameof(AfterLoad));
            _active = this;
            log("[P3] Aug interactions installed; threatExposure=5; killExposure=10; presence=1..99; spawning continues");
            return true;

            void Patch(Type type, string method, Type[] parameters,
                string? prefix, string? postfix)
            {
                var target = NativeHookSet.Find(type, method, parameters);
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(
                        typeof(AugInteractionRuntimeFlow), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(
                        typeof(AugInteractionRuntimeFlow), postfix));
            }
        }
        catch (Exception ex)
        {
            _installationAttempted = true;
            log($"[ERROR] [P3] Aug interactions unavailable: {ex}");
            try { harmony?.UnpatchSelf(); }
            catch (Exception cleanup)
            {
                log($"[ERROR] [P3] Aug interaction cleanup failed: {cleanup}");
            }
            return false;
        }
    }

    private static void BeforeThreat(BargainUIManager __instance,
        out PendingAction? __state)
    {
        __state = null;
        try
        {
            if (!NativeFields.Get<bool>(__instance,"threatenApplied"))
                __state = _active?.Capture(AugAction.Threat, NativeFields.Get<StoreClient>(__instance,"trackedClient"));
        }
        catch (Exception ex) { _active?.LogError("threat observation", ex); }
    }

    private static void AfterThreat(BargainUIManager __instance,
        PendingAction? __state)
    {
        try
        {
            // The native flag changes only after a real Aug accepts the threat.
            // False accusations and calls that did not apply a threat cost nothing.
            if (__state != null && NativeFields.Get<bool>(__instance,"threatenApplied") &&
                NativeFields.Get<StoreClient>(__instance,"trackedClient")?.identifier == __state.ClientId)
                _active?.Enqueue(__state);
        }
        catch (Exception ex) { _active?.LogError("threat settlement", ex); }
    }

    private static void BeforeKill(out PendingAction? __state)
    {
        __state = null;
        try
        {
            // Match GetShootResult: prefer the captured shot target, because the
            // current visitor may already have changed by the outcome callback.
            var target = StoreUIManager.Instance?.shotClientInstance
                         ?? (PlayerStore.HasInstance ? PlayerStore.Instance : null)?.currentClientInstance;
            __state = _active?.Capture(AugAction.Kill, target?.GetClientBlueprint());
        }
        catch (Exception ex) { _active?.LogError("kill observation", ex); }
    }

    private static void AfterKill(PendingAction? __state)
    {
        ClampNativePresence((PlayerStore.HasInstance ? PlayerStore.Instance : null));
        if (__state != null) _active?.Enqueue(__state);
    }

    private static void AfterAugLeftAlive()
        => ClampNativePresence((PlayerStore.HasInstance ? PlayerStore.Instance : null));

    private static void BeforeAugGeneration()
        => ClampNativePresence((PlayerStore.HasInstance ? PlayerStore.Instance : null));

    private static void BeforeBeginDay(PlayerStore __instance)
    {
        _active?.Update();
        ClampNativePresence(__instance);
    }

    private static void BeforeSave(PlayerStore __instance)
        => ClampNativePresence(__instance);

    private static void AfterLoad(PlayerStore __instance)
    {
        _active?._pending.Clear();
        ClampNativePresence(__instance);
    }

    private static void ClampNativePresence(PlayerStore? store)
    {
        try
        {
            // Native load/generation can run before the Mod session is restored.
            // The independent native start type is the scope for this invariant.
            if (store == null || NativeObject.Handle(store) == IntPtr.Zero ||
                NativeObject.Handle(store) != NativeObject.Handle((PlayerStore.HasInstance ? PlayerStore.Instance : null)) ||
                !StartDefinition.MatchesNativeStartType((int)store.startType)) return;
            int presence = AugInteractionRules.ClampPresence(store.augPresence);
            if (store.augPresence != presence) store.augPresence = presence;
            if (store.isAugEnded) store.isAugEnded = false;
        }
        catch (Exception ex) { _active?.LogError("presence normalization", ex); }
    }

    private PendingAction? Capture(AugAction action, StoreClient? client)
    {
        var view = bridge.CurrentSession;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        if (view == null || store == null || NativeObject.Handle(store) == IntPtr.Zero ||
            NativeObject.Handle(store) != bridge.CurrentStorePointer ||
            !StartDefinition.MatchesNativeStartType((int)store.startType) ||
            store.runID != view.State.GameRunId ||
            store.saveSlotId != view.State.SaveSlotId ||
            view.State.InitPhase != "complete" || view.State.FailureReason != "none" ||
            client == null || NativeObject.Handle(client) == IntPtr.Zero || !client.isAug ||
            string.IsNullOrEmpty(client.identifier)) return null;
        return new PendingAction(action, NativeObject.Handle(store), store.runID,
            store.saveSlotId, StoreStation.GetDayCounter(), store.visitLeftTonight,
            client.identifier);
    }

    private void Enqueue(PendingAction action)
    {
        _pending.Enqueue(action);
        Update();
    }

    internal void Update()
    {
        if (!TryInstallWhenLocalizationReady()) return;
        if (_processing) return;
        _processing = true;
        try
        {
            while (_pending.Count > 0)
            {
                var action = _pending.Peek();
                var view = bridge.CurrentSession;
                var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
                if (view == null || store == null ||
                    NativeObject.Handle(store) != action.StorePointer ||
                    NativeObject.Handle(store) != bridge.CurrentStorePointer ||
                    store.runID != action.RunId || store.saveSlotId != action.SaveSlotId ||
                    view.State.GameRunId != action.RunId ||
                    view.State.SaveSlotId != action.SaveSlotId ||
                    view.State.FailureReason != "none")
                {
                    _pending.Dequeue();
                    continue;
                }
                int day = StoreStation.GetDayCounter();
                if (day != action.Day)
                {
                    _pending.Dequeue();
                    log("[ERROR] [P3] Aug action exposure could not settle before the day changed");
                    continue;
                }
                // Wait for an in-flight consumable or daily revision, without
                // retrying an action whose save has already been attempted.
                if (view.State.LastObservedDay != day ||
                    view.State.HasPendingExternalOperation) return;
                _pending.Dequeue();
                Apply(store, view.State, action);
            }
        }
        catch (Exception ex) { LogError("action exposure save", ex); }
        finally { _processing = false; }
    }

    private void Apply(PlayerStore store, RunState state, PendingAction action)
    {
        var data = store.modData;
        if (data == null || !data.ContainsKey(RunState.SaveKey)) return;
        AugInteractionReceipt? previous = null;
        if (data.ContainsKey(ReceiptKey))
        {
            previous = JsonSerializer.Deserialize<AugInteractionReceipt>(data[ReceiptKey]);
            if (previous == null || string.IsNullOrEmpty(previous.RunId) ||
                string.IsNullOrEmpty(previous.ClientId))
                throw new InvalidOperationException("Saved Aug action receipt is unreadable");
        }
        var receipt = AugInteractionRules.RecordAction(previous, action.Action,
            action.RunId, action.Day, action.VisitsLeft, action.ClientId);
        if (receipt == null) return;
        bool mechanicalAscension = NicoWorkshop.HasMechanicalAscension(state);
        var next = AugInteractionRules.ApplyAction(state, action.Action, mechanicalAscension);
        if (next == null) return;
        string serialized = JsonSerializer.Serialize(receipt);
        data[ReceiptKey] = serialized;
        var saved = RuntimeRevisionNativeSaveAdapter.Apply(store, bridge.Sessions,
            state, next, action.Day,
            new Dictionary<string, string> { [ReceiptKey] = serialized },
            mechanicalAscension: mechanicalAscension);
        log($"{(saved.Status is RuntimeRevisionSaveStatus.StagedInMemory or RuntimeRevisionSaveStatus.FailureStagedInMemory ? "" : "[ERROR] ")}[P3] Aug action={action.Action}; client={action.ClientId}; " +
            $"day={action.Day}; exposure={state.Exposure}->{next.Exposure}; " +
            $"stage={saved.Status}; detail={saved.Error ?? "none"}");
    }

    private void LogError(string operation, Exception ex)
        => log($"[ERROR] [P3] Aug {operation}; {ex.GetType().Name}: {ex.Message}");
}
