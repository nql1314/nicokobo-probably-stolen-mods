using System.Reflection;
using HarmonyLib;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Integration;

internal sealed class DayReconciliationFlow(
    NativeSessionBridge bridge, Action<string> log)
{
    private static DayReconciliationFlow? _active;
    private string? _attemptKey;
    private string? _nativeFailureKey;
    private long? _generation;
    private DateTime _nextErrorLogUtc;
    private NativeSessionBridge Bridge => bridge;

    internal static DayReconciliationFlow Install(HarmonyLib.Harmony harmony,
        NativeSessionBridge bridge, Action<string> log)
    {
        var flow = new DayReconciliationFlow(bridge, log);
        _active = flow;
        // Stage survival before the native morning save serializes modData.
        // The Mod never starts a second save from its update/report callbacks.
        var save = AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.SaveGame), Type.EmptyTypes)
            ?? throw new MissingMethodException(nameof(PlayerStore), nameof(PlayerStore.SaveGame));
        harmony.Patch(save,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(DayReconciliationFlow),
                nameof(BeforeNativeSave))));
        // Run before the morning report entry points so the native night log
        // receives the settlement even when the entire night transition
        // happens between two OnUpdate calls.
        foreach (var name in new[]
                 { nameof(StartOfDayUIManager.ShowMorningReport),
                   nameof(StartOfDayUIManager.InitPanel) })
        {
            try
            {
                var target = typeof(StartOfDayUIManager).GetMethod(name,
                    BindingFlags.Public | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (target?.ReturnType != typeof(void))
                    throw new MissingMethodException(nameof(StartOfDayUIManager), name);
                var prefix = typeof(DayReconciliationFlow).GetMethod(
                    nameof(BeforeMorningReport), BindingFlags.NonPublic |
                    BindingFlags.Static)!;
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                log($"[P3] Morning report settlement hook installed; method={name}");
            }
            catch (Exception ex)
            {
                log($"[ERROR] [P3] Morning report settlement hook unavailable; " +
                    $"method={name}; {ex.GetType().Name}: {ex.Message}");
            }
        }
        return flow;
    }

    private static void BeforeMorningReport() => _active?.Update();

    private static void BeforeNativeSave(PlayerStore __instance)
    {
        if (_active != null && NativeObject.Handle(__instance) == _active.Bridge.CurrentStorePointer)
            _active.Update();
    }

    internal void Update()
    {
        try
        {
            var view = bridge.CurrentSession;
            // Only inspect an existing store after NativeSessionBridge has
            // restored a validated Mod session; never construct one from the menu.
            if (view == null) return;
            if (_generation != view.Generation)
            {
                _generation = view.Generation;
                _attemptKey = null;
                _nativeFailureKey = null;
            }
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            if (store == null || NativeObject.Handle(store) == IntPtr.Zero ||
                NativeObject.Handle(store) != bridge.CurrentStorePointer ||
                store.runID != view.State.GameRunId ||
                store.saveSlotId != view.State.SaveSlotId)
                return;
            int nativeDay = StoreStation.GetDayCounter();
            var state = view.State;
            if (state.FailureReason is "bodyCollapse" or "exposure")
            {
                // The native save owns slot history. Trigger the ending once per loaded session.
                if (nativeDay == state.LastObservedDay && state.InitPhase == "complete")
                {
                    string failureKey = $"{state.RunId}:{state.FailureReason}";
                    if (_nativeFailureKey != failureKey)
                    {
                        _nativeFailureKey = failureKey;
                        log($"[P3] Native failure ending requested; reason={state.FailureReason}; day={nativeDay}");
                        store.ExecuteGameOver("aug");
                    }
                }
                return;
            }
            _nativeFailureKey = null;
            if (nativeDay <= state.LastObservedDay) return;
            string key = $"{state.RunId}:{state.Revision}:{nativeDay}";
            if (_attemptKey == key) return;
            bool mechanicalAscension = NicoWorkshop.HasMechanicalAscension(state);
            var effects = mechanicalAscension ? default : CyberneticItemOwnership.Capture(store);
            var next = SurvivalRules.ReconcileDay(state, nativeDay, effects, mechanicalAscension);
            if (next.Status != SettleStatus.Applied)
            {
                if (next.Status == SettleStatus.DayGap) _attemptKey = key;
                return;
            }
            _attemptKey = key;
            var saved = RuntimeRevisionNativeSaveAdapter.Apply(store,
                bridge.Sessions, state, next.State, nativeDay, effects: effects,
                mechanicalAscension: mechanicalAscension);
            log($"{(saved.Status is RuntimeRevisionSaveStatus.StagedInMemory or RuntimeRevisionSaveStatus.FailureStagedInMemory ? "" : "[ERROR] ")}[P3] Day reconciliation stage={saved.Status}; "
                + $"day={state.LastObservedDay}->{nativeDay}; "
                + $"health={state.Health}->{next.State.Health}; "
                + $"exposure={state.Exposure}->{next.State.Exposure}; "
                + $"detail={saved.Error ?? "none"}");
            if (saved.Status == RuntimeRevisionSaveStatus.StagedInMemory && !mechanicalAscension)
            {
                try
                {
                    string healthDelta = (next.State.Health - state.Health).ToString("+0;-0;0");
                    string exposureDelta = (next.State.Exposure - state.Exposure).ToString("+0;-0;0");
                    string message = AugText.Get("aug.aug_settlement_health_exposure", state.Health, next.State.Health, healthDelta, state.Exposure, next.State.Exposure, exposureDelta);
                    store.AddNightLog(message, "#BFA9FF");
                }
                catch (Exception ex)
                {
                    log($"[ERROR] [P3] Saved day settlement could not enter night log; " +
                        $"day={state.LastObservedDay}; {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow < _nextErrorLogUtc) return;
            _nextErrorLogUtc = DateTime.UtcNow.AddSeconds(AugNumbers.Timing.DayReconciliationErrorLogIntervalSeconds);
            log($"[ERROR] [P3] Day reconciliation unavailable; {ex.GetType().Name}: {ex.Message}");
        }
    }
}
