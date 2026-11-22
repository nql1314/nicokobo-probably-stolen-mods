using System.Reflection;
using MechcoreProtocol;
using MechcoreProtocol.SynthesisExpansion;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;

internal static class DeviceChecks
{
    private static GameItem Owned(string id, long value = 100)
    { var item = new GameItem { identifier = id, unitValue = value }; GeneralHelper.SetItemOwned(item, true); return item; }
    private static GameItem Cell(GameSlotInventory slot, int energy, string id = "battery")
    {
        var item = Owned(id); item.EnableTag("power_source_item"); item.GetTagReadonly("energy").SetInt(energy);
        slot.UncheckedAccept(item); return item;
    }
    private static GameItem Robot(int energy = 100)
    {
        var robot = SynthesisRobots.Create(); GeneralHelper.SetItemOwned(robot, true);
        Cell(SynthesisRobots.Battery(robot), energy); return robot;
    }
    private static object? Hook(Type type, string name, params object?[] arguments) =>
        type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments);
    private static void Reset()
    {
        PlayerStore.Current = new(); EmporiumEntry.Instance = new(); StoreStation.Day = 8;
        DirectoryMaster.Factory = null; DirectoryMaster.FailCleanup = false; DirectoryMaster.Created.Clear();
        PowerHelper.RefuseDraw = PowerHelper.ThrowAfterDebit = PowerHelper.RefuseRestore = false; PowerHelper.DrawCalls = 0;
        CurrentConverterHelper.isNightCycle = false;
        UnityEngine.Random.Rolls.Clear(); ScavHelper.Calls = 0;
        ScavHelper.Loot = () => new() { new GameItem { identifier = "scrap_metal" } };
        ManufacturedEffectsRuntime.Reset(); CyberneticTrading.Reset(); SynthesisRobots.Reset(); SynthesisCyberEye.Reset(); SynthesisPower.Reset();
    }
    internal static void Run(Action<bool, string> check)
    {
        Reset();
        foreach (var definition in SynthesisDefinitions.Machines)
        {
            var machine = SynthesisMachines.Create(definition);
            var bay = MachineHelper.GetModuleInv(machine);
            check(bay.inventoryShape.Data == MachineHelper.DEFAULT_MODULE_BAY_CONFIG &&
                machine.IsTag("STANDARD_MACHINE_TAG") && machine.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt() == 0,
                $"New {definition.Code} module bay must start at the native unexpanded layout");
        }
        var terminal = SynthesisMachines.Create(SynthesisDefinitions.Machine(SynthesisDefinitions.UniversalManufacturerItem.Id));
        GeneralHelper.SetItemOwned(terminal, true);
        var terminalInput = (GameGridInventory)terminal.Storages[2]!;
        var terminalOutput = (GameGridInventory)terminal.Storages[3]!;
        var terminalBattery = (GameSlotInventory)terminal.Storages[0]!;
        var terminalCell = Cell(terminalBattery, 1000);
        var aiRecipe = SynthesisDefinitions.Recipes.Single(recipe => recipe.Code == "M01");
        foreach (var rule in aiRecipe.ItemInputs)
        { var item = Owned(rule.ResourceId); item.unitCount = rule.Amount * 2; terminalInput.UncheckedAccept(item); }
        terminal.GetTagReadonly("performance").SetInt(900);
        terminal.onCycleEndSlotItemFunc!(terminal, terminalInput, new());
        check(terminalOutput.childItems.Count == 1 && terminalOutput.childItems[0].identifier == aiRecipe.OutputItemId &&
            terminal.ActionCalls == 1 && terminalCell.GetTagReadonly("energy").GetInt() == 975,
            $"Terminal single-batch result: outputs={string.Join(",", terminalOutput.childItems.Select(item => item.identifier))}, actions={terminal.ActionCalls}, energy={terminalCell.GetTagReadonly("energy").GetInt()}");
        check(terminalInput.childItems.All(item => !item.Destroyed && item.unitCount > 0), "Terminal consumed two batches");

        // Full M09 transaction, including scanner level and a nonrectangular donor mask.
        Reset();
        var robotRecipe = SynthesisDefinitions.Recipes.Single(recipe => recipe.Code == "M09");
        var input = new GameGridInventory(); var output = new GameGridInventory();
        var battery = new GameSlotInventory(); var cell = Cell(battery, 100);
        terminal = SynthesisMachines.Create(SynthesisDefinitions.Machine(robotRecipe.MachineId));
        var scanner = Owned("metal_scanner"); scanner.ModifyTag(ScavengingRobotRules.ScannerChanceTag, tag => tag.Enable().SetInt(85));
        var storage = Owned("storage_bay"); storage.SetGameItemType("STORAGE");
        var donorGrid = new GameGridInventory(3, 2); donorGrid.SetShape("001000", 3);
        storage.SetContentWindow(new(new MachineryPanel(donorGrid))); storage.EnableTag("CONTAINER_TAG");
        var donor = SynthesisInterop.View(robotRecipe).Ingredients.Single(rule => rule.ItemId == "CONTAINER_TAG");
        foreach (var (id, allowed) in new[] { ("storage_bay_large", true), ("storage_bay_gun", false), ("hypertech_bay", false) })
        {
            storage.identifier = id;
            check(donor.Matches(storage) == allowed, $"M09 matched the wrong donor type: {id}");
        }
        storage.identifier = "storage_bay";
        check(donor.LocalizedLabel!().Contains("Storage Bay / Storage Bay (Extended)", StringComparison.Ordinal) &&
            donor.Chinese.Contains("储藏区 / 储藏区（扩建）", StringComparison.Ordinal),
            "M09 must list both storage bay types in the recipe query");
        var coreRule = robotRecipe.ItemInputs.Single(rule => rule.ResourceId == SynthesisDefinitions.MechanicalCore.Id);
        var core = Owned(coreRule.ResourceId); core.unitCount = coreRule.Amount;
        input.UncheckedAccept(core); input.UncheckedAccept(scanner); input.UncheckedAccept(storage);
        DirectoryMaster.Factory = id => id == SynthesisDefinitions.ScavengingRobot.Id ? SynthesisRobots.Create() : null;
        check(SynthesisProduction.TryProcess(terminal, battery, input, output, robotRecipe), "M09 manufacture failed");
        var robot = output.childItems.Single(); var cargo = SynthesisRobots.Cargo(robot);
        check(robot.GetTagReadonly(ScavengingRobotRules.ScannerChanceTag).GetInt() == 85 && cargo.inventoryShape.Data == "001000" &&
            cargo.inventoryShape.width == 3 && input.childItems.Count == 0 && scanner.Destroyed && storage.Destroyed,
            "M09 discarded the actual scanner or cargo shape");
        check(cell.GetTagReadonly("energy").GetInt() == 75, "M09 charged power more than once");
        var saved = robot.state.Clone(); var reloaded = SynthesisRobots.Create(); reloaded.state = saved;
        SynthesisRobots.RestoreStorage(reloaded);
        var savedCargo = SynthesisRobots.Cargo(reloaded); savedCargo.UncheckedAccept(Owned("saved_loot"));
        SynthesisRobots.RestoreStorage(reloaded);
        check(savedCargo.inventoryShape.Data == "001000" && savedCargo.childItems.Count == 1, "Restoring robot cargo reshaped populated storage");
        var invalidStorage = Owned("liquid"); invalidStorage.SetGameItemType("STORAGE");
        invalidStorage.EnableTag("CONTAINER_TAG"); invalidStorage.EnableTag("LIQUID_CONTAINER_TAG");
        check(!StorageShape.TryCapture(invalidStorage, out _, out _), "Liquid container accepted as robot cargo donor");
        check(!ScavengingRobotRules.IsValidStorageShape(3, "111111") && !ScavengingRobotRules.IsValidStorageShape(3, "0002"),
            "Invalid cargo masks accepted");
        var containerRule = robotRecipe.ItemInputs.Single(rule => rule.Kind == RecipeResourceKind.TaggedItem);
        check(!SynthesisIngredients.Matches(reloaded, containerRule), "Populated/multipanel container accepted as an empty donor");

        // One payment per night, retries after known rejection, and safe handling of uncertain debit.
        foreach (string mode in new[] { "normal", "full", "refused", "restored", "faulted", "breaks", "junk" })
        {
            Reset(); robot = Robot(); cargo = SynthesisRobots.Cargo(robot); cell = SynthesisRobots.Battery(robot).childItem!;
            var generated = new List<(GameItem Item, ItemWindow Window, GameItem[] Children)>();
            ScavHelper.Loot = () =>
            {
                var loot = new GameItem { identifier = mode == "junk" ? "junk" : "loot" };
                var contents = new GameGridInventory();
                var children = new[] { Owned("contained_a"), Owned("contained_b") };
                foreach (var child in children) contents.UncheckedAccept(child);
                var window = new ItemWindow(new MachineryPanel(contents)); loot.SetContentWindow(window);
                generated.Add((loot, window, children)); return new() { loot };
            };
            if (mode == "full") cargo.Capacity = 0;
            if (mode == "refused") PowerHelper.RefuseDraw = true;
            if (mode is "restored" or "faulted") PowerHelper.ThrowAfterDebit = true;
            if (mode == "faulted") PowerHelper.RefuseRestore = true;
            if (mode == "breaks") UnityEngine.Random.Rolls.Enqueue(0);
            if (mode == "junk") robot.EnableTag(ScavengingRobotRules.SkipJunkTag);
            SynthesisRobots.Work(robot, "0:run:8", 3); SynthesisRobots.Work(robot, "0:run:8", 3);
            int energy = cell.GetTagReadonly("energy").GetInt();
            if (mode is "refused" or "restored")
                check(cargo.childItems.Count == 0 && energy == 100 && robot.GetTagReadonly(ScavengingRobotRules.LastNightTag).valueString == "",
                    "Rejected debit consumed the robot night or energy: " + mode);
            else if (mode == "faulted")
                check(cargo.childItems.Count == 0 && energy == 75 && PowerHelper.DrawCalls == 1 && SynthesisPower.IsFaulted(cell),
                    "Uncertain debit was retried or granted loot");
            else
                check(energy == 75 && PowerHelper.DrawCalls == 1 && ScavHelper.Calls == (mode == "full" ? 1 : 3) &&
                    cargo.childItems.Count == (mode is "full" or "junk" ? 0 : 3), "Robot night repeated or changed cost: " + mode);
            check(generated.All(value => value.Item.parentInventory == cargo
                ? !value.Item.Destroyed && !value.Window.handler.gameObject.Destroyed && value.Children.All(child => !child.Destroyed)
                : value.Item.Destroyed && value.Window.Hidden && value.Window.handler.gameObject.Destroyed &&
                    !GraphHandler.windowGraphNodeRoots.Contains(value.Window) && value.Children.All(child => child.Destroyed)),
                "Robot discarded loot leaked its window/contents or destroyed delivered cargo: " + mode);
            if (mode == "breaks") check(robot.IsTag(ScavengingRobotRules.BrokenTag), "Breakdown state not persisted");
        }
        Reset(); robot = Robot(); PlayerStore.Instance.Items.Add(robot);
        SynthesisRobots.Night(); SynthesisRobots.Night();
        check(SynthesisRobots.Battery(robot).childItem!.GetTagReadonly("energy").GetInt() == 75 &&
            robot.GetTagReadonly(ScavengingRobotRules.LastNightTag).valueString == "0:test-run:8", "Night identity did not include slot/run/day");
        StoreStation.Day++; SynthesisRobots.Night();
        check(SynthesisRobots.Battery(robot).childItem!.GetTagReadonly("energy").GetInt() == 50, "New night was incorrectly deduplicated");

        // Repair/upgrade consume actual owned resources and roll back failed state writes.
        foreach (string action in new[] { "repair", "scanner", "drill", "rollback" })
        {
            Reset(); var target = action == "drill" ? Owned(SynthesisDefinitions.MiningDrill.Id) : Robot();
            target.unitValue = 500;
            if (action == "repair") target.EnableTag(ScavengingRobotRules.BrokenTag);
            var resource = Owned(action == "repair" ? "metal_ingot" : action == "drill" ? SynthesisDefinitions.TitaniumAlloy.Id : "common_electronic", 100);
            input = new(); input.UncheckedAccept(resource);
            if (action == "drill") { IngotPurityHelper.InitIngotItem(resource); IngotPurityHelper.SetPurity(resource, "INGOT_PURITY_PERFECT"); }
            if (action == "rollback") target.ThrowOnModify = true;
            bool applied = RobotResources.Apply(resource, target);
            if (action == "rollback")
                check(!applied && !resource.Destroyed && resource.parentInventory == input && resource.unitCount == 1 && target.unitValue == 500 &&
                    target.GetTagReadonly(ScavengingRobotRules.ScannerChanceTag).GetInt() == 15, "Failed upgrade lost resource or changed target");
            else
            {
                check(applied && resource.Destroyed && input.childItems.Count == 0, "Device action did not consume resource: " + action);
                check(action == "repair" ? !target.IsTag(ScavengingRobotRules.BrokenTag) && target.unitValue == 500 :
                    action == "scanner" ? target.GetTagReadonly(ScavengingRobotRules.ScannerChanceTag).GetInt() == 20 && target.unitValue == 515 :
                    target.IsTag(MiningDrillRules.UpgradedTag) && target.unitValue == 800, "Wrong device upgrade state/value: " + action);
            }
        }
        Reset(); robot = Robot(); SynthesisRobots.SetScannerChance(robot, 100);
        input = new(); var electronic = Owned("common_electronic"); input.UncheckedAccept(electronic);
        check(!RobotResources.Apply(electronic, robot) && electronic.parentInventory == input, "Capped scanner consumed an electronic");
        var drill = Owned(SynthesisDefinitions.MiningDrill.Id); var alloy = Owned(SynthesisDefinitions.TitaniumAlloy.Id);
        input.UncheckedAccept(alloy); IngotPurityHelper.InitIngotItem(alloy); IngotPurityHelper.SetPurity(alloy, "INGOT_PURITY_HIGH");
        check(!RobotResources.Apply(alloy, drill) && !alloy.Destroyed, "Non-Perfect alloy upgraded drill");
        drill.EnableTag(MiningDrillRules.UpgradedTag); IngotPurityHelper.SetPurity(alloy, "INGOT_PURITY_PERFECT");
        check(!RobotResources.Apply(alloy, drill), "Drill upgraded twice");

        // Real tool context and the common native out overload receive one drill roll.
        Reset(); robot = Robot(); drill = Owned(SynthesisDefinitions.MiningDrill.Id);
        EmporiumEntry.Instance!.Afterhour.Add(drill);
        check(SynthesisRobots.FindDrill() == drill, "Manual scavenging did not see owned drill");
        SynthesisRobots.Scavenging = robot;
        check(SynthesisRobots.FindTool("metal_scanner") == robot && SynthesisRobots.FindDrill() == null,
            "Robot borrowed a player tool outside its cargo");
        SynthesisRobots.Cargo(robot).UncheckedAccept(drill);
        var lootList = new List<GameItem>(); UnityEngine.Random.Rolls.Enqueue(0); UnityEngine.Random.Rolls.Enqueue(75);
        Hook(typeof(MiningDrillLootPatch), "Postfix", lootList);
        check(lootList.Count == 1 && lootList[0].identifier == SynthesisDefinitions.Quartz.Id &&
            SynthesisRobots.FindDrill() == drill, "Robot drill loot or context failed");
        SynthesisRobots.Scavenging = null;
        check(MiningDrillRules.SelectOre(49) == "common_ore" && MiningDrillRules.SelectOre(50) == SynthesisDefinitions.TitaniumOre.Id &&
            MiningDrillRules.SelectOre(74) == SynthesisDefinitions.TitaniumOre.Id && MiningDrillRules.SelectOre(75) == SynthesisDefinitions.Quartz.Id &&
            !MiningDrillRules.FindsOre(10) && MiningDrillRules.FindsOre(19, true) && !MiningDrillRules.FindsOre(20, true),
            "Drill loot probability boundaries changed");

        Effects(check);
        EffectCandidates(check);
        Eye(check);
        Reset();
    }

    private static void Effects(Action<bool, string> check)
    {
        Reset();
        var store = PlayerStore.Instance;
        foreach (var definition in ProductionCatalog.Items.Where(item => item.Kind == ItemKind.Cybernetic))
        { var item = Owned(definition.Id); item.unitCount = 4; store.Items.Add(item); }
        var printer = Owned("ce_3d_printer");
        printer.EnableTag("STANDARD_MACHINE_TAG");
        printer.contentWindow.childElement = new GridPixelElement(10, 1);
        printer.Storages = Array.Empty<object?>();
        ManufacturedEffectsRuntime.BeforeRead(printer);
        check(!printer.IsTag("IS_RECEIVING_BONUS_FROM_MODULE_BOOL"), "A custom one-row layout has no native module cell");
        ManufacturedEffectsRuntime.RegisterModuleBay(new GameGridInventory(), printer);
        ManufacturedEffectsRuntime.BeforeRead(printer);
        check(printer.GetTagReadonly("TOTAL_PERCENTAGE_POWER_BONUS_INT").GetInt() == 50,
            "An explicitly registered module bay still receives owned bonuses");
        var machine = SynthesisMachines.Create(SynthesisDefinitions.Machine(SynthesisDefinitions.RefinerItem.Id));
        GeneralHelper.SetItemOwned(machine, true); store.Items.Add(machine);
        var modules = (GameGridInventory)machine.Storages[1]!; modules.UncheckedAccept(Owned("native_module"));
        ManufacturedEffectsRuntime.BeforeRead(machine); ManufacturedEffectsRuntime.BeforeRead(machine);
        check(new[] { "TOTAL_PERCENTAGE_POWER_BONUS_INT", "TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT", "TOTAL_PERCENTAGE_QUALITY_BONUS_INT" }
            .All(tag => machine.GetTagReadonly(tag).GetInt() == 60), "Owned duplicates stacked cybernetic machine effects");
        int scans = store.FindAllItemCalls;
        for (int i = 0; i < 100; i++) ManufacturedEffectsRuntime.BeforeRead(machine);
        check(store.FindAllItemCalls == scans, "Unchanged machine reads repeatedly traversed the store inventory");
        ModuleHelper.ComputeModuleEffect(modules, null, machine);
        check(machine.GetTagReadonly("TOTAL_PERCENTAGE_POWER_BONUS_INT").GetInt() == 60, "Native module reset dropped/stacked owned efficiency");
        store.visitLeftTonight = 1; ManufacturedEffectsRuntime.Closed();
        check(store.visitLeftTonight == 2, "Sleep compression did not add exactly one action");
        var soldRecovery = store.Items.Single(item => item.identifier == SynthesisDefinitions.KineticRecovery.Id);
        GeneralHelper.SetItemOwned(soldRecovery, false);
        store.Items.Remove(soldRecovery);
        ManufacturedEffectsRuntime.BeforeRead(machine);
        check(machine.GetTagReadonly("TOTAL_PERCENTAGE_POWER_BONUS_INT").GetInt() == 10 &&
            machine.GetTagReadonly("TOTAL_PERCENTAGE_QUALITY_BONUS_INT").GetInt() == 60, "Sold cybernetic bonus did not disappear");
        var client = new StoreClient(); store.currentClientInstance = new() { Client = client };
        var product = Owned("valuable"); EmporiumEntry.Instance!.backInvinvElement.UncheckedAccept(product);
        ManufacturedEffectsRuntime.RefreshSale(product); ManufacturedEffectsRuntime.RefreshSale(product);
        check(product.itemFeatures.Count == 1 && product.itemFeatures[0].GetActualValueModifier() == 10 &&
            product.itemFeatures[0].valueStage == ItemFeature.ValueStage.Market, "AI sale modifier stacked or entered innate value");
        scans = store.FindAllItemCalls;
        for (int i = 0; i < 100; i++) ManufacturedEffectsRuntime.RefreshSale(product);
        check(store.FindAllItemCalls == scans, "Unchanged counter price reads repeatedly traversed the store inventory");
        product.SetGameItemType("CYBERNETIC"); client.isAug = true; ManufacturedEffectsRuntime.RefreshSale(product);
        check(product.itemFeatures.Count == 1 && product.itemFeatures[0].GetActualValueModifier() == 25, "Aug cybernetic bonus combined with AI bonus");
        client.identifier = ManufacturedEffectsRuntime.PeerClientId; ManufacturedEffectsRuntime.RefreshSale(product);
        check(product.itemFeatures.Count == 0, "Peer trade acquired an NPC sale bonus");
        client.identifier = "npc"; EmporiumEntry.Instance.backInvinvElement.Expel(product); ManufacturedEffectsRuntime.RefreshSale(product);
        check(product.itemFeatures.Count == 0, "Sale modifier survived moving off the counter");
        check(CyberneticTrading.Accepts(client, product), "Aug refused sellable CYBERNETIC type");
        product.EnableTag("NOT_FOR_RESALE"); check(!CyberneticTrading.Accepts(client, product), "Aug bypassed not-for-resale tag");
        client.SetBudget(300); CyberneticTrading.Budget(client); CyberneticTrading.Budget(client);
        check(client.GetBudget() == 600, "Aug budget initializer compounded");
        client.OverrideBudget(450); check(client.GetBudget() == 450, "Native budget debit was rescaled");
        client.isAug = false; client.SetBudget(300); CyberneticTrading.Budget(client);
        check(client.GetBudget() == 300, "Non-Aug budget doubled");
        var box = CyberneticStorage.Create(); GeneralHelper.SetItemOwned(box, true);
        var inventory = (GameGridInventory)box.contentWindow.childElement!;
        var cyber = Owned("cyber"); cyber.SetGameItemType("CYBERNETIC");
        check(box.IsTag("ITEM_HIDDEN_TAG") && inventory.identifier == "smuggler_bay" && inventory.mayInventoryAddItemFunc!(cyber, inventory),
            "Cybernetic storage did not use native concealment/admission");
        check(!inventory.mayInventoryAddItemFunc!(Owned("ordinary"), inventory), "Non-cybernetic accepted by cyber storage");
        GeneralHelper.SetItemOwned(cyber, false);
        check(!inventory.mayInventoryAddItemFunc!(cyber, inventory), "Cyber storage discarded native ownership check");
        var cat = Owned(SynthesisDefinitions.RobotCat.Id); EmporiumEntry.Instance.showcaseElement.UncheckedAccept(cat);
        object?[] arguments = { 40 }; Hook(typeof(RobotCatAttractivenessPatch), "Postfix", arguments);
        check((int)arguments[0]! == 540, "Showcase robot cat did not add attractiveness");
        EmporiumEntry.Instance.showcaseElement.Expel(cat); arguments[0] = 40; Hook(typeof(RobotCatAttractivenessPatch), "Postfix", arguments);
        check((int)arguments[0]! == 40, "Off-showcase cat still added attractiveness");
    }

    private static void EffectCandidates(Action<bool, string> check)
    {
        Reset();
        var store = PlayerStore.Instance;
        var core = Owned(SynthesisDefinitions.AiCore.Id);
        GeneralHelper.SetItemOwned(core, false); store.Items.Add(core);
        check(!ManufacturedEffectsRuntime.Capture().AiCore, "Unowned indexed core supplied an effect");
        int scans = store.FindAllItemCalls;
        GeneralHelper.SetItemOwned(core, true);
        check(ManufacturedEffectsRuntime.Capture().AiCore, "Buying an indexed core did not immediately enable its effect");
        core.unitCount = 0;
        check(!ManufacturedEffectsRuntime.Capture().AiCore, "Consuming the last indexed core retained its effect");
        core.unitCount = 1;
        check(ManufacturedEffectsRuntime.Capture().AiCore, "Restoring an indexed core count retained an empty effect");
        GeneralHelper.SetItemOwned(core, false);
        check(!ManufacturedEffectsRuntime.Capture().AiCore && store.FindAllItemCalls == scans,
            "Live quantity/ownership changes required another full inventory scan");

        var container = Owned("container");
        var grid = new GameGridInventory();
        container.SetContentWindow(new ItemWindow(new MachineryPanel(grid)));
        grid.UncheckedAccept(Owned(SynthesisDefinitions.AiCore.Id));
        store.Items.Add(container);
        // Items models native window roots in this port; report the successful
        // attachment through the production postfix used by the real graph methods.
        ItemGraphChanges.Postfix(true);
        check(ManufacturedEffectsRuntime.Capture().AiCore && store.FindAllItemCalls == scans + 1,
            "Attaching a nested core did not refresh the candidate index once");
        scans = store.FindAllItemCalls;
        ItemGraphChanges.Postfix(false);
        check(ManufacturedEffectsRuntime.Capture().AiCore && store.FindAllItemCalls == scans,
            "A rejected graph attachment discarded the candidate index");
        store.Items.Remove(container); ItemGraphChanges.Postfix(true);
        check(!ManufacturedEffectsRuntime.Capture().AiCore && store.FindAllItemCalls == scans + 1,
            "Detaching an owned container retained its nested effect");
        scans = store.FindAllItemCalls;
        store.saveSlotId++;
        ManufacturedEffectsRuntime.Capture();
        store.runID = "other-run";
        ManufacturedEffectsRuntime.Capture();
        check(store.FindAllItemCalls == scans + 2, "Candidate index crossed a slot or run identity change");
        PlayerStore.Current = new();
        check(!ManufacturedEffectsRuntime.Capture().AiCore && PlayerStore.Current.FindAllItemCalls == 1,
            "Candidate index crossed a store replacement");
    }

    private static void Eye(Action<bool, string> check)
    {
        Reset();
        foreach (var entry in new[] { ("genuineCigarette", "CATEGORY_GENUINE_CIGARETTE", "packet_red_cigarette", 100L),
            ("counterfeitCigarette", "CATEGORY_GENUINE_CIGARETTE", "packet_red_cigarette", 50L),
            ("genuineStamp", "CATEGORY_GENUINE_STAMP", "food_stamp", 9L), ("counterfeitStamp", "CATEGORY_GENUINE_STAMP", "food_stamp", 8L),
            ("genuineInjector", "CATEGORY_GENUINE_INJECTOR", "large_purple_injector", 160L),
            ("expiredInjector", "CATEGORY_GENUINE_INJECTOR", "large_purple_injector", 80L),
            ("counterfeitInjector", "CATEGORY_GENUINE_INJECTOR", "large_purple_injector", 40L),
            ("unusableInjector", "CATEGORY_GENUINE_INJECTOR", "large_purple_injector", 8L) })
        {
            var item = Owned(entry.Item3, 999); item.uniqueId = 11; item.EnableTag("INSPECTABLE_TAG"); item.GetTagReadonly("STAMP_AMOUNT").SetInt(12);
            var condition = new ItemCondition { identifier = entry.Item1, category = entry.Item2, isTransformative = true };
            var feature = new ItemFeature(ItemFeature.FeatureType.Normale) { identifier = "truth", useCondition = true, realCondition = condition, isExposable = true, Display = "Truth" };
            item.itemFeatures.Add(feature);
            var scan = CyberEyeScan.Capture(item);
            check(scan?.Value == entry.Item4 && scan.Cost == 5 && item.unitValue == 999 && feature.realCondition == condition && feature.useCondition,
                "Eye mutated appraisal state or used outdated value: " + entry.Item1);
            feature.Display = "Localized truth";
            check(CyberEyeScan.Capture(item)?.Fingerprint == scan?.Fingerprint, "Localized eye label caused another scan charge");
        }
        var berry = Owned("wine_berry", 100); berry.uniqueId = 12; berry.EnableTag("INSPECTABLE_TAG"); berry.EnableTag("BERRY_TAG");
        berry.GetTagReadonly("BERRY_CONDITION_INT").SetInt(-2);
        var innate = new ItemFeature(ItemFeature.FeatureType.Normale) { valueStage = ItemFeature.ValueStage.Innate }; innate.SetValueModifier(20);
        var market = new ItemFeature(ItemFeature.FeatureType.TemporarySelling) { valueStage = ItemFeature.ValueStage.Market }; market.SetValueModifier(900);
        var final = new ItemFeature(ItemFeature.FeatureType.Normale) { valueStage = ItemFeature.ValueStage.Final }; final.SetValueModifier(50);
        berry.itemFeatures.AddRange(new[] { innate, market, final });
        check(CyberEyeScan.Capture(berry) is { Value: 180 } scanBerry && scanBerry.RealDisplays.Contains("Moldy fruit"),
            "Eye ignored native final stages or included the market stage");
        var unsupported = Owned("unknown"); unsupported.EnableTag("APPRAISAL_ITEM"); unsupported.EnableTag("HAND_TAG");
        check(CyberEyeScan.Capture(unsupported) == null, "Native tags admitted an unsupported eye item");
        var hand = Owned("hand"); hand.uniqueId = 13; hand.EnableTag("APPRAISAL_ITEM"); hand.EnableTag("HAND_TAG"); hand.GetTagReadonly("correctValue").SetInt(321);
        check(CyberEyeScan.Capture(hand) is { Value: 321, Cost: 15 }, "Appraisal eye cost/value changed");
        var eye = Owned(SynthesisDefinitions.CyberEye.Id); var battery = new GameSlotInventory(); eye.Storages[0] = battery;
        var cell = Cell(battery, 30); PlayerStore.Instance.Items.Add(eye);
        var text = new RichTextBuilder(); SynthesisCyberEye.Tooltip(text, hand); SynthesisCyberEye.Tooltip(text, hand);
        check(cell.GetTagReadonly("energy").GetInt() == 15 && PowerHelper.DrawCalls == 1 && text.Lines.Any(line => line.Contains("321")),
            "Repeated tooltip charged the eye twice");
        hand.GetTagReadonly("correctValue").SetInt(322); SynthesisCyberEye.Tooltip(text, hand);
        check(cell.GetTagReadonly("energy").GetInt() == 0 && PowerHelper.DrawCalls == 2, "Changed appraisal truth did not invalidate paid scan");
        SynthesisCyberEye.Tooltip(text, hand);
        check(PowerHelper.DrawCalls == 2, "Cached eye scan charged an empty cell");
        hand.GetTagReadonly("correctValue").SetInt(323); text = new(); SynthesisCyberEye.Tooltip(text, hand);
        check(PowerHelper.DrawCalls == 2 && !text.Lines.Any(line => line.Contains("323")), "Insufficient power revealed a new scan");
        cell.GetTagReadonly("energy").SetInt(30); PlayerStore.Instance.runID = "another-run";
        SynthesisCyberEye.Tooltip(text, hand);
        check(PowerHelper.DrawCalls == 3 && cell.GetTagReadonly("energy").GetInt() == 15, "Eye paid cache survived a different run");
        foreach (bool night in new[] { false, true })
        {
            Reset(); CurrentConverterHelper.isNightCycle = night; PlayerStore.Instance.playerCash = 100;
            battery = new(); cell = Cell(battery, 0, "current_converter"); cell.EnableTag("service");
            check(SynthesisPower.Debit(battery, 25) == PowerDebit.Committed && PlayerStore.Instance.playerCash == (night ? 100 : 50),
                "Native converter billing semantics lost: night=" + night);
        }
        Reset(); battery = new(); cell = Cell(battery, 0, "hypertech_battery"); cell.GetTagReadonly("output").SetInt(20);
        check(SynthesisPower.Debit(battery, 25) == PowerDebit.Rejected && SynthesisPower.Debit(battery, 15) == PowerDebit.Committed,
            "Hypertech output limit was treated as stored battery energy");
    }
}
