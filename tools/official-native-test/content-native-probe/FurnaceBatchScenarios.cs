using Newtonsoft.Json.Linq;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const string FurnaceBatchModuleId = "nicokobo.mechcore_protocol.module_matrix.batch_smelting_module";

    private IEnumerable<object?> FurnaceBatchScenarios()
    {
        R.phase = "s26-native-furnace-batches";
        object[] recipes = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", "furnace")).ToArray();
        R.Check("s26.live-furnace-recipes", "Native metal, blast and junk plus registered glass recipes",
            recipes.Select(recipe => Reflect.Str(recipe, "Id")).ToArray(), recipes.Length >= 3);
        foreach (object view in recipes)
        {
            string id = Reflect.Str(view, "Id");
            try
            {
                using (var f = CreateRecipeFixture(view, retain: false))
                {
                    if (f.Modules == null) throw new FixtureUnavailable("Native furnace module bay missing");
                    var module = Spawn(FurnaceBatchModuleId);
                    Place(f.Modules, module, "s26-native:" + id, f.Machine);
                    R.Check("s26.real-module." + id, "S26 placed in the real native furnace module bay",
                        ItemState(module), module.parentInventory == f.Modules && GeneralHelper.IsItemOwned(module));

                    // Keep junk's full six-piece input batch inside its native
                    // bay; metal and glass also exercise two batches per call.
                    int batches = id.EndsWith(".junk", StringComparison.Ordinal) ? 1 : 2;
                    if (batches == 2)
                    foreach (object rule in Reflect.Each(Reflect.Get(view, "Ingredients")))
                    for (int index = 0; index < Reflect.Int(rule, "Amount"); index++)
                        Place(f.Input, Ingredient(rule), "s26-second-input:" + id, f.Machine);

                    int required = Reflect.Each(Reflect.Get(view, "Ingredients")).Sum(rule => Reflect.Int(rule, "Amount"));
                    int expectedOutput = batches * Reflect.Int(view, "OutputCount");
                    string outputId = Reflect.Str(view, "OutputItemId");
                    int inputBefore = f.Input.childItems.Sum(item => item.unitCount), energyBefore = Energy(f.Battery);
                    int power = MachineryHelper.GetMachinePowerUsage(f.Machine);
                    JToken admissionBefore = OutputAdmissionState(f);
                    bool completed = Process(f);
                    int inputDebit = inputBefore - f.Input.childItems.Sum(item => item.unitCount);
                    int outputCount = f.Output.childItems.Sum(item => item.unitCount);
                    int powerDebit = energyBefore - Energy(f.Battery);
                    R.Check("s26.actual-native-batches." + id,
                        new { batches, inputDebit = batches * required, outputCount = expectedOutput, powerDebit = batches * power, outputAdmissionRestored = true },
                        new { completed, inputDebit, outputCount, powerDebit, outputAdmissionRestored = JToken.DeepEquals(admissionBefore, OutputAdmissionState(f)) },
                        completed && inputDebit == batches * required && outputCount == expectedOutput && powerDebit == batches * power &&
                        JToken.DeepEquals(admissionBefore, OutputAdmissionState(f)) &&
                        f.Output.childItems.All(item => item.identifier == outputId && item.parentInventory == f.Output && GeneralHelper.IsItemOwned(item)),
                        "One real native onCycleEnd callback with a physically installed S26; resource expectations use complete declared recipe batches.");

                    JToken beforeRetry = TransactionState(f);
                    bool retried = Process(f);
                    R.Check("s26.no-input-stops." + id, "No repeated output, debit, UID or admission change without new input",
                        new { retried, unchanged = JToken.DeepEquals(beforeRetry, TransactionState(f)) },
                        !retried && JToken.DeepEquals(beforeRetry, TransactionState(f)));
                }
                CheckOutputCapacity(view, "s26." + id, batchModule: true);
            }
            catch (Exception error)
            {
                R.Check("s26.native-furnace." + id, "Real S26 production and whole-batch capacity refusal", null, false,
                    "No duplicated production algorithm or manually inserted outputs", error.ToString());
            }
            yield return null;
        }
        foreach (object? wait in FurnaceRefillCapacityScenarios()) yield return wait;
    }

    private IEnumerable<object?> FurnaceRefillCapacityScenarios()
    {
        R.phase = "native-furnace-refill-capacity";
        foreach (bool batchModule in new[] { false, true })
        foreach (string kind in new[] { "blast", "junk" })
        {
            string name = kind + (batchModule ? ".s26" : ".ordinary");
            object view = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", "furnace"))
                .Single(recipe => Reflect.Str(recipe, "Id").EndsWith(".native.furnace." + kind, StringComparison.Ordinal));
            using var f = CreateRecipeFixture(view, retain: false);
            try
            {
                if (f.Modules == null) throw new FixtureUnavailable("Native furnace module bay missing");
                Place(f.Modules, Spawn("nicokobo.automated_logistics.auto_feeder"), "native-refill-feeder:" + name, f.Machine);
                Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false);
                int required = Reflect.Each(Reflect.Get(view, "Ingredients")).Sum(rule => Reflect.Int(rule, "Amount"));
                int producedPerBatch = Reflect.Int(view, "OutputCount");
                int fillingBatches = kind == "blast" ? 4 : 2;
                // The retained Build 26284217 failure has nine physical output
                // slots: four blast/two junk batches leave eight real products.
                for (int batch = 0; batch < fillingBatches; batch++)
                {
                    if (batch != 0)
                    foreach (object rule in Reflect.Each(Reflect.Get(view, "Ingredients")))
                    for (int index = 0; index < Reflect.Int(rule, "Amount"); index++)
                        Place(f.Input, Ingredient(rule), "native-refill-warmup:" + name, f.Machine);
                    int energy = Energy(f.Battery), power = MachineryHelper.GetMachinePowerUsage(f.Machine);
                    bool completed = Process(f);
                    bool valid = completed && f.Input.childItems.Count == 0 &&
                        f.Output.childItems.Sum(item => item.unitCount) == (batch + 1) * producedPerBatch && Energy(f.Battery) == energy - power;
                    R.Check("native-refill.real-warmup." + name + "." + batch,
                        new { inputUnits = 0, outputUnits = (batch + 1) * producedPerBatch, energyDebit = power },
                        new { completed, inputUnits = f.Input.childItems.Sum(item => item.unitCount), outputUnits = f.Output.childItems.Sum(item => item.unitCount), energyDebit = energy - Energy(f.Battery) }, valid,
                        "Real ordinary native cycles create all outputs and consumption debt; no output insertion, demand writes or added cycle opportunities.");
                    if (!valid) throw new InvalidOperationException("Native refill warmup did not establish the required real capacity fixture");
                }
                Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false);
                var debt = LogisticsState["Machines"]?[f.Machine.uniqueId.ToString()]?["PendingItems"] as JArray;
                bool realDebt = debt != null && debt.OfType<JObject>().Sum(item => (int)item["Remaining"]!) == required;
                R.Check("native-refill.real-consumption-debt." + name, required, debt, realDebt);
                if (!realDebt) throw new InvalidOperationException("Native furnace did not record one consumed batch of debt");
                if (batchModule) Place(f.Modules, Spawn(FurnaceBatchModuleId), "native-refill-s26:" + name, f.Machine);
                var resources = (GameGridInventory?)Reflect.Call(Logistics, "ResourceRegion")
                    ?? throw new FixtureUnavailable("Real unlocked resource storage unavailable");
                var sources = new List<GameItem>();
                foreach (object rule in Reflect.Each(Reflect.Get(view, "Ingredients")))
                for (int index = 0; index < Reflect.Int(rule, "Amount"); index++)
                {
                    var source = Ingredient(rule);
                    Place(resources, source, "native-refill-stock:" + name);
                    sources.Add(source);
                }
                int[] stockUids = sources.Select(item => item.uniqueId).OrderBy(uid => uid).ToArray();
                int[] productUids = f.Output.childItems.Select(item => item.uniqueId).OrderBy(uid => uid).ToArray();
                int energyBefore = Energy(f.Battery);
                JToken admissionBefore = OutputAdmissionState(f);
                Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false);
                bool ready = f.Input.childItems.Count == 0 && productUids.Length == 8 && sources.All(item => item.parentInventory == resources);
                R.Check("native-refill.empty-input-before-real-callback." + name,
                    "Eight real outputs, empty input and a real consumed-batch demand; source UIDs still in resource storage",
                    new { inputUnits = f.Input.childItems.Sum(item => item.unitCount), productUids, stockUids }, ready);
                if (!ready) throw new FixtureUnavailable("Native refill capacity fixture changed before its actual callback");
                bool produced = Process(f);
                int[] inputUids = f.Input.childItems.Select(item => item.uniqueId).OrderBy(uid => uid).ToArray();
                bool atomic = !produced && Energy(f.Battery) == energyBefore && inputUids.SequenceEqual(stockUids) &&
                    f.Input.childItems.Sum(item => item.unitCount) == required &&
                    f.Output.childItems.Select(item => item.uniqueId).OrderBy(uid => uid).SequenceEqual(productUids) &&
                    JToken.DeepEquals(admissionBefore, OutputAdmissionState(f));
                R.Check("native-refill.capacity-after-real-fill." + name,
                    new { produced = false, energyDebit = 0, inputUids = stockUids, outputUids = productUids, outputAdmissionRestored = true },
                    new { produced, energyDebit = energyBefore - Energy(f.Battery), inputUids, outputUids = f.Output.childItems.Select(item => item.uniqueId).ToArray(), outputAdmission = OutputAdmissionState(f) }, atomic,
                    "The machine callback itself must refill real debt, then reject the entire native batch. Refill may move inputs but cannot consume them, debit power or partially deliver output.");
                if (!atomic) throw new InvalidOperationException("Native capacity protection failed after the callback's actual feeder refill");

                for (int index = 0; index < producedPerBatch - 1; index++)
                {
                    var output = f.Output.childItems[0];
                    f.Output.Expel(output); ModHelper.MoveItemToInventory(output);
                    if (output.parentInventory == null) throw new FixtureUnavailable("Cannot normally remove produced material to free output space");
                }
                bool retried = Process(f);
                bool recovered = retried && f.Input.childItems.Count == 0 && f.Output.childItems.Sum(item => item.unitCount) == 9 &&
                    Energy(f.Battery) == energyBefore - MachineryHelper.GetMachinePowerUsage(f.Machine) &&
                    JToken.DeepEquals(admissionBefore, OutputAdmissionState(f));
                R.Check("native-refill.recovery-after-freeing-output." + name,
                    "The same actual refilled source UIDs produce one complete batch after normal removal creates space",
                    new { retried, inputUnits = f.Input.childItems.Sum(item => item.unitCount), outputUnits = f.Output.childItems.Sum(item => item.unitCount), energyDebit = energyBefore - Energy(f.Battery) }, recovered);
            }
            catch (Exception error)
            {
                R.Check("native-refill.scenario." + name, "Real debt/refill/whole-output refusal and recovery", null, false,
                    "No fabricated inventory admission, output shape, material demand or production opportunity", error.ToString());
            }
            finally { Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false); }
            yield return null;
        }

        R.phase = "s26-real-refill-multiple-batches";
        object metal = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", "furnace"))
            .Single(recipe => Reflect.Str(recipe, "Id").EndsWith(".native.furnace.metal", StringComparison.Ordinal));
        using var loop = CreateRecipeFixture(metal, retain: false);
        try
        {
            if (loop.Modules == null) throw new FixtureUnavailable("Native furnace module bay missing");
            Place(loop.Modules, Spawn("nicokobo.automated_logistics.auto_feeder"), "s26-refill-loop-feeder", loop.Machine);
            Reflect.Call(Logistics, "ConfigureMachine", loop.Machine, true, false);
            bool seeded = Process(loop);
            int required = Reflect.Each(Reflect.Get(metal, "Ingredients")).Sum(rule => Reflect.Int(rule, "Amount"));
            var debt = LogisticsState["Machines"]?[loop.Machine.uniqueId.ToString()]?["PendingItems"] as JArray;
            bool seedValid = seeded && loop.Input.childItems.Count == 0 &&
                loop.Output.childItems.Sum(item => item.unitCount) == Reflect.Int(metal, "OutputCount") &&
                debt != null && debt.OfType<JObject>().Sum(item => (int)item["Remaining"]!) == required;
            R.Check("s26.real-refill-loop.seed-debt", "One actual ordinary batch establishes real consumed-material demand", debt, seedValid);
            if (!seedValid) throw new InvalidOperationException("S26 refill loop lacks its real consumed-batch demand");
            Reflect.Call(Logistics, "ConfigureMachine", loop.Machine, false, false);
            var resources = (GameGridInventory?)Reflect.Call(Logistics, "ResourceRegion")
                ?? throw new FixtureUnavailable("Real resource storage unavailable");
            const int batches = 3;
            var stock = new List<GameItem>();
            foreach (object rule in Reflect.Each(Reflect.Get(metal, "Ingredients")))
            for (int index = 0; index < batches * Reflect.Int(rule, "Amount"); index++)
            {
                var source = Ingredient(rule); Place(resources, source, "s26-refill-loop-stock"); stock.Add(source);
            }
            int[] stockUids = stock.Select(item => item.uniqueId).ToArray();
            Place(loop.Modules, Spawn(FurnaceBatchModuleId), "s26-refill-loop-module", loop.Machine);
            Reflect.Call(Logistics, "ConfigureMachine", loop.Machine, true, false);
            if (loop.Input.childItems.Count != 0 || stock.Any(item => item.parentInventory != resources))
                throw new FixtureUnavailable("S26 stock moved before its actual native callback");
            int energy = Energy(loop.Battery), power = MachineryHelper.GetMachinePowerUsage(loop.Machine);
            int outputBefore = loop.Output.childItems.Sum(item => item.unitCount);
            JToken admission = OutputAdmissionState(loop);
            bool produced = Process(loop);
            int remainingStock = resources.childItems.Concat(loop.Input.childItems)
                .Where(item => stockUids.Contains(item.uniqueId)).Sum(item => item.unitCount);
            int outputDelta = loop.Output.childItems.Sum(item => item.unitCount) - outputBefore;
            R.Check("s26.real-refill-loop.three-complete-batches",
                new { batches, consumed = batches * required, outputDelta = batches * Reflect.Int(metal, "OutputCount"), energyDebit = batches * power, remainingStock = 0 },
                new { produced, outputDelta, energyDebit = energy - Energy(loop.Battery), remainingStock, inputUnits = loop.Input.childItems.Sum(item => item.unitCount) },
                produced && remainingStock == 0 && loop.Input.childItems.Count == 0 &&
                outputDelta == batches * Reflect.Int(metal, "OutputCount") && energy - Energy(loop.Battery) == batches * power &&
                JToken.DeepEquals(admission, OutputAdmissionState(loop)),
                "One actual S26 callback repeatedly refills the previous actual consumption debt. Consumption must be captured after refill even when input is empty before and after every batch.");
            JToken stopped = TransactionState(loop);
            bool retried = Process(loop);
            R.Check("s26.real-refill-loop.stops-with-empty-stock", "No extra production or debit after stock is exhausted",
                new { retried, unchanged = JToken.DeepEquals(stopped, TransactionState(loop)) },
                !retried && JToken.DeepEquals(stopped, TransactionState(loop)));
        }
        catch (Exception error)
        {
            R.Check("s26.real-refill-loop.scenario", "Three genuinely refilled native batches and then stop", null, false,
                "No manual input refill, manufactured demand or added native opportunities during the actual callback", error.ToString());
        }
        finally { Reflect.Call(Logistics, "ConfigureMachine", loop.Machine, false, false); }
        yield return null;
    }
}
