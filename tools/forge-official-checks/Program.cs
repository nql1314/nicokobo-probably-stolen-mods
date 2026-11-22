extern alias clientA;
extern alias clientB;
using A = clientA::ForgeChecks.ClientProbe;
using B = clientB::ForgeChecks.ClientProbe;
using Nicokobo.Forge;

int checks = 0;
void Check(bool value, string description) { checks++; if (!value) throw new Exception(description); }
Check(B.ConfirmPayload(A.DialoguePayload()) == "run:accept" && A.ConversationCalls == 1,
    "Dialogue contexts and callbacks must cross independently compiled contract types");
Check(B.ReadSupplierPayload(A.SupplierPayload()) && A.Purchases == 1,
    "Supplier refresh, disabled offers and purchase callbacks must retain their source owner");
Check(B.ReadWorkshopPayload(A.WorkshopPayload()), "Workshop costs, rewards, dependencies and graph positions were lost on the wire");
Check(B.CallPhonePayload(A.PhonePayload()) == "run:accept" && A.ConversationCalls == 2,
    "Phone contacts must return usable dialogue callbacks across assemblies");
A.Enable("a");
var forge = new ForgeMod(); forge.Init(new()); forge.OnEnable();
ModLoader.Mods.Add(new() { Manifest = new() { ID = ForgeMod.Id }, Instance = forge, IsEnabled = true });
B.Enable("b");
Check(A.Starts == 0 && B.Starts == 0, "Content activation must await completed discovery, regardless of host order");
ModHook.Assets("a"); ModHook.Assets("b");
Check(A.Starts == 1 && B.Starts == 1 && A.AssetLoads == 1 && B.AssetLoads == 1, "First-resource replay or later resource subscription was lost/duplicated");
A.RegisterRecipe();
Check(B.PrepareRecipe() && A.Prepares == 1, "Shared-terminal recipes must preserve their source preparation callback and priority across assemblies");
Check(B.Recipe() == ("recipe", "first", true), "Recipe registration, callbacks and ingredient predicates must cross client assemblies");
Check(B.RecipePreview(new() { identifier = "machine" }) == (true, "Preview: machine") &&
    B.RecipePreview(new() { identifier = "other" }) == (false, "Preview: other"),
    "External machine selection and live previews must survive both client/host wire conversions");
A.OpenRecipes("proof.item");
Check(Nicokobo.ContentInterop.RecipeQuery.LastItem == "proof.item", "Workshop recipe query must reach the one host window");
A.Output = "changed";
Check(B.Recipe().Output == "changed", "Dynamic output callback was snapshotted instead of retained");
A.Recipe();
Check(A.LastView!.GetType().Assembly != B.LastView!.GetType().Assembly, "The test must use genuinely separate client DTO identities");
var input = new GameInventory(); var output = new GameInventory(); var machine = new GameItem { identifier = "machine" };
var item = new GameItem { identifier = "input", unitCount = 5, parentInventory = input }; input.childItems.Add(item);
var water = new GameSlotInventory(); var bottle = new GameItem { WaterParts = 100 }; water.childItems.Add(bottle);
A.RegisterMachine(input, output);
Check(ReferenceEquals(B.Input(machine), input), "Native inventory identity must survive transport");
B.Observe(); var parent = A.Begin(machine, input, output, water);
var nested = B.Begin(machine, input, output, water); item.unitCount -= 2; bottle.WaterParts -= 30;
nested.Commit(); nested.Scope.Dispose();
Check(B.Before == 1 && B.After == 0 && parent.Consumed(), "Cross-client nested work must share the host's one transaction");
parent.Scope.Dispose(); parent.Scope.Dispose();
Check(B.After == 1 && B.Committed && B.Consumed == 2 && B.Water == 30, "Actual consumption and committed receipts must reach observers once");
var battery = new GameItem { Energy = 10 }; A.Power(battery);
Check(B.Debit(battery, 3) == (true, true) && battery.Energy == 7, "Shared power must route to its owner across clients");
Check(B.Debit(battery, 100) == (true, false) && battery.Energy == 7, "Rejected power debits must preserve energy");
A.Supply(); Check(B.Stock().SequenceEqual(new[] { "a:stock" }), "Supply offers must use the one host catalog and preserve owner");
bool collision = false; try { B.Supply(); } catch (InvalidOperationException) { collision = true; }
Check(collision, "Different clients must not create parallel catalogs that allow duplicate ownership");
A.Flush(); ModHook.Night(); Check(A.Flushes == 1, "Night flush must route to the registering client");
var fixedNode = FixedAbilityChecks.Run(Check);
A.Disable(); Check(B.RecipeCount() == 0 && B.Input(machine) == null && B.Stock().Length == 0 && !B.Debit(battery, 1).Handled,
    "Detaching an owner must remove its recipes, machine, stock and power without affecting other clients");
Nicokobo.ContentInterop.FixedModuleActionPatch.Postfix(fixedNode, null, true);
Check(!B.HasAbility() && A.FixedCalls == 1 && fixedNode.GetTagReadonly("bonus").GetInt() == 25 && ModuleEffectHelper.moduleEffects.ContainsKey("native.affix"),
    "Detaching an owner must remove private abilities and callbacks while retaining native affixes");
forge.OnDisable(); B.RemoveAbilities(); B.Disable();
ModLoader.Mods.Clear(); ModLoader.Mods.Add(new() { Manifest = new() { ID = ForgeMod.Id }, IsEnabled = true, Instance = new WrongProtocol() });
int starts = A.Starts; A.Enable("a"); ModHook.Assets("a");
Check(A.Starts == starts && UnityEngine.Debug.Errors.Last().Contains("protocol"), "Incompatible host protocol must stop content activation");
Check(HarmonyLib.Harmony.Removed.Last() == "mod.a", "Failed activation must remove only that content's automatic patches");
A.Disable(); ModLoader.Mods.Clear(); A.Enable("a"); ModHook.Assets("a");
Check(A.Starts == starts && UnityEngine.Debug.Errors.Last().Contains("nicokobo.forge"), "Missing prerequisite must stop activation with a useful error");
A.Disable();
foreach (string firstOwner in new[] { "a", "other" })
{
    ModLoader.Mods.Clear();
    var resourceHost = new ForgeMod(); resourceHost.OnEnable();
    ModLoader.Mods.Add(new() { Manifest = new() { ID = ForgeMod.Id }, Instance = resourceHost, IsEnabled = true });
    int stops = A.Stops, assets = A.AssetLoads;
    A.ThrowAssets = true; A.Enable("a");
    ModHook.Assets(firstOwner);
    if (firstOwner != "a") ModHook.Assets("a");
    Check(!A.Connected && A.Stops == stops + 1 && A.AssetLoads == assets + 1,
        "Required resource failure must stop and disconnect content in either asset load order: " + firstOwner);
    Check(HarmonyLib.Harmony.Removed.Last() == "mod.a", "Resource failure must remove only the failed content's patches");
    ModHook.Assets("a");
    Check(A.AssetLoads == assets + 1, "Failed content must unsubscribe its resource callback");
    A.ThrowAssets = false; A.Disable(); resourceHost.OnDisable();
}
ModLoader.Mods.Clear();
AchievementChecks.Run();
Console.WriteLine($"PASS {checks} Forge cross-assembly checks; real host/client code, native/UI ports only. No game launch.");

sealed class WrongProtocol : IMod, IServiceProvider
{
    public void Init(ModManifest manifest) { }
    public void OnEnable() { }
    public void OnDisable() { }
    public object GetService(Type type) => (Func<string, object[], object>)((_, _) => 2);
}
