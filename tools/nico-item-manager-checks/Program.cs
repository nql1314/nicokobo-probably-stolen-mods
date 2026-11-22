using DseCecil;
using NicoItemManager;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
BrowsingChecks.Run(Check);
LocHelper.LocaleCode = "ja";
Check(CatalogFilterGroups.CollectionDisplay("equipment") == "装備・戦闘" &&
    CatalogFilterGroups.Display("food") == "食品・飲料", "Catalog filters must use the selected game language");
Check(Text.Get("reset_edits") == "編集をリセット", "Editor actions must use the same locale as the catalog");
LocHelper.LocaleCode = "en";

// Prototype disposal must release native windows as well as the item renderer.
{
    var contents = new GameInventory();
    var child = new GameItemElement { parentInventory = contents };
    contents.childItems.Add(child);
    var window = new PixelWindow { Child = contents };
    var releasedPrototype = new GameItemElement();
    releasedPrototype.SetContentWindow(window);
    ItemSpawner.Release(releasedPrototype);
    Check(releasedPrototype.Destroyed && child.Destroyed && window.handler.gameObject.Destroyed
        && window.parentItems.Count == 0 && !GraphHandler.windowGraphNodeRoots.Contains(window),
        "Releasing a detached prototype must dispose its exclusive contents and native window.");

    var shared = new PixelWindow();
    var discarded = new GameItemElement();
    var retained = new GameItemElement();
    discarded.SetContentWindow(shared); retained.SetContentWindow(shared);
    ItemSpawner.Release(discarded);
    Check(!retained.Destroyed && !shared.handler.gameObject.Destroyed
        && shared.parentItems.Count == 1 && shared.parentItems[0] == retained,
        "Releasing a prototype must preserve a window still owned by another item.");
    ItemSpawner.Release(retained);
}

GameGridInventory Reset()
{
    DirectoryMaster.Created.Clear();
    DirectoryMaster.SpawnCalls = 0;
    DirectoryMaster.Factories = new() { ["sample"] = () => new GameItemElement() };
    var inventory = new GameGridInventory();
    inventory.Find = item => new() { inventory = inventory,
        Accept = () => { item.parentInventory = inventory; return item.unitCount; } };
    EmporiumEntry.Instance = new() { invElement = inventory };
    return inventory;
}

// The native trashcan factory replaces this singleton field as a side effect.
// Exercise the actual catalog, editor and delivery code around that boundary.
{
    Reset();
    var originalTrash = new GameGridInventory();
    EmporiumEntry.Instance.trashcanInvElement = originalTrash;
    DirectoryMaster.Factories["trashcan"] = () =>
    {
        var temporaryTrash = new GameGridInventory();
        EmporiumEntry.Instance.trashcanInvElement = temporaryTrash;
        var item = new GameItemElement { identifier = "trashcan", name = "Trashcan" };
        item.SetContentWindow(new PixelWindow { Child = temporaryTrash });
        return item;
    };
    var trashCatalog = new ItemCatalog();
    using (var scan = trashCatalog.Scan()) while (scan.MoveNext()) { }
    Check(ReferenceEquals(EmporiumEntry.Instance.trashcanInvElement, originalTrash)
        && trashCatalog.Entries.Single(entry => entry.Id == "trashcan").Resolved,
        "Catalog inspection must preserve the real store trash inventory after releasing its temporary trashcan.");
    using (var editor = new ItemEditor("trashcan"))
        Check(ReferenceEquals(EmporiumEntry.Instance.trashcanInvElement, originalTrash)
            && !ReferenceEquals(editor.Item.contentWindow.Child, originalTrash),
            "A trashcan editor must own its prototype inventory without replacing the store endpoint.");
    Check(ItemSpawner.Spawn("trashcan", 1).Success
        && ReferenceEquals(EmporiumEntry.Instance.trashcanInvElement, originalTrash),
        "An explicitly spawned trashcan must not replace the fixed store endpoint.");
    DirectoryMaster.Factories["trashcan"] = () =>
    {
        EmporiumEntry.Instance.trashcanInvElement = new GameGridInventory();
        throw new InvalidOperationException("native factory failed after setting its endpoint");
    };
    var failedCatalog = new ItemCatalog();
    using (var scan = failedCatalog.Scan()) while (scan.MoveNext()) { }
    Check(ReferenceEquals(EmporiumEntry.Instance.trashcanInvElement, originalTrash)
        && failedCatalog.Entries.Single(entry => entry.Id == "trashcan").Error != null,
        "Factory failure must restore the original endpoint and retain the actual catalog error.");
    bool editorFailed = false;
    try { using var editor = new ItemEditor("trashcan"); }
    catch (InvalidOperationException) { editorFailed = true; }
    Check(editorFailed && ReferenceEquals(EmporiumEntry.Instance.trashcanInvElement, originalTrash),
        "A failing editor factory must preserve the store endpoint and report its failure.");
    Check(!ItemSpawner.Spawn("trashcan", 1).Success
        && ReferenceEquals(EmporiumEntry.Instance.trashcanInvElement, originalTrash),
        "A failing delivery factory must preserve the store endpoint and report its failure.");
}

var inventory = Reset();
var desktop = new GameGridInventory();
desktop.Find = item => new() { inventory = desktop,
    Accept = () => { item.parentInventory = desktop; return item.unitCount; } };
EmporiumEntry.Instance.backInvinvElement = desktop;
inventory.Find = item => throw new InvalidOperationException("The backpack must not be queried while the desktop has room.");
Check(ItemSpawner.Spawn("sample", 1).Success && DirectoryMaster.Created.Single().parentInventory == desktop,
    "The default destination must deliver to the player's desktop without requiring backpack space.");
inventory = Reset();
desktop = new GameGridInventory { Find = item => throw new InvalidOperationException("Backpack mode must not query the desktop.") };
EmporiumEntry.Instance.backInvinvElement = desktop;
Check(ItemSpawner.Spawn("sample", 1, destination: SpawnDestination.Backpack).Success
    && DirectoryMaster.Created.Single().parentInventory == inventory,
    "An explicit backpack destination must bypass the desktop.");
inventory = Reset();
EmporiumEntry.Instance.backInvinvElement = desktop;
inventory.Find = item => null;
Check(!ItemSpawner.Spawn("sample", 1, destination: SpawnDestination.Backpack).Success
    && DirectoryMaster.Created.Single().Destroyed,
    "An explicit backpack destination must stop when full instead of using the desktop.");
inventory = Reset();
desktop = new GameGridInventory { Find = item => null };
EmporiumEntry.Instance.backInvinvElement = desktop;
int configuredSpawns = 0;
Check(ItemSpawner.Spawn("sample", 1, configure: item => { item.name = "Routed edit"; configuredSpawns++; }).Success
    && DirectoryMaster.Created.Single().parentInventory == inventory && DirectoryMaster.Created.Single().name == "Routed edit"
    && configuredSpawns == 1 && DirectoryMaster.SpawnCalls == 1,
    "A full desktop must fall back to the backpack using the same configured item and a single factory call.");
inventory = Reset();
DirectoryMaster.Factories["sample"] = () => new GameItemElement { unitCount = 2 };
desktop = new GameGridInventory();
desktop.Find = item => new() { inventory = desktop, numTransfer = 1,
    Accept = () => throw new InvalidOperationException("An incomplete desktop slot must not receive part of the item.") };
EmporiumEntry.Instance.backInvinvElement = desktop;
inventory.Find = item => new() { inventory = inventory, numTransfer = 2,
    Accept = () => { item.parentInventory = inventory; return item.unitCount; } };
Check(ItemSpawner.Spawn("sample", 1).Success && DirectoryMaster.Created.Single().parentInventory == inventory
    && DirectoryMaster.Created.Single().unitCount == 2,
    "Desktop priority must preserve the factory quantity when only the backpack can receive the complete item.");
inventory = Reset();
desktop = new GameGridInventory { Find = item => null };
EmporiumEntry.Instance.backInvinvElement = desktop;
inventory.Find = item => null;
var locationResult = ItemSpawner.Spawn("sample", 5);
Check(!locationResult.Success && locationResult.SpawnedCount == 0 && DirectoryMaster.Created.Single().Destroyed,
    "When neither destination has room, the batch must stop and release the unplaced item.");
inventory = Reset();
int desktopAttempts = 0;
desktop = new GameGridInventory();
desktop.Find = item => ++desktopAttempts > 2 ? null : new() { inventory = desktop,
    Accept = () => { item.parentInventory = desktop; return item.unitCount; } };
EmporiumEntry.Instance.backInvinvElement = desktop;
Check(ItemSpawner.Spawn("sample", 5).Success && DirectoryMaster.Created.Count(item => item.parentInventory == desktop) == 2
    && DirectoryMaster.Created.Count(item => item.parentInventory == inventory) == 3 && DirectoryMaster.Created.All(item => !item.Destroyed),
    "Each item in a batch must prefer the desktop and use backpack space once the desktop fills up.");
inventory = Reset();
desktop = new GameGridInventory();
desktop.Find = item => new() { inventory = desktop,
    Accept = () => { item.parentInventory = desktop; throw new InvalidOperationException("after desktop placement"); } };
EmporiumEntry.Instance.backInvinvElement = desktop;
inventory.Find = item => throw new InvalidOperationException("A delivery error must not trigger a second placement.");
locationResult = ItemSpawner.Spawn("sample", 5);
Check(!locationResult.Success && locationResult.SpawnedCount == 1 && DirectoryMaster.Created.Single().parentInventory == desktop
    && !DirectoryMaster.Created.Single().Destroyed,
    "An exception after desktop delivery must preserve and count that item, then stop without a backpack fallback.");
inventory = Reset();
inventory.Find = item => new() { inventory = inventory, Accept = () => throw new InvalidOperationException("rejected") };
Check(!ItemSpawner.Spawn("sample", 1).Success && DirectoryMaster.Created.Single().Destroyed,
    "Acceptance failure must release an unplaced object.");
inventory = Reset();
inventory.Find = item => new() { inventory = inventory,
    Accept = () => { item.parentInventory = inventory; throw new InvalidOperationException("after placement"); } };
var result = ItemSpawner.Spawn("sample", 5);
Check(!result.Success && result.SpawnedCount == 1 && result.Message.Contains("1/5") && !DirectoryMaster.Created.Single().Destroyed,
    "An exception after delivery must preserve and count the item, then stop the batch.");
inventory = Reset();
inventory.Find = item => null;
Check(!ItemSpawner.Spawn("sample", 1).Success && DirectoryMaster.Created.Single().Destroyed,
    "A full backpack with no desktop available must reject the item and release its temporary object.");
inventory = Reset();
inventory.Find = item => new() { inventory = inventory, numTransfer = 0, Accept = () => throw new Exception("must not accept") };
Check(!ItemSpawner.Spawn("sample", 1).Success, "A zero-capacity native slot cannot receive an item.");
inventory = Reset();
inventory.Find = item => new() { inventory = inventory, Accept = () => 1 };
Check(!ItemSpawner.Spawn("sample", 1).Success, "An acceptance return value without placement is not a receipt.");
inventory = Reset();
Check(ItemSpawner.Spawn("sample", 5).Success && DirectoryMaster.Created.All(item => item.parentInventory == inventory && !item.Destroyed),
    "Successful delivery must keep each item alive in its target inventory.");
inventory = Reset();
int attempts = 0;
inventory.Find = item => ++attempts > 2 ? null : new() { inventory = inventory,
    Accept = () => { item.parentInventory = inventory; return 1; } };
result = ItemSpawner.Spawn("sample", 5);
Check(!result.Success && result.SpawnedCount == 2 && result.Message.Contains("2/5")
    && DirectoryMaster.Created.Count(item => !item.Destroyed) == 2, "A partial batch must preserve its successful deliveries.");
inventory = Reset();
var stack = new GameItemElement { parentInventory = inventory, unitCount = 4 };
inventory.Find = item => new() { targetItem = stack, inventory = inventory,
    Accept = () => { stack.unitCount++; item.unitCount = 0; return 1; } };
Check(ItemSpawner.Spawn("sample", 1).Success && stack.unitCount == 5 && !stack.Destroyed
    && DirectoryMaster.Created.Single().DestroyCalls == 1, "A native stack merge must count delivery and release its empty source.");
Reset();
Check(!ItemSpawner.Spawn("removed_id", 1).Success && DirectoryMaster.SpawnCalls == 0,
    "Removed identifiers must not resolve to the game's dummy item.");
Check(!ItemSpawner.Spawn("sample", 0).Success && DirectoryMaster.SpawnCalls == 0, "Invalid counts must not invoke factories.");
EmporiumEntry.Instance = null;
Check(!ItemSpawner.Spawn("sample", 1).Success && DirectoryMaster.SpawnCalls == 0,
    "A hotkey in the main menu must not instantiate a player store or item.");

Reset();
DirectoryMaster.Factories["food_stamp"] = () =>
{
    var item = new GameItemElement { identifier = "food_stamp" };
    item.ModifyTag("FOOD_STAMP_TAG", tag => tag.Enable());
    foreach (var pair in new[] { ("STAMP_AMOUNT", 40), ("STAMP_SEAL", -1), ("STAMP_LOGO", 0),
        ("STAMP_CHIP", 0), ("STAMP_LETTERING", 0) })
        item.ModifyTag(pair.Item1, tag => tag.Enable().SetInt(pair.Item2));
    return item;
};
Check(ItemSpawner.Spawn("food_stamp", 2).Success
    && DirectoryMaster.Created.All(item => item.itemFeatures.Count == 1)
    && DirectoryMaster.Created.Select(item => item.itemFeatures[0]).Distinct().Count() == 2
    && ReferenceEquals(DirectoryMaster.Created.Last().itemFeatures[0], ItemFeatureList.LastStampAuthenticity),
    "Each food stamp spawned from the base factory must receive its own native authenticity feature required by redemption.");
using (var draft = new ItemEditor("food_stamp"))
{
    Check(ReferenceEquals(draft.Item.FindItemFeatureByCategory("CATEGORY_GENUINE_STAMP"), ItemFeatureList.LastStampAuthenticity)
        && draft.Item.itemFeatures.Count == 1,
        "The food stamp preview must initialize the same native authenticity feature as generated items.");
    Check(draft.ReadNumericTags().Count == 5 && draft.ReadNumericTags().All(edit => edit.Kind == TagValueKind.Int),
        "The zero-valued logo, chip and lettering indices must remain integer fields alongside amount and seal.");
    foreach (var pair in new[] { ("STAMP_SEAL", 2), ("STAMP_LOGO", 6), ("STAMP_CHIP", 4), ("STAMP_LETTERING", 9),
        ("STAMP_SEAL", -2), ("STAMP_SEAL", 12345) })
    {
        var unchanged = draft.Int(pair.Item1);
        var edit = draft.ReadTags().Single(tag => tag.Key == pair.Item1);
        edit.Text = pair.Item2.ToString();
        bool rejected = false;
        try { draft.ApplyTag(edit); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && draft.Int(pair.Item1) == unchanged,
            "An out-of-range stamp appearance index must be rejected before changing the item: " + pair);
    }
    var sealEdit = draft.ReadTags().Single(edit => edit.Key == "STAMP_SEAL");
    sealEdit.Text = "0";
    draft.ApplyTag(sealEdit);
    var configuration = draft.Capture();
    Check(ItemSpawner.Spawn("food_stamp", 1, configure: configuration.ApplyTo).Success
        && DirectoryMaster.Created.Last().GetTagReadonly("STAMP_SEAL").GetInt() == 0
        && DirectoryMaster.Created.Last().itemFeatures.Count == 1
        && !ReferenceEquals(DirectoryMaster.Created.Last().itemFeatures[0], draft.Item.itemFeatures[0]),
        "Editor spawning must preserve a valid zero index and initialize an independent native authenticity feature.");
}
var suppliedStampFeature = new ItemFeature { category = "CATEGORY_GENUINE_STAMP" };
DirectoryMaster.Factories["food_stamp"] = () => new GameItemElement
    { identifier = "food_stamp", itemFeatures = new() { suppliedStampFeature } };
ItemFeatureList.LastStampAuthenticity = null;
Check(ItemSpawner.Spawn("food_stamp", 1).Success
    && ReferenceEquals(DirectoryMaster.Created.Last().itemFeatures.Single(), suppliedStampFeature)
    && ItemFeatureList.LastStampAuthenticity == null,
    "An existing authenticity feature, including a counterfeit one, must remain unchanged without adding a duplicate.");
Check(ItemSpawner.Spawn("sample", 1).Success && DirectoryMaster.Created.Last().itemFeatures.Count == 0,
    "Completing a food stamp must not attach authenticity to unrelated items.");
Reset();
var prototype = new GameItemElement();
prototype.state.dict["FLOAT_DATA"] = new TagState("FLOAT_DATA", "FLOAT_DATA") { valueFloat = 2.5f };
var edits = ItemTags.ReadItem(prototype);
Check(edits[0].Kind == TagValueKind.Float && edits[0].Text == "2.5",
    "An existing float tag must show its floating-point type and value in the editor.");
edits[0].Enabled = true;
Check(ItemTags.ApplyTo(prototype, edits) == null && prototype.state.dict["FLOAT_DATA"].valueFloat == 2.5f,
    "Changing only enabled state must preserve the floating-point value.");
var savedCulture = System.Globalization.CultureInfo.CurrentCulture;
try
{
    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
    prototype = new GameItemElement();
    prototype.state.dict["RATIO"] = new TagState("RATIO", "RATIO") { valueFloat = 1.2345678f };
    prototype.state.dict["PRECISE_RATIO"] = new TagState("PRECISE_RATIO", "PRECISE_RATIO") { valueDouble = 1.2345678901234567 };
    prototype.state.dict["ZERO_FLOAT"] = new TagState("ZERO_FLOAT", "ZERO_FLOAT");
    prototype.state.dict["ZERO_DOUBLE"] = new TagState("ZERO_DOUBLE", "ZERO_DOUBLE");
    edits = ItemTags.ReadItem(prototype);
    var floatEdit = edits.Single(edit => edit.Key == "RATIO");
    var doubleEdit = edits.Single(edit => edit.Key == "PRECISE_RATIO");
    Check(floatEdit.Kind == TagValueKind.Float
        && float.Parse(floatEdit.Text, System.Globalization.CultureInfo.InvariantCulture) == 1.2345678f
        && doubleEdit.Kind == TagValueKind.Double
        && double.Parse(doubleEdit.Text, System.Globalization.CultureInfo.InvariantCulture) == 1.2345678901234567,
        "Floating-point fields must retain round-trip precision and use a decimal dot regardless of the current culture.");
    Check(edits.Single(edit => edit.Key == "ZERO_FLOAT").Kind == TagValueKind.Float
        && edits.Single(edit => edit.Key == "ZERO_FLOAT").Text == "0"
        && edits.Single(edit => edit.Key == "ZERO_DOUBLE").Kind == TagValueKind.Double
        && edits.Single(edit => edit.Key == "ZERO_DOUBLE").Text == "0",
        "Explicit floating-point suffixes must identify zero-valued fields.");
    floatEdit.Text = "-2.75e-3";
    doubleEdit.Text = "1.2345678901234567e100";
    Check(ItemTags.ApplyTo(prototype, edits) == null && prototype.state.dict["RATIO"].valueFloat == -0.00275f
        && prototype.state.dict["PRECISE_RATIO"].valueDouble == 1.2345678901234567e100,
        "Float and double edits must accept signed fractions and scientific notation without truncation or culture-dependent parsing.");
}
finally { System.Globalization.CultureInfo.CurrentCulture = savedCulture; }
foreach (TagValueKind kind in new[] { TagValueKind.Float, TagValueKind.Double })
{
    foreach (string invalid in new[] { "", "invalid", "1,25", "NaN", "Infinity", "-Infinity", "1e9999" })
    {
        prototype = new GameItemElement();
        prototype.state.dict["KEEP"] = new TagState("KEEP", "KEEP").Enable().SetInt(42);
        var keep = ItemTags.ReadItem(prototype).Single();
        keep.Text = "43";
        Check(ItemTags.ApplyTo(prototype, new[] { keep,
            new TagEdit { Key = "INVALID", Kind = kind, Text = invalid, IsNew = true } }) != null
            && prototype.state.dict.Count == 1 && prototype.state.dict["KEEP"].valueInt == 42,
            "An invalid floating-point edit must reject the entire batch before changing any tag: " + kind + " " + invalid);
    }
}
Reset();
DirectoryMaster.Factories["float_sample"] = () =>
{
    var item = new GameItemElement { identifier = "float_sample" };
    item.state.dict["RATIO"] = new TagState("RATIO", "RATIO").Enable().SetFloat(2.5f);
    return item;
};
using (var draft = new ItemEditor("float_sample"))
{
    var edit = draft.ReadTags().Single();
    edit.Text = "0";
    draft.ApplyTag(edit);
    Check(draft.ReadTags().Single().Kind == TagValueKind.Float && draft.ReadTags().Single().Text == "0",
        "Refreshing the editor must preserve the selected floating-point type even when an unsuffixed tag is zero.");
    draft.ApplyTag(new TagEdit { Key = "PRECISION", Kind = TagValueKind.Double, Text = "0.12345678901234567", IsNew = true });
    var setup = draft.Capture();
    Check(ItemSpawner.Spawn("float_sample", 2, configure: setup.ApplyTo).Success,
        "The spawn path must accept a configuration with floating-point edits.");
    var copies = DirectoryMaster.Created.Where(item => item.identifier == "float_sample" && item.parentInventory != null).ToList();
    Check(copies.Count == 2 && copies.All(item => item.state.dict["RATIO"].valueFloat == 0
        && item.state.dict["PRECISION"].valueDouble == 0.12345678901234567),
        "Generated items must retain edited zero floats and new full-precision double tags.");
    copies[0].state.dict["PRECISION"].SetDouble(9);
    Check(copies[1].state.dict["PRECISION"].valueDouble == 0.12345678901234567
        && draft.Item.state.dict["PRECISION"].valueDouble == 0.12345678901234567,
        "Floating-point tags must be independent between generated copies and the preview.");
}
prototype = new GameItemElement();
prototype.state.dict["FLAG"] = new TagState("FLAG", "FLAG").Enable().SetBool(true);
edits = ItemTags.ReadItem(prototype);
edits[0].BoolValue = false;
Check(ItemTags.ApplyTo(prototype, edits) == null && prototype.state.dict["FLAG"].valueEnabled && !prototype.state.dict["FLAG"].valueBool,
    "An enabled false boolean is distinct from a disabled tag.");
prototype = new GameItemElement();
prototype.state.dict["HIDDEN_STRING"] = new TagState("HIDDEN_STRING", "HIDDEN_STRING").SetString("old");
edits = ItemTags.ReadItem(prototype);
edits[0].Text = "new";
Check(ItemTags.ApplyTo(prototype, edits) == null && !prototype.state.dict["HIDDEN_STRING"].valueEnabled
    && prototype.state.dict["HIDDEN_STRING"].valueString == "new", "Editing a disabled tag must preserve its disabled state and new value.");
prototype = new GameItemElement();
Check(ItemTags.ApplyTo(prototype, new[] { new TagEdit { Key = "BAD_INT", Kind = TagValueKind.Int, Text = "invalid", IsNew = true } }) != null
    && prototype.state.dict.Count == 0, "Invalid integer edits must be rejected before any mutation.");
prototype = new GameItemElement();
prototype.AddItemFeature(IngotPurityHelper.FeaturePurity());
IngotPurityHelper.SetPurity(prototype, "INGOT_PURITY_PERFECT");
edits = ItemTags.ReadItem(prototype);
edits.Single(edit => edit.IsPurity).Removed = true;
Check(ItemTags.ApplyTo(prototype, edits) == null && !prototype.state.dict[IngotPurityHelper.INGOT_PURITY_TAG].valueEnabled
    && !prototype.state.dict[IngotPurityHelper.INGOT_PURITY_FAKE_TAG].valueEnabled && prototype.itemFeatures.Count == 0,
    "Clearing purity must remove its valuation feature and both native purity tags.");
Reset();
using (var draft = new ItemEditor("sample"))
{
    edits = draft.ReadTags();
    Check(DirectoryMaster.SpawnCalls == 0 && !DirectoryMaster.Created.Single().Destroyed,
        "The real editor reads a detached prototype without spawning a run item.");
}
Check(DirectoryMaster.SpawnCalls == 0 && DirectoryMaster.Created.Single().Destroyed,
    "Disposing the real editor must release its inspected prototype.");

Reset();
DirectoryMaster.Factories["custom"] = () => new GameItemElement { identifier = "custom", name = "Custom machine",
    itemTypes = new() { "MACHINE", "TOOL", "CUSTOM_TYPE", "TOOL" } };
DirectoryMaster.Factories["empty"] = () => new GameItemElement { identifier = "empty", name = "Empty entry", itemTypes = new() };
var catalog = new ItemCatalog();
using (var scan = catalog.Scan())
{
    Check(scan.MoveNext() && !catalog.Ready && catalog.Entries.Count == 3
        && catalog.Entries.All(entry => !entry.Resolved) && DirectoryMaster.Created.Count == 0,
        "Identifiers must be visible before metadata factories run, while complete exports remain gated.");
    catalog.Prioritize(new[] { "sample" });
    catalog.Prioritize(new[] { "empty" });
    Check(scan.MoveNext() && catalog.Scanned == 0,
        "Localization preparation must yield before starting item factories so its work is charged to the frame budget.");
    Check(scan.MoveNext() && catalog.Entries.Single(entry => entry.Id == "empty").Resolved
        && !catalog.Entries.Single(entry => entry.Id == "sample").Resolved,
        "Changing the visible page must replace stale priorities and resolve the current page before the alphabetical backlog.");
    var early = new List<CatalogEntry>();
    catalog.FilterInto(early, "custom", null);
    Check(early.Count == 1 && early[0].Id == "custom" && !early[0].Resolved,
        "An unresolved ID must already be searchable without waiting for the full catalog.");
    while (scan.MoveNext()) { }
}
Check(catalog.Ready && catalog.Entries.Count == 3 && DirectoryMaster.SpawnCalls == 0
    && DirectoryMaster.Created.All(item => item.Destroyed), "Scanning must include registered mod factories without spawning into the run.");
var filtered = new List<CatalogEntry>();
catalog.FilterInto(filtered, "custom_type", "tool");
Check(filtered.Count == 1 && filtered[0].Id == "custom" && filtered[0].Categories.Length == 3,
    "Search must match secondary custom types within the selected group and retain distinct native types.");
catalog.FilterInto(filtered, "工具", null);
Check(filtered.Count == 2, "Search must also match localized native type labels.");
catalog.FilterInto(filtered, "", "other");
Check(filtered.Count == 1 && filtered[0].Id == "empty" && filtered[0].Categories.Length == 0,
    "An empty native type list must remain empty and appear in Other.");
catalog.FilterInto(filtered, "自定义机器", null);
Check(filtered.Count == 1 && filtered[0].Id == "custom" && filtered[0].NameZh == "自定义机器"
    && filtered[0].NameEn == "Custom machine", "Chinese and English names must remain separate and searchable together.");
catalog.FilterInto(filtered, "CUSTOM MACHINE", null);
Check(filtered.Count == 1 && filtered[0].Id == "custom", "English name search must remain case-insensitive.");

GameItemElement ModuleFactory()
{
    var item = new GameItemElement { identifier = "module", itemTypes = new() { "MODULE" } };
    item.ModifyTag("MODULE_TAG", tag => tag.Enable());
    item.ModifyTag("BONUS_PERCENTAGE_PERFORMANCE_INT", tag => tag.Enable().SetInt(20));
    item.ModifyTag("MODULE_TIER_INT", tag => tag.Enable().SetInt(1));
    return item;
}
GameItemElement BottleFactory()
{
    var item = new GameItemElement { identifier = "bottle" };
    item.ModifyTag("LIQUID_CONTAINER_TAG", tag => tag.Enable());
    item.ModifyTag("LIQUID_CONTAINER_CAPACITY", tag => tag.Enable().SetInt(1000000));
    item.ModifyTag("water", tag => tag.Enable().SetInt(150000));
    item.ModifyTag("alcohol", tag => tag.Enable().SetInt(50000));
    return item;
}
Reset();
DirectoryMaster.Factories["module"] = ModuleFactory;
DirectoryMaster.Factories["bottle"] = BottleFactory;
// Verify the menu-to-native-helper binding. The native helper is only a call
// recorder here; filling chemistry itself requires the real game implementation.
Check(LiquidEditing.Labels().Count == LiquidEditing.Presets.Length
    && LiquidEditing.Presets.Length == 12 + Liquid.Liquids.Count - 1,
    "The existing mixture presets and every non-water component must each have one matching menu label.");
string[] pureIds = { "alcohol", "acid", "base", "dream_cap_extract", "nightmare_extract", "wine_extract" };
for (int i = 0; i < pureIds.Length; i++)
{
    var container = BottleFactory();
    LiquidEditing.Presets[12 + i](container);
    Check(WaterHelper.LastPureFill.Item == container && WaterHelper.LastPureFill.LiquidId == pureIds[i],
        "Each pure preset must invoke native AddMaxLiquid for the selected component: " + pureIds[i]);
}
int effectCreated = 0, effectRemoved = 0;
ModuleEffectHelper.moduleEffects = new()
{
    ["MODULE_EFFECT_LEARNING_ALGORITHM"] = new()
    {
        displayName = "Learning", effectType = ModuleEffectHelper.ModuleEffect.ModuleEffectType.ModuleOnly,
        onCreateAction = (item, slot) =>
        {
            effectCreated++;
            int start = item.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt();
            item.ModifyTag("LEARNING_ALGO_MAX_PERFORMANCE_INT", tag => tag.Enable().SetInt(start * 2));
            item.ModifyTag("BONUS_PERCENTAGE_PERFORMANCE_INT", tag => tag.SetInt(0));
        },
        onRemoveAction = item => effectRemoved++
    },
    ["reverse_conflict"] = new() { displayName = "Conflict", incompatibleWithEffects = new() { "MODULE_EFFECT_LEARNING_ALGORITHM" } },
    ["node_only"] = new() { displayName = "Node", effectType = ModuleEffectHelper.ModuleEffect.ModuleEffectType.NodeOnly }
};
using (var draft = new ItemEditor("module"))
{
    Check(DirectoryMaster.SpawnCalls == 0, "Opening a preview must not invoke the run's spawn entry.");
    Check(!ModuleEditing.EffectsFor(draft.Item).Contains("node_only"), "A module cannot offer a node-only effect.");
    draft.SetModuleStat("BONUS_PERCENTAGE_PERFORMANCE_INT", 50);
    ModuleEditing.SetEffect(draft.Item, 1, "MODULE_EFFECT_LEARNING_ALGORITHM");
    var setup = draft.Capture();
    var first = ModuleFactory();
    var second = ModuleFactory();
    setup.ApplyTo(first);
    setup.ApplyTo(second);
    Check(first.GetTagReadonly("LEARNING_ALGO_MAX_PERFORMANCE_INT").GetInt() == 100
        && second.GetTagReadonly("LEARNING_ALGO_MAX_PERFORMANCE_INT").GetInt() == 100
        && second.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt() == 0,
        "Every generated copy must retain the edited learning cap, even when its factory starts with a lower stat.");
    first.ModifyTag("LEARNING_ALGO_MAX_PERFORMANCE_INT", tag => tag.SetInt(1));
    Check(second.GetTagReadonly("LEARNING_ALGO_MAX_PERFORMANCE_INT").GetInt() == 100
        && draft.Item.GetTagReadonly("LEARNING_ALGO_MAX_PERFORMANCE_INT").GetInt() == 100,
        "Generated copies, the configuration and the preview must not share mutable tag instances.");
    bool conflict = false;
    try { ModuleEditing.SetEffect(draft.Item, 2, "reverse_conflict"); } catch (InvalidOperationException) { conflict = true; }
    Check(conflict && ModuleEditing.Effect(draft.Item, 2) == "" && effectRemoved == 0,
        "A one-sided incompatibility must reject the new slot before removing or applying any effects.");
    ModuleEditing.SetEffect(draft.Item, 1, "");
    Check(effectCreated == 3 && effectRemoved == 1 && !draft.Item.IsTag("LEARNING_ALGO_MAX_PERFORMANCE_INT")
        && draft.Item.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt() == 50
        && draft.Item.GetTagReadonly("TEMP_PERCENTAGE_PERFORMANCE_INT").GetInt() == 50,
        "Removing learning must run removal callbacks, restore its base stat and refresh temporary stats.");
    bool protectedField = false;
    try { draft.ApplyTag(new TagEdit { Key = "MODULE_EFFECT2_TAG", Text = "node_only", IsNew = true }); }
    catch (InvalidOperationException) { protectedField = true; }
    Check(protectedField && ModuleEditing.Effect(draft.Item, 2) == "", "Advanced tags must not bypass specialized effect validation.");
}
Check(DirectoryMaster.Created.Single(item => item.identifier == "module").Destroyed, "Closing an editor must release its unplaced preview.");
using (var draft = new ItemEditor("bottle"))
{
    draft.SetLiquidAmount(100);
    Check(draft.Item.GetTagReadonly("water").GetInt() == 75000 && draft.Item.GetTagReadonly("alcohol").GetInt() == 25000,
        "Changing liquid volume must preserve every component's proportion instead of replacing the mixture with water.");
    draft.SetCapacity(50);
    Check(WaterHelper.GetTotalCapacityML(draft.Item) == 50 && WaterHelper.GetCurrentCapacityUL(draft.Item) <= 50000,
        "Reducing capacity must also remove liquid exceeding the new capacity.");
    draft.EmptyLiquid();
    var setup = draft.Capture();
    var copy = BottleFactory();
    setup.ApplyTo(copy);
    Check(WaterHelper.GetCurrentCapacityUL(copy) == 0 && WaterHelper.GetTotalCapacityML(copy) == 50 && copy.WaterFeatureInitialized,
        "An explicitly emptied bottle must stay empty on every generated copy, including prefilled factories.");
    bool emptyRejected = false;
    try { draft.SetLiquidAmount(10); } catch (InvalidOperationException) { emptyRejected = true; }
    Check(emptyRejected && WaterHelper.GetCurrentCapacityUL(draft.Item) == 0,
        "Increasing an empty container's volume must request a liquid choice rather than silently inventing water.");
    draft.FillLiquid(0);
    Check(draft.Item.GetTagReadonly("water").GetInt() == 50000 && draft.Item.GetTagReadonly("alcohol").GetInt() == 0,
        "Filling with a preset must replace the old mixture rather than mix into the remaining space.");
    draft.FillLiquid(11);
    Check(draft.Item.GetTagReadonly("alcohol").GetInt() == 22500 && WaterHelper.GetCurrentCapacityUL(draft.Item) == 50000,
        "The spirit preset must fill even small containers using its alcohol ratio instead of requiring a fixed 750 mL bottle.");
}
inventory = Reset();
result = ItemSpawner.Spawn("sample", 1, configure: item => throw new InvalidOperationException("configuration failed"));
Check(!result.Success && result.SpawnedCount == 0 && DirectoryMaster.Created.Single().Destroyed,
    "A configuration failure must stop before placement and release the failed item.");

var bilingual = new ItemNames();
var enNames = new Dictionary<string, string> { ["cylinder_name"] = "Graduated Cylinder" };
var zhNames = new Dictionary<string, string> { ["cylinder_name"] = "量筒" };
bilingual.AddSource("", new Dictionary<string, string> { ["cylinder_name"] = "Éprouvette" }, zhNames, enNames);
Check(bilingual.Resolve("", "Éprouvette", "fr") == ("量筒", "Graduated Cylinder"),
    "The same localization key must resolve both names even while another UI locale is selected.");
Check(bilingual.Resolve("", "量筒", "zh") == ("量筒", "Graduated Cylinder"),
    "The Chinese display name must resolve its English counterpart without a locale switch.");
bilingual.AddSource("example.mod", new Dictionary<string, string> { ["custom_name"] = "Graduated Cylinder" },
    new Dictionary<string, string> { ["custom_name"] = "模组量筒" },
    new Dictionary<string, string> { ["custom_name"] = "Graduated Cylinder" });
Check(bilingual.Resolve("example.mod", "Graduated Cylinder", "en") == ("模组量筒", "Graduated Cylinder"),
    "A mod's translation must take precedence over a matching vanilla display name.");
var oneLanguage = new ItemNames();
oneLanguage.AddSource("", enNames, new Dictionary<string, string>(), enNames);
Check(oneLanguage.Resolve("", "Graduated Cylinder", "zh") == ("", "Graduated Cylinder"),
    "A missing Chinese translation must stay empty rather than repeat English in the Chinese column.");
var ambiguous = new ItemNames();
ambiguous.AddSource("", new Dictionary<string, string> { ["one"] = "Same", ["two"] = "Same" },
    new Dictionary<string, string> { ["one"] = "甲", ["two"] = "乙" },
    new Dictionary<string, string> { ["one"] = "Alpha", ["two"] = "Beta" });
Check(ambiguous.Resolve("", "Same", "fr") == ("", ""), "Ambiguous display names must not select another item's translation.");
Check(oneLanguage.Resolve("", "只提供中文", "zh") == ("只提供中文", ""),
    "An unlocalized mod name must retain its current-language display without inventing the other language.");

int scannedInSlice = 0;
IEnumerator<object> ScanWork()
{
    for (int i = 0; i < 513; i++)
    {
        scannedInSlice++;
        yield return null;
    }
}
using (var work = ScanWork())
{
    Check(!ItemCatalog.AdvanceScan(work, 0) && scannedInSlice == 1,
        "An exhausted frame budget must yield after one item while still making progress.");
    Check(ItemCatalog.AdvanceScan(work, double.MaxValue) && scannedInSlice == 513,
        "Available frame time must process the remaining catalog without an eight-item cap.");
}

PropertyChecks.Run(Check);
ExistingItemChecks.Run(Check);
PresetChecks.Run(Check);

if (args.Length != 2) throw new ArgumentException("Pass the compiled NicoItemManager.dll and current Assembly-CSharp.dll.");
using (var binary = AssemblyDefinition.ReadAssembly(args[0]))
using (var game = AssemblyDefinition.ReadAssembly(args[1]))
{
    var menu = binary.MainModule.GetType("NicoItemManager.ExistingItemMenu");
    var menuPatches = menu?.Methods.SelectMany(method => method.CustomAttributes
        .Where(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch" && attribute.ConstructorArguments.Count >= 2)
        .Select(attribute => (Target: (string)attribute.ConstructorArguments[1].Value, Method: method)))
        .ToDictionary(patch => patch.Target, patch => patch.Method);
    Check(menuPatches != null && menuPatches.TryGetValue("TriggerButton", out var trigger)
        && trigger.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
            .Any(call => call.Name == "OpenExistingItem"),
        "Choosing the right-click management row must still open the existing-item editor.");
    var highlightCalls = menuPatches["UpdateSelectButtonColor"].Body.Instructions
        .Select(instruction => instruction.Operand).OfType<MethodReference>().Select(call => call.Name).ToArray();
    Check(highlightCalls.Contains("set_color") && !highlightCalls.Any(name => name is "SetText" or "SetFixedSize" or "Validate"),
        "The manage-row hover patch must set its color without resizing or validating the menu.");
    Check(binary.Name.Name == "NicoItemManager" && binary.Name.Version.ToString() == "1.3.9.0", "The native DLL must use the current name and version.");
    var entryType = binary.MainModule.GetType("NicoItemManager.NicoItemManagerMod");
    Check(!entryType.Methods.Single(method => method.Name == "OnEnable").Body.Instructions
        .Select(instruction => instruction.Operand).OfType<MethodReference>()
        .Any(method => method.Name == "get_LocaleCode" || method.Name == "GetCurrentLocale"),
        "Enabling the mod must not synchronously select a locale during the game's startup localization.");
    Check(!entryType.Properties.Any(property => property.Name == "SpawnEnabled")
        && !entryType.Methods.Single(method => method.Name == "Init").Body.Instructions.Any(instruction =>
            instruction.OpCode.Code == DseCecil.Cil.Code.Ldstr && (string)instruction.Operand == "SpawnEnabled"),
        "The removed spawning option must not be registered or gate the compiled mod.");
    Check(binary.MainModule.GetType("NicoItemManager.NicoItemManagerMod").Interfaces.Any(value => value.InterfaceType.FullName == "IMod"),
        "The native package needs an official IMod entry.");
    Check(!binary.MainModule.AssemblyReferences.Any(value => value.Name.Contains("Il2Cpp") || value.Name.Contains("Melon") || value.Name.Contains("Forge")),
        "The native assembly must have no Demo loader or Forge dependencies.");
    foreach (var type in binary.MainModule.Types)
        foreach (var patch in type.CustomAttributes.Concat(type.Methods.SelectMany(method => method.CustomAttributes))
            .Where(value => value.AttributeType.FullName == "HarmonyLib.HarmonyPatch" && value.ConstructorArguments.Count >= 2))
        {
            string target = ((TypeReference)patch.ConstructorArguments[0].Value).FullName;
            string method = (string)patch.ConstructorArguments[1].Value;
            Check(game.MainModule.GetType(target)?.Methods.Count(value => value.Name == method) == 1,
                "The current game must contain the unique patch target: " + target + "." + method);
        }
    foreach (string field in new[] { "publicDisplay", "actualDisplay", "removedByTool", "valueModifier", "initiallyShown", "isFeatureExposed", "isFeatureDiscovered", "usePreExposeValue", "preExposeValueModifier" })
        Check(game.MainModule.GetType("ItemFeature").Fields.Any(value => value.Name == field && value.FieldType.FullName == (field switch
            { "publicDisplay" or "actualDisplay" or "removedByTool" => "System.String", "valueModifier" or "preExposeValueModifier" => "System.Int32", _ => "System.Boolean" })),
            "The feature snapshot's native field must match the current game: " + field);
    Check(game.MainModule.GetType("InputActionManager").Fields.Any(value => value.Name == "lastMousePosition" && value.FieldType.FullName == "UnityEngine.Vector3"),
        "The input guard's injected field must match the current game.");
    var panel = binary.MainModule.GetType("NicoItemManager.ManagerPanel");
    var rowBuilder = panel.Methods.Single(method => method.Name == "CreateRow");
    var selectionStore = rowBuilder.Body.Instructions.Single(instruction => instruction.Operand is FieldReference field && field.Name == "OnSelect");
    var cursor = selectionStore.Previous;
    while (cursor != null && cursor.OpCode.Code != DseCecil.Cil.Code.Ldftn) cursor = cursor.Previous;
    var selectionHandler = ((MethodReference)cursor.Operand).Resolve();
    var selectionCalls = selectionHandler.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>().ToList();
    Check(selectionCalls.Any(call => call.Name == "OpenEditor") && !selectionCalls.Any(call => call.Name == "Spawn"),
        "The compiled row-selection callback must open the editor and must not call spawning.");
}
Console.WriteLine($"Passed {checks} Nico' Item Manager checks (managed fixtures and current-assembly metadata; no game process).");
