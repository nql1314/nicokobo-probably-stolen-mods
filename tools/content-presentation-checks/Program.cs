using Nicokobo.ContentInterop;
using Nicokobo.ModUtilities;
using UnityEngine;
using AugMechanicalAscension.Official;

int checks = 0;
void Check(bool value, string message)
{ checks++; if (!value) throw new InvalidOperationException(message); }

const string scenarioTitle = "aug.start.name";
const string health = "aug.health";
ModLoader.Mods.Add(new LoadedMod
{
    Manifest = new() { ID = MechcorePackage.Id },
    Localization = new()
    {
        ["ko"] = new() { [scenarioTitle] = "오그: 기계 승천", [health] = "체력 {0}/{1}" },
        ["ko-KR"] = new() { [scenarioTitle] = "오그: 기계적 승천" },
        ["eo"] = new() { [scenarioTitle] = "Aŭgo: Mekanika Supretiro", [health] = " " },
        ["en"] = new() { [health] = "Health: {0}/{1}" }
    }
});
LocHelper.LocaleCode = "ko";
Check(AugMechanicalAscension.AugText.Get(scenarioTitle) == "오그: 기계 승천",
    "Aug scenario text must prefer the loaded Korean translation to bundled English");
LocHelper.LocaleCode = "ko-KR";
Check(AugMechanicalAscension.AugText.Get(scenarioTitle) == "오그: 기계적 승천" &&
    ModText.Get(MechcorePackage.Id, health, 3, 8) == "체력 3/8",
    "A regional table must take precedence and fall back per key to the language table");
ModLoader.Mods[0].Localization.Remove("ko");
LocHelper.LocaleCode = "ko";
Check(ModText.Get(MechcorePackage.Id, scenarioTitle) == "오그: 기계적 승천",
    "Selecting ko must also pick up translations supplied as ko-KR");
LocHelper.LocaleCode = "eo-001";
Check(ModText.Get(MechcorePackage.Id, scenarioTitle) == "Aŭgo: Mekanika Supretiro" &&
    ModText.GetFor(MechcorePackage.Id, health, "eo-001", 3, 8) == "Health: 3/8" &&
    ModText.Get(MechcorePackage.Id, "aug.health_2") == "Health",
    "A community locale must resolve its own table and fall back to loaded or bundled English for missing keys");
ModLoader.Mods[0].Localization["ja"] = new() { [scenarioTitle] = "外部の翻訳" };
LocHelper.LocaleCode = "ja";
Check(ModText.Get(MechcorePackage.Id, scenarioTitle) == "外部の翻訳",
    "Loaded translations must also take precedence for languages that are bundled");
ModLoader.Mods.Clear();

const string translationOwner = "translation-owner";
ModLoader.Mods.Add(new LoadedMod
{
    Manifest = new() { ID = translationOwner },
    Localization = new()
    {
        ["zh"] = new() { ["test"] = "产量 {0}", ["en_only"] = "简体" },
        ["zh-TW"] = new() { ["test"] = "產量 {0}" },
        ["en"] = new() { ["test"] = "Yield {0}", ["en_only"] = "English" }
    }
});
foreach (string id in new[] { "seacucu.zh-tw", "dbfoxtw.zh-hant" })
{
    LocHelper.LocaleCode = "zh";
    var patch = new LoadedMod { Manifest = new() { ID = id }, Instance = new object() };
    ModLoader.Mods.Add(patch);
    Check(ModText.LocaleCode == "zh" && ModText.Get(translationOwner, "test", 3) == "产量 3",
        "A disabled Traditional Chinese patch must leave Simplified Chinese selected");
    patch.IsEnabled = true;
    Check(ModText.LocaleCode == "zh-TW" && ModText.Get(translationOwner, "test", 3) == "產量 3",
        "An active overlay on zh must select and format the Mod's Traditional Chinese translation");
    Check(ModText.Get(translationOwner, "en_only") == "English" && ModText.Get(translationOwner, "missing") == "missing",
        "An overlay must use English for missing translations and show the key if neither table provides it");
    foreach (string locale in new[] { "zh-TW", "en" })
    {
        LocHelper.LocaleCode = locale;
        string expected = locale == "zh-TW" ? "產量 3" : "Yield 3";
        Check(ModText.LocaleCode == locale && ModText.Get(translationOwner, "test", 3) == expected,
            "A separately selected locale must use its own table");
    }
    ModLoader.Mods.Remove(patch);
}
ModLoader.Mods.Clear();

LocHelper.LocaleCode = "ja";
LocHelper.LocaleSelectionReady = false;
Check(ModText.Get(MechcorePackage.Id, "aug.start.name") == "Aug: Mechanical Ascension",
    "Early text lookup must use the bundled fallback while native locale selection is still pending");
LocHelper.LocaleSelectionReady = true;
Check(ModText.Get(MechcorePackage.Id, "aug.start.name") == "オーグ：機械昇華",
    "Text must pick up the selected locale once it is ready");

// Native atlas keys are not CustomUI dictionary keys. The actual item resolver
// must supply the Sprite, including sprites supplied by an overriding provider.
foreach (var (atlas, key, modded) in new[]
{
    ("Items/items_material", "metal_ingot", false),
    ("Items/items_medical", "green_serum", false),
    ("Nicokobo/AutomatedLogistics", "output_ejector", false),
    ("nicokobo.food_processing", "fish_floss", true)
})
{
    var icon = new Sprite();
    var prototype = new GameItemElement { name = "Localized product", spriteAtlasPath = atlas, spritePath = key, useSpriteFromMod = modded };
    prototype.Resolve = name => name == key ? icon : null;
    DirectoryMaster.Factories["product"] = () => prototype;
    var shown = RecipePresentation.ReadItem("product");
    Check(ReferenceEquals((object?)shown.Item2, icon), "Recipe art must use the item's sprite resolver: " + atlas);
    Check(shown.Name == prototype.name && prototype.Destroys == 1, "Reading a recipe must clean up its prototype once");
}
var missing = RecipePresentation.ReadItem("missing");
Check(missing.Name == "missing" && missing.Item2 == null, "An unavailable provider must not create a white placeholder image");
var machine = new GameItemElement { name = "Machine" };
var contentWindow = new PixelWindow();
var examineWindow = new PixelWindow();
machine.SetContentWindow(contentWindow);
machine.SetExamineWindow(examineWindow);
GraphHandler.windowGraphNodeRoots.AddRange(new[] { contentWindow, examineWindow });
DirectoryMaster.Factories["machine"] = () => machine;
RecipePresentation.ReadItem("machine");
Check(machine.Destroys == 1 && contentWindow.handler.gameObject.Destroyed && examineWindow.handler.gameObject.Destroyed &&
    !GraphHandler.windowGraphNodeRoots.Contains(contentWindow) && !GraphHandler.windowGraphNodeRoots.Contains(examineWindow) &&
    machine.contentWindow == null && machine.examineWindow == null,
    "Reading a machine recipe must release both independent window roots and detach their owners");
var input = new IngredientView("product", 2, _ => true, "材料", "Material");
Check(RecipePresentation.Ingredient(input, false) == "材料 ×2" &&
    RecipePresentation.Ingredient(input, true) == "Material ×2", "Ingredient labels must select one language");
string language = "ja";
var supplied = new ExtraRecipe("example", "machine", new[] { new IngredientView("input", 2, _ => true, "原料", "Material")
    { LocalizedLabel = () => language == "ja" ? "材料" : "Zutat" } }, "product", "产物", "Product", () => true, () => "product")
{
    LocalizedOutputName = () => language == "ja" ? "完成品" : "Ergebnis"
};
var payload = ForgeWire.Recipe(supplied);
var transported = ForgeWire.Recipe(payload);
Check(RecipePresentation.Ingredient(transported.Ingredients.Single(), true) == "材料 ×2" &&
    RecipePresentation.Output(transported, "product", true) == "完成品", "The recipe wire must preserve both localized names");
language = "de";
LocHelper.LocaleCode = "de";
Check(RecipePresentation.Ingredient(transported.Ingredients.Single()) == "Zutat ×2" &&
    RecipePresentation.Output(transported, "product") == "Ergebnis", "Decoded recipes must use the locale at display time");
payload[2] = ((object[][])payload[2]).Select(label => label[..5]).ToArray();
payload[7] = ((object[])payload[7])[..6];
var legacy = ForgeWire.Recipe(payload);
Check(RecipePresentation.Ingredient(legacy.Ingredients.Single()) == "Material ×2" &&
    RecipePresentation.Output(legacy, "product") == "Product", "Older recipe payloads must retain English fallback labels");

var text = new NativeItemOptions(new("nicokobo.aug.brain_computer_interface_module.name"),
    new("aug.install_in_a_machine_s_module_bay"), new("aug.at_least_this_piece_of_paper_is"));
var localized = new GameItem();
foreach (string locale in new[] { "zh", "en", "ja", "zh-TW", "zh" })
{
    LocHelper.LocaleCode = locale;
    text.Apply(localized);
    Check(localized.name == ModText.GetFor(MechcorePackage.Id, text.Name!.Key, locale) &&
        localized.shortDescription == ModText.GetFor(MechcorePackage.Id, text.ShortDescription!.Key, locale) &&
        localized.flavorText == ModText.GetFor(MechcorePackage.Id, text.FlavorText!.Key, locale),
        "Existing Aug items must refresh name, description and flavor together after a locale change");
}
LocHelper.LocaleCode = "ja";
Check(ModText.Get(MechcorePackage.Id, "aug.start.name") == "オーグ：機械昇華",
    "Starting scenario text must use Japanese before ModLoader assets are available");
string dynamicDescription = localized.shortDescription;
new NativeItemOptions(Name: new("nicokobo.aug.story.evidence.copy.name")).Apply(localized);
Check(localized.shortDescription == dynamicDescription, "Name-only evidence registration must preserve its dynamic description");

// A magnifier opens its story window from inside native EndDrag -> Target.
// Resetting the drag during Opened clears currentItem before EndDrag's cleanup.
// WindowInputCapture's existing tests do not exercise this native reset boundary.
var drag = ItemMouseDragHandler.current = new ItemMouseDragHandler();
var inputManager = InputActionManager.current = new InputActionManager();
OfficialStoryUi.AnyVisible = true;
Time.frameCount = 100;
PresentationInputGuard.Opened();
Check(drag.Resets == 0, "Opening a story window inside a targeting callback must let native EndDrag finish before resetting its item");
var inputPrefix = typeof(PresentationInputGuard).GetMethod("InputPrefix",
    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
Time.frameCount++;
bool allowBackground = (bool)inputPrefix.Invoke(null, new object[] { inputManager, new Vector3() })!;
Check(drag.Resets == 1 && allowBackground,
    "The next input dispatch must clear the old selection even when the pointer is outside the new window");
Time.frameCount++;
inputPrefix.Invoke(null, new object[] { inputManager, new Vector3() });
Check(drag.Resets == 1, "Deferred opening cleanup must not repeatedly reset later drags outside the window");
OfficialStoryUi.PointerOverWindow = true;
Time.frameCount++;
allowBackground = (bool)inputPrefix.Invoke(null, new object[] { inputManager, new Vector3() })!;
Check(!allowBackground && inputManager.Resets > 0, "Story window input must still block the inventory behind it");
PresentationInputGuard.Opened();
PresentationInputGuard.Uninstall();
OfficialStoryUi.AnyVisible = OfficialStoryUi.PointerOverWindow = false;
Time.frameCount++;
inputPrefix.Invoke(null, new object[] { inputManager, new Vector3() });
Check(drag.Resets == 1, "Uninstall must discard pending selection cleanup");

// Exercise the real guard with each window source. Story-only coverage missed
// the workshop and recipe popup, which never participated in pointer capture.
bool Dispatch() => (bool)inputPrefix.Invoke(null, new object[] { inputManager, new Vector3() })!;
foreach (bool recipe in new[] { false, true })
{
    PresentationInputGuard.Uninstall();
    OfficialWorkshopApi.IsVisible = OfficialWorkshopApi.PointerOverWindow = !recipe;
    RecipeQuery.IsVisible = RecipeQuery.PointerOverWindow = recipe;
    Time.frameCount++;
    PresentationInputGuard.Opened();
    Time.frameCount++;
    Check(!Dispatch(), "Workshop and recipe windows must block background input without a story window");
    var storeUi = new StoreUIManager();
    var raycast = typeof(PresentationInputGuard).GetMethod("RaycastPrefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    Check(!(bool)raycast.Invoke(null, new object[] { storeUi })! && storeUi.hoveringStoreUI,
        "Workshop and recipe bounds must block native item raycasts");
    var doubleClick = typeof(PresentationInputGuard).GetMethod("DoubleClickPrefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    object?[] clickArgs = { new CustomEventHandler { doubleClickTimeThreshold = 0.3f }, new TreeNodeRender(), true, 0f };
    Check(!(bool)doubleClick.Invoke(null, clickArgs)! && clickArgs[1] == null && !(bool)clickArgs[2]!,
        "Workshop and recipe clicks must not reach the native double-click handler");
    var storeUpdate = typeof(PresentationInputGuard).GetMethod("StoreUpdatePostfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    storeUi.hoveringStoreUI = false;
    storeUpdate.Invoke(null, new object[] { storeUi });
    Check(storeUi.hoveringStoreUI, "Native StoreUI.Update must not overwrite window pointer protection");
    Time.frameCount++;
    Input.MouseHeld = true;
    Check(!Dispatch(), "A mouse press inside the window must be captured");
    Time.frameCount++;
    OfficialWorkshopApi.PointerOverWindow = RecipeQuery.PointerOverWindow = false;
    Check(!Dispatch(), "Dragging out of the window must retain its captured press");
    OfficialWorkshopApi.IsVisible = RecipeQuery.IsVisible = false;
    PresentationInputGuard.Closed();
    Time.frameCount++;
    Input.MouseHeld = false; Input.MouseReleased = true;
    Check(!Dispatch(), "Closing a window must consume the captured mouse release");
    Time.frameCount++;
    Input.MouseReleased = false;
    Check(Dispatch(), "Background input must resume after the closing release frame");
}
// A query can close while its workshop remains open.
OfficialWorkshopApi.IsVisible = true;
RecipeQuery.IsVisible = true;
Time.frameCount++;
PresentationInputGuard.Opened();
RecipeQuery.IsVisible = false;
PresentationInputGuard.Closed();
OfficialWorkshopApi.PointerOverWindow = true;
Time.frameCount++;
Check(!Dispatch(), "Closing the recipe popup must preserve the workshop's input capture");
OfficialWorkshopApi.PointerOverWindow = false;
Time.frameCount++;
Check(Dispatch(), "An idle pointer outside all visible windows must allow normal inventory input");
Input.MouseHeld = true;
Time.frameCount++;
Check(Dispatch(), "A native drag beginning outside the window must remain owned by the game");
OfficialWorkshopApi.PointerOverWindow = true;
Input.MouseHeld = false; Input.MouseReleased = true;
int resetBeforeDrop = drag.Resets;
Time.frameCount++;
Check(Dispatch() && drag.Resets == resetBeforeDrop + 1,
    "A native drag ending over the workshop must receive release with the underlying placement cancelled");
PresentationInputGuard.Uninstall();
Console.WriteLine($"PASS: {checks} presentation checks (item sprites, cleanup, localization and window input capture). No game launch.");
