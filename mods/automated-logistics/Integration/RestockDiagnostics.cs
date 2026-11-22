
namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private void TraceRestock(GameItem machine, string trigger, string phase, string detail) =>
        debug?.Invoke($"[AutomatedLogistics/Restock] slot={_slot}; run={_run}; machine={machine.identifier}#{machine.GetUniqueID()}; trigger={trigger}; phase={phase}; {detail}");

    private static string WarehouseDiagnostic()
    {
        var warehouse = Warehouse();
        if (warehouse == null) return "warehouse=EmporiumEntry.invElement; available=false";
        return $"warehouse=EmporiumEntry.invElement; inventory={warehouse.identifier}@{warehouse.Handle().ToInt64():x}; " +
            $"removeLocked={warehouse.IsRemoveLocked()}; insertLocked={warehouse.IsInsertLocked()}; " +
            $"directStacks={warehouse.childItems.Count}; items={string.Join(",", warehouse.childItems.ToArray().Where(item => item != null).Take(16).Select(item => $"{item.identifier}#{item.GetUniqueID()}x{item.unitCount}"))}";
    }

    private void TraceSources(GameItem machine, IReadOnlyList<GameItem> existing, IReadOnlyList<RestockSource> solids,
        IReadOnlyList<RestockSource> liquids)
    {
        static string Counts(IReadOnlyList<RestockSource> sources) => string.Join(",", Enumerable.Range(0, 3)
            .Select(tier => $"{tier}:{sources.Count(source => source.Tier == tier)}"));
        debug?.Invoke($"[AutomatedLogistics/Restock] machine={machine.identifier}#{machine.GetUniqueID()}; " +
            $"networkOnline={ResourceNetworkOnline}; sourceTiers=network,nearby,warehouse; solids={Counts(solids)}; liquids={Counts(liquids)}; " +
            $"input={string.Join(",", existing.Select(item => $"{item.identifier}#{item.GetUniqueID()}x{item.unitCount}"))}");
    }

    private void TraceNightRestocking()
    {
        if (debug == null) return;
        EnsureLoaded();
        debug($"[AutomatedLogistics/Restock] phase=before-night; ready={Ready}; {WarehouseDiagnostic()}");
        foreach (var machine in Items().Where(item => Content.Supported.Contains(item.identifier)).OrderBy(item => item.GetUniqueID()))
            TraceRestock(machine, "night-scan", "machine", $"eligible={MachineAccess.Eligible(machine)}; parent={machine.parentInventory?.Handle()}; " +
                $"parentRemoveLocked={machine.parentInventory?.IsRemoveLocked()}; feeder={NativeModuleApi.FindInstalled(machine, Content.Feeder).Count}; fillEnabled={Settings(machine).FillEnabled}");
    }
}
