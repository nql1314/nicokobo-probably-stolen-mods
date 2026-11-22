using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nicokobo.ModUtilities;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const string ReviewFoodOwner = "nicokobo.food_processing";
    private const string ReviewFizzRecipe = "nicokobo.mechcore.synthesis.recipe.starport_fizz";
    private const string ReviewFizzMachine = "nicokobo.mechcore.synthesis.liquid_infusion_tower";
    private const string ReviewFizzItem = "nicokobo.mechcore.synthesis.starport_fizz";
    private readonly List<JObject> _reviewProducts = new();

    internal IEnumerable<object?> ReviewProductionRegressions()
    {
        R.environment["reviewExecutionScope"] = "Real installed native inventory admission and onCycleEndSlotItemFunc/production entries, with native UID objects. This is not PlayerStore.EndNight or natural elapsed fermentation. Saving uses real SaveGame/ES3; review-reload requires a new process.";
        foreach (object? wait in ReviewWineBottles()) yield return wait;
        foreach (object? wait in ReviewFurnaceSelection()) yield return wait;
        foreach (object? wait in ReviewFoodAlcohol()) yield return wait;
        foreach (object? wait in ReviewRobotDiscard()) yield return wait;
        foreach (object? wait in ReviewRecipePrototypeCleanup()) yield return wait;
        R.persistence["reviewProductionProducts"] = new JArray(_reviewProducts);
        R.Skip("R08.full-ui-ten-rounds", "Manager/G/handbook/theft preview/reroll UI ownership and repeated visual interactions require the supervisor's separate UI scenarios. R01 and R07 below observe actual discarded graphs only.", false);
    }

    private object ReviewRecipe(string machine, string id) => Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", machine))
        .Single(recipe => Reflect.Str(recipe, "Id") == id);
    private object ReviewWineRecipe => ReviewRecipe("furnace", "nicokobo.mechcore.synthesis.recipe.furnace_wine_bottle");
    private static JArray ReviewItemGraph(GameItem item) => new(new[] { item }.Concat(item.FindAllChildItems()).Distinct().OrderBy(i => i.uniqueId).Select(ItemState));

    private GameItem ReviewWine(string state)
    {
        GameItem bottle = Spawn("wine_bottle");
        if (state == "liquid") WaterHelper.AddPureWater(bottle, 125000);
        if (state is "ingredients" or "fermenting" or "finished")
        {
            var ingredients = GraphUtils.FindAllChildrenType<GameInventory>(bottle).First();
            var berry = Spawn("wine_berry"); WineHelper.SetBerryCondition(berry, 2);
            Place(ingredients, berry, "R01-wine-ingredient");
        }
        if (state is "fermenting" or "finished")
        {
            WaterHelper.AddPureWater(bottle, 750000);
            PixelWindow oldWindow = bottle.contentWindow;
            if (!WineHelper.TryStartFermentation(bottle)) throw new InvalidOperationException("Native valid fermentation fixture was rejected");
            if (state == "finished") WineHelper.FinishFermentation(bottle);
            // Native fermentation detaches the previous ingredient window.
            // It belongs to this fixture; release it through the real helper.
            if (oldWindow != null && bottle.contentWindow != oldWindow && oldWindow.parentItems.Count == 0)
            {
                var carrier = ItemDirectory.CreateEmptyItem(""); carrier.SetContentWindow(oldWindow); ItemGraphCleanup.Destroy(carrier);
            }
        }
        return bottle;
    }

    private static void ReviewPlaceHistorical(GameInventory destination, GameItem item)
    {
        // Explicit old-save fixture only: bypass admission while arranging an
        // already populated bay. Restore the actual delegate before production.
        var admission = destination.mayInventoryAddItemFunc; bool locked = destination.overrideLockInsert;
        try
        {
            destination.mayInventoryAddItemFunc = null; destination.overrideLockInsert = false;
            var slot = destination.TryFindOneValidInventorySlot(item);
            if (slot == null || !slot.IsValid() || slot.targetItem != null || slot.AcceptUnchecked() != item.unitCount || item.parentInventory != destination)
                throw new InvalidOperationException("Historical fixture does not fit actual inventory geometry");
        }
        finally { destination.mayInventoryAddItemFunc = admission; destination.overrideLockInsert = locked; }
    }

    private IEnumerable<object?> ReviewWineBottles()
    {
        R.phase = "R01-native-wine-state-admission-and-cycle";
        foreach (string state in new[] { "empty", "liquid", "ingredients", "fermenting", "finished" })
        {
            using var f = CreateRecipeFixture(ReviewWineRecipe, ingredients: false);
            var bottle = ReviewWine(state); int uid = bottle.uniqueId;
            var graph = ReviewItemGraph(bottle); PixelWindow? window = bottle.contentWindow;
            var slot = f.Input.TryFindOneValidInventorySlot(bottle);
            bool admitted = slot != null && slot.IsValid() && slot.inventory == f.Input;
            R.Check("R01.admission." + state, state == "empty", new { admitted, graph }, admitted == (state == "empty"),
                "Real furnace input admission. Fermenting/finished fixtures use native WineHelper transitions, not elapsed nights.");
            if (state == "empty") Place(f.Input, bottle, "R01-empty", f.Machine);
            else ReviewPlaceHistorical(f.Input, bottle);
            JArray before = ReviewItemGraph(bottle); int energy = Energy(f.Battery);
            NativeCycle(f.Machine);
            bool output = f.Output.childItems.Sum(i => i.unitCount) == 1 && f.Output.childItems.All(i => i.identifier == "nicokobo.mechcore.synthesis.quartz");
            bool valid = state == "empty"
                ? bottle.parentInventory == null && output && Energy(f.Battery) == energy - MachineryHelper.GetMachinePowerUsage(f.Machine)
                : JToken.DeepEquals(before, ReviewItemGraph(bottle)) && bottle.parentInventory == f.Input && f.Output.childItems.Count == 0 && Energy(f.Battery) == energy;
            R.Check("R01.actual-cycle." + state, state == "empty" ? "Consume one empty bottle and produce one quartz" : "Existing invalid bottle, descendants, liquids and state preserved; no debit/output",
                new { uid, energyBefore = energy, energyAfter = Energy(f.Battery), graph = ReviewItemGraph(bottle), outputs = f.Output.childItems.Select(ItemState).ToArray() }, valid,
                state == "empty" ? "Native furnace callback, actual resulting UID" : "Explicit historical-bay fixture, admission restored before native callback");
            if (state != "empty") { f.Input.Expel(bottle); Retain(bottle); }
            else
            {
                yield return null; yield return null;
                R.Check("R01.consumed-bottle-window-released", "Original content window is detached, unregistered and Unity handler destroyed",
                    new { hadWindow = window != null, attached = bottle.contentWindow != null, registered = window != null && GraphHandler.windowGraphNodeRoots.Contains(window), handlerAlive = window?.handler != null },
                    window != null && bottle.contentWindow == null && !GraphHandler.windowGraphNodeRoots.Contains(window) && window.handler == null,
                    "Two Update frames after actual ProductionBatch consumed the native bottle; observes the original window reference");
            }
            yield return null;
        }
    }

    private IEnumerable<object?> ReviewFurnaceSelection()
    {
        R.phase = "R02-mixed-furnace-and-logistics-selection";
        PrepareLogisticsNetworks();
        var resources = (GameGridInventory)Reflect.Call(Logistics, "ResourceRegion")!;
        using var f = CreateRecipeFixture(ReviewWineRecipe, ingredients: false);
        Place(f.Modules!, Spawn("nicokobo.automated_logistics.auto_feeder"), "R02-feeder", f.Machine);
        var seed = ReviewWine("empty"); Place(f.Input, seed, "R02-first-manual-batch", f.Machine);
        Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false);
        try { NativeCycle(f.Machine); }
        finally { Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false); }
        R.Check("R02.real-consumption-seeds-restock", "Consumed native empty wine bottle and one actual quartz", new { seedConsumed = seed.parentInventory == null, outputs = f.Output.childItems.Select(ItemState).ToArray() },
            seed.parentInventory == null && f.Output.childItems.Sum(item => item.unitCount) == 1,
            "Actual consumption with feeder enabled creates debt; no fabricated demand or recipe-prefill assumption");
        foreach (var item in f.Output.childItems.ToArray()) ItemGraphCleanup.Destroy(item);
        var invalid = ReviewWine("liquid"); var empty = ReviewWine("empty");
        // The resource network intentionally refuses container shells. These
        // bottles stay in the real main warehouse, where the player prepares
        // containers manually; only ordinary glass goes into ResourceRegion.
        ModHelper.MoveItemToInventory(invalid); ModHelper.MoveItemToInventory(empty);
        if (invalid.parentInventory == null || empty.parentInventory == null)
            throw new FixtureUnavailable("R02 main warehouse has no legal space for its two native wine bottles");
        var invalidSource = invalid.parentInventory; var emptySource = empty.parentInventory;
        JArray invalidBefore = ReviewItemGraph(invalid); int validUid = empty.uniqueId;
        Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false);
        try { Reflect.Call(Logistics, "FillOnce", f.Machine); }
        finally { Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false); }
        var bottleDebt = LogisticsState["Machines"]?[f.Machine.uniqueId.ToString()]?["PendingItems"]?.Children<JObject>()
            .SingleOrDefault(item => (string?)item["ItemId"] == "wine_bottle");
        R.Check("R02.current-container-debt-is-manual", new { manual = true, emptyUid = validUid, moved = 0, invalidUnchanged = true },
            new { debt = bottleDebt, input = f.Input.childItems.Select(ItemState).ToArray(), empty = ItemState(empty), invalid = ReviewItemGraph(invalid) },
            bottleDebt != null && (bool?)bottleDebt["Manual"] == true && empty.parentInventory == emptySource && invalid.parentInventory == invalidSource &&
                JToken.DeepEquals(invalidBefore, ReviewItemGraph(invalid)),
            "Current logistics marks native liquid containers, including empty wine_bottle, as manual restock. Actual FillOnce must not invent automatic container restocking.");
        R.Skip("R02.automatic-empty-wine-bottle-restock", "Current production explicitly uses manual debts for liquid containers. Automatic wine-bottle feeding in the planning checklist is not implemented; it is not claimed by this test.", false);
        Place(f.Input, empty, "R02-manual-empty-source", f.Machine);
        // Zero-energy is a real battery state; no debit implementation is replaced.
        f.Battery.ModifyTag("power_source_item_energy", tag => tag.SetInt(0));
        JToken noPower = TransactionState(f); NativeCycle(f.Machine);
        R.Check("R02.no-power-atomic", noPower, TransactionState(f), JToken.DeepEquals(noPower, TransactionState(f)));
        f.Battery.ModifyTag("power_source_item_energy", tag => tag.SetInt(100));
        NativeCycle(f.Machine);
        R.Check("R02.power-restored-single-batch", 1, f.Output.childItems.Sum(i => i.unitCount), f.Output.childItems.Sum(i => i.unitCount) == 1 && empty.parentInventory == null);
        // Fill actual output geometry with existing physical goods, then verify
        // a normal cycle refuses without consuming the separately supplied input.
        int blockers = ReviewFillOutput(f.Output, "nicokobo.mechcore.synthesis.quartz");
        var retry = ReviewWine("empty"); Place(f.Input, retry, "R02-full-output-input", f.Machine);
        JToken full = TransactionState(f); NativeCycle(f.Machine);
        R.Check("R02.full-output-atomic", full, TransactionState(f), blockers > 0 && JToken.DeepEquals(full, TransactionState(f)), "Protected output admission bypassed only while arranging a full bay; production uses restored admission");
        foreach (var item in f.Output.childItems.ToArray()) ItemGraphCleanup.Destroy(item);
        NativeCycle(f.Machine);
        R.Check("R02.capacity-restored-single-batch", 1, f.Output.childItems.Sum(i => i.unitCount), f.Output.childItems.Sum(i => i.unitCount) == 1 && retry.parentInventory == null);
        // Historical invalid bottle must not mask another eligible glass rule.
        invalid.parentInventory!.Expel(invalid); ReviewPlaceHistorical(f.Input, invalid);
        object other = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", "furnace")).Single(v => Reflect.Str(v, "Id").EndsWith(".furnace_glass_shard", StringComparison.Ordinal));
        foreach (object rule in Reflect.Each(Reflect.Get(other, "Ingredients")))
            for (int i = 0; i < Reflect.Int(rule, "Amount"); i++) Place(f.Input, Ingredient(rule), "R02-other-glass", f.Machine);
        var badBefore = ReviewItemGraph(invalid); int oldCount = f.Output.childItems.Sum(i => i.unitCount);
        Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false);
        try { NativeCycle(f.Machine); }
        finally { Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false); }
        R.Check("R02.invalid-bottle-does-not-mask-other-glass", new { added = Reflect.Int(other, "OutputCount"), invalidUnchanged = true },
            new { added = f.Output.childItems.Sum(i => i.unitCount) - oldCount, invalid = ReviewItemGraph(invalid) },
            f.Output.childItems.Sum(i => i.unitCount) - oldCount == Reflect.Int(other, "OutputCount") && JToken.DeepEquals(badBefore, ReviewItemGraph(invalid)));
        int requiredGlass = 0;
        foreach (object rule in Reflect.Each(Reflect.Get(other, "Ingredients")))
            for (int i = 0; i < Reflect.Int(rule, "Amount"); i++) { Place(resources, Ingredient(rule), "R02-other-glass-restock-source"); requiredGlass++; }
        Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false);
        try { Reflect.Call(Logistics, "FillOnce", f.Machine); }
        finally { Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false); }
        R.Check("R02.actual-other-glass-restock", new { glassUnits = requiredGlass, invalidBottleUnchanged = true },
            new { input = f.Input.childItems.Select(ItemState).ToArray(), invalid = ReviewItemGraph(invalid) },
            f.Input.childItems.Where(item => item.identifier == "glass_shard").Sum(item => item.unitCount) == requiredGlass && JToken.DeepEquals(badBefore, ReviewItemGraph(invalid)),
            "Actual glass-shard consumption debt and FillOnce, without changing the current manual-container policy");
        yield return null;
    }

    private int ReviewFillOutput(GameInventory output, string fillerId)
    {
        var admission = output.mayInventoryAddItemFunc; bool locked = output.overrideLockInsert;
        int added = 0;
        try
        {
            output.mayInventoryAddItemFunc = null; output.overrideLockInsert = false;
            for (int i = 0; i < 200; i++)
            {
                GameItem filler = Spawn(fillerId); var stack = filler.mayStackItemFunc; filler.mayStackItemFunc = null;
                var slot = output.TryFindOneValidInventorySlot(filler);
                if (slot == null || !slot.IsValid() || slot.targetItem != null) { filler.mayStackItemFunc = stack; ItemGraphCleanup.Destroy(filler); break; }
                slot.AcceptUnchecked(); filler.mayStackItemFunc = stack; added++;
            }
        }
        finally { output.mayInventoryAddItemFunc = admission; output.overrideLockInsert = locked; }
        if (added == 200) throw new InvalidOperationException("Output fixture did not fill within bounded geometry");
        return added;
    }

    private object ReviewAssertFoodConfiguration(int required)
    {
        var api = Reflect.Type("Nicokobo.FoodProcessing.Definitions.RecipeConfiguration");
        string json = ModHelper.ReadModDataFileText(ReviewFoodOwner, "recipes.json");
        object parsed = Reflect.Call(api, "Parse", json)!;
        object active = Reflect.Each(Reflect.Get(Reflect.Type("Nicokobo.FoodProcessing.Definitions.FoodDefinitions"), "Recipes")).Single(r => Reflect.Str(r, "Id") == ReviewFizzRecipe);
        object need = Reflect.Each(Reflect.Get(active, "ItemInputs")).Single(r => Reflect.Str(r, "ResourceId") == "alcohol_base");
        object view = ReviewRecipe(ReviewFizzMachine, ReviewFizzRecipe);
        object shown = Reflect.Each(Reflect.Get(view, "Ingredients")).Single(r => Reflect.Str(r, "ItemId") == "alcohol_base");
        bool valid = Reflect.Int(parsed, "SchemaVersion") == 7 && Reflect.Int(need, "Amount") == required && Reflect.Int(shown, "Amount") == required;
        R.Check("R03.active-schema7-and-F32-amount-" + required, new { schema = 7, alcoholBase = required },
            new { schema = Reflect.Int(parsed, "SchemaVersion"), activeAmount = Reflect.Int(need, "Amount"), recipeViewAmount = Reflect.Int(shown, "Amount"), file = Path.Combine(ModHelper.GetModDataFolder(ReviewFoodOwner), "recipes.json") }, valid,
            "Real RecipeConfiguration.Parse + loaded FoodDefinitions + Forge recipe-view data used by G; no prototype edits");
        if (!valid) throw new InvalidOperationException("review modes require actual complete schema 7 with F32 alcohol_base amount " + required);
        return view;
    }

    private RecipeFixture ReviewFizzFixture(object view, params (string Id, int Count)[] alcohol)
    {
        var f = CreateRecipeFixture(view, ingredients: false);
        try
        {
            foreach (var input in alcohol)
            {
                GameItem item = Spawn(input.Id);
                var stack = item.mayStackItemFunc; item.mayStackItemFunc = null;
                try { Place(f.Input, item, "R03-alcohol-" + input.Id, f.Machine); }
                finally { item.mayStackItemFunc = stack; }
                for (int i = 1; i < input.Count; i++)
                {
                    var next = Spawn(input.Id); var slot = item.GetStackSlot(next);
                    if (slot == null || !slot.IsValid() || slot.targetItem != item || slot.TryAcceptOnce() != 1)
                    { ItemGraphCleanup.Destroy(next); throw new FixtureUnavailable("Native alcohol does not permit this actual stack"); }
                    ItemGraphCleanup.Destroy(next);
                }
            }
            Place(f.Input, Spawn("soda_red"), "R03-soda", f.Machine);
            var berry = Spawn("wine_berry"); WineHelper.SetBerryCondition(berry, 2); Place(f.Input, berry, "R03-berry", f.Machine);
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private void ReviewProduceFizz(RecipeFixture f, string name, int amount, int expectedStrength)
    {
        GameItem[] inputs = f.Input.childItems.ToArray(); JArray before = new(inputs.Select(ItemState));
        int energy = Energy(f.Battery), units = inputs.Where(i => i.identifier is "red_beer" or "nudka").Sum(i => i.unitCount);
        NativeCycle(f.Machine);
        var outputs = f.Output.childItems.ToArray();
        int remaining = f.Input.childItems.Where(i => i.identifier is "red_beer" or "nudka").Sum(i => i.unitCount);
        bool valid = outputs.Length == 1 && outputs[0].identifier == ReviewFizzItem && outputs[0].unitCount == 1 && outputs[0].uniqueId > 0 &&
            outputs[0].IsTag("ALCOHOL_VALUE") && outputs[0].GetTagReadonly("ALCOHOL_VALUE").GetInt() == expectedStrength &&
            units - remaining == amount && Energy(f.Battery) == energy - 10;
        R.Check(name, new { alcoholConsumed = amount, strength = expectedStrength, count = 1, powerDebit = 10 },
            new { before, after = f.Input.childItems.Select(ItemState).ToArray(), actualProducts = outputs.Select(ItemState).ToArray(), alcoholConsumed = units - remaining, powerDebit = energy - Energy(f.Battery) }, valid,
            "Actual K04 callback -> FoodProduction/Forge transaction. ALCOHOL_VALUE read from the committed output UID, not the directory factory.");
        if (!valid) throw new InvalidOperationException("Actual F32 production failed: " + name);
        GameItem product = outputs[0]; f.Output.Expel(product); Retain(product);
        _reviewProducts.Add(new JObject { ["scenario"] = name, ["uid"] = product.uniqueId, ["strength"] = expectedStrength, ["item"] = ItemState(product) });
    }

    private IEnumerable<object?> ReviewFoodAlcohol()
    {
        R.phase = "R03-R04-actual-food-production";
        object view = ReviewAssertFoodConfiguration(2);
        using (var same = ReviewFizzFixture(view, ("red_beer", 1), ("red_beer", 1)))
        {
            var beer = same.Input.childItems.Where(item => item.identifier == "red_beer").ToArray();
            R.Check("R03.two-distinct-native-alcohol-UIDs", "Two distinct physical red_beer items with unitCount 1", beer.Select(ItemState).ToArray(),
                beer.Length == 2 && beer.All(item => item.unitCount == 1) && beer.Select(item => item.uniqueId).Distinct().Count() == 2);
            ReviewProduceFizz(same, "R03.two-separate-red-beers", 2, 1);
        }
        using (var mixed = ReviewFizzFixture(view, ("red_beer", 1), ("nudka", 1))) ReviewProduceFizz(mixed, "R04.mixed-separate-strengths", 2, 2);
        using (var noPower = ReviewFizzFixture(view, ("red_beer", 1), ("red_beer", 1)))
        {
            noPower.Battery.ModifyTag("power_source_item_energy", tag => tag.SetInt(0));
            var before = TransactionState(noPower); NativeCycle(noPower.Machine);
            R.Check("R03.no-power-inputs-and-UIDs-preserved", before, TransactionState(noPower), JToken.DeepEquals(before, TransactionState(noPower)));
        }
        using (var full = ReviewFizzFixture(view, ("red_beer", 1), ("red_beer", 1)))
        {
            ReviewFillOutput(full.Output, ReviewFizzItem); var before = TransactionState(full);
            using var observation = new ReviewCleanupObservation("Nicokobo.Forge");
            NativeCycle(full.Machine);
            R.Check("R03.full-output-inputs-and-UIDs-preserved", before, TransactionState(full), JToken.DeepEquals(before, TransactionState(full)));
            yield return null; yield return null;
            observation.Check(R, "R07.failed-food-product-cleanup");
        }
        var stackCanary = Spawn("red_beer"); var stackOther = Spawn("red_beer"); bool legalStack = stackCanary.MayStack(stackOther);
        ItemGraphCleanup.Destroy(stackCanary); ItemGraphCleanup.Destroy(stackOther);
        if (legalStack)
        {
            using var partial = ReviewFizzFixture(view, ("red_beer", 3), ("nudka", 1));
            GameItem beer = partial.Input.childItems.Single(i => i.identifier == "red_beer"); GameItem unused = partial.Input.childItems.Single(i => i.identifier == "nudka");
            JToken unusedBefore = ItemState(unused); int uid = beer.uniqueId;
            ReviewProduceFizz(partial, "R04.partial-native-stack", 2, 1);
            R.Check("R04.partial-stack-remainder-and-unselected-item", new { uid, remaining = 1, unused = unusedBefore }, new { beer = ItemState(beer), unused = ItemState(unused) },
                beer.uniqueId == uid && beer.unitCount == 1 && beer.parentInventory == partial.Input && JToken.DeepEquals(unusedBefore, ItemState(unused)));
        }
        else R.Skip("R04.partial-native-stack", "Current native red_beer does not expose a stacking delegate; no synthetic stack is claimed as a legal player stack.", false);
        byte[] original = ModHelper.ReadModDataFileBytes(ReviewFoodOwner, "recipes.json");
        try
        {
            var document = JObject.Parse(ModHelper.ReadModDataFileText(ReviewFoodOwner, "recipes.json"));
            var recipe = document["Recipes"]!.Children<JObject>().Single(r => (string?)r["Code"] == "F32");
            recipe["Requirements"]!.Children<JObject>().Single(r => (string?)r["ResourceId"] == "alcohol_base")["Amount"] = 1;
            ModHelper.WriteModDataFileText(ReviewFoodOwner, "recipes.json", document.ToString(Formatting.Indented));
            Reflect.Call(Reflect.Type("Nicokobo.FoodProcessing.Definitions.RecipeConfiguration"), "Load");
            object single = ReviewAssertFoodConfiguration(1);
            using (var beer = ReviewFizzFixture(single, ("red_beer", 1))) ReviewProduceFizz(beer, "R04.default-single-red-beer", 1, 1);
            using (var nudka = ReviewFizzFixture(single, ("nudka", 1))) ReviewProduceFizz(nudka, "R04.default-single-nudka", 1, 4);
        }
        finally
        {
            ModHelper.WriteModDataFileBytes(ReviewFoodOwner, "recipes.json", original);
            Reflect.Call(Reflect.Type("Nicokobo.FoodProcessing.Definitions.RecipeConfiguration"), "Load");
        }
        ReviewAssertFoodConfiguration(2);
        R.environment["R04ConfigurationScope"] = "The process started with a genuine schema-7 amount-2 file. Single-unit controls temporarily edited only this isolated file and invoked the actual RecipeConfiguration.Load in the same process; original bytes and amount 2 restored in finally. This is not a second startup test.";
        yield return null;
    }

    private IEnumerable<object?> ReviewProductionReload(JObject baseline)
    {
        ReviewAssertFoodConfiguration(2);
        var products = baseline["persistence"]?["reviewProductionProducts"] as JArray ?? throw new InvalidOperationException("Missing actual production UID baseline");
        foreach (JObject product in products)
        {
            int uid = (int)product["uid"]!, strength = (int)product["strength"]!;
            GameItem? actual = Store.FindAllItem().SingleOrDefault(item => item.uniqueId == uid);
            R.Check("R04.real-es3-product." + (string?)product["scenario"], product, actual == null ? null : ItemState(actual),
                actual != null && actual.identifier == ReviewFizzItem && actual.IsTag("ALCOHOL_VALUE") && actual.GetTagReadonly("ALCOHOL_VALUE").GetInt() == strength && actual.unitCount == 1,
                "Actual normal InventoryScene -> LoadGame -> ES3 reconstructed object looked up by saved production UID in a different process");
        }
        R.persistence["reviewProductionProducts"] = products.DeepClone();
        yield return null;
    }

    private IEnumerable<object?> ReviewRobotDiscard()
    {
        R.phase = "R07-actual-robot-discard-observation";
        var robotApi = Reflect.Type("MechcoreProtocol.SynthesisExpansion.SynthesisRobots");
        var rules = Reflect.Type("MechcoreProtocol.SynthesisExpansion.Domain.ScavengingRobotRules");
        var robot = Spawn("nicokobo.mechcore.synthesis.scavenging_robot"); Retain(robot);
        var cargo = (GameGridInventory)Reflect.Call(robotApi, "Cargo", robot)!;
        var battery = PreBuiltItemHelper.EnergyCredit(100);
        Place((GameSlotInventory)Reflect.Call(robotApi, "Battery", robot)!, battery, "R07-robot-battery");
        // This native registered node occupies exactly one cell, so a failed
        // final placement proves no smaller loot footprint can still fit.
        ReviewFillOutput(cargo, "nicokobo.mechcore_protocol.module_matrix.performance_conversion_node");
        JArray before = ReviewItemGraph(robot); int energy = Energy(battery);
        string night = (string)Reflect.Call(rules, "NightKey", Store.saveSlotId, Store.runID, StoreStation.GetDayCounter())!;
        using var observation = new ReviewCleanupObservation("MechcoreProtocol.SynthesisExpansion");
        Reflect.Call(robotApi, "Work", robot, night, 1);
        JArray cargoAfter = new(cargo.childItems.OrderBy(item => item.uniqueId).Select(ItemState));
        JArray cargoBefore = new(before.Children<JObject>().Where(i => (int?)i["parentUid"] == robot.uniqueId && (string?)i["parentStorage"] == cargo.identifier));
        R.Check("R07.full-cargo-cost-and-existing-items", new { powerDebit = 25, cargo = cargoBefore },
            new { powerDebit = energy - Energy(battery), cargo = cargoAfter, discarded = observation.Items.Count },
            Energy(battery) == energy - 25 && JToken.DeepEquals(cargoBefore, cargoAfter) && robot.contentWindow != null,
            "Actual SynthesisRobots.Work with real native scavenging, one attempt, same native night key; does not execute a full EndNight");
        int after = Energy(battery); Reflect.Call(robotApi, "Work", robot, night, 1);
        R.Check("R07.same-night-repeat-no-extra-debit", after, Energy(battery), Energy(battery) == after,
            "Actual second Work call with the same native night token. A random broken state may also prevent the second attempt.");
        yield return null; yield return null;
        observation.Check(R, "R07.discarded-native-loot");
        if (observation.Windows.Count == 0)
            R.Skip("R07.discarded-container-instance", "This real random scavenging attempt produced no discarded container window. Non-container discard and retained cargo were observed; a container-drop run is still required for this branch.", false);
        R.persistence["reviewRobot"] = new { uid = robot.uniqueId, batteryUid = battery.uniqueId, energy = Energy(battery), cargo = cargoAfter };
    }

    private IEnumerable<object?> ReviewRecipePrototypeCleanup()
    {
        R.phase = "R08-forge-presentation-prototype-ownership";
        var presentation = Reflect.Type("Nicokobo.ContentInterop.RecipePresentation");
        int uidBefore = Store.currentUniqueId; JArray inventoryBefore = SnapshotItems();
        using var observation = new ReviewCleanupObservation("Nicokobo.Forge");
        for (int round = 0; round < 10; round++)
        {
            foreach (string id in new[] { ReviewFizzMachine, "wine_bottle" })
            {
                int callUidBefore = Store.currentUniqueId;
                string dataBefore = JsonConvert.SerializeObject(Store.ModData());
                JArray callInventoryBefore = SnapshotItems();
                Reflect.Call(presentation, "ReadItem", id);
                bool dataUnchanged = dataBefore == JsonConvert.SerializeObject(Store.ModData());
                bool inventoryUnchanged = JToken.DeepEquals(callInventoryBefore, SnapshotItems());
                R.Check("R08.presentation-synchronous-call-" + round + "." + id,
                    new { uid = callUidBefore, modDataUnchanged = true, inventoryUnchanged = true },
                    new { uid = Store.currentUniqueId, modDataUnchanged = dataUnchanged, inventoryUnchanged },
                    callUidBefore == Store.currentUniqueId && dataUnchanged && inventoryUnchanged,
                    "For these two UID-free factories only: one actual ReadItem call, without yielding to scene updates");
            }
            yield return null; yield return null;
            observation.Check(R, "R08.forge-real-prototype-round-" + round);
        }
        R.Check("R08.presentation-no-live-ownership-mutation", new { items = inventoryBefore, minimumWindows = 20 },
            new { items = SnapshotItems(), windows = observation.Windows.Count, counterBeforeFrames = uidBefore, counterAfterFrames = Store.currentUniqueId },
            JToken.DeepEquals(inventoryBefore, SnapshotItems()) && observation.Windows.Count >= 20,
            "Actual Forge RecipePresentation.ReadItem, used by G/handbooks. Across destroy frames, compare live inventory/ownership and original graph cleanup; the global UID counter also belongs to unrelated scene updates and is observed separately.");
    }

    // Only observes the installed owner's real cleanup entry. In particular it
    // never replaces a factory, loot list, RNG, transaction, or return value.
    private sealed class ReviewCleanupObservation : IDisposable
    {
        private static ReviewCleanupObservation? Active;
        private readonly Harmony _harmony;
        private readonly MethodInfo _target;
        internal readonly List<GameItem> Items = new();
        internal readonly List<PixelWindow> Windows = new();
        private readonly List<object> _before = new();
        internal ReviewCleanupObservation(string assembly)
        {
            if (Active != null) throw new InvalidOperationException("Cleanup observation is already active");
            _target = Reflect.Type("Nicokobo.ModUtilities.ItemGraphCleanup", assembly).GetMethod("Destroy", Reflect.All, null, new[] { typeof(GameItem) }, null)
                ?? throw new MissingMethodException(assembly, "ItemGraphCleanup.Destroy(GameItem)");
            _harmony = new Harmony("nicokobo.content_native_probe.cleanup." + Guid.NewGuid().ToString("N"));
            _harmony.Patch(_target, prefix: new HarmonyMethod(typeof(ReviewCleanupObservation).GetMethod(nameof(Before), Reflect.All)));
            Active = this;
        }
        private static void Before(GameItem __0)
        {
            var observation = Active;
            if (observation == null || __0 == null) return;
            foreach (var item in new[] { __0 }.Concat(__0.FindAllChildItems()).Distinct())
            {
                if (!observation.Items.Contains(item))
                {
                    observation.Items.Add(item);
                    observation._before.Add(new { item.identifier, item.uniqueId, parent = item.parentInventory?.identifier, children = item.FindAllChildItems().Count });
                }
                foreach (var window in new[] { item.contentWindow, item.examineWindow })
                    if (window != null && !window.parentItems.Any(owner => !ReferenceEquals(owner, item)) && !observation.Windows.Contains(window))
                        observation.Windows.Add(window);
            }
        }
        internal void Check(ProbeResult result, string name)
        {
            bool valid = Items.Count > 0 && Items.All(item => item.parentInventory == null && item.contentWindow == null && item.examineWindow == null) &&
                Windows.All(window => window.handler == null && !GraphHandler.windowGraphNodeRoots.Contains(window));
            result.Check(name, "Every observed exclusively owned discarded item/window detached; Unity handler gone after two frames",
                new { originals = _before.ToArray(), windows = Windows.Count, remainingHandlers = Windows.Count(window => window.handler != null), registeredRoots = Windows.Count(GraphHandler.windowGraphNodeRoots.Contains), attachedItems = Items.Count(item => item.parentInventory != null) }, valid,
                "Passive Harmony prefix on this installed assembly's real ItemGraphCleanup.Destroy(GameItem); the original method always executes");
        }
        public void Dispose() { Active = null; _harmony.Unpatch(_target, HarmonyPatchType.Prefix, _harmony.Id); }
    }
}
