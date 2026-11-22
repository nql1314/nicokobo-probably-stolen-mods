using HarmonyLib;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Integration;

// Character passive: the Aug start recognizes actual Aug customers and
// tags the active customer blueprint itself, so every native name surface sees
// the label. It is deliberately independent of StartingPerk and equipment.
internal static class AugIdentityLabel
{
    private static NativeSessionBridge? _bridge;
    private static Action<string>? _log;
    private const int ForceAug = 1;
    private const int ForceAppraisalIntro = 2;
    private const int ForceThief = 3;
    private static int _forcedNextCustomer;

    internal static int ForcedNextCustomer => Volatile.Read(ref _forcedNextCustomer);
    internal static bool NextAugArmed => ForcedNextCustomer == ForceAug;
    internal static bool AppraisalIntroArmed =>
        ForcedNextCustomer == ForceAppraisalIntro;
    internal static bool NextThiefArmed => ForcedNextCustomer == ForceThief;

    internal static bool ArmNextAug()
    {
        if (!IsActiveSession()) return false;
        Interlocked.Exchange(ref _forcedNextCustomer, ForceAug);
        return true;
    }

    internal static bool ArmNextAppraisalIntro()
        => ArmNext(ForceAppraisalIntro);

    internal static bool ArmNextThief() => ArmNext(ForceThief);

    internal static void ClearForcedNextCustomer()
        => Interlocked.Exchange(ref _forcedNextCustomer, 0);

    private static bool ArmNext(int kind)
    {
        if (!IsActiveSession()) return false;
        Interlocked.Exchange(ref _forcedNextCustomer, kind);
        return true;
    }

    internal static bool Install(HarmonyLib.Harmony harmony,
        NativeSessionBridge bridge, Action<string> log)
    {
        _bridge = bridge;
        _log = log;
        try
        {
            var next = AccessTools.Method(typeof(PlayerStore),
                nameof(PlayerStore.GetNextClient), Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(PlayerStore),
                    nameof(PlayerStore.GetNextClient));
            harmony.Patch(next, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(AugIdentityLabel), nameof(NextClientPrefix))));
            var create = AccessTools.Method(typeof(StoreClientInstance),
                nameof(StoreClientInstance.CreateClientInstance), [typeof(StoreClient)])
                ?? throw new MissingMethodException(nameof(StoreClientInstance),
                    nameof(StoreClientInstance.CreateClientInstance));
            harmony.Patch(create, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(AugIdentityLabel), nameof(CreatePrefix))));
            var target = AccessTools.Method(typeof(DialogUIManager),
                nameof(DialogUIManager.DisplayClientText), [typeof(Dialogue)])
                ?? throw new MissingMethodException(nameof(DialogUIManager),
                    nameof(DialogUIManager.DisplayClientText));
            harmony.Patch(target, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(AugIdentityLabel), nameof(DisplayPostfix))));
            log("[P3] Character passive Aug identity hooks installed; all-name tag=true");
            return true;
        }
        catch (Exception ex)
        {
            log($"[ERROR] [P3] Character passive Aug identity label hook failed; " +
                $"{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void NextClientPrefix(PlayerStore __instance)
    {
        try
        {
            if (!IsActiveSession() || __instance == null ||
                NativeObject.Handle(__instance) == IntPtr.Zero) return;
            int forcedKind = ForcedNextCustomer;
            if (forcedKind == 0) return;
            var queue = __instance.storeClientManager?.clientStack;
            if (queue == null || queue.Count == 0) return;
            var forced = forcedKind switch
            {
                ForceAug => StoreClientList.CreateAugMaterial(1),
                ForceAppraisalIntro => StoreClientListAppraisal.CreateAppraisal1(),
                ForceThief => StoreClientList.CreateThief(),
                _ => null
            };
            if (forced == null || NativeObject.Handle(forced) == IntPtr.Zero)
                throw new InvalidOperationException("Native debug customer factory returned no customer");
            queue[0] = forced;
            Interlocked.CompareExchange(ref _forcedNextCustomer, 0, forcedKind);
            _log?.Invoke($"[Debug] Next queued customer replaced; kind={forcedKind}; id={forced.identifier}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Debug] Next queued customer unchanged; " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void CreatePrefix(ref StoreClient storeClient)
    {
        try
        {
            if (!IsActiveSession() || storeClient == null ||
                NativeObject.Handle(storeClient) == IntPtr.Zero) return;
            if (storeClient.isAug)
                storeClient.displayName = IdentityDisplayRules.AppendAugTag(
                    storeClient.displayName, true);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[P3] Character passive Aug blueprint label unchanged; " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void DisplayPostfix(DialogUIManager __instance)
    {
        try
        {
            var bridge = _bridge;
            var view = bridge?.CurrentSession;
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            if (bridge == null || view == null || store == null ||
                NativeObject.Handle(store) != bridge.CurrentStorePointer ||
                store.runID != view.State.GameRunId ||
                store.saveSlotId != view.State.SaveSlotId ||
                view.State.InitPhase != "complete" ||
                view.State.FailureReason != "none") return;

            var client = store.currentClientInstance?.GetClientBlueprint();
            var label = __instance?.clientNameTMP;
            if (client == null || NativeObject.Handle(client) == IntPtr.Zero || !client.isAug ||
                label == null || NativeObject.Handle(label) == IntPtr.Zero) return;
            label.text = IdentityDisplayRules.AppendAugTag(label.text, true);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[P3] Character passive Aug label unchanged; " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsActiveSession()
    {
        var bridge = _bridge;
        var view = bridge?.CurrentSession;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        return bridge != null && view != null && store != null &&
               NativeObject.Handle(store) != IntPtr.Zero &&
               NativeObject.Handle(store) == bridge.CurrentStorePointer &&
               store.runID == view.State.GameRunId &&
               store.saveSlotId == view.State.SaveSlotId &&
               view.State.InitPhase == "complete" &&
               view.State.FailureReason == "none";
    }
}
