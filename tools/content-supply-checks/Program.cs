using System.Reflection;
using DseCecil;
using Nicokobo.ContentInterop;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
void Reject(Action action, string message)
{ bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } Check(rejected, message); }
GameItem Item(string id, string type = "FOOD", int count = 1) => new() { identifier = id, unitCount = count, Types = new() { type } };
void AddFactory(string id, string type = "FOOD") => DirectoryMaster.Factories[id] = () => Item(id, type);
void Register(string owner, params StockOffer[] offers)
{
    ContentRuntime.Owners.Add(owner); SupplyCatalog.Register(owner, offers);
    foreach (var offer in offers) AddFactory(offer.ItemId, offer.Category == StockCategory.Module ? "MODULE" : "FOOD");
}
void Reset()
{
    foreach (string owner in ContentRuntime.Owners.ToArray()) SupplyCatalog.Remove(owner);
    ContentRuntime.Owners.Clear(); DirectoryMaster.Factories.Clear(); DirectoryMaster.Created.Clear();
    PlayerStore.Current = new(); StoreStation.Day = 25; RNG.Samples.Clear(); RNG.Calls = 0;
    LootRegistry.Tables.Clear();
}
object? Hook(string name, params object?[] args) => typeof(NativeSupplyHooks).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

Reset();
Reject(() => SupplyCatalog.Register("missing", new[] { new StockOffer("x", StockCategory.Food) }), "Unattached provider registered stock");
Register("food", new("food.a", StockCategory.Food), new("food.b", StockCategory.Food));
Register("synthesis", new("synthesis.material", StockCategory.Material) { MinimumDay = 22 },
    new("synthesis.quartz", StockCategory.Ore,125) { MinerWeight=.5f, PreserveLootWeight=true },
    new("synthesis.titanium", StockCategory.Ore,125) { MinerWeight=.5f, PreserveLootWeight=true });
Register("matrix", new StockOffer("matrix.module", StockCategory.Module) { Suppliers=StockSupplier.Thief|StockSupplier.Inventor, IncludeInNightShop=false });
Check(SupplyCatalog.Available(StockCategory.Food).Count==2,"Food supply catalog mismatch");
StoreStation.Day=21; Check(SupplyCatalog.Available(StockCategory.Material).Count==0,"Refined material appeared before day22");
StoreStation.Day=22; Check(SupplyCatalog.Available(StockCategory.Material).Count==1,"Refined material not unlocked on day22");
Check(SupplyCatalog.Available(StockCategory.Module).Count==0 && SupplyCatalog.Available(StockCategory.Module,StockSupplier.Thief).Count==1 &&
    SupplyCatalog.Available(StockCategory.Module,StockSupplier.Inventor,true).Count==0,"Matrix supplier/night restrictions changed");
var unique=new StockOffer("unique",StockCategory.Food){SkipWhenOwned=true}; Register("unique-provider",unique);
unique.SkipWhenOwned=false; PlayerStore.Instance.Items.Add(new(){identifier="unique",Owned=true});
Check(!SupplyCatalog.Available(StockCategory.Food).Any(offer=>offer.ItemId=="unique"),"Catalog retained mutable caller declaration or ignored owned item");
PlayerStore.Instance.Items[0].unitCount=0;
Check(SupplyCatalog.Available(StockCategory.Food).Any(offer=>offer.ItemId=="unique"),"Zero-quantity owned item blocked supply");
DirectoryMaster.Factories.Remove("unique");
Check(!SupplyCatalog.Available(StockCategory.Food).Any(offer=>offer.ItemId=="unique"),"Unregistered item remained eligible");
Reject(()=>SupplyCatalog.Register("matrix",new[]{new StockOffer("food.a",StockCategory.Module)}),"Foreign stock ID collision accepted");
foreach(float weight in new[]{0,-1,float.NaN,float.PositiveInfinity})
    Reject(()=>SupplyCatalog.Register("food",new[]{new StockOffer("bad",StockCategory.Food,weight)}),"Invalid stock weight accepted");
Check(SupplyCatalog.Available(StockCategory.Food).Count==2,"Rejected catalog update erased valid offers");

// Every provider shares one quarter of the retained table's weight.
var general=new SupplySession(StockCategory.All);
var native=new List<LootEntry>{new("native.food",100),new("food.a",.125f,"food"),new("food.machine",.05f,"food"),new("foreign",20,"another-mod")};
var merged=SupplyEngine.Merge(general,"packedFoodTable",native);
Check(native.Count==4 && native[1].weight==.125f && !ReferenceEquals(native,merged),"NPC merge mutated the global/native list");
Check(merged.Single(entry=>entry.id=="foreign").weight==20 && !merged.Any(entry=>entry.id=="food.machine"),"Merge lost unrelated stock or retained disallowed owned loot");
Check(Math.Abs(merged.Where(entry=>entry.sourceModId=="food").Sum(entry=>entry.weight)-30f)<1e-7,"One provider did not share a quarter of the retained weight");
Register("food-extra",new("food.c",StockCategory.Food),new("food.d",StockCategory.Food),new("food.e",StockCategory.Food));
merged=SupplyEngine.Merge(general,"packedFoodTable",native);
Check(Math.Abs(merged.Where(entry=>entry.sourceModId is "food" or "food-extra").Sum(entry=>entry.weight)-30f)<1e-5,
    "Multiple providers multiplied the Mod supply budget");
var mineralTable=new List<LootEntry>{new("common_ore",125),new("synthesis.quartz",125,"synthesis"),new("synthesis.titanium",125,"synthesis")};
var minerals=SupplyEngine.Merge(general,"materialTable",mineralTable);
Check(minerals.Where(entry=>entry.id is "common_ore" or "synthesis.quartz" or "synthesis.titanium").All(entry=>entry.weight==125) &&
    minerals.Single(entry=>entry.id=="synthesis.material").weight==93.75f,"Ore native-table weights were replaced by the generic budget");
var nativeScale = SupplyEngine.Merge(general, "packedFoodTable", new List<LootEntry>{new("native.food",1000)});
double nativeScaleMods = nativeScale.Where(entry=>entry.sourceModId is "food" or "food-extra").Sum(entry=>(double)entry.weight);
Check(Math.Abs(nativeScaleMods / nativeScale.Sum(entry=>(double)entry.weight) - .2d) < 1e-6,
    "A native 1000-weight table does not yield the shared 20% Mod share");
var ordinaryModule=new List<LootEntry>{new("native.module",1),new("matrix.module",.08f,"matrix")};
Check(!SupplyEngine.Merge(general,"allModuleTable",ordinaryModule).Any(entry=>entry.id=="matrix.module"),"Matrix modules entered an unapproved supplier pool");
Check(SupplyEngine.Merge(new(StockCategory.Module,StockSupplier.Thief),"allModuleTable",ordinaryModule)
    .Single(entry=>entry.id=="matrix.module").weight==.25f,"Thief module pool did not use the shared budget");
Check(!SupplyEngine.Merge(new(StockCategory.Module,StockSupplier.Inventor,nightShop:true),"allModuleTable",ordinaryModule)
    .Any(entry=>entry.id=="matrix.module"),"Matrix entered Jackson's original night slots");

var candidates=SupplyCatalog.Available(StockCategory.Food);
Check(SupplyPolicy.SelectDirect(candidates,0)==null && SupplyPolicy.SelectDirect(candidates,.799999)==null &&
    SupplyPolicy.SelectDirect(candidates,.8)==candidates[0],"Direct native-versus-Mod probability boundary changed");
Check(SupplyPolicy.SelectDirect(candidates,.999999)==candidates[^1] && SupplyPolicy.SelectDirect(candidates,double.NaN)==null,"Direct pool upper/invalid boundary changed");
var ores=SupplyCatalog.Available(StockCategory.Ore);
Check(SupplyPolicy.SelectOre(ores,.699999)=="common_ore" && SupplyPolicy.SelectOre(ores,.700001)=="synthesis.quartz" &&
    SupplyPolicy.SelectOre(ores,.849999)=="synthesis.quartz" && SupplyPolicy.SelectOre(ores,.850001)=="synthesis.titanium", "Miner 70/15/15 selection changed");

// Named specialist items are protected; only actual random module/node factories
// create direct replacement slots. Purpose categories outrank household labels.
foreach(string type in new[]{"WEAPON","AMMUNITION","ARMOR","TOOL","MACHINE","STORAGE","ACCESS_CARD","DOCUMENT"})
    Check(SupplyEngine.DirectCategory(general,Item("protected",type))==StockCategory.None,"Protected purpose type admitted: "+type);
foreach(string tag in new[]{"IMPORTANT_TAG","NOT_FOR_RESALE","CONTAINER_TAG","LIQUID_CONTAINER_TAG"})
{
    var protectedItem=Item("protected"); protectedItem.Tags.Add(tag);
    Check(SupplyEngine.DirectCategory(general,protectedItem)==StockCategory.None,"Protected tag admitted: "+tag);
}
var namedModule=Item("furnace_module_blast","MODULE");
Check(SupplyEngine.DirectCategory(general,namedModule)==StockCategory.None,"Named specialist module was a random replacement slot");
var randomModule=Item("system_module_performance","MODULE");
SupplyEngine.Spawned(general,"random_performance_module",randomModule);
Check(SupplyEngine.DirectCategory(general,randomModule)==StockCategory.Module && SupplyEngine.DirectCategory(general,randomModule)==StockCategory.None,
    "Random module slot eligibility was missing or reusable");
var medical=Item("blood","MEDICAL"); medical.Types.Add("HOUSEHOLD_GOOD");
Check(SupplyEngine.DirectCategory(general,medical)==StockCategory.Medical,"Medical item was reclassified as household");

var pooled=Item("native.food"); SupplyEngine.Rolled(general,"packedFoodTable",pooled.identifier); SupplyEngine.PoolItem(general,pooled);
int beforeCalls=RNG.Calls; var pooledOriginal=pooled;
Check(SupplyEngine.Replace(general,ref pooled,false) && pooled==pooledOriginal && RNG.Calls==beforeCalls,"Table stock was rolled a second time during placement");
var aliasSession=new SupplySession(StockCategory.Module,StockSupplier.Thief);
SupplyEngine.Rolled(aliasSession,"allModuleTable","random_performance_module");
var aliasModule=Item("system_module_performance","MODULE"); SupplyEngine.Spawned(aliasSession,"random_performance_module",aliasModule);
beforeCalls=RNG.Calls;
Check(SupplyEngine.Replace(aliasSession,ref aliasModule,false) && RNG.Calls==beforeCalls,"Table alias was rolled a second time using its resolved native ID");
var crateSession=new SupplySession(StockCategory.Food,repeatPoolDraw:true);
SupplyEngine.Rolled(crateSession,"packedFoodTable","native.crate.food"); beforeCalls=RNG.Calls;
for(int i=0;i<7;i++)
{
    var crateFood=Item("native.crate.food"); SupplyEngine.Spawned(crateSession,"native.crate.food",crateFood);
    Check(SupplyEngine.Replace(crateSession,ref crateFood,false) && crateFood.identifier=="native.crate.food","A cloned crate item was redrawn after the batch's native pool selection");
}
Check(RNG.Calls==beforeCalls,"Cloned crate stock performed extra supply draws");
var own=Item("food.a"); beforeCalls=RNG.Calls;
Check(SupplyEngine.Replace(general,ref own,false) && RNG.Calls==beforeCalls,"Existing custom stock was rolled again");
var original=Item("native.food",count:7); var replacedOriginal=original; RNG.Samples.Enqueue(.99);
Check(SupplyEngine.Replace(general,ref original,false) && original!=replacedOriginal && original.unitCount==7 && replacedOriginal.DestroyCalls==1,
    "Direct replacement lost quantity or did not dispose the original detached stock");
var inventory=new GameInventory(); inventory.Add(original); var attached=original; beforeCalls=RNG.Calls;
Check(SupplyEngine.Replace(general,ref attached,false) && attached==original && original.DestroyCalls==0 && RNG.Calls==beforeCalls,"Attached stock was replaced or destroyed");
original=Item("native.food"); var owned=original; beforeCalls=RNG.Calls;
Check(SupplyEngine.Replace(general,ref original,true) && original==owned && RNG.Calls==beforeCalls,"Player-owned giveaway stock was replaced");

Reset(); Register("provider",new StockOffer("replacement",StockCategory.Food));
foreach(string failure in new[]{"throw","identity","count","attached"})
{
    var holding=new GameInventory(); GameItem? failed=null;
    DirectoryMaster.Factories["replacement"]=()=>
    {
        if(failure=="throw") throw new InvalidOperationException("factory failure");
        failed=Item(failure=="identity"?"wrong":"replacement"); failed.DeclineSetCount=failure=="count";
        if(failure=="attached") holding.Add(failed);
        return failed;
    };
    var stock=Item("native",count:4); var retained=stock; RNG.Samples.Enqueue(.99);
    Check(SupplyEngine.Replace(general,ref stock,false) && stock==retained && stock.unitCount==4 && stock.DestroyCalls==0,
        "Failed direct replacement did not retain native stock: "+failure);
    if(failed!=null) Check(failed.DestroyCalls==(failure=="attached"?0:1),"Failed replacement cleanup unsafe: "+failure);
}
Reset(); Register("synthesis",new("quartz",StockCategory.Ore,125){MinerWeight=.5f,PreserveLootWeight=true},
    new("titanium",StockCategory.Ore,125){MinerWeight=.5f,PreserveLootWeight=true});
var miner=new SupplySession(StockCategory.Ore,miner:true); RNG.Samples.Enqueue(.95);
for(int i=0;i<4;i++)
{
    var stock=Item("common_ore","MATERIAL",i+1);
    Check(SupplyEngine.Replace(miner,ref stock,false) && stock.identifier=="titanium" && stock.unitCount==i+1,"Miner batch type/quantity changed within one visit");
}
Check(RNG.Calls==1,"Miner rolled each ore slot instead of once per batch");
var failingMiner=new SupplySession(StockCategory.Ore,miner:true); RNG.Samples.Enqueue(.95);
DirectoryMaster.Factories["titanium"]=()=>throw new InvalidOperationException("missing ore factory");
for(int i=0;i<2;i++)
{
    var stock=Item("common_ore","MATERIAL");
    Check(!SupplyEngine.Replace(failingMiner,ref stock,false) && stock.DestroyCalls==1,"Failed selected ore mixed native ore into a committed batch");
}

// Exercise the actual scope/finalizer and placement callbacks with native ports.
Reset(); Register("food",new StockOffer("food",StockCategory.Food));
object?[] outer={null}; Hook("BeginGeneral",outer);
object?[] inner={true,null}; Hook("BeginInventor",inner);
LootRegistry.Tables["packedFoodTable"]=new(){new("native",1)};
var rolled=new LootRollContext{tableId="packedFoodTable",itemId="native"}; int callsBefore=RNG.Calls;
Hook("LootRolled",rolled);
Check(rolled.itemId=="native" && RNG.Calls==callsBefore,"Nested inventor module scope leaked food offers");
var nativeException=new InvalidOperationException("native callback failed");
Check(ReferenceEquals(Hook("EndScope",inner[1],nativeException),nativeException),"Supply finalizer swallowed native exception");
rolled=new(){tableId="packedFoodTable",groupId="food-group",itemId="native"}; RNG.Samples.Enqueue(.99); Hook("LootRolled",rolled);
Check(rolled.itemId=="food","Official loot event did not use the restored supplier scope and shared budget");
Check(LootRegistry.Tables["packedFoodTable"].Count==1 && LootRegistry.Tables["packedFoodTable"][0].id=="native",
    "Official loot callback mutated the registered game table");
rolled=new(){tableId="packedFoodTable",itemId="another-mod.override"}; callsBefore=RNG.Calls; Hook("LootRolled",rolled);
Check(rolled.itemId=="another-mod.override" && RNG.Calls==callsBefore,"Official loot callback replaced an earlier foreign result");
rolled=new(){tableId="medicalTable",groupId="food-group",itemId="native"}; Hook("LootRolled",rolled);
Check(rolled.itemId=="native" && RNG.Calls==callsBefore,"A nested group roll inherited another table's supply candidates");
var nativeStock=Item("native.food",count:3); object?[] placeArgs={nativeStock,false,false,null}; RNG.Samples.Enqueue(.99);
Check((bool)Hook("Place",placeArgs)! && ((GameItem)placeArgs[0]!).identifier=="food" && ((GameItem)placeArgs[0]!).unitCount==3,
    "Placement callback did not pass replacement/quantity to native stock placement");
var placedReplacement=(GameItem)placeArgs[0]!; PlayerStore.Instance.AcceptStock=false;
bool accepted=PlayerStore.Instance.TryAddDirectSellingItemToTable(placedReplacement);
Hook("PlacementEnded",placeArgs[3],null);
Check(!accepted && placedReplacement.DestroyCalls==1,"Rejected native placement leaked or double-destroyed replacement");
nativeStock=Item("native.food"); placeArgs=new object?[]{nativeStock,false,false,null}; RNG.Samples.Enqueue(.99); Hook("Place",placeArgs);
placedReplacement=(GameItem)placeArgs[0]!;
Check(ReferenceEquals(Hook("PlacementEnded",placeArgs[3],nativeException),nativeException) && placedReplacement.DestroyCalls==1,
    "Placement exception did not clean up detached replacement or preserve error");
Hook("EndScope",outer[0],null);

// Real delegate graphs are wrapped once, including shared/cyclic dialogue nodes;
// their stock scope is restored when the original callback throws.
Reset(); Register("synthesis",new StockOffer("titanium",StockCategory.Ore,125){MinerWeight=1});
typeof(NativeSupplyHooks).GetField("_harmony", BindingFlags.NonPublic|BindingFlags.Static)!
    .SetValue(null,new HarmonyLib.Harmony("content-supply.checks"));
bool throwFromStock=false; int originalCalls=0;
var supplier=StoreClientList.CreateMiner(()=>
{
    originalCalls++;
    var ore=Item("common_ore","MATERIAL"); object?[] itemArgs={ore,false,false,null};
    RNG.Samples.Enqueue(.99); Hook("Place",itemArgs);
    Check(((GameItem)itemArgs[0]!).identifier=="titanium","Official client graph did not open its miner stock scope");
    if(throwFromStock) throw nativeException;
});
var originalAction=supplier.mainDialogue!.endAction;
supplier.mainDialogue.nextDialogue=supplier.mainDialogue;
PlayerStore.Instance.storeClientManager.clientStack.Add(supplier);
NativeSupplyHooks.AttachCustomers(PlayerStore.Instance.storeClientManager);
var wrappedAction=supplier.mainDialogue.endAction;
NativeSupplyHooks.AttachCustomers(PlayerStore.Instance.storeClientManager);
Check(ReferenceEquals(wrappedAction,supplier.mainDialogue.endAction),"Official customer hook wrapped an action twice");
supplier.mainDialogue.endAction!();
throwFromStock=true;
try { supplier.mainDialogue.endAction!(); } catch(InvalidOperationException ex) { Check(ReferenceEquals(ex,nativeException),"Dialogue scope swallowed original error"); }
var outsideOre=Item("common_ore","MATERIAL"); object?[] outsideArgs={outsideOre,false,false,null}; Hook("Place",outsideArgs);
Check(ReferenceEquals(outsideArgs[0],outsideOre) && originalCalls==2,"Dialogue stock scope escaped into unrelated placement");
NativeSupplyHooks.Uninstall();
Check(supplier.mainDialogue.endAction==originalAction,"Disable did not restore the original live dialogue callback");

if(args.Length!=1) throw new ArgumentException("Expected current Assembly-CSharp.dll");
using(var game=AssemblyDefinition.ReadAssembly(args[0]))
{
    IEnumerable<TypeDefinition> Nested(TypeDefinition root)
    { foreach(var nested in root.NestedTypes) { yield return nested; foreach(var child in Nested(nested)) yield return child; } }
    foreach(var spec in NativeSupplyHooks.Callbacks)
    {
        var type=game.MainModule.Types.Single(type=>type.FullName==spec.Root.FullName);
        var matches=Nested(type).SelectMany(type=>type.Methods).Where(method=>NativeSupplyHooks.MatchesName(spec,method.Name) &&
            method.ReturnType.FullName=="System.Void" && method.Parameters.Count==0).ToArray();
        Check(matches.Length==(spec.BothBranches?2:1),$"Current native supply callback selection changed: {type.Name}.{spec.Factory}; matched {string.Join(",",matches.Select(method=>method.Name))}");
        bool PlacesStock(MethodDefinition method, HashSet<string> visited)
        {
            if(!method.HasBody || !visited.Add(method.FullName)) return false;
            var calls=method.Body.Instructions.Select(instruction=>instruction.Operand).OfType<MethodReference>().ToArray();
            if(calls.Any(call=>call.DeclaringType.Name=="PlayerStore" && call.Name.Contains("DirectSellingItemToTable",StringComparison.Ordinal))) return true;
            return calls.Where(call=>call.DeclaringType.FullName==type.FullName).Any(call=>
                type.Methods.Where(helper=>helper.Name==call.Name && helper.Parameters.Count==call.Parameters.Count).Any(helper=>PlacesStock(helper,visited)));
        }
        foreach(var method in matches)
            Check(PlacesStock(method,new()),
                "Selected native callback does not place supplier stock: "+method.FullName);
    }
    void Target(string typeName,string name,string result,params string[] parameters)
    {
        var type=game.MainModule.Types.Single(type=>type.Name==typeName);
        Check(type.Methods.Count(method=>method.Name==name && method.ReturnType.FullName==result &&
            method.Parameters.Select(parameter=>parameter.ParameterType.FullName).SequenceEqual(parameters))==1,"Native supply hook signature changed: "+typeName+"."+name);
    }
    Target("StoreClientList","PlaceInventorInventory","System.Void","System.Boolean");
    Target("StoreClientList","PlaceSupplierInventory","System.Void");
    Target("LootRegistry","GetTableEntries","System.Collections.Generic.List`1<LootEntry>","System.String");
    Target("ItemSpawner","Spawn","GameItem","System.String");
    Target("ItemSpawner","SpawnFromTable","GameItem","System.String");
    Target("ItemSpawner","SpawnFromTableGroup","GameItem","System.String");
    foreach(string name in new[]{"SpawnFromTable","SpawnFromTableGroup"})
        Check(game.MainModule.Types.Single(type=>type.Name=="ItemSpawner").Methods.Single(method=>method.Name==name)
            .Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call &&
                call.DeclaringType.Name=="ItemSpawner" && call.Name=="Spawn"),
            "Native table spawning no longer delegates to the shared Spawn hook: "+name);
    Target("PlayerStore","TryAddDirectSellingItemToTable","System.Boolean","GameItem","System.Boolean","System.Boolean","System.Boolean","System.Int32");
    var placement=game.MainModule.Types.Single(type=>type.Name=="PlayerStore").Methods.Single(method=>method.Name=="TryAddDirectSellingItemToTable" &&
        method.Parameters.Count==5 && method.Parameters[0].ParameterType.FullName=="GameItem");
    Check(placement.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call && call.Name=="TryAcceptAllOrDestroy"),
        "Native stock placement no longer owns full-counter cleanup");
}
Console.WriteLine($"PASS: {checks} supply checks (shared weights, catalog/supplier/day/ownership gates, batch ore, protected slots, native pools, resource cleanup, scope/finalizers and current callback metadata). No game launch.");
