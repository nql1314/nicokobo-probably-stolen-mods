using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.ModuleMatrix.Definitions;
using Nicokobo.ContentInterop;

internal static class TurboChecks
{
    private static void Reset()
    {
        MatrixTurbo.Reset(); PlayerStore.Current = new(); StoreStation.Day = 25;
    }
    private static GameItem Implant(int count = 1, bool owned = true)
    {
        var item = new GameItem { identifier = MatrixTurbo.CognitiveTurbo, unitCount = count, Owned = owned };
        PlayerStore.Instance.Items.Add(item); return item;
    }
    private static (GameItem Machine, GameGridInventory Bay) Host()
    {
        var machine = new GameItem { identifier = "turbo_test_machine", onCycleEndSlotItemFunc = (_, _, _) => { } };
        machine.EnableTag("STANDARD_MACHINE_TAG");
        var bay = new GameGridInventory { Parent = machine }; machine.Storages[1] = bay;
        PlayerStore.Instance.Items.Add(machine); return (machine, bay);
    }
    private static string Tag(ContinuousModuleDefinition definition) => "BONUS_PERCENTAGE_" + definition.Attribute + "_INT";
    private static GameItem Booster(string id = "turbo_booster") => new() { identifier = id };
    internal static void Run(Action<bool, string> check)
    {
        Reset(); var empty = Implant(0); var host = Host();
        check(!MatrixTurbo.HasImplant() && MatrixTurbo.Begin(Booster(), host.Machine) == null,
            "Zero-quantity Cognitive Turbo enabled a boost scope");
        empty.unitCount = 1; empty.Owned = false;
        check(!MatrixTurbo.HasImplant(), "Unowned Cognitive Turbo enabled growth");
        empty.Owned = true;
        check(MatrixTurbo.HasImplant() && MatrixTurbo.TotalCap(ContinuousModuleDefinitions.M08) == 48 &&
            MatrixTurbo.TotalCap(ContinuousModuleDefinitions.M11) == 24, "Cognitive Turbo total caps differ from legacy values");

        foreach (var definition in ContinuousModuleDefinitions.All)
        {
            Reset(); var implant = Implant(); host = Host();
            var module = DirectoryMaster.Item(definition.Module.Id); host.Bay.Add(module, 2, 1);
            for (int i = 0; i < 50; i++) MatrixGrowth.Work(module, host.Bay, host.Machine);
            int expected = definition.Cap * 3 / 2;
            check(module.GetTagReadonly(Tag(definition)).GetInt() == expected, "Augmented continuous cap failed: " + definition.Module.Code);
            implant.Owned = false; MatrixGrowth.Work(module, host.Bay, host.Machine);
            check(module.GetTagReadonly(Tag(definition)).GetInt() == expected && MatrixTurbo.TotalCap(definition) == definition.Cap,
                "Losing the implant discarded earned growth or retained the raised cap");
            StoreStation.Day++; MatrixGrowth.Decay();
            check(module.GetTagReadonly(Tag(definition)).GetInt() == expected - definition.Step,
                "Above-normal earned growth did not decay after losing the implant");
        }

        foreach (string toolId in new[] { "turbo_booster", "turbo_booster_adv", "turbo_booster_hypertech" })
        foreach (bool adjacent in new[] { false, true })
        {
            Reset(); Implant(); host = Host(); var definition = ContinuousModuleDefinitions.M08;
            var module = DirectoryMaster.Item(definition.Module.Id); host.Bay.Add(module, 2, 1);
            if (adjacent) host.Bay.Add(DirectoryMaster.Item(NodeItemDefinitions.N16.Id), 1, 1);
            var scope = MatrixTurbo.Begin(Booster(toolId), host.Machine);
            MatrixGrowth.Work(module, host.Bay, host.Machine); MatrixTurbo.End(scope, true); MatrixTurbo.End(scope, true);
            int expected = definition.Initial + definition.Step * (adjacent ? 4 : 2);
            check(module.GetTagReadonly(Tag(definition)).GetInt() == expected,
                "Completed turbo work did not top up once with the learning-node multiplier: " + toolId);
        }
        Reset(); Implant(); host = Host(); var continuous = ContinuousModuleDefinitions.M08;
        var working = DirectoryMaster.Item(continuous.Module.Id); host.Bay.Add(working, 2, 1);
        var multi = MatrixTurbo.Begin(Booster(), host.Machine);
        for (int i = 0; i < 3; i++) MatrixGrowth.Work(working, host.Bay, host.Machine);
        MatrixTurbo.End(multi, true);
        check(working.GetTagReadonly(Tag(continuous)).GetInt() == continuous.Initial + 3 * continuous.Step,
            "Turbo work duplicated growth already supplied by multiple real batches");

        foreach (string mode in new[] { "rejected", "exception", "removed", "run", "implant" })
        {
            Reset(); var implant = Implant(); host = Host(); working = DirectoryMaster.Item(continuous.Module.Id); host.Bay.Add(working, 2, 1);
            var scope = MatrixTurbo.Begin(Booster(), host.Machine);
            if (mode != "rejected") MatrixGrowth.Work(working, host.Bay, host.Machine);
            if (mode == "removed") host.Bay.Expel(working);
            if (mode == "run") PlayerStore.Instance.runID = "different-run";
            if (mode == "implant") implant.Owned = false;
            MatrixTurbo.End(scope, mode != "exception");
            check(working.GetTagReadonly(Tag(continuous)).GetInt() == continuous.Initial + (mode == "rejected" ? 0 : continuous.Step),
                "Invalid/rejected turbo work granted extra growth: " + mode);
        }
        Reset(); Implant(); host = Host(); working = DirectoryMaster.Item(continuous.Module.Id); host.Bay.Add(working, 2, 1);
        check(MatrixTurbo.Begin(Booster("flashlight"), host.Machine) == null,
            "A non-booster tool enabled Cognitive Turbo growth");

        // Native learning caps are per-item and must remain untouched.
        var learner = new GameItem(); ModuleHelper.InitModuleItem(learner, "MODULE_TYPE_UNIVERSAL");
        learner.EnableTag("MODULE_EFFECT1_TAG"); learner.GetTagReadonly("MODULE_EFFECT1_TAG").SetString(NodeEffectDefinitions.NativeLearning);
        learner.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").SetInt(5);
        learner.GetTagReadonly("LEARNING_ALGO_MAX_PERFORMANCE_INT").SetInt(5); host.Bay.Add(learner, 4, 2);
        var before = MatrixTurbo.BeforeLearning(learner); MatrixTurbo.AfterLearning(learner, before);
        check(learner.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt() == 6 &&
            learner.GetTagReadonly("LEARNING_ALGO_MAX_PERFORMANCE_INT").GetInt() == 5,
            "Native learning did not pass its old cap without mutating that cap");
        var tool = MatrixTurbo.Begin(Booster("turbo_booster_adv"), host.Machine);
        before = MatrixTurbo.BeforeLearning(learner); MatrixTurbo.AfterLearning(learner, before); MatrixTurbo.End(tool, true);
        check(learner.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt() == 7,
            "Odd native learning cap was not rounded down or turbo exceeded it");
        var nativeEffect = new ModuleEffectHelper.ModuleEffect { identifier = NodeEffectDefinitions.NativeLearning, description = "native learning" };
        ModuleEffectHelper.moduleEffects[NodeEffectDefinitions.NativeLearning] = nativeEffect;
        object?[] tooltip = { learner, null }; Ports.Hook(typeof(MatrixLearningTooltipPatch), "Prefix", tooltip);
        check(nativeEffect.description.Contains("+7%"), "Augmented learning tooltip did not show the per-item effective cap");
        var nativeError = new InvalidOperationException("native tooltip failure");
        check(ReferenceEquals(Ports.Hook(typeof(MatrixLearningTooltipPatch), "Finalizer", tooltip[1], nativeError), nativeError) &&
            nativeEffect.description == "native learning", "Tooltip cleanup lost the native exception or shared description");
        ModuleEffectHelper.moduleEffects.Remove(NodeEffectDefinitions.NativeLearning);
        Reset();
    }
}
