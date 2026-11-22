using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private sealed class RecipeFixture : IDisposable
    {
        internal GameItem Machine = null!, Battery = null!;
        internal GameInventory Input = null!, Output = null!;
        internal GameInventory? Modules;
        internal GameSlotInventory? WaterSlot;
        internal GameItem? Water;
        internal GameItem? NativeAdmissionMaterial;
        internal object View = null!;
        internal readonly List<(GameItem Item, int Required)> Inputs = new();
        internal bool Retained;
        public void Dispose()
        {
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(NativeAdmissionMaterial);
            if (!Retained) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(Machine);
        }
    }
    private readonly Dictionary<string, string[]> _fixtureAlternatives = new(StringComparer.Ordinal)
    {
        ["BIO_PRINTABLE_TAG"] = new[] { "small_raw_meat", "raw_meat" },
        ["CONTAINER_TAG"] = new[] { "storage_bay" },
        ["fish_fillet"] = new[] { "fish_fillet", "small_fish_fillet", "large_fish_fillet" },
        ["eel_fillet"] = new[] { "eel_fillet", "small_eel_fillet" },
        ["system_module_performance"] = new[] { "system_module_performance" }
        , ["alcohol_base"] = new[] { "red_beer", "nudka" }
        , ["edible_mushroom"] = new[] { "meal_cap", "dream_cap" }
        , ["small_meat_cut"] = new[] { "morsel", "small_morsel" }
        , [""] = new[] { "cassette3d", "cassette4d", "cassette5d", "cassette6d" }
    };
    internal IEnumerable<object?> Production()
    {
        R.phase = "live-registered-production-catalog";
        PrepareLogisticsNetworks();
        foreach (object? wait in NativePurifierScenarios()) yield return wait;
        string[] machines = Reflect.Each(Reflect.Call(MachineCatalog, "MachineIds")).Cast<string>().ToArray();
        R.Check("production.catalog-nonempty", "Current registered machines and recipes", machines, machines.Length >= 6);
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (string machineId in machines)
        {
            var recipes = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", machineId)).ToArray();
            foreach (object view in recipes)
            {
                string recipeId = Reflect.Str(view, "Id");
                if (!done.Add(recipeId)) { R.Check("production.unique." + recipeId, "Unique live recipe ID", "duplicate", false); continue; }
                R.phase = "production:" + recipeId;
                RecipeFixture? fixture = null;
                try
                {
                    fixture = CreateRecipeFixture(view);
                    ObserveBatch(fixture, recipeId, false);
                    CheckNoPower(view, recipeId);
                    CheckMissingInput(view, recipeId);
                    if (Reflect.Int(view, "WaterParts") > 0) CheckNoWater(view, recipeId);
                    CheckOutputCapacity(view, recipeId);
                }
                catch (FixtureUnavailable error) { R.Skip("production." + recipeId, error.Message); }
                catch (Exception error) { R.Check("production." + recipeId, "Native production and atomic failure", null, false, "Actual loaded production methods", error.ToString()); }
                finally { fixture?.Dispose(); }
                yield return null;
            }
            yield return null;
        }
        R.environment["registeredRecipeIds"] = done.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        foreach (object? wait in FurnaceBatchScenarios()) yield return wait;
        foreach (object? wait in Matrix()) yield return wait;
        foreach (object? wait in LogisticsScenarios()) yield return wait;
    }
    private sealed class FixtureUnavailable(string message) : Exception(message) { }
    private RecipeFixture CreateRecipeFixture(object view, bool battery = true, bool ingredients = true, bool water = true, bool retain = false)
    {
        string machineId = Reflect.Str(view, "MachineId");
        var f = new RecipeFixture { View = view, Machine = Spawn(machineId) };
        try
        {
            object ports = Ports(f.Machine);
            f.Input = (GameInventory)Reflect.Get(ports, "Input")!; f.Output = (GameInventory)Reflect.Get(ports, "Output")!;
            f.Modules = Reflect.Get(ports, "Modules") as GameInventory;
            f.WaterSlot = Reflect.Get(ports, "Water") as GameSlotInventory;
            if (machineId == "furnace") f.NativeAdmissionMaterial = Spawn("metal_ingot");
            ModHelper.MoveItemToInventory(f.Machine);
            if (f.Machine.parentInventory == null) ModHelper.MoveItemToBackCounter(f.Machine);
            if (f.Machine.parentInventory == null) throw new FixtureUnavailable("Native main/back storage cannot fit machine " + machineId);
            string recipeId = Reflect.Str(view, "Id");
            bool acceptsBlast = machineId is "furnace" or "nicokobo.mechcore.synthesis.material_refiner";
            string? furnaceModule = acceptsBlast && recipeId.IndexOf("blast", StringComparison.OrdinalIgnoreCase) >= 0
                ? "furnace_module_blast" : machineId == "furnace" && recipeId.EndsWith(".junk", StringComparison.Ordinal) ? "furnace_module_junk" : null;
            if (furnaceModule != null)
            {
                if (f.Modules == null) throw new FixtureUnavailable("Furnace recipe has no native module inventory");
                Place(f.Modules, Spawn(furnaceModule), "furnace-module:" + recipeId, f.Machine);
            }
            if (battery)
            {
                var slot = Reflect.Get(ports, "Battery") as GameSlotInventory ?? throw new FixtureUnavailable("No real battery slot");
                f.Battery = PreBuiltItemHelper.EnergyCredit(100);
                Place(slot, f.Battery, "battery:" + machineId, f.Machine);
            }
            if (ingredients)
            foreach (object rule in Reflect.Each(Reflect.Get(view, "Ingredients")))
            {
                int count = Reflect.Int(rule, "Amount");
                GameItem item = Ingredient(rule);
                int max = item.GetTagReadonly("MAX_STACK_COUNT_TAG").GetInt();
                // Preserve a real stack where the item supports it. For genuine
                // nonstackable items create the declared number of instances.
                if (item.mayStackItemFunc != null && count > 1)
                {
                    item.SetUnitCount(count); Place(f.Input, item, "stacked-input:" + Reflect.Str(view, "Id"), f.Machine); f.Inputs.Add((item, count));
                }
                else
                {
                    Place(f.Input, item, "input:" + Reflect.Str(view, "Id"), f.Machine); f.Inputs.Add((item, 1));
                    for (int i = 1; i < count; i++) { var next = Ingredient(rule); Place(f.Input, next, "input:" + Reflect.Str(view, "Id"), f.Machine); f.Inputs.Add((next, 1)); }
                }
            }
            int parts = Reflect.Int(view, "WaterParts");
            if (parts > 0 && water)
            {
                if (f.WaterSlot == null) throw new FixtureUnavailable("Recipe declares water without a real water slot");
                f.Water = Spawn("water_jug"); WaterHelper.EmptyContainer(f.Water); WaterHelper.AddPureWater(f.Water, parts);
                if (WaterHelper.GetTotalVolume(f.Water) != parts) throw new FixtureUnavailable("Native jug capacity is insufficient for " + parts + " parts");
                Place(f.WaterSlot, f.Water, "water:" + Reflect.Str(view, "Id"), f.Machine);
            }
            f.Retained = retain; return f;
        }
        catch { f.Dispose(); throw; }
    }
    private GameItem Ingredient(object rule)
    {
        string id = Reflect.Str(rule, "ItemId"); var match = (Func<GameItem, bool>)Reflect.Get(rule, "Matches")!;
        var candidates = new[] { id }.Concat(_fixtureAlternatives.TryGetValue(id, out string[] alternatives) ? alternatives : Array.Empty<string>()).Distinct();
        foreach (string candidate in candidates.Where(DirectoryMaster.Has<GameItem>))
        {
            GameItem item = candidate == "large_purple_injector" ? PreBuiltItemHelper.CreateRealGenuineInjector() : Spawn(candidate);
            if (candidate is "metal_ingot" or "nicokobo.mechcore.synthesis.silicon" or "nicokobo.mechcore.synthesis.titanium_alloy")
                IngotPurityHelper.SetPurity(item, "INGOT_PURITY_PERFECT");
            if (candidate == "metal_scanner") Reflect.Call(Reflect.Type("MechcoreProtocol.SynthesisExpansion.SynthesisRobots"), "SetScannerChance", item, 100);
            if (candidate == "wine_berry") WineHelper.SetBerryCondition(item, 2);
            if (match(item)) return item;
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
        }
        throw new FixtureUnavailable("No independently specified legal fixture for ingredient " + id + "; candidate predicate rejected native factory defaults");
    }
    private bool Process(RecipeFixture f)
    {
        string id = Reflect.Str(f.View, "Id");
        if (f.Machine.identifier == "furnace")
        {
            int before = f.Output.childItems.Sum(i => i.unitCount);
            Reflect.Call(Reflect.Type("Nicokobo.ContentInterop.NativeMachineAccess", "MechcoreProtocol"), "Process", f.Machine);
            return f.Output.childItems.Sum(i => i.unitCount) > before;
        }
        object? raw = Reflect.Each(Reflect.Get(Reflect.Type("Nicokobo.FoodProcessing.Definitions.FoodDefinitions"), "Recipes")).FirstOrDefault(x => Reflect.Str(x, "Id") == id);
        if (raw != null)
            return (bool)Reflect.Call(Reflect.Type("Nicokobo.FoodProcessing.FoodProduction"), "TryProcess", f.Machine,
                MachineHelper.GetBodyStorage(f.Machine, 0), f.Input, f.Output, f.WaterSlot, raw)!;
        raw = Reflect.Each(Reflect.Get(Reflect.Type("MechcoreProtocol.SynthesisExpansion.ProductionCatalog"), "AllRecipes")).FirstOrDefault(x => Reflect.Str(x, "Id") == id);
        if (raw != null)
            return (bool)Reflect.Call(Reflect.Type("MechcoreProtocol.SynthesisExpansion.SynthesisProduction"), "TryProcess", f.Machine,
                MachineHelper.GetBodyStorage(f.Machine, 0), f.Input, f.Output, raw)!;
        if (f.View.GetType().FullName == "Nicokobo.ContentInterop.ExtraRecipe")
        {
            // Recipe contracts are source-linked into each client. Ask the
            // machine owner to decode the host payload, even when a third Mod
            // registered the recipe. Food extras cannot use Synthesis pricing.
            bool food = Reflect.Each(Reflect.Get(Reflect.Type("Nicokobo.FoodProcessing.Definitions.FoodDefinitions"), "Machines"))
                .Any(machine => Reflect.Str(machine, "ItemId") == f.Machine.identifier);
            bool synthesis = Reflect.Each(Reflect.Get(Reflect.Type("MechcoreProtocol.SynthesisExpansion.Definitions.SynthesisDefinitions"), "Machines"))
                .Any(machine => Reflect.Str(machine, "ItemId") == f.Machine.identifier);
            if (!food && !synthesis) throw new FixtureUnavailable("No audited owning client for extra recipe " + id);
            var clientCatalog = Reflect.Type("Nicokobo.ContentInterop.MachineCatalog", food ? "Nicokobo.FoodProcessing" : "MechcoreProtocol.SynthesisExpansion");
            var clientView = Reflect.Each(Reflect.Call(clientCatalog, "Recipes", f.Machine.identifier)).Single(x => Reflect.Str(x, "Id") == id);
            if (food)
                return (bool)Reflect.Call(Reflect.Type("Nicokobo.FoodProcessing.FoodProduction"), "TryProcess", f.Machine,
                    MachineHelper.GetBodyStorage(f.Machine, 0), f.Input, f.Output, f.WaterSlot, clientView)!;
            return (bool)Reflect.Call(Reflect.Type("MechcoreProtocol.SynthesisExpansion.SynthesisProduction"), "TryProcess", f.Machine,
                MachineHelper.GetBodyStorage(f.Machine, 0), f.Input, f.Output, clientView)!;
        }
        throw new FixtureUnavailable("No audited real processing adapter for " + id);
    }
    private void ObserveBatch(RecipeFixture f, string name, bool nativeCycle)
    {
        int power = MachineryHelper.GetMachinePowerUsage(f.Machine), energy = Energy(f.Battery);
        int water = f.Water == null ? 0 : WaterHelper.GetTotalVolume(f.Water);
        int expectedWater = Reflect.Int(f.View, "WaterParts"), expectedCount = Reflect.Int(f.View, "OutputCount");
        string outputId = Reflect.Str(f.View, "OutputItemId");
        var admission = f.Output.mayInventoryAddItemFunc;
        JToken admissionBefore = OutputAdmissionState(f);
        int[] beforeInputs = f.Inputs.Select(x => x.Item.unitCount).ToArray();
        long? expectedFoodBatchBasis = null;
        if (expectedCount == 2)
        {
            var foodDefinitions = Reflect.Type("Nicokobo.FoodProcessing.Definitions.FoodDefinitions");
            object? foodRecipe = Reflect.Each(Reflect.Get(foodDefinitions, "Recipes"))
                .SingleOrDefault(recipe => Reflect.Str(recipe, "Id") == Reflect.Str(f.View, "Id"));
            if (foodRecipe != null && !(bool)Reflect.Call(foodDefinitions, "IsNativeConsumable", outputId)! && outputId != "industrial_fish_feed")
            {
                // Price the input invoice before the action. Shared input/native
                // valuation is an input fact; no output or Share result is used
                // to derive the expected total or the allowed rounding spread.
                var valuation = Reflect.Type("Nicokobo.ContentInterop.ProductionValue");
                decimal inputs = f.Inputs.Sum(input => checked(Convert.ToDecimal(Reflect.Call(valuation, "InputValue", input.Item), CultureInfo.InvariantCulture) * input.Required));
                decimal liquid = expectedWater == 0 ? 0m : expectedWater / 25000m *
                    Convert.ToDecimal(Reflect.Call(valuation, "InnateFactor", f.Water!), CultureInfo.InvariantCulture);
                int markup = (int)Reflect.Call(Reflect.Type("Nicokobo.FoodProcessing.Definitions.RecipeConfiguration"), "RecipeMarkup", foodRecipe)!;
                int basePower = f.Machine.GetTagReadonly(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).GetInt();
                expectedFoodBatchBasis = checked((long)decimal.Ceiling((inputs + liquid) * (100m + markup) / 100m + basePower));
            }
        }
        bool committed;
        if (nativeCycle) { NativeCycle(f.Machine); committed = f.Output.childItems.Any(); }
        else committed = Process(f);
        GameItem[] outputs = f.Output.childItems.ToArray();
        bool rare = Reflect.Str(f.View, "Id").EndsWith(".rare_module_upgrade", StringComparison.Ordinal);
        string[] rarePool = rare ? Reflect.Each(Reflect.Get(Reflect.Type("MechcoreProtocol.ModuleMatrix.Definitions.ModuleTierDefinitions"), "All"))
            .Where(d => Reflect.Str(d, "Tier") == "Rare").Select(d => Reflect.Str(d, "Id")).Where(DirectoryMaster.Has<GameItem>).ToArray() : Array.Empty<string>();
        int consumed = f.Inputs.Select((x, i) => beforeInputs[i] - (x.Item.parentInventory == f.Input ? x.Item.unitCount : 0)).Sum();
        int required = f.Inputs.Sum(x => x.Required);
        JToken admissionAfter = OutputAdmissionState(f);
        bool admissionPreserved = JToken.DeepEquals(admissionBefore, admissionAfter) &&
            (f.NativeAdmissionMaterial != null || f.Output.mayInventoryAddItemFunc == admission);
        bool valid = committed && outputs.Sum(i => i.unitCount) == expectedCount && outputs.All(i => i.uniqueId > 0 && i.parentInventory == f.Output && GeneralHelper.IsItemOwned(i) && (rare ? rarePool.Contains(i.identifier) : i.identifier == outputId)) &&
            consumed == required && Energy(f.Battery) == energy - power && (f.Water == null ? 0 : WaterHelper.GetTotalVolume(f.Water)) == water - expectedWater && admissionPreserved;
        R.Check("production.batch." + name, new { output = rare ? "one actual enabled Rare module/node, excludes Functional" : outputId, outputCount = expectedCount,
                inputUnits = required, energyDebit = power, waterPartsDebit = expectedWater, outputAdmissionRestored = true },
            new { committed, outputs = outputs.Select(ItemState).ToArray(), consumed, energyDebit = energy - Energy(f.Battery), waterPartsDebit = water - (f.Water == null ? 0 : WaterHelper.GetTotalVolume(f.Water)), outputAdmissionRestored = admissionPreserved, admissionBefore, admissionAfter }, valid,
            nativeCycle || f.Machine.identifier == "furnace" ? "Real installed machine onCycleEndSlotItemFunc; native furnace admission compared by actual owned-material behavior because its cycle replaces equivalent reject-all lambdas" : "Actual loaded recipe transaction; expected quantities come from declared live recipe contract, not its transaction algorithm");
        if (expectedCount == 2 && outputs.Length == 2 && !rare)
        {
            if (expectedFoodBatchBasis is long total)
            {
                long[] values = outputs.Select(item => item.unitValue).ToArray();
                long[] bases = new long[outputs.Length];
                bool saved = outputs.Select((item, index) => item.IsTag("NICOKOBO_FORGE_PRODUCTION_BASIS_V1") &&
                    long.TryParse(item.GetTagReadonly("NICOKOBO_FORGE_PRODUCTION_BASIS_V1").GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out bases[index])).All(validBasis => validBasis);
                R.Check("production.double-output-value." + name,
                    new { batchTotal = total, totalUnitValue = total, totalSavedBasis = total, maximumPerUnitDifference = 1, eachValueEqualsSavedBasis = true },
                    new { values, bases, saved, totalUnitValue = values.Sum(), totalSavedBasis = bases.Sum() },
                    saved && values.All(value => value >= 0) && values.SequenceEqual(bases) && values.Sum() == total && bases.Sum() == total && values.Max() - values.Min() <= 1,
                    "Current food batch invoice is conserved across both actual output UIDs; a one-credit remainder is valid. Independent sum/spread checks do not invoke production Share.");
            }
            else
            R.Check("production.double-output-value." + name, "Two products retain identical positive per-unit production basis without doubling it", outputs.Select(i => new { i.identifier, i.unitValue, basis = i.GetTagReadonly("NICOKOBO_FORGE_PRODUCTION_BASIS_V1").GetString() }).ToArray(),
                outputs[0].unitValue > 0 && outputs[0].unitValue == outputs[1].unitValue && outputs[0].GetTagReadonly("NICOKOBO_FORGE_PRODUCTION_BASIS_V1").GetString() == outputs[1].GetTagReadonly("NICOKOBO_FORGE_PRODUCTION_BASIS_V1").GetString());
        }
    }
    private static JToken OutputAdmissionState(RecipeFixture f) => f.NativeAdmissionMaterial == null
        ? JObject.FromObject(new { comparison = "delegate-method", method = f.Output.mayInventoryAddItemFunc?.Method.ToString() })
        : JObject.FromObject(new { comparison = "native-furnace-admission", materialUid = f.NativeAdmissionMaterial.uniqueId,
            acceptsOwnedMaterial = f.Output.mayInventoryAddItemFunc?.Invoke(f.NativeAdmissionMaterial, f.Output) ?? true,
            acceptsMachine = f.Output.mayInventoryAddItemFunc?.Invoke(f.Machine, f.Output) ?? true });
    private JToken TransactionState(RecipeFixture f) => JObject.FromObject(new { inputs = f.Input.childItems.OrderBy(i => i.uniqueId).Select(ItemState),
        outputs = f.Output.childItems.OrderBy(i => i.uniqueId).Select(ItemState), battery = f.Battery == null ? null : ItemState(f.Battery),
        water = f.Water == null ? null : ItemState(f.Water), inputAdmission = f.Input.mayInventoryAddItemFunc?.Method.ToString(), outputAdmission = OutputAdmissionState(f) });
    private void AssertRefusal(RecipeFixture f, string name)
    {
        JToken before = TransactionState(f); var admission = f.Output.mayInventoryAddItemFunc;
        bool committed = Process(f); JToken after = TransactionState(f);
        R.Check("production.atomic-refusal." + name, before, after, !committed && JToken.DeepEquals(before, after) &&
                (f.NativeAdmissionMaterial != null || admission == f.Output.mayInventoryAddItemFunc),
            "First actual call returned false; inputs, outputs, battery, every liquid component and IDs unchanged. Native furnace compares real owned-material admission; other transactions must restore their original delegate.");
    }
    private void CheckNoPower(object view, string id)
    {
        using var f = CreateRecipeFixture(view, battery: false);
        AssertRefusal(f, id + ":no-power");
    }
    private void CheckMissingInput(object view, string id)
    {
        using var f = CreateRecipeFixture(view);
        if (f.Inputs.Count == 0) return;
        var first = f.Inputs[0].Item;
        // Removing a fixture before the action models a legal missing-input context.
        if (!f.Input.Expel(first)) throw new InvalidOperationException("Cannot arrange missing-input fixture");
        AssertRefusal(f, id + ":missing-input"); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(first);
    }
    private void CheckNoWater(object view, string id)
    {
        using var f = CreateRecipeFixture(view, water: false);
        AssertRefusal(f, id + ":no-water");
    }
    private void CheckOutputCapacity(object view, string id, bool batchModule = false)
    {
        using var f = CreateRecipeFixture(view);
        if (batchModule)
        {
            if (f.Modules == null) throw new FixtureUnavailable("Native furnace module bay missing");
            Place(f.Modules, Spawn(FurnaceBatchModuleId), "s26-capacity:" + id, f.Machine);
        }
        var isAvailable = (Func<GameItem, bool>)Reflect.Get(view, "IsAvailableFor")!;
        int conservedBatches = 0;
        int required = Reflect.Each(Reflect.Get(view, "Ingredients")).Sum(rule => Reflect.Int(rule, "Amount"));
        int expectedCount = Reflect.Int(view, "OutputCount"), expectedWater = Reflect.Int(view, "WaterParts");
        // Produce real outputs repeatedly to fill the real output. This avoids
        // granting the probe an insertion privilege on a protected output bay.
        for (int attempt = 1; attempt <= 80; attempt++)
        {
            JToken before = TransactionState(f); var admission = f.Output.mayInventoryAddItemFunc;
            int inputUnits = f.Input.childItems.Sum(item => item.unitCount), outputUnits = f.Output.childItems.Sum(item => item.unitCount);
            int energy = Energy(f.Battery), power = MachineryHelper.GetMachinePowerUsage(f.Machine);
            int water = f.Water == null ? 0 : WaterHelper.GetTotalVolume(f.Water);
            bool available = isAvailable(f.Machine);
            if (!available || inputUnits != required || energy < power || water < expectedWater)
            {
                R.Check("production.capacity-precondition." + id,
                    new { available = true, inputUnits = required, minimumEnergy = power, minimumWater = expectedWater },
                    new { available, inputUnits, energy, water, conservedBatches }, false,
                    "Capacity coverage requires the actual recipe predicate and complete usable input/power/water fixture before invoking production; a prerequisite rejection is not output backpressure.");
                return;
            }
            if (!Process(f))
            {
                if (conservedBatches == 0 || outputUnits == 0)
                {
                    R.Check("production.capacity-refusal-without-fill." + id,
                        "At least one successful conserved filling batch and nonempty output before capacity refusal",
                        new { available, conservedBatches, outputUnits, inputUnits, energy, water }, false,
                        "Production returned false before establishing a filled output. This is a fixture or processing failure, not passing full-output coverage.");
                    return;
                }
                // Compare the same rejected attempt. Retrying would reroll a
                // random Rare output and a smaller legal shape can then fit.
                JToken after = TransactionState(f);
                R.Check("production.atomic-refusal." + id + ":full-or-partial-output-capacity", before, after,
                    JToken.DeepEquals(before, after) && (f.NativeAdmissionMaterial != null || admission == f.Output.mayInventoryAddItemFunc),
                    "First actual output-capacity refusal after " + conservedBatches + " successful conserved filling batches; actual recipe available with fresh inputs and sufficient water/power. No second transaction or random reroll; resources, UIDs and admission must remain unchanged.");
                return;
            }
            int consumed = inputUnits - f.Input.childItems.Sum(item => item.unitCount), produced = f.Output.childItems.Sum(item => item.unitCount) - outputUnits;
            int energyDebit = energy - Energy(f.Battery), waterDebit = water - (f.Water == null ? 0 : WaterHelper.GetTotalVolume(f.Water));
            bool restored = JToken.DeepEquals(before["outputAdmission"], OutputAdmissionState(f)) &&
                (f.NativeAdmissionMaterial != null || admission == f.Output.mayInventoryAddItemFunc);
            bool conserved = consumed == required && produced == expectedCount && energyDebit == power && waterDebit == expectedWater && restored &&
                f.Output.childItems.All(item => item.uniqueId > 0 && item.parentInventory == f.Output && GeneralHelper.IsItemOwned(item));
            R.Check("production.capacity-fill." + id + ":" + attempt,
                new { inputUnits = required, outputUnits = expectedCount, energyDebit = power, waterPartsDebit = expectedWater, outputAdmissionRestored = true },
                new { inputUnits = consumed, outputUnits = produced, energyDebit, waterPartsDebit = waterDebit, outputAdmissionRestored = restored },
                conserved,
                "Each real filling batch still owes the complete declared output and resource ledger; a partial successful batch must not be hidden while reaching capacity.");
            if (conserved) conservedBatches++;
            foreach (object rule in Reflect.Each(Reflect.Get(view, "Ingredients")))
            {
                int n = Reflect.Int(rule, "Amount");
                for (int i = 0; i < n; i++) { var item = Ingredient(rule); Place(f.Input, item, "capacity-fixture:" + id, f.Machine); }
            }
            if (f.Water != null) WaterHelper.AddPureWater(f.Water, Reflect.Int(view, "WaterParts"));
            if (f.Battery != null && Energy(f.Battery) < MachineryHelper.GetMachinePowerUsage(f.Machine))
            {
                var batterySlot = f.Battery.parentInventory!; batterySlot.Expel(f.Battery); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(f.Battery);
                f.Battery = PreBuiltItemHelper.EnergyCredit(100); Place(batterySlot, f.Battery, "capacity-battery:" + id, f.Machine);
            }
        }
        R.Skip("production.output-full." + id, "Output did not reach refusal within 80 real batches");
    }
    private IEnumerable<object?> Matrix()
    {
        R.phase = "matrix-native-growth";
        var definitions = new[] { (Id: "continuous_acceleration_module", Tag: "PERFORMANCE", Initial: 8, Step: 4),
            (Id: "continuous_efficiency_module", Tag: "EFFICIENCY", Initial: 8, Step: 4), (Id: "continuous_calibration_module", Tag: "QUALITY", Initial: 4, Step: 2) };
        object view = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", "nicokobo.mechcore.synthesis.synthesizer"))
            .First(x => Reflect.Str(x, "Id").EndsWith(".rare_module_upgrade", StringComparison.Ordinal));
        foreach (var d in definitions)
        {
            using var f = CreateRecipeFixture(view, retain: false);
            var module = Spawn("nicokobo.mechcore_protocol.module_matrix." + d.Id);
            Place(f.Modules!, module, "matrix-continuous:" + d.Id, f.Machine);
            string tag = "BONUS_PERCENTAGE_" + d.Tag + "_INT";
            int before = module.GetTagReadonly(tag).GetInt();
            ObserveBatch(f, d.Id + ":real-cycle", true);
            int after = module.GetTagReadonly(tag).GetInt();
            R.Check("matrix.growth." + d.Id, new { before = d.Initial, after = d.Initial + d.Step }, new { before, after }, before == d.Initial && after == d.Initial + d.Step,
                "Growth via installed module + real successful machine cycle -> ModuleEffectHelper.ActivateOnUseAction");
            int day = StoreStation.GetDayCounter();
            Reflect.Call(Reflect.Type("MechcoreProtocol.ModuleMatrix.MatrixGrowth"), "Decay");
            R.Check("matrix.worked-no-same-day-decay." + d.Id, after, module.GetTagReadonly(tag).GetInt(), module.GetTagReadonly(tag).GetInt() == after);
            StoreStation.Instance.dayCounter = day + 1;
            Reflect.Call(Reflect.Type("MechcoreProtocol.ModuleMatrix.MatrixGrowth"), "Decay");
            int decayed = module.GetTagReadonly(tag).GetInt();
            Reflect.Call(Reflect.Type("MechcoreProtocol.ModuleMatrix.MatrixGrowth"), "Decay");
            R.Check("matrix.decay-once." + d.Id, d.Initial, decayed, decayed == d.Initial && module.GetTagReadonly(tag).GetInt() == decayed,
                "Next-day fixture -> actual Decay scans real player inventories; second same-day dispatch must not decay again");
            f.Retained = true; Retain(f.Machine);
            yield return null;
        }
        R.Skip("matrix.full-night-ordering", "Real cycle/growth/decay executed, but full PlayerStore.EndNight including all production and service ordering requires a separate isolated scenario", false);
    }
}
