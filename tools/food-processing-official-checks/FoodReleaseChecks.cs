using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing;
using Nicokobo.FoodProcessing.Definitions;
using Nicokobo.FoodProcessing.Domain;

internal static class FoodReleaseChecks
{
    private static (GameItem Machine, GameSlotInventory Battery, GameGridInventory Input, GameGridInventory Output, GameSlotInventory Water)
        Setup(RecipeDefinition recipe)
    {
        PowerHelper.RefuseDraw = PowerHelper.ThrowAfterDebit = DirectoryMaster.FailCleanup = false;
        IndustrialFishFeedHelper.Requests.Clear();
        var machine = new GameItem { identifier = recipe.MachineId };
        machine.GetTagReadonly("is_machinery_powered").SetBool(true);
        machine.GetTagReadonly("power").SetInt(10);
        machine.GetTagReadonly("machine_quality").SetInt(200);
        machine.GetTagReadonly(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).SetInt(10);
        var battery = new GameSlotInventory(); var cell = new GameItem(); battery.UncheckedAccept(cell);
        cell.GetTagReadonly("energy").SetInt(100);
        var input = new GameGridInventory(); var output = new GameGridInventory();
        var water = new GameSlotInventory(); var bottle = new GameItem(); water.UncheckedAccept(bottle);
        bottle.GetTagReadonly("water").SetInt(1000000); bottle.GetTagReadonly("quality").SetInt(2);
        machine.Storages[0] = battery; machine.Storages[1] = new GameGridInventory();
        machine.Storages[2] = input; machine.Storages[3] = output; machine.Storages[5] = water;
        foreach (var rule in recipe.ItemInputs)
        {
            string id = rule.ResourceId switch
            {
                "edible_mushroom" => "dream_cap", "small_meat_cut" => "morsel", "alcohol_base" => "red_beer",
                "fish_fillet" when recipe.Code == "F45" => "tiny_fish_fillet",
                FoodDefinitions.RawMeatTag => "small_raw_meat",
                _ => rule.ResourceId
            };
            bool pooled = FoodDefinitions.IsPooledMedicalKit(recipe.OutputItemId);
            var types = rule.ResourceId switch
            {
                FoodDefinitions.BasicMedicalPool => new[] { "kotton_fiber", "rubbing_alcohol", "bandage_item" },
                FoodDefinitions.AdvancedMedicalPool => new[] { "tilapia_skin_graft", "salve", "hemostatic_bandage_item" },
                FoodDefinitions.RestrictedMedicalPool => new[] { "blue_blood_bag", "caffeine_pill", "phagimycin_pill" },
                _ => new[] { id }
            };
            foreach (string type in types)
            {
                var item = new GameItem { identifier = type, spritePath = type, unitCount = pooled ? 1 : rule.Amount, unitValue = 10 };
                if (id == "nutrifruit" && recipe.Code == "F47") item.spritePath = "nutrifruit_l";
                item.Types.Add("FOOD"); item.EnableTag(FoodDefinitions.RawMeatTag);
                item.GetTagReadonly("owned").SetBool(true);
                item.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(100);
                item.GetTagReadonly("genuine").SetBool(true);
                if (id == "wine_berry") { item.EnableTag("BERRY_TAG"); item.GetTagReadonly("BERRY_CONDITION_INT").SetInt(0); }
                if (id == "wine_bottle") { item.EnableTag("WINE_CONTAINER_TAG"); item.GetTagReadonly("WINE_CONTAINER_STATE").SetInt(2); item.GetTagReadonly("WINE_SCORE_INT").SetInt(5); item.Types.Add("ALCOHOL"); item.GetTagReadonly("ALCOHOL_VALUE").SetInt(3); }
                if (id == "red_beer") item.GetTagReadonly("ALCOHOL_VALUE").SetInt(1);
                input.UncheckedAccept(item);
            }
        }
        return (machine, battery, input, output, water);
    }

    private static void CheckIngredientStock(Action<bool, string> check)
    {
        GameItem Put(GameInventory inventory, string id, int count = 1)
        {
            var item = new GameItem { identifier = id, unitCount = count };
            item.GetTagReadonly("owned").SetBool(true);
            inventory.UncheckedAccept(item);
            return item;
        }

        var warehouse = new GameGridInventory();
        var voidContainer = Put(warehouse, FoodIngredientStock.VoidContainer);
        var firstPage = Put(new GameGridInventory { ContainingItem = voidContainer }, "nutrifruit");
        var laterPage = Put(new GameGridInventory { ContainingItem = voidContainer }, "nutrifruit");
        var direct = Put(warehouse, "nutrifruit");
        var container = Put(warehouse, "storage_bay");
        var contents = new GameGridInventory { ContainingItem = container };
        var shallow = Put(contents, "nutrifruit");
        var inner = Put(contents, "backpack");
        var nested = Put(new GameGridInventory { ContainingItem = inner }, "nutrifruit");
        var machine = Put(warehouse, FoodDefinitions.FoodProcessorItem.Id);
        machine.Types.Add("MACHINE");
        var reserved = Put(new GameGridInventory { ContainingItem = machine }, "nutrifruit");
        var locked = Put(new GameGridInventory { ContainingItem = inner, RemoveLocked = true }, "nutrifruit");
        check(FoodIngredientStock.OrderedSources(new[] { nested, shallow, locked, direct, firstPage, reserved, laterPage }, warehouse)
            .SequenceEqual(new[] { firstPage, laterPage, direct, shallow, nested }),
            "Recipe filling takes stock from every void tab before warehouse, container and nested stock, skipping unavailable stock");

        var input = new GameGridInventory();
        var stack = Put(input, "nutrifruit", 3);
        var rules = new[]
        {
            new IngredientView("nutrifruit", 2, item => item.identifier == "nutrifruit", "", ""),
            new IngredientView("fruit", 2, item => item.identifier is "nutrifruit" or "wine_berry", "", "")
        };
        check(FoodIngredientStock.MissingItems(input, rules, 0) == 0 &&
            FoodIngredientStock.MissingItems(input, rules, 1) == 1 && stack.unitCount == 3,
            "Recipe fill counts a machine-held stack only once across overlapping requirements");

        var counter = Put(new GameGridInventory(), "nutrifruit");
        var showcase = Put(new GameGridInventory(), "nutrifruit");
        check(FoodIngredientStock.OrderedSources(new[] { counter, showcase }, warehouse).Length == 2,
            "Owned counter and showcase ingredients must participate in recipe availability and filling");
        var recipe = FoodDefinitions.Recipes.First(value => value.Stars == 3 &&
            value.ItemInputs.Any() && FoodProduction.WaterNeeds(value).Parts == 0);
        var setup = Setup(recipe);
        var stock = setup.Input.childItems.ToArray();
        foreach (var item in stock) { setup.Input.Expel(item); warehouse.UncheckedAccept(item); }
        var view = FoodInterop.View(recipe);
        var counts = stock.Select(item => item.unitCount).ToArray();
        check(!FoodProduction.HasIngredients(setup.Machine, view) &&
            FoodIngredientStock.CanSupply(setup.Machine, view, stock),
            "Recipe lighting must include external stock before ingredients have been moved into the machine");
        check(stock.All(item => item.parentInventory == warehouse) && stock.Select(item => item.unitCount).SequenceEqual(counts),
            "Availability scanning must not move or consume ingredients");
        check(!FoodIngredientStock.CanSupply(setup.Machine, view, Array.Empty<GameItem>()),
            "Missing ingredients must leave the recipe dimmed");
        setup.Machine.GetTagReadonly("machine_quality").SetInt(0);
        check(!FoodIngredientStock.CanSupply(setup.Machine, view, stock),
            "External stock must not bypass machine quality requirements");
    }

    private static void CheckStorageExpansion(Action<bool, string> check)
    {
        foreach (var definition in FoodDefinitions.Machines)
        {
            var machine = new GameItem { identifier = definition.ItemId, contentWindow = new PixelWindow() };
            machine.GetTagReadonly("owned").SetBool(true);
            var input = new GameGridInventory().SetShape(9, 6);
            var output = new GameGridInventory().SetShape(9, 6);
            var ingredient = new GameItem();
            var product = new GameItem();
            input.UncheckedAccept(ingredient);
            output.UncheckedAccept(product);
            FoodStorageExpansion.Attach(machine, definition, input, output);
            machine.onLoaded!(machine);

            var ingot = new GameItem { identifier = "metal_ingot" };
            machine.onItemTargetThisFunc!(ingot, machine);
            check(input.Height == 6 && output.Height == 6 && !ingot.Destroyed,
                "An old machine changed capacity or took an unowned ingot: " + definition.Code);
            ingot.GetTagReadonly("owned").SetBool(true);
            machine.onItemTargetThisFunc(ingot, machine);
            check(ingot.Destroyed && input.Height == 7 && output.Height == 7 &&
                input.childItems.Single() == ingredient && output.childItems.Single() == product,
                "First expansion failed to take one ingot, grow both grids or preserve stock: " + definition.Code);

            var loaded = new GameItem { identifier = definition.ItemId, contentWindow = new PixelWindow(), state = machine.state.Clone() };
            var loadedInput = new GameGridInventory().SetShape(9, 6);
            var loadedOutput = new GameGridInventory().SetShape(9, 6);
            FoodStorageExpansion.Attach(loaded, definition, loadedInput, loadedOutput);
            loaded.onLoaded!(loaded);
            check(loadedInput.Height == 7 && loadedOutput.Height == 7,
                "Loaded machine lost its upgraded grid size: " + definition.Code);

            var stack = new GameItem { identifier = "metal_ingot", unitCount = 4 };
            stack.GetTagReadonly("owned").SetBool(true);
            for (int attempt = 0; attempt < 4; attempt++) loaded.onItemTargetThisFunc!(stack, loaded);
            check(!stack.Destroyed && stack.unitCount == 1 && FoodStorageExpansion.Level(loaded) == 4 &&
                loadedInput.Width == 9 && loadedOutput.Width == 9 && loadedInput.Height == 10 && loadedOutput.Height == 10,
                "Expansion consumed too many ingots or exceeded the limit: " + definition.Code);
        }
    }

    private static void CheckNewIngredients(Action<bool, string> check)
    {
        var recipes = FoodDefinitions.Recipes.ToDictionary(recipe => recipe.Code);
        RecipeRequirement Rule(string code, string id) => recipes[code].ItemInputs.Single(input => input.ResourceId == id);
        foreach (var (sprite, small) in new[] { ("nutrifruit_s", true), ("nutrifruit", true), ("nutrifruit_l", false), ("nutrifruit_xl", false) })
        {
            var fruit = new GameItem { identifier = "nutrifruit", spritePath = sprite };
            check(FoodMachines.Matches(fruit, Rule("F42", "nutrifruit")) == small &&
                FoodMachines.Matches(fruit, Rule("F47", "nutrifruit")) != small, "Wrong nutrifruit size admitted: " + sprite);
        }
        foreach (var (id, chowder, fry) in new[]
        {
            ("tiny_fish_fillet", true, false), ("small_fish_fillet", true, false),
            ("fish_fillet", false, true), ("large_fish_fillet", false, false)
        })
        {
            var fillet = new GameItem { identifier = id };
            check(FoodMachines.Matches(fillet, Rule("F45", "fish_fillet")) == chowder &&
                FoodMachines.Matches(fillet, Rule("F46", "fish_fillet")) == fry &&
                FoodMachines.Matches(fillet, Rule("F47", "large_fish_fillet")) == (id == "large_fish_fillet"),
                "Fish size leaked into the wrong new recipe: " + id);
        }
        var berry = new GameItem { identifier = "wine_berry" };
        berry.EnableTag("BERRY_TAG");
        foreach (int condition in new[] { -2, -1, 1, 2 })
        {
            berry.GetTagReadonly("BERRY_CONDITION_INT").SetInt(condition);
            check(FoodMachines.Matches(berry, Rule("F43", "wine_berry")) == (condition > 0), "Jam accepted unripe or moldy berries");
        }
    }

    private static void CheckCareKit(Action<bool, string> check)
    {
        foreach (var (kitId, expected) in new (string, string[])[]
        {
            (FoodDefinitions.NomoreCareKitId, ["pink_alt_injector", "bandage_item", "rubbing_alcohol"]),
            (FoodDefinitions.BasicMedicalKitId, ["bandage_item", "rubbing_alcohol", "smelling_salt"]),
            (FoodDefinitions.AdvancedMedicalKitId, ["hemostatic_bandage_item", "salve", "tilapia_skin_graft"]),
            (FoodDefinitions.RestrictedMedicalKitId, ["unlicensed_phagimycin_pill", "fanta_pill", "dream_dust"])
        })
        {
            var kit = DirectoryMaster.Item(kitId);
            var backpack = new GameGridInventory();
            backpack.UncheckedAccept(kit);
            kit.GetTagReadonly("owned").SetBool(true);
            check(FoodCareKit.TryUnpack(kit, backpack) && kit.Destroyed &&
                backpack.childItems.Select(item => item.identifier).OrderBy(id => id).SequenceEqual(expected.OrderBy(id => id)) &&
                backpack.childItems.All(item => item.unitCount == 1), $"Spawned care kit did not unpack its three default items: {kitId}");

            SaveManager.IsDecoding = true;
            GameItem loading;
            try { loading = DirectoryMaster.Item(kitId); }
            finally { SaveManager.IsDecoding = false; }
            check(loading.FindAllChildItems().Count == 0, $"Loading care kit would add defaults before restoring saved contents: {kitId}");
        }

        var basic = FoodDefinitions.Recipes.Single(value => value.Code == "F54");
        var bulk = Setup(basic);
        foreach (var item in bulk.Input.childItems.ToArray()) bulk.Input.Expel(item);
        var pills = Enumerable.Range(0, 4).Select(_ => new GameItem { identifier = "birth_control_pill", unitValue = 15 }).ToArray();
        foreach (var pill in pills.Take(2)) bulk.Input.UncheckedAccept(pill);
        check(!FoodProduction.TryProcess(bulk.Machine, bulk.Battery, bulk.Input, bulk.Output, bulk.Water, basic),
            "Medical pool packed fewer than three items");
        foreach (var pill in pills.Skip(2)) bulk.Input.UncheckedAccept(pill);
        var unrelated = new GameItem { identifier = "fanta_pill" };
        bulk.Input.UncheckedAccept(unrelated);
        check(!FoodProduction.TryProcess(bulk.Machine, bulk.Battery, bulk.Input, bulk.Output, bulk.Water, basic),
            "Medical pool counted duplicate items as different types");
        var alcohol = new GameItem { identifier = "rubbing_alcohol" };
        var salts = new GameItem { identifier = "smelling_salt" };
        var view = FoodInterop.View(basic);
        check(!FoodIngredientStock.CanSupply(bulk.Machine, view, new[] { alcohol }) &&
            FoodIngredientStock.CanSupply(bulk.Machine, view, new[] { alcohol, salts }),
            "Stock readiness disagreed with the medical pool type minimum");
        bulk.Input.UncheckedAccept(alcohol);
        bulk.Input.UncheckedAccept(salts);
        var eligible = pills.Concat(new[] { alcohol, salts }).ToArray();
        var preview = FoodProduction.PreviewCycle(bulk.Machine);
        check(preview.Batches == 1 && preview.Outputs.Single().Recipe.OutputItemId == basic.OutputItemId &&
            preview.Inputs.Select(take => take.Item).SequenceEqual(eligible), "Medical pool preview disagreed with the batch inventory");
        check(FoodProduction.TryProcess(bulk.Machine, bulk.Battery, bulk.Input, bulk.Output, bulk.Water, basic),
            "Medical pool did not accept duplicates alongside three different eligible types");
        var bulkKit = bulk.Output.childItems.Single();
        bulkKit.GetTagReadonly("owned").SetBool(true);
        var unpacked = new GameGridInventory();
        check(bulk.Input.childItems.SequenceEqual(new[] { unrelated }) && FoodCareKit.TryUnpack(bulkKit, unpacked) &&
            unpacked.childItems.SequenceEqual(eligible), "Medical pool did not move exactly the original eligible items");

        var advanced = FoodDefinitions.Recipes.Single(value => value.Code == "F55");
        var injector = new GameItem { identifier = FoodDefinitions.ImmunivaxId };
        var advancedPool = advanced.ItemInputs.Single();
        check(!FoodMachines.Matches(injector, advancedPool), "Advanced medical pool accepted counterfeit Immunivax");
        injector.GetTagReadonly("genuine").SetBool(true);
        check(FoodMachines.Matches(injector, advancedPool), "Advanced medical pool rejected genuine Immunivax");

        foreach (var (code, bandage, fee) in new[]
        {
            ("F53", "", 40),
            ("F54", "bandage_item", 18), ("F54", "hemostatic_bandage_item", 18), ("F54", "topical_bandage_item", 18),
            ("F55", "", 25), ("F56", "", 40)
        })
        foreach (var failure in new[] { "output", "power", "stack", "" })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == code);
            var s = Setup(recipe);
            var originals = s.Input.childItems.ToArray();
            if (bandage.Length != 0) originals[2].identifier = bandage;
            var preserved = code == "F53" ? originals[0] : originals[2];
            preserved.ModifyTag("preserved-fixture", tag => tag.SetInt(87));
            if (failure == "output") s.Output.Capacity = 0;
            if (failure == "power") PowerHelper.ThrowAfterDebit = true;
            if (failure == "stack") originals[2].SetUnitCount(2);
            bool produced = FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, recipe);
            check(produced == (failure.Length == 0), $"Care kit {code} ({bandage}) production result differs: {failure}");
            if (!produced)
            {
                check(s.Output.childItems.Count == 0 && originals.All(item => item.parentInventory == s.Input && !item.Destroyed) &&
                    s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100, "Failed care kit batch did not restore original objects and power: " + failure);
                continue;
            }
            var kit = s.Output.childItems.Single();
            kit.GetTagReadonly("owned").SetBool(true);
            var contents = (GameGridInventory)kit.contentWindow!.childElement!;
            check(kit.identifier == recipe.OutputItemId && contents.childItems.SequenceEqual(originals) && contents.IsRemoveLocked() && kit.unitValue == fee &&
                s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 90, "Care kit lost originals, charged twice, or has the wrong fee");
            var main = new GameGridInventory { Capacity = 2 };
            check(!FoodCareKit.TryUnpack(kit, main) && main.childItems.Count == 0 && kit.parentInventory == s.Output &&
                contents.childItems.Count == 3 && originals.All(item => item.parentInventory == contents), "Partial unpack lost or duplicated items");
            main.Capacity = 3;
            check(FoodCareKit.TryUnpack(kit, main) && kit.Destroyed && originals.All(item => item.parentInventory == main && !item.Destroyed) &&
                preserved.GetTagReadonly("preserved-fixture").GetInt() == 87, "Unpacking changed identity or item state");
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        // These exercise real recipe/production code. Boundary fixtures supply only
        // native inventory, power and quality APIs; UI is verified by native metadata.
        CheckIngredientStock(check);
        CheckStorageExpansion(check);
        CheckNewIngredients(check);
        CheckCareKit(check);
        check(new[] { 1, 2, 3 }.Select(stars => FoodDefinitions.Recipes.Count(recipe => recipe.Stars == stars))
            .SequenceEqual(new[] { 29, 22, 16 }), "Star distribution differs from the 67-recipe catalog");
        check(FoodDefinitions.Recipes.All(recipe => recipe.Requirements.Count <= 3 &&
            (recipe.Stars == 3 || recipe.ItemInputs.Count <= 2) &&
            recipe.ItemInputs.All(input => input.ResourceId != "bottle_hot_sauce")), "Ingredient limit or unavailable hot sauce regressed");
        foreach (var (code, minimumQuality) in new[] { ("F03", 0), ("F28", 50), ("B09", 100) })
        foreach (int quality in new[] { 0, 49, 50, 99, 100 })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == code);
            var s = Setup(recipe); bool eligible = quality >= minimumQuality;
            s.Machine.GetTagReadonly("machine_quality").SetInt(quality);
            s.Machine.GetTagReadonly(FoodProduction.SelectionTag).SetString(recipe.Id);
            var preview = FoodProduction.PreviewCycle(s.Machine);
            check(FoodInterop.View(recipe).IsAvailableFor(s.Machine) == eligible &&
                FoodProduction.HasIngredients(s.Machine, FoodInterop.View(recipe)) == eligible &&
                (FoodProduction.OutputPreview(s.Machine) != null) == eligible && preview.Batches == (eligible ? 1 : 0),
                "Quality gates disagree across logistics/selection/output/cycle: " + code + " / " + quality);
            check(s.Output.childItems.Count == 0 && s.Input.childItems.Count == recipe.ItemInputs.Count &&
                s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100, "Quality preview mutated resources");
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, recipe) == eligible,
                "Production ignored quality threshold: " + code + " / " + quality);
            if (!eligible) check(s.Input.childItems.Count == recipe.ItemInputs.Count &&
                s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100, "Locked recipe spent resources");
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F28"); var s = Setup(recipe);
            s.Machine.GetTagReadonly("machine_quality").SetInt(49);
            var forecast = FoodProduction.PreviewCycle(s.Machine);
            check(forecast.Outputs.Single().Recipe.OutputItemId == FoodDefinitions.NativeAnimalFatId,
                "Automatic preview did not skip locked beer stew and use eligible animal fat");
            s.Machine.GetTagReadonly(FoodProduction.SelectionTag).SetString(recipe.Id);
            check(FoodProduction.PreviewCycle(s.Machine).Batches == 0, "Locked selected recipe switched to animal fat");
        }
        foreach (int score in new[] { 4, 5, 6 })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "B09"); var s = Setup(recipe);
            s.Input.childItems[0].GetTagReadonly("WINE_SCORE_INT").SetInt(score);
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, recipe) == (score >= 5),
                "Fine-wine recipe ignored ingredient quality: " + score);
        }
        foreach (string code in new[] { "F15", "F16" })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == code); var s = Setup(recipe);
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, recipe), "Serum production failed");
            var serum = s.Output.childItems.Single(); long full = serum.unitValue;
            check(full == 100, "Full manufactured serum lost its native value floor");
            serum.GetTagReadonly("CURRENT_USE_COUNT_INT").SetInt(5);
            serum.state = JsonConvert.DeserializeObject<TagSystem>(JsonConvert.SerializeObject(serum.state))!;
            serum.unitValue = 999; FoodStateValue.RestoreBasis(serum);
            check(serum.unitValue == full / 2 && !serum.IsTag(FoodProduction.BasisTag), "Serum load restored full-bottle value");
            var target = DirectoryMaster.Item(recipe.OutputItemId); target.GetTagReadonly("CURRENT_USE_COUNT_INT").SetInt(8); target.unitValue = 80;
            var from = FoodStateValue.Read(serum); var to = FoodStateValue.Read(target);
            serum.GetTagReadonly("CURRENT_USE_COUNT_INT").SetInt(3); target.GetTagReadonly("CURRENT_USE_COUNT_INT").SetInt(10);
            FoodStateValue.Transfer(serum, target, 5, 8, from, to);
            check(serum.unitValue == (long)(full * 3m / 10) && target.unitValue == (long)(80 + full * 2m / 10),
                "Serum transfer failed to carry the transferred uses' manufactured value");
            serum.GetTagReadonly("CURRENT_USE_COUNT_INT").SetInt(0); FoodStateValue.RestoreBasis(serum);
            check(serum.unitValue == 0, "Empty manufactured serum retained full value");
        }
        {
            var feed = DirectoryMaster.Item(FoodDefinitions.IndustrialFishFeedId);
            feed.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(6300); FoodStateValue.SetBasis(feed, 60);
            check(feed.unitValue == 60, "Fractional per-kcal price lost a credit on a full bag");
            var half = DirectoryMaster.Item(FoodDefinitions.IndustrialFishFeedId);
            half.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(3150);
            FoodStateValue.Write(half, FoodStateValue.Read(feed));
            feed.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(3150); FoodStateValue.Refresh(feed);
            check(feed.unitValue + half.unitValue == 60, "Split feed did not conserve manufactured value");
            half.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(1575); FoodStateValue.Refresh(half);
            check(half.unitValue == 15, "Feed consumption restored its original value");
        }
        {
            var legacy = JObject.Parse(RecipeConfiguration.DefaultJson()); legacy["SchemaVersion"] = 7;
            foreach (var value in legacy["Recipes"]!) ((JObject)value).Remove("Stars");
            var stew = legacy["Recipes"]!.Single(value => value["Code"]!.Value<string>() == "F28");
            stew["Requirements"]![0]!["ResourceId"] = "small_meat_cut";
            stew["Requirements"]![1]!["ResourceId"] = "bottle_hot_sauce";
            stew["MarkupPercent"] = 35; stew["BaseOutputCount"] = 2;
            var migrated = RecipeConfiguration.Parse(legacy.ToString()).Recipes.Single(value => value.Code == "F28");
            check(migrated.BaseOutputCount == 2 && migrated.Stars == 2 && migrated.MarkupPercent == 50 &&
                migrated.ItemInputs.Select(input => input.ResourceId).SequenceEqual(new[] { "small_raw_meat", "red_beer" }),
                "Schema-7 migration retained hot sauce or lost customized output count");
            var invalid = JObject.Parse(RecipeConfiguration.DefaultJson());
            var fine = invalid["Recipes"]!.Single(value => value["Code"]!.Value<string>() == "B09");
            fine["Requirements"]![0]!["Conditions"] = new JArray();
            bool rejected = false; try { RecipeConfiguration.Parse(invalid.ToString()); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Configuration bypassed fine-wine eligibility");
        }
        foreach (int oldSchema in new[] { 1, 2, 3, 4, 5, 6, 22 })
        {
            var document = JObject.Parse(RecipeConfiguration.DefaultJson()); document["SchemaVersion"] = oldSchema;
            bool rejected = false; try { RecipeConfiguration.Parse(document.ToString()); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Unpublished mod retained an obsolete configuration importer: " + oldSchema);
        }
        check(new[] { "F36", "F37", "F38" }.All(code => FoodDefinitions.Recipes.Any(recipe => recipe.Code == code)),
            "Requested animal fat, instant ramen and yeast cheese recipes are missing");
        {
            var legacy = JObject.Parse(RecipeConfiguration.DefaultJson());
            legacy["SchemaVersion"] = 7;
            foreach (var recipe in legacy["Recipes"]!.Where(recipe => recipe!["Code"]!.Value<string>() is "F36" or "F37" or "F38" or "F39" or "F40" or "F41" || recipe!["Code"]!.Value<string>()![0] is 'A' or 'B' or 'C' or 'D' or 'E').ToArray())
                recipe.Remove();
            legacy["ProductionMarkups"]!["K04"] = 37;
            legacy["Recipes"]![0]!["Requirements"]![0]!["Amount"] = 4;
            var upgraded = RecipeConfiguration.Parse(legacy.ToString());
            check(upgraded.Recipes.Count == 67 && upgraded.ProductionMarkups["K04"] == 37 &&
                upgraded.Recipes.Single(recipe => recipe.Code == "F03").ItemInputs[0].Amount == 4 &&
                upgraded.Recipes.Single(recipe => recipe.Code == "F38").BaseOutputCount == 2,
                "Adding native ingredient recipes discarded the existing recipe configuration");
        }
        {
            var legacy = JObject.Parse(RecipeConfiguration.DefaultJson());
            legacy["SchemaVersion"] = 8;
            foreach (var recipe in legacy["Recipes"]!.Where(recipe => recipe["Code"]!.Value<string>() is "F39" or "F40" or "F41").ToArray())
                recipe.Remove();
            var juice = legacy["Recipes"]!.Single(recipe => recipe["Code"]!.Value<string>() == "F03");
            juice["BaseOutputCount"] = 3;
            juice["Requirements"]![1]!["Conditions"]![0]!["MinimumValue"] = 2;
            var upgraded = RecipeConfiguration.Parse(legacy.ToString());
            var migratedJuice = upgraded.Recipes.Single(recipe => recipe.Code == "F03");
            check(upgraded.SchemaVersion == RecipeConfiguration.SchemaVersion && migratedJuice.BaseOutputCount == 3 &&
                migratedJuice.Requirements[1].Conditions.Single().MinimumValue == 2 &&
                upgraded.Recipes.Any(recipe => recipe.Code == "F39" && recipe.OutputItemId == "soda_red") &&
                upgraded.Recipes.Any(recipe => recipe.Code == "F40" && recipe.OutputItemId == "energy_drink") &&
                upgraded.Recipes.Any(recipe => recipe.Code == "F41" && recipe.OutputItemId == "processed_meat"),
                "Schema-8 migration lost custom settings or a new native recipe");
        }
        {
            var legacy = JObject.Parse(RecipeConfiguration.DefaultJson());
            legacy["SchemaVersion"] = 9;
            var added = FoodExpansion.FarmAndPharmacyProducts.Select(product => product.Recipe.Code).ToHashSet();
            var kitCodes = new[] { "F54", "F55", "F56" };
            foreach (var recipe in legacy["Recipes"]!.Where(recipe =>
                added.Contains(recipe["Code"]!.Value<string>()!) || kitCodes.Contains(recipe["Code"]!.Value<string>()!)).ToArray())
                recipe.Remove();
            var f40 = legacy["Recipes"]!.Single(recipe => recipe["Code"]!.Value<string>() == "F40");
            f40["BaseOutputCount"] = 2;
            var migrated = RecipeConfiguration.Parse(legacy.ToString());
            check(legacy["Recipes"]!.Count() == 52 && migrated.Recipes.Count == 67 &&
                migrated.Recipes.Single(recipe => recipe.Code == "F40").BaseOutputCount == 2 &&
                migrated.Recipes.Count(recipe => added.Contains(recipe.Code)) == 12,
                "Schema-9 settings were lost or new recipes were not added");
        }
        {
            var legacy = JObject.Parse(RecipeConfiguration.DefaultJson());
            legacy["SchemaVersion"] = 10;
            var kitCodes = new[] { "F54", "F55", "F56" };
            foreach (var recipe in legacy["Recipes"]!.Where(recipe => kitCodes.Contains(recipe["Code"]!.Value<string>()!)).ToArray())
                recipe.Remove();
            legacy["Recipes"]!.Single(recipe => recipe["Code"]!.Value<string>() == "F03")["BaseOutputCount"] = 3;
            var migrated = RecipeConfiguration.Parse(legacy.ToString());
            check(migrated.Recipes.Count(recipe => kitCodes.Contains(recipe.Code)) == kitCodes.Length &&
                migrated.Recipes.Single(recipe => recipe.Code == "F03").BaseOutputCount == 3,
                "Schema-10 configuration lost custom output counts or did not get all medical kits");
        }
        {
            var legacy = JObject.Parse(RecipeConfiguration.DefaultJson());
            legacy["SchemaVersion"] = 11;
            var basic = legacy["Recipes"]!.Single(recipe => recipe["Code"]!.Value<string>() == "F54");
            basic["Requirements"] = new JArray(new[] { "kotton_fiber", "rubbing_alcohol", "any_bandage" }.Select(id =>
                JObject.FromObject(new RecipeRequirement(RecipeResourceKind.Item, id, 1, RecipeAmountBasis.PerBatch,
                    [new RecipeCondition(RecipeProperty.SingleItem, 1)]))));
            legacy["ProductionMarkups"]!["K04"] = 37;
            var migrated = RecipeConfiguration.Parse(legacy.ToString());
            var pool = migrated.Recipes.Single(recipe => recipe.Code == "F54").ItemInputs.Single();
            check(pool.ResourceId == FoodDefinitions.BasicMedicalPool && pool.Amount == 3 && migrated.ProductionMarkups["K04"] == 37,
                "Schema-11 fixed kit did not migrate to its pool or lost unrelated settings");
        }
        check(FoodDefinitions.Recipes.Count(recipe => recipe.MachineId == FoodDefinitions.FoodProcessorItem.Id) == 36 &&
            FoodDefinitions.Recipes.Count(recipe => recipe.MachineId == FoodDefinitions.InfusionTowerItem.Id) == 31, "Machine recipe split differs from the design");
        foreach (var recipe in FoodDefinitions.Recipes)
        {
            var s = Setup(recipe);
            int calories = s.Input.childItems.Sum(item => item.unitCount * item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt());
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, recipe), "Recipe cannot produce: " + recipe.Code);
            check(s.Output.childItems.Count == recipe.BaseOutputCount && s.Input.childItems.Count == 0 &&
                s.Output.childItems.All(item => item.identifier == recipe.OutputItemId), "Recipe consumed or produced the wrong items: " + recipe.Code);
            if (FoodDefinitions.IsFoodOutput(recipe.OutputItemId))
                check(s.Output.childItems.Sum(item => item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt()) == calories, "Recipe changed total calories: " + recipe.Code);
            if (recipe.OutputItemId == FoodDefinitions.IndustrialFishFeedId)
                check(IndustrialFishFeedHelper.Requests.Sum() == calories && s.Output.childItems.All(FoodStateValue.HasRate),
                    "Feed lost actual calories or remaining-value basis");
            check(s.Output.childItems.All(item => item.IsTag(FoodProduction.BasisTag) || FoodStateValue.HasRate(item)),
                "Produced item has no persistent value: " + recipe.Code);

        }
        // Cycle preview must agree with committed production across performance
        // thresholds, without spending resources merely to calculate the forecast.
        foreach (var sample in new (int Bonus, int Batches)[] { (-1, 1), (0, 1), (99, 1), (100, 2), (199, 2), (200, 3) })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F36");
            var s = Setup(recipe);
            s.Machine.GetTagReadonly("performance").SetInt(sample.Bonus);
            s.Input.childItems.Single().unitCount = 8;
            var preview = FoodProduction.PreviewCycle(s.Machine);
            check(preview.Capacity == sample.Batches && preview.Batches == sample.Batches &&
                preview.Outputs.Single().Quantity == sample.Batches && preview.Power == sample.Batches * 10 &&
                s.Input.childItems.Single().unitCount == 8 && s.Output.childItems.Count == 0 &&
                s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100,
                "Cycle preview crossed a performance threshold incorrectly or spent resources: " + sample.Bonus);
            for (int batch = 0; batch < sample.Batches; batch++)
                check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe),
                    "Forecast batch could not actually process: " + sample.Bonus);
            check(s.Output.childItems.Count == preview.Outputs.Single().Quantity &&
                s.Input.childItems.Single().unitCount == 8 - preview.Inputs.Single().Count,
                "Cycle forecast differs from committed input/output counts");
        }
        foreach (var limit in new[] { "ingredients", "water", "power", "off" })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F03");
            var s = Setup(recipe);
            s.Machine.GetTagReadonly("performance").SetInt(300);
            var fruit = s.Input.childItems.Single(); fruit.unitCount = limit == "ingredients" ? 7 : 12;
            s.Water.childItem!.GetTagReadonly("water").SetInt(limit == "water" ? 1250000 : 2000000);
            s.Battery.childItem!.GetTagReadonly("energy").SetInt(limit == "power" ? 25 : 100);
            if (limit == "off") s.Machine.GetTagReadonly("is_machinery_powered").SetBool(false);
            var preview = FoodProduction.PreviewCycle(s.Machine);
            int expected = limit == "off" ? 0 : 2;
            check(preview.Capacity == 4 && preview.Batches == expected &&
                preview.Outputs.Sum(output => output.Quantity) == expected && preview.WaterParts == expected * 500000 &&
                fruit.unitCount == (limit == "ingredients" ? 7 : 12) && s.Output.childItems.Count == 0,
                "Cycle forecast did not respect cumulative " + limit);
            int actual = 0;
            while (actual < 4 && FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, recipe)) actual++;
            check(actual == preview.Batches, "Forecast resource limit disagrees with production: " + limit);
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F36");
            var s = Setup(recipe);
            s.Machine.GetTagReadonly("performance").SetInt(200);
            var raw = new GameItem { identifier = "raw_meat", unitCount = 2, unitValue = 10 };
            raw.EnableTag(FoodDefinitions.RawMeatTag); raw.Types.Add("FOOD");
            raw.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(250);
            s.Input.UncheckedAccept(raw);
            var preview = FoodProduction.PreviewCycle(s.Machine);
            check(preview.Batches == 2 && preview.Outputs.Select(output => output.Recipe.OutputItemId)
                .SequenceEqual(new[] { FoodDefinitions.NativeAnimalFatId, FoodDefinitions.LargeJerky.Id }) &&
                preview.Outputs.All(output => output.Quantity == 1) && preview.Calories == 700 &&
                s.Input.childItems.Count == 2 && s.Output.childItems.Count == 0,
                "Automatic cycle forecast failed to switch recipes after reserving the first batch");
            for (int batch = 0; batch < 2; batch++)
                check(FoodProduction.OrderRecipes(s.Machine.identifier).Any(value =>
                    FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, value)), "Mixed forecast failed real processing");
            check(s.Output.childItems.Select(item => item.identifier).SequenceEqual(preview.Outputs.Select(output => output.Recipe.OutputItemId)),
                "Mixed forecast products disagree with real automatic order");
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F37");
            var s = Setup(recipe);
            s.Machine.GetTagReadonly("performance").SetInt(200);
            foreach (var item in s.Input.childItems) item.unitCount *= 3;
            var preview = FoodProduction.PreviewCycle(s.Machine);
            check(preview.Batches == 3 && preview.Outputs.Single().Recipe.Id == recipe.Id && preview.Outputs.Single().Quantity == 6,
                "Cycle forecast confused batches with multi-portion output quantity");
            for (int batch = 0; batch < 3; batch++)
                check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe), "Multi-portion forecast could not process");
            check(s.Output.childItems.Count == 6 && s.Input.childItems.Count == 0, "Multi-portion forecast disagrees with actual output");
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F03");
            var s = Setup(recipe);
            s.Machine.GetTagReadonly("performance").SetInt(200);
            var fruit = s.Input.childItems.Single(); fruit.unitCount = 1;
            var more = new GameItem { identifier = "nutrifruit", unitCount = 8, unitValue = 10 };
            more.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(200); s.Input.UncheckedAccept(more);
            s.Water.childItem!.GetTagReadonly("water").SetInt(1500000);
            var preview = FoodProduction.PreviewCycle(s.Machine);
            check(preview.Batches == 3 && preview.Inputs.Sum(take => take.Count) == 9 && preview.Calories == 1700 &&
                fruit.unitCount == 1 && more.unitCount == 8,
                "Cycle forecast double-counted an overlapping stack or its calories");
            const string owner = "checks.food.cycle_power";
            ContentRuntime.Attach(owner);
            try
            {
                int debits = 0;
                DevicePower.Register(owner, item => item == s.Battery.childItem ? 25 : null,
                    (item, cost) => { debits++; return true; });
                preview = FoodProduction.PreviewCycle(s.Machine);
                check(preview.Batches == 2 && preview.Power == 20 && debits == 0 &&
                    fruit.unitCount == 1 && more.unitCount == 8 && WaterHelper.GetTotalVolume(s.Water.childItem) == 1500000,
                    "Cycle forecast ignored shared energy limits or debited the provider");
            }
            finally { ContentRuntime.Detach(owner); }
        }
        foreach (var sample in new[]
        {
            (Name: "small meat auto", ItemId: "small_raw_meat", Selection: "", Expected: FoodDefinitions.NativeAnimalFatId),
            (Name: "standard meat auto", ItemId: "raw_meat", Selection: "", Expected: FoodDefinitions.LargeJerky.Id),
            (Name: "small meat selected jerky", ItemId: "small_raw_meat", Selection: "F12", Expected: FoodDefinitions.LargeJerky.Id)
        })
        {
            var s = Setup(FoodDefinitions.Recipes.Single(recipe => recipe.Code == "F36"));
            s.Input.childItems.Single().identifier = sample.ItemId;
            if (sample.Selection.Length != 0)
                s.Machine.GetTagReadonly(FoodProduction.SelectionTag).SetString(
                    FoodDefinitions.Recipes.Single(recipe => recipe.Code == sample.Selection).Id);
            var preview = FoodProduction.OutputPreview(s.Machine);
            check(preview?.OutputItemId == sample.Expected && preview.OutputCount == 1,
                "Output preview disagrees with automatic/selected meat recipe: " + sample.Name);
            check(s.Input.childItems.Single().unitCount == 2 && s.Output.childItems.Count == 0 &&
                s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100,
                "Output preview consumed ingredients/power or inserted a product: " + sample.Name);
            var produced = FoodProduction.OrderRecipes(s.Machine.identifier).FirstOrDefault(recipe =>
                FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe));
            check(produced != null && s.Output.childItems.Single().identifier == sample.Expected && s.Input.childItems.Count == 0,
                "Meat recipe selection produced the wrong item: " + sample.Name);
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F17");
            var s = Setup(recipe);
            var view = FoodInterop.View(recipe);
            s.Machine.GetTagReadonly("is_machinery_powered").SetBool(false);
            check(FoodProduction.HasIngredients(s.Machine, view) && FoodProduction.OutputPreview(s.Machine)?.Id == recipe.Id,
                "Ingredients filter incorrectly requires power");
            s.Water.childItem!.GetTagReadonly("quality").SetInt(1);
            check(!FoodProduction.HasIngredients(s.Machine, view) && FoodProduction.OutputPreview(s.Machine) == null,
                "Ingredients filter/auto preview ignored required water quality");
            s.Water.childItem.GetTagReadonly("quality").SetInt(2);
            s.Water.childItem.GetTagReadonly("water").SetInt(749000);
            check(!FoodProduction.HasIngredients(s.Machine, view), "Ingredients filter ignored water quantity");
            s.Machine.GetTagReadonly(FoodProduction.SelectionTag).SetString(recipe.Id);
            check(FoodProduction.OutputPreview(s.Machine)?.Id == recipe.Id,
                "Selected output disappeared while waiting for water");
            var different = FoodDefinitions.Recipes.Single(value => value.Code == "F03");
            s.Machine.GetTagReadonly(FoodProduction.SelectionTag).SetString(different.Id);
            check(FoodProduction.PreviewForSelection(s.Machine, view) == FoodText.Get("food.not_enough_water_or_water_quality_is") &&
                s.Machine.GetTagReadonly(FoodProduction.SelectionTag).GetString() == different.Id &&
                s.Input.childItems.Single().unitCount == 1 && s.Output.childItems.Count == 0 &&
                WaterHelper.GetTotalVolume(s.Water.childItem) == 749000,
                "Browsing an alternative recipe changed selection/resources or hid its actual requirements");
        }
        {
            const string owner = "checks.food.preview";
            ContentRuntime.Attach(owner);
            try
            {
                int rolls = 0;
                foreach (int priority in new[] { 1, 20 })
                    ExtraRecipes.Register(owner, new ExtraRecipe("checks.preview." + priority, FoodDefinitions.FoodProcessorItem.Id,
                        new[] { new IngredientView("nutrifruit", 1, item => item.identifier is "nutrifruit" or "small_raw_meat", "", "") },
                        "processed_cheese", "奶酪", "Cheese", () => true, () => { rolls++; return "processed_cheese"; })
                    { Priority = priority });
                var s = Setup(FoodDefinitions.Recipes.Single(value => value.Code == "F36"));
                s.Input.childItems.Single().identifier = "nutrifruit";
                check(FoodProduction.OutputPreview(s.Machine)?.Id == "checks.preview.20" && rolls == 0 &&
                    s.Input.childItems.Single().unitCount == 2 && s.Output.childItems.Count == 0,
                    "Extension preview ignored priority, rolled a random result, or produced items");
                s.Input.childItems.Single().identifier = "small_raw_meat";
                check(FoodProduction.OutputPreview(s.Machine)?.OutputItemId == FoodDefinitions.NativeAnimalFatId,
                    "Extension priority displaced a matching built-in recipe");
            }
            finally { ContentRuntime.Detach(owner); }
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F36"); var s = Setup(recipe);
            check(recipe.MachineId == FoodDefinitions.FoodProcessorItem.Id && recipe.OutputItemId == "fat_meat" &&
                recipe.BaseOutputCount == 1 && recipe.ItemInputs.Count == 1 &&
                recipe.ItemInputs[0].ResourceId == "small_raw_meat" && recipe.ItemInputs[0].Amount == 2,
                "Animal fat does not use two small raw meat cuts");
            var meat = s.Input.childItems.Single(); meat.unitValue = 65;
            meat.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(550);
            meat.GetTagReadonly("MEAT_TYPE_TAG").SetString("ANIMAL_RAT"); meat.EnableTag("MEAT_FOOD_TAG");
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Output.childItems.Single().identifier == "fat_meat" &&
                s.Output.childItems[0].GetTagReadonly("CALORIE_VALUE_TAG").GetInt() == 1100 &&
                s.Output.childItems[0].unitValue == 173 && s.Output.childItems[0].IsTag("MEAT_FOOD_TAG") &&
                s.Output.childItems[0].GetTagReadonly("MEAT_TYPE_TAG").GetString() == "ANIMAL_RAT",
                "Manufactured animal fat lost actual nutrition, value or meat provenance");
        }
        foreach (var (fruitValue, expected) in new[] { (8, 20L), (10, 22L) })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F37"); var s = Setup(recipe);
            check(recipe.MachineId == FoodDefinitions.FoodProcessorItem.Id && recipe.OutputItemId == "cup_noodle" &&
                recipe.BaseOutputCount == 2 && recipe.ItemInputs.Select(rule => (rule.ResourceId, rule.Amount))
                    .SequenceEqual(new[] { ("nutrifruit", 2), ("edible_mushroom", 1) }),
                "Instant ramen does not use two Nutrifruits and one edible mushroom");
            var fruit = s.Input.childItems.Single(item => item.identifier == "nutrifruit"); fruit.unitValue = fruitValue;
            fruit.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(300);
            var mushroom = s.Input.childItems.Single(item => item.identifier == "dream_cap"); mushroom.unitValue = 7;
            mushroom.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(101);
            check(FoodProduction.Preview(s.Machine, recipe).EndsWith((expected * 2).ToString(), StringComparison.Ordinal) &&
                FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Output.childItems.All(item => item.identifier == "cup_noodle" && item.unitValue == expected) &&
                s.Output.childItems.Select(item => item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt()).SequenceEqual(new[] { 351, 350 }),
                "Manufactured instant ramen lost its per-item native value floor or actual calorie split");
        }
        {
            DirectoryMaster.Known.UnionWith(new[] { "nutrifruit", "meal_cap" });
            var ramen = FoodDefinitions.Recipes.Single(value => value.Code == "F37");
            var driedFruit = FoodDefinitions.Recipes.Single(value => value.Code == "F42");
            var ramenQuote = FoodRecipeValue.Reference(FoodInterop.View(ramen));
            var fruitQuote = FoodRecipeValue.Reference(FoodInterop.View(driedFruit));
            check(ramenQuote is { First: 32, Last: 32, Total: 64 } && fruitQuote is { First: 40, Last: 40, Total: 40 },
                "Handbook reference prices lost the eligible ingredients or per-unit and batch quantities");
            var s = Setup(driedFruit);
            s.Input.childItems.Single().unitValue = 1;
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, driedFruit) &&
                s.Output.childItems.Single().unitValue == 13, "Native floor was incorrectly applied to modded dried fruit");
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F38"); var s = Setup(recipe);
            check(recipe.MachineId == FoodDefinitions.InfusionTowerItem.Id && recipe.OutputItemId == "processed_cheese" &&
                recipe.BaseOutputCount == 2 && recipe.ItemInputs.Select(rule => (rule.ResourceId, rule.Amount))
                    .SequenceEqual(new[] { ("processed_milk", 1), ("wine_yeast", 1) }),
                "Yeast cheese does not use one Pilk and one regular yeast for two cheese portions");
            var milk = s.Input.childItems.Single(item => item.identifier == "processed_milk"); milk.unitValue = 16;
            milk.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(600);
            var yeast = s.Input.childItems.Single(item => item.identifier == "wine_yeast"); yeast.unitValue = 10;
            yeast.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(0);
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Output.childItems.All(item => item.identifier == "processed_cheese" && item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt() == 300) &&
                s.Output.childItems.Select(item => item.unitValue).SequenceEqual(new long[] { 22, 21 }),
                "Yeast cheese created factory calories or rounded each output value up");
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F24");
            var s = Setup(recipe);
            s.Input.childItems.Single(item => item.identifier == "eel_fillet")
                .GetTagReadonly("MEAT_TYPE_TAG").SetString("FISH_EEL");
            s.Input.childItems.Single(item => item.identifier == "caviar")
                .GetTagReadonly("MEAT_TYPE_TAG").SetString("FISH_STURGEON");
            string preview = FoodProduction.Preview(s.Machine, recipe);
            bool processed = FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe);
            var platter = s.Output.childItems.SingleOrDefault();
            check(preview.Contains("caviar", StringComparison.Ordinal) && processed && platter != null &&
                platter.identifier == recipe.OutputItemId && platter.GetTagReadonly("MEAT_TYPE_TAG").GetString() == "FISH_EEL",
                "Eel platter rejected harvested caviar or lost its eel provenance: " + preview);
        }
        var floss = FoodDefinitions.Recipes.Single(recipe => recipe.Code == "F18");
        foreach (var size in new[] { ("tiny_fish_fillet", 100), ("small_fish_fillet", 200), ("fish_fillet", 400), ("large_fish_fillet", 900) })
        {
            var s = Setup(floss); s.Input.childItems[0].identifier = size.Item1;
            s.Input.childItems[0].GetTagReadonly("CALORIE_VALUE_TAG").SetInt(size.Item2);
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, floss) &&
                s.Output.childItems[0].GetTagReadonly("CALORIE_VALUE_TAG").GetInt() == size.Item2 * 2, "Fish size alias lost nutrition: " + size.Item1);
        }
        foreach (var sample in new[]
        {
            (Code: "F18", SecondId: "small_fish_fillet", FirstCalories: 400, SecondCalories: 200, ExpectedCalories: 600),
            (Code: "F25", SecondId: "fish_scrap", FirstCalories: 40, SecondCalories: 50, ExpectedCalories: 240),
            (Code: "F34", SecondId: "fish_scrap", FirstCalories: 40, SecondCalories: 50, ExpectedCalories: 240)
        })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == sample.Code);
            var s = Setup(recipe);
            var first = s.Input.childItems[0];
            var second = new GameItem { identifier = sample.SecondId, unitCount = first.unitCount - 1, unitValue = 10 };
            first.SetUnitCount(1);
            first.GetTagReadonly("MEAT_TYPE_TAG").SetString("FISH_TILAPIA");
            second.GetTagReadonly("MEAT_TYPE_TAG").SetString("FISH_STURGEON");
            first.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(sample.FirstCalories);
            second.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(sample.SecondCalories);
            s.Input.UncheckedAccept(second);
            string preview = FoodProduction.Preview(s.Machine, recipe);
            bool processed = FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe);
            var product = s.Output.childItems.SingleOrDefault();
            check(preview.Contains(sample.ExpectedCalories + " kcal", StringComparison.Ordinal) && processed && product != null &&
                product.identifier == recipe.OutputItemId && !product.IsTag("MEAT_TYPE_TAG") && s.Input.childItems.Count == 0 &&
                product.GetTagReadonly("CALORIE_VALUE_TAG").GetInt() == sample.ExpectedCalories,
                "Mixed fish batch was rejected or lost its input nutrition: " + sample.Code + " / " + preview);
        }
        foreach (string failure in new[] { "medicinal", "contraband", "rotten" })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F27"); var s = Setup(recipe);
            var mushroom = s.Input.childItems[0];
            if (failure == "medicinal") mushroom.Types.Remove("FOOD");
            if (failure == "contraband") mushroom.EnableTag("CONTRABAND_ITEM_TAG");
            if (failure == "rotten") { mushroom.EnableTag("FRESH_FOOD_TAG"); mushroom.GetTagReadonly("FRESHNESS_TAG").SetInt(0); }
            check(!FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Input.childItems.Count == 2 && s.Output.childItems.Count == 0 && s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100,
                "Invalid edible-mushroom variant consumed resources: " + failure);
        }
        foreach (var sample in new[] { (Code: "F12", Component: "caffeine"), (Code: "F27", Component: "dream_cap_mush"),
            (Code: "F27", Component: "caffeine") })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == sample.Code); var s = Setup(recipe);
            var ingredient = s.Input.childItems[0];
            ingredient.GetTagReadonly("part." + sample.Component).SetInt(100);
            check(FoodProduction.AcceptsIngredient(ingredient) && FoodInterop.View(recipe).Ingredients[0].Matches(ingredient) &&
                FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Input.childItems.Count == 0 && s.Output.childItems.Single().identifier == recipe.OutputItemId,
                "Additional chemical components blocked an otherwise eligible ingredient: " + sample.Code + " / " + sample.Component);
        }
        foreach (string code in new[] { "F12", "F28", "F36" })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == code); var s = Setup(recipe);
            s.Input.childItems[0].EnableTag("ANIMAL_PRION_TAG");
            check(!FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Output.childItems.Count == 0 && s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100, "Prion meat was processed: " + code);
        }
        foreach (string code in new[] { "F12", "F28", "F36" })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == code); var s = Setup(recipe);
            var first = s.Input.childItems[0];
            first.SetUnitCount(1);
            first.GetTagReadonly("MEAT_TYPE_TAG").SetString("ANIMAL_RAT");
            first.EnableTag("MEAT_FOOD_TAG");
            var second = new GameItem { identifier = first.identifier, unitValue = 10 };
            second.EnableTag(FoodDefinitions.RawMeatTag);
            second.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(50);
            second.GetTagReadonly("MEAT_TYPE_TAG").SetString("ANIMAL_HUMAN");
            s.Input.UncheckedAccept(second);
            int calories = s.Input.childItems.Sum(item => item.unitCount * item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt());
            check(FoodProduction.Preview(s.Machine, recipe).Contains("本批：", StringComparison.Ordinal) &&
                FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Input.childItems.Count == 0 && s.Output.childItems.Single().identifier == recipe.OutputItemId &&
                !s.Output.childItems[0].IsTag("MEAT_TYPE_TAG") && s.Output.childItems[0].IsTag("MEAT_FOOD_TAG") &&
                s.Output.childItems[0].GetTagReadonly("CALORIE_VALUE_TAG").GetInt() == calories,
                "Mixed land meat was rejected, lost nutrition or claimed a single species: " + code);
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F28"); var s = Setup(recipe);
            s.Input.childItems[0].GetTagReadonly("MEAT_TYPE_TAG").SetString("ANIMAL_RAT");
            s.Input.childItems[0].EnableTag("MEAT_FOOD_TAG");
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Output.childItems[0].GetTagReadonly("MEAT_TYPE_TAG").GetString() == "ANIMAL_RAT" && s.Output.childItems[0].IsTag("MEAT_FOOD_TAG"),
                "Meat processing erased its native species");
        }
        foreach (string baseId in new[] { "wine_bottle" })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F32"); var s = Setup(recipe);
            var item = s.Input.childItems[0]; item.identifier = baseId;
            int alcohol = baseId == "wine_bottle" ? 9 : baseId == "nudka" ? 4 : 1;
            item.GetTagReadonly("ALCOHOL_VALUE").SetInt(alcohol);
            if (baseId == "wine_bottle")
            {
                item.EnableTag("WINE_CONTAINER_TAG");
                item.GetTagReadonly("WINE_CONTAINER_STATE").SetInt(2);
                item.Types.Add("ALCOHOL");
            }
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Output.childItems[0].GetTagReadonly("ALCOHOL_VALUE").GetInt() == alcohol,
                "Cocktail rejected an eligible alcohol base or reset its strength: " + baseId);
        }
        foreach (var sample in new[]
        {
            (Name: "empty bottle", State: 0, Drinkable: false, Counterfeit: false),
            (Name: "fermenting wine", State: 1, Drinkable: false, Counterfeit: false),
            (Name: "failed wine", State: 2, Drinkable: false, Counterfeit: false),
            (Name: "counterfeit wine", State: 2, Drinkable: true, Counterfeit: true)
        })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F32"); var s = Setup(recipe);
            var item = s.Input.childItems[0]; item.identifier = "wine_bottle";
            item.EnableTag("WINE_CONTAINER_TAG");
            item.GetTagReadonly("WINE_CONTAINER_STATE").SetInt(sample.State);
            item.Types.Remove("ALCOHOL");
            if (sample.Drinkable) item.Types.Add("ALCOHOL");
            if (sample.Counterfeit) item.EnableTag("WINE_COUNTERFEIT_TAG");
            check(FoodProduction.Preview(s.Machine, recipe).Contains("已发酵完成", StringComparison.Ordinal) &&
                !FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                item.parentInventory == s.Input && s.Input.childItems.Count == 2 && s.Output.childItems.Count == 0 &&
                s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100,
                "Cocktail accepted an ineligible wine or consumed resources: " + sample.Name);
        }
        foreach (var sample in new[]
        {
            (Name: "split same base", Required: 2, FirstCount: 1, FirstStrength: 1, SecondId: "wine_bottle", SecondCount: 1, SecondStrength: 1, Expected: 1),
            (Name: "split different strengths", Required: 2, FirstCount: 1, FirstStrength: 1, SecondId: "wine_bottle", SecondCount: 1, SecondStrength: 4, Expected: 2),
            (Name: "mixed base types", Required: 2, FirstCount: 1, FirstStrength: 1, SecondId: "wine_bottle", SecondCount: 1, SecondStrength: 4, Expected: 2),
            (Name: "partially consumed stack", Required: 3, FirstCount: 2, FirstStrength: 1, SecondId: "wine_bottle", SecondCount: 5, SecondStrength: 4, Expected: 2),
            (Name: "mixed homebrew", Required: 2, FirstCount: 1, FirstStrength: 1, SecondId: "wine_bottle", SecondCount: 1, SecondStrength: 9, Expected: 5)
        })
        {
            var document = JObject.Parse(RecipeConfiguration.DefaultJson());
            var configuredRecipe = document["Recipes"]!.Single(value => value!["Code"]!.Value<string>() == "F32")!;
            configuredRecipe["Requirements"]!.Single(value => value!["ResourceId"]!.Value<string>() == "wine_bottle")!["Amount"] = sample.Required;
            var recipe = RecipeConfiguration.Parse(document.ToString()).Recipes.Single(value => value.Code == "F32");
            var s = Setup(recipe);
            var first = s.Input.childItems[0]; first.unitCount = sample.FirstCount;
            first.GetTagReadonly("ALCOHOL_VALUE").SetInt(sample.FirstStrength);
            var second = new GameItem { identifier = sample.SecondId, unitCount = sample.SecondCount };
            second.GetTagReadonly("ALCOHOL_VALUE").SetInt(sample.SecondStrength);
            if (sample.SecondId == "wine_bottle")
            {
                second.EnableTag("WINE_CONTAINER_TAG");
                second.GetTagReadonly("WINE_CONTAINER_STATE").SetInt(2);
                second.Types.Add("ALCOHOL");
            }
            s.Input.UncheckedAccept(second);
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Output.childItems.Single().GetTagReadonly("ALCOHOL_VALUE").GetInt() == sample.Expected,
                "Configured cocktail did not average actually consumed alcohol units: " + sample.Name);
            int remaining = sample.FirstCount + sample.SecondCount - sample.Required;
            check(first.Destroyed && (remaining == 0 ? second.Destroyed && s.Input.childItems.Count == 0 :
                !second.Destroyed && second.parentInventory == s.Input && second.unitCount == remaining),
                "Configured cocktail consumed an entire partial stack: " + sample.Name);
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F30"); var s = Setup(recipe);
            int[] calories = { 150, 225, 450 };
            for (int i = 0; i < 3; i++) s.Input.childItems[i].GetTagReadonly("CALORIE_VALUE_TAG").SetInt(calories[i]);
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Output.childItems.Select(item => item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt()).SequenceEqual(new[] { 413, 412 }), "Odd calories were lost or duplicated");
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F31"); var s = Setup(recipe);
            long[] values = { 18, 17 }; int[] calories = { 450, 250 };
            for (int i = 0; i < 2; i++) { s.Input.childItems[i].unitValue = values[i]; s.Input.childItems[i].GetTagReadonly("CALORIE_VALUE_TAG").SetInt(calories[i]); }
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Output.childItems.Select(item => item.unitValue).SequenceEqual(new long[] { 32, 31 }) &&
                s.Output.childItems.All(item => item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt() == 350), "Multi-output batch rounded up its value per portion");
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F06"); var s = Setup(recipe);
            var coffee = s.Input.childItems[0]; coffee.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(0);
            coffee.GetTagReadonly("part.caffeine").SetInt(1000);
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, recipe) &&
                s.Output.childItems[0].GetTagReadonly("CALORIE_VALUE_TAG").GetInt() == 0, "Normal coffee caffeine was rejected or invented calories");
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F03"); var s = Setup(recipe);
            s.Machine.GetTagReadonly(FoodProduction.SelectionTag).SetString("different.recipe");
            check(!FoodInterop.View(recipe).IsAvailableFor(s.Machine) &&
                !FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, recipe) &&
                s.Input.childItems.Count == 1 && s.Output.childItems.Count == 0, "Selected recipe allowed a different production or logistics view");
            var view = FoodInterop.View(recipe); var decoded = ForgeWire.Recipe(ForgeWire.Recipe(view));
            check(decoded.Preview != null && decoded.Preview(s.Machine) == FoodProduction.Preview(s.Machine, recipe), "Forge transport lost the machine preview delegate");
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F33"); var s = Setup(recipe);
            s.Input.childItems[0].unitValue = 16;
            s.Input.childItems[0].GetTagReadonly("CALORIE_VALUE_TAG").SetInt(600);
            s.Input.childItems[1].unitValue = 40;
            s.Input.childItems[1].EnableTag("MEAT_FOOD_TAG");
            s.Input.childItems[1].GetTagReadonly("CALORIE_VALUE_TAG").SetInt(1250);
            check(FoodProduction.Preview(s.Machine, recipe).Contains("2450 kcal"), "Cheese preview omits the actual batch nutrition");
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe), "Native cheese route failed");
            check(s.Output.childItems.Select(item => item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt())
                .SequenceEqual(new[] { 613, 613, 612, 612 }) && s.Output.childItems.All(item => item.unitValue == 25),
                "Cheese reset to native reference nutrition or lost the batch value");
            check(s.Output.childItems.All(item => item.identifier == "processed_cheese" && item.IsGameItemType("FOOD") && !item.IsTag("MEAT_FOOD_TAG")),
                "Recombined cheese lost native food / rat-bait identity");
            var cheese = s.Output.childItems[0];
            cheese.state = JsonConvert.DeserializeObject<TagSystem>(JsonConvert.SerializeObject(cheese.state))!;
            cheese.unitValue = 999; FoodStateValue.RestoreBasis(cheese);
            check(cheese.unitValue == 25 && cheese.GetTagReadonly("CALORIE_VALUE_TAG").GetInt() == 613,
                "Native cheese did not preserve its manufactured basis and calories");
        }
        foreach (int scrapKcal in new[] { 100, 2000 })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F34"); var s = Setup(recipe);
            s.Input.childItems[0].GetTagReadonly("CALORIE_VALUE_TAG").SetInt(scrapKcal);
            s.Input.childItems[1].GetTagReadonly("CALORIE_VALUE_TAG").SetInt(300);
            int expected = scrapKcal == 100 ? 600 : 6300;
            check(FoodProduction.Preview(s.Machine, recipe).Contains(expected + " kcal") &&
                FoodProduction.Preview(s.Machine, recipe).EndsWith(IndustrialFishFeedHelper.ReturnedValue.ToString(), StringComparison.Ordinal),
                "Feed preview did not use the value returned by the native setter");
            IndustrialFishFeedHelper.Requests.Clear();
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                IndustrialFishFeedHelper.Requests.SequenceEqual(new[] { expected }), "Feed used the default 5000 kcal or clamped a large bag");
            var feed = s.Output.childItems.Single();
            check(feed.unitValue == IndustrialFishFeedHelper.ReturnedValue && FoodStateValue.HasRate(feed) && !feed.IsTag(FoodProduction.BasisTag),
                "Feed did not receive its manufactured value");
            feed.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(expected / 2); feed.unitValue = 999; FoodStateValue.RestoreBasis(feed);
            check(feed.unitValue == IndustrialFishFeedHelper.ReturnedValue / 2, "Remaining feed regained its full manufactured value");
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F34") with { BaseOutputCount = 2 };
            var s = Setup(recipe);
            s.Input.childItems[0].GetTagReadonly("CALORIE_VALUE_TAG").SetInt(167);
            s.Input.childItems[1].GetTagReadonly("CALORIE_VALUE_TAG").SetInt(500);
            check(FoodProduction.Preview(s.Machine, recipe).EndsWith((2 * IndustrialFishFeedHelper.ReturnedValue).ToString(), StringComparison.Ordinal) &&
                IndustrialFishFeedHelper.Requests.SequenceEqual(new[] { 501, 500 }),
                "Feed preview did not value each portion at its own calories");
            IndustrialFishFeedHelper.Requests.Clear();
            check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                IndustrialFishFeedHelper.Requests.SequenceEqual(new[] { 501, 500 }), "Configured feed portions lost kcal or used whole-batch rounding for native prices");
        }
        foreach (string code in new[] { "F33", "F34" })
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == code); var s = Setup(recipe);
            s.Output.Capacity = code == "F33" ? 2 : 0;
            check(!FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                s.Input.childItems.Count == 2 && s.Output.childItems.Count == 0 && s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100,
                "Native product route did not roll back an incomplete output: " + code);
        }
        {
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F34"); var s = Setup(recipe);
            foreach (var item in s.Input.childItems) item.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(0);
            check(!FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe) &&
                IndustrialFishFeedHelper.Requests.Count == 0 && s.Output.childItems.Count == 0 &&
                s.Input.childItems.Count == 2 && s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100,
                "Zero-calorie feed invoked the destructive native setter or consumed inputs");
        }
        foreach (var frozen in new[] { FoodDefinitions.GalaxySundae, FoodDefinitions.SupplyJelly, FoodDefinitions.RoeFiberPatty })
            check(FoodRefrigeration.Freezes(new() { identifier = frozen.Id }) &&
                !FoodRefrigeration.Chills(new() { identifier = frozen.Id }) &&
                !Nicokobo.FoodProcessing.Domain.FoodProcessingRules.ItemTypes(frozen).Contains("BREVAGE"),
                "Frozen food could stack native chilled and frozen bonuses: " + frozen.Code);
        check(FoodRefrigeration.Chills(new() { identifier = FoodDefinitions.FishFiberSalad.Id }) &&
            !FoodRefrigeration.Freezes(new() { identifier = FoodDefinitions.FishFiberSalad.Id }), "Fish salad should only gain the chilled effect");
        foreach (var drink in FoodDefinitions.Items.Where(item => item.Kind == ItemKind.Drink))
            check(!FoodRefrigeration.Freezes(new() { identifier = drink.Id }), "Added frozen qualification to an already-chillable drink");
    }
}
