using Nicokobo.ModUtilities;
using static PsModI18n.I18n;
using Nicokobo.ContentInterop;

namespace Nicokobo.AutomatedLogistics.Integration;

internal static class Content
{
    internal const string ManufacturingTerminal = "nicokobo.forge.machines.universal_manufacturing_terminal";
    internal const string Owner = "nicokobo.automated_logistics";
    internal const string Feeder = Owner + ".auto_feeder";
    internal const string Ejector = Owner + ".output_ejector";
    internal const string Terminal = Owner + ".network_terminal";
    internal const string ResourceDevice = Owner + ".void_container";
    internal const string WaterDevice = Owner + ".void_tank";
    internal const string PowerDevice = Owner + ".void_grid";
    internal static bool IsDevice(string id) => id is ResourceDevice or WaterDevice or PowerDevice;
    internal const string CapacityTag = "LIQUID_CONTAINER_CAPACITY";
    internal const string ContentsId = "nicokobo.automated_logistics.contents";
    internal const string ElectromechanicalWorkbench = "nicokobo.mechcore.synthesis.synthesizer";
    internal static readonly string[] Supported =
    {
        "furnace", "water_purifier", "moisture_farm", "desequencer", "printer", "evaporator", "hydroponic", "plant_dna_manipulator", ManufacturingTerminal,
        "nicokobo.mechcore.synthesis.material_refiner", ElectromechanicalWorkbench,
        "nicokobo.mechcore.synthesis.precision_assembler", "nicokobo.mechcore.synthesis.liquid_infusion_tower",
        "nicokobo.mechcore.synthesis.food_processor"
    };
    private sealed record Definition(string Id, string Template, bool Module, int Value);
    private static readonly Definition[] Items =
    {
        new(Feeder, "blank_module", true, 180),
        new(Ejector, "blank_module", true, 180),
        new(Terminal, "aquascan", false, 240),
        new(ResourceDevice, "", false, 0),
        new(WaterDevice, "", false, 0),
        new(PowerDevice, "", false, 0)
    };
    internal static string? RegisteredName(string id, bool english) =>
        Items.Any(item => item.Id == id) ? ModText.GetFor(Owner, id + ".name", english ? "en" : "zh") : null;
    internal static string? LocalizedName(string id) =>
        Items.Any(item => item.Id == id) ? ModText.Get(Owner, id + ".name") : null;
    internal static void Register(ModItemDirectory directory)
    {
        ItemArtwork.Configure(UnityEngine.Debug.LogWarning);
        foreach (var definition in Items)
        {
            if (DirectoryMaster.Has<GameItem>(definition.Id)) throw new InvalidOperationException("Logistics item ID collision: " + definition.Id);
            if (!directory.Add(definition.Id, () => { var item = Create(definition); ModHelper.SetSource(item, Owner, Get("title")); return item; }))
                throw new InvalidOperationException("Logistics item registration failed: " + definition.Id);
        }
    }
    internal static void RegisterModuleRecipes()
    {
        string machine = MachineCatalog.Contains(ElectromechanicalWorkbench) ? ElectromechanicalWorkbench : ManufacturingTerminal;
        RegisterRecipe("make_feeder", machine, Feeder, 2, () => MachineCatalog.Contains(machine),
            Ingredient("common_electronic", 1), Ingredient("metal_ingot", 1));
        RegisterRecipe("make_ejector", machine, Ejector, 2, () => MachineCatalog.Contains(machine),
            Ingredient("common_electronic", 1), Ingredient("wire", 2));
        RegisterDevice("make_void_container", ResourceDevice, "storage_bay", () => AutomatedLogisticsRuntime.HasNetworkUpgrade(AutomatedLogisticsNumbers.ResourceUpgrade));
        RegisterDevice("make_void_tank", WaterDevice, "water_jug", () => AutomatedLogisticsRuntime.HasNetworkUpgrade(AutomatedLogisticsNumbers.WaterUpgrade));
        RegisterDevice("make_void_grid", PowerDevice, "machine_bay", AutomatedLogisticsRuntime.CanUnlockPowerGrid);
    }
    private static IngredientView Ingredient(string id, int count, Func<GameItem, bool>? condition = null) =>
        new(id, count, item => item.identifier == id && (condition?.Invoke(item) ?? true), id, id)
        { LocalizedLabel = () => MachineAccess.Name(id) };
    private static void RegisterRecipe(string id, string machine, string output, int count, Func<bool> available, params IngredientView[] inputs) =>
        ExtraRecipes.Register(Owner, new ExtraRecipe(Owner + "." + id, machine, inputs, output,
            RegisteredName(output, false)!, RegisteredName(output, true)!, available, () => output, outputCount: count)
        { LocalizedOutputName = () => LocalizedName(output)! });
    private static void RegisterDevice(string id, string output, string shell, Func<bool> unlocked) =>
        RegisterRecipe(id, ManufacturingTerminal, output, 1,
            () => MachineCatalog.Contains(ManufacturingTerminal) && unlocked(),
            Ingredient("metal_ingot", 1, item => item.GetTagReadonly("INGOT_PURITY_TAG")?.valueString == "INGOT_PURITY_PERFECT"),
            Ingredient(shell, 1, item => NativeSaveGraphApi.Items(item).Count == 1 && (NativeLiquidApi.Capture(item)?.TotalParts ?? 0) == 0));
    private static GameItem Create(Definition definition)
    {
        if (IsDevice(definition.Id)) ItemArtwork.TryInstall();
        // Device items own their graph and callbacks; no storage/machine template
        // can restore its sprite or whitelist on inventory changes.
        var item = IsDevice(definition.Id)
            ? ItemDirectory.CreateEmptyItem(definition.Id)
            : DirectoryMaster.Create<GameItem>(definition.Template)
                ?? throw new InvalidOperationException("Logistics template unavailable: " + definition.Template);
        try
        {
            if (definition.Module)
            {
                ModuleHelper.InitModuleItem(item, "MODULE_TYPE_UNIVERSAL", 0, 0, 0, true, LocalizedName(definition.Id)!, 1);
                foreach (string name in new[] { "MODULE_EFFECT1_TAG", "MODULE_EFFECT2_TAG" })
                { var tag = item.state.GetTag(name); tag.valueString = ""; tag.valueInt = 0; }
                ModuleEffectHelper.ResetAllTempStat(item);
            }
            item.identifier = definition.Id;
            if (IsDevice(definition.Id)) ConfigureDevice(item, definition.Id);
            ItemArtwork.Apply(item);
            ApplyText(item);
            item.unitBaseValue = 0; item.unitValue = definition.Value;
            item.mayStackItemFunc = null;
            ConfigureTool(item);
            return item;
        }
        catch { if (item.parentInventory == null) item.Destroy(); throw; }
    }
    internal static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        if (Items.Any(definition => definition.Id == item.identifier)) ApplyText(item);
    }

    private static void ApplyText(GameItem item)
    {
        item.name = ModText.Get(Owner, item.identifier + ".name");
        item.shortDescription = ModText.Get(Owner, item.identifier + ".description");
    }

    internal static void ConfigureTool(GameItem item)
    {
        item.EnableManufacturer(Owner);
        if (item.identifier is Feeder or Ejector)
        {
            // Settings use explicit item activation. Keep module installation
            // and machine-work callbacks intact, and preserve click-to-drag.
            item.forceDisableActivate = false;
            item.activateDefault = false;
            return;
        }
        if (IsDevice(item.identifier))
        {
            item.activateDefault = false;
            // Native force-disable flags also add a misleading "Disabled" tooltip line.
            item.forceDisableUse = item.forceDisableActivate = false;
            item.maySelectSlotItemFunc = item.canSelectSlotItemFunc = (_, _, _) => false;
            item.mayInventoryAddItemFunc = (_, inventory) => inventory.GetParentItem()?.identifier != "trashcan";
            item.contentWindow?.SetTitle(GeneralHelper.HasCustomName(item) ? GeneralHelper.GetCustomName(item) : MachineAccess.DisplayName(item));
            item.state.GetTag("NOT_FOR_RESALE").Enable(); item.SyncModifiedState();
            if (item.identifier == WaterDevice)
            {
                item.mayActivateSlotItemFunc = item.canActivateSlotItemFunc = (_, _, _) => false;
                item.onActivateSlotItemFunc = null;
                // Native transparent-water modifier assumes a built-in sprite
                // family and produces an unresolved overlay for a custom tank.
                item.getItemSpriteModifierForItemFunc = null;
            }
            else item.onActivateSlotItemFunc = NativeDelegate.Convert<System.Action<GameItem, GameInventory, SlotMarker>>(
                new Action<GameItem, GameInventory, SlotMarker>((current, _, _) =>
                {
                    if (!NativeInventoryApi.IsPlayerOwned(current)) return;
                    current.contentWindow.SetTitle(GeneralHelper.HasCustomName(current) ? GeneralHelper.GetCustomName(current) : MachineAccess.DisplayName(current));
                    current.contentWindow.Show(); current.RestoreContentWindowPosition();
                }));
            return;
        }
        if (item.identifier != Terminal) return;
        item.RemoveAllGameItemType();
        item.SetGameItemType("TOOL");
        item.mayActivateSlotItemFunc = item.canActivateSlotItemFunc = null;
        item.mayItemTargetSlotItemFunc = item.canItemTargetSlotItemFunc = null;
        item.onItemTargetSlotItemFunc = null;
        item.maySlotItemTargetItemFunc = item.canSlotItemTargetItemFunc = null;
        item.onSlotItemTargetItemFunc = null;
        item.mayItemTargetItemFunc = item.canItemTargetItemFunc = null;
        item.onItemTargetItemFunc = null;
        item.mayThisTargetItemFunc = item.canThisTargetItemFunc = null;
        item.onThisTargetItemFunc = null;
        item.mayItemTargetThisFunc = item.canItemTargetThisFunc = null;
        item.onItemTargetThisFunc = null;
        item.onActivateSlotItemFunc = NativeDelegate.Convert<System.Action<GameItem, GameInventory, SlotMarker>>(
            new Action<GameItem, GameInventory, SlotMarker>((current, _, _) =>
            {
                if (NativeInventoryApi.IsPlayerOwned(current)) NativeHooks.Open?.Invoke(current);
            }));
        item.forceDisableActivate = false;
        // Use-mode selection is separate from activation. The force-disable
        // flag also prints "Disabled" in the native tooltip.
        item.forceDisableUse = false;
        item.maySelectSlotItemFunc = item.canSelectSlotItemFunc = (_, _, _) => false;
        // Default activation claims the press before the native drag handler.
        // Double-click and the context-menu activation still open these tools.
        item.activateDefault = false;
    }

    private static void ConfigureDevice(GameItem item, string id)
    {
        const int size = 3;
        item.SetShape(new GridShapeBuilder().SetData(new string('1', size * size), size).Build());
        if (id == WaterDevice)
        {
            int capacity = NativeHooks.Runtime?.State.Resources?.WaterCapacityParts ?? AutomatedLogisticsNumbers.WaterInitialParts;
            WaterHelper.InitLiquidContainerItem(item, capacity / 1000, 0);
            WaterHelper.EmptyContainer(item);
            var tag = item.state.GetTag(CapacityTag); tag.Enable(); tag.SetInt(capacity); item.SyncModifiedState();
        }
        else
        {
            int width = id == ResourceDevice ? AutomatedLogisticsNumbers.ResourceWidth : AutomatedLogisticsNumbers.Require().PowerWidth;
            int height = id == ResourceDevice ? NativeHooks.Runtime?.State.Resources?.Rows ?? AutomatedLogisticsNumbers.ResourceInitialRows
                : NativeHooks.Runtime?.State.Power.Height > 0 ? NativeHooks.Runtime.State.Power.Height : AutomatedLogisticsNumbers.Require().PowerHeight;
            var grid = new GameGridInventory(width, height) { identifier = ContentsId };
            var window = new PixelWindow(true, id == ResourceDevice ? PsModI18n.I18n.Get("void_container") : PsModI18n.I18n.Get("void_grid"));
            if (!window.Attach(grid.Cast<PixelElement>())) throw new InvalidOperationException(PsModI18n.I18n.Get("could_not_create_the_void_storage_window"));
            item.SetContentWindow(window);
            ContainerHelper.InitContainerItemTagOnly(grid, item);
            if (grid.GetParentItem()?.Handle() != item.Handle()) throw new InvalidOperationException(PsModI18n.I18n.Get("invalid_void_storage_ownership"));
            window.Validate();
        }
        item.SyncModifiedState();
        if (item.shape.width != size || item.shape.height != size || item.modifiedShape.width != size || item.modifiedShape.height != size)
            throw new InvalidOperationException(PsModI18n.I18n.Get("void_devices_must_occupy_a_3_3"));
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            if (item.modifiedShape.GetLocal(x, y) != 1) throw new InvalidOperationException(PsModI18n.I18n.Get("void_device_grid_shape_is_incomplete"));
        item.RemoveAllGameItemType(); item.SetGameItemType(id == PowerDevice ? "MACHINE" : "STORAGE");
    }
}
