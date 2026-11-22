using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;
using Nicokobo.ContentInterop;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class SynthesisMachines
{
    internal static GameItem Create(MachineDefinition definition)
    {
        var battery = new GameSlotInventory();
        var modules = new GameGridInventory(MachineHelper.DEFAULT_MODULE_BAY_CONFIG, MachineHelper.DEFAULT_MODULE_BAY_WIDTH);
        var input = new GameGridInventory(definition.InputWidth, definition.InputHeight);
        var output = new GameGridInventory(definition.OutputWidth, definition.OutputHeight);
        var note = new GameSlotInventory();
        // Keep the published battery/module/input/output/note storage indices.
        var machine = ItemDirectory.CreateEmptyItem(definition.ItemId).SetContentWindow(MachineHelper.CreateMachineryWindow(true,
            MachineHelper.PowerPanel(battery),
            MachineHelper.Panel(LocHelper.GetLocalizedMechanic("mech_furnace_label_module"), modules, "module"),
            MachineHelper.Panel(LocHelper.GetLocalizedMechanic("mech_furnace_label_input"), input, "input"),
            MachineHelper.Panel(LocHelper.GetLocalizedMechanic("mech_furnace_label_output"), output, "output"),
            MachineHelper.NotePanel(note)));
        machine.EnableTag("CONTAINER_TAG"); machine.EnableTag("STANDARD_MACHINE_TAG"); machine.EnableTag("manufacture");
        MachineHelper.SetupBatterySlot(battery, machine);
        MachineryHelper.InitMachinery(machine, definition.BasePowerCost);
        MachineHelper.SetupModuleBay(modules, machine, allowedModuleTypes: new[] { "MODULE_TYPE_UNIVERSAL", "MODULE_TYPE_FURNACE" });
        MachineHelper.SetupManualSlot(note);
        input.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) &&
            (ProductionCatalog.Recipes.Where(recipe => recipe.MachineId == definition.ItemId).Any(recipe => recipe.ItemInputs.Any(rule => SynthesisIngredients.Matches(item, rule))) ||
             ExtraRecipes.ForMachine(definition.ItemId).Any(recipe => recipe.Ingredients.Any(rule => rule.Matches(item))));
        output.mayInventoryAddItemFunc = (_, _) => false;
        machine.onLoaded += item =>
        {
            item.ModifyTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE, tag => tag.Enable().SetInt(definition.BasePowerCost));
            ModuleHelper.ApplyModifiedPower(item);
        };
        machine.onCycleEndSlotItemFunc = (_, _, _) =>
        {
            int batches = MachineProcessingRules.NightlyBatchCount(definition, MachineryHelper.GetCurrentPerformanceBonus(machine));
            for (int batch = 0; batch < batches; batch++)
            {
                bool produced = false;
                RecipeDefinition? mouseTrapRecipe = null;
                foreach (var recipe in SynthesisProduction.OrderRecipes(machine, input))
                {
                    if (recipe.Code == "P16")
                    {
                        mouseTrapRecipe = recipe;
                        continue;
                    }
                    try
                    {
                        if (!SynthesisProduction.TryProcess(machine, battery, input, output, recipe)) continue;
                        produced = true; break;
                    }
                    catch (Exception error) { SynthesisMod.Warn($"Stopped {definition.Code}: {error}"); return; }
                }
                if (!produced)
                foreach (var recipe in ExtraRecipes.ForMachine(definition.ItemId))
                {
                    try
                    {
                        if (!SynthesisProduction.TryProcess(machine, battery, input, output, recipe)) continue;
                        produced = true; break;
                    }
                    catch (Exception error) { SynthesisMod.Warn($"Stopped extra recipe {recipe.Id}: {error}"); return; }
                }
                // P16 is a fallback after both the other fixed recipes and the extension recipes.
                if (!produced && mouseTrapRecipe != null)
                {
                    try { produced = SynthesisProduction.TryProcess(machine, battery, input, output, mouseTrapRecipe); }
                    catch (Exception error) { SynthesisMod.Warn($"Stopped {mouseTrapRecipe.Id}: {error}"); return; }
                }
                if (!produced) break;
                try { MachineHelper.OnMachineActioned(machine, modules); }
                catch (Exception error) { SynthesisMod.Warn("Module action failed after production: " + error); return; }
            }
        };
        return machine;
    }

}
