using Nicokobo.FoodProcessing.Definitions;

// These ports exercise resource and rollback behavior without initializing
// Unity or loading a player save. API signatures are checked against game IL.
public sealed class TagState
{
    public bool Enabled;
    public int valueInt;
    public string? valueString;
    private bool _bool;
    public TagState Enable() => SetEnabled(true);
    public TagState SetEnabled(bool enabled) { Enabled = enabled; return this; }
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

public class GameItem : GraphNodeStorage
{
    public List<GraphNodeStorage> children => contentWindow == null ? new() : new() { contentWindow };
    public PixelWindow? examineWindow;
    public void SetExamineWindow(PixelWindow? window)
    { examineWindow?.RemoveParent(this); examineWindow = window; window?.AddParent(this); }
    public string identifier = "", name = "", shortDescription = "", spriteAtlasPath = "", spritePath = "";
    public bool useSpriteFromMod;
    public string GetDisplayName() => name;
    public int unitCount = 1;
    public long unitValue, lateUnitValue, unitBaseValue;
    public long uniqueId;
    public HashSet<string> Types = new();
    public ItemWindow contentWindow = new();
    public Action<GameItem>? onLoaded;
    public bool RefuseRemoval, ThrowOnModify;
    public int InnatePercent;
    public bool Destroyed;
    public bool ThrowOnDestroy;
    public List<ItemFeature> itemFeatures = new();
    public object?[] Storages = new object?[5];
    public int ActionCalls;
    public Action<GameItem, GameGridInventory, SlotMarker>? onCycleEndSlotItemFunc;
    public ItemFeature? FindItemFeatureByID(string id) => itemFeatures.FirstOrDefault(feature => feature.identifier == id);
    public void AddItemFeature(ItemFeature feature) => itemFeatures.Add(feature);
    public void RemoveItemFeatureByID(string id) => itemFeatures.RemoveAll(feature => feature.identifier == id);
    public GameGridInventory? parentInventory;
    public TagSystem state = new();
    public Func<GameItem, GameItem, bool>? mayStackItemFunc;
    public TagState GetTagReadonly(string key) => state.GetTag(key);
    public bool IsTag(string key) => state.Tags.TryGetValue(key, out var tag) && tag.Enabled;
    public void EnableTag(string key) => ModifyTag(key, tag => tag.Enable());
    public void ModifyTag(string key, Action<TagState> action)
    { if (ThrowOnModify) { ThrowOnModify = false; throw new InvalidOperationException("Simulated state write failure"); } action(state.GetTag(key)); }
    public void SyncModifiedState() { }
    public bool IsGameItemType(string type) => Types.Contains(type);
    public void SetGameItemType(string type) => Types.Add(type);
    public GameItem SetContentWindow(ItemWindow? window)
    {
        contentWindow?.RemoveParent(this);
        contentWindow = window!;
        if (window == null) { Storages = new object?[5]; return this; }
        window.AddParent(this);
        Storages = window.Inventories.Cast<object?>().Concat(Enumerable.Repeat<object?>(null, 5)).Take(5).ToArray();
        foreach (var inventory in window.Inventories) inventory.ParentItem = this;
        return this;
    }
    public void SetUnitCount(int count) => unitCount = count;
    public List<GameItem> FindAllChildItems() => contentWindow == null ? new() : contentWindow.Inventories.SelectMany(inv => inv.childItems)
        .SelectMany(item => new[] { item }.Concat(item.FindAllChildItems())).Distinct().ToList();
    public bool MayRemove() => !RefuseRemoval;
    public SlotMarker? GetIncompleteSlotInInventory() => parentInventory == null ? null : new(parentInventory, this, parentInventory.childItems.IndexOf(this));
    public bool MayTarget(GameItem target) => false;
    public bool CanTarget(GameItem target) => false;
    public void Target(GameItem target) { }
    public void Destroy()
    {
        parentInventory?.Expel(this); Destroyed = true;
        if (ThrowOnDestroy) throw new InvalidOperationException("Simulated cleanup failure");
    }
    private void AccumulateFeatureStages(ref long innate, ref long market, ref double final, bool skipEvents)
    { innate = InnatePercent + itemFeatures.Where(feature => feature.valueStage == ItemFeature.ValueStage.Innate).Sum(feature => feature.GetActualValueModifier()); market = 900; final = 1; }
    private static long ComposeStagedValue(long raw, long innate, long market, double final, int misc, int containerPrice)
        => (long)Math.Round(((Math.Max(0d, raw * (1 + innate / 100d)) + containerPrice) *
            Math.Max(0d, 1 + market / 100d) + misc) * final, MidpointRounding.ToEven);
    public long GetCurrentValue() => unitValue;
    public long GetNegociatedValue() => unitValue;
    public void OnAddedToWeightedArea() { }
    public void OnRemovedFromWeightedArea() { }
    public long GetRefreshedValue() => unitValue;
    public long GetBaseValue() => unitValue;
}

public class GameInventory : GraphNodeStorage
{
    public readonly List<GameItem> childItems = new();
    public List<GraphNodeStorage> children => childItems.Cast<GraphNodeStorage>().ToList();
    public int Capacity = int.MaxValue;
    public bool RefuseExpel;
    public string identifier = "", name = "", shortDescription = "", spriteAtlasPath = "", spritePath = "";
    public bool useSpriteFromMod;
    public string GetDisplayName() => name;
    public GameItem? ParentItem;
    public bool overrideLockInsert;
    public Func<GameItem, GameInventory, bool>? mayInventoryAddItemFunc;
    public GameItem? GetParentItem() => ParentItem;
    public bool UncheckedAccept(GameItem item)
    {
        if (childItems.Count >= Capacity) return false;
        if (item.parentInventory != null && !item.parentInventory.Expel(item)) return false;
        childItems.Add(item); item.parentInventory = (GameGridInventory)this; return true;
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
    public InventoryShape inventoryShape;
    public GameGridInventory(int width = 8, int height = 8) => inventoryShape = new(width, new string('0', width * height));
    public GameGridInventory(string data, int width) => inventoryShape = new(width, data);
    public void SetShape(string data, int width) => inventoryShape = new(width, data);
    public SlotMarker? TryFindOneValidInventorySlot(GameItem item) => childItems.Count >= Math.Min(Capacity,
        inventoryShape.Data.Count(cell => cell == '0')) ? null : new(this, item, childItems.Count);
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

public static class DirectoryMaster
{
    public static readonly List<GameItem> Created = new();
    public static bool FailCleanup;
    public static Func<string, GameItem?>? Factory;
    public static Func<string, bool>? AvailableFactory;
    public static bool Has<T>(string id) => AvailableFactory?.Invoke(id) ?? true;
    public static T Create<T>(string id) => (T)(object)(Factory?.Invoke(id) ?? new GameItem { identifier = id });
    public static GameItem Item(string id, bool unused = false)
    {
        var item = Factory?.Invoke(id) ?? new GameItem { identifier = id, ThrowOnDestroy = FailCleanup };
        if (id == "metal_ingot") { item.unitValue = 100; IngotPurityHelper.InitIngotItem(item); }
        Created.Add(item); return item;
    }
}

public static class PowerHelper
{
    public static bool RefuseDraw, ThrowAfterDebit, RefuseRestore;
    public static int DrawCalls;
    public static int GetPowerSourceItemValue(GameItem item) => 0;
    public static bool CanDrawPowerSource(GameItem item, int power) => HypertechBatteryHelper.IsHypertechBattery(item)
        ? power <= HypertechBatteryHelper.GetOutput(item) : CurrentConverterHelper.IsCurrentConverter(item)
        ? CurrentConverterHelper.CanSupply(item, power) : item.GetTagReadonly("energy").valueInt >= power;
    public static bool DrawPowerSource(GameItem item, int power)
    {
        DrawCalls++;
        if (RefuseDraw) return false;
        if (HypertechBatteryHelper.IsHypertechBattery(item)) return power <= HypertechBatteryHelper.GetOutput(item);
        if (CurrentConverterHelper.IsCurrentConverter(item)) return CurrentConverterHelper.Supply(item, power);
        item.GetTagReadonly("energy").valueInt -= power;
        if (ThrowAfterDebit) throw new InvalidOperationException("Simulated debit failure");
        return true;
    }
    public static int GetAvailableEnergyFromItem(GameItem item, bool unused) => item.GetTagReadonly("energy").valueInt;
    public static void SetPowerSourceAt(GameItem item, int value)
    { if (RefuseRestore) throw new InvalidOperationException("Simulated restore failure"); item.GetTagReadonly("energy").SetInt(value); }
    public static void UpdateEnergyCreditSprite(GameItem item) { }
}

public static class MachineryHelper
{
    public const string MACHINERY_DRAWN_POWER_BASE = "MACHINERY_DRAWN_POWER_BASE";
    public static int GetCurrentQualityBonus(GameItem item) => item.GetTagReadonly("quality").valueInt;
    public static int GetCurrentPerformanceBonus(GameItem item) => item.GetTagReadonly("performance").valueInt;
    public static int GetMachinePowerUsage(GameItem item) => item.GetTagReadonly("power").valueInt;
    public static void InitMachinery(GameItem item, int power)
    { item.GetTagReadonly("is_machinery_powered").SetBool(true); item.GetTagReadonly("power").SetInt(power); item.GetTagReadonly(MACHINERY_DRAWN_POWER_BASE).SetInt(power); }
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

public static class GutterflowPelletHelper
{
    public static bool IsTainted(GameItem item) => item.IsTag("GUTTERFLOW_TAINTED_TAG");
}
public sealed class Liquid
{
    public static readonly List<Liquid> Liquids = new();
    public string identifier = "", currentPart = "";
}
public static class GeneralHelper
{
    public static bool IsItemOwned(GameItem item) => item.GetTagReadonly("owned").GetBool();
    public static void SetItemOwned(GameItem item, bool owned) => item.GetTagReadonly("owned").SetBool(owned);
}
public static class MachineFridge
{
    public static bool IsChillable(GameItem item) => false;
    public static bool IsFreezable(GameItem item) => false;
    public static bool MayZoneAcceptItem(GameItem item) => false;
}

public sealed class ConfigEntry<T>(T value) { public T Value => value; }
public sealed class ModConfig
{
    public object? Get(string key) => null;
    public ConfigEntry<T> Define<T>(string key, T value, string section, Func<string> label) => new(value);
}

public static class ModHelper
{
    public static ModConfig GetConfig(string modId) => new();
    public static string GetLocalized(string modId, string key, params object[] args) => key;
    private static readonly Dictionary<string, string> Files = new();
    public static bool ModDataFileExists(string owner, string file) => Files.ContainsKey(owner + file);
    public static void WriteModDataFileText(string owner, string file, string content) => Files[owner + file] = content;
    public static string ReadModDataFileText(string owner, string file) => Files[owner + file];
}

namespace Nicokobo.FoodProcessing
{
    internal static class FoodMod
    {
        internal static void Warn(string text) { }
        internal static string T(string chinese, string english) => english;
    }
    internal static class FoodMachines
    {
        internal static bool Matches(GameItem item, RecipeRequirement requirement) => item.identifier == requirement.ResourceId;
        internal static int WaterQuality(GameItem item) => item.GetTagReadonly("quality").valueInt;
    }
}
