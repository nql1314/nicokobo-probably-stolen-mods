using Nicokobo.ContentInterop;
using MechcoreProtocol.SynthesisExpansion;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.ModuleMatrix.Definitions;

internal static class RareUpgradeChecks
{
    private static GameItem Basic(string id, int count = 1)
    {
        var item = new GameItem { identifier = id, unitCount = count, unitValue = 70 };
        item.SetGameItemType(id is "node_small" or "node_medium" ? "NODE" : "MODULE");
        GeneralHelper.SetItemOwned(item, true); return item;
    }
    private static (GameItem Machine, GameSlotInventory Battery, GameGridInventory Input, GameGridInventory Output) Setup()
    {
        PowerHelper.RefuseDraw = PowerHelper.ThrowAfterDebit = DirectoryMaster.FailCleanup = false;
        DirectoryMaster.Factory = null;
        var machine = SynthesisMachines.Create(SynthesisDefinitions.Machine(ModuleTierDefinitions.UpgradeMachineId));
        GeneralHelper.SetItemOwned(machine, true);
        var battery = (GameSlotInventory)machine.Storages[0]!;
        var cell = new GameItem(); cell.GetTagReadonly("energy").SetInt(1000); battery.UncheckedAccept(cell);
        return (machine, battery, (GameGridInventory)machine.Storages[2]!, (GameGridInventory)machine.Storages[3]!);
    }
    internal static void Run(Action<bool,string> check)
    {
        var matrixOwner = MatrixMod.Id; var synthOwner = SynthesisDefinitions.OwnerId;
        MatrixMod.Disabled.Clear(); MatrixMod.Active = true; DirectoryMaster.AvailableFactory = null;
        ContentRuntime.Attach(matrixOwner); MatrixInterop.Register();
        try
        {
            check(ExtraRecipes.ForMachine(ModuleTierDefinitions.UpgradeMachineId).Count == 0,
                "Upgrade recipe appeared before K02's production provider was registered");
            ContentRuntime.Attach(synthOwner); SynthesisInterop.Register();
            var recipe = ExtraRecipes.ForMachine(ModuleTierDefinitions.UpgradeMachineId).Single();
            check(recipe.Id == ModuleTierDefinitions.UpgradeRecipeId && MatrixInterop.RarePool().Count == 13,
                "Rare upgrade identity or complete 13-kind pool changed");
            foreach (string id in new[] { "system_module_performance", "system_module_efficiancy", "system_module_quality", "node_small", "node_medium", FixedModuleDefinitions.M01.Id })
                check(MatrixInterop.IsBasic(Basic(id)), "Eligible basic module/node rejected: " + id);
            foreach (string id in new[] { "system_module_ruined", "system_module_corrupt", "system_capped_neural_core", "furnace_module_blast",
                MachineModuleDefinitions.All[0].Id, FixedModuleDefinitions.M05.Id })
                check(!MatrixInterop.IsBasic(Basic(id)), "Special/functional/rare item admitted as Basic: " + id);
            var unowned = Basic("node_small"); GeneralHelper.SetItemOwned(unowned,false);
            check(!MatrixInterop.IsBasic(unowned), "Unowned module accepted by provider predicate");

            var expected = MatrixInterop.RarePool().ToArray();
            for (int index = 0; index < expected.Length; index++)
            {
                var state = Setup(); state.Input.UncheckedAccept(Basic("node_small")); state.Input.UncheckedAccept(Basic(FixedModuleDefinitions.M01.Id));
                RNG.Indices.Enqueue(index);
                bool made = SynthesisProduction.TryProcess(state.Machine,state.Battery,state.Input,state.Output,recipe);
                check(made &&
                    state.Output.childItems.Single().identifier == expected[index] && state.Input.childItems.Count == 0 &&
                    state.Battery.childItem!.GetTagReadonly("energy").GetInt() == 985,
                    $"Equal-kind draw index {index}: made={made}, input={state.Input.childItems.Count}, output={string.Join(",",state.Output.childItems.Select(item=>item.identifier))}, expected={expected[index]}, energy={state.Battery.childItem!.GetTagReadonly("energy").GetInt()}");
            }
            var excluded = ContinuousModuleDefinitions.M08.Module.Id;
            MatrixMod.Disabled.Add(excluded);
            check(!MatrixInterop.RarePool().Contains(excluded) && MatrixInterop.RarePool().Count == 12, "Disabled provider item stayed in rare pool");
            MatrixMod.Disabled.Clear(); DirectoryMaster.AvailableFactory = id => id != excluded;
            check(!MatrixInterop.RarePool().Contains(excluded), "Unavailable factory stayed in rare pool");
            DirectoryMaster.AvailableFactory = null;
            foreach (string failure in new[] { "full", "draw", "missing ingredient", "invalid output", "throwing factory" })
            {
                var state = Setup(); var source = Basic("system_module_performance",failure == "missing ingredient" ? 1 : 2); state.Input.UncheckedAccept(source);
                if(failure == "full") state.Output.Capacity = 0;
                if(failure == "draw") PowerHelper.RefuseDraw = true;
                if(failure == "throwing factory") DirectoryMaster.Factory = _ => throw new InvalidOperationException("Factory failed");
                var selected = failure == "invalid output" ? new ExtraRecipe("checks.invalid",recipe.MachineId,recipe.Ingredients,recipe.OutputItemId,
                    recipe.ChineseOutput,recipe.EnglishOutput,()=>true,()=>null!) : recipe;
                bool produced=false;
                try { produced=SynthesisProduction.TryProcess(state.Machine,state.Battery,state.Input,state.Output,selected); }
                catch(InvalidOperationException) when(failure is "invalid output" or "throwing factory") { }
                check(!produced && !source.Destroyed && source.parentInventory==state.Input && source.unitCount==(failure=="missing ingredient"?1:2) &&
                    state.Output.childItems.Count==0 && state.Battery.childItem!.GetTagReadonly("energy").GetInt()==1000,
                    "Upgrade failure lost inputs, power or created output: "+failure);
            }
            var cycles = Setup();
            cycles.Machine.GetTagReadonly("performance").SetInt(300);
            cycles.Input.UncheckedAccept(Basic("system_module_performance", 4));
            foreach (var rule in SynthesisDefinitions.Recipes.Where(recipe => recipe.Code is "P15" or "P16")
                .SelectMany(recipe => recipe.ItemInputs))
            {
                var material = new GameItem { identifier = rule.ResourceId, unitCount = rule.Amount, unitValue = 10 };
                GeneralHelper.SetItemOwned(material, true);
                cycles.Input.UncheckedAccept(material);
            }
            cycles.Machine.onCycleEndSlotItemFunc!(cycles.Machine, cycles.Input, new());
            var outputIds = cycles.Output.childItems.Select(item => item.identifier).ToArray();
            check(outputIds.Length == 4 && outputIds[0] == SynthesisDefinitions.CommonElectronicId &&
                outputIds.Skip(1).Take(2).All(expected.Contains) && outputIds[3] == SynthesisDefinitions.MouseTrapId &&
                cycles.Machine.ActionCalls == 4 && cycles.Battery.childItem!.GetTagReadonly("energy").GetInt() == 940,
                "K02 should produce electronics, two rare upgrades, then the mouse trap across four Performance batches");
            check(MachineCatalog.Recipes(ModuleTierDefinitions.UpgradeMachineId).Count(view=>view.Id==recipe.Id)==1 &&
                RecipeConfiguration.DefaultDocument().Recipes.Count==30, "Extra upgrade duplicated or rewrote schema-25 configuration");

            ContentRuntime.Detach(matrixOwner);
            check(ExtraRecipes.ForMachine(recipe.MachineId).Count==0 && MachineCatalog.Contains(recipe.MachineId),
                "Detaching Matrix left an active upgrade or removed Synthesis's machine");
            ContentRuntime.Attach(matrixOwner); MatrixInterop.Register();
            check(ExtraRecipes.ForMachine(recipe.MachineId).Count==1, "Reverse provider registration order lost the upgrade");
        }
        finally
        {
            ContentRuntime.Detach(matrixOwner); ContentRuntime.Detach(synthOwner);
            MatrixMod.Disabled.Clear(); DirectoryMaster.AvailableFactory=null; DirectoryMaster.Factory=null;
            PowerHelper.RefuseDraw=PowerHelper.ThrowAfterDebit=false; RNG.Indices.Clear();
        }
    }
}
