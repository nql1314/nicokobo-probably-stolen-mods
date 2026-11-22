using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Nicokobo.ContentInterop;

// Collect only callbacks invoked by the native middle pass and finish before
// RunWindow(2). Machine delegates and their native Targets stay unchanged.
internal static class NativeProductionOrder
{
    private sealed record Opportunity(GameItem Machine, Action<GameItem, GameInventory, SlotMarker> Work,
        GameInventory Parent, SlotMarker Slot, PlayerStore Store, string Run, int SaveSlot, int Day, bool NightPower, int Order);
    private static readonly List<Opportunity> Pending = new();
    private static bool _collecting;
    [ThreadStatic] private static bool _nativeNight;
    [ThreadStatic] private static bool _previousNightPower;
    internal static int Level(string id) => MachineSchedule.Order(id);
    internal static void RunNight(List<GameItem> items, Action<GameItem> nativeStep)
    {
        Pending.Clear(); _collecting = true;
        try
        {
            // Preserve native bookkeeping and any patches on the iterator.
            foreach (var item in items) nativeStep(item);
            _collecting = false;
            Finish();
        }
        finally { Reset(); }
    }
    internal static void Invoke(Action<GameItem, GameInventory, SlotMarker> cycle, GameItem machine, GameInventory parent, SlotMarker slot)
    {
        var store = PlayerStore.Instance;
        if (!_collecting || store == null || Level(machine.identifier) <= 0)
        { NativeMachines.Invoke(cycle, machine, parent, slot); return; }
        Pending.Add(new(machine, cycle, parent, slot, store, store.runID, store.saveSlotId,
            StoreStation.GetDayCounter(), CurrentConverterHelper.isNightCycle, Pending.Count));
    }
    internal static void Reset() { _collecting = false; Pending.Clear(); }
    internal static void EnterNativeNight()
    {
        if (_nativeNight) throw new InvalidOperationException("A native night is already being processed");
        _previousNightPower = CurrentConverterHelper.isNightCycle; _nativeNight = true;
    }
    internal static void LeaveNativeNight()
    { CurrentConverterHelper.isNightCycle = _previousNightPower; _nativeNight = false; Reset(); }
    private static void Finish()
    {
        var opportunities = Pending.OrderBy(work => Level(work.Machine.identifier)).ThenBy(work => work.Machine.GetUniqueID()).ThenBy(work => work.Order).ToArray();
        Pending.Clear();
        foreach (var work in opportunities)
        {
            var store = PlayerStore.Instance;
            if (store != work.Store || store.runID != work.Run || store.saveSlotId != work.SaveSlot || StoreStation.GetDayCounter() != work.Day ||
                work.Machine.parentInventory != work.Parent || !GeneralHelper.IsItemOwned(work.Machine)) continue;
            bool previous = CurrentConverterHelper.isNightCycle;
            try { CurrentConverterHelper.isNightCycle = work.NightPower; NativeMachines.Invoke(work.Work, work.Machine, work.Parent, work.Slot); }
            finally { CurrentConverterHelper.isNightCycle = previous; }
        }
    }
}

internal static class NativeProductionSite
{
    internal static (int Loop, MethodInfo Callback) Find(IReadOnlyList<CodeInstruction> code)
    {
        var window = AccessTools.Method(typeof(HopperHelper), nameof(HopperHelper.RunWindow), new[] { typeof(int) });
        int before = -1, after = -1;
        for (int i = 1; i < code.Count; i++)
        {
            if (!code[i].Calls(window)) continue;
            if (code[i - 1].LoadsConstant(1))
            { if (before != -1) throw new InvalidOperationException("Duplicate native pre-production hopper window"); before = i; }
            if (code[i - 1].LoadsConstant(2))
            { if (after != -1) throw new InvalidOperationException("Duplicate native post-production hopper window"); after = i; }
        }
        if (before < 0 || after <= before) throw new InvalidOperationException("Native production hopper windows changed");
        var loops = new List<int>();
        var callbacks = new List<MethodInfo>();
        for (int i = before + 1; i < after; i++)
        {
            if (code[i].operand is not MethodInfo method) continue;
            if (code[i].Calls(method) && method.DeclaringType == typeof(List<GameItem>) && method.Name == nameof(List<GameItem>.ForEach)) loops.Add(i);
            if (code[i].Is(OpCodes.Ldftn, method)) callbacks.Add(method);
        }
        if (loops.Count != 1 || callbacks.Count != 1)
            throw new InvalidOperationException("Native production iterator changed");
        return (loops[0], callbacks[0]);
    }
    internal static List<CodeInstruction> Read(MethodBase method) => PatchProcessor.ReadMethodBody(method)
        .Select(instruction => new CodeInstruction(instruction.Key, instruction.Value)).ToList();
    internal static bool IsProductionGetter(CodeInstruction instruction) => instruction.operand is MethodInfo method && instruction.Calls(method) &&
        method.Name == "get_" + nameof(GameItem.onCycleEndSlotItemFunc) && method.ReturnType == typeof(Action<GameItem, GameInventory, SlotMarker>);
    internal static MethodBase Callback() => Find(Read(AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.EndNight)))).Callback;
    internal static MethodBase Callback(MethodBase factory)
    {
        var callbacks = Read(factory).Where(instruction => instruction.operand is MethodInfo method && instruction.Is(OpCodes.Ldftn, method))
            .Select(instruction => (MethodInfo)instruction.operand).Distinct().Where(method => Read(method).Any(IsProductionGetter)).ToArray();
        if (callbacks.Length != 1) throw new InvalidOperationException("Native manual production iterator changed: " + factory.Name);
        return callbacks[0];
    }
}

[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.EndNight))]
internal static class NightQueueLifetimePatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(out bool __state)
    { __state = false; NativeProductionOrder.EnterNativeNight(); __state = true; }
    private static Exception? Finalizer(bool __state, Exception? __exception)
    { if (__state) NativeProductionOrder.LeaveNativeNight(); return __exception; }
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        int loop = NativeProductionSite.Find(code).Loop;
        code[loop].opcode = OpCodes.Call;
        code[loop].operand = AccessTools.Method(typeof(NativeProductionOrder), nameof(NativeProductionOrder.RunNight));
        return code;
    }
}

// Resolve the compiler's iterator from EndNight's actual ldftn operand rather
// than assuming a DisplayClass name or inspecting a machine callback Target.
[HarmonyPatch]
internal static class NativeProductionCyclePatch
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return NativeProductionSite.Callback();
        yield return AccessTools.Method(typeof(MachineTurboBoosterAdv), nameof(MachineTurboBoosterAdv.BoostMachine));
        yield return NativeProductionSite.Callback(AccessTools.Method(typeof(ToolDirectory), "CreateTurboBooster"));
        yield return AccessTools.Method(typeof(DebugPanel), nameof(DebugPanel.SpawnGunKit));
        yield return AccessTools.Method(typeof(DebugPanel), nameof(DebugPanel.SpawnHydroponicKit));
        yield return NativeProductionSite.Callback(AccessTools.Method(typeof(DebugPanel), nameof(DebugPanel.SkipNightMachine)));
    }
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        bool production = false;
        int getters = 0, redirected = 0;
        foreach (var instruction in code)
        {
            if (instruction.operand is not MethodInfo method || !instruction.Calls(method)) continue;
            if (method.ReturnType == typeof(Action<GameItem, GameInventory, SlotMarker>))
            { production = NativeProductionSite.IsProductionGetter(instruction); if (production) getters++; }
            if (method.DeclaringType != typeof(Action<GameItem, GameInventory, SlotMarker>) || method.Name != nameof(Action.Invoke)) continue;
            if (production)
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(NativeProductionOrder), nameof(NativeProductionOrder.Invoke));
                redirected++;
            }
            production = false;
        }
        if (getters == 0 || redirected != getters) throw new InvalidOperationException("Native production callback invocation changed");
        return code;
    }
}
