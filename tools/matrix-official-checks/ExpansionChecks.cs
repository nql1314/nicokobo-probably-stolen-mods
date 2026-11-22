using HarmonyLib;
using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.ModuleMatrix.Definitions;
using MechcoreProtocol.ModuleMatrix.Domain;

internal static class ExpansionChecks
{
    // The shipped default and upgrade masks are native inputs, not Matrix rules.
    private const string Initial = "10000010001000001110000010001000001";
    private const string First = "10000010000000000000000000001000001";
    private const string Second = "00000000000000000000000000000000000";

    private static (GameItem Machine, GameGridInventory Bay) Host(string id = "expansion_checks")
    {
        PlayerStore.Current = new();
        var machine = new GameItem { identifier = id };
        if (id != "evaporator") machine.EnableTag("STANDARD_MACHINE_TAG");
        var bay = new GameGridInventory { Parent = machine, inventoryShape = new(Initial, 7) };
        machine.Storages[1] = bay;
        PlayerStore.Instance.Items.Add(machine);
        return (machine, bay);
    }

    private static GameItem Source(GameGridInventory bay, int x, int y)
    {
        var source = new GameItem { identifier = "native_expansion_checks" };
        ModuleHelper.InitModuleItem(source, "MODULE_TYPE_UNIVERSAL", 100);
        bay.Add(source, x, y);
        return source;
    }

    internal static void Run(Action<bool, string> check)
    {
        var patch = typeof(MatrixMachines).Assembly.GetType("MechcoreProtocol.ModuleMatrix.MatrixExpansionPatch");
        check(patch != null, "Native expansion has no Matrix recomputation patch");
        var binding = patch!.GetCustomAttributes(typeof(HarmonyPatch), false).Cast<HarmonyPatch>().Single().info;
        check(binding.declaringType == typeof(MachineHelper) && binding.methodName == nameof(MachineHelper.ExpandModule),
            "Expansion recomputation is not bound to the native successful expansion entry");

        // Drive the real patch around the native operation's observed shape/stage boundary.
        void Expand(GameItem machine, GameGridInventory bay, int nextStage, string shape)
        {
            object?[] before = { machine, null };
            Ports.Hook(patch, "Prefix", before);
            bay.inventoryShape = new(shape, 7);
            machine.ModifyTag("MODULE_UPGRADE_STAGE_INT", tag => tag.SetInt(nextStage));
            Ports.Hook(patch, "Postfix", bay, machine, before[1]);
        }

        var cases = new[]
        {
            (PlacementEffectDefinitions.E01, 1, 0, 130, 130, 100),
            (PlacementEffectDefinitions.E03, 2, 1, 108, 116, 116),
            (PlacementEffectDefinitions.E04, 1, 1, 110, 110, 110),
            (PlacementEffectDefinitions.E06, 1, 1, 150, 150, 150),
            (PlacementEffectDefinitions.E07, 0, 3, 120, 120, 100),
            (PlacementEffectDefinitions.E08, 5, 0, 120, 120, 100)
        };
        foreach (var (effect, x, y, initial, first, second) in cases)
        {
            var host = Host();
            var source = Source(host.Bay, x, y);
            ModuleEffectHelper.AddEffectToModule(source, effect.Id);
            if (effect == PlacementEffectDefinitions.E04) Source(host.Bay, 1, 0);
            var items = host.Bay.childItems.ToArray();
            var positions = items.Select(item => item.modifiedShape).ToArray();
            MatrixMachines.Recompute(host.Machine);
            check(MatrixEffects.Temp(source).Performance == initial, "Initial native bay fixture changed: " + effect.Code);
            for (int stage = 1; stage <= 2; stage++)
            {
                MatrixEffects.WriteTemp(source, new(-999, 0, 0));
                Expand(host.Machine, host.Bay, stage, stage == 1 ? First : Second);
                check(MatrixEffects.Temp(source).Performance == (stage == 1 ? first : second),
                    "Expansion did not immediately refresh placement effect " + effect.Code + " at stage " + stage);
                check(host.Bay.childItems.SequenceEqual(items) && items.Select(item => item.modifiedShape).SequenceEqual(positions),
                    "Expansion recomputation moved or reordered installed modules: " + effect.Code);
                var expected = items.Select(MatrixEffects.Temp).ToArray();
                int aggregate = host.Machine.GetTagReadonly("TOTAL_PERFORMANCE").GetInt();
                ModHook.Loaded();
                check(items.Select(MatrixEffects.Temp).SequenceEqual(expected) && host.Machine.GetTagReadonly("TOTAL_PERFORMANCE").GetInt() == aggregate,
                    "Immediate expansion and loaded recomputation disagree: " + effect.Code);
                MatrixMachines.Recompute(host.Machine);
                check(items.Select(MatrixEffects.Temp).SequenceEqual(expected), "Repeated expansion recomputation compounded: " + effect.Code);
            }
        }

        var nodes = Host();
        var nodeSource = Source(nodes.Bay, 1, 2);
        ModuleEffectHelper.AddEffectToModule(nodeSource, PlacementEffectDefinitions.E03.Id);
        var earlier = DirectoryMaster.Item(NodeItemDefinitions.N09.Id);
        nodes.Bay.Add(earlier, 0, 2); MatrixEffects.Arriving(earlier, nodes.Bay);
        var later = DirectoryMaster.Item(NodeItemDefinitions.N11.Id);
        nodes.Bay.Add(later, 1, 1); MatrixEffects.Arriving(later, nodes.Bay);
        int earlyOrder = earlier.GetTagReadonly(MatrixEffects.ArrivalTag).GetInt();
        int laterOrder = later.GetTagReadonly(MatrixEffects.ArrivalTag).GetInt();
        nodes.Bay.childItems.Reverse();
        for (int stage = 1; stage <= 2; stage++)
        {
            Expand(nodes.Machine, nodes.Bay, stage, stage == 1 ? First : Second);
            check(MatrixEffects.Temp(earlier) == new NodeStats(-108, 54, 27) && MatrixEffects.Temp(later) == default &&
                earlier.GetTagReadonly(MatrixEffects.ArrivalTag).GetInt() == earlyOrder && later.GetTagReadonly(MatrixEffects.ArrivalTag).GetInt() == laterOrder,
                "Expansion changed persisted node arrival priority");
            foreach (var item in nodes.Bay.childItems)
                item.state = Newtonsoft.Json.JsonConvert.DeserializeObject<TagSystem>(Newtonsoft.Json.JsonConvert.SerializeObject(item.state))!;
            ModHook.Loaded();
            check(MatrixEffects.Temp(earlier) == new NodeStats(-108, 54, 27) && MatrixEffects.Temp(later) == default,
                "Node results after expansion differ from state reload");
        }
        MatrixEffects.WriteTemp(nodeSource, new(-999, 0, 0));
        Expand(nodes.Machine, nodes.Bay, 3, Second);
        check(MatrixEffects.Temp(nodeSource).Performance == -999, "An unsupported third expansion triggered recomputation");
        nodes.Machine.ModifyTag("MODULE_UPGRADE_STAGE_INT", tag => tag.SetInt(0));
        Expand(nodes.Machine, nodes.Bay, 0, Second);
        check(MatrixEffects.Temp(nodeSource).Performance == -999, "A cancelled expansion triggered recomputation");

        var evaporator = Host("evaporator");
        check(MatrixMachines.Modules(evaporator.Machine) == evaporator.Bay && !evaporator.Machine.IsTag("STANDARD_MACHINE_TAG"),
            "Evaporator recognition requires a fabricated standard-machine tag");
        var growing = DirectoryMaster.Item(ContinuousModuleDefinitions.M08.Module.Id);
        evaporator.Bay.inventoryShape = new(Second, 7); evaporator.Bay.Add(growing, 1, 0);
        ModuleEffectHelper.ActivateOnUseAction(growing, evaporator.Bay, evaporator.Machine);
        ModHook.Loaded();
        check(growing.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt() == 12 && MatrixEffects.Temp(growing).Performance == 12,
            "Evaporator work and loaded recomputation lost continuous growth");
        StoreStation.Day++;
        MatrixGrowth.Decay();
        check(growing.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt() == 8 && MatrixEffects.Temp(growing).Performance == 8,
            "Evaporator idle decay failed to recompute its installed modules");
    }
}
