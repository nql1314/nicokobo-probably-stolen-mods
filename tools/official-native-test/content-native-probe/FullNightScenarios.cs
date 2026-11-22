using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private static readonly string[] NightEvents =
    {
        "OnShutterOpenedEarly", "OnShutterOpenedLate", "OnShutterClosedEarly", "OnShutterClosedLate",
        "OnGoingSleepEarly", "OnHandlingNightlyServicesEarly", "OnHandlingNightlyServicesLate",
        "OnGoingSleepLate", "OnWakingUpEarly", "OnWakingUpLate"
    };
    private const string NightFoodRecipe = "nicokobo.mechcore.synthesis.recipe.meat_sauce_noodles";
    private const string NightSiliconRecipe = "nicokobo.mechcore.synthesis.recipe.silicon";
    private const string NightCheeseRecipe = "nicokobo.mechcore.synthesis.recipe.recombined_cheese";
    private const string NightPattyRecipe = "nicokobo.mechcore.synthesis.recipe.roe_fiber_patty";
    private const string NightCheeseId = "processed_cheese";
    private const string NightPattyId = "nicokobo.mechcore.synthesis.roe_fiber_patty";
    private static readonly string[] NightSavedTags =
    {
        "power_source_item_energy", "NICOKOBO_FORGE_PRODUCTION_BASIS_V1", "CALORIE_VALUE_TAG",
        "nicokobo.mechcore_protocol.module_matrix.continuous_last_work_day.v1",
        "nicokobo.mechcore_protocol.module_matrix.continuous_last_decay_day.v1",
        "BONUS_PERCENTAGE_PERFORMANCE_INT", "BONUS_PERCENTAGE_EFFICIENCY_INT", "BONUS_PERCENTAGE_QUALITY_INT"
    };

    // The fixtures are injected explicitly; opening, closing, sleep, native
    // callbacks, coroutine, next day and automatic ES3 saving are not injected.
    internal IEnumerable<object?> Night()
    {
        R.phase = "night-unprocessed-native-fixtures";
        R.environment["fullNightScope"] = "Three real nights through shutter -> close confirmation -> bedroll -> enabled End Day button -> Unity coroutine -> EndNight -> BeginDay -> automatic ES3. First night retains the original K01/K06/control checks and consumes one native cheese in K06 F30 to establish real feeder debt. Second night K06 lacks cheese and must pause before K04 F33 produces four units; logistics may replenish but cannot add a production opportunity. Third night K06 consumes one of those units. Explicit native input/module fixtures; no Process/NativeCycle/FireOn calls, debt writes or queue/lock edits.";
        object Recipe(string id) => Reflect.Each(Reflect.Call(MachineCatalog, "MachineIds")).Cast<string>()
            .SelectMany(machine => Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", machine)))
            .Single(recipe => Reflect.Str(recipe, "Id") == id);
        object food = Recipe(NightFoodRecipe), silicon = Recipe(NightSiliconRecipe);
        var fixtures = new[]
        {
            CreateRecipeFixture(food, retain: true), CreateRecipeFixture(silicon, retain: true),
            CreateRecipeFixture(food, ingredients: false, retain: true)
        };
        foreach (var fixture in fixtures) Retain(fixture.Machine);
        var before = fixtures.Select(NightMachineState).ToArray();
        int[] energy = fixtures.Select(f => Energy(f.Battery)).ToArray();
        int[] power = fixtures.Select(f => MachineryHelper.GetMachinePowerUsage(f.Machine)).ToArray();
        int day = StoreStation.GetDayCounter();
        string initialFile = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId));
        int initialSavedDay = ES3.Load<StoreStation>("storeStation", new ES3Settings(initialFile, ES3.Location.File)).dayCounter;
        R.environment["nightFixtureInvoice"] = fixtures.Select((f, i) => new
        {
            role = i == 2 ? "missing-input-control" : "one-pending-batch", state = before[i],
            recipe = Reflect.Str(f.View, "Id"), output = Reflect.Str(f.View, "OutputItemId"),
            outputCount = Reflect.Int(f.View, "OutputCount"), waterParts = Reflect.Int(f.View, "WaterParts"),
            power = power[i], energyBefore = energy[i],
            requirements = Reflect.Each(Reflect.Get(f.View, "Ingredients")).Select(rule => new { id = Reflect.Str(rule, "ItemId"), amount = Reflect.Int(rule, "Amount") }).ToArray()
        }).ToArray();
        NightRequire("night.fixtures-unprocessed-and-owned", "Two exactly supplied machines and an empty input control, in owned main/back storage, zero outputs, no modules or water demand", before,
            initialSavedDay == day && fixtures.All(f => f.Machine.parentInventory != null && GeneralHelper.IsItemOwned(f.Machine) &&
                f.Output.childItems.Count == 0 && f.Machine.onCycleEndSlotItemFunc != null && (f.Modules == null || f.Modules.childItems.Count == 0) && Reflect.Int(f.View, "WaterParts") == 0) &&
            fixtures.Take(2).All(f => f.Input.childItems.Sum(item => item.unitCount) == Reflect.Each(Reflect.Get(f.View, "Ingredients")).Sum(rule => Reflect.Int(rule, "Amount"))) && fixtures[2].Input.childItems.Count == 0,
            "The original fixed recipes declare no water; their successful first-night transactions and missing-input control remain independently checked.");

        object cheeseRecipe = Recipe(NightCheeseRecipe), pattyRecipe = Recipe(NightPattyRecipe);
        NightRequire("night.cheese-chain.recipe-contract", "Default F33: milk x2 + fat x1 -> cheese x4; F30: fillerweed + sturgeon roe + cheese -> patties x2; no water", new
        {
            cheese = Reflect.Each(Reflect.Get(cheeseRecipe, "Ingredients")).Select(rule => new { id = Reflect.Str(rule, "ItemId"), count = Reflect.Int(rule, "Amount") }).ToArray(),
            patties = Reflect.Each(Reflect.Get(pattyRecipe, "Ingredients")).Select(rule => new { id = Reflect.Str(rule, "ItemId"), count = Reflect.Int(rule, "Amount") }).ToArray()
        }, NightRecipeMatches(cheeseRecipe, NightCheeseId, 4, ("processed_milk", 2), ("fat_meat", 1)) &&
            NightRecipeMatches(pattyRecipe, NightPattyId, 2, ("fillerweed", 1), ("sturgeon_roe", 1), (NightCheeseId, 1)),
            "Independent default recipe constants; a changed configuration is an unavailable fixture, not a successful chain.");
        // Create K04 first: K06 must precede it because of its registered stage,
        // not because it happens to have the smaller UID.
        var producer = CreateRecipeFixture(cheeseRecipe, ingredients: false, retain: true);
        var consumer = CreateRecipeFixture(pattyRecipe, retain: true);
        Retain(consumer.Machine); Retain(producer.Machine);
        Place(consumer.Modules!, Spawn("nicokobo.automated_logistics.auto_feeder"), "night-cheese-feeder", consumer.Machine);
        Place(producer.Modules!, Spawn("nicokobo.automated_logistics.output_ejector"), "night-cheese-ejector", producer.Machine);
        Reflect.Call(Logistics, "ConfigureMachine", consumer.Machine, true, false);
        Reflect.Call(Logistics, "ConfigureMachine", producer.Machine, false, true);
        var allFixtures = fixtures.Concat(new[] { producer, consumer }).ToArray();
        int consumerSeedEnergy = Energy(consumer.Battery), producerSeedEnergy = Energy(producer.Battery);
        int consumerPower = MachineryHelper.GetMachinePowerUsage(consumer.Machine);
        NightRequire("night.cheese-chain.seed-only", "One real native cheese seed inside K06, no preexisting patties, empty K04; neither machine has a performance bonus", new
        {
            producer = NightMachineState(producer), consumer = NightMachineState(consumer), items = NightChainItems(),
            debt = NightCheeseDebt(consumer.Machine.uniqueId)
        }, NightChainUnits(NightCheeseId) == 1 && NightChainUnits(NightPattyId) == 0 &&
            consumer.Input.childItems.Where(item => item.identifier == NightCheeseId).Sum(item => item.unitCount) == 1 &&
            producer.Input.childItems.Count == 0 && producer.Output.childItems.Count == 0 && consumer.Output.childItems.Count == 0 &&
            NightCheeseDebt(consumer.Machine.uniqueId) == null && producer.Machine.uniqueId < consumer.Machine.uniqueId &&
            MachineryHelper.GetCurrentPerformanceBonus(producer.Machine) == 0 && MachineryHelper.GetCurrentPerformanceBonus(consumer.Machine) == 0);

        // Use SpeedUp's real reset action in the isolated clone, not a write to
        // Time.timeScale or a replacement for its production settings logic.
        var speed = Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp");
        Reflect.Call(speed, "RestoreNormalSpeed");
        yield return null;
        NightRequire("night.normal-speed-preset", "Actual normal preset and native time scale 1", new
        {
            Time.timeScale, dialogue = Reflect.Get(speed, "Multiplier"), queue = Reflect.Get(speed, "QueueMultiplier"),
            arrive = Reflect.Get(speed, "ClientArriveSpeed"), leave = Reflect.Get(speed, "ClientLeaveSpeed"), instant = Reflect.Get(speed, "Instant"), global = Reflect.Get(speed, "GlobalEnabled")
        }, Time.timeScale == 1f && !(bool)Reflect.Get(speed, "Instant")! && !(bool)Reflect.Get(speed, "GlobalEnabled")! &&
            new[] { "Multiplier", "QueueMultiplier", "ClientArriveSpeed", "ClientLeaveSpeed" }.All(name => Convert.ToSingle(Reflect.Get(speed, name)) == 1f));

        using (var observation = new NightObservation(this, allFixtures, day))
        {
            R.environment["nightObservation"] = observation.Evidence;
            R.environment["nightActualRegistry"] = Reflect.Each(Reflect.Call(MachineCatalog, "MachineIds")).Cast<string>()
                .Select(id => new { id, registeredOrder = Reflect.Call(Reflect.Type("Nicokobo.ContentInterop.MachineSchedule"), "Order", id) }).ToArray();
            foreach (object? wait in NightDay(day, "night", observation)) yield return wait;

            for (int i = 0; i < fixtures.Length; i++)
            {
                var fixture = fixtures[i]; bool ready = i < 2;
                GameItem[] outputs = fixture.Output.childItems.ToArray();
                JObject[] commits = observation.Work.Where(row => (int?)row["uid"] == fixture.Machine.uniqueId && (bool?)row["committed"] == true).ToArray();
                NightRequire("night.machine-transaction." + (ready ? Reflect.Str(fixture.View, "Id") : "missing-input-control"), new
                {
                    commits = ready ? 1 : 0, inputUnits = 0, outputId = Reflect.Str(fixture.View, "OutputItemId"),
                    outputCount = ready ? Reflect.Int(fixture.View, "OutputCount") : 0, energyAfter = energy[i] - (ready ? power[i] : 0), waterPartsDebit = 0
                }, new { before = before[i], after = NightMachineState(fixture), commits },
                    fixture.Input.childItems.Count == 0 && Energy(fixture.Battery) == energy[i] - (ready ? power[i] : 0) &&
                    outputs.Sum(item => item.unitCount) == (ready ? Reflect.Int(fixture.View, "OutputCount") : 0) &&
                    outputs.All(item => item.identifier == Reflect.Str(fixture.View, "OutputItemId") && item.uniqueId > 0 && item.parentInventory == fixture.Output && GeneralHelper.IsItemOwned(item)) &&
                    commits.Length == (ready ? 1 : 0) && commits.All(row => (string?)row["recipe"] == Reflect.Str(fixture.View, "Id") && (int?)row["day"] == day) &&
                    (ready || JToken.DeepEquals(before[i], NightMachineState(fixture))),
                    "Independent pre-action inventory and native power draw plus declared recipe amounts; only the real full-night callbacks perform work.");
            }
            var seedCommits = observation.Work.Where(row => (int?)row["uid"] == consumer.Machine.uniqueId && (bool?)row["committed"] == true).ToArray();
            JObject? seedDebt = NightCheeseDebt(consumer.Machine.uniqueId);
            NightRequire("night.cheese-chain.real-consumption-debt", "One F30 commit consumed the native seed; two patties, no cheese, and one real non-manual cheese debt", new
            {
                commits = seedCommits, debt = seedDebt, items = NightChainItems(), consumer = NightMachineState(consumer), producer = NightMachineState(producer)
            }, seedCommits.Length == 1 && (string?)seedCommits[0]["recipe"] == NightPattyRecipe &&
                NightChainUnits(NightCheeseId) == 0 && NightChainUnits(NightPattyId) == 2 && consumer.Input.childItems.Count == 0 &&
                Energy(consumer.Battery) == consumerSeedEnergy - consumerPower && Energy(producer.Battery) == producerSeedEnergy &&
                !observation.Work.Any(row => (int?)row["uid"] == producer.Machine.uniqueId && (bool?)row["committed"] == true) &&
                (int?)seedDebt?["Remaining"] == 1 && (int?)seedDebt?["TargetCount"] == 1 && (bool?)seedDebt?["Manual"] == false);
            R.persistence["fullNight"] = new JObject
            {
                ["dayBefore"] = day, ["dayAfter"] = StoreStation.GetDayCounter(), ["gameRunId"] = Store.runID,
                ["fixtures"] = new JArray(allFixtures.Select(NightMachineState)), ["automaticSave"] = observation.AutoSave!.DeepClone(),
                ["events"] = new JArray(observation.Events.Select(e => e.DeepClone())), ["actualWork"] = new JArray(observation.Work.Select(e => e.DeepClone()))
            };
        }

        // Only non-cheese inputs are supplied before the bottling night. The feeder
        // is paused while the fixture is arranged, then uses its genuine debt.
        Reflect.Call(Logistics, "ConfigureMachine", consumer.Machine, false, false);
        foreach (var fixture in new[] { producer, consumer })
        foreach (object rule in Reflect.Each(Reflect.Get(fixture.View, "Ingredients")))
        {
            if (fixture == consumer && Reflect.Str(rule, "ItemId") == NightCheeseId) continue;
            for (int count = 0; count < Reflect.Int(rule, "Amount"); count++)
                Place(fixture.Input, Ingredient(rule), "night-chain-input:" + Reflect.Str(rule, "ItemId"), fixture.Machine);
        }
        Reflect.Call(Logistics, "ConfigureMachine", consumer.Machine, true, false);
        int chainDay = StoreStation.GetDayCounter();
        int producerEnergy = Energy(producer.Battery), consumerEnergy = Energy(consumer.Battery);
        int producerPower = MachineryHelper.GetMachinePowerUsage(producer.Machine);
        consumerPower = MachineryHelper.GetMachinePowerUsage(consumer.Machine);
        long cheeseCalories = producer.Input.childItems.Sum(item => (long)item.unitCount * item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt());
        long otherCalories = consumer.Input.childItems.Sum(item => (long)item.unitCount * item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt());
        long oldPattyCalories = NightChainCalories(NightPattyId);
        int[] idleEnergy = fixtures.Select(f => Energy(f.Battery)).ToArray();
        NightRequire("night.cheese-chain.no-preloaded-product", "K04 has only its three raw input units; K06 has only fillerweed and roe; zero cheese anywhere; debt is still one", new
        {
            producer = NightMachineState(producer), consumer = NightMachineState(consumer), items = NightChainItems(), debt = NightCheeseDebt(consumer.Machine.uniqueId)
        }, chainDay == day + 1 && NightChainUnits(NightCheeseId) == 0 && producer.Input.childItems.Sum(item => item.unitCount) == 3 &&
            consumer.Input.childItems.Sum(item => item.unitCount) == 2 && producer.Output.childItems.Count == 0 &&
            (int?)NightCheeseDebt(consumer.Machine.uniqueId)?["Remaining"] == 1,
            "No cheese is spawned or placed after the warm-up consumption; no call to FillOnce, EjectOnce or a production method.");
        JObject bottlingNight, bottlingCommit;
        using (var observation = new NightObservation(this, allFixtures, chainDay))
        {
            R.environment["nightBottlingObservation"] = observation.Evidence;
            foreach (object? wait in NightDay(chainDay, "night.bottling", observation)) yield return wait;
            JObject[] attempts = observation.Work.Where(row => (int?)row["uid"] == consumer.Machine.uniqueId && (string?)row["recipe"] == NightPattyRecipe).ToArray();
            JObject[] bottlingAttempts = observation.Work.Where(row => (int?)row["uid"] == producer.Machine.uniqueId && (string?)row["recipe"] == NightCheeseRecipe).ToArray();
            JObject[] produced = observation.Work.Where(row => (int?)row["uid"] == producer.Machine.uniqueId && (bool?)row["committed"] == true).ToArray();
            NightRequire("night.bottling.food-before-bottler", "One failed F30 attempt without cheese, then one F33 attempt/commit in the same real night, despite K06 having the larger UID", new { attempts, bottlingAttempts, produced },
                attempts.Length == 1 && (bool?)attempts[0]["completed"] == true && (bool?)attempts[0]["committed"] == false &&
                !((JArray)attempts[0]["inputsAtBegin"]!).Any(item => (string?)item["id"] == NightCheeseId) &&
                bottlingAttempts.Length == 1 && produced.Length == 1 && (string?)produced[0]["recipe"] == NightCheeseRecipe && (bool?)produced[0]["completed"] == true &&
                (int?)attempts[0]["ordinal"] < (int?)produced[0]["ordinal"] &&
                (int?)attempts[0]["day"] == chainDay && (int?)produced[0]["day"] == chainDay);
            int stockedCheese = consumer.Input.childItems.Where(item => item.identifier == NightCheeseId).Sum(item => item.unitCount);
            JObject? pendingCheese = NightCheeseDebt(consumer.Machine.uniqueId);
            int pendingCheeseUnits = pendingCheese == null ? 0 : (int?)pendingCheese["Remaining"] ?? -1;
            NightRequire("night.bottling.no-same-night-food", "Four genuine cheese units, still only the two warm-up patties, no K06 power debit; natural replenishment replaces only the prior one-unit debt", new
            {
                items = NightChainItems(), consumer = NightMachineState(consumer), producer = NightMachineState(producer),
                inputCheese = stockedCheese, remaining = pendingCheeseUnits, debt = pendingCheese, work = observation.Work
            }, NightChainUnits(NightCheeseId) == 4 && NightChainUnits(NightPattyId) == 2 &&
                NightChainCalories(NightCheeseId) == cheeseCalories && NightChainCalories(NightPattyId) == oldPattyCalories &&
                producer.Input.childItems.Count == 0 && producer.Output.childItems.Count == 0 &&
                consumer.Input.childItems.Where(item => item.identifier != NightCheeseId).Sum(item => item.unitCount) == 2 &&
                Energy(producer.Battery) == producerEnergy - producerPower && Energy(consumer.Battery) == consumerEnergy &&
                stockedCheese is 0 or 1 && stockedCheese + pendingCheeseUnits == 1 &&
                (pendingCheese == null || (int?)pendingCheese["TargetCount"] == 1 && (bool?)pendingCheese["Manual"] == false) &&
                !observation.Work.Any(row => (int?)row["uid"] == consumer.Machine.uniqueId && (bool?)row["committed"] == true),
                "The unspent F30 materials may receive real ejected cheese after their nightly opportunity, but logistics cannot manufacture a second opportunity.");
            NightRequire("night.bottling.no-extra-original-work", "The original exhausted fixtures stay idle", observation.Work,
                fixtures.Select((fixture, index) => Energy(fixture.Battery) == idleEnergy[index] &&
                    !observation.Work.Any(row => (int?)row["uid"] == fixture.Machine.uniqueId && (bool?)row["committed"] == true)).All(value => value));
            bottlingCommit = (JObject)produced[0].DeepClone();
            bottlingNight = new JObject
            {
                ["dayBefore"] = chainDay, ["dayAfter"] = StoreStation.GetDayCounter(), ["automaticSave"] = observation.AutoSave!.DeepClone(),
                ["fixtures"] = new JArray(allFixtures.Select(NightMachineState)), ["chainItems"] = NightChainItems(),
                ["events"] = new JArray(observation.Events.Select(e => e.DeepClone())), ["actualWork"] = new JArray(observation.Work.Select(e => e.DeepClone()))
            };
        }
        // No input, debt or opportunity is added between these nights. A normal
        // later scan or the next F30 Before hook may finish the genuine refill.
        using (var observation = new NightObservation(this, allFixtures, chainDay + 1))
        {
            R.environment["nightCheeseChainObservation"] = observation.Evidence;
            foreach (object? wait in NightDay(chainDay + 1, "night.cheese-chain", observation)) yield return wait;
            // The measured automatic save has already completed. Pause through
            // the real settings API so a later background refill cannot change
            // the final-save/reload baseline after its inventory is captured.
            Reflect.Call(Logistics, "ConfigureMachine", consumer.Machine, false, false);
            Reflect.Call(Logistics, "ConfigureMachine", producer.Machine, false, false);
            JToken pausedMachines = LogisticsState["Machines"]!;
            var pausedSettings = new JObject(new[] { producer.Machine, consumer.Machine }.Select(machine =>
                new JProperty(machine.uniqueId.ToString(), pausedMachines[machine.uniqueId.ToString()]!.DeepClone())));
            NightRequire("night.cheese-chain.automation-paused-for-save", "Both real fill/eject settings are false after the measured automatic save", pausedSettings,
                pausedSettings.Properties().All(property => (bool?)property.Value["FillEnabled"] == false && (bool?)property.Value["EjectEnabled"] == false));
            JObject[] producerAttempts = observation.Work.Where(row => (int?)row["uid"] == producer.Machine.uniqueId && (string?)row["recipe"] == NightCheeseRecipe).ToArray();
            JObject[] consumerAttempts = observation.Work.Where(row => (int?)row["uid"] == consumer.Machine.uniqueId && (string?)row["recipe"] == NightPattyRecipe).ToArray();
            JObject[] consumed = observation.Work.Where(row => (int?)row["uid"] == consumer.Machine.uniqueId && (bool?)row["committed"] == true).ToArray();
            NightRequire("night.cheese-chain.actual-work-order", "The previous night's F33 output is consumed by exactly one F30 attempt/commit on the next night, before the now-empty K04 attempt", new { bottlingCommit, consumed, consumerAttempts, producerAttempts },
                consumerAttempts.Length == 1 && consumed.Length == 1 && (string?)consumed[0]["recipe"] == NightPattyRecipe && (bool?)consumed[0]["completed"] == true &&
                (int?)bottlingCommit["day"] == chainDay && (int?)consumed[0]["day"] == chainDay + 1 &&
                producerAttempts.Length == 1 && (bool?)producerAttempts[0]["completed"] == true && (bool?)producerAttempts[0]["committed"] == false &&
                (int?)consumed[0]["ordinal"] < (int?)producerAttempts[0]["ordinal"] &&
                !observation.Work.Any(row => (int?)row["uid"] == producer.Machine.uniqueId && (bool?)row["committed"] == true),
                "User-selected food-before-bottling rule: replenishment may happen earlier, but production must wait for the next genuine night.");
            var minted = (JArray)bottlingCommit["chainItemsAfter"]!;
            var actualInputs = (JArray)consumed[0]["inputsAtBegin"]!;
            var inputCheese = actualInputs.Where(item => (string?)item["id"] == NightCheeseId).ToArray();
            NightRequire("night.cheese-chain.actual-products-and-feeding", "The previous night's F33 made four real cheese units; F30 received one through logistics and produced only on the third night", new
            {
                producerCompleted = minted, consumerInputs = actualInputs, remaining = NightChainItems()
            }, minted.Where(item => (string?)item["id"] == NightCheeseId).Sum(item => (int)item["count"]!) == 4 &&
                inputCheese.Sum(item => (int)item["count"]!) == 1 && inputCheese.All(item => (int)item["uid"]! > 0) &&
                actualInputs.Sum(item => (int)item["count"]!) == 3 &&
                actualInputs.Any(item => (string?)item["id"] == "fillerweed") && actualInputs.Any(item => (string?)item["id"] == "sturgeon_roe") &&
                producer.Output.childItems.Count == 0 && NightChainUnits(NightCheeseId) == 3 && NightChainUnits(NightPattyId) == 4,
                "UIDs are recorded before and after real transfers. Native stack splitting may allocate a new UID; zero preexisting cheese plus the measured 0 -> 4 -> 3 inventory proves provenance without requiring UID equality across splits.");
            NightRequire("night.cheese-chain.conservation", new { cheese = 3, newPatties = 2, totalCalories = cheeseCalories + otherCalories,
                producerEnergy = producerEnergy - producerPower, consumerEnergy = consumerEnergy - consumerPower, waterPartsDebit = 0 }, new
            {
                cheese = NightChainUnits(NightCheeseId), patties = NightChainUnits(NightPattyId),
                totalCalories = NightChainCalories(NightCheeseId) + NightChainCalories(NightPattyId) - oldPattyCalories,
                producerEnergy = Energy(producer.Battery), consumerEnergy = Energy(consumer.Battery),
                producer = NightMachineState(producer), consumer = NightMachineState(consumer)
            }, NightChainCalories(NightCheeseId) + NightChainCalories(NightPattyId) - oldPattyCalories == cheeseCalories + otherCalories &&
                Energy(producer.Battery) == producerEnergy - producerPower && Energy(consumer.Battery) == consumerEnergy - consumerPower &&
                producer.Input.childItems.Count == 0 && consumer.Input.childItems.All(item => item.identifier == NightCheeseId) &&
                producer.WaterSlot?.childItem == null && consumer.WaterSlot?.childItem == null &&
                NightChainObjects().All(item => item.uniqueId > 0 && item.parentInventory != null && GeneralHelper.IsItemOwned(item)));
            int stockedCheese = consumer.Input.childItems.Where(item => item.identifier == NightCheeseId).Sum(item => item.unitCount);
            JObject? pendingCheese = NightCheeseDebt(consumer.Machine.uniqueId);
            int pendingCheeseUnits = pendingCheese == null ? 0 : (int?)pendingCheese["Remaining"] ?? -1;
            NightRequire("night.cheese-chain.one-consumed-unit-restock", "After pausing, K06 holds zero or one cheese; input plus remaining debt equals the one unit actually consumed", new
            {
                inputCheese = stockedCheese, remaining = pendingCheeseUnits, debt = pendingCheese,
                consumedInput = inputCheese, commit = consumed[0]
            }, stockedCheese is 0 or 1 && stockedCheese + pendingCheeseUnits == 1 &&
                (pendingCheese == null || (int?)pendingCheese["TargetCount"] == 1 && (bool?)pendingCheese["Manual"] == false),
                "Independent restock limit, following the verified one-unit F30 input and successful commit. No debt is assigned by the probe.");
            NightRequire("night.cheese-chain.no-extra-original-work", "Previously consumed original fixtures and the original missing-input control do not gain a second successful batch", observation.Work,
                fixtures.Select((fixture, index) => Energy(fixture.Battery) == idleEnergy[index] &&
                    !observation.Work.Any(row => (int?)row["uid"] == fixture.Machine.uniqueId && (bool?)row["committed"] == true)).All(value => value));
            var warmup = ((JObject)R.persistence["fullNight"]!).DeepClone();
            R.persistence["fullNight"] = new JObject
            {
                ["dayBefore"] = day, ["dayAfter"] = StoreStation.GetDayCounter(), ["completedNights"] = 3, ["gameRunId"] = Store.runID,
                ["warmup"] = warmup, ["bottlingNight"] = bottlingNight, ["fixtures"] = new JArray(allFixtures.Select(NightMachineState)),
                ["chainItems"] = NightChainItems(), ["automationPausedAfterAutomaticSave"] = true,
                ["chainLogistics"] = pausedSettings,
                ["automaticSave"] = observation.AutoSave!.DeepClone(),
                ["events"] = new JArray(observation.Events.Select(e => e.DeepClone())), ["actualWork"] = new JArray(observation.Work.Select(e => e.DeepClone()))
            };
        }
        R.untested.Add("Full night: three ordinary nights establish real consumption debt, food-before-bottling execution, and use of genuinely produced cheese on the following night. Nonzero water usage, full six-machine dependency chain, grid power plus matrix growth/idle decay, and long customer/story choices are not established.");
        R.phase = "night-complete-before-final-baseline-save";
    }

    private IEnumerable<object?> NightDay(int day, string prefix, NightObservation observation, bool ensureInsurance = false)
    {
        foreach (object? wait in NightWait(prefix + ".morning-ready", () => Store.storeState == PlayerStore.StoreState.MORNING &&
            StoreShutterButton.Instance != null && EndOfDayUIManager.Instance?.UI != null &&
            !StoreUIManager.Instance.blackscreen.gameObject.activeSelf && Store.lockShutterButtonFlag == 0, observation)) yield return wait;
        if (AdvCalendarUIManager.Instance.UIPanel.activeSelf) AdvCalendarUIManager.Instance.CloseUI();
        NightRequire(prefix + ".can-open", "Native CanStartDay, no arrest/shootout or shutter lock", NightUiState(),
            Store.CanStartDay() && !Store.isClientBeingArrested && !Store.isHandlingShootout && Store.lockShutterButtonFlag == 0 && !Store.devMode);
        StoreShutterButton.Instance.OnClick();
        foreach (object? wait in NightWait(prefix + ".shutter-opened", () => Store.storeState == PlayerStore.StoreState.OPEN && Store.lockShutterButtonFlag == 0, observation)) yield return wait;
        FixtureClearVisitors(prefix + ".generated-no-customer-night-input");
        if (!Store.CanEndDay())
        {
            R.environment["nightRequiredVisitorActions"] = _graylineActions;
            // wanted4 becomes dismissable in its first native dialogue action.
            // Finish the actual dialogue instead of closing at that early flag.
            foreach (object? wait in GraylineServeUntil(() => Store.CanEndDay() && DialogUIManager.Instance.currentDialog == null,
                "", prefix + ".required-customers"))
            {
                observation.SampleClock();
                yield return wait;
            }
        }
        if (ensureInsurance && StoreService.GetInsuranceService().unlocked)
        {
            R.environment["nightInsuranceFundingTrades"] = _reputationTrades;
            R.environment["nightInsuranceSaleFixtures"] = _forgeFixtures;
            R.environment["nightInsuranceFundingDecisions"] = _reputationFundingDecisions;
            foreach (object? wait in ReputationServeDay()) { observation.SampleClock(); yield return wait; }
        }
        if (!Store.CanEndDay()) NightBlock(prefix + ".can-close", "Native CanEndDay rejected the current/queued clients. Their normal dialogue/choices must be completed; no dismissable flags or queue are changed.");
        NightRequire(prefix + ".can-close", "Native CanEndDay, no arrest/shootout or shutter lock", NightUiState(),
            Store.CanEndDay() && !Store.isClientBeingArrested && !Store.isHandlingShootout && Store.lockShutterButtonFlag == 0);
        StoreShutterButton.Instance.OnClick();
        if (StoreUIManager.Instance.closeShutterConfirmationPanel.activeSelf)
        {
            NightRequire(prefix + ".native-close-confirmation", "Actual visible confirmation and native close eligibility", NightUiState(), Store.CanEndDay());
            // This is the actual confirmation callback, only after OnClick
            // opens its native confirmation panel. It does not clear clients.
            StoreShutterButton.Instance.EndAndClose();
        }
        foreach (object? wait in NightWait(prefix + ".shutter-closed", () => Store.storeState == PlayerStore.StoreState.CLOSED &&
            Store.currentClientInstance == null && StoreShutterButton.Instance.shutterGameObject.transform.position == StoreShutterButton.Instance.startPosition &&
            StoreShutterButton.Instance.blockerGameObject.transform.localPosition == StoreShutterButton.Instance.blockerStartPosition, observation)) yield return wait;
        var beds = UnityEngine.Object.FindObjectsOfType<StoreBedroll>().Where(bed => bed.gameObject.activeInHierarchy).ToArray();
        NightRequire(prefix + ".actual-bedroll", "Exactly one active native bedroll", beds.Select(bed => bed.name).ToArray(), beds.Length == 1);
        beds[0].OnClick();
        foreach (object? wait in NightWait(prefix + ".end-day-tablet", () => EndOfDayUIManager.Instance.IsOpen &&
            Reflect.Get(EndOfDayUIManager.Instance, "endDayButton") is TabletButton, observation)) yield return wait;
        if (ensureInsurance && StoreService.GetInsuranceService().unlocked)
        {
            var insurance = StoreService.GetInsuranceService();
            var rows = Reflect.Each(Reflect.Get(EndOfDayUIManager.Instance, "rows")).Where(row => ReferenceEquals(Reflect.Get(row, "Item1"), insurance)).ToArray();
            NightRequire(prefix + ".actual-insurance-row", "One unlocked noncooldown native insurance row", new { count = rows.Length, insurance.unlocked, cooldown = insurance.IsOnCooldown() }, rows.Length == 1 && !insurance.IsOnCooldown());
            var checkbox = (TabletCheckbox)Reflect.Get(rows[0], "Item2")!;
            if (!checkbox.IsOn)
            {
                NightRequire(prefix + ".actual-insurance-checkbox", "Visible interactable native insurance checkbox", checkbox.button?.IsInteractable(), checkbox.gameObject.activeInHierarchy && checkbox.button != null && checkbox.button.IsInteractable());
                checkbox.button!.onClick.Invoke(); yield return null;
            }
            NightRequire(prefix + ".insurance-selected-for-real-payment", true, new { checkbox.IsOn, insurance.isUsing, insurance.cost }, checkbox.IsOn && insurance.isUsing);
        }
        var end = (TabletButton)Reflect.Get(EndOfDayUIManager.Instance, "endDayButton")!;
        observation.Invoice = new JArray(Store.storeService.Where(service => service.unlocked && service.isUsing)
            .Select(service => JObject.FromObject(new { service.id, service.cost, service.totalCooldown })));
        observation.InvoiceTotal = Store.storeService.Where(service => service.unlocked && service.isUsing).Sum(service => service.cost);
        observation.Evidence["invoice"] = observation.Invoice;
        NightRequire(prefix + ".end-day-button-enabled", "Visible, interactable End Day button and affordable real service invoice", new
        {
            open = EndOfDayUIManager.Instance.IsOpen, active = end.gameObject.activeInHierarchy, end.Interactable,
            actualButton = end.button != null && end.button.IsInteractable(), cash = Store.playerCash, invoice = observation.Invoice, total = observation.InvoiceTotal
        }, end.gameObject.activeInHierarchy && end.Interactable && end.button != null && end.button.IsInteractable() && observation.InvoiceTotal <= Store.playerCash);
        observation.Armed = true;
        R.phase = prefix + ".native-end-day-coroutine";
        end.button!.onClick.Invoke();
        foreach (object? wait in NightWait(prefix + ".next-day-morning-report", () => StoreStation.GetDayCounter() == day + 1 &&
            Store.storeState == PlayerStore.StoreState.MORNING && !StoreUIManager.Instance.blackscreen.gameObject.activeSelf &&
            !StoreUIManager.Instance.savingTMP.gameObject.activeSelf && !EndOfDayUIManager.Instance.IsOpen &&
            AdvCalendarUIManager.Instance.UIPanel.activeSelf && AdvCalendarUIManager.Instance.nightlyReportSection.activeSelf &&
            observation.Events.Count(e => (string?)e["name"] == "OnWakingUpLate") == 1, observation, 60f)) yield return wait;

        NightRequire(prefix + ".exact-native-stages", "One native EndNight, BeginDay and automatic SaveGame; each real lifecycle event once in native order; old-day sleep/new-day waking", observation.Evidence,
            observation.Entered.GetValueOrDefault("EndNight") == 1 && observation.Returned.GetValueOrDefault("EndNight") == 1 &&
            observation.Entered.GetValueOrDefault("BeginDay") == 1 && observation.Returned.GetValueOrDefault("BeginDay") == 1 &&
            observation.Entered.GetValueOrDefault("SaveGame") == 1 && observation.Returned.GetValueOrDefault("SaveGame") == 1 &&
            observation.Events.Select(e => (string)e["name"]!).SequenceEqual(NightEvents) &&
            observation.Events.All(e => (int)e["day"]! == ((string)e["name"]! is "OnWakingUpEarly" or "OnWakingUpLate" ? day + 1 : day)));
        var sleep = observation.Events.Single(e => (string?)e["name"] == "OnGoingSleepEarly");
        var charged = observation.Events.Single(e => (string?)e["name"] == "OnHandlingNightlyServicesEarly");
        NightRequire(prefix + ".actual-service-charge", observation.Invoice, new { cashBefore = sleep["cash"], cashAfter = charged["cash"], total = observation.InvoiceTotal, services = charged["services"] },
            (int)sleep["cash"]! - (int)charged["cash"]! == observation.InvoiceTotal && observation.Invoice.All(invoice =>
                ((JArray)charged["services"]!).Any(service => (string?)service["id"] == (string?)invoice["id"] && (int?)service["currentCooldown"] == (int?)invoice["totalCooldown"])),
            "Invoice captured from actual enabled services before the click; cash/cooldowns observed immediately at the native post-charge event, before other services and next-day income.");

        NightRequire(prefix + ".automatic-save-before-probe-save", "Automatic native ES3 readback succeeded before waking and before ProbeContext.Save", observation.AutoSave,
            observation.AutoSave != null && (bool?)observation.AutoSave["valid"] == true);
        NightRequire(prefix + ".normal-speed-and-no-native-errors", "Time scale remained 1; no native errors or observer failures", new
        {
            observation.MinimumTimeScale, observation.MaximumTimeScale, observation.Errors, observation.ObserverErrors
        }, observation.MinimumTimeScale == 1f && observation.MaximumTimeScale == 1f && observation.Errors.Count == 0 && observation.ObserverErrors.Count == 0);
    }

    internal IEnumerable<object?> NightReload(JObject baseline)
    {
        R.phase = "night-independent-process-reload";
        var night = baseline["persistence"]?["fullNight"] as JObject;
        NightRequire("night.reload-completed-night-baseline", "Passed three-night save with three actual automatic saves and exactly three days advanced", night,
            (string?)baseline["mode"] == "night-save" && night != null && (bool?)night["automaticSave"]?["valid"] == true &&
            (bool?)night["warmup"]?["automaticSave"]?["valid"] == true && (bool?)night["bottlingNight"]?["automaticSave"]?["valid"] == true &&
            (int?)night["completedNights"] == 3 && (int?)night["warmup"]?["dayAfter"] == (int?)night["dayBefore"] + 1 &&
            (int?)night["bottlingNight"]?["dayBefore"] == (int?)night["dayBefore"] + 1 &&
            (int?)night["bottlingNight"]?["dayAfter"] == (int?)night["dayBefore"] + 2 &&
            (int?)night["dayAfter"] == (int?)night["dayBefore"] + 3 && (string?)night["gameRunId"] == Store.runID);
        NightRequire("night.reload-day-and-morning", night!["dayAfter"], new { day = StoreStation.GetDayCounter(), state = Store.storeState.ToString() },
            StoreStation.GetDayCounter() == (int)night["dayAfter"]! && Store.storeState == PlayerStore.StoreState.MORNING);
        foreach (var expected in (JArray)night["fixtures"]!)
        {
            int uid = (int)expected["machineUid"]!;
            var machine = Store.FindAllItem().SingleOrDefault(item => item.uniqueId == uid);
            NightRequire("night.reload-machine." + uid, expected, machine == null ? null : NightMachineState(machine),
                machine != null && JToken.DeepEquals(expected, NightMachineState(machine)),
                "Actual independent-process LoadGame: machine, battery, remaining input, output UIDs, quantities, values, tags and real input/output placement.");
            yield return null;
        }
        NightRequire("night.reload-cheese-chain-items", night["chainItems"], NightChainItems(),
            JToken.DeepEquals(night["chainItems"], NightChainItems()) && NightChainObjects().All(item =>
                item.uniqueId > 0 && item.parentInventory != null && GeneralHelper.IsItemOwned(item)),
            "Includes the real ejected cheese outside machine storage, actual patties, their UIDs, quantities, values and nutrition after independent-process LoadGame.");
        var loadedMachines = LogisticsState["Machines"]!;
        foreach (var expected in ((JObject)night["chainLogistics"]!).Properties())
            NightRequire("night.reload-cheese-chain-logistics." + expected.Name, expected.Value, loadedMachines[expected.Name],
                JToken.DeepEquals(expected.Value, loadedMachines[expected.Name]),
                "Real saved per-machine settings and remaining consumption debt; no feeder state is fabricated during reload.");
        R.persistence["fullNight"] = night.DeepClone();
    }

    private static bool NightRecipeMatches(object view, string output, int count, params (string Id, int Count)[] inputs)
    {
        object[] actual = Reflect.Each(Reflect.Get(view, "Ingredients")).ToArray();
        return Reflect.Str(view, "OutputItemId") == output && Reflect.Int(view, "OutputCount") == count && Reflect.Int(view, "WaterParts") == 0 &&
            actual.Length == inputs.Length && inputs.All(input => actual.Count(rule => Reflect.Str(rule, "ItemId") == input.Id && Reflect.Int(rule, "Amount") == input.Count) == 1);
    }
    private GameItem[] NightChainObjects() => Store.FindAllItem().Where(item => item.identifier is NightCheeseId or NightPattyId)
        .Distinct().OrderBy(item => item.uniqueId).ToArray();
    private JArray NightChainItems() => new(NightChainObjects().Select(NightItemState));
    private int NightChainUnits(string id) => NightChainObjects().Where(item => item.identifier == id).Sum(item => item.unitCount);
    private long NightChainCalories(string id) => NightChainObjects().Where(item => item.identifier == id)
        .Sum(item => (long)item.unitCount * item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt());
    private JObject? NightCheeseDebt(int machineUid) => LogisticsState["Machines"]?[machineUid.ToString()]?["PendingItems"]?
        .Children<JObject>().SingleOrDefault(item => (string?)item["ItemId"] == NightCheeseId);

    private void NightRequire(string name, object? expected, object? observed, bool passed, string evidence = "")
    {
        R.Check(name, expected, observed, passed, evidence);
        if (!passed) throw new InvalidOperationException(name + " failed; do not generate a successful final-save baseline");
    }
    private void NightBlock(string name, string reason)
    {
        R.environment["nightBlocker"] = NightUiState(); R.Skip(name, reason);
        throw new InvalidOperationException(name + ": " + reason);
    }
    private IEnumerable<object?> NightWait(string name, Func<bool> ready, NightObservation observation, float seconds = 20f)
    {
        R.phase = name; float deadline = Time.realtimeSinceStartup + seconds;
        while (!ready())
        {
            observation.SampleClock();
            if (GameOverUIManager.Instance != null && GameOverUIManager.Instance.IsShowing) NightBlock(name, "Native game-over branch reached");
            if (Time.realtimeSinceStartup >= deadline) NightBlock(name, "Timed out waiting for the actual native UI/coroutine condition; see nightBlocker and nightObservation");
            yield return null;
        }
        observation.SampleClock();
        R.Check(name, "Actual native condition reached", NightUiState(), true);
    }
    private object NightUiState()
    {
        object? Client(StoreClient? client) => client == null ? null : new { client.identifier, client.displayName, client.dismissable, intent = client.clientIntent.ToString() };
        return new
        {
            day = StoreStation.GetDayCounter(), state = Store.storeState.ToString(), Time.timeScale, Store.lockShutterButtonFlag,
            Store.isClientBeingArrested, Store.isHandlingShootout, Store.devMode, canEndDay = Store.CanEndDay(),
            currentClient = Client(Store.currentClientInstance?.GetClientBlueprint()), queue = Store.storeClientManager.clientStack.Select(Client).ToArray(),
            endDayOpen = EndOfDayUIManager.Instance?.IsOpen, saving = StoreUIManager.Instance.savingTMP.gameObject.activeSelf,
            blackscreen = StoreUIManager.Instance.blackscreen.gameObject.activeSelf,
            morningReport = AdvCalendarUIManager.Instance != null && AdvCalendarUIManager.Instance.UIPanel.activeSelf
        };
    }
    private JObject NightMachineState(RecipeFixture fixture) => NightMachineState(fixture.Machine);
    private JObject NightMachineState(GameItem machine)
    {
        object ports = Ports(machine);
        JArray Items(GameInventory? inventory) => new((inventory?.childItems ?? new List<GameItem>()).OrderBy(item => item.uniqueId).Select(NightItemState));
        return new JObject
        {
            ["machineUid"] = machine.uniqueId, ["machine"] = NightItemState(machine),
            ["input"] = Items((GameInventory)Reflect.Get(ports, "Input")!), ["output"] = Items((GameInventory)Reflect.Get(ports, "Output")!),
            ["battery"] = Items(Reflect.Get(ports, "Battery") as GameInventory), ["water"] = Items(Reflect.Get(ports, "Water") as GameInventory),
            ["modules"] = Items(Reflect.Get(ports, "Modules") as GameInventory)
        };
    }
    private static JObject NightItemState(GameItem item) => NightItemState(item.identifier, item.uniqueId, item.unitCount, item.unitValue, item.state);
    private static JObject NightItemState(string id, int uid, int count, long value, TagSystem state)
    {
        var tags = new JObject();
        foreach (string key in NightSavedTags.Concat(Liquid.Liquids.Select(liquid => liquid.currentPart)))
        {
            state.dict.TryGetValue(key, out var tag);
            tags[key] = JObject.FromObject(new { enabled = tag?.valueEnabled ?? false, number = tag?.GetInt() ?? 0, text = tag?.GetString() ?? "" });
        }
        return new JObject { ["id"] = id, ["uid"] = uid, ["count"] = count, ["value"] = value, ["tags"] = tags };
    }

    // Scoped observers never replace a callback, suppress an exception, alter a
    // return value, queue work or write a completion marker into game state.
    private sealed class NightObservation : IDisposable
    {
        private static NightObservation? Active;
        private readonly ProbeContext _context;
        private readonly RecipeFixture[] _fixtures;
        private readonly int _day;
        private readonly Harmony _harmony;
        private readonly List<MethodInfo> _patched = new();
        private readonly List<(EventInfo Event, Action Handler)> _handlers = new();
        private readonly Dictionary<object, JObject> _attempts = new();
        internal readonly List<JObject> Events = new(), Work = new();
        internal readonly List<object> Errors = new(), Warnings = new();
        internal readonly List<string> ObserverErrors = new();
        internal readonly Dictionary<string, int> Entered = new(), Returned = new();
        internal readonly JObject Evidence = new();
        internal JObject? AutoSave;
        internal JArray Invoice = new();
        internal int InvoiceTotal;
        internal bool Armed;
        internal float MinimumTimeScale = 1f, MaximumTimeScale = 1f;
        internal NightObservation(ProbeContext context, RecipeFixture[] fixtures, int day)
        {
            if (Active != null) throw new InvalidOperationException("A full-night observation is already active");
            _context = context; _fixtures = fixtures; _day = day;
            _harmony = new Harmony("nicokobo.content_native_probe.night." + context.O.RunId);
            try
            {
                foreach (string name in NightEvents)
                {
                    EventInfo hook = typeof(ModHook).GetEvent(name, Reflect.All)!;
                    Action handler = () => Observe(() => Event(name));
                    hook.AddEventHandler(null, handler); _handlers.Add((hook, handler));
                }
                Patch(typeof(PlayerStore).GetMethod(nameof(PlayerStore.EndNight))!, nameof(BeforeNative), nameof(AfterNative));
                Patch(typeof(PlayerStore).GetMethod(nameof(PlayerStore.BeginDay))!, nameof(BeforeNative), nameof(AfterNative));
                Patch(typeof(PlayerStore).GetMethod(nameof(PlayerStore.SaveGame))!, nameof(BeforeNative), nameof(AfterNative));
                Type hooks = Reflect.Type("Nicokobo.ContentInterop.ProductionHooks");
                Patch(hooks.GetMethod("Begin", Reflect.All)!, null, nameof(AfterBegin));
                Patch(hooks.GetNestedType("Attempt", Reflect.All)!.GetMethod("Dispose", Reflect.All)!, null, nameof(AfterDispose));
                Application.logMessageReceived += Log;
                Active = this;
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodInfo method, string? before, string? after)
        {
            _harmony.Patch(method, prefix: before == null ? null : new HarmonyMethod(typeof(NightObservation).GetMethod(before, Reflect.All)),
                postfix: after == null ? null : new HarmonyMethod(typeof(NightObservation).GetMethod(after, Reflect.All)));
            _patched.Add(method);
        }
        private void Observe(Action action)
        {
            try { action(); }
            catch (Exception error) { ObserverErrors.Add(error.ToString()); }
            Refresh();
        }
        private void Refresh()
        {
            Evidence["events"] = new JArray(Events); Evidence["actualWork"] = new JArray(Work);
            Evidence["entered"] = JObject.FromObject(Entered); Evidence["returned"] = JObject.FromObject(Returned);
            Evidence["errors"] = JArray.FromObject(Errors); Evidence["warnings"] = JArray.FromObject(Warnings);
            Evidence["observerErrors"] = new JArray(ObserverErrors); Evidence["automaticSave"] = AutoSave;
        }
        internal void SampleClock()
        { MinimumTimeScale = Math.Min(MinimumTimeScale, Time.timeScale); MaximumTimeScale = Math.Max(MaximumTimeScale, Time.timeScale); }
        private void Event(string name)
        {
            SampleClock();
            Events.Add(JObject.FromObject(new
            {
                name, day = StoreStation.GetDayCounter(), frame = Time.frameCount, cash = _context.Store.playerCash,
                services = _context.Store.storeService.Where(service => service.unlocked && service.isUsing)
                    .Select(service => new { service.id, service.cost, service.currentCooldown }).ToArray()
            }));
        }
        private void Log(string message, string trace, LogType type)
        {
            if (type is LogType.Error or LogType.Exception or LogType.Assert) Errors.Add(new { type = type.ToString(), message, trace, frame = Time.frameCount });
            else if (type == LogType.Warning) Warnings.Add(new { message, trace, frame = Time.frameCount });
            Refresh();
        }
        private static void BeforeNative(MethodBase __originalMethod)
        {
            var o = Active;
            if (o == null || !o.Armed) return;
            o.Observe(() => { o.SampleClock(); string name = __originalMethod.Name; o.Entered[name] = o.Entered.GetValueOrDefault(name) + 1; });
        }
        private static void AfterNative(MethodBase __originalMethod)
        {
            var o = Active;
            if (o == null || !o.Armed) return;
            o.Observe(() =>
            {
                string name = __originalMethod.Name; o.Returned[name] = o.Returned.GetValueOrDefault(name) + 1;
                if (name == nameof(PlayerStore.SaveGame)) o.ReadAutomaticSave();
            });
        }
        private static void AfterBegin(object __result)
        {
            var o = Active;
            if (o == null || !o.Armed) return;
            o.Observe(() =>
            {
                if (Reflect.Get(__result, "_parent") != null) return;
                object context = Reflect.Get(__result, "Context")!;
                var machine = (GameItem)Reflect.Get(context, "Machine")!;
                object? recipe = Reflect.Get(context, "Recipe");
                var row = JObject.FromObject(new { ordinal = o.Work.Count, uid = machine.uniqueId, machine = machine.identifier,
                    recipe = recipe == null ? "" : Reflect.Str(recipe, "Id"), day = StoreStation.GetDayCounter(), frame = Time.frameCount,
                    nativeNightPower = CurrentConverterHelper.isNightCycle, completed = false, committed = false });
                if ((string?)row["recipe"] is NightCheeseRecipe or NightPattyRecipe)
                {
                    object ports = Reflect.Get(context, "Ports")!;
                    var input = (GameInventory)Reflect.Get(ports, "Input")!;
                    row["inputsAtBegin"] = new JArray(input.childItems.OrderBy(item => item.uniqueId).Select(NightItemState));
                }
                o._attempts.Add(__result, row); o.Work.Add(row);
            });
        }
        private static void AfterDispose(object __instance)
        {
            var o = Active;
            if (o == null || !o.Armed) return;
            o.Observe(() =>
            {
                if (!o._attempts.TryGetValue(__instance, out var row) || (bool?)row["completed"] == true) return;
                row["committed"] = (bool)Reflect.Get(__instance, "_committed")!;
                row["completed"] = (bool)Reflect.Get(__instance, "_closed")!;
                row["completedFrame"] = Time.frameCount;
                if ((string?)row["recipe"] is NightCheeseRecipe or NightPattyRecipe)
                    row["chainItemsAfter"] = o._context.NightChainItems();
            });
        }
        private void ReadAutomaticSave()
        {
            var store = _context.Store;
            string file = SaveFiles.AbsolutePath(SaveFiles.Slot(store.saveSlotId));
            var settings = new ES3Settings(file, ES3.Location.File);
            var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", settings)!;
            var station = ES3.Load<StoreStation>("storeStation", settings);
            var nodes = new[] { saved.mainInvJSON, saved.backInvJSON }.SelectMany(json => JsonUtility.FromJson<SaveState>(json).saveItems).ToArray();
            var live = _fixtures.SelectMany(f => new[] { f.Machine }.Concat(f.Machine.FindAllChildItems()))
                .Concat(_context.NightChainObjects()).Distinct().OrderBy(item => item.uniqueId).ToArray();
            var expected = new JArray(live.Select(NightItemState));
            var observed = new JArray(); bool unique = true;
            foreach (var item in live)
            {
                var matches = nodes.Where(node => node.uniqueId == item.uniqueId).ToArray();
                if (matches.Length != 1) { unique = false; continue; }
                var node = matches[0]; observed.Add(NightItemState(node.identifier, node.uniqueId, node.unitCount, node.unitValue, node.itemState));
            }
            var actualData = JObject.FromObject(store.ModData()); var savedData = JObject.FromObject(saved.ModData());
            bool valid = Returned.GetValueOrDefault("EndNight") == 1 && Returned.GetValueOrDefault("BeginDay") == 1 &&
                !Events.Any(e => (string?)e["name"] is "OnWakingUpEarly" or "OnWakingUpLate") && station.dayCounter == _day + 1 &&
                saved.runID == store.runID && saved.saveSlotId == store.saveSlotId && saved.playerCash == store.playerCash &&
                unique && JToken.DeepEquals(expected, observed) && JToken.DeepEquals(actualData, savedData);
            AutoSave = new JObject
            {
                ["valid"] = valid, ["path"] = file, ["sha256"] = Reflect.HashFile(file), ["day"] = station.dayCounter,
                ["gameRunId"] = saved.runID, ["slotId"] = saved.saveSlotId, ["cash"] = saved.playerCash,
                ["expectedItems"] = expected, ["savedItems"] = observed, ["savedModData"] = savedData, ["expectedModData"] = actualData,
                ["beforeWaking"] = !Events.Any(e => (string?)e["name"] is "OnWakingUpEarly" or "OnWakingUpLate")
            };
        }
        public void Dispose()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            Application.logMessageReceived -= Log;
            foreach (var handler in _handlers) handler.Event.RemoveEventHandler(null, handler.Handler);
            foreach (var method in _patched) _harmony.Unpatch(method, HarmonyPatchType.All, _harmony.Id);
            Refresh();
        }
    }
}
