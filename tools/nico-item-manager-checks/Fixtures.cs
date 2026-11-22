public class GameItem : GraphNodeStorage
{
    public string identifier = "sample", name = "Sample tool", spritePath = "sample", spriteAtlasPath = "Items/test";
    public string shortDescription, longDescription, flavorText, customText;
    public bool activateDefault;
    public int unitCount = 1;
    public int uniqueId;
    public long unitValue = 10;
    public long unitBaseValue, lateUnitValue, backupUnitValue;
    public bool Owned = true, Locked, Removable = true;
    public int MaxRemovable = int.MaxValue;
    public Action<GameItem> onChangeAmountItemFunc;
    public Action<GameItem, GameInventory, SlotMarker> onSlotChangeAmountItemFunc;
    public List<GameItem> NestedItems = new();
    public bool useSpriteFromMod;
    public GridShape shape = new();
    public GameInventory parentInventory;
    public PixelWindow contentWindow, examineWindow;
    public TagSystem state = new();
    public List<string> itemTypes = new() { "TOOL" };
    public List<ItemFeature> itemFeatures = new();
    public bool Destroyed, WaterFeatureInitialized;
    public int DestroyCalls, Validations, BatteryRefreshes, GunRefreshes;
    public void Destroy()
    {
        DestroyCalls++; Destroyed = true;
        parentInventory?.childItems.Remove(this); parentInventory = null;
        contentWindow?.Hide();
    }
    public GameItem SetContentWindow(PixelWindow value) { SetWindow(ref contentWindow, value); return this; }
    public GameItem SetExamineWindow(PixelWindow value) { SetWindow(ref examineWindow, value); return this; }
    void SetWindow(ref PixelWindow field, PixelWindow value)
    {
        if (field != null)
        {
            field.parentItems.Remove(this);
            if (field.parentItems.Count == 0 && !GraphHandler.windowGraphNodeRoots.Contains(field))
                GraphHandler.windowGraphNodeRoots.Add(field);
        }
        field = value;
        if (value == null) return;
        if (!value.parentItems.Contains(this)) value.parentItems.Add(this);
        GraphHandler.windowGraphNodeRoots.Remove(value);
    }
    public string GetDisplayName() => name;
    public void ValidateShapeState() { Validations++; }
    public bool IsTag(string key) => state.dict.TryGetValue(key, out var tag) && tag.valueEnabled;
    public bool IsGameItemType(string type) => itemTypes.Contains(type);
    public TagState GetTagReadonly(string key) => state.dict.TryGetValue(key, out var tag) ? tag : new TagState(key, key);
    public GameItem ModifyTag(string key, Action<TagState> change)
    {
        if (!state.dict.TryGetValue(key, out var tag)) state.dict[key] = tag = new TagState(key, key);
        change(tag);
        return this;
    }
    public GameItem SetValue(long value) { unitValue = value; return this; }
    public GameItem SetUnitCount(int value) { unitCount = value; return this; }
    public GameItem SetSprite(string atlas, string sprite) { spriteAtlasPath = atlas; spritePath = sprite; return this; }
    public bool MayRemove() => Removable;
    public int MaxNumRemove() => MaxRemovable;
    public List<GameItem> FindAllChildItems() => NestedItems;
    public GameItem SetSpriteAndShape(string atlas, string sprite) { spriteAtlasPath = atlas; spritePath = sprite; return this; }
    public static GridShapeBuilder BuildShapeFromSprite(string atlas, string sprite) => new();
    public bool DisableTag(string key, bool skipValidate)
    {
        if (!state.dict.TryGetValue(key, out var tag) || !tag.valueEnabled) return false;
        tag.valueEnabled = false;
        return true;
    }
    public ItemFeature FindItemFeatureByID(string id) => itemFeatures.Find(feature => feature.identifier == id);
    public ItemFeature FindItemFeatureByCategory(string category) => itemFeatures.Find(feature => feature.category == category);
    public void AddItemFeature(ItemFeature feature) => itemFeatures.Add(feature);
    public void RemoveItemFeature(ItemFeature feature) => itemFeatures.Remove(feature);
}
public class GameItemElement : GameItem
{
    public bool IsDestroyed() => Destroyed;
    public UnityEngine.Sprite ResolveSpriteByName(string name) => new();
}
public class GridShape { public GridShape Clone() => new(); }
public class GridShapeBuilder { public int width = 1, height = 1; public int GetLocal(int x, int y) => 1; }
public class ItemFeature
{
    public string identifier, category, publicDisplay, actualDisplay;
    public int parentItemUniqueId;
    public string removedByTool;
    public int featureType, valueStage, valueModifier, preExposeValueModifier, customIntValue1;
    public bool initiallyShown, isFeatureExposed, isExposing, isFeatureDiscovered, usePreExposeValue,
        useCondition, isExposable, isPublicHidden, isDisabled;
    public bool isFeatureMatch;
    public long pricingBase;
    public ItemCondition realCondition, fakeCondition;
}
public class ItemCondition
{
    public string identifier, display, category, onActivateActionId;
    public int modValue, modValueDisplay, customValue, compareValue;
    public bool isTransformative, hiddenAsPublic;
}
public class TagSystem
{
    public Dictionary<string, TagState> dict = new();
    public TagSystem Clone() => new() { dict = dict.ToDictionary(pair => pair.Key, pair => pair.Value?.Clone()) };
}
public class TagState
{
    public string identifier { get; set; }
    public string identifierName { get; set; }
    public bool valueEnabled, valueBool;
    public string valueString = "";
    public int valueInt;
    public float valueFloat;
    public long valueLong;
    public double valueDouble;
    public TagState(string key, string label) { identifier = key; identifierName = label; }
    public TagState Reset() { valueString = ""; valueInt = 0; valueBool = false; valueFloat = 0; valueLong = 0; valueDouble = 0; return this; }
    public TagState Enable() { valueEnabled = true; return this; }
    public TagState SetEnabled(bool value) { valueEnabled = value; return this; }
    public TagState SetString(string value) { valueString = value; return this; }
    public TagState SetInt(int value) { valueInt = value; return this; }
    public TagState SetBool(bool value) { valueBool = value; return this; }
    public TagState SetFloat(float value) { valueFloat = value; return this; }
    public TagState SetDouble(double value) { valueDouble = value; return this; }
    public string GetString() => valueString;
    public int GetInt() => valueInt;
    public TagState Clone() => (TagState)MemberwiseClone();
}
public class GraphNodeStorage { public virtual List<GraphNodeStorage> children => new(); }
public class TreeNodeRender { public UnityEngine.GameObject gameObject = new(); }
public static class GraphHandler { public static readonly List<PixelWindow> windowGraphNodeRoots = new(); }
public class PixelWindow : GraphNodeStorage
{
    public TreeNodeRender handler = new();
    public GraphNodeStorage Child;
    public readonly List<GameItem> parentItems = new();
    public override List<GraphNodeStorage> children => Child == null ? new() : new() { Child };
    public void Hide() => handler.gameObject.Active = false;
}
public class GameInventory : GraphNodeStorage
{
    public readonly List<GameItem> childItems = new();
    public bool RejectExpel;
    public Action<GameItem, GameInventory, SlotMarker> onSlotChangeAmountItemFunc;
    public bool Expel(GameItem item)
    {
        if (RejectExpel || !childItems.Remove(item)) return false;
        item.parentInventory = null;
        return true;
    }
    public SlotMarker GetIncompleteItemInventorySlot(GameItem item) => new() { inventory = this };
    public void Validate() { }
    public override List<GraphNodeStorage> children => childItems.Cast<GraphNodeStorage>().ToList();
}
public static class GeneralHelper
{
    public static bool IsItemOwned(GameItem item) => item.Owned;
    public static bool IsItemLocked(GameItem item) => item.Locked;
}
public class OverlayHandler
{
    public static OverlayHandler current;
    public void UpdateInventoryStateUI() { }
}
public class GameGridInventory : GameInventory
{
    public Func<GameItem, SlotMarker> Find;
    public SlotMarker TryFindOneValidInventorySlot(GameItem item, bool keepOrientation) => Find(item);
}
public class SlotMarker
{
    public GameItem targetItem;
    public GameInventory inventory;
    public int numTransfer = 1;
    public bool Valid = true;
    public Func<int> Accept;
    public bool IsValid() => Valid;
    public int TryAcceptOnce(int units) => Accept();
}
public class EmporiumEntry
{
    public static EmporiumEntry Instance;
    public GameGridInventory invElement;
    public GameGridInventory backInvinvElement;
    public GameGridInventory trashcanInvElement;
}
public static class DirectoryMaster
{
    public static Dictionary<string, Func<GameItem>> Factories = new();
    public static List<GameItem> Created = new();
    public static int SpawnCalls;
    public static List<string> GetIdentifierList<T>() => Factories.Keys.ToList();
    public static bool Has<T>(string id) => Factories.ContainsKey(id);
    public static T Create<T>(string id) where T : class
    {
        if (!Factories.TryGetValue(id, out var factory)) return null;
        GameItem item = factory();
        Created.Add(item);
        return item as T;
    }
    public static GameItem Item(string id, bool isOwned) { SpawnCalls++; return Create<GameItem>(id); }
}
public static class IngotPurityHelper
{
    public const string INGOT_PURITY_TAG = "INGOT_PURITY_TAG", INGOT_PURITY_FAKE_TAG = "INGOT_PURITY_FAKE_TAG";
    public static ItemFeature FeaturePurity() => new() { identifier = INGOT_PURITY_TAG };
    public static void SetPurity(GameItem item, string purity)
    {
        if (item.FindItemFeatureByID(INGOT_PURITY_TAG) == null) throw new InvalidOperationException("Missing native purity feature");
        foreach (string key in new[] { INGOT_PURITY_TAG, INGOT_PURITY_FAKE_TAG })
            item.state.dict[key] = new TagState(key, key).Enable().SetString(purity);
    }
}
public static class RenderHandler
{
    public static readonly HashSet<string> AvailableSprites = new();
    public static UnityEngine.Sprite LoadFromAtlas(string atlas, string sprite) => new();
    public static UnityEngine.Sprite TryLoadFromAtlas(string atlas, string sprite) => AvailableSprites.Contains(sprite) ? new() : null;
}

// Only the native file boundary is substituted; preset serialization and mutations use production code.
public static class ModHelper
{
    public static readonly Dictionary<string, string> DataFiles = new();
    public static bool RejectWrite;
    public static string GetLocalized(string modId, string key, params object[] args) => key;
    public static string ReadModDataFileText(string modId, string path) => DataFiles.GetValueOrDefault(modId + "/" + path);
    public static void WriteModDataFileText(string modId, string path, string text)
    {
        if (RejectWrite) throw new IOException("Test write failure");
        DataFiles[modId + "/" + path] = text;
    }
}
public static class TypeHelper
{
    public static string GetTypeDisplayName(string key) => key == "TOOL" ? "工具" : key;
}
public class Liquid
{
    public string identifier, displayName, currentPart;
    public static List<Liquid> Liquids = new() {
        new() { identifier = "water", displayName = "Water", currentPart = "water" },
        new() { identifier = "alcohol", displayName = "Alcohol", currentPart = "alcohol" },
        new() { identifier = "acid", displayName = "Acid", currentPart = "acid" },
        new() { identifier = "base", displayName = "Base", currentPart = "base" },
        new() { identifier = "dream_cap_extract", displayName = "Dream cap extract", currentPart = "dream_cap_extract" },
        new() { identifier = "nightmare_extract", displayName = "Nightmare extract", currentPart = "nightmare_extract" },
        new() { identifier = "wine_extract", displayName = "Wine extract", currentPart = "wine_extract" } };
}
public static class WaterHelper
{
    public static (GameItem Item, string LiquidId) LastPureFill;
    public static void AddMaxLiquid(GameItem item, string liquidId) => LastPureFill = (item, liquidId);
    public const int NUDKA_ALCOHOL_PERCENT = 45;
    public static int GetTotalCapacityML(GameItem item) => item.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt() / 1000;
    public static int GetCurrentCapacityUL(GameItem item) => Liquid.Liquids.Sum(liquid => item.GetTagReadonly(liquid.currentPart).GetInt());
    public static void RemoveExcessLiquid(GameItem item)
    {
        int excess = GetCurrentCapacityUL(item) - GetTotalCapacityML(item) * 1000;
        foreach (var liquid in Liquid.Liquids)
        {
            int remove = Math.Min(Math.Max(0, excess), item.GetTagReadonly(liquid.currentPart).GetInt());
            item.ModifyTag(liquid.currentPart, tag => tag.SetInt(tag.GetInt() - remove));
            excess -= remove;
        }
    }
    public static void EmptyContainer(GameItem item)
    {
        foreach (var liquid in Liquid.Liquids) item.ModifyTag(liquid.currentPart, tag => tag.SetInt(0));
    }
    public static void FillWithPureWater(GameItem item)
    {
        EmptyContainer(item);
        item.ModifyTag("water", tag => tag.Enable().SetInt(GetTotalCapacityML(item) * 1000));
    }
    public static void FillWithHighQualityWater(GameItem item) => FillWithPureWater(item);
    public static void FillWithBaseWater(GameItem item) => FillWithPureWater(item);
    public static void FillWithGhostwaterHighEnd(GameItem item) => FillWithPureWater(item);
    public static void FillWithGhostwater(GameItem item) => FillWithPureWater(item);
    public static void FillWithGhostwaterLowEnd(GameItem item) => FillWithPureWater(item);
    public static void FillWithRustWaterHighEnd(GameItem item) => FillWithPureWater(item);
    public static void FillWithRustWater(GameItem item) => FillWithPureWater(item);
    public static void FillWithRustWaterLowEnd(GameItem item) => FillWithPureWater(item);
    public static void FillWithGutterflow(GameItem item) => FillWithPureWater(item);
    public static void FillWithNutrientSolution(GameItem item, int amount) => FillWithPureWater(item);
    public static void PourNudka(GameItem item) => FillWithPureWater(item);
    public static void PourSpirit(GameItem item, int ml, int alcoholPercent)
    {
        EmptyContainer(item);
        item.ModifyTag("alcohol", tag => tag.Enable().SetInt(ml * 1000 * alcoholPercent / 100));
        item.ModifyTag("water", tag => tag.Enable().SetInt(ml * 1000 * (100 - alcoholPercent) / 100));
    }
}
public static class WaterFeatureHelper
{
    public static void InitWaterFeature(GameItem item, bool isExpose = false) { item.WaterFeatureInitialized = true; }
}
public static class PowerHelper
{
    public static void SetPowerSourceAt(GameItem item, int value)
    {
        item.ModifyTag("power_source_item_energy", tag => tag.Enable().SetInt(value));
        item.BatteryRefreshes++;
        if (item.identifier == "energy_credit") item.spritePath = "battery" + value * 100 / item.GetTagReadonly("power_source_item_max_energy").GetInt();
    }
}
public static class DurabilityHelper
{
    public static int GetRealDurability(GameItem item) => item.GetTagReadonly("DURABILITY_TAG").GetInt();
    public static int GetFakeDurability(GameItem item) => item.GetTagReadonly("DURABILITY_FAKE_TAG").GetInt();
    public static int GetMaxDurability(GameItem item) => item.GetTagReadonly("MAX_DURABILITY_TAG").GetInt();
    public static int GetDefDurability(GameItem item) => item.GetTagReadonly("DEFAULT_DURABILITY_TAG").GetInt();
    public static void SetDurability(GameItem item, int real, int fake, int max, int def)
    {
        item.ModifyTag("DURABILITY_TAG", tag => tag.Enable().SetInt(real));
        item.ModifyTag("DURABILITY_FAKE_TAG", tag => tag.Enable().SetInt(fake));
        item.ModifyTag("MAX_DURABILITY_TAG", tag => tag.Enable().SetInt(max));
        item.ModifyTag("DEFAULT_DURABILITY_TAG", tag => tag.Enable().SetInt(def));
        var feature = item.FindItemFeatureByID("DURABILITY_TAG");
        feature.isFeatureMatch = real == fake;
        feature.publicDisplay = $"{fake}/{max}";
        feature.actualDisplay = $"{real}/{max}";
        feature.pricingBase = item.unitValue;
    }
}
public static class ArmorHelper
{
    public static void RepairArmorWithoutLoss(GameItem item) => item.ModifyTag("current_armor_value",
        tag => tag.Enable().SetInt(item.GetTagReadonly("current_max_armor_value").GetInt()));
}
public static class ItemFeatureList
{
    public enum EquipmentCondition { New, Excellent, Good, Fair, Poor }
    public static ItemFeature LastStampAuthenticity;
    public static ItemFeature StampAuthenticity() => LastStampAuthenticity = new() { category = "CATEGORY_GENUINE_STAMP" };
}
public static class GunFrameHelper
{
    public static ItemFeatureList.EquipmentCondition GetCondition(GameItem item) =>
        Enum.TryParse(item.FindItemFeatureByCategory("CATEGORY_EQUIPMENT_CONDITION")?.realCondition?.identifier,
            out ItemFeatureList.EquipmentCondition condition) ? condition : ItemFeatureList.EquipmentCondition.Good;
}
public static class GunHelper
{
    public static void SetEquipmentCondition(GameItem item, ItemFeatureList.EquipmentCondition condition)
    {
        item.itemFeatures.RemoveAll(feature => feature.category == "CATEGORY_EQUIPMENT_CONDITION");
        item.AddItemFeature(new ItemFeature { identifier = "CATEGORY_EQUIPMENT_CONDITION", category = "CATEGORY_EQUIPMENT_CONDITION",
            realCondition = new() { identifier = condition.ToString() }, fakeCondition = new() { identifier = condition.ToString() }, isFeatureMatch = true });
        UpdateCurrentGunStats(item);
    }
    public static void UpdateCurrentGunStats(GameItem item)
    {
        item.GunRefreshes++;
        int percentage = new[] { 130, 115, 100, 85, 70 }[(int)GunFrameHelper.GetCondition(item)];
        foreach (string stat in new[] { "FIREPOWER", "STYLE", "RELIABILITY", "RECOIL", "ERGONOMICS", "ACCURACY" })
            item.ModifyTag("GUN_CURRENT_" + stat + "_TAG", tag => tag.Enable().SetInt(item.GetTagReadonly("GUN_" + stat + "_TAG").GetInt() * percentage / 100));
    }
}
public static class UseCountHelper
{
    public static void ModMaxUseCount(GameItem item, int amount)
    {
        int max = Math.Max(0, item.GetTagReadonly("MAX_USE_COUNT_INT").GetInt() + amount);
        item.ModifyTag("MAX_USE_COUNT_INT", tag => tag.Enable().SetInt(max));
        item.ModifyTag("CURRENT_USE_COUNT_INT", tag => tag.Enable().SetInt(Math.Min(max, tag.GetInt())));
    }
    public static void SetCurrentUseCount(GameItem item, int amount)
    {
        item.ModifyTag("CURRENT_USE_COUNT_INT", tag => tag.Enable().SetInt(Math.Clamp(amount, 0, item.GetTagReadonly("MAX_USE_COUNT_INT").GetInt())));
        if (item.GetTagReadonly("USE_COUNT_AUTO_VALUE_BOOL").valueBool)
            item.unitValue = item.GetTagReadonly("USE_COUNT_BASE_VALUE_INT").GetInt()
                + amount * item.GetTagReadonly("USE_COUNT_VALUE_PER_USE_INT").GetInt();
    }
}
public static class FoodItemHelper
{
    public static void InitFreshFood(GameItem item, int value)
    {
        item.ModifyTag("FRESH_FOOD_TAG", tag => tag.Enable());
        item.ModifyTag("FRESHNESS_TAG", tag => tag.Enable().SetInt(value));
    }
    public static void SetCalories(GameItem item, int value) => item.ModifyTag("CALORIE_VALUE_TAG", tag => tag.Enable().SetInt(value));
}
public static class ModuleEffectHelper
{
    public class ModuleEffect
    {
        public enum ModuleEffectType { Universal, ModuleOnly, NodeOnly }
        public ModuleEffectType effectType;
        public string displayName;
        public List<string> incompatibleWithEffects = new();
        public Action<GameItem, int> onCreateAction;
        public Action<GameItem> onRemoveAction;
    }
    public static Dictionary<string, ModuleEffect> moduleEffects = new();
    public static void ResetAllTempStat(GameItem item)
    {
        foreach (string stat in new[] { "PERFORMANCE", "EFFICIENCY", "QUALITY" })
            item.ModifyTag("TEMP_PERCENTAGE_" + stat + "_INT", tag =>
                tag.SetInt(item.GetTagReadonly("BONUS_PERCENTAGE_" + stat + "_INT").GetInt()));
    }
}
namespace UnityEngine
{
    public class Sprite { }
    public class GameObject { public bool Active = true, Destroyed; }
    public static class Object { public static void Destroy(GameObject value) => value.Destroyed = true; }
}
namespace NicoItemManager
{
    internal static class NativeItemNames
    {
        internal static ItemNames Load()
        {
            var en = new Dictionary<string, string> { ["tool_name"] = "Sample tool", ["custom_name"] = "Custom machine" };
            var zh = new Dictionary<string, string> { ["tool_name"] = "示例工具", ["custom_name"] = "自定义机器" };
            var names = new ItemNames();
            names.AddSource("", en, zh, en);
            return names;
        }
    }
    internal static class NicoItemManagerMod { internal const string Id = "nicokobo.item-manager"; internal static FixtureLog Log = new(); }
    internal sealed class FixtureLog { internal void Warning(string value) { } }
}
