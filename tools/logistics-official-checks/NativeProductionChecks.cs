using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using DseCecil;
using HarmonyLib;
using Nicokobo.ContentInterop;

internal static class NativeProductionChecks
{
    private sealed class NativeClosure
    {
        internal int Calls;
        internal Action? Work;
        internal void Produce(GameItem item, GameInventory parent, SlotMarker slot)
        { Calls++; HopperHelper.Trace.Add("native"); Work?.Invoke(); }
    }
    private static void Reject(Action work, Action<bool, string> check, string reason)
    {
        try { work(); } catch (InvalidOperationException) { check(true, reason); return; }
        check(false, reason);
    }
    internal static void Run(Action<bool, string> check)
    {
        var priorStore = PlayerStore.Instance;
        bool priorPower = CurrentConverterHelper.isNightCycle;
        int priorDay = StoreStation.Day;
        // The game's Unity build of Harmony references Mono-only SRE APIs.
        // Its bundled Cecil generator can patch the offline CoreCLR ports.
        var priorGenerator = AppContext.GetData("MonoMod.DMDType");
        AppContext.SetData("MonoMod.DMDType", "cecil");
        const string patchSerialization = "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization";
        AppContext.TryGetSwitch(patchSerialization, out bool priorSerialization);
        // Unity Harmony serializes only its own in-process patch metadata.
        AppContext.SetSwitch(patchSerialization, true);
        var harmony = new Harmony("checks.forge.production");
        try
        {
            // Execute both real transpilers and the real scheduler/observer/
            // power services. Only the game loop and inventories are ports.
            harmony.CreateClassProcessor(typeof(NativeProductionCyclePatch)).Patch();
            harmony.CreateClassProcessor(typeof(NightQueueLifetimePatch)).Patch();
            Reject(() => NightQueueLifetimePatch.Transpiler(Array.Empty<CodeInstruction>()).ToArray(), check,
                "A changed native night loop must reject dispatch installation");
            Reject(() => NativeProductionCyclePatch.Transpiler(Array.Empty<CodeInstruction>()).ToArray(), check,
                "A changed production Invoke must reject dispatch installation");

            MachineSchedule.Set("checks.synthesis", "refiner", 10);
            MachineSchedule.Set("checks.synthesis", "bench", 20);
            MachineSchedule.Set("checks.forge", "terminal", 50);
            GameItem Machine(string id, int uid)
            {
                var item = new GameItem { identifier = id, Id = uid };
                item.onCycleEndSlotItemFunc = (_, _, _) =>
                {
                    check(CurrentConverterHelper.isNightCycle, "Native night-power semantics must survive sorting");
                    HopperHelper.Trace.Add(id + uid);
                };
                return item;
            }
            var terminal = Machine("terminal", 20);
            var refiner = Machine("refiner", 30);
            var benchLater = Machine("bench", 11);
            var bench = Machine("bench", 10);
            var closure = new NativeClosure();
            var furnace = new GameItem { identifier = "furnace", Id = 90, onCycleEndSlotItemFunc = closure.Produce };
            furnace.onCycleEndLateSlotItemFunc = (_, _, _) => HopperHelper.Trace.Add("late");
            var original = furnace.onCycleEndSlotItemFunc;
            using var nativeLease = NativeMachines.Subscribe("checks.native", machine =>
            {
                HopperHelper.Trace.Add("before");
                return success => HopperHelper.Trace.Add("after:" + success);
            });
            PlayerStore.Instance = new PlayerStore { Items = new() { terminal, benchLater, refiner, bench, bench, furnace } };
            HopperHelper.Trace.Clear(); CurrentConverterHelper.isNightCycle = false;
            PlayerStore.Instance.EndNight();
            check(HopperHelper.Trace.SequenceEqual(new[] { "hopper-1", "before", "native", "after:True", "refiner30", "bench10", "bench10", "bench11", "terminal20", "hopper-2", "late", "hopper-3" }),
                "Actual native opportunities must sort before the second hopper window and preserve Late");
            check(bench.StolenUpdates == 2 && furnace.StolenUpdates == 1 && terminal.StolenUpdates == 1,
                "Dispatch must preserve native iterator bookkeeping once per opportunity");
            check(ReferenceEquals(original, furnace.onCycleEndSlotItemFunc) && ReferenceEquals(furnace.onCycleEndSlotItemFunc!.Target, closure),
                "Forge must preserve the callback object and the native Target used by other mods");
            check(!CurrentConverterHelper.isNightCycle, "EndNight must restore the entering power mode");

            HopperHelper.Trace.Clear(); NativeMachines.Process(furnace);
            check(HopperHelper.Trace.SequenceEqual(new[] { "before", "native", "after:True" }) && closure.Calls == 2,
                "Manual work must use the same single before/work/after boundary");
            check(ReferenceEquals(original, furnace.onCycleEndSlotItemFunc), "Manual dispatch must not replace the callback");
            furnace.onCycleEndEarlySlotItemFunc = (_, _, _) => HopperHelper.Trace.Add("early");
            DebugPanel.Target = furnace; PlayerStore.Instance.Items = new() { furnace };
            var tool = new ToolDirectory().CreateTurboBooster();
            foreach (var (run, cycles, early, late) in new (Action, int, int, int)[] {
                (() => MachineTurboBoosterAdv.BoostMachine(furnace), 1, 1, 0),
                (() => tool.onThisTargetItemFunc!(tool, furnace), 1, 1, 0),
                (DebugPanel.SpawnGunKit, 1, 0, 0), (DebugPanel.SpawnHydroponicKit, 7, 0, 0),
                (DebugPanel.SkipNightMachine, 1, 1, 1) })
            {
                HopperHelper.Trace.Clear(); run();
                check(HopperHelper.Trace.Count(row => row == "native") == cycles &&
                    HopperHelper.Trace.Count(row => row == "before") == cycles && HopperHelper.Trace.Count(row => row == "after:True") == cycles,
                    "Native manual callers must retain every legitimate cycle and run Forge hooks exactly once");
                check(HopperHelper.Trace.Count(row => row == "early") == early && HopperHelper.Trace.Count(row => row == "late") == late,
                    "Manual production redirection must preserve separate Early and Late callbacks");
            }
            furnace.onCycleEndEarlySlotItemFunc = null;
            check(ReferenceEquals(original, furnace.onCycleEndSlotItemFunc), "Boosting and debug work must preserve the machine callback");
            using (NativeMachines.Subscribe("checks.veto", _ => null))
            {
                HopperHelper.Trace.Clear(); PlayerStore.Instance.Items = new() { furnace }; PlayerStore.Instance.EndNight();
                check(!HopperHelper.Trace.Contains("native") && HopperHelper.Trace.Count(row => row == "after:False") == 1 && furnace.StolenUpdates == 2,
                    "A veto must cancel production, close earlier observers and retain native bookkeeping");
            }

            // Real Forge power receipts are opened by the dispatcher and
            // exercise their actual commit/restore/dispose paths.
            var source = new GameItem();
            source.Tags["power_source_item"] = new();
            source.Tags["power_source_item_max_energy"] = new() { valueInt = 100 };
            var battery = new GameSlotInventory { Parent = furnace, childItem = source };
            source.parentInventory = battery; furnace.Body[0] = battery;
            int energy = 100, restored = 0, disposed = 0; bool ledgerBusy = false;
            using var powerLease = ForgePower.RegisterMachine("checks.power", "checks.power", _ => energy, (_, cost) =>
            {
                if (ledgerBusy) return null;
                ledgerBusy = true;
                int before = energy;
                return new object[] { (Func<bool>)(() => energy == before && energy >= cost),
                    (Action)(() => energy -= cost), (Func<bool>)(() => energy == before - cost),
                    (Func<bool>)(() => { energy = before; restored++; return true; }),
                    (Action<string>)(reason => throw new InvalidOperationException(reason)), (Action)(() => { disposed++; ledgerBusy = false; }) };
            });
            closure.Work = () => check(DevicePower.TryDebit(source, 7, out bool paid) && paid,
                "A recognized machine source must debit through the actual power provider");
            NativeMachines.Process(furnace);
            check(energy == 93 && restored == 0 && disposed == 1, "Successful work must settle energy exactly once");
            closure.Work = () =>
            {
                check(DevicePower.TryDebit(source, 7, out bool paid) && paid, "Failing work must first perform a real energy debit");
                throw new InvalidOperationException("native-work-failure");
            };
            HopperHelper.Trace.Clear();
            Reject(() => NativeMachines.Process(furnace), check, "Manual production exceptions must propagate");
            check(energy == 93 && restored == 1 && disposed == 2 && HopperHelper.Trace.Last() == "after:False",
                "A failed callback must restore energy and notify observers once");
            Reject(PlayerStore.Instance.EndNight, check, "Night production exceptions must propagate");
            check(energy == 93 && restored == 2 && disposed == 3 && !CurrentConverterHelper.isNightCycle,
                "Night failure must restore power receipts and the native power-mode flag");
            closure.Work = null; PlayerStore.Instance.Items.Clear();
            int callsAfterFailure = closure.Calls;
            PlayerStore.Instance.EndNight();
            check(closure.Calls == callsAfterFailure, "A failed pass must not replay work on the next night");
            NativeMachines.RegisterBatch("checks.batch", "furnace", (_, next) => { next(); next(); });
            energy = 100; restored = disposed = 0;
            closure.Work = () => check(DevicePower.TryDebit(source, 7, out bool paid) && paid,
                "Each native batch must acquire a fresh exclusive power receipt");
            HopperHelper.Trace.Clear(); NativeMachines.Process(furnace);
            check(energy == 86 && disposed == 2 && !ledgerBusy && HopperHelper.Trace.Count(row => row == "before") == 2 &&
                HopperHelper.Trace.Count(row => row == "after:True") == 2 && ReferenceEquals(original, furnace.onCycleEndSlotItemFunc),
                "Batch dispatch must settle power and provider callbacks between batches without replacing the machine delegate");
            energy = 100; restored = disposed = 0; int batchIndex = 0;
            closure.Work = () =>
            {
                check(DevicePower.TryDebit(source, 7, out bool paid) && paid, "A later batch must draw through its own power receipt");
                if (++batchIndex == 2) throw new InvalidOperationException("second-batch-failure");
            };
            Reject(() => NativeMachines.Process(furnace), check, "A later batch failure must propagate");
            check(energy == 93 && restored == 1 && disposed == 2 && !ledgerBusy,
                "A later failure must restore only that batch and retain the earlier completed batch's debit");
            NativeMachines.Remove("checks.batch"); closure.Work = null;
            HopperHelper.Trace.Clear(); NativeMachines.Process(furnace);
            check(HopperHelper.Trace.Count(row => row == "before") == 1, "Detaching a batch owner must restore single-cycle dispatch");
            DevicePower.Remove("checks.power");

            // Keep the old queue's run/slot/parent/ownership protections at
            // the new, earlier flush boundary.
            foreach (var invalidate in new Action<GameItem>[] {
                _ => PlayerStore.Instance = new PlayerStore { runID = "other-run", saveSlotId = 3 },
                item => item.parentInventory = new(), item => item.Owned = false,
                _ => StoreStation.Day++ })
            {
                int produced = 0;
                var queued = new GameItem { identifier = "terminal", Id = 40 };
                queued.onCycleEndSlotItemFunc = (_, _, _) => produced++;
                var mutation = new GameItem { identifier = "mutation" };
                mutation.onCycleEndSlotItemFunc = (_, _, _) => invalidate(queued);
                PlayerStore.Instance = new PlayerStore { Items = new() { queued, mutation } };
                PlayerStore.Instance.EndNight();
                check(produced == 0, "A queued opportunity must keep its original run, day, owner and physical parent");
            }
            int abandoned = 0;
            var delayed = new GameItem { identifier = "terminal" };
            delayed.onCycleEndSlotItemFunc = (_, _, _) => abandoned++;
            var failBeforeFlush = new GameItem { identifier = "failure" };
            failBeforeFlush.onCycleEndSlotItemFunc = (_, _, _) => throw new InvalidOperationException("before-flush");
            PlayerStore.Instance = new PlayerStore { Items = new() { delayed, failBeforeFlush } };
            Reject(PlayerStore.Instance.EndNight, check, "A failure before the sorted flush must propagate");
            PlayerStore.Instance.Items.Clear(); PlayerStore.Instance.EndNight();
            check(abandoned == 0, "Interrupted collection must not leak pending opportunities");
            var recursive = new GameItem { identifier = "recursive" };
            recursive.onCycleEndSlotItemFunc = (_, _, _) =>
            {
                Reject(PlayerStore.Instance.EndNight, check, "Recursive EndNight must be rejected");
                Reject(PlayerStore.Instance.EndNight, check, "Rejected recursion must not release the outer guard");
            };
            PlayerStore.Instance.Items = new() { recursive }; PlayerStore.Instance.EndNight();
            check(!CurrentConverterHelper.isNightCycle, "Rejected recursion must preserve the outer power mode");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            NativeProductionOrder.Reset(); MachineSchedule.Remove("checks.synthesis"); MachineSchedule.Remove("checks.forge");
            DevicePower.Remove("checks.power"); PlayerStore.Instance = priorStore; CurrentConverterHelper.isNightCycle = priorPower;
            StoreStation.Day = priorDay; AppContext.SetData("MonoMod.DMDType", priorGenerator);
            AppContext.SetSwitch(patchSerialization, priorSerialization);
        }
    }

    internal static void InspectGame(string managed, string forgePath, Action<bool, string> check)
    {
        // Run the built Forge's own IL selectors/transpilers against the real
        // game bodies. This reads managed metadata; it never calls EndNight.
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            string path = Path.Combine(Path.GetFullPath(managed), name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var forge = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(forgePath));
            var site = forge.GetType("Nicokobo.ContentInterop.NativeProductionSite", true)!;
            var callback = (MethodBase)site.GetMethod("Callback", BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null)!.Invoke(null, null)!;
            check(callback.DeclaringType!.Assembly.GetName().Name == "Assembly-CSharp", "The selector must bind to the actual game iterator");
            var endNight = callback.DeclaringType.Assembly.GetType("PlayerStore", true)!.GetMethod("EndNight")!;
            var cyclePatch = forge.GetType("Nicokobo.ContentInterop.NativeProductionCyclePatch", true)!;
            var targets = ((IEnumerable<MethodBase>)cyclePatch.GetMethod("TargetMethods", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!).ToArray();
            foreach (var (patchName, target, dispatch) in targets.Select(target => ("NativeProductionCyclePatch", target, "Invoke"))
                .Append(("NightQueueLifetimePatch", (MethodBase)endNight, "RunNight")))
            {
                var instructions = PatchProcessor.ReadMethodBody(target).Select(row => new CodeInstruction(row.Key, row.Value)).ToList();
                int expected = dispatch == "RunNight" ? 1 : instructions.Count(row => row.operand is MethodInfo method && method.Name == "get_onCycleEndSlotItemFunc");
                int allInvokes = instructions.Count(row => row.operand is MethodInfo method && method.DeclaringType?.Name == "Action`3" && method.Name == "Invoke");
                var patch = forge.GetType("Nicokobo.ContentInterop." + patchName, true)!;
                var result = ((IEnumerable<CodeInstruction>)patch.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { instructions })!).ToArray();
                check(expected > 0 && result.Count(row => row.operand is MethodInfo method && method.DeclaringType?.FullName == "Nicokobo.ContentInterop.NativeProductionOrder" && method.Name == dispatch) == expected,
                    "Built Forge must redirect exactly the intended real game call site: " + dispatch);
                if (dispatch == "Invoke")
                    check(result.Count(row => row.operand is MethodInfo method && method.DeclaringType?.Name == "Action`3" && method.Name == "Invoke") == allInvokes - expected,
                        "The built patch must retain real game Early/Late invocations: " + target.Name);
            }
            using var assembly = AssemblyDefinition.ReadAssembly(forgePath);
            check(!assembly.MainModule.Types.Any(type => type.Name is "NativeMachineFactories" or "ProductionOrderFactoryPatch"),
                "Forge must not reinstall factory-time callback wrappers");
            foreach (var type in assembly.MainModule.Types.Where(type => type.Name is "NativeMachines" or "NativeProductionOrder"))
                check(!type.Methods.Where(method => method.HasBody).SelectMany(method => method.Body.Instructions)
                    .Any(row => row.Operand is MethodReference method && method.Name == "set_onCycleEndSlotItemFunc"),
                    "Dispatch must never write the real game callback property: " + type.Name);
            using var game = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"));
            var callers = AllTypes(game.MainModule.Types).SelectMany(type => type.Methods).Where(method => method.HasBody &&
                method.Body.Instructions.Any(row => row.Operand is MethodReference member && member.Name == "get_onCycleEndSlotItemFunc") &&
                method.Body.Instructions.Any(row => row.Operand is MethodReference member && member.Name == "Invoke" &&
                    member.DeclaringType is GenericInstanceType action && action.ElementType.FullName == "System.Action`3" &&
                    action.GenericArguments.Select(type => type.FullName).SequenceEqual(new[] { "GameItem", "GameInventory", "SlotMarker" }))).ToArray();
            check(game.MainModule.Mvid == callback.Module.ModuleVersionId && callers.Select(method => method.MetadataToken.ToInt32()).ToHashSet().SetEquals(targets.Select(method => method.MetadataToken)),
                "Every native game production invocation must reach the patched iterator; found: " + string.Join(", ", callers.Select(method => method.FullName)));
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) =>
        types.SelectMany(type => new[] { type }.Concat(AllTypes(type.NestedTypes)));
}
