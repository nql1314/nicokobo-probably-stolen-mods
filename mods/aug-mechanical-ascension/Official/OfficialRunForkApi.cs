using HarmonyLib;
using AugMechanicalAscension.Serialization;
using UnityEngine;

namespace AugMechanicalAscension.Official;

/// <summary>Distinguishes genuine starts from InitialSave continuations and
/// migrates registered content before the nested native save.</summary>
internal static class OfficialRunForkApi
{
    private const string HookId = "nicokobo.aug.run_fork";
    private sealed record Participant(string Owner, string Key,
        Func<string, OfficialRunIdentity, OfficialRunIdentity, string> Rebind,
        Action<PlayerStore>? Rebound, Func<PlayerStore, bool>? Ready);
    private sealed class Attempt(PlayerStore store, OfficialRunIdentity source,
        (Participant Handler, string Json)[] states)
    {
        internal readonly PlayerStore Store = store;
        internal readonly OfficialRunIdentity Source = source;
        internal readonly (Participant Handler, string Json)[] States = states;
        internal OfficialRunIdentity? Destination;
        internal bool WriteCompleted;
    }
    private static readonly Dictionary<string, Participant> Participants = new(StringComparer.Ordinal);
    private static IDisposable? _loaded;
    private static Action<string>? _log;
    private static (IntPtr Store, OfficialRunIdentity Identity)? _known;
    private static IntPtr _freshStore, _blockedStore;
    private sealed record LoadSource(IntPtr Store, int SelectedSlot, string ExplicitFileName);
    private static LoadSource? _loadSource;
    internal static LoadedSlotRebindResult LastLoadedRebind { get; private set; } = new("None", -1, -1, [], "No load observed");
    [ThreadStatic] private static Attempt? _attempt;
    [ThreadStatic] private static int _initialDepth;
    public static bool IsAvailable { get; private set; }

    /// <summary>Rebind is pure and validates the entire owned state, including
    /// unresolved transactions. It runs once with source==destination before
    /// allocation, then with the actual destination before the inner SaveGame.
    /// Rebound invalidates live caches; it must not grant, save, or replay actions.</summary>
    public static IDisposable RegisterState(string ownerId, string key,
        Func<string, OfficialRunIdentity, OfficialRunIdentity, string> rebind,
        Action<PlayerStore>? rebound = null, Func<PlayerStore, bool>? ready = null)
    {
        if (!IsAvailable) throw new InvalidOperationException("Run-fork adapter unavailable");
        if (!OwnedCallbacks<bool, bool>.ValidId(ownerId, key) || rebind == null)
            throw new ArgumentException("Invalid run-fork owner, key or callback");
        var participant = new Participant(ownerId, key, rebind, rebound, ready);
        if (!Participants.TryAdd(key, participant)) throw new InvalidOperationException("Run-fork key already registered: " + key);
        return new CallbackLease(() => { if (ReferenceEquals(Participants.GetValueOrDefault(key), participant)) Participants.Remove(key); });
    }

    public static bool IsForking(PlayerStore store) => IsAvailable &&
        _attempt is { } attempt && NativeObject.Handle(store) == NativeObject.Handle(attempt.Store);



    internal static void Install(bool allowed, Action<string> log)
    {
        _log = log;
        if (!allowed || IsAvailable) return;
        var harmony = new HarmonyLib.Harmony(HookId);
        try
        {
            var hooks = new[]
            {
                (nameof(PlayerStore.StartNewGame), nameof(NewGamePrefix), (string?)null, (string?)null),
                (nameof(PlayerStore.InitialSave), nameof(InitialPrefix), (string?)nameof(InitialPostfix), (string?)nameof(InitialFinalizer)),
                (nameof(PlayerStore.SaveGame), nameof(SavePrefix), (string?)null, (string?)null),
                ("WriteSlotFile", (string?)null, (string?)nameof(WritePostfix), (string?)null)
            }.Select(h => (Target: NativeHookSet.Require(typeof(PlayerStore), h.Item1, Type.EmptyTypes, typeof(void)),
                Prefix: Callback(h.Item2), Postfix: Callback(h.Item3), Finalizer: Callback(h.Item4))).ToArray();
            foreach (var hook in hooks) NativeHookSet.RequireNativeEntryPoint(hook.Target);
            foreach (var hook in hooks) harmony.Patch(hook.Target, hook.Prefix, hook.Postfix, finalizer: hook.Finalizer);
            _loaded = OfficialLifecycleApi.Subscribe("nicokobo.aug", HookId + ".loaded",
                OfficialLifecyclePhase.AfterGameLoadedLate, context => MarkLoaded(context.Store));
            ModHook.OnGameLoadedInit += CaptureLoadSource;
            ModHook.OnGameLoadedNormal += RebindLoadedSlot;
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf(); _loaded?.Dispose(); _loaded = null;
            Log("[ERROR] Run-fork adapter disabled: " + ex.Message);
        }
    }

    internal static void Uninstall()
    {
        IsAvailable = false; _loaded?.Dispose(); _loaded = null;
        ModHook.OnGameLoadedInit -= CaptureLoadSource;
        ModHook.OnGameLoadedNormal -= RebindLoadedSlot;
        NativeHookSet.Remove(HookId, _log); Participants.Clear();
        _loadSource = null;
        _attempt = null; _known = null; _freshStore = _blockedStore = IntPtr.Zero; _initialDepth = 0;
    }

    private static HarmonyMethod? Callback(string? name) => name == null ? null :
        new(AccessTools.Method(typeof(OfficialRunForkApi), name)) { priority = Priority.First };
    private static OfficialRunIdentity Identity(PlayerStore store) => new(store.runID, store.saveSlotId, StoreStation.GetDayCounter());
    private static bool Valid(OfficialRunIdentity id) => !string.IsNullOrWhiteSpace(id.RunId) && id.SlotId >= 0 && id.Day >= 0;
    private static bool Same(OfficialRunIdentity a, OfficialRunIdentity b) => a.RunId == b.RunId && a.SlotId == b.SlotId;
    private static bool AppliesTo(PlayerStore store) =>
        store.startScenarioId == AugMechanicalAscension.Domain.StartDefinition.Id ||
        store.modData != null && Participants.Keys.Any(store.modData.ContainsKey);
    private static void CaptureLoadSource()
    {
        _loadSource = null; _known = null; _blockedStore = IntPtr.Zero;
        LastLoadedRebind = new("Loading", -1, -1, [], "Awaiting loaded source evidence");
        if (!PlayerStore.HasInstance) return;
        var selected = NewGameData.Instance;
        // The game has resolved the load file. An empty name selects the slot's default save.
        string explicitFileName = selected.saveFile ?? string.Empty;
        if (selected.saveSlot < 0 ||
            (explicitFileName.Length != 0 && (Path.GetFileName(explicitFileName) != explicitFileName ||
                !explicitFileName.EndsWith(".es3", StringComparison.OrdinalIgnoreCase)))) return;
        _loadSource = new(NativeObject.Handle(PlayerStore.Instance), selected.saveSlot, explicitFileName);
    }
    private static void RebindLoadedSlot()
    {
        if (!PlayerStore.HasInstance) return;
        var store = PlayerStore.Instance;
        if (!AppliesTo(store)) { LastLoadedRebind = new("Unrelated", -1, store.saveSlotId, [], "No Aug state"); return; }
        try
        {
            var source = _loadSource ?? throw new InvalidDataException("No captured native load file");
            var selected = NewGameData.Instance;
            if (source.Store != NativeObject.Handle(store) || source.SelectedSlot != selected.saveSlot ||
                source.ExplicitFileName != (selected.saveFile ?? string.Empty) ||
                store.saveSlotId != source.SelectedSlot || _attempt != null || _initialDepth != 0)
                throw new InvalidDataException("Native load selection changed");
            string run = store.runID; int day = StoreStation.GetDayCounter();
            var handlers = Participants.Values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
            var plan = OfficialLoadedSlotRebind.PrepareLoaded(store.modData, run, store.saveSlotId,
                day, (int)store.startType, store.startScenarioId,
                handlers.Select(h => new LoadedSlotBinding(h.Key, (json, sourceRun, sourceSlot, targetRun, targetSlot) =>
                    h.Rebind(json, new(sourceRun, sourceSlot, day), new(targetRun, targetSlot, day)))).ToArray());
            if (plan.Writes.Length != 0 && handlers.Any(h => h.Ready?.Invoke(store) == false))
                throw new InvalidDataException("Owned state has an active operation during native copy load");
            OfficialLoadedSlotRebind.Apply(plan, store.modData, () => NativeObject.Handle(PlayerStore.Instance) == source.Store &&
                store.runID == run && store.saveSlotId == source.SelectedSlot && StoreStation.GetDayCounter() == day &&
                NewGameData.Instance.saveSlot == source.SelectedSlot);
            LastLoadedRebind = new(plan.Writes.Length == 0 ? "SameSlot" : "ReboundInMemory", plan.SourceSlot,
                plan.TargetSlot, plan.Writes.Select(x => x.Key).ToArray(), "Loaded state rebound; normal native SaveGame remains unchanged");
            if (plan.Writes.Length != 0) Log($"[INFO] Native copied slot rebound in memory: {plan.SourceSlot} -> {plan.TargetSlot}; states={plan.Writes.Length}; saved=false");
            // The ordinary LoadGame postfix restores sessions after inventory
            // decoding/Late. Do not invoke Rebound caches early or replay grants.
        }
        catch (Exception ex)
        {
            _blockedStore = NativeObject.Handle(store);
            LastLoadedRebind = new("Rejected", -1, store.saveSlotId, [], ex.Message);
            Log("[ERROR] Native copied-slot Mod data could not be rebound: " + ex.Message);
        }
        finally { _loadSource = null; }
    }
    private static void MarkLoaded(PlayerStore store)
    {
        if (NativeObject.Handle((PlayerStore.HasInstance ? PlayerStore.Instance : null)) != NativeObject.Handle(store)) return;
        if (_blockedStore == NativeObject.Handle(store)) { _known = null; return; }
        if (!AppliesTo(store)) { _known = null; return; }
        var identity = Identity(store);
        if (Valid(identity)) { _known = (NativeObject.Handle(store), identity); _blockedStore = IntPtr.Zero; _freshStore = IntPtr.Zero; }
    }
    private static void NewGamePrefix(PlayerStore __instance)
    {
        if (_attempt != null) throw new InvalidOperationException("Cannot start a new game during a run fork");
        _known = null; _blockedStore = IntPtr.Zero; _freshStore = NativeObject.Handle(__instance);
    }

    private static void InitialPrefix(PlayerStore __instance, ref bool __state)
    {
        __state = false;
        if (!AppliesTo(__instance)) return;
        if (_initialDepth != 0) throw new InvalidOperationException("Recursive InitialSave is not a valid run fork");
        CheckNotBlocked(__instance);
        var source = Identity(__instance);
        bool fresh = _freshStore == NativeObject.Handle(__instance) || !Valid(source);
        if (!fresh)
        {
            if (_known is not { } known || known.Store != NativeObject.Handle(__instance) || !Same(known.Identity, source) ||
                NativeObject.Handle((PlayerStore.HasInstance ? PlayerStore.Instance : null)) != NativeObject.Handle(__instance) || __instance.modData == null)
                throw new InvalidOperationException("InitialSave continuation has no verified loaded source");
            var states = new List<(Participant, string)>();
            foreach (var handler in Participants.Values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray())
            {
                if (handler.Ready?.Invoke(__instance) == false)
                    throw new InvalidOperationException("Run-fork participant is busy: " + handler.Owner);
                if (!__instance.modData.ContainsKey(handler.Key)) continue;
                string json = __instance.modData[handler.Key];
                RequireJson(handler.Rebind(json, source, source));
                if (!Same(Identity(__instance), source) || !__instance.modData.ContainsKey(handler.Key) || __instance.modData[handler.Key] != json)
                    throw new InvalidOperationException("Run-fork source changed during validation");
                states.Add((handler, json));
            }
            _attempt = new(__instance, source, states.ToArray());
        }
        _freshStore = IntPtr.Zero; _initialDepth++; __state = true;
    }

    private static void SavePrefix(PlayerStore __instance)
    {
        if (_attempt is not { } attempt || !ReferenceEquals(attempt.Store, __instance)) return;
        var data = __instance.modData;
        var writes = new List<(Participant Handler, string Before, string After)>();
        try
        {
            var destination = Identity(__instance);
            if (!PlayerStore.HasInstance || !ReferenceEquals(PlayerStore.Instance, __instance) || !Valid(destination) ||
                destination.Day != attempt.Source.Day || Same(destination, attempt.Source) || data == null)
                throw new InvalidOperationException("InitialSave did not allocate a usable destination");
            if (attempt.Destination is { } previous)
            {
                if (!Same(destination, previous)) throw new InvalidOperationException("Run-fork destination changed twice");
                return;
            }
            foreach (var (handler, json) in attempt.States)
            {
                if (!data.TryGetValue(handler.Key, out var current) || current != json)
                    throw new InvalidOperationException("Run-fork state changed: " + handler.Key);
                string next = handler.Rebind(json, attempt.Source, destination);
                RequireJson(next); writes.Add((handler, json, next));
            }
            if (!Same(destination, Identity(__instance))) throw new InvalidOperationException("Run-fork native identity changed");
            foreach (var write in writes)
                if (!data.TryGetValue(write.Handler.Key, out var current) || current != write.Before)
                    throw new InvalidOperationException("Run-fork source revision changed");
            foreach (var write in writes) data[write.Handler.Key] = write.After;
            foreach (var write in writes)
                if (data[write.Handler.Key] != write.After) throw new InvalidOperationException("Run-fork staging readback failed");
            foreach (var write in writes) write.Handler.Rebound?.Invoke(__instance);
            if (!Same(destination, Identity(__instance))) throw new InvalidOperationException("Run-fork rebound changed native identity");
            attempt.Destination = destination;
            _known = (NativeObject.Handle(__instance), destination);
            Log($"[INFO] Run fork rebound before save: {attempt.Source.SlotId} -> {destination.SlotId}; states={writes.Count}");
        }
        catch (Exception ex)
        {
            foreach (var write in writes)
                if (data != null && data.TryGetValue(write.Handler.Key, out var current) && current == write.After)
                    data[write.Handler.Key] = write.Before;
            attempt.Destination = null;
            _blockedStore = NativeObject.Handle(__instance);
            Log("[ERROR] Run-fork Mod data staging failed; continuing native save. " + ex);
        }
    }
    private static void WritePostfix(PlayerStore __instance, bool __runOriginal)
    {
        if (__runOriginal && _attempt is { Destination: not null } attempt && ReferenceEquals(attempt.Store, __instance))
            attempt.WriteCompleted = true;
    }
    private static void InitialPostfix(PlayerStore __instance, bool __runOriginal)
    {
        if (!__runOriginal) return;
        if (IsForking(__instance) && (_attempt!.Destination == null || !_attempt.WriteCompleted))
        {
            _blockedStore = NativeObject.Handle(__instance);
            Log("[ERROR] Run-fork Mod state was not confirmed for the destination slot.");
            return;
        }
        MarkLoaded(__instance);
    }
    private static Exception? InitialFinalizer(PlayerStore __instance, bool __state, Exception? __exception)
    {
        if (!__state) return __exception;
        if (__exception != null && _attempt is { } attempt && !Same(Identity(__instance), attempt.Source))
        {
            _blockedStore = NativeObject.Handle(__instance);
            Log("[ERROR] Run fork failed after native allocation; another fork requires reloading the source. " + __exception.Message);
        }
        _attempt = null; _initialDepth = 0;
        return __exception;
    }
    private static void CheckNotBlocked(PlayerStore store)
    {
        if (_blockedStore == NativeObject.Handle(store)) throw new InvalidOperationException("Failed run fork requires reloading its source save");
    }
    private static void RequireJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1048576) throw new InvalidDataException("Invalid run-fork JSON size");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Run-fork state must be an object");
    }
    private static void Log(string message) { try { _log?.Invoke(message); } catch { } }
}
