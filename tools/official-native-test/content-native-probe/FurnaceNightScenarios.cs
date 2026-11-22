using Newtonsoft.Json.Linq;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    internal IEnumerable<object?> FurnaceNight()
    {
        R.phase = "furnace-night-generated-inputs";
        PrepareLogisticsNetworks();
        var powerRegion = (GameGridInventory)Reflect.Call(Logistics, "PowerRegion")!;
        var donor = PreBuiltItemHelper.EnergyCredit(100);
        long gridBeforeDeposit = GridTotal;
        Place(powerRegion, donor, "furnace-night-grid-donor");
        Reflect.Call(LogisticsRuntime, "CollectGridEnergy");
        R.Check("furnace-night.grid-fixture-deposit", 100, GridTotal - gridBeforeDeposit, GridTotal - gridBeforeDeposit == 100);
        object recipe = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", "furnace"))
            .Single(view => Reflect.Str(view, "Id").EndsWith(".native.furnace.metal", StringComparison.Ordinal));
        var fixtures = new[] { CreateRecipeFixture(recipe, retain: true), CreateRecipeFixture(recipe, retain: true) };
        for (int index = 0; index < fixtures.Length; index++)
        {
            var fixture = fixtures[index];
            Retain(fixture.Machine);
            // Four physical native metal inputs, enough for two ordinary batches.
            foreach (object rule in Reflect.Each(Reflect.Get(recipe, "Ingredients")))
            for (int count = 0; count < Reflect.Int(rule, "Amount"); count++)
                Place(fixture.Input, Ingredient(rule), "furnace-night-second-batch", fixture.Machine);
            if (index == 0) Place(fixture.Modules!, Spawn(FurnaceBatchModuleId), "furnace-night-s26", fixture.Machine);
        }
        string sourceFixture = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(O.Output))!, "user-furnace-fixture.json");
        if (File.Exists(sourceFixture))
        {
            var existing = Store.FindAllItem().ToHashSet();
            Store.DecodeSaveItem(File.ReadAllText(sourceFixture), EmporiumEntry.Instance.invElement);
            var imported = Store.FindAllItem().Where(item => !existing.Contains(item)).ToArray();
            var machine = imported.Single(item => item.identifier == "furnace");
            Store.currentUniqueId = Math.Max(Store.currentUniqueId, checked((int)imported.Max(item => item.uniqueId)));
            Retain(machine);
            fixtures = fixtures.Concat(new[] { new RecipeFixture { Machine = machine, View = recipe, Retained = true,
                Input = (GameInventory)MachineHelper.GetBodyStorage(machine, 2), Output = (GameInventory)MachineHelper.GetBodyStorage(machine, 3),
                Modules = (GameInventory)MachineHelper.GetBodyStorage(machine, 1), Battery = ((GameSlotInventory)MachineHelper.GetBodyStorage(machine, 0)).childItem } }).ToArray();
            R.environment["userFurnaceFixture"] = new { sourceFixture, sha256 = Reflect.HashFile(sourceFixture), machine.uniqueId,
                scope = "Read-only extraction from save_43; native DecodeSaveItem; existing input, modules, battery and output preserved as fixture inputs" };
        }
        var gridFixture = CreateRecipeFixture(recipe, battery: false, retain: true);
        gridFixture.Battery = PreBuiltItemHelper.EnergyCredit(0);
        Place((GameSlotInventory)MachineHelper.GetBodyStorage(gridFixture.Machine, 0), gridFixture.Battery, "furnace-night-empty-battery", gridFixture.Machine);
        foreach (object rule in Reflect.Each(Reflect.Get(recipe, "Ingredients")))
        for (int count = 0; count < Reflect.Int(rule, "Amount"); count++)
            Place(gridFixture.Input, Ingredient(rule), "furnace-night-grid-second-batch", gridFixture.Machine);
        Place(gridFixture.Modules!, Spawn(FurnaceBatchModuleId), "furnace-night-grid-s26", gridFixture.Machine);
        Retain(gridFixture.Machine);
        fixtures = fixtures.Concat(new[] { gridFixture }).ToArray();
        long gridBeforeNight = GridTotal;
        int[] inputBefore = fixtures.Select(fixture => fixture.Input.childItems.Sum(item => item.unitCount)).ToArray();
        int[] outputBefore = fixtures.Select(fixture => fixture.Output.childItems.Sum(item => item.unitCount)).ToArray();
        int[] energy = fixtures.Select(fixture => Energy(fixture.Battery)).ToArray();
        int[] power = fixtures.Select(fixture => MachineryHelper.GetMachinePowerUsage(fixture.Machine)).ToArray();
        var matrix = Reflect.Type("MechcoreProtocol.ModuleMatrix.MatrixMachines", "MechcoreProtocol");
        R.environment["furnaceNightInputs"] = fixtures.Select((fixture, index) => new
        {
            role = fixture == gridFixture ? "S26-empty-battery-powered-grid" : index == 0 ? "S26-two-batches" : index == 1 ? "ordinary-one-batch" : "user-save-furnace",
            state = NightMachineState(fixture), energy = energy[index], power = power[index],
            installed = Reflect.Each(Reflect.Call(matrix, "Installed", fixture.Machine, "S26")).Cast<GameItem>().Select(item => item.uniqueId).ToArray(),
            callback = fixture.Machine.onCycleEndSlotItemFunc.Method.ToString(),
            target = fixture.Machine.onCycleEndSlotItemFunc.Target?.GetType().FullName
        }).ToArray();
        Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
        yield return null;
        int day = StoreStation.GetDayCounter();
        using (var observation = new NightObservation(this, fixtures, day))
        {
            R.environment["furnaceNightObservation"] = observation.Evidence;
            foreach (object? wait in NightDay(day, "furnace-night", observation)) yield return wait;
            for (int index = 0; index < fixtures.Length; index++)
            {
                var fixture = fixtures[index]; int batches = index == 1 ? 1 : inputBefore[index] / 2;
                int expectedLocal = fixture == gridFixture ? 0 : energy[index] - batches * power[index];
                R.Check("furnace-night.actual-batches." + index,
                    new { input = inputBefore[index] - batches * 2, output = outputBefore[index] + batches, energy = expectedLocal },
                    new { state = NightMachineState(fixture), work = observation.Work.Where(row => (int?)row["uid"] == fixture.Machine.uniqueId).ToArray() },
                    fixture.Input.childItems.Sum(item => item.unitCount) == inputBefore[index] - batches * 2 &&
                    fixture.Output.childItems.Sum(item => item.unitCount) == outputBefore[index] + batches &&
                    fixture.Output.childItems.All(item => item.identifier == "metal_ingot") && Energy(fixture.Battery) == expectedLocal,
                    "Generated only inputs; real shutter/bedroll/End Day/EndNight performs production, never direct cycle calls.");
            }
            R.Check("furnace-night.empty-battery-grid-debit", new { local = 0, gridDebit = 16 },
                new { local = Energy(gridFixture.Battery), gridDebit = gridBeforeNight - GridTotal },
                Energy(gridFixture.Battery) == 0 && gridBeforeNight - GridTotal == 16,
                "Two real native batches must debit 8 kJ each from the grid; the empty physical battery remains empty.");
            R.persistence["furnaceNight"] = new JObject { ["dayAfter"] = StoreStation.GetDayCounter(),
                ["fixtures"] = new JArray(fixtures.Select(NightMachineState)), ["automaticSave"] = observation.AutoSave!.DeepClone() };
        }
        foreach (var fixture in fixtures) fixture.Dispose();
        // Keep the existing direct-cycle furnace recipe and rejection checks as a comparison.
        foreach (object? wait in FurnaceBatchScenarios()) yield return wait;
    }
}
