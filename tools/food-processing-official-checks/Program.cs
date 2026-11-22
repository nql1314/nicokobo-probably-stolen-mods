using System.Reflection;
using System.Xml.Linq;
using DseCecil;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nicokobo.FoodProcessing;
using Nicokobo.FoodProcessing.Definitions;
using Nicokobo.ContentInterop;

int checks = 0;
foreach (var recipe in FoodDefinitions.Recipes) DirectoryMaster.Known.Add(recipe.OutputItemId);
void Check(bool condition, string message)
{ checks++; if (!condition) throw new InvalidOperationException(message); }
void Reject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, message);
}

// A real Newtonsoft round-trip must read the records and readonly collections
// written by the schema-7 JSON format, including string enums.
Check(FoodDefinitions.Items.Count == 58 && FoodDefinitions.Recipes.Count == 67 &&
    FoodDefinitions.Items.Count(item => item.Kind is ItemKind.Food or ItemKind.Drink) == 50 &&
    FoodDefinitions.Items.Count(item => item.Kind == ItemKind.Medical) == 5, "Food and medical catalog counts differ");
var document = RecipeConfiguration.Parse(RecipeConfiguration.DefaultJson());
Check(document.Recipes.Count == 67 && DefinitionValidator.ValidateRecipes(document.Recipes).Count == 0, "Default recipes invalid");
var configured = JObject.Parse(RecipeConfiguration.DefaultJson());
configured["ProductionMarkups"]!["K04"] = 37;
configured["Recipes"]![0]!["Requirements"]![0]!["Amount"] = 4;
var custom = RecipeConfiguration.Parse(configured.ToString());
Check(custom.ProductionMarkups["K04"] == 37 && custom.Recipes[0].Requirements[0].Amount == 4, "Custom settings were lost");
foreach (string property in new[] { "Recipes", "ProductionMarkups" })
{
    var invalid = JObject.Parse(RecipeConfiguration.DefaultJson()); invalid[property] = null;
    Reject(() => RecipeConfiguration.Parse(invalid.ToString()), "Accepted null " + property);
}
var missing = JObject.Parse(RecipeConfiguration.DefaultJson()); ((JArray)missing["Recipes"]!).RemoveAt(0);
Reject(() => RecipeConfiguration.Parse(missing.ToString()), "Accepted missing recipe");
var changed = JObject.Parse(RecipeConfiguration.DefaultJson()); changed["Recipes"]![0]!["MachineId"] = "other.machine";
Reject(() => RecipeConfiguration.Parse(changed.ToString()), "Accepted changed recipe identity");
var invalidWater = JObject.Parse(RecipeConfiguration.DefaultJson()); invalidWater["Recipes"]![0]!["Requirements"]![1]!["Amount"] = int.MaxValue;
Reject(() => RecipeConfiguration.Parse(invalidWater.ToString()), "Accepted overflowing water debit");

(GameItem Machine, GameSlotInventory Battery, GameGridInventory Input, GameGridInventory Output, GameSlotInventory Water) Setup(string machineId)
{
    PowerHelper.RefuseDraw = PowerHelper.ThrowAfterDebit = false;
    DirectoryMaster.FailCleanup = false;
    DirectoryMaster.Created.Clear();
    var machine = new GameItem { identifier = machineId };
    machine.GetTagReadonly("is_machinery_powered").SetBool(true);
    machine.GetTagReadonly("power").SetInt(10);
    machine.GetTagReadonly("machine_quality").SetInt(200);
    machine.GetTagReadonly(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).SetInt(10);
    var battery = new GameSlotInventory(); var cell = new GameItem(); cell.GetTagReadonly("energy").SetInt(100); battery.UncheckedAccept(cell);
    var waterSlot = new GameSlotInventory(); var water = new GameItem(); water.GetTagReadonly("water").SetInt(1000000);
    water.GetTagReadonly("quality").SetInt(2); waterSlot.UncheckedAccept(water);
    var input = new GameGridInventory();
    var output = new GameGridInventory { mayInventoryAddItemFunc = (_, _) => false };
    machine.Storages[0] = battery; machine.Storages[1] = new GameGridInventory();
    machine.Storages[2] = input; machine.Storages[3] = output; machine.Storages[5] = waterSlot;
    return (machine, battery, input, output, waterSlot);
}
GameItem Add(GameGridInventory inventory, string id, int count = 1, long value = 10)
{
    var item = new GameItem { identifier = id, unitCount = count, unitValue = value };
    inventory.UncheckedAccept(item); return item;
}
var fruitJuice = document.Recipes.Single(recipe => recipe.Code == "F03");
foreach (bool cleanAvailable in new[] { false, true })
{
    var s = Setup(fruitJuice.MachineId);
    var tainted = Add(s.Input, "nutrifruit", 5);
    tainted.GetTagReadonly("GUTTERFLOW_TAINTED_TAG").Enable();
    var clean = cleanAvailable ? Add(s.Input, "nutrifruit", 3) : null;
    Check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, fruitJuice) == cleanAvailable,
        "Polluted input was accepted or prevented a clean batch");
    Check(tainted.parentInventory == s.Input && tainted.unitCount == 5 &&
        s.Battery.childItem!.GetTagReadonly("energy").GetInt() == (cleanAvailable ? 90 : 100) &&
        WaterHelper.GetTotalVolume(s.Water.childItem!) == (cleanAvailable ? 500000 : 1000000) &&
        (!cleanAvailable || clean!.Destroyed), "Polluted food changed resources or displaced eligible input");
}
{
    var s = Setup(fruitJuice.MachineId);
    var first = Add(s.Input, "nutrifruit", 2, 12); var second = Add(s.Input, "nutrifruit", 2, 12);
    Check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, fruitJuice), "Cross-stack recipe failed");
    Check(first.Destroyed && second.unitCount == 1 && s.Input.childItems.Count == 1, "Wrong cross-stack consumption");
    Check(s.Battery.childItem!.GetTagReadonly("energy").valueInt == 90 && WaterHelper.GetTotalVolume(s.Water.childItem!) == 500000,
        "Power/water were not debited exactly once");
    Check(s.Output.childItems.Single().unitValue == 80, "Production pricing ignores the configured recipe markup");
    var product = s.Output.childItems[0]; string state = JsonConvert.SerializeObject(product.state);
    product.state = JsonConvert.DeserializeObject<TagSystem>(state)!; product.unitValue = 999;
    FoodProduction.RestoreBasis(product);
    Check(product.unitValue == 80, "Production basis did not survive JSON save/reload");
    Check(s.Output.mayInventoryAddItemFunc!(new(), s.Output) == false, "Output admission left open");
}
foreach (string failure in new[] { "materials", "water", "quality", "power", "output", "draw", "draw throws", "expel", "cleanup" })
{
    var s = Setup(fruitJuice.MachineId);
    var fruit = Add(s.Input, "nutrifruit", failure == "materials" ? 2 : 3);
    int initialCount = fruit.unitCount;
    if (failure == "water") s.Water.childItem!.GetTagReadonly("water").SetInt(1000);
    if (failure == "quality") s.Water.childItem!.GetTagReadonly("quality").SetInt(0);
    if (failure == "power") s.Battery.childItem!.GetTagReadonly("energy").SetInt(0);
    if (failure == "output") s.Output.Capacity = 0;
    if (failure is "draw" or "cleanup") PowerHelper.RefuseDraw = true;
    if (failure == "cleanup") DirectoryMaster.FailCleanup = true;
    if (failure == "draw throws") PowerHelper.ThrowAfterDebit = true;
    if (failure == "expel") s.Input.RefuseExpel = true;
    int initialWater = WaterHelper.GetTotalVolume(s.Water.childItem!);
    int initialEnergy = s.Battery.childItem!.GetTagReadonly("energy").valueInt;
    bool success = false;
    try { success = FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, fruitJuice); }
    catch (InvalidOperationException) when (failure is "draw throws" or "expel") { }
    catch (AggregateException) when (failure == "cleanup") { }
    Check(!success && fruit.parentInventory == s.Input && fruit.unitCount == initialCount && !fruit.Destroyed,
        "Lost material on " + failure);
    Check(s.Output.childItems.Count == 0 && DirectoryMaster.Created.All(item => item.Destroyed), "Left free output on " + failure);
    Check(WaterHelper.GetTotalVolume(s.Water.childItem!) == initialWater && s.Battery.childItem!.GetTagReadonly("energy").valueInt == initialEnergy,
        "Lost water/power on " + failure);
    Check(s.Output.mayInventoryAddItemFunc!(new(), s.Output) == false, "Output admission not restored on " + failure);
}
{
    var recipe = document.Recipes.Single(recipe => recipe.Code == "F17");
    var s = Setup(recipe.MachineId); Add(s.Input, "common_chemical"); s.Output.Capacity = 2;
    Check(!FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, recipe), "Partial three-output batch succeeded");
    Check(s.Output.childItems.Count == 0 && s.Input.childItems.Count == 1 && WaterHelper.GetTotalVolume(s.Water.childItem!) == 1000000,
        "Partial output rejection consumed resources");
}
{
    var recipe = document.Recipes.Single(recipe => recipe.Code == "F25");
    var s = Setup(recipe.MachineId);
    var a = Add(s.Input, "fish_scrap", 2); a.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(40);
    var b = Add(s.Input, "fish_scrap"); b.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(100);
    var meat = Add(s.Input, "processed_meat"); meat.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(200);
    Check(FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, null, recipe), "Variable calorie batch failed");
    Check(s.Output.childItems.Single().GetTagReadonly("CALORIE_VALUE_TAG").valueInt == 380, "Lost actual input calories");
}

// Exercise the public logistics contract through the real food production
// method: the observer moves a real stock item and water before selection.
ContentRuntime.Attach(FoodDefinitions.ModId);
FoodInterop.Register();
Check(FoodDefinitions.Machines.All(definition => MachineCatalog.Contains(definition.ItemId)), "Food machine contracts missing");
Check(MachineSchedule.Order(FoodDefinitions.FoodProcessorItem.Id) == 10,
    "K06 food processor must run at night stage 10; actual=" + MachineSchedule.Order(FoodDefinitions.FoodProcessorItem.Id));
Check(MachineSchedule.Order(FoodDefinitions.InfusionTowerItem.Id) == 40,
    "K04 liquid bottler must run at night stage 40; actual=" + MachineSchedule.Order(FoodDefinitions.InfusionTowerItem.Id));
var publicRecipe = MachineCatalog.Recipes(fruitJuice.MachineId).Single(recipe => recipe.Id == fruitJuice.Id);
Check(publicRecipe.WaterParts == 500000 && publicRecipe.OutputItemId == fruitJuice.OutputItemId &&
    publicRecipe.Ingredients.Single().Amount == 3 && publicRecipe.Ingredients.Single().Matches(new() { identifier = "nutrifruit" }),
    "Food recipe contract lost native water units or ingredient matching");
PlayerStore.Current = new();
DirectoryMaster.Known.UnionWith(FoodDefinitions.Items.Select(item => item.Id));
Check(SupplyCatalog.Available(StockCategory.Food).Select(offer => offer.ItemId).ToHashSet().SetEquals(
    FoodDefinitions.Items.Where(item => item.Kind is ItemKind.Food or ItemKind.Drink).Select(item => item.Id)),
    "Food supply contract lost consumables or included machines");
const string logistics = "checks.logistics";
ContentRuntime.Attach(logistics);
foreach (bool rejected in new[] { false, true })
{
    var s = Setup(fruitJuice.MachineId);
    s.Water.childItem!.GetTagReadonly("water").SetInt(0);
    if (rejected) s.Output.Capacity = 0;
    var stock = new GameGridInventory();
    var fruit = Add(stock, "nutrifruit", 5);
    var reservoir = new GameItem(); reservoir.GetTagReadonly("water").SetInt(600000);
    int beforeCalls = 0;
    var results = new List<WorkResult>();
    ProductionHooks.Subscribe(logistics, "food.logistics", context =>
    {
        if (context.Machine != s.Machine) return;
        beforeCalls++;
        if (!context.Ports.Input.UncheckedAccept(fruit)) throw new InvalidOperationException("Fixture stock transfer rejected");
        int amount = context.Recipe!.WaterParts;
        WaterHelper.Remove(reservoir, amount);
        context.Ports.Water!.childItem!.GetTagReadonly("water").SetInt(amount);
    }, result => results.Add(result));
    var ports = MachineCatalog.Ports(s.Machine)!;
    Check(ports.Input == s.Input && ports.Output == s.Output && ports.Battery == s.Battery && ports.Water == s.Water,
        "Food contract points at the wrong machine inventories");
    bool success = FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, fruitJuice);
    Check(beforeCalls == 1 && stock.childItems.Count == 0 && WaterHelper.GetTotalVolume(reservoir) == 100000,
        "Before-work did not supply actual material and water once");
    Check(success == !rejected && results.Count == 1 && results[0].Committed == success &&
        results[0].Context.Machine == s.Machine && results[0].Context.Recipe!.Id == fruitJuice.Id,
        "After-work reported an incorrect transaction result");
    if (rejected)
    {
        Check(results[0].Consumed.Count == 0 && results[0].WaterPartsConsumed == 0 && s.Output.childItems.Count == 0 &&
            fruit.parentInventory == s.Input && fruit.unitCount == 5 && WaterHelper.GetTotalVolume(s.Water.childItem!) == 500000,
            "Rejected batch reported success or consumed the supplied resources");
    }
    else
    {
        Check(results[0].Consumed.Count == 1 && results[0].Consumed[0].Item == fruit && results[0].Consumed[0].Amount == 3 &&
            fruit.unitCount == 2 && results[0].WaterPartsConsumed == 500000,
            "After-work consumption does not match the actual partial stack and water debit");
        Check(s.Output.childItems.Count == 1 && s.Output.childItems.Single().unitCount == 1,
            "Committed food batch did not create the actual output item");
    }
}
// A recipe supplied by another content pack must use the same real food
// transaction, including partial stacks, price, capacity and failed-debit rollback.
const string extraOwner = "checks.aug";
const string repairGel = "checks.repair_gel";
ContentRuntime.Attach(extraOwner);
DirectoryMaster.Known.Add(repairGel);
bool extraAvailable = true;
var extraRecipe = new ExtraRecipe("checks.repair_gel.recipe", FoodDefinitions.InfusionTowerItem.Id,
    new[] { new IngredientView("blood_bag", 1, item => item.identifier == "blood_bag", "血袋", "Blood Bag"),
        new IngredientView("bandage_item", 1, item => item.identifier == "bandage_item", "绷带", "Bandage") },
    repairGel, "修补胶", "Repair Gel", () => extraAvailable, () => repairGel);
ExtraRecipes.Register(extraOwner, extraRecipe);
Check(MachineCatalog.Recipes(extraRecipe.MachineId).Any(recipe => recipe.Id == extraRecipe.Id),
    "Food machine did not expose the external recipe");
foreach (string failure in new[] { "none", "unavailable", "capacity", "power", "tainted" })
{
    var s = Setup(extraRecipe.MachineId);
    var blood = Add(s.Input, "blood_bag", 2, 33);
    var bandage = Add(s.Input, "bandage_item", 1, 8);
    if (failure == "tainted") blood.GetTagReadonly("GUTTERFLOW_TAINTED_TAG").Enable();
    var admission = s.Output.mayInventoryAddItemFunc;
    extraAvailable = failure != "unavailable";
    if (failure == "capacity") s.Output.Capacity = 0;
    PowerHelper.RefuseDraw = failure == "power";
    bool produced = FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, extraRecipe);
    Check(produced == (failure == "none"), "External food recipe result differs for " + failure);
    Check(s.Output.mayInventoryAddItemFunc == admission, "External food recipe left output admission changed");
    if (produced)
    {
        Check(blood.unitCount == 1 && bandage.Destroyed && s.Output.childItems.Single().identifier == repairGel &&
            s.Output.childItems.Single().unitValue == 62 && s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 90,
            "External food recipe lost input quantities, price or its one energy debit");
    }
    else
    {
        Check(blood.parentInventory == s.Input && blood.unitCount == 2 && bandage.parentInventory == s.Input &&
            bandage.unitCount == 1 && s.Output.childItems.Count == 0 && s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100,
            "Rejected external food recipe did not preserve its resources for " + failure);
    }
}
extraAvailable = true;
PowerHelper.RefuseDraw = false;
var graftRecipe = new ExtraRecipe("checks.repair_gel.recipe.skin_graft", FoodDefinitions.InfusionTowerItem.Id,
    new[] { new IngredientView("tilapia_skin_graft", 1, item => item.identifier == "tilapia_skin_graft", "植皮片", "Skin Graft"),
        new IngredientView("bandage_item", 1, item => item.identifier == "bandage_item", "绷带", "Bandage") },
    repairGel, "修补胶（植皮）", "Repair Gel (Skin Graft)", () => true, () => repairGel);
ExtraRecipes.Register(extraOwner, graftRecipe);
var publicGraft = MachineCatalog.Recipes(graftRecipe.MachineId).Single(recipe => recipe.Id == graftRecipe.Id);
Check(publicGraft is ExtraRecipe && publicGraft.Preview != null,
    "Host metadata did not retain the external route label and dynamic preview");
foreach (string failure in new[] { "none", "quality", "capacity", "power", "tainted", "selection" })
{
    var s = Setup(graftRecipe.MachineId);
    var graft = Add(s.Input, "tilapia_skin_graft", 1, 24);
    var bandage = Add(s.Input, "bandage_item", 1, 8);
    var blood = Add(s.Input, "blood_bag", 1, 33);
    s.Machine.GetTagReadonly(FoodProduction.SelectionTag).SetString(failure == "selection" ? extraRecipe.Id : graftRecipe.Id);
    if (failure == "quality") graft.InnatePercent = 50;
    if (failure == "tainted") graft.GetTagReadonly("GUTTERFLOW_TAINTED_TAG").Enable();
    if (failure == "capacity") s.Output.Capacity = 0;
    PowerHelper.RefuseDraw = failure == "power";
    bool expected = failure is "none" or "quality";
    if (expected)
    {
        var roundtrip = ForgeWire.Recipe(ForgeWire.Recipe(publicGraft));
        Check(roundtrip.IsAvailableFor(s.Machine) && roundtrip.Preview!(s.Machine).Contains(failure == "quality" ? "65" : "50") &&
            !FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, extraRecipe),
            "Selected graft route lost its preview across the wire or silently used blood");
    }
    bool produced = FoodProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, s.Water, graftRecipe);
    Check(produced == expected && blood.parentInventory == s.Input && blood.unitCount == 1,
        "Graft recipe result or preservation of the unused blood bag differs for " + failure);
    if (produced)
    {
        var product = s.Output.childItems.Single();
        Check(product.unitValue == (failure == "quality" ? 65 : 50) && graft.Destroyed && bandage.Destroyed &&
            s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 90, "Graft recipe lost actual costs or its one debit");
        product.unitValue = 999;
        FoodProduction.RestoreBasis(product);
        Check(product.unitValue == (failure == "quality" ? 65 : 50), "Graft product lost its persisted production basis");
    }
    else Check(graft.parentInventory == s.Input && graft.unitCount == 1 && bandage.parentInventory == s.Input &&
        bandage.unitCount == 1 && s.Output.childItems.Count == 0 && s.Battery.childItem!.GetTagReadonly("energy").GetInt() == 100,
        "Rejected graft batch changed resources for " + failure);
}
PowerHelper.RefuseDraw = false;
ContentRuntime.Detach(extraOwner);
Check(ExtraRecipes.ForMachine(extraRecipe.MachineId).Count == 0, "Detached external food recipe remains active");
ContentRuntime.Detach(logistics);
ContentRuntime.Detach(FoodDefinitions.ModId);
Check(!MachineCatalog.Contains(fruitJuice.MachineId) && SupplyCatalog.Available(StockCategory.Food).Count == 0,
    "Disabling the food provider retained public machine or supply registrations");

// Read actual game and candidate metadata. Do not load Unity or execute the
// game's scanner here: these checks verify target signatures and dependencies.
if (args.Length != 3) throw new ArgumentException("Expected candidate DLL, game DLL and mod source directory");
using (var binary = AssemblyDefinition.ReadAssembly(args[0]))
using (var game = AssemblyDefinition.ReadAssembly(args[1]))
{
    var factoryCalls = binary.MainModule.Types.Single(type => type.Name == "FoodItems").Methods.Single(method => method.Name == "Create")
        .Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().ToArray();
    Check(factoryCalls.Any(method => method.DeclaringType.Name == "ItemDirectory" && method.Name == "CreateEmptyItem") &&
        factoryCalls.All(method => method.DeclaringType.Name != "DirectoryMaster"),
        "Food factory copies unrelated template state or allocates template UIDs");

    Check(!binary.MainModule.AssemblyReferences.Any(reference => reference.Name.Contains("Il2Cpp") ||
        reference.Name.Contains("Melon") || reference.Name.Contains("Forge") || reference.Name == "System.Text.Json"),
        "Official mod still references Demo dependencies");
    var nativeItem = game.MainModule.Types.Single(type => type.Name == "GameItem");
    foreach (var target in new[]
    {
        (Type: "HusbandryDirectory", Method: "SerumGreen", Parameters: "", Result: "GameItem"),
        (Type: "HusbandryDirectory", Method: "SerumRed", Parameters: "", Result: "GameItem"),
        (Type: "MaterialDirectory", Method: "CanIngotTarget", Parameters: "source:GameItem,target:GameItem", Result: "System.Boolean"),
        (Type: "IndustrialFishFeedHelper", Method: "Split", Parameters: "feed:GameItem", Result: "System.Void"),
        (Type: "IndustrialFishFeedHelper", Method: "Merge", Parameters: "source:GameItem,target:GameItem", Result: "System.Void"),
        (Type: "IndustrialFishFeedHelper", Method: "SetKcal", Parameters: "feed:GameItem,kcal:System.Int32", Result: "System.Void"),
        (Type: "GameItem", Method: "DisplayManufacturer", Parameters: "builder:RichTextBuilder,manufacturer:System.String", Result: "System.Void")
    })
    {
        var method = game.MainModule.Types.Single(type => type.Name == target.Type).Methods.Single(method => method.Name == target.Method);
        Check(method.ReturnType.FullName == target.Result && string.Join(",", method.Parameters.Select(parameter =>
            parameter.Name + ":" + parameter.ParameterType.FullName)) == target.Parameters, "Food patch target changed: " + target.Type + "." + target.Method);
    }
    foreach (var targets in new[]
    {
        (Patch: "RemainingFoodValuePatch", Type: "GameItem", Methods: new[] { "GetCurrentValue", "GetRefreshedValue", "GetBaseValue" }),
        (Patch: "FoodUseTransferPatch", Type: "HusbandryDirectory", Methods: new[] { "SerumGreen", "SerumRed" }),
        (Patch: "FoodUseValuePatch", Type: "UseCountHelper", Methods: new[] { "Use", "SetCurrentUseCount", "ReplenishUseCount", "ModMaxUseCount" })
    })
    {
        var factory = binary.MainModule.Types.Single(type => type.Name == targets.Patch).Methods.Single(method => method.Name == "TargetMethods");
        var names = factory.Body.Instructions.Where(instruction => instruction.OpCode.Code == DseCecil.Cil.Code.Ldstr)
            .Select(instruction => (string)instruction.Operand).ToArray();
        Check(names.SequenceEqual(targets.Methods) && targets.Methods.All(name =>
            game.MainModule.Types.Single(type => type.Name == targets.Type).Methods.Count(method => method.Name == name) == 1),
            "Multi-target Harmony patch omitted or ambiguously resolved a native target: " + targets.Patch);
    }
    var accumulate = nativeItem.Methods.Single(method => method.Name == "AccumulateFeatureStages");
    Check(accumulate.ReturnType.FullName == "System.Void" && accumulate.Parameters.Select(p => p.ParameterType.FullName)
        .SequenceEqual(new[] { "System.Int64&", "System.Int64&", "System.Double&", "System.Boolean" }), "Private value accumulator changed");
    var compose = nativeItem.Methods.Single(method => method.Name == "ComposeStagedValue");
    Check(compose.IsStatic && compose.ReturnType.FullName == "System.Int64" && compose.Parameters.Select(p => p.ParameterType.FullName)
        .SequenceEqual(new[] { "System.Int64", "System.Int64", "System.Int64", "System.Double", "System.Int32", "System.Int32" }),
        "Private price composition changed");
    var fridge = game.MainModule.Types.Single(type => type.Name == "MachineFridge");
    foreach (string method in new[] { "MayZoneAcceptItem", "IsChillable", "IsFreezable" })
        Check(fridge.Methods.Any(m => m.Name == method && m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 1 &&
            m.Parameters[0].Name == "item" && m.Parameters[0].ParameterType.FullName == "GameItem"), "Fridge patch target changed: " + method);
    Check(binary.MainModule.Types.Count(type => type.Interfaces.Any(i => i.InterfaceType.FullName == "IMod")) == 1,
        "Expected one official entry point");
    var manifest = XDocument.Load(Path.Combine(args[2], "manifest.xml")).Root!;
    var declaredAssemblies = manifest.Element("Assemblies")!.Elements("Assembly").Select(element => element.Value).ToArray();
    Check((string?)manifest.Element("ID") == FoodDefinitions.ModId && manifest.Element("Capabilities")!
        .Elements("Capability").Any() == false &&
        declaredAssemblies.FirstOrDefault() == Path.GetFileName(args[0]) && declaredAssemblies.Distinct().Count() == declaredAssemblies.Length,
        "Manifest does not match candidate");
    Check(declaredAssemblies.Length == 1 && manifest.Element("Prerequisites")!.Elements("Mod").Any(element => element.Value == "nicokobo.forge") && binary.MainModule.AssemblyReferences
        .Where(reference => reference.Name.StartsWith("Nicokobo.", StringComparison.Ordinal)).All(reference =>
            declaredAssemblies.Contains(reference.Name + ".dll") && File.Exists(Path.Combine(Path.GetDirectoryName(args[0])!, reference.Name + ".dll"))),
        "Manifest or build output omits a content runtime dependency");
    Check((string?)manifest.Element("ModVersion") == binary.Name.Version.ToString(3), "Manifest/assembly versions differ");
    foreach (var item in FoodDefinitions.Items)
    {
        string key = item.Id.Substring(FoodDefinitions.IdPrefix.Length + 1).Replace('.', '_');
        using var stream = File.OpenRead(Path.Combine(args[2], "Textures", "Items", key + ".png"));
        using var reader = new BinaryReader(stream); stream.Position = 16;
        int BigInt() => System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32());
        Check(BigInt() == item.Width * 16 && BigInt() == item.Height * 16, "Sprite size changed: " + item.Id);
    }
}
FinalParityChecks.Run(Check, args[2]);
Console.WriteLine($"PASS: {checks} food processing checks (fixture behavior, configuration, actual game metadata and sprites). No game launch.");
