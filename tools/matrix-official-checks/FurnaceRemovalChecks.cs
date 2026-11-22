using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.ModuleMatrix.Definitions;
using MechcoreProtocol.ModuleMatrix.Domain;
using Nicokobo.ContentInterop;

internal static class FurnaceRemovalChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var retired = MachineModuleDefinitions.All.Single(definition => definition.Code == "S26");
        var oldModule = DirectoryMaster.Item(retired.Id);
        check(retired.Retired && oldModule.identifier == retired.Id && !MatrixMod.Enabled(retired.Id),
            "Retired S26 must remain readable without an active effect");
        check(!SupplyCatalog.Available(StockCategory.Module).Any(offer => offer.ItemId == retired.Id) &&
            NightShopModuleSelection.Draw(new[] { retired.Id }, _ => 0).Count == 0,
            "Retired S26 is still stocked");
        check(!MachineCatalog.Recipes("furnace").Any(recipe => recipe.Id.StartsWith(MatrixMod.Id, StringComparison.Ordinal)),
            "Matrix still contributes native furnace recipes");

        var furnace = new GameItem { identifier = "furnace" };
        furnace.EnableTag("STANDARD_MACHINE_TAG");
        var modules = new GameGridInventory { Parent = furnace };
        furnace.Storages[1] = modules;
        Action<GameItem, GameInventory, SlotMarker> cycle = (_, _, _) => { };
        furnace.onCycleEndSlotItemFunc = cycle;
        MatrixMachines.Configure(furnace);
        check(ReferenceEquals(furnace.onCycleEndSlotItemFunc, cycle),
            "Matrix configuration replaced the native furnace cycle");
        check(!modules.mayInventoryAddItemFunc!(oldModule, modules),
            "Native furnace still admits the retired batch module");
        modules.Add(oldModule);
        check(!MatrixMachines.Installed(furnace, "S26").Any() && oldModule.parentInventory == modules && !oldModule.Destroyed,
            "A saved S26 was activated or removed from its original slot");
    }
}
