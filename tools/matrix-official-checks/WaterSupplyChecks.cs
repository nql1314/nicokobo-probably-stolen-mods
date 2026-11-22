using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.ModuleMatrix.Definitions;

internal static class WaterSupplyChecks
{
    internal static void Run(Action<bool, string> check)
    {
        PlayerStore.Current = new();
        Liquid.Liquids = new() { new() { identifier = "water" } };
        var warehouse = new GameGridInventory();
        var farm = new GameItem { identifier = "moisture_farm" };
        farm.EnableTag("STANDARD_MACHINE_TAG");
        var modules = new GameGridInventory { Parent = farm };
        farm.Storages[1] = modules;
        modules.Add(DirectoryMaster.Item(MachineModuleDefinitions.All.Single(def => def.Code == "S06").Id));
        var bottleSlot = new GameSlotInventory { Parent = farm };
        farm.Storages[2] = bottleSlot;
        var water = new GameItem(); water.EnableTag("LIQUID_CONTAINER_TAG");
        water.GetTagReadonly("capacity").SetInt(100);
        water.GetTagReadonly("liquid:water").SetInt(100);
        bottleSlot.Add(water); warehouse.Add(farm);
        PlayerStore.Instance.Items.Add(farm);
        int nativeOpportunities = 0;
        // Native boundary: a full bottle skips Fill entirely in Build 26317870.
        // The supply algorithm and cycle wrapper are the production implementation.
        farm.onCycleEndSlotItemFunc = (_, _, _) =>
        { if (WaterHelper.GetFreeCapacity(water) > 0) nativeOpportunities++; };
        MatrixMachines.Configure(farm); MatrixMachines.Configure(farm);
        var target = new GameItem(); target.EnableTag("LIQUID_CONTAINER_TAG");
        target.GetTagReadonly("capacity").SetInt(100); warehouse.Add(target, 1, 0);
        farm.onCycleEndSlotItemFunc!(farm, warehouse, new());
        check(WaterHelper.GetTotalVolume(target) == 100 && WaterHelper.GetTotalVolume(water) == 0,
            "S06 must resume delivery from a full source bottle when an adjacent destination becomes available");
        check(nativeOpportunities == 1, "S06 configuration must retain one native cycle callback without double wrapping");
        farm.onCycleEndSlotItemFunc!(farm, warehouse, new());
        check(WaterHelper.GetTotalVolume(target) == 100 && WaterHelper.GetTotalVolume(water) == 0 && nativeOpportunities == 2,
            "Repeated S06 cycles must not create water or duplicate the native opportunity");

        PlayerStore.Current = new(); warehouse = new GameGridInventory();
        (GameItem Farm, GameItem Water) FullFarm(int x)
        {
            var item = new GameItem { identifier = "moisture_farm" }; item.EnableTag("STANDARD_MACHINE_TAG");
            var bay = new GameGridInventory { Parent = item }; item.Storages[1] = bay;
            bay.Add(DirectoryMaster.Item(MachineModuleDefinitions.All.Single(def => def.Code == "S06").Id));
            var slot = new GameSlotInventory { Parent = item }; item.Storages[2] = slot;
            var bottle = new GameItem(); bottle.EnableTag("LIQUID_CONTAINER_TAG");
            bottle.GetTagReadonly("capacity").SetInt(100); bottle.GetTagReadonly("liquid:water").SetInt(100);
            slot.Add(bottle); warehouse.Add(item, x, 0); PlayerStore.Instance.Items.Add(item);
            var receiver = new GameItem(); receiver.EnableTag("LIQUID_CONTAINER_TAG");
            receiver.GetTagReadonly("capacity").SetInt(100); warehouse.Add(receiver, x + 1, 0);
            return (item, bottle);
        }
        var first = FullFarm(0); var second = FullFarm(4); int runningAtNativeCount = 0;
        first.Farm.onCycleEndSlotItemFunc = (_, _, _) =>
        {
            if (WaterHelper.GetFreeCapacity(first.Water) <= 0) return;
            // Boundary dispatch for the native nightly counter. Missing patches
            // behave as the unpatched game; no supply implementation lives here.
            var prefix = typeof(MatrixMod).Assembly.GetType("MechcoreProtocol.ModuleMatrix.MatrixWaterSupplyCountPatch");
            if (prefix != null) Ports.Hook(prefix, "Prefix");
            runningAtNativeCount = new[] { first.Water, second.Water }.Count(bottle => WaterHelper.GetFreeCapacity(bottle) > 0);
        };
        MatrixMachines.Configure(first.Farm); MatrixMachines.Configure(second.Farm);
        first.Farm.onCycleEndSlotItemFunc(first.Farm, warehouse, new());
        check(runningAtNativeCount == 2 && WaterHelper.GetTotalVolume(first.Water) == 0 && WaterHelper.GetTotalVolume(second.Water) == 0,
            "S06 must drain all eligible full farms before native shared-air counting, including a later farm");
    }
}
