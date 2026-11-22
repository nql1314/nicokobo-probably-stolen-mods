using System.Reflection;
using System.Xml.Linq;
using DseCecil;
using DseCecil.Cil;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MechcoreProtocol.SynthesisExpansion;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;
using FoodDefinitions = Nicokobo.FoodProcessing.Definitions.FoodDefinitions;
using FoodStateValue = Nicokobo.FoodProcessing.FoodStateValue;

int checks = 0;
void Check(bool condition, string message)
{ checks++; if (!condition) throw new InvalidOperationException(message); }
if (args is ["patches", var candidatePath, var gamePath])
{
    using var candidate = AssemblyDefinition.ReadAssembly(candidatePath);
    using var game = AssemblyDefinition.ReadAssembly(gamePath);
    PatchChecks.Run(candidate, game, Check);
    Console.WriteLine($"Patch checks passed: {checks}");
    return;
}
void Reject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, message);
}
RecipeDefinition Recipe(string code) => SynthesisDefinitions.Recipes.Single(recipe => recipe.Code == code);
long NativeValue(GameItem item) => (long)Math.Round(item.unitValue *
    (100m + item.itemFeatures.Sum(feature => feature.GetActualValueModifier())) / 100m, MidpointRounding.ToEven);
var document = RecipeConfiguration.Parse(RecipeConfiguration.DefaultJson());
Check(GlassRecycling.Feedstocks.All(feedstock => feedstock.Recipe.MachineId == SynthesisDefinitions.RefinerItem.Id),
    "Glass recycling must belong to the material refiner");
Check(document.Recipes.Count == 30 && SynthesisDefinitions.Recipes.Count == 26 && GlassRecycling.Feedstocks.Count == 4,
    "Full schema-26 JSON lost the machine or glass recipes");
Check(ProductionCatalog.Items.Count == 25 && ProductionCatalog.Machines.Count() == 3 &&
    ProductionCatalog.Recipes.Count() == 30 && ProductionCatalog.AllRecipes.Count() == 30, "Full manufacturing catalog is incomplete");
Check(!ProductionCatalog.Items.Select(item => item.Id).Intersect(FoodDefinitions.Items.Select(item => item.Id)).Any(),
    "Synthesis conflicts with migrated food item IDs");
Check(DefinitionValidator.Validate().Count == 0, "Preserved synthesis definitions are invalid");
var customJson = JObject.Parse(RecipeConfiguration.DefaultJson());
customJson["ProductionMarkups"]!["K02"] = 37;
var custom = RecipeConfiguration.Parse(customJson.ToString());
Check(custom.ProductionMarkups["K02"] == 37 && SynthesisDefinitions.Recipes.Count == 26, "Parsing changed active settings");
foreach (string failure in new[] { "missing", "identity", "amount", "output", "cycle" })
{
    var invalid = JObject.Parse(RecipeConfiguration.DefaultJson());
    var first = (JObject)((JArray)invalid["Recipes"]!).Single(token => (string?)token["Code"] == "P01");
    if (failure == "missing") ((JArray)invalid["Recipes"]!).Remove(first);
    if (failure == "identity") first["MachineId"] = "other.machine";
    if (failure == "amount") first["Requirements"]![0]!["Amount"] = 0;
    if (failure == "output") first["OutputItemId"] = "unknown.output";
    if (failure == "cycle") first["Requirements"]![0]!["ResourceId"] = SynthesisDefinitions.Silicon.Id;
    Reject(() => RecipeConfiguration.Parse(invalid.ToString()), "Accepted invalid configuration: " + failure);
    Check(SynthesisDefinitions.Recipes.Count == 26 && GlassRecycling.Feedstocks.Count == 4,
        "Rejected configuration leaked active state: " + failure);
}

(GameItem Machine, GameSlotInventory Battery, GameGridInventory Input, GameGridInventory Output, GameGridInventory Modules) Setup(string id)
{
    PowerHelper.RefuseDraw = PowerHelper.ThrowAfterDebit = DirectoryMaster.FailCleanup = false;
    DirectoryMaster.Created.Clear();
    var machine = new GameItem { identifier = id };
    machine.GetTagReadonly("is_machinery_powered").SetBool(true);
    machine.GetTagReadonly("power").SetInt(10);
    machine.GetTagReadonly(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).SetInt(10);
    var battery = new GameSlotInventory(); var cell = new GameItem(); cell.GetTagReadonly("energy").SetInt(100); battery.UncheckedAccept(cell);
    var modules = new GameGridInventory(); var input = new GameGridInventory();
    var output = new GameGridInventory { mayInventoryAddItemFunc = (_, _) => false };
    machine.Storages = new object?[] { battery, modules, input, output, null };
    return (machine, battery, input, output, modules);
}
GameItem Add(GameGridInventory inventory, string id, int count = 1, long value = 100, string? purity = null)
{
    var item = new GameItem { identifier = id, unitCount = count, unitValue = value };
    item.GetTagReadonly("owned").SetBool(true);
    if (purity != null) { IngotPurityHelper.InitIngotItem(item); IngotPurityHelper.SetPurity(item, purity); }
    inventory.UncheckedAccept(item); return item;
}
{
    var s = Setup(Recipe("P03").MachineId);
    var low = Add(s.Input, "metal_ingot", 2, purity: "INGOT_PURITY_FAIR");
    Check(!SynthesisProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, Recipe("P03")), "Accepted low-purity ingots");
    IngotPurityHelper.SetPurity(low, "INGOT_PURITY_PERFECT");
    Check(SynthesisProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, Recipe("P03")), "Perfect-ingot machining failed");
    var product = s.Output.childItems.Single();
    Check(IngotPurityHelper.GetPurity(product) == "INGOT_PURITY_VERY_HIGH" && NativeValue(product) == 850,
        "Zero-Quality machining amplified or discarded input value");
    long basis = product.unitValue; FoodStateValue.RestoreBasis(product); SynthesisProduction.RestoreBasis(product);
    Check(product.unitValue == basis && NativeValue(product) == 850, "Food and synthesis price patches conflict");
    var reloaded = new GameItem { identifier = product.identifier, unitValue = 999,
        state = JsonConvert.DeserializeObject<TagSystem>(JsonConvert.SerializeObject(product.state))!,
        itemFeatures = JsonConvert.DeserializeObject<List<ItemFeature>>(JsonConvert.SerializeObject(product.itemFeatures))! };
    SynthesisQuality.Restore(reloaded); SynthesisQuality.Restore(reloaded); FoodStateValue.RestoreBasis(reloaded);
    Check(reloaded.unitValue == basis && NativeValue(reloaded) == 850, "Repeated save/reload compounded purity pricing");
}
{
    var s = Setup(Recipe("P03").MachineId); s.Machine.GetTagReadonly("quality").SetInt(100);
    Add(s.Input, "metal_ingot", 1, purity: "INGOT_PURITY_PERFECT"); Add(s.Input, "metal_ingot", 2, purity: "INGOT_PURITY_PERFECT");
    Check(SynthesisProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, Recipe("P03")), "Cross-stack machining failed");
    Check(IngotPurityHelper.GetPurity(s.Output.childItems.Single()) == "INGOT_PURITY_PERFECT" &&
        NativeValue(s.Output.childItems.Single()) == 1701, "Formal Perfect-purity +200% is not applied correctly");
    Check(s.Input.childItems.Single().unitCount == 1 && s.Battery.childItem!.GetTagReadonly("energy").valueInt == 90,
        "Cross-stack batch consumed the wrong count or power");
}
foreach (string failure in new[] { "materials", "power", "output", "draw", "draw throws", "cleanup" })
{
    var s = Setup(Recipe("P03").MachineId);
    var ingots = Add(s.Input, "metal_ingot", failure == "materials" ? 1 : 2, purity: "INGOT_PURITY_HIGH");
    int beforeCount = ingots.unitCount;
    if (failure == "power") s.Battery.childItem!.GetTagReadonly("energy").SetInt(0);
    if (failure == "output") s.Output.Capacity = 0;
    if (failure is "draw" or "cleanup") PowerHelper.RefuseDraw = true;
    if (failure == "draw throws") PowerHelper.ThrowAfterDebit = true;
    if (failure == "cleanup") DirectoryMaster.FailCleanup = true;
    int energy = s.Battery.childItem!.GetTagReadonly("energy").valueInt;
    bool produced = false;
    try { produced = SynthesisProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, Recipe("P03")); }
    catch (InvalidOperationException) when (failure == "draw throws") { }
    catch (AggregateException) when (failure == "cleanup") { }
    Check(!produced && !ingots.Destroyed && ingots.parentInventory == s.Input && ingots.unitCount == beforeCount,
        "Material loss on " + failure);
    Check(s.Output.childItems.Count == 0 && s.Battery.childItem!.GetTagReadonly("energy").valueInt == energy,
        "Free output or power loss on " + failure);
    Check(s.Output.mayInventoryAddItemFunc!(new(), s.Output) == false, "Output admission left open on " + failure);
}
foreach (int quality in new[] { 0, 100, 200, 999 })
{
    var recipe = Recipe("P04"); var s = Setup(recipe.MachineId); s.Machine.GetTagReadonly("quality").SetInt(quality);
    foreach (var input in recipe.ItemInputs) Add(s.Input, input.ResourceId, input.Amount, 100,
        SynthesisDefinitions.CarriesPurity(input.ResourceId) ? "INGOT_PURITY_HIGH" : null);
    Check(SynthesisProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, recipe), "Advanced-circuit batch failed");
    var product = s.Output.childItems.Single();
    Check(product.itemFeatures.Single(feature => feature.identifier == ComponentQualityRules.GradeTag).GetActualValueModifier() ==
        ComponentQualityRules.ValueBonusPercent(ComponentQualityRules.FromQuality(quality)), "Wrong circuit grade");
    long value = NativeValue(product); SynthesisQuality.Restore(product); FoodStateValue.RestoreBasis(product);
    Check(NativeValue(product) == value, "Circuit grade compounded during restoration");
}
foreach (bool blast in new[] { false, true })
{
    var s = Setup(SynthesisDefinitions.RefinerItem.Id);
    if (blast) Add(s.Modules, RefinerMetalRules.BlastModuleId);
    Add(s.Input, "common_ore", 5); Add(s.Input, "scrap_metal", 6);
    var ordered = SynthesisProduction.OrderRecipes(s.Machine, s.Input).ToArray();
    var metal = ordered.First(recipe => RefinerMetalRules.IsSmelting(recipe.OutputRule));
    Check(metal.ItemInputs.Single(input => input.ResourceId == "common_ore").Amount == 1, "Balanced smelting did not prefer one ore");
    Check(RefinerMetalRules.MatchesMode(metal.OutputRule, blast), "Wrong blast-mode recipe family");
    Check(SynthesisProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, metal), "Balanced smelting failed");
    Check(s.Output.childItems.Count == (blast ? 2 : 1) && s.Output.childItems.All(item =>
        IngotPurityHelper.GetPurity(item) == "INGOT_PURITY_HIGH" && !item.IsTag(SynthesisProduction.BasisTag)),
        "Smelted native ingots lost their native factory value or purity");
    Check(s.Battery.childItem!.GetTagReadonly("energy").valueInt == 90, "Smelting drew power per output instead of per batch");
}
{
    var s = Setup(SynthesisDefinitions.RefinerItem.Id);
    s.Machine.GetTagReadonly("quality").SetInt(100);
    Add(s.Modules, RefinerMetalRules.BlastModuleId);
    Add(s.Input, "common_ore", 4); Add(s.Input, "scrap_metal", 2);
    for (int batch = 0; batch < 2; batch++)
    {
        var recipe = SynthesisProduction.OrderRecipes(s.Machine, s.Input)
            .First(candidate => RefinerMetalRules.IsSmelting(candidate.OutputRule));
        Check(recipe.Code == "P21" && SynthesisProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, recipe),
            "Four ore and two scrap should run two identical blast batches");
    }
    Check(s.Input.childItems.Count == 0 && s.Output.childItems.Count == 4 &&
          s.Output.childItems.All(item => IngotPurityHelper.GetPurity(item) == "INGOT_PURITY_VERY_HIGH"),
        "Balanced blast batches should yield four very-high-purity ingots");
}
{
    var s = Setup(SynthesisDefinitions.RefinerItem.Id); Add(s.Input, "common_ore", 5); Add(s.Input, "scrap_metal", 6);
    Add(s.Input, SynthesisDefinitions.TitaniumOre.Id, 5); Add(s.Input, "metal_ingot", 5, purity: "INGOT_PURITY_PERFECT");
    Check(SynthesisProduction.OrderRecipes(s.Machine, s.Input).First().OutputItemId == SynthesisDefinitions.TitaniumAlloy.Id,
        "Balanced smelting displaced a higher-tier alloy recipe");
    Check(!SynthesisProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, Recipe("P20")), "Blast recipe ran without module");
}

// Run each migrated recipe through the actual refiner transaction.
foreach (var feedstock in GlassRecycling.Feedstocks)
foreach (string mode in new[] { "glass", "full", "power", "furnace" })
{
    var s = Setup(mode == "furnace" ? "furnace" : SynthesisDefinitions.RefinerItem.Id);
    var glass = Add(s.Input, feedstock.ItemId, feedstock.RequiredCount);
    if (mode == "full") s.Output.Capacity = 0;
    if (mode == "power") s.Battery.childItem!.GetTagReadonly("energy").SetInt(0);
    bool processed = SynthesisProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, feedstock.Recipe);
    if (mode == "glass") Check(processed && glass.Destroyed && s.Output.childItems.Count == feedstock.Recipe.BaseOutputCount &&
        s.Battery.childItem!.GetTagReadonly("energy").valueInt == 90, "Refiner glass transaction did not consume one batch");
    else Check(!processed && !glass.Destroyed && s.Output.childItems.Count == 0 &&
        s.Battery.childItem!.GetTagReadonly("energy").valueInt == (mode == "power" ? 0 : 100),
        "Rejected glass batch consumed inputs: " + mode);
}
{
    var s = Setup(SynthesisDefinitions.RefinerItem.Id);
    foreach (var feedstock in GlassRecycling.Feedstocks.Where(stock => stock.RequiredCount > 1))
        Add(s.Input, feedstock.ItemId, feedstock.RequiredCount - 1);
    Check(GlassRecycling.Feedstocks.All(feedstock => !SynthesisProduction.TryProcess(s.Machine, s.Battery, s.Input, s.Output, feedstock.Recipe)),
        "Mixed incomplete glass families were combined");
}

RefinerReceiptChecks.Run(Check);
DeviceChecks.Run(Check);
InteropProtocolChecks.Run(Check);
RareUpgradeChecks.Run(Check);

if (args.Length != 3) throw new ArgumentException("Expected candidate DLL, game DLL and mod source directory");
using (var binary = AssemblyDefinition.ReadAssembly(args[0]))
using (var game = AssemblyDefinition.ReadAssembly(args[1]))
{
    var factoryCalls = binary.MainModule.Types.Single(type => type.Name == "SynthesisItems").Methods.Single(method => method.Name == "Create")
        .Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().Where(method => method.DeclaringType.Name == "DirectoryMaster").ToArray();
    Check(factoryCalls.Any(method => method.Name == "Create") && factoryCalls.All(method => method.Name != "Item"),
        "Synthesis prototype factory allocates template UIDs or fires native permit creation hooks");

    PatchChecks.Run(binary, game, Check);
    Check(!binary.MainModule.AssemblyReferences.Any(reference => reference.Name.Contains("Il2Cpp") || reference.Name.Contains("Melon") ||
        reference.Name.Contains("Forge") || reference.Name == "System.Text.Json"), "Demo dependency remains in official candidate");
    Check(binary.MainModule.Types.Count(type => type.Interfaces.Any(entry => entry.InterfaceType.FullName == "IMod")) == 1,
        "Expected one official entry point");
    Check(!binary.MainModule.Types.Any(type => type.CustomAttributes.Any(attribute =>
        attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch" && attribute.ConstructorArguments.Any(argument =>
            argument.Value is TypeReference target && target.Name == "MachineFurnace"))),
        "Mechcore still patches the native furnace");
    Check(!binary.MainModule.Types.Any(type => type.Name is "SynthesisFurnace" or "MatrixFurnace" or "MechcoreFurnacePatch"),
        "Removed furnace and continuous-processing code remains in the candidate");
    var nativeItem = game.MainModule.Types.Single(type => type.Name == "GameItem");
    Check(nativeItem.Methods.Single(method => method.Name == "AccumulateFeatureStages").Parameters.Select(p => p.ParameterType.FullName)
        .SequenceEqual(new[] { "System.Int64&", "System.Int64&", "System.Double&", "System.Boolean" }), "Native value stages changed");
    Check(nativeItem.Methods.Single(method => method.Name == "ComposeStagedValue").Parameters.Select(p => p.ParameterType.FullName)
        .SequenceEqual(new[] { "System.Int64", "System.Int64", "System.Int64", "System.Double", "System.Int32", "System.Int32" }),
        "Native price composition changed");
    foreach (string name in new[] { "GetCurrentValue", "GetRefreshedValue", "GetBaseValue" })
        Check(nativeItem.Methods.Any(method => method.Name == name), "Price patch target missing: " + name);

    int ReadInt(Instruction instruction) => instruction.OpCode.Code switch
    {
        Code.Ldc_I4 => (int)instruction.Operand, Code.Ldc_I4_S => Convert.ToInt32(instruction.Operand),
        Code.Ldc_I4_M1 => -1, _ => int.Parse(instruction.OpCode.Code.ToString().Substring("Ldc_I4_".Length))
    };
    var purity = game.MainModule.Types.Single(type => type.Name == "IngotPurityHelper");
    var instructions = purity.Methods.Single(method => method.IsConstructor && method.IsStatic).Body.Instructions;
    int nativeGrades = 0;
    for (int index = 5; index < instructions.Count; index++)
    {
        if (instructions[index].OpCode.Code != Code.Newobj || instructions[index].Operand is not MethodReference ctor ||
            ctor.DeclaringType.Name != "IngotPurity") continue;
        string tag = (string)instructions[index - 5].Operand;
        int modifier = ReadInt(instructions[index - 3]); int stage = ReadInt(instructions[index - 2]);
        var grade = IngotPurityRules.FromNativeTag(tag);
        Check((int)grade == stage && IngotPurityRules.ValueFactorPercent(grade) == 100 + modifier,
            "Pure pricing table differs from current game: " + tag);
        nativeGrades++;
    }
    Check(nativeGrades == 6, "Native purity initializer coverage is incomplete");
    var manifest = XDocument.Load(Path.Combine(args[2], "manifest.xml")).Root!;
    Check((string?)manifest.Element("ID") == "nicokobo.mechcore_protocol" && manifest.Element("Capabilities")!
        .Elements("Capability").Any() == false &&
        manifest.Element("Assemblies")!.Elements("Assembly").Any(element => element.Value == Path.GetFileName(args[0])) &&
        manifest.Element("Prerequisites")!.Elements("Mod").Any(element => element.Value == "nicokobo.forge") &&
        (string?)manifest.Element("ModVersion") == binary.Name.Version.ToString(3), "Manifest does not match candidate");
    foreach (var item in ProductionCatalog.Items)
    {
        using var file = File.OpenRead(Path.Combine(args[2], "Textures", "Items", ProductionCatalog.SpriteKey(item) + ".png"));
        file.Position = 16; using var reader = new BinaryReader(file);
        int BigInt() => System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32());
        Check(BigInt() == item.Width * 16 && BigInt() == item.Height * 16, "Sprite dimensions changed: " + item.Id);
    }
}
FinalParityChecks.Run(Check, args[2]);
Console.WriteLine($"PASS: {checks} synthesis checks (production/rollback, K02 rare upgrade, shared work receipts, owned cybernetics, robots, drill, eye scans/power/cache, value persistence, native patch signatures and sprites). No game launch.");
