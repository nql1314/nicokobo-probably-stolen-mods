using Nicokobo.ModUtilities;
using Nicokobo.Forge;
namespace Nicokobo.ContentInterop;

public static class ManufacturingTerminal
{
    public const string ItemId = "nicokobo.forge.machines.universal_manufacturing_terminal";
    private static ConfigEntry<int>? _markup;
    internal static void Attach()
    {
        _markup = ModHelper.GetConfig(ForgeMod.Id).Define("ManufacturingMarkupPercent", 25, "forge.config.manufacturing",
            () => ModText.Get(ForgeMod.Id, "manufacturing.markup_percent"));
        ModHook.OnModItemDirectoryInit += Register;
        ModHook.OnLootTablesLoaded += Loot;
        NightShop.Register(ForgeMod.Id, _ => HasRecipes && RNG.Roll(40) ? new[] { ItemId } : Array.Empty<string>());
    }
    internal static void Detach() { ModHook.OnModItemDirectoryInit -= Register; ModHook.OnLootTablesLoaded -= Loot; }
    private static void Register(ModItemDirectory directory)
    {
        if (!directory.Add(ItemId, Create)) throw new InvalidOperationException("Manufacturing terminal ID collision");
        MachineCatalog.Register(ForgeMod.Id, ItemId, Ports, () => Array.Empty<RecipeView>(), nightOrder: 50);
    }
    private static bool HasRecipes => ExtraRecipes.ForMachine(ItemId).Count > 0;
    private static void Loot()
    {
        if (HasRecipes) LootRegistry.AddEntry(ForgeMod.Id, TableMaster.toolTable, ItemId, .05f);
    }
    private static MachinePorts Ports(GameItem machine) => new((GameInventory)MachineHelper.GetBodyStorage(machine, 2),
        (GameInventory)MachineHelper.GetBodyStorage(machine, 3), (GameInventory)MachineHelper.GetBodyStorage(machine, 1),
        (GameSlotInventory)MachineHelper.GetBodyStorage(machine, 0));
    private static GameItem Create()
    {
        var battery = new GameSlotInventory();
        var modules = new GameGridInventory(MachineHelper.DEFAULT_MODULE_BAY_CONFIG, MachineHelper.DEFAULT_MODULE_BAY_WIDTH);
        var input = new GameGridInventory(9, 6); var output = new GameGridInventory(9, 6); var note = new GameSlotInventory();
        var machine = ItemDirectory.CreateEmptyItem(ItemId).SetContentWindow(MachineHelper.CreateMachineryWindow(true,
            MachineHelper.PowerPanel(battery), MachineHelper.Panel(LocHelper.GetLocalizedMechanic("mech_furnace_label_module"), modules, "module"),
            MachineHelper.Panel(LocHelper.GetLocalizedMechanic("mech_furnace_label_input"), input, "input"),
            MachineHelper.Panel(LocHelper.GetLocalizedMechanic("mech_furnace_label_output"), output, "output"), MachineHelper.NotePanel(note)));
        machine.name = ModText.Get(ForgeMod.Id, ItemId + ".name");
        machine.shortDescription = ModText.Get(ForgeMod.Id, ItemId + ".description");
        machine.flavorText = ModText.Get(ForgeMod.Id, ItemId + ".flavor");
        machine.SetSpriteAndShapeFromMod(ForgeMod.Id, "universal_manufacturing_terminal");
        machine.SetShape(new GridShapeBuilder().SetData("111111111", 3).Build());
        machine.unitBaseValue = 0; machine.unitValue = 400; machine.mayStackItemFunc = null;
        ModHelper.SetSource(machine, ForgeMod.Id, "NicoKobo Forge");
        machine.EnableTag("CONTAINER_TAG"); machine.EnableTag("STANDARD_MACHINE_TAG"); machine.EnableTag("manufacture");
        MachineHelper.SetupBatterySlot(battery, machine); MachineryHelper.InitMachinery(machine, 25);
        MachineHelper.SetupModuleBay(modules, machine, allowedModuleTypes: new[] { "MODULE_TYPE_UNIVERSAL", "MODULE_TYPE_FURNACE" });
        MachineHelper.SetupManualSlot(note);
        input.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) && ExtraRecipes.ForMachine(ItemId).Any(recipe => recipe.Ingredients.Any(rule => rule.Matches(item)));
        output.mayInventoryAddItemFunc = (_, _) => false;
        machine.onLoaded += item => { item.ModifyTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE, tag => tag.Enable().SetInt(25)); ModuleHelper.ApplyModifiedPower(item); };
        machine.onCycleEndSlotItemFunc = (_, _, _) =>
        {
            foreach (var recipe in ExtraRecipes.ForMachine(ItemId))
            {
                if (!recipe.IsAvailableFor(machine)) continue;
                using var work = ProductionHooks.Begin(new WorkContext(machine, Ports(machine), recipe));
                if (!recipe.IsAvailable() || !machine.GetTagReadonly("is_machinery_powered").GetBool()) continue;
                var source = battery.childItem; int power = MachineryHelper.GetMachinePowerUsage(machine);
                if (source == null || (DevicePower.TryEnergy(source, out int available) ? available < power : !PowerHelper.CanDrawPowerSource(source, power))) continue;
                var takes = ProductionBatch.SelectInputs(input, recipe.Ingredients);
                if (takes == null) continue;
                long basis = checked((long)decimal.Ceiling((takes.Sum(take => ProductionValue.InputValue(take.Item) * take.Count) *
                    (100m + Math.Max(0, Math.Min(1000, _markup!.Value))) / 100m + 25) / recipe.OutputCount));
                if (!ProductionBatch.Run(source, input, output, takes, recipe.OutputCount, recipe.ResolveOutputId, (product, index) =>
                {
                    ProductionValue.SetBasis(product, basis);
                    recipe.PrepareOutput?.Invoke(machine, product, takes.Select(t => t.Item).ToArray(), takes.Select(t => t.Count).ToArray());
                }, power)) continue;
                work.Commit(); MachineHelper.OnMachineActioned(machine, modules); break;
            }
        };
        return machine;
    }
}
