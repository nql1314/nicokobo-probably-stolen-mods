using Nicokobo.ModUtilities;
using Nicokobo.FoodProcessing.Definitions;
using Nicokobo.FoodProcessing.Domain;

namespace Nicokobo.FoodProcessing;

internal static class FoodMachines
{
    internal static GameItem Create(MachineDefinition definition)
    {
        var battery = new GameSlotInventory();
        var modules = new GameGridInventory(MachineHelper.DEFAULT_MODULE_BAY_CONFIG, MachineHelper.DEFAULT_MODULE_BAY_WIDTH);
        var input = new GameGridInventory(definition.InputWidth, definition.InputHeight);
        var output = new GameGridInventory(definition.OutputWidth, definition.OutputHeight);
        var note = new GameSlotInventory();
        var water = definition.WaterSourceSlots == 0 ? null : new GameSlotInventory();
        // Inventory indices remain battery/modules/input/output/note/water.
        // Display order must never change the serialized storage order.
        var panels = new List<MachineryPanel>
        {
            MachineHelper.PowerPanel(battery),
            MachineHelper.Panel(LocHelper.GetLocalizedMechanic("mech_furnace_label_module"), modules, "module"),
            MachineHelper.Panel(ModText.Get(FoodDefinitions.ModId, "food.clean_input"), input, "input"),
            MachineHelper.Panel(LocHelper.GetLocalizedMechanic("mech_furnace_label_output"), output, "output"),
            MachineHelper.NotePanel(note)
        };
        if (water != null)
            panels.Add(MachineHelper.Panel(LocHelper.GetLocalizedMechanic("mech_hydroponic_label_water"), water, "water"));
        var machine = ItemDirectory.CreateEmptyItem(definition.ItemId);
        var selection = new MachineryButton("");
        FoodOutputPreview? preview = null;
        void RefreshSelection()
        {
            selection.SetText(FoodSelection.Label(machine));
            preview?.Refresh();
        }
        selection.SetCallback(() => FoodSelection.Open(machine, RefreshSelection));
        var previewInfo = new MachineryText(FoodText.Get("food.preview_title"), uppercase: false);
        panels.Add(MachineHelper.Panel(null, MachineryStack.Vertical(selection, previewInfo), "settings"));
        machine.SetContentWindow(MachineHelper.CreateMachineryWindow(true, panels.ToArray()));
        RefreshSelection();
        machine.EnableTag("CONTAINER_TAG");
        machine.EnableTag("STANDARD_MACHINE_TAG");
        machine.EnableTag("manufacture");
        MachineHelper.SetupBatterySlot(battery, machine);
        MachineryHelper.InitMachinery(machine, definition.BasePowerCost);
        MachineHelper.SetupModuleBay(modules, machine,
            allowedModuleTypes: new[] { "MODULE_TYPE_UNIVERSAL", "MODULE_TYPE_FURNACE" });
        MachineHelper.SetupManualSlot(note);
        input.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) && FoodProduction.AcceptsIngredient(item) &&
            (FoodDefinitions.Recipes.Where(recipe => recipe.MachineId == definition.ItemId)
                .Any(recipe => recipe.ItemInputs.Any(requirement => Matches(item, requirement))) ||
             Nicokobo.ContentInterop.ExtraRecipes.ForMachine(definition.ItemId)
                .Any(recipe => recipe.Ingredients.Any(requirement => requirement.Matches(item))));
        output.mayInventoryAddItemFunc = (_, _) => false;
        if (water != null)
            water.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) &&
                item.unitCount == 1 && item.IsTag("LIQUID_CONTAINER_TAG");
        FoodStorageExpansion.Attach(machine, definition, input, output);
        machine.onLoaded += item =>
        {
            item.ModifyTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE, tag => tag.Enable().SetInt(definition.BasePowerCost));
            ModuleHelper.ApplyModifiedPower(item);
            RefreshSelection();
        };
        machine.onCycleEndSlotItemFunc = (_, _, _) =>
        {
            int batches = FoodProduction.BatchCapacity(machine);
            for (int batch = 0; batch < batches; batch++)
            {
                bool produced = false;
                foreach (var recipe in FoodProduction.OrderRecipes(definition.ItemId))
                {
                    try
                    {
                        if (!FoodProduction.TryProcess(machine, battery, input, output, water, recipe)) continue;
                        produced = true;
                        break;
                    }
                    catch (Exception error)
                    {
                        FoodMod.Warn($"Stopped {definition.Code}: {error}");
                        return;
                    }
                }
                if (!produced)
                foreach (var recipe in Nicokobo.ContentInterop.ExtraRecipes.ForMachine(definition.ItemId))
                {
                    try
                    {
                        if (!FoodProduction.TryProcess(machine, battery, input, output, water, recipe)) continue;
                        produced = true;
                        break;
                    }
                    catch (Exception error) { FoodMod.Warn($"Stopped extra recipe {recipe.Id}: {error}"); return; }
                }
                if (!produced) break;
                // Native module wear/effects happen only after a committed batch.
                try { MachineHelper.OnMachineActioned(machine, modules); }
                catch (Exception error) { FoodMod.Warn("Module action failed after production: " + error); return; }
            }
        };
        preview = FoodOutputPreview.Attach(machine, output);
        previewInfo.WithInfoLink(preview.LinkId, () => preview.Description);
        return machine;
    }

    internal static bool Matches(GameItem item, RecipeRequirement input) => FoodIngredients.Matches(item, input, IsGenuineInjector);

    private static bool IsGenuineInjector(GameItem item)
    {
        if (item.identifier != FoodDefinitions.ImmunivaxId || item.itemFeatures == null) return false;
        var genuine = ItemConditionList.CreateGenuineInjector();
        int matches = 0;
        foreach (var feature in item.itemFeatures)
        {
            var real = feature.realCondition;
            if (!feature.useCondition || real == null || real.category != genuine.category) continue;
            if (real.identifier != genuine.identifier) return false;
            matches++;
        }
        return matches == 1;
    }

    internal static int WaterQuality(GameItem container)
    {
        var id = WaterFeatureHelper.GetConditionFromPurity(WaterHelper.GetWaterPurity(container))?.identifier;
        if (id == ItemConditionList.CreatePureWater().identifier) return 3;
        if (id == ItemConditionList.CreateHighQualityWater().identifier) return 2;
        return id == ItemConditionList.CreateBaseWater().identifier ? 1 : 0;
    }
}
