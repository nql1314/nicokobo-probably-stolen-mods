// Headless ports for the actual supply catalog, selection engine and Harmony
// callbacks. Native target discovery is separately checked from the shipped IL.
public class GraphNodeStorage { public List<GraphNodeStorage> children = new(); }
public sealed class TreeNodeRender { public UnityEngine.GameObject gameObject = new(); }
public sealed class PixelWindow : GraphNodeStorage
{
    public List<GameItem> parentItems = new();
    public TreeNodeRender handler = new();
    public void Hide() { }
}
public static class GraphHandler { public static List<PixelWindow> windowGraphNodeRoots = new(); }
public sealed class GameItem : GraphNodeStorage
{
    public string identifier = "";
    public int unitCount = 1, DestroyCalls;
    public bool Owned, DeclineSetCount;
    public GameInventory? parentInventory;
    public PixelWindow? contentWindow, examineWindow;
    public void SetContentWindow(PixelWindow? value)
    { contentWindow?.parentItems.Remove(this); contentWindow = value; value?.parentItems.Add(this); }
    public void SetExamineWindow(PixelWindow? value)
    { examineWindow?.parentItems.Remove(this); examineWindow = value; value?.parentItems.Add(this); }
    public HashSet<string> Types = new(), Tags = new();
    public bool IsGameItemType(string type) => Types.Contains(type);
    public bool IsTag(string tag) => Tags.Contains(tag);
    public void SetUnitCount(int count) { if (!DeclineSetCount) unitCount = count; }
    public void Destroy() { parentInventory?.Expel(this); contentWindow?.Hide(); DestroyCalls++; unitCount = 0; }
}
public sealed class GameInventory : GraphNodeStorage
{
    public List<GameItem> childItems = new();
    public void Add(GameItem item) { item.parentInventory?.Expel(item); childItems.Add(item); children.Add(item); item.parentInventory = this; }
    public void Expel(GameItem item) { childItems.Remove(item); children.Remove(item); item.parentInventory = null; }
}
public static class GeneralHelper { public static bool IsItemOwned(GameItem item) => item.Owned; }
public static class DirectoryMaster
{
    public static Dictionary<string, Func<GameItem>> Factories = new();
    public static List<GameItem> Created = new();
    public static bool Has<T>(string id) => Factories.ContainsKey(id);
    public static GameItem Item(string id, bool owned = true)
    { var item = Factories[id](); item.Owned = owned; Created.Add(item); return item; }
}
public sealed class PlayerStore
{
    public static PlayerStore? Current;
    public static bool HasInstance => Current != null;
    public static PlayerStore Instance => Current ??= new();
    public List<GameItem> Items = new();
    public List<GameItem> FindAllItem() => Items;
    public bool AcceptStock = true;
    public GameInventory Counter = new();
    public StoreClientManager storeClientManager = new();
    public bool TryAddDirectSellingItemToTable(GameItem item, bool owned = false, bool stolen = false, bool ignorePerk = false, int forcedHeat = 0)
    { if (!AcceptStock) { item.Destroy(); return false; } item.Owned = owned; Counter.Add(item); return true; }
}
public static class StoreStation { public static int Day = 25; public static int GetDayCounter() => Day; }
public static class RNG
{
    public static Queue<double> Samples = new();
    public static int Calls;
    public static double GetRandomDouble(double min, double max) { Calls++; return Samples.Count == 0 ? .99 : Samples.Dequeue(); }
}
public sealed class LootEntry
{
    public string id, sourceModId;
    public float weight;
    public LootEntry(string id, float weight, string? owner = null) { this.id = id; this.weight = weight; sourceModId = owner ?? ""; }
}
public static class LootRegistry
{
    public static Dictionary<string,List<LootEntry>> Tables = new();
    public static List<LootEntry> GetTableEntries(string id) => Tables.TryGetValue(id,out var table)
        ? table.Select(entry=>new LootEntry(entry.id,entry.weight,entry.sourceModId)).ToList() : new();
}
public sealed class LootRollContext { public string tableId="",groupId=""; public string? itemId=""; }
public sealed class Dialogue
{
    public Action? endAction;
    public Dialogue? nextDialogue;
    public List<(GameItem, Dialogue?, Action?)> choices = new();
}
public sealed class StoreClient
{
    public Dialogue? mainDialogue, acceptDealDialogue, acceptDealDialogueLastLine, placedWrongItemWhenSellingToDialogue,
        placeRightItemWhenSellingToDialogue, allDoneDialogue, repeatDialogue, interogationDialogue,
        glasseDialogue, customOnArrestDialogue, resumeAfterConfrontDialogue;
}
public sealed class StoreClientManager
{
    public List<StoreClient> clientStack = new(), chemicalFeedbackStack = new();
}
public static class ModHook
{
    public static event Action<StoreClientManager>? OnGenerateCustomerVeryLate;
    public static event Action<LootRollContext>? OnLootRolled;
    public static void Generate(StoreClientManager manager) => OnGenerateCustomerVeryLate?.Invoke(manager);
    public static void Rolled(LootRollContext context) => OnLootRolled?.Invoke(context);
}
public static class StoreClientList
{
    public static void PlaceInventorInventory(bool visiting) { }
    public static void PlaceSupplierInventory() { }
    public static StoreClient CreateMiner(Action stock) => new() { mainDialogue = new() { endAction = () => stock() } };
}
public static class StoreClientListMinor { }
public static class StoreClientListRev { }
public static class StoreClientListSurplus { }
public static class StoreClientListEvent { }
public static class ItemSpawner
{
    public static GameItem Spawn(string id) => DirectoryMaster.Item(id);
    public static GameItem SpawnFromTable(string id) => new();
    public static GameItem SpawnFromTableGroup(string id) => new();
}
namespace Nicokobo.ContentInterop
{
    public static class ContentRuntime
    {
        public static HashSet<string> Owners = new();
        public static bool IsAttached(string owner) => Owners.Contains(owner);
        internal static void RequireOwner(string owner) { if (!Owners.Contains(owner)) throw new InvalidOperationException("Provider not attached"); }
    }
}
namespace UnityEngine
{
    public class Object { public static void Destroy(GameObject value) => value.Destroyed = true; }
    public sealed class GameObject { public bool Destroyed; }
    public static class Debug { public static List<string> Warnings = new(); public static void LogWarning(object text) => Warnings.Add(text.ToString() ?? ""); }
}
