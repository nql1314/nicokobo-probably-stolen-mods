using System.Reflection;
using System.Xml.Linq;
using DseCecil;
using HarmonyLib;
using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.ModuleMatrix.Definitions;
using MechcoreProtocol.ModuleMatrix.Domain;
using SynthesisDefinitions = MechcoreProtocol.SynthesisExpansion.Definitions.SynthesisDefinitions;
using Nicokobo.ContentInterop;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
if (args is ["--localization"])
{
    ResetStore();
    NightShop.Attach();
    var localizedMod = new MatrixMod();
    localizedMod.Init(new() { ID = MechcorePackage.Id });
    localizedMod.EnableContent();
    ModHook.Register();
    FinalParityChecks.RunLocalization(Check);
    Console.WriteLine($"PASS: {checks} matrix localization checks.");
    return;
}
if(args.Length!=3) throw new ArgumentException("Expected candidate DLL, native game DLL, and Matrix source directory");
MachinePurifier.NativePurityCap = Assembly.LoadFrom(Path.GetFullPath(args[1])).GetType("MachinePurifier", true)!
    .GetMethod(nameof(MachinePurifier.GetMaxPurityCap))!.CreateDelegate<Func<int, int>>();
GameItem Item(string id) => DirectoryMaster.Item(id);
(GameItem Machine, GameGridInventory Bay) Machine(string id)
{
    var machine = new GameItem { identifier = id, onCycleEndSlotItemFunc = (_,_,_)=>{} }; machine.EnableTag("STANDARD_MACHINE_TAG");
    var bay = new GameGridInventory { Parent = machine, mayInventoryAddItemFunc = (item, _) => item.IsTag("MODULE_TYPE_UNIVERSAL") };
    machine.Storages[1] = bay; PlayerStore.Instance.Items.Add(machine); MatrixMachines.Configure(machine); return (machine, bay);
}
void ResetStore()
{
    PlayerStore.Current = new(); StoreStation.Day = 25; RNG.Success = true; RNG.Indices.Clear();
    Liquid.Liquids = new() { new() { identifier = "water", canBeRemovedByPurifier = false },
        new() { identifier = "dirt", canBeRemovedByPurifier = true }, new() { identifier = "permanent", canBeRemovedByPurifier = false } };
}
ResetStore();
NightShop.Attach();
var mod = new MatrixMod(); mod.Init(new() { ID = MechcorePackage.Id }); mod.EnableContent(); ModHook.Register();
OsmosisChecks.Run(Check);
WaterSupplyChecks.Run(Check);
ExpansionChecks.Run(Check);
PriceRollbackChecks.Run(Check);
ResetStore();
Check(DirectoryMaster.Factories.Count == 19 && ModuleIconDefinitions.All.Count == 19, "Matrix must retain 18 active definitions and the retired S26 factory");
Check(!DirectoryMaster.Factories.ContainsKey(MachineModuleDefinitions.S11Reserved.Id), "Hidden projection module was registered");
Check(NodeEffectDefinitions.All.All(effect => !ModuleEffectHelper.moduleEffects.ContainsKey(effect.Id)),
    "Fixed node abilities must stay out of the native random registry without candidate-list rewriting");
bool collision = false; try { ModHook.Register(); } catch (InvalidOperationException) { collision = true; }
Check(collision && DirectoryMaster.Factories.Count == 19, "Collision check replaced existing factories");
foreach (var icon in ModuleIconDefinitions.All)
{
    var item = Item(icon.ItemId);
    Check(item.identifier == icon.ItemId && item.unitCount == 1 && item.mayStackItemFunc == null, "Factory identity/stacking mismatch: " + item.identifier);
    if (NodeItemDefinitions.All.FirstOrDefault(node => node.Id == item.identifier) is { } node)
        Check(item.shortDescription == NodeEffectDefinitions.ByCode[node.Code].EnglishDescription,
            "Node factory has no standalone ability description: " + node.Code);
    Check(item.shape.width * 16 == icon.PixelWidth && item.shape.height * 16 == icon.PixelHeight, "Factory shape mismatch: " + item.identifier);
    Check(item.IsGameItemType("NODE") == NodeItemDefinitions.All.Any(def => def.Id == item.identifier) &&
        item.IsGameItemType("MODULE") != item.IsGameItemType("NODE"), "Node/module categories overlap: " + item.identifier);
    long value = item.unitValue; MatrixItems.Reprice(item); MatrixItems.Reprice(item);
    Check(item.unitValue == value, "Fresh module price compounded: " + item.identifier);
}
ModHook.Loot();
Check(LootRegistry.Entries.Count == 10 && LootRegistry.Entries.All(entry =>
    FixedModuleDefinitions.Scavenging.Any(item => item.Id == entry.Id) && entry.Weight == .08f), "Restricted basic-module loot pool changed");
ModHook.NightShop();
Check(PlayerStore.Instance.Stock.Count == 2 && PlayerStore.Instance.Stock.Distinct().Count() == 2, "Night shop did not add two distinct modules");
foreach (string code in new[] { "M04", "M05", "M06" })
{
    var recipe = SynthesisDefinitions.Recipes.Single(recipe => recipe.Code == code);
    var dependency = recipe.ItemInputs.Single(input => input.ResourceId.StartsWith(MatrixMod.Id + ".", StringComparison.Ordinal));
    Check(DirectoryMaster.Has<GameItem>(dependency.ResourceId) && ContinuousModuleDefinitions.ById.ContainsKey(dependency.ResourceId),
        "Synthesis prerequisite still unavailable: " + code);
}
var actualModuleIds = ModuleIconDefinitions.All.Select(icon => icon.ItemId).ToHashSet();
for (int i = 0; i < 3; i++)
{
    int tier = i; int step = 0;
    var chosen = NightShopModuleSelection.Draw(actualModuleIds, count => step++ == 0 ? tier : 0);
    Check(chosen.Count == 2 && chosen[0] != chosen[1], "Tiered night stock duplicated a module");
}
Check(ModuleEffectHelper.moduleEffects.Keys.SequenceEqual(PlacementEffectDefinitions.All.Select(def => def.Id)),
    "Enabled placement affixes must remain native random candidates");
var savedNode = new GameItem { identifier = NodeItemDefinitions.N09.Id };
savedNode.ModifyTag("MODULE_EFFECT1_TAG", tag => tag.Enable().SetString(NodeEffectDefinitions.N09.Id));
var savedNodeText = new RichTextBuilder();
Check(FixedModuleAbilities.CreateTooltip(savedNodeText, savedNode) && savedNodeText.Lines.Contains(NodeEffectDefinitions.N09.EnglishName),
    "A legacy fixed-node effect tag lost its tooltip in the independent ability catalog");

// Real callback sequencing: fixed base -> summed placement -> isolation -> conversion -> aggregate.
ResetStore();
var placed = Machine("test_machine");
var source = Item(FixedModuleDefinitions.M01.Id); placed.Bay.Add(source, 0, 0);
ModuleEffectHelper.AddEffectToModule(source, PlacementEffectDefinitions.E01.Id);
ModuleEffectHelper.AddEffectToModule(source, PlacementEffectDefinitions.E03.Id);
ModuleEffectHelper.ComputeAllModuleTempStat(placed.Bay.childItems);
Check(MatrixEffects.Temp(source) == new NodeStats(11, 11, 5), "Two placement percentages were not summed before truncation");
ModuleEffectHelper.ComputeAllModuleTempStat(placed.Bay.childItems);
Check(MatrixEffects.Temp(source) == new NodeStats(11, 11, 5), "Placement compounded on repeated aggregation");
source.modifiedShape = new GridShapeBuilder(source.shape).SetTransform(2, 2, false, 0).Build();
ModuleEffectHelper.ComputeAllModuleTempStat(placed.Bay.childItems);
Check(MatrixEffects.Temp(source) == new NodeStats(10, 10, 5), "Corner bonus survived moving off bay edge");
var rotated = new GridShapeBuilder(source.shape).SetTransform(5, 0, true, 1).Build(); source.modifiedShape = rotated;
Check(MatrixGeometry.Layout(source) is { } right && PlacementEffectRules.Analyze(right).TouchesCorner, "Rotation/flip lost bay edge geometry");
var hole = new HashSet<GridCell>(); for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) if (x != 1 || y != 1) hole.Add(new(x,y));
Check(PlacementEffectRules.Analyze(new(hole, new HashSet<GridCell> { new(0,1) }, new Dictionary<GridCell,string>())).TouchesRight == false,
    "Internal blocked cell was treated as exterior edge");
ResetStore(); placed = Machine("test_machine");
source = Item(FixedModuleDefinitions.M01.Id); placed.Bay.Add(source, 2, 1);
var isolation = Item(NodeItemDefinitions.N12.Id); placed.Bay.Add(isolation, 1, 1); MatrixEffects.Arriving(isolation, placed.Bay);
var conversion = Item(NodeItemDefinitions.N09.Id); placed.Bay.Add(conversion, 4, 1); MatrixEffects.Arriving(conversion, placed.Bay);
var laterConversion = Item(NodeItemDefinitions.N11.Id); placed.Bay.Add(laterConversion, 2, 0); MatrixEffects.Arriving(laterConversion, placed.Bay);
ModuleEffectHelper.ComputeAllModuleTempStat(placed.Bay.childItems);
Check(MatrixEffects.Temp(source) == new NodeStats(6, 6, 3) && MatrixEffects.Temp(conversion) == new NodeStats(-6, 3, 1) &&
    MatrixEffects.Temp(laterConversion) == default, "Isolation/conversion order or first-arrival claim changed");
placed.Bay.childItems.Reverse(); ModuleEffectHelper.ComputeAllModuleTempStat(placed.Bay.childItems);
Check(MatrixEffects.Temp(conversion) == new NodeStats(-6, 3, 1), "Grid enumeration order replaced persisted arrival priority");
placed.Bay.Expel(conversion); MatrixEffects.Removed(conversion); placed.Bay.Add(conversion, 4, 1); MatrixEffects.Arriving(conversion, placed.Bay);
ModuleEffectHelper.ComputeAllModuleTempStat(placed.Bay.childItems);
Check(MatrixEffects.Temp(laterConversion) == new NodeStats(3, 3, -3) && MatrixEffects.Temp(conversion) == default,
    "Removed/reinserted conversion node retained old priority");
ModHook.Loaded();
Check(MatrixEffects.Temp(source) == new NodeStats(6, 6, 3), "Loaded recomputation accumulated node effects");

foreach (var definition in ContinuousModuleDefinitions.All)
{
    ResetStore(); placed = Machine("test_machine"); var module = Item(definition.Module.Id); placed.Bay.Add(module, 2, 1);
    string tag = "BONUS_PERCENTAGE_" + definition.Attribute + "_INT";
    MatrixGrowth.Work(module, placed.Bay, placed.Machine);
    Check(module.GetTagReadonly(tag).GetInt() == definition.Initial + definition.Step, "Continuous module did not grow after work");
    var learn = Item(NodeItemDefinitions.N16.Id); placed.Bay.Add(learn, 1, 1);
    MatrixGrowth.Work(module, placed.Bay, placed.Machine);
    Check(module.GetTagReadonly(tag).GetInt() == definition.Initial + 3 * definition.Step, "Learning node did not double continuous growth");
    ModHook.Night();
    Check(module.GetTagReadonly(tag).GetInt() == definition.Initial + 3 * definition.Step, "Worked module decayed during the same night");
    StoreStation.Day++; ModHook.Night(); ModHook.Night();
    Check(module.GetTagReadonly(tag).GetInt() == definition.Initial + 2 * definition.Step, "Idle decay skipped or repeated the same day");
    string persisted = Newtonsoft.Json.JsonConvert.SerializeObject(module.state);
    module.state = Newtonsoft.Json.JsonConvert.DeserializeObject<TagSystem>(persisted)!; ModHook.Loaded(); ModHook.Night();
    Check(module.GetTagReadonly(tag).GetInt() == definition.Initial + 2 * definition.Step, "Reload repeated idle decay");
    for (int i = 0; i < 40; i++) MatrixGrowth.Work(module, placed.Bay, placed.Machine);
    Check(module.GetTagReadonly(tag).GetInt() == definition.Cap, "Continuous growth exceeded total cap");
    module.Owned = false; module.ModifyTag(tag, state => state.SetInt(definition.Initial)); MatrixGrowth.Work(module, placed.Bay, placed.Machine);
    Check(module.GetTagReadonly(tag).GetInt() == definition.Initial, "Unowned module grew");
}
Check(NodeEffectRules.Isolate(new(-3,-1,0)) == default && NodeEffectRules.ConversionContribution("N09", new(3,0,0)) == new NodeStats(-3,1,0),
    "Node negative/truncation boundaries changed");
Check(ContinuousGrowthRules.Decay(3, 4, 1) == 0 && ContinuousGrowthRules.Advance(23,4,24,2) == 24, "Growth/decay boundaries changed");

ResetStore(); placed = Machine("test_machine");
source = Item(FixedModuleDefinitions.M01.Id); placed.Bay.Add(source, 2, 1);
var learningNode = Item(NodeItemDefinitions.N16.Id); placed.Bay.Add(learningNode, 1, 1);
source.ModifyTag("BONUS_PERCENTAGE_PERFORMANCE_INT", tag => tag.SetInt(3));
source.ModifyTag("LEARNING_ALGO_MAX_PERFORMANCE_INT", tag => tag.SetInt(5));
object?[] snapshot = { source, null }; Ports.Hook(typeof(MatrixLearningPatch), "Prefix", snapshot);
Check(snapshot[1]==null,"Learning acceleration admitted a module without the native learning effect");
ModuleEffectHelper.AddEffectToModule(source,NodeEffectDefinitions.NativeLearning);
Ports.Hook(typeof(MatrixLearningPatch), "Prefix", snapshot);
source.ModifyTag("BONUS_PERCENTAGE_PERFORMANCE_INT", tag => tag.SetInt(4));
Ports.Hook(typeof(MatrixLearningPatch), "Postfix", source, snapshot[1]);
Check(source.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt() == 5, "Native learning was not topped up to its existing cap");

ResetStore();
foreach (var definition in MachineModuleDefinitions.All)
{
    placed = Machine(definition.MachineId); var module = Item(definition.Id);
    Check(placed.Bay.mayInventoryAddItemFunc!(module, placed.Bay) == !definition.Retired,
        "Machine bay admission disagrees with module retirement: " + definition.Code);
    var other = Machine("other_machine"); Check(!other.Bay.mayInventoryAddItemFunc!(module, other.Bay), "Machine module accepted by the wrong machine");
    module.Owned = false; Check(!placed.Bay.mayInventoryAddItemFunc!(module, placed.Bay), "Unowned machine module admitted");
}
// Preserve native admission while filtering the exact custom module types.
var nativeAllowed = new GameItem(); nativeAllowed.EnableTag("MODULE_TYPE_UNIVERSAL");
Check(placed.Bay.mayInventoryAddItemFunc!(nativeAllowed, placed.Bay), "Native bay admission was lost");
ResetStore(); placed = Machine("water_purifier");
var osmosis = Item(MachineModuleDefinitions.All.Single(def => def.Code == "S24").Id); placed.Bay.Add(osmosis);
var water = new GameItem(); water.EnableTag("LIQUID_CONTAINER_TAG");
WaterHelper.AddLiquid(water,"water",20); WaterHelper.AddLiquid(water,"dirt",80); WaterHelper.AddLiquid(water,"permanent",10);
int removed = MatrixMachines.Purify(placed.Machine, water);
Check(removed == 20 && WaterHelper.GetTotalVolume(water) == 110 && water.GetTagReadonly("liquid:permanent").GetInt() == 10,
    "Osmosis lost liquid volume or removed an unsupported contaminant");
placed.Bay.Add(Item(osmosis.identifier));
Check(MatrixMachines.Purify(placed.Machine,water) == OsmosisRules.ExtraRemoval(60,2), "Osmosis stacks were added instead of filtering remaining amounts");

ResetStore(); var farm = Machine("moisture_farm"); farm.Bay.Add(Item(MachineModuleDefinitions.All.Single(def=>def.Code=="S06").Id));
var room = new GameGridInventory(10,10); room.Add(farm.Machine,2,2);
var farmWater = new GameItem(); farmWater.EnableTag("LIQUID_CONTAINER_TAG"); var farmSlot = new GameSlotInventory { Parent = farm.Machine }; farm.Machine.Storages[2] = farmSlot; farmSlot.Add(farmWater);
WaterHelper.AddLiquid(farmWater,"water",100);
var purifier = Machine("water_purifier").Machine; room.Add(purifier,3,2);
var purifierWater = new GameItem(); purifierWater.EnableTag("LIQUID_CONTAINER_TAG"); purifierWater.GetTagReadonly("capacity").SetInt(70);
var purifierSlot = new GameSlotInventory { Parent = purifier }; purifier.Storages[2] = purifierSlot; purifierSlot.Add(purifierWater);
var spare = new GameItem(); spare.EnableTag("LIQUID_CONTAINER_TAG"); spare.GetTagReadonly("capacity").SetInt(100); room.Add(spare,2,3);
MatrixMachines.Supply(farmWater,100);
Check(WaterHelper.GetTotalVolume(purifierWater)==70 && WaterHelper.GetTotalVolume(spare)==30 && WaterHelper.GetTotalVolume(farmWater)==0,
    "Water supply did not prefer adjacent purifier containers or conserve volume");

ResetStore(); placed = Machine("desequencer"); var cache = Item(MachineModuleDefinitions.All.Single(def=>def.Code=="S09").Id); placed.Bay.Add(cache);
placed.Machine.ModifyTag("DESEQUENCER_TARGET_ID_TAG", tag=>tag.Enable().SetString("keycard"));
var inputSlot = new GameSlotInventory { Parent = placed.Machine }; var outputSlot = new GameSlotInventory { Parent = placed.Machine };
var blank = new GameItem { identifier = "blank" }; inputSlot.Add(blank);
object?[] decode = { placed.Machine, inputSlot, outputSlot, null }; Ports.Hook(typeof(MatrixDecodeFinishPatch),"Prefix",decode);
Ports.Hook(typeof(MatrixDecodeFinishPatch),"Postfix",placed.Machine,inputSlot,outputSlot,decode[3]);
Check(!MatrixMachines.CacheHit(placed.Machine),"Rejected decode entered the cache");
inputSlot.Expel(blank); outputSlot.Add(new() { identifier="keycard" });
Ports.Hook(typeof(MatrixDecodeFinishPatch),"Postfix",placed.Machine,inputSlot,outputSlot,decode[3]);
Check(MatrixMachines.CacheHit(placed.Machine),"Successful native decode did not enter the cache");
var secondCache=Item(cache.identifier); secondCache.ModifyTag(MatrixMachines.CacheTag,tag=>tag.Enable().SetString("keycard")); placed.Bay.Add(secondCache);
cache.ModifyTag(MatrixMachines.CacheTag,tag=>tag.SetString("other"));
Check(!MatrixMachines.CacheHit(placed.Machine),"Additional cache modules changed the original first-module selection");
cache.ModifyTag(MatrixMachines.CacheTag,tag=>tag.SetString("keycard"));
placed.Machine.GetTagReadonly("CURRENT_PROCESSING_SPEED_TAG").SetInt(33);
object?[] progress = { placed.Machine, null }; Ports.Hook(typeof(MatrixDecodeProgressPatch),"Prefix",progress);
Check(placed.Machine.GetTagReadonly("CURRENT_PROCESSING_SPEED_TAG").GetInt()==66,"Matching cached target did not double speed");
var failure = new InvalidOperationException("native failure");
Check(ReferenceEquals(Ports.Hook(typeof(MatrixDecodeProgressPatch),"Finalizer",placed.Machine,progress[1],failure),failure) &&
    placed.Machine.GetTagReadonly("CURRENT_PROCESSING_SPEED_TAG").GetInt()==33,"Cache speed was not restored or native exception was swallowed");
ResetStore(); placed = Machine("security_alarm"); placed.Bay.Add(Item(MachineModuleDefinitions.All.Single(def=>def.Code=="S25").Id));
MatrixMachines.Market(); MatrixMachines.Market();
Check(PlayerStore.Instance.NightLog.Count==1,"Market tracking attempted twice on the same day");
StoreStation.Day++; RNG.Success=false; MatrixMachines.Market();
Check(PlayerStore.Instance.NightLog.Count==2 && PlayerStore.Instance.NightLog[1].Contains("no information"),"Market tracking miss was not recorded");

FurnaceRemovalChecks.Run(Check);

TurboChecks.Run(Check);
using(var candidate=AssemblyDefinition.ReadAssembly(args[0]))
using(var game=AssemblyDefinition.ReadAssembly(args[1]))
{
    MetadataChecks.Run(candidate,game,Check);
    var manifest=XDocument.Load(Path.Combine(args[2],"manifest.xml")).Root!;
    Check((string?)manifest.Element("ID")==MechcorePackage.Id && manifest.Element("Capabilities")!
        .Elements("Capability").Any() == false &&
        (string?)manifest.Element("ModVersion")==candidate.Name.Version.ToString(3) &&
        manifest.Element("Assemblies")!.Elements("Assembly").First().Value==Path.GetFileName(args[0]) &&
        manifest.Element("Prerequisites")!.Elements("Mod").Any(element=>element.Value=="nicokobo.forge"),"Manifest/assembly mismatch");
    foreach(var icon in ModuleIconDefinitions.All)
    {
        using var file=File.OpenRead(Path.Combine(args[2],"Textures","Items",icon.SpriteKey+".png")); file.Position=16;
        using var reader=new BinaryReader(file);
        Check(System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32())==icon.PixelWidth &&
            System.Net.IPAddress.NetworkToHostOrder(reader.ReadInt32())==icon.PixelHeight,"Official sprite dimensions mismatch: "+icon.SpriteKey);
    }
}
FinalParityChecks.Run(Check, args[2]);
mod.DisableContent(); int stock=PlayerStore.Instance.Stock.Count; ModHook.NightShop();
Check(!MatrixMod.Active && ModuleEffectHelper.moduleEffects.Count==0 && PlayerStore.Instance.Stock.Count==stock,"Disable did not detach owned hooks/effects");
DirectoryMaster.Factories.Clear();
string[] categoryFlags = { "EnableP1FixedModules", "EnableP2PlacementSlice", "EnableP4ContinuousModules", "EnableNodeItems", "EnableMachineModules" };
foreach (string flag in categoryFlags) ModConfig.Flags[flag] = false;
mod.Init(new() { ID = MechcorePackage.Id }); mod.EnableContent(); ModHook.Register();
Check(DirectoryMaster.Factories.Count == 19 && NodeEffectDefinitions.All.All(effect => FixedModuleAbilities.Contains(effect.Id)) &&
    ModuleIconDefinitions.All.All(icon => DirectoryMaster.Has<GameItem>(icon.ItemId)), "Disabled categories lost their stable item/effect definitions");
Check(ModuleIconDefinitions.All.All(icon => !MatrixMod.Enabled(icon.ItemId)) && MatrixInterop.RarePool().All(id => !ModuleIconDefinitions.ByItemId.ContainsKey(id)),
    "Disabled Matrix items remain available as new upgrade outputs");
Check(ModuleEffectHelper.moduleEffects.Count == 0,
    "Disabled placement or fixed node effects leaked into new random rolls");
var oldPlacement = new GameItem { identifier = "native-module-with-saved-affix" };
oldPlacement.ModifyTag("MODULE_EFFECT1_TAG", tag => tag.Enable().SetString(PlacementEffectDefinitions.E01.Id));
var oldPlacementText = new RichTextBuilder();
Check(FixedModuleAbilities.CreateTooltip(oldPlacementText, oldPlacement) && oldPlacementText.Lines.Contains(PlacementEffectDefinitions.E01.EnglishName)
    && oldPlacement.GetTagReadonly("MODULE_EFFECT1_TAG").GetString() == PlacementEffectDefinitions.E01.Id,
    "Disabling random placement affixes erased a saved slot or its tooltip");
ResetStore(); placed = Machine("test_machine");
var disabledDefinition = ContinuousModuleDefinitions.All[0];
var oldContinuous = Item(disabledDefinition.Module.Id); placed.Bay.Add(oldContinuous, 0, 0);
string growthTag = "BONUS_PERCENTAGE_" + disabledDefinition.Attribute + "_INT";
int savedGrowth = disabledDefinition.Initial + 9;
oldContinuous.ModifyTag(growthTag, tag => tag.SetInt(savedGrowth));
var oldNode = Item(NodeItemDefinitions.N09.Id); placed.Bay.Add(oldNode, 1, 0);
oldNode.ModifyTag(MatrixEffects.ArrivalTag, tag => tag.Enable().SetInt(7));
int disabledActions = 0;
ModuleEffectHelper.moduleEffects["checks.disabled"] = new() { onAnyAction = (_, _) => disabledActions++, onUsedAction = _ => disabledActions++ };
ModuleEffectHelper.AddEffectToModule(oldContinuous, "checks.disabled");
var nativeSupport = new GameItem { identifier = "native-support" }; ModuleEffectHelper.InitNode(nativeSupport); placed.Bay.Add(nativeSupport, 2, 0);
ModuleEffectHelper.moduleEffects["checks.support"] = new() { onAnyAction = (item, _) =>
    MatrixEffects.WriteTemp(item, new(oldContinuous.GetTagReadonly(growthTag).GetInt() / 5, 0, 0)) };
ModuleEffectHelper.AddEffectToModule(nativeSupport, "checks.support");
MatrixMachines.Recompute(placed.Machine);
ModuleEffectHelper.ActivateOnUseAction(oldContinuous, placed.Bay, placed.Machine);
Check(disabledActions == 0 && MatrixEffects.Temp(oldContinuous) == default && MatrixEffects.Temp(oldNode) == default &&
    placed.Machine.GetTagReadonly("TOTAL_PERFORMANCE").GetInt() == 0,
    "Disabled base, growth or affixes leaked directly or through a native neighbour effect");
Check(oldContinuous.GetTagReadonly(growthTag).GetInt() == savedGrowth && oldNode.GetTagReadonly(MatrixEffects.ArrivalTag).GetInt() == 7 &&
    placed.Bay.childItems.Contains(oldContinuous), "Suppressing a category changed saved growth, arrival order or inventory");
ModuleEffectHelper.moduleEffects["checks.support"].onAnyAction = (_, _) => throw new InvalidOperationException("callback failure");
bool callbackFailed = false; try { MatrixMachines.Recompute(placed.Machine); } catch (InvalidOperationException) { callbackFailed = true; }
Check(callbackFailed && oldContinuous.GetTagReadonly(growthTag).GetInt() == savedGrowth, "A failing native callback left saved growth masked");
ModuleEffectHelper.moduleEffects.Remove("checks.support"); ModuleEffectHelper.moduleEffects.Remove("checks.disabled");
foreach (string flag in categoryFlags) ModConfig.Flags[flag] = true;
mod.Init(new() { ID = MechcorePackage.Id }); MatrixMachines.Recompute(placed.Machine);
Check(oldContinuous.GetTagReadonly(growthTag).GetInt() == savedGrowth && MatrixEffects.Temp(oldContinuous) != default &&
    oldNode.GetTagReadonly(MatrixEffects.ArrivalTag).GetInt() == 7, "Re-enabling a category failed to restore its existing growth and order");
mod.DisableContent();
Console.WriteLine($"PASS: {checks} Matrix checks (real factories/lifecycle, placement/nodes/growth, machine effects, furnace removal, synthesis materials, native metadata and sprites). No game launch.");
