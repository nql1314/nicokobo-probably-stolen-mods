using MechcoreProtocol.SynthesisExpansion;

// Headless native boundaries: no game save or Unity objects are created here.
public sealed class InventoryShape
{
    public int width, height;
    public string Data;
    public InventoryShape(int width, string data) { this.width = width; Data = data; height = data.Length / width; }
    public byte GetLocal(int x, int y) => (byte)(Data[y * width + x] - '0');
}
public sealed class GameGridScrollableInventory : GameInventory { }
public sealed class GameCharacterRaidInventory : GameInventory { }
public sealed class MachineryPanel
{
    public object Element;
    public MachineryPanel(object element) => Element = element;
}
public sealed class ItemWindow : PixelWindow
{
    public override List<GraphNodeStorage> children => Inventories.Cast<GraphNodeStorage>().ToList();
    public List<GameInventory> Inventories = new();
    public ItemWindow(params MachineryPanel[] panels)
    { Inventories = panels.Select(panel => panel.Element).OfType<GameInventory>().ToList(); childElement = panels.FirstOrDefault()?.Element; }
}
public sealed class MachineryButton
{
    public string Text;
    public Action? Click;
    public MachineryButton(string text = "") => Text = text;
    public void SetText(string text) => Text = text;
    public void SetCallback(Action callback) => Click = callback;
    public MachineryButton SetSize(int width, int height) => this;
    public MachineryButton SetFillWidth(bool fill) => this;
    public MachineryButton SetDescription(string description) => this;
    public MachineryButton SetFaceSprites(UnityEngine.Sprite? sprite) => this;
}
public sealed class MachineryText
{
    public MachineryText(string text) { }
}
public static class MachineryStack
{
    public static object Vertical(params object[] elements) => elements;
}
public static class ItemDirectory
{
    public static GameItem CreateEmptyItem(string id) => new() { identifier = id };
}
public static class DirectoryUtils
{
    public static (ItemWindow, GameGridInventory) CreateInventoryWindow(int width, int height, bool unused)
    { var grid = new GameGridInventory(width, height); return (new(new MachineryPanel(grid)), grid); }
}
public static class ContainerHelper
{
    public static void InitContainerItem(GameInventory grid, GameItem item)
    { item.EnableTag("CONTAINER_TAG"); grid.mayInventoryAddItemFunc = (candidate, _) => GeneralHelper.IsItemOwned(candidate); }
    public static void InitSmugglerBay(GameInventory grid, GameItem item)
    {
        item.EnableTag("CONTAINER_TAG"); item.EnableTag("IMPORTANT_TAG");
        grid.mayInventoryAddItemFunc = (candidate, target) => GeneralHelper.IsItemOwned(candidate) &&
            target.GetParentItem() is { } parent && GeneralHelper.IsItemOwned(parent) && candidate.identifier != "smuggler_bay" &&
            !candidate.IsTag("BACKPACK_TAG") && !candidate.IsTag("CAGE_TAG");
    }
}
public static class InvokeFuncExtensions
{
    public static bool BoolReduceOR(bool a, bool b) => a | b;
    public static bool InvokeAllReduce(this Func<GameItem, GameInventory, bool> func, GameItem item, GameInventory inv,
        Func<bool, bool, bool> reduce) => func.GetInvocationList().Cast<Func<GameItem, GameInventory, bool>>()
            .Select(callback => callback(item, inv)).Aggregate(reduce);
}
public sealed class RichTextBuilder
{
    public List<string> Lines = new();
    public void AddLine(string text) => Lines.Add(text);
}
public sealed class PlayerStore
{
    public static PlayerStore? Current;
    public static PlayerStore Instance => Current ??= new();
    public static bool HasInstance => Current != null;
    public int saveSlotId, visitLeftTonight;
    public string runID = "test-run";
    public long playerCash;
    public ClientInstance? currentClientInstance;
    public List<GameItem> Items = new();
    public int FindAllItemCalls;
    public List<GameItem> FindAllItem(bool isOwn = true)
    {
        FindAllItemCalls++;
        return Items.SelectMany(item => new[] { item }.Concat(item.FindAllChildItems())).Distinct().ToList();
    }
    public bool CanSellThisItem(GameItem item, bool unused = false) => true;
    public void SellItem(GameItem item) { }
    public int GetCurrentStoreAttractiveness() => 0;
}
public sealed class ClientInstance
{
    public StoreClient Client = new();
    public StoreClient GetClientBlueprint() => Client;
}
public sealed class StoreClient
{
    public string identifier = "npc";
    public bool isAug, useClientBudget;
    private int _budget;
    public int GetBudget() => _budget;
    public void OverrideBudget(int budget) => _budget = budget;
    public void SetBudget(int budget) { useClientBudget = true; _budget = budget; }
    public void SetBudget(int budget, int conversion) => SetBudget(budget);
    public void SetClientBudget(int budget) => SetBudget(budget);
    public void SetClientBudget(int budget, int range) => SetBudget(budget);
    public bool IsClientBuyingThisItem(GameItem item) => false;
    public bool IsClientRefusingItem(GameItem item, bool ignoreContents = false) => true;
    public bool IsItemInVarietyList(GameItem item) => true;
}
public static class AugHelper { public static void MakeDisguisedAug(StoreClient client, int chance) => client.isAug = true; }
public static class TypeHelper { public static string GetTypeDisplayName(string type) => type; }
public sealed class NegociationUIManager
{
    public void UpdateUI(GameItem item) { }
    public void SellMultiple(List<GameItem> items, bool ignoreBudget = false) { }
}
public sealed class EmporiumEntry
{
    public static EmporiumEntry? Instance;
    public GameGridInventory frontInvinvElement = new(), backInvinvElement = new(), showcaseElement = new();
    public List<GameItem> Afterhour = new();
    public List<GameItem> GetAllAfterhourOwnedItems() => Afterhour;
    public bool IsAfterhourHaveOwnedItems(string id) => Afterhour.Any(item => item.identifier == id);
    public GameItem? GetAfterhourItemById(string id) => Afterhour.FirstOrDefault(item => item.identifier == id);
}
public static class StoreStation { public static int Day; public static int GetDayCounter() => Day; }
public static class StartingPerk
{
    public static bool IsDegenerativeOsteoarthritis() => true;
    public static bool IsMaimed() => false;
}
public static class ScavHelper
{
    public static int Attempts = 2, Calls;
    public static Func<List<GameItem>> Loot = () => new() { new GameItem { identifier = "scrap_metal" } };
    public static int GetMaxScavAttempts() => Attempts;
    public static List<GameItem> GetRandomScavengedItem() => GetRandomScavengedItem(out _);
    public static List<GameItem> GetRandomScavengedItem(out GameItem? tool) { Calls++; tool = null; return Loot(); }
}
public static class AugScanner { public static GameSlotInventory? GetBatterySlot(GameItem item) => item.Storages[0] as GameSlotInventory; }
public static class AppraisalHelper
{
    public static int GetCorrectBaseValue(GameItem item) => item.GetTagReadonly("correctValue").GetInt();
}
public sealed class OverlayHandler { public static OverlayHandler? current; public void UpdateInventoryStateUI() { } }
public static class HypertechBatteryHelper
{
    public static bool IsHypertechBattery(GameItem item) => item.identifier == "hypertech_battery";
    public static int GetOutput(GameItem item) => item.GetTagReadonly("output").GetInt();
}
public static class CurrentConverterHelper
{
    public static bool isNightCycle;
    public static bool IsCurrentConverter(GameItem item) => item.identifier == "current_converter";
    public static float GetRate(GameItem item) => 2;
    public static bool CanSupply(GameItem item, int amount) => item.IsTag("service") &&
        (isNightCycle || PlayerStore.Instance.playerCash >= amount * GetRate(item));
    public static bool Supply(GameItem item, int amount)
    {
        if (!CanSupply(item, amount)) return false;
        if (!isNightCycle) PlayerStore.Instance.playerCash -= (long)(amount * GetRate(item));
        return true;
    }
}
public static class ModuleEffectHelper { public static void ComputeAllModuleTempStat(List<GameItem> items) { } }
public static class ModuleHelper
{
    public static int Aggregations;
    public static void ComputeModuleEffect(GameInventory inventory, GameItem? item, GameItem machine) => ComputeModuleEffect(inventory.childItems, machine);
    public static void ComputeModuleEffect(List<GameItem> items, GameItem machine)
    {
        Aggregations++;
        foreach (string tag in new[] { "TOTAL_PERCENTAGE_POWER_BONUS_INT", "TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT", "TOTAL_PERCENTAGE_QUALITY_BONUS_INT" })
            machine.ModifyTag(tag, state => state.SetInt(items.Count * 10));
        // Dispatch the actual candidate's aggregation postfix after the native reset.
        ManufacturedEffectsRuntime.AfterAggregate(machine);
    }
    public static void ApplyBasicModuleEffect(GameInventory inventory, GameItem? item, GameItem machine) { }
    public static void ApplyModifiedPower(GameItem machine) { }
}
namespace UnityEngine
{
    public static class Random
    {
        public static Queue<int> Rolls = new();
        public static int Range(int min, int max) => Rolls.Count > 0 ? Rolls.Dequeue() : 99;
    }
}
