using HarmonyLib;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using AugMechanicalAscension.Services;
namespace AugMechanicalAscension.Integration;

// Restore an existing Mod run from the game's loaded store. No new run is
// created here, and this bridge never writes or invokes a save method.
internal sealed class NativeSessionBridge
{
    private sealed class LoadAttempt
    {
        internal bool LateHookSeen;
    }

    [ThreadStatic] private static LoadAttempt? _loadAttempt;
    private static NativeSessionBridge? _active;
    private readonly SessionCoordinator _sessions = new();
    private readonly Action<string> _log;
    private IntPtr _currentStorePointer;
    private long? _nativeLoadedRevision;
    private string? _legacySchemaNotice;

    private NativeSessionBridge(Action<string> log) => _log = log;

    internal SessionView? CurrentSession => _sessions.Current;
    internal IntPtr CurrentStorePointer => _currentStorePointer;
    internal long? NativeLoadedRevision =>
        _sessions.Current?.State.Revision >= _nativeLoadedRevision &&
        _currentStorePointer != IntPtr.Zero ? _nativeLoadedRevision : null;
    internal SessionCoordinator Sessions => _sessions;
    internal static void Uninstall()
    { ModHook.OnGameLoadedLate-=MarkLateHook;_active?.Clear("Disable");_active=null;_loadAttempt=null; }
    internal void RebindAfterFork(PlayerStore store)
    {
        var read = _sessions.RestoreReadResult(ModDataReader.Read(store), store.runID, store.saveSlotId);
        if (read.Status != ReadStatus.Ready) throw new InvalidDataException("Aug fork could not rebind its session: " + read.Status);
        _currentStorePointer = NativeObject.Handle(store);
        _nativeLoadedRevision = null;
    }
    // Set when the loaded slot holds a Mod state written by a retired schema. The
    // notice only explains the refusal; no writes may follow from it.
    internal string? LegacySchemaNotice => _legacySchemaNotice;

    internal void MarkNewStore(PlayerStore store)
    {
        if (_sessions.Current?.State.GameRunId == store.runID &&
            _sessions.Current.State.SaveSlotId == store.saveSlotId &&
            Domain.StartDefinition.MatchesNativeStartType((int)store.startType))
            _currentStorePointer = NativeObject.Handle(store);
    }

    internal static NativeSessionBridge? Install(HarmonyLib.Harmony harmony, Action<string> log)
    {
        if (_active != null) return _active;
        var bridge = new NativeSessionBridge(log);
        harmony = new HarmonyLib.Harmony("nicokobo.aug.session");
        try
        {
            var hooks = new (Type Type, string Method, string? Prefix, string? Postfix)[]
            {
                (typeof(MainMenuUIController), "Awake", nameof(ClearForMainMenu), null),
                (typeof(PlayerStore), nameof(PlayerStore.StartNewGame), nameof(ClearForNewGame), null),
                (typeof(PlayerStore), nameof(PlayerStore.LoadGame), nameof(ClearForLoad), nameof(RestoreAfterLoad))
            };
            var resolved = hooks.Select(hook =>
            {
                var target = NativeHookSet.Find(hook.Type, hook.Method, Type.EmptyTypes, declaredOnly:true);
                if (!OfficialHookApi.TryValidateNativeTarget(target, out var reason))
                    throw new InvalidOperationException(reason);
                HarmonyMethod? Callback(string? name) => name == null ? null :
                    new(AccessTools.DeclaredMethod(typeof(NativeSessionBridge), name)
                        ?? throw new MissingMethodException(nameof(NativeSessionBridge), name));
                return (Target: target, Prefix: Callback(hook.Prefix), Postfix: Callback(hook.Postfix));
            }).ToArray();
            foreach (var hook in resolved)
                harmony.Patch(hook.Target, prefix: hook.Prefix, postfix: hook.Postfix);
            // Publish the bridge only when clearing and complete-load evidence
            // are installed together. A partial bridge must never authorize a start.
            ModHook.OnGameLoadedLate += MarkLateHook;
            _active = bridge;
            bridge.Log("lifecycleHooks=installed; nativeWrite=false");
            return bridge;
        }
        catch (Exception ex)
        {
            try { harmony.UnpatchSelf(); }
            catch (Exception cleanup) { bridge.Log("[ERROR] lifecycle rollback failed: " + cleanup.Message); }
            bridge.Log($"[ERROR] lifecycleHooks=disabled; {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void ClearForNewGame()
    {
        _loadAttempt = null;
        _active?.Clear("StartNewGame");
    }

    private static void ClearForMainMenu()
    {
        _loadAttempt = null;
        _active?.Clear("MainMenu.Awake");
    }

    private static void ClearForLoad()
    {
        _loadAttempt = new LoadAttempt();
        _active?.Clear("LoadGame");
    }

    private static void MarkLateHook()
    {
        var attempt = _loadAttempt;
        if (attempt != null) attempt.LateHookSeen = true;
    }

    private void Clear(string reason)
    {
        AugIdentityLabel.ClearForcedNextCustomer();
        _sessions.Clear();
        _currentStorePointer = IntPtr.Zero;
        _nativeLoadedRevision = null;
        _legacySchemaNotice = null;
        Log($"sessionCleared={reason}");
    }

    private static void RestoreAfterLoad(PlayerStore __instance)
    {
        var attempt = _loadAttempt;
        _loadAttempt = null;
        var bridge = _active;
        if (bridge == null) return;
        bridge._currentStorePointer = IntPtr.Zero;
        bridge._nativeLoadedRevision = null;
        bridge._legacySchemaNotice = null;
        if (attempt?.LateHookSeen != true)
        {
            bridge.Log("loadRead=Skipped; reason=late-load-hook-not-seen; sessionReady=false");
            return;
        }
        try
        {
            if (!Domain.StartDefinition.MatchesNativeStartType((int)__instance.startType))
            {
                bridge.Log($"loadRead=Skipped; reason=native-start-type-mismatch; "
                    + $"nativeStartType={(int)__instance.startType}; sessionReady=false");
                return;
            }
            var read = ModDataReader.Read(__instance);
            var result = bridge._sessions.RestoreReadResult(read, __instance.runID, __instance.saveSlotId);
            if (result.Status == ReadStatus.Ready && bridge._sessions.Current != null)
            {
                bridge._currentStorePointer = NativeObject.Handle(__instance);
                bridge._nativeLoadedRevision = bridge._sessions.Current.State.Revision;
            }
            else if (result.Status == ReadStatus.LegacySchema)
            {
                bridge._legacySchemaNotice =
                    $"slot {__instance.saveSlotId}: {result.Error ?? "retired Mod schema"}";
                bridge.Log("[WARN] modDisabled=true; reason=retired-mod-schema; "
                    + "nativeSaveUntouched=true; action=start a new Aug run");
            }
            bridge.Log($"loadRead={result.Status}; slot={__instance.saveSlotId}; "
                + $"runID={__instance.runID ?? "missing"}; sessionReady={bridge._sessions.Current != null}; "
                + $"detail={result.Error ?? "none"}");
        }
        catch (Exception ex)
        {
            bridge._sessions.Clear();
            bridge._currentStorePointer = IntPtr.Zero;
            bridge._nativeLoadedRevision = null;
            bridge.Log($"[ERROR] loadRead=Unavailable; sessionReady=false; "
                + $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        try
        {
            if (message.StartsWith("[ERROR] ", StringComparison.Ordinal))
                _log("[ERROR] [P0] NativeSession " + message[8..]);
            else if (message.StartsWith("[WARN] ", StringComparison.Ordinal))
                _log("[WARN] [P0] NativeSession " + message[7..]);
            else
                _log("[P0] NativeSession " + message);
        }
        catch { /* Logging must not change a native load. */ }
    }
}
