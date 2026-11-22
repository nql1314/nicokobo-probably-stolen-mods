using Nicokobo.FoodProcessing.Definitions;

// These ports exercise resource and rollback behavior without initializing
// Unity or loading a player save. API signatures are checked against game IL.
public sealed class TagState
{
    public int valueInt;
    public string? valueString;
    private bool _bool;
    public TagState Enable() => this;
    public TagState SetInt(int value) { valueInt = value; return this; }
    public int GetInt() => valueInt;
    public TagState SetString(string value) { valueString = value; return this; }
    public string? GetString() => valueString;
    public TagState SetBool(bool value) { _bool = value; return this; }
    public bool GetBool() => _bool;
    public TagState Clone() => (TagState)MemberwiseClone();
}

public sealed class TagSystem
{
    public Dictionary<string, TagState> Tags = new();
    public TagState GetTag(string key) => Tags.TryGetValue(key, out var value) ? value : Tags[key] = new();
    public TagSystem Clone() => new() { Tags = Tags.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()) };
}

public sealed class GameItem : GraphNodeStorage
{
    public PixelWindow? contentWindow, examineWindow;
    public List<GraphNodeStorage> children => contentWindow == null ? new() : new() { contentWindow };
    public void SetContentWindow(PixelWindow? window)
    { contentWindow?.RemoveParent(this); contentWindow = window; window?.AddParent(this); }
    public void SetExamineWindow(PixelWindow? window)
    { examineWindow?.RemoveParent(this); examineWindow = window; window?.AddParent(this); }
    public string identifier = "";
    public string spritePath = "";
    public GridShape modifiedShape = new();
    public TagSystem modifiedState => state;
    public int unitCount = 1;
    public long unitValue, lateUnitValue, unitBaseValue;
    public int InnatePercent;
    public bool Destroyed;
    public bool activateDefault;
    public bool ThrowOnDestroy;
    public GameInventory? parentInventory;
    public readonly Dictionary<int, GraphNodeStorage> Storages = new();
    public readonly HashSet<string> Types = new();
    public TagSystem state = new();
    public Func<GameItem, GameItem, bool>? mayStackItemFunc;
    public Action<GameItem>? onLoaded;
    public Action<GameItem, GameItem>? onItemTargetThisFunc;
    public Func<GameItem, GameInventory, SlotMarker, bool>? mayActivateSlotItemFunc, canActivateSlotItemFunc;
    public Action<GameItem, GameInventory, SlotMarker>? onActivateSlotItemFunc;
    public TagState GetTagReadonly(string key) => state.GetTag(key);
    public bool IsTag(string key) => state.Tags.ContainsKey(key);
    public bool IsGameItemType(string type) => Types.Contains(type);
    public bool MayRemove() => parentInventory is { RemoveLocked: false };
    public void ModifyTag(string key, Action<TagState> action) => action(state.GetTag(key));
    public void EnableManufacturer(string key) => GetTagReadonly("manufacturer").SetString(key);
    public void EnableTag(string key) => state.GetTag(key);
    public void DisableTag(string key) => state.Tags.Remove(key);
    public string GetDisplayName() => identifier;
    public void SyncModifiedState() { }
    public void SetUnitCount(int count) => unitCount = count;
    public List<GameItem> FindAllChildItems() => Storages.Values.Concat(contentWindow?.children ?? new()).OfType<GameInventory>().SelectMany(inventory =>
        inventory.childItems.SelectMany(item => new[] { item }.Concat(item.FindAllChildItems()))).ToList();
    public void Destroy()
    {
        parentInventory?.Expel(this); Destroyed = true;
        if (ThrowOnDestroy) throw new InvalidOperationException("Simulated cleanup failure");
    }
    private void AccumulateFeatureStages(ref long innate, ref long market, ref double final, bool skipEvents)
    { innate = InnatePercent; market = 900; final = 1; }
    private static long ComposeStagedValue(long raw, long innate, long market, double final, int misc, int containerPrice)
        => (long)(raw * (100 + innate + market) / 100m) + misc + containerPrice;
    public long GetCurrentValue() => unitValue;
    public long GetRefreshedValue() => unitValue;
    public long GetBaseValue() => unitValue;
}

public class GameInventory : GraphNodeStorage
{
    public readonly List<GameItem> childItems = new();
    public List<GraphNodeStorage> children => childItems.Cast<GraphNodeStorage>().ToList();
    public int Capacity = int.MaxValue;
    public bool RefuseExpel;
    public GameItem? ContainingItem;
    public bool RemoveLocked { get => overrideLockRemove; set => overrideLockRemove = value; }
    public bool overrideLockInsert, overrideLockRemove;
    public void LockInv() => overrideLockInsert = overrideLockRemove = true;
    public GameItem? GetParentItem() => ContainingItem;
    public bool IsRemoveLocked() => RemoveLocked;
    public bool UncheckedAccept(GameItem item)
    {
        if (childItems.Count >= Capacity) return false;
        item.parentInventory?.Expel(item);
        childItems.Add(item); item.parentInventory = this; return true;
    }
    public bool Expel(GameItem item)
    {
        if (RefuseExpel) return false;
        if (!childItems.Remove(item)) return false;
        item.parentInventory = null; return true;
    }
}

public class GameGridInventory : GameInventory
{
    public Func<GameItem, GameGridInventory, bool>? mayInventoryAddItemFunc;
    public int Width, Height;
    public SlotMarker? TryFindOneValidInventorySlot(GameItem item, bool keepOrientation = false) =>
        overrideLockInsert || childItems.Count >= Capacity || mayInventoryAddItemFunc?.Invoke(item, this) == false
            ? null : new SlotMarker(item, this);
    public SlotMarker? TryInventorySlot(GameItem item, int count, UnityEngine.Vector2 position, GridShape shape, TagSystem state) =>
        TryFindOneValidInventorySlot(item);
    public GameGridInventory SetShape(int width, int height)
    {
        Width = width;
        Height = height;
        return this;
    }
}

public sealed class GridShape
{
    public GridShape Clone() => new();
}

public sealed class SlotMarker(GameItem item, GameGridInventory destination)
{
    public readonly GameGridInventory inventory = destination;
    public GameItem? targetItem { get; init; }
    public int TryAcceptOnce(int count) => count == 1 && item.unitCount == 1 &&
        (item.parentInventory == null || item.parentInventory.Expel(item)) && inventory.UncheckedAccept(item) ? 1 : 0;
}

public sealed class GameSlotInventory : GameGridInventory
{
    public GameItem? childItem => childItems.FirstOrDefault();
}

public static class GraphUtils
{
    public static int TryAcceptOnce(GameGridInventory inventory, GameItem item) =>
        inventory.mayInventoryAddItemFunc?.Invoke(item, inventory) == false || !inventory.UncheckedAccept(item) ? 1 : 0;
}

public static class SaveManager
{
    public static bool IsDecoding { get; set; }
}

public static class DirectoryMaster
{
    public static readonly List<GameItem> Created = new();
    public static readonly HashSet<string> Known = new();
    public static bool FailCleanup;
    public static bool Has<T>(string id) => Known.Contains(id);
    public static T? Create<T>(string id) where T : class => Has<T>(id) ? MakeItem(id) as T : null;
    public static GameItem Item(string id, bool isOwned = true)
    {
        var item = MakeItem(id);
        Nicokobo.FoodProcessing.FoodCareKit.AddSpawnContents(item, isOwned);
        return item;
    }

    private static GameItem MakeItem(string id)
    {
        var definition = FoodDefinitions.Items.FirstOrDefault(value => value.Id == id);
        var item = new GameItem { identifier = id, ThrowOnDestroy = FailCleanup, unitValue = id switch
            { "serum_green" or "serum_red" => 100, "blue_blood_bag" => 250, "rubbing_alcohol" => 35,
                "cup_noodle" or "processed_cheese" => 20, "industrial_fish_feed" => 100,
                "nutrifruit" => 12, "meal_cap" => 19, _ => definition?.BaseValue ?? 0 } };
        if (definition != null) item.GetTagReadonly("MOD_ID_TAG").SetString(FoodDefinitions.ModId);
        if (id is "nutrifruit" or "meal_cap") { item.Types.Add("FOOD"); item.spritePath = id; }
        if (id is "serum_green" or "serum_red") item.GetTagReadonly("CURRENT_USE_COUNT_INT").SetInt(10);
        if (id == "processed_cheese")
        {
            item.Types.Add("FOOD");
            item.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(450);
        }
        if (id == "industrial_fish_feed")
        {
            item.EnableTag("INDUSTRIAL_FISH_FEED_TAG");
            item.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(5000);
        }
        if (FoodDefinitions.IsCareKit(id)) Nicokobo.FoodProcessing.FoodCareKit.Initialize(item);
        Created.Add(item); return item;
    }
}

public static class ModSourceHelper
{
    public static string? GetItemModName(GameItem item) => item.state.Tags.TryGetValue("MOD_ID_TAG", out var tag) ? tag.GetString() : null;
}

public static class WineHelper
{
    public static void SetBerryCondition(GameItem item, int condition, int visualVariant = 0) => throw new NotSupportedException();
    public static void FinishFermentation(GameItem item) => throw new NotSupportedException();
}

public sealed class ItemCondition
{
    public int modValue;
}

public static class ItemConditionList
{
    public static ItemCondition CreateBaseWater() => new();
    public static ItemCondition CreateHighQualityWater() => new() { modValue = 50 };
    public static ItemCondition CreatePureWater() => new() { modValue = 100 };
}

public static class PowerHelper
{
    public static bool RefuseDraw, ThrowAfterDebit;
    public static int GetPowerSourceItemValue(GameItem item) => 0;
    public static bool CanDrawPowerSource(GameItem item, int power) => item.GetTagReadonly("energy").valueInt >= power;
    public static bool DrawPowerSource(GameItem item, int power)
    {
        if (RefuseDraw) return false;
        item.GetTagReadonly("energy").valueInt -= power;
        if (ThrowAfterDebit) throw new InvalidOperationException("Simulated debit failure");
        return true;
    }
    public static void UpdateEnergyCreditSprite(GameItem item) { }
}

// Native boundary port: record the call and return a distinctive price. The
// checks assert native calorie writes followed by our remaining-value basis;
// the game's kcal-to-price implementation is not reimplemented here.


public static class MachineryHelper
{
    public const string MACHINERY_DRAWN_POWER_BASE = "MACHINERY_DRAWN_POWER_BASE";
    public static int GetMachinePowerUsage(GameItem item) => item.GetTagReadonly("power").valueInt;
    public static int GetCurrentQualityBonus(GameItem item) => item.GetTagReadonly("machine_quality").valueInt;
    public static int GetCurrentPerformanceBonus(GameItem item) => item.GetTagReadonly("performance").valueInt;
}

public static class WaterHelper
{
    public static int GetTotalVolume(GameItem item) => item.GetTagReadonly("water").valueInt;
    public static int GetContainerPrice(GameItem item) => 0;
    public static void Remove(GameItem item, int amount) => item.GetTagReadonly("water").valueInt -= amount;
}

public static class WaterFeatureHelper
{
    public static void OnLiquidContainerPourOutOf(GameItem item) { }
}

public static class GeneralHelper
{
    public static bool IsItemOwned(GameItem item) => item.GetTagReadonly("owned").GetBool();
    public static bool IsItemEffectivelyOwned(GameItem item) => IsItemOwned(item);
}

public static class MaterialDirectory
{
    public static bool CanIngotTarget(GameItem source, GameItem target) => throw new NotSupportedException();
}

public sealed class EmporiumAudioPlayer
{
    public static EmporiumAudioPlayer Instance { get; } = new();
    public readonly Sound ratchet = new();
    public sealed class Sound { public void Play() { } }
}

public static class MachineFridge
{
    public const string FRIDGE_TAG = "FRIDGE_TAG";
    public static bool IsChillable(GameItem item) => false;
    public static bool IsFreezable(GameItem item) => false;
    public static bool MayZoneAcceptItem(GameItem item) => false;
}

public static class GutterflowPelletHelper
{
    public static bool IsTainted(GameItem item) => item.IsTag("GUTTERFLOW_TAINTED_TAG");
}

public sealed class Liquid
{
    public string identifier = "", currentPart = "";
    public static readonly List<Liquid> Liquids = new[] { "water", "starch", "protein", "sugar", "mineral", "organic_waste",
        "microbe", "physical_contaminant", "fat", "coffee_mush", "caffeine", "plant_matter", "dream_cap_mush", "nightmare_cap_mush" }
        .Select(id => new Liquid { identifier = id, currentPart = "part." + id }).ToList();
}

public static class ModHelper
{
    public static string GetLocalized(string modId, string key, params object[] args) => key;
    private static readonly Dictionary<string, string> Files = new();
    public static bool ModDataFileExists(string owner, string file) => Files.ContainsKey(owner + file);
    public static void WriteModDataFileText(string owner, string file, string content) => Files[owner + file] = content;
    public static string ReadModDataFileText(string owner, string file) => Files[owner + file];
}

namespace Nicokobo.FoodProcessing
{
    internal static class FoodMod { internal static void Warn(string text) { } internal static string T(string chinese, string english) => chinese; }
    internal static class FoodMachines
    {
        internal static bool Matches(GameItem item, RecipeRequirement requirement) =>
            FoodIngredients.Matches(item, requirement, value => value.GetTagReadonly("genuine").GetBool());
        internal static int WaterQuality(GameItem item) => item.GetTagReadonly("quality").valueInt;
    }
}
