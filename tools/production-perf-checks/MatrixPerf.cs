using System.Reflection;
using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.ModuleMatrix.Definitions;
using Nicokobo.ContentInterop;

internal static class MatrixPerf
{
    private static readonly Action<GameItem> LearningTooltip = CreateTooltip();
    private static Action<GameItem> CreateTooltip()
    {
        var method = typeof(MatrixLearningTooltipPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
        var item = System.Linq.Expressions.Expression.Parameter(typeof(GameItem));
        var before = System.Linq.Expressions.Expression.Variable(typeof(MatrixLearningTooltipPatch.Before));
        return System.Linq.Expressions.Expression.Lambda<Action<GameItem>>(
            System.Linq.Expressions.Expression.Block(new[] { before }, System.Linq.Expressions.Expression.Call(method, item, before)), item).Compile();
    }
    private static (GameItem Machine, GameItem Module, GameItem Implant, GameGridInventory Nested) Setup(int count)
    {
        PlayerStore.Current = new(); MatrixTurbo.Reset(); StoreStation.Day = 25;
        var machine = new GameItem { identifier = "perf_machine", onCycleEndSlotItemFunc = (_, _, _) => { } };
        machine.EnableTag("STANDARD_MACHINE_TAG");
        var bay = new GameGridInventory { Parent = machine }; machine.Storages[1] = bay;
        PlayerStore.Instance.Items.Add(machine);
        var module = DirectoryMaster.Item(ContinuousModuleDefinitions.M08.Module.Id); bay.Add(module, 2, 1);
        var container = new GameItem { identifier = "nested_container" };
        var nested = new GameGridInventory { Parent = container }; container.Storages[0] = nested;
        PlayerStore.Instance.Items.Add(container);
        var implant = new GameItem { identifier = MatrixTurbo.CognitiveTurbo }; nested.Add(implant);
        for (int index = 4; index < count; index++)
        {
            var item = new GameItem { identifier = "ordinary_item_" + index };
            if (index % 2 == 0) nested.Add(item); else PlayerStore.Instance.Items.Add(item);
        }
        return (machine, module, implant, nested);
    }
    private static void StateChecks()
    {
        var fixture = Setup(100); var definition = ContinuousModuleDefinitions.M08;
        void Cap(int value, string message)
        {
            PerfCounters.Check(MatrixTurbo.TotalCap(definition) == value, message);
            PerfCounters.Check(MatrixTurbo.ContinuousDescription(fixture.Module, definition).Contains("+" + value + "%"), message + " (tooltip)");
        }
        Cap(48, "Initial owned implant missing");
        fixture.Implant.Owned = false; Cap(32, "Same-frame ownership loss retained cap");
        fixture.Implant.Owned = true; Cap(48, "Same-frame ownership gain failed to raise cap");
        fixture.Implant.unitCount = 0; Cap(32, "Same-frame zero quantity retained cap");
        fixture.Implant.unitCount = 1; Cap(48, "Same-frame quantity recovery lost cap");
        fixture.Nested.Expel(fixture.Implant); Cap(32, "Same-frame removal retained cap");
        fixture.Nested.Add(fixture.Implant); Cap(48, "Same-frame reinsertion lost cap");
        PlayerStore.Instance.runID = ""; Cap(32, "Invalid run retained cap");
        PlayerStore.Instance.runID = "restored"; Cap(48, "Restored run failed to raise cap");
        // Existing source-linked checks cover growth/top-up, loss mid-work, exceptions and run switches.
        TurboChecks.Run(PerfCounters.Check);
    }
    private static void Main(string[] args)
    {
        NightShop.Attach(); var mod = new MatrixMod(); mod.Init(new()); mod.OnEnable(); ModHook.Register();
        StateChecks();
        foreach (int count in new[] { 100, 1000 })
        {
            var fixture = Setup(count);
            PerfCounters.Check(PlayerStore.Instance.FindAllItem().Count == count, "Inventory cardinality differs from benchmark label");
            PerfCounters.Measure("HasImplant", count, () => MatrixTurbo.HasImplant());
            PerfCounters.Measure("ContinuousDescription", count, () => MatrixTurbo.ContinuousDescription(fixture.Module, ContinuousModuleDefinitions.M08));
            PerfCounters.Measure("UnrelatedLearningTooltip", count, () => LearningTooltip(fixture.Module));
            PerfCounters.Measure("GrowthWork", count, () => MatrixGrowth.Work(fixture.Module, fixture.Module.parentInventory!, fixture.Machine));
        }
        PerfCounters.Save(args.Single());
    }
}
