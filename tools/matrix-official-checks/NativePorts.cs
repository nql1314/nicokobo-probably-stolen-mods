using System.Reflection;
using HarmonyLib;
using MechcoreProtocol.ModuleMatrix;

// Minimal data/graph/native-operation ports. The actual Matrix implementation is
// linked unchanged; Unity rendering and real save IO are outside this executable.
public sealed class TagState
{
    public bool Enabled;
    public int valueInt;
    public string valueString = "";
    public TagState Enable() => SetEnabled(true);
    public TagState SetEnabled(bool value) { Enabled = value; return this; }
    public TagState SetInt(int value) { valueInt = value; return this; }
    public TagState SetString(string value) { valueString = value; return this; }
    public int GetInt() => valueInt;
    public string GetString() => valueString;
}
public sealed class TagSystem
{
    public Dictionary<string, TagState> Tags = new();
    public TagState Tag(string key) => Tags.TryGetValue(key, out var value) ? value : Tags[key] = new();
}
public sealed class GameItem : GraphNodeStorage
{
    public PixelWindow? contentWindow, examineWindow;
    public List<GraphNodeStorage> children => contentWindow == null ? new() : new() { contentWindow };
    public void SetContentWindow(PixelWindow? window)
    { contentWindow?.RemoveParent(this); contentWindow = window; window?.AddParent(this); }
    public void SetExamineWindow(PixelWindow? window)
    { examineWindow?.RemoveParent(this); examineWindow = window; window?.AddParent(this); }
    public string identifier = "", name = "", shortDescription = "";
    public long unitValue, unitBaseValue, uniqueId;
    public int unitCount = 1;
    public bool Owned = true, Destroyed;
    public HashSet<string> Types = new();
    public TagSystem state = new();
    public GameInventory? parentInventory;
    public object?[] Storages = new object?[6];
    public GridShape shape = new("1", 1), modifiedShape = new("1", 1);
    public Action<GameItem>? onLoaded;
    public Func<GameItem, GameItem, bool>? mayStackItemFunc;
    public Action<GameItem, GameInventory, SlotMarker>? onCycleEndSlotItemFunc;
    public TagState GetTagReadonly(string key) => state.Tag(key);
    public void ModifyTag(string key, Action<TagState> update) => update(state.Tag(key));
    public bool IsTag(string key) => state.Tags.TryGetValue(key, out var tag) && tag.Enabled;
    public void EnableTag(string key) => state.Tag(key).Enable();
    public void SetGameItemType(string type) => Types.Add(type);
    public void Target(GameItem target) { }
    public void SetUnitCount(int count) => unitCount = count;
    public void RemoveGameItemType(string type) => Types.Remove(type);
    public bool IsGameItemType(string type) => Types.Contains(type);
    public void SetShape(GridShape data) { shape = data; modifiedShape = data; }
    public void SetSpriteAndShapeFromMod(string mod, string sprite) { }
    public List<GameItem> FindAllChildItems() => Storages.OfType<GameInventory>().SelectMany(inv => inv.childItems)
        .SelectMany(item => new[] { item }.Concat(item.FindAllChildItems())).ToList();
    public void Destroy() { parentInventory?.Expel(this); Destroyed = true; }
}
public sealed class GridShape
{
    public string Data;
    public int width, height, minX, minY, orientation;
    public bool flipped;
    public int globalWidth => orientation % 2 == 0 ? width : height;
    public int globalHeight => orientation % 2 == 0 ? height : width;
    public int maxX => minX + globalWidth - 1;
    public int maxY => minY + globalHeight - 1;
    public GridShape(string data, int width) { Data = data; this.width = width; height = data.Length / width; }
    public byte Get(int x, int y)
    {
        x -= minX; y -= minY;
        for (int sy = 0; sy < height; sy++)
        for (int sx = 0; sx < width; sx++)
        {
            int lx = flipped ? width - 1 - sx : sx;
            (int tx, int ty) = (orientation % 4) switch
            { 1 => (sy, width - 1 - lx), 2 => (width - 1 - lx, height - 1 - sy), 3 => (height - 1 - sy, lx), _ => (lx, sy) };
            if (tx == x && ty == y) return (byte)(Data[sy * width + sx] - '0');
        }
        return 1;
    }
}
public sealed class GridShapeBuilder
{
    private string _data = "1";
    private int _width = 1, _x, _y, _rotation;
    private bool _flip;
    public GridShapeBuilder() { }
    public GridShapeBuilder(GridShape shape) { _data = shape.Data; _width = shape.width; }
    public GridShapeBuilder SetData(string data, int width) { _data = data; _width = width; return this; }
    public GridShapeBuilder SetTransform(int x, int y, bool flipped, int rotation)
    { _x = x; _y = y; _flip = flipped; _rotation = rotation % 4; return this; }
    public GridShape Build() => new(_data, _width) { minX = _x, minY = _y, flipped = _flip, orientation = _rotation };
}
public class GameInventory : GraphNodeStorage
{
    public List<GameItem> childItems = new();
    public List<GraphNodeStorage> children => childItems.Cast<GraphNodeStorage>().ToList();
    public GameItem? Parent;
    public Func<GameItem, GameInventory, bool>? mayInventoryAddItemFunc;
    public GameItem? GetParentItem() => Parent;
    public bool Expel(GameItem item) { if (!childItems.Remove(item)) return false; item.parentInventory = null; return true; }
    public void Add(GameItem item, int x = 0, int y = 0)
    {
        item.parentInventory?.Expel(item); childItems.Add(item); item.parentInventory = this;
        item.modifiedShape = new GridShapeBuilder(item.shape).SetTransform(x, y, false, 0).Build();
    }
}
public sealed class GameSlotInventory : GameInventory { public GameItem? childItem => childItems.FirstOrDefault(); }
public sealed class GameGridInventory : GameInventory
{
    public GridShape inventoryShape;
    public GameGridInventory(int width = 7, int height = 5) => inventoryShape = new(new string('0', width * height), width);
    public bool CheckShapePlacement(GameItem item, GridShape shape, List<GameItem> ignored)
    {
        for (int y = shape.minY; y <= shape.maxY; y++)
        for (int x = shape.minX; x <= shape.maxX; x++)
        {
            if (shape.Get(x, y) != 1) continue;
            if (x < 0 || y < 0 || x >= inventoryShape.width || y >= inventoryShape.height || inventoryShape.Get(x, y) != 0) return false;
            if (childItems.Except(ignored).Any(other => x >= other.modifiedShape.minX && x <= other.modifiedShape.maxX &&
                y >= other.modifiedShape.minY && y <= other.modifiedShape.maxY && other.modifiedShape.Get(x, y) == 1)) return false;
        }
        return true;
    }
}
public sealed class SlotMarker { }
public static class MachineFurnace
{
    public static GameItem Furnace() => throw new NotSupportedException("Native factory signature only; checks invoke the real Matrix factory guard around explicit native-operation ports.");
}
public static class GeneralHelper { public static bool IsItemOwned(GameItem item) => item.Owned; }
public static class ItemDirectory { public static GameItem CreateEmptyItem(string id) => new() { identifier = id }; }
public static class DirectoryMaster
{
    public static Dictionary<string, Func<GameItem>> Factories = new();
    public static List<GameItem> Probes = new();
    private static long _id;
    public static bool Has<T>(string id) => Factories.ContainsKey(id);
    public static GameItem Item(string id, bool owned = true)
    {
        var item = Factories.TryGetValue(id, out var factory) ? factory() : new GameItem { identifier = id };
        item.identifier = id; item.Owned = owned; item.uniqueId = ++_id; Probes.Add(item);
        Ports.Hook(typeof(MatrixCreatedPatch), "Postfix", item); return item;
    }
}
public static class ModuleHelper
{
    public static void InitModuleItem(GameItem item, string moduleType, int bonusPercentagePerformance = 0,
        int bonusPercentageEfficiency = 0, int bonusPercentageQuality = 0, bool isShipModule = true, string additionalEffectDisplay = "", int tier = 1)
    {
        item.SetGameItemType("MODULE"); item.EnableTag("MODULE_TAG"); item.EnableTag(moduleType);
        item.ModifyTag("MODULE_TYPE", tag => tag.Enable().SetString(moduleType));
        item.ModifyTag("BONUS_PERCENTAGE_PERFORMANCE_INT", tag => tag.Enable().SetInt(bonusPercentagePerformance));
        item.ModifyTag("BONUS_PERCENTAGE_EFFICIENCY_INT", tag => tag.Enable().SetInt(bonusPercentageEfficiency));
        item.ModifyTag("BONUS_PERCENTAGE_QUALITY_INT", tag => tag.Enable().SetInt(bonusPercentageQuality));
        ModuleEffectHelper.ResetAllTempStat(item);
    }
    public static void ComputeModuleEffect(GameInventory grid, GameItem? changed, GameItem machine)
        => ComputeModuleEffect(grid.childItems, machine);
    public static void ComputeModuleEffect(List<GameItem> items, GameItem machine)
    {
        MatrixActiveModulePatch.Prefix(ref items);
        foreach (string attribute in MatrixEffects.Attributes)
            machine.ModifyTag("TOTAL_" + attribute, tag => tag.SetInt(items.Sum(item => item.GetTagReadonly("TEMP_PERCENTAGE_" + attribute + "_INT").GetInt())));
    }
    public static void ApplyBasicModuleEffect(GameInventory grid, GameItem? changed, GameItem machine) { }
}
public static class ModuleEffectHelper
{
    public sealed class ModuleEffect
    {
        public enum ModuleEffectType { Universal, ModuleOnly, NodeOnly }
        public ModuleEffectType effectType;
        public string identifier = "", displayName = "", description = "";
        public List<string> incompatibleWithEffects = new();
        public Action<GameItem, GameInventory>? onAnyAction;
        public Action<GameItem>? onUsedAction;
    }
    public static Dictionary<string, ModuleEffect> moduleEffects = new();
    public static void InitNode(GameItem item) { InitModule(item); item.SetGameItemType("NODE"); item.RemoveGameItemType("MODULE"); }
    private static void InitModule(GameItem item) => ModuleHelper.InitModuleItem(item, "MODULE_TYPE_UNIVERSAL");
    public static void AddEffectToModule(GameItem item, string id)
    { string key = item.IsTag("MODULE_EFFECT1_TAG") ? "MODULE_EFFECT2_TAG" : "MODULE_EFFECT1_TAG"; item.ModifyTag(key, tag => tag.Enable().SetString(id)); }
    public static void ResetAllTempStat(GameItem item)
    { foreach (string attribute in MatrixEffects.Attributes) item.ModifyTag("TEMP_PERCENTAGE_" + attribute + "_INT", tag => tag.SetInt(item.GetTagReadonly("BONUS_PERCENTAGE_" + attribute + "_INT").GetInt())); }
    public static void ComputeAllModuleTempStat(List<GameItem> items)
    {
        MatrixEffects.DisabledStats? saved = null;
        try
        {
            MatrixNodeAggregatePatch.Prefix(items, out saved);
            foreach (var item in items) ResetAllTempStat(item);
            foreach (var item in items) HandleModuleEffectAction(item, item.parentInventory!);
            MatrixNodeAggregatePatch.Postfix(items, saved);
        }
        finally { MatrixNodeAggregatePatch.Finalizer(null, saved); }
    }
    public static void HandleModuleEffectAction(GameItem item, GameInventory inventory)
    {
        if (!MatrixInactiveEffectPatch.Prefix(item)) return;
        foreach (string slot in new[] { "MODULE_EFFECT1_TAG", "MODULE_EFFECT2_TAG" })
            if (item.IsTag(slot) && moduleEffects.TryGetValue(item.GetTagReadonly(slot).GetString(), out var effect)) effect.onAnyAction?.Invoke(item, item.parentInventory!);
    }
    public static void OnAddedModuleToGrid(GameItem item, GameItem machine, List<GameItem> items) { }
    public static void OnRemovedModuleFromGrid(GameItem item, GameItem machine, List<GameItem> items) { }
    public static void OnUsedLearningAlgo(GameItem item) { }
    public static void CreateModuleEffectTooltip(RichTextBuilder builder, GameItem item) { }
    public static void ActivateOnUseAction(GameItem item, GameInventory grid, GameItem machine)
    {
        if (!MatrixContinuousWorkPatch.Prefix(item, grid, machine)) return;
        foreach (string slot in new[] { "MODULE_EFFECT1_TAG", "MODULE_EFFECT2_TAG" })
            if (item.IsTag(slot) && moduleEffects.TryGetValue(item.GetTagReadonly(slot).GetString(), out var effect)) effect.onUsedAction?.Invoke(item);
    }
    public static void InitRandomEffect(GameItem item, int count = -1) { }
}
public static class MachineHelper
{
    public static object? GetBodyStorage(GameItem machine, int index) => machine.Storages[index];
    public static void ExpandModule(GameGridInventory moduleGrid, GameItem item) { }
}
public static class MachineryHelper
{
    public static int GetCurrentPerformanceBonus(GameItem machine) => machine.GetTagReadonly("performance").GetInt();
    public static int GetCurrentQualityBonus(GameItem machine) => machine.GetTagReadonly("quality").GetInt();
}
public sealed class Liquid
{
    public string identifier = "";
    public bool canBeRemovedByPurifier;
    public static List<Liquid> Liquids = new();
    public static string GetCurrentPartFromId(string id) => "liquid:" + id;
}
public static class WaterHelper
{
    public static int GetTotalVolume(GameItem item) => Liquid.Liquids.Sum(liquid => item.GetTagReadonly(Liquid.GetCurrentPartFromId(liquid.identifier)).GetInt());
    public static int GetFreeCapacity(GameItem item) => item.GetTagReadonly("capacity").GetInt() - GetTotalVolume(item);
    public static int RemoveContaminantFromContainer(GameItem item, string id, int amount, int unused)
    { var tag = item.GetTagReadonly(Liquid.GetCurrentPartFromId(id)); int removed = Math.Min(tag.GetInt(), amount); tag.SetInt(tag.GetInt() - removed); return removed; }
    public static void AddLiquid(GameItem item, string id, int amount)
    { var tag = item.GetTagReadonly(Liquid.GetCurrentPartFromId(id)); tag.SetInt(tag.GetInt() + amount); }
    public static void TransferLiquid(GameItem source, GameItem target)
    {
        int total = GetTotalVolume(source); int amount = Math.Min(total, GetFreeCapacity(target));
        foreach (var liquid in Liquid.Liquids)
        { int move = total == 0 ? 0 : source.GetTagReadonly(Liquid.GetCurrentPartFromId(liquid.identifier)).GetInt() * amount / total;
            RemoveContaminantFromContainer(source, liquid.identifier, move, 0); AddLiquid(target, liquid.identifier, move); }
    }
}
public static class MachinePurifier
{
    // Use the installed game's pure calculation so the cap is not duplicated here.
    public static Func<int, int> NativePurityCap = null!;
    public static int GetMaxPurityCap(int bonusQuality) => NativePurityCap(bonusQuality);
    public static int PurifyContainer(GameItem machine, GameItem waterContainer, bool ignoreBonus = false) => 0;
}
public static class MachineMoistureFarm { public static void Fill(string quality, GameItem container, int volume) { } }
public static class MachineDesequencer { public static void FinishDesequencer(GameItem item, GameSlotInventory input, GameSlotInventory output) { } }
public static class MachineProgressHelper { public static void ContinueProgressTypeMachine(GameItem machine) { } }
public sealed class PlayerStore
{
    public static PlayerStore? Current;
    public static bool HasInstance => Current != null;
    public static PlayerStore Instance => Current ??= new();
    public string runID = "matrix-test";
    public int saveSlotId;
    public List<GameItem> Items = new();
    public List<string> Stock = new(), NightLog = new();
    public List<GameItem> FindAllItem(bool isOwn = true) => Items.SelectMany(item => new[] { item }.Concat(item.FindAllChildItems())).Where(item => !isOwn || item.Owned).Distinct().ToList();
    public void AddDirectSellingItemToTable(string id) => Stock.Add(id);
    public void AddNightLog(string text, string color) => NightLog.Add(text);
    public List<KeyValuePair<string, int>> GetMarketModifierType(List<string> types) => new() { new("MATERIAL", 15) };
}
public static class StoreStation { public static int Day = 25; public static int GetDayCounter() => Day; }
public static class RNG
{
    public static bool Success = true;
    public static Queue<int> Indices = new();
    public static bool Roll(float chance) => chance > 0 && Success;
    public static int GetRandomIntExculsive(int min, int max) => Indices.Count > 0 ? Indices.Dequeue() : min;
    public static double GetRandomDouble(double min, double max) => min;
}
public static class TypeHelper { public static List<string> MAJOR_TYPES_LIST = new() { "FOOD", "MATERIAL" }; public static string GetTypeDisplayName(string id) => id; }
public sealed class RichTextBuilder
{
    public List<string> Lines = new();
    public void AddLine(string text, bool ignoreEmpty = false, object? color = null, bool bold = false) => Lines.Add(text);
}
public static class RenderHandler { public static class ColorPalette { public static readonly object LightBlue = new(); } }
public interface IMod { void Init(ModManifest manifest); void OnEnable(); void OnDisable(); }
public sealed class ModManifest { public string ID = ""; }
public sealed class ModItemDirectory
{
    public bool Add(string id, Func<GameItem> factory) => DirectoryMaster.Factories.TryAdd(id, factory);
}
public static class TableMaster { public const string allModuleTable = "allModuleTable", t1moduleTable = "t1moduleTable"; }
public static class LootRegistry { public static List<(string Table, string Id, float Weight)> Entries = new(); public static void AddEntry(string owner, string table, string id, float weight) => Entries.Add((table, id, weight)); }
public sealed class LootEntry
{
    public string id;
    public float weight;
    public string sourceModId, modifiedByModId = "";
    public LootEntry(string id, float weight, string? sourceModId = null)
    { this.id = id; this.weight = weight; this.sourceModId = sourceModId ?? ""; }
}
public sealed class TestLocale { public TestIdentifier Identifier = new(); }
public sealed class TestIdentifier { public string Code = "en"; }
public abstract class ConfigEntry { }
public sealed class ConfigEntry<T> : ConfigEntry { public T Value; public ConfigEntry(T value) => Value = value; }
public sealed class ModConfig
{
    public static Dictionary<string, bool> Flags = new();
    public ConfigEntry? Get(string key) => null;
    public ConfigEntry<T> Define<T>(string key, T value, string category, Func<string> name, Func<string>? description = null, bool requiresRestart = false) =>
        new(typeof(T)==typeof(bool) && Flags.TryGetValue(key,out bool flag) ? (T)(object)flag : value);
}
public static class ModHelper
{
    public static ModConfig GetConfig(string id) => new();
    public static TestLocale Locale = new();
    public static TestLocale GetCurrentLocale() => Locale;
    public static Func<string,string,string>? Localize;
    public static string GetLocalized(string id, string key, params object[] args)
    {
        string text = Localize?.Invoke(id, key) ?? key;
        return args.Length == 0 ? text : string.Format(text, args);
    }
    public static void SetSource(GameItem item, string owner, string name) { }
    public static string GetCurrentAfterhourLocationId() => AfterhourLocation.DR_JACKSON;
}
public static class AfterhourLocation { public const string DR_JACKSON = "jackson"; }
public static class ModHook
{
    public static event Action<ModItemDirectory>? OnModItemDirectoryInit;
    public static event Action<LoadedMod>? OnModAssetsLoaded;
    public static event Action? OnLootTablesLoaded, OnGameLoadedLate, OnGoingSleepLate;
    public static event Action<string>? OnNewGameStarted;
    public static event Action<List<GameItem>>? OnPlaceInventorInventoryItemLate;
    public static event Action<RichTextBuilder, GameItem>? OnCreateTooltipEarly, OnCreateTooltipLate;
    public static void Register() => OnModItemDirectoryInit?.Invoke(new());
    public static void Loot() => OnLootTablesLoaded?.Invoke();
    public static void NightShop() => OnPlaceInventorInventoryItemLate?.Invoke(new());
    public static void Loaded() => OnGameLoadedLate?.Invoke();
    public static void NewGame(string scenario) => OnNewGameStarted?.Invoke(scenario);
    public static void Night() => OnGoingSleepLate?.Invoke();
    public static void Assets(LoadedMod mod) => OnModAssetsLoaded?.Invoke(mod);
    public static void Tooltip(RichTextBuilder text, GameItem item) { OnCreateTooltipEarly?.Invoke(text,item); OnCreateTooltipLate?.Invoke(text,item); }
}
namespace UnityEngine { public static class Debug { public static void LogWarning(object text) { } } }

public static class InvokeFuncExtensions
{
    public static bool BoolReduceOR(bool a, bool b) => a | b;
    public static bool InvokeAllReduce(this Func<GameItem, GameInventory, bool> func, GameItem item, GameInventory inventory,
        Func<bool, bool, bool> reduce) => func.GetInvocationList().Cast<Func<GameItem, GameInventory, bool>>()
        .Select(callback => callback(item, inventory)).Aggregate(reduce);
}
public static class Ports
{
    public static object? Hook(Type type, string name, params object?[] args) =>
        type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
}
namespace Nicokobo.ContentInterop
{
    // The operation ports above invoke the real patch methods directly.
    // Unity localization scheduling and native detours are outside this harness.
    internal sealed class MechanicLocalizedPatches
    {
        internal MechanicLocalizedPatches(string modId, string patchGroup, Action<Harmony> install) { }
        internal void Start() { }
        internal void Stop() { }
    }
}
namespace MechcoreProtocol.SynthesisExpansion.Definitions
{
    // Only the real synthesis recipe identities/ingredients are used by this
    // executable. Matrix does not invoke synthesis production pricing.
    public static class RecipeConfiguration
    {
        public static int ProductionMarkupPercent(string machine) => throw new NotSupportedException("Synthesis pricing is covered by synthesis-official-checks");
    }
}
