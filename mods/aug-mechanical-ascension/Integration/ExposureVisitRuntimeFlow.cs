using AugMechanicalAscension.Serialization;
using HarmonyLib;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

namespace AugMechanicalAscension.Integration;

// Observes the game's completed GetNextClient call, then waits until the
// visitor and the daily Mod revision are both ready. The receipt is stored in
// native modData alongside the survival revision; it does not alter the schema.
internal sealed class ExposureVisitRuntimeFlow
{
    private const string ReceiptKey = "nicokobo.aug.exposure_visit_v1";
    private static ExposureVisitRuntimeFlow? _active;
    private readonly NativeSessionBridge _bridge;
    private readonly Action<string> _log;
    private PendingVisit? _pending;
    private string? _attemptedKey;
    private DateTime _nextErrorLogUtc;

    private sealed record PendingVisit(IntPtr StorePointer, IntPtr ClientPointer,
        int Day, int VisitsLeft, string ClientId);
    private sealed record VisitReceipt(string RunId, int Day, int VisitsLeft,
        string ClientId, long Revision, int Exposure, bool InspectionRolled,
        string InspectionStatus, string? InspectorId = null);

    private ExposureVisitRuntimeFlow(NativeSessionBridge bridge, Action<string> log)
    {
        _bridge = bridge;
        _log = log;
    }

    internal static ExposureVisitRuntimeFlow? Install(HarmonyLib.Harmony harmony,
        NativeSessionBridge bridge, Action<string> log)
    {
        try
        {
            var next = AccessTools.Method(typeof(PlayerStore),
                nameof(PlayerStore.GetNextClient), Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(PlayerStore),
                    nameof(PlayerStore.GetNextClient));
            var load = AccessTools.Method(typeof(PlayerStore),
                nameof(PlayerStore.LoadGame), Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(PlayerStore),
                    nameof(PlayerStore.LoadGame));
            var queue = AccessTools.Method(typeof(StoreClientManager),
                nameof(StoreClientManager.AddNextClient), [typeof(StoreClient)])
                ?? throw new MissingMethodException(nameof(StoreClientManager),
                    nameof(StoreClientManager.AddNextClient));
            var factory = AccessTools.Method(typeof(StoreClientList),
                nameof(StoreClientList.CreateInspectionClient),
                [typeof(int), typeof(bool)])
                ?? throw new MissingMethodException(nameof(StoreClientList),
                    nameof(StoreClientList.CreateInspectionClient));
            var flow = new ExposureVisitRuntimeFlow(bridge, log);
            harmony.Patch(next, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(ExposureVisitRuntimeFlow),
                    nameof(NextClientPostfix))));
            harmony.Patch(load, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(ExposureVisitRuntimeFlow),
                    nameof(ResetAfterLoad))));
            _active = flow;
            log($"[P3] Exposure visitor hook installed; inspection factory={factory}; queue={queue}");
            return flow;
        }
        catch (Exception ex)
        {
            log($"[ERROR] [P3] Exposure visitor hook unavailable; {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void NextClientPostfix(PlayerStore __instance)
        => _active?.Observe(__instance);

    private static void ResetAfterLoad()
    {
        if (_active == null) return;
        _active._pending = null;
        _active._attemptedKey = null;
    }

    private void Observe(PlayerStore store)
    {
        try
        {
            var view = _bridge.CurrentSession;
            if (view == null || store == null || NativeObject.Handle(store) == IntPtr.Zero ||
                NativeObject.Handle(store) != _bridge.CurrentStorePointer ||
                store.runID != view.State.GameRunId ||
                store.saveSlotId != view.State.SaveSlotId) return;
            var instance = store.currentClientInstance;
            var client = instance?.GetClientBlueprint();
            if (instance == null || client == null ||
                NativeObject.Handle(instance) == IntPtr.Zero || NativeObject.Handle(client) == IntPtr.Zero ||
                string.IsNullOrEmpty(client.identifier)) return;
            _pending = new PendingVisit(NativeObject.Handle(store), NativeObject.Handle(instance),
                StoreStation.GetDayCounter(), store.visitLeftTonight,
                client.identifier);
        }
        catch (Exception ex) { LogError("arrival observation", ex); }
    }

    internal void Update()
    {
        var pending = _pending;
        if (pending == null) return;
        try { Process(pending); }
        catch (Exception ex)
        {
            _pending = null;
            LogError("visitor settlement", ex);
        }
    }

    private void Process(PendingVisit pending)
    {
        var view = _bridge.CurrentSession;
        if (view == null) { _pending = null; return; }
        var state = view.State;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        var instance = store?.currentClientInstance;
        var client = instance?.GetClientBlueprint();
        if (store == null || NativeObject.Handle(store) != pending.StorePointer ||
            NativeObject.Handle(store) != _bridge.CurrentStorePointer ||
            store.runID != state.GameRunId || store.saveSlotId != state.SaveSlotId ||
            instance == null || NativeObject.Handle(instance) != pending.ClientPointer ||
            client == null || client.identifier != pending.ClientId ||
            StoreStation.GetDayCounter() != pending.Day)
        {
            _pending = null;
            return;
        }
        if (!store.isClientArrived || state.LastObservedDay != pending.Day)
            return;
        if (state.InitPhase != "complete" || state.FailureReason != "none") return;
        if (state.HasPendingExternalOperation)
            return;

        string? faction = client.IsFaction(StoreClient.FACTION_SECURITY)
            ? ExposureVisitRules.Security : null;
        _pending = null;
        if (faction == null) return;
        string key = $"{state.GameRunId}|{pending.Day}|{pending.VisitsLeft}|{pending.ClientId}";
        if (_attemptedKey == key) return;
        var data = store.modData;
        if (data == null || !data.ContainsKey(RunState.SaveKey)) return;
        var previousReceipt = ReadReceipt(data);
        if (data.ContainsKey(ReceiptKey) && previousReceipt == null)
        {
            _log("[ERROR] [P3] Exposure visitor blocked: saved visit receipt is unreadable");
            return;
        }
        if (previousReceipt != null && SameVisit(previousReceipt, state.GameRunId,
                pending.Day, pending.VisitsLeft, pending.ClientId))
        {
            _log($"[P3] Exposure visitor already recorded; client={pending.ClientId}; " +
                $"day={pending.Day}; status={previousReceipt.InspectionStatus}");
            return;
        }
        bool mechanicalAscension = NicoWorkshop.HasMechanicalAscension(state);
        var next = ExposureVisitRules.ApplyVisit(state, faction, mechanicalAscension);
        if (next == null) return;
        int chance = faction == ExposureVisitRules.Security &&
                     client.clientIntent != StoreClient.ClientIntent.INSPECTION
            ? ExposureVisitRules.InspectionChancePercent(next.Exposure) : 0;
        bool rolled = chance > 0 && UnityEngine.Random.Range(0, 100) < chance;
        var receipt = new VisitReceipt(state.GameRunId, pending.Day,
            pending.VisitsLeft, pending.ClientId, next.Revision, next.Exposure,
            rolled, rolled ? "selected" : "none");
        // Do not roll or queue twice in the same session. The live receipt and
        // exposure reach disk together at the base game's overnight save.
        _attemptedKey = key;
        string receiptJson = JsonSerializer.Serialize(receipt);
        data[ReceiptKey] = receiptJson;
        var saved = RuntimeRevisionNativeSaveAdapter.Apply(store,
            _bridge.Sessions, state, next, pending.Day,
            new Dictionary<string, string> { [ReceiptKey] = receiptJson },
            mechanicalAscension: mechanicalAscension);
        _log($"[P3] Exposure visitor client={pending.ClientId}; faction={faction}; " +
            $"day={pending.Day}; exposure={state.Exposure}->{next.Exposure}; " +
            $"inspectionChance={chance}%; selected={rolled}; stage={saved.Status}");
        if (saved.Status != RuntimeRevisionSaveStatus.StagedInMemory || !rolled ||
            next.FailureReason != "none") return;
        QueueInspection(store, receipt);
    }

    private void QueueInspection(PlayerStore store, VisitReceipt receipt)
    {
        var manager = store.storeClientManager;
        var stack = manager?.clientStack;
        var data = store.modData;
        if (manager == null || stack == null || data == null) return;
        try
        {
            int before = stack.Count;
            for (int i = 0; i < before; i++)
            {
                var queuedClient = stack[i];
                if (queuedClient == null ||
                    queuedClient.clientIntent != StoreClient.ClientIntent.INSPECTION)
                    continue;
                SaveInspectionReceipt(store, receipt with
                {
                    InspectionStatus = "alreadyQueued",
                    InspectorId = queuedClient.identifier
                }, $"existing inspector={queuedClient.identifier}; no duplicate queued");
                return;
            }
            var security = store.secData;
            if (security == null || NativeObject.Handle(security) == IntPtr.Zero)
                throw new InvalidOperationException("Native security data unavailable");
            int personality = security.GetInspectorPersonality();
            var inspector = StoreClientList.CreateInspectionClient(personality,
                false);
            // CompleteSecClient sets the security role but leaves this factory's
            // clientFaction at NONE. Faction membership is not an inspector invariant.
            if (inspector == null || NativeObject.Handle(inspector) == IntPtr.Zero ||
                inspector.clientIntent != StoreClient.ClientIntent.INSPECTION ||
                !inspector.isSecurity || string.IsNullOrEmpty(inspector.identifier))
                throw new InvalidOperationException("Native inspector factory returned an unexpected client");
            manager.AddNextClient(inspector);
            bool observed = stack.Count == before + 1;
            bool found = false;
            for (int i = 0; i < stack.Count; i++)
                if (stack[i] != null && NativeObject.Handle(stack[i]) == NativeObject.Handle(inspector))
                    found = true;
            if (!observed || !found)
                throw new InvalidOperationException("Native inspector queue change was not observed");
            SaveInspectionReceipt(store, receipt with
            {
                InspectionStatus = "queued",
                InspectorId = inspector.identifier
            }, $"inspector={inspector.identifier}; queue={before}->{stack.Count}");
        }
        catch (Exception ex)
        {
            // A queue failure can have partial native effects. Never retry it
            // automatically or produce a second inspector from one roll.
            LogError("inspection queue indeterminate; no automatic retry", ex);
        }
    }

    private void SaveInspectionReceipt(PlayerStore store, VisitReceipt receipt,
        string detail)
    {
        var data = store.modData;
        if (data == null || !data.ContainsKey(RunState.SaveKey))
            throw new InvalidOperationException("Native modData became unavailable");
        string serialized = JsonSerializer.Serialize(receipt);
        data[ReceiptKey] = serialized;
        if (data[ReceiptKey] != serialized)
            throw new InvalidOperationException(
                "Inspection queue marker staging failed");
        _log($"[P3] Native inspection {receipt.InspectionStatus}; {detail}; " +
            "marker=StagedInMemory; save=deferred-until-overnight");
    }

    private static VisitReceipt? ReadReceipt(
        System.Collections.Generic.Dictionary<string, string> data)
    {
        if (!data.ContainsKey(ReceiptKey)) return null;
        try { return JsonSerializer.Deserialize<VisitReceipt>(data[ReceiptKey]); }
        catch { return null; }
    }

    private static bool SameVisit(VisitReceipt receipt, string runId,
        int day, int visitsLeft, string clientId)
        => receipt.RunId == runId && receipt.Day == day &&
           receipt.VisitsLeft == visitsLeft && receipt.ClientId == clientId;

    private void LogError(string operation, Exception ex)
    {
        if (DateTime.UtcNow < _nextErrorLogUtc) return;
        _nextErrorLogUtc = DateTime.UtcNow.AddSeconds(AugNumbers.Timing.ExposureVisitErrorLogIntervalSeconds);
        _log($"[ERROR] [P3] Exposure {operation}; {ex.GetType().Name}: {ex.Message}");
    }
}
