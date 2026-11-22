// Managed stand-ins for inventory, UI and loader APIs. No game process runs.
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } }
    public static class AccessTools
    {
        public static FieldInfo Field(Type type, string name) => type.GetField(name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object value)
        {
            if (value == null || value.Destroyed) return;
            value.Destroyed = true;
            if (value is GameObject) GameObject.LiveCount--;
        }
        public static void DontDestroyOnLoad(Object value) { }
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b)
            || ReferenceEquals(a, null) && !ReferenceEquals(b, null) && b.Destroyed
            || ReferenceEquals(b, null) && !ReferenceEquals(a, null) && a.Destroyed;
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class GameObject : Object
    {
        public static int LiveCount;
        public readonly Dictionary<Type, object> Components = new();
        public bool Active = true;
        public GameObject(string name = "") { LiveCount++; }
        public void SetActive(bool active) => Active = active;
        public T AddComponent<T>() where T : new() { var value = new T(); Components[typeof(T)] = value; return value; }
        public T GetComponentInParent<T>() where T : class => Components.TryGetValue(typeof(T), out var value) ? value as T : null;
    }
    public class MonoBehaviour : Object { public bool isActiveAndEnabled = true; }
    [AttributeUsage(AttributeTargets.Class)]
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int value) { } }
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static int frameCount; }
    public enum KeyCode { None, F5, F6, R, BackQuote, LeftControl, RightControl, LeftAlt, RightAlt, LeftShift, RightShift }
    public static class Input
    {
        public static string compositionString = "";
        public static readonly HashSet<KeyCode> Held = new();
        public static KeyCode Down;
        public static bool GetKeyDown(KeyCode key) => Down == key;
        public static bool GetKey(KeyCode key) => Held.Contains(key);
    }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem : UnityEngine.Object
    { public static EventSystem current; public UnityEngine.GameObject currentSelectedGameObject; }
}
namespace TMPro { public class TMP_InputField : UnityEngine.MonoBehaviour { public bool isFocused; } }
namespace UnityEngine.UI { public class InputField : UnityEngine.MonoBehaviour { public bool isFocused; } }

// The native API owns capture lifetime, including its capture-end frame.
public static class KeyBindHelper { public static bool SuppressHotkeys; }

public interface IMod { void Init(ModManifest manifest); void OnEnable(); void OnDisable(); }
public class ModManifest { public string ID = "nicokobo.stock-reroll"; }
public class ModLog
{
    public ModLog(ModManifest manifest) { }
    public void Log(string text) { }
    public void Warning(string text) { }
    public void Error(string text) { }
}
public class ConfigEntry<T> { public T Value; public ConfigEntry(T value) { Value = value; } }
public class ModConfig
{
    public readonly Dictionary<string, object> Entries = new();
    public ConfigEntry<T> Define<T>(string key, T value, string section, Func<string> label, Func<string> tooltip = null)
    { var entry = new ConfigEntry<T>(value); Entries[key] = entry; return entry; }
}
public static class ModHelper
{
    public static ModConfig Config;
    public static object NightShopStock;
    public static ModConfig GetConfig(string id) => Config = new();
    public static T Lookup<T>(string modId, string key) where T : class =>
        modId == "nicokobo.stock-reroll-better-merchants" && key == "stock" ? NightShopStock as T : null;
    private static readonly Lazy<Dictionary<string, string>> Texts = new(() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Localization", "en.csv"))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
            .Select(line => line.Split(',', 2)).ToDictionary(fields => fields[0], fields => fields[1]));
    public static string GetLocalized(string id, string key, params object[] args) =>
        string.Format(Texts.Value[key], args);
}
public static class ModHook
{
    public static event Action OnGameLoadedInit;
    public static event Action<string> OnAfterhourArriveEarly, OnAfterhourLeaveLate;
    public static void Load() => OnGameLoadedInit?.Invoke();
    public static void Arrive(string id) => OnAfterhourArriveEarly?.Invoke(id);
    public static void Leave(string id) => OnAfterhourLeaveLate?.Invoke(id);
}
public class CustomUIManager : UnityEngine.Object
{
    public static CustomUIManager Instance;
    public bool Open;
    public bool IsOpen(string id) => Open;
}
public class GraphNodeStorage { public virtual List<GraphNodeStorage> children => new(); }
public class TreeNodeRender : UnityEngine.Object { public UnityEngine.GameObject gameObject = new(); }
public static class GraphHandler { public static readonly List<PixelWindow> windowGraphNodeRoots = new(); }
public class PixelWindow : GraphNodeStorage
{
    public TreeNodeRender handler = new();
    public GraphNodeStorage Child;
    public readonly List<GameItem> parentItems = new();
    public override List<GraphNodeStorage> children => Child == null ? new() : new() { Child };
    public void Hide() => handler.gameObject.SetActive(false);
}
public class GameItem : GraphNodeStorage
{
    public bool Owned, Destroyed, CannotDetach, ThrowOnDetach;
    public string identifier;
    public long BaseValue = 20;
    public bool Stolen, IgnorePerk;
    public int ForcedHeat;
    public GameInventory parentInventory;
    public PixelWindow contentWindow, examineWindow;
    public UnityEngine.GameObject renderedObject = new();
    public GameItem SetContentWindow(PixelWindow value) { DetachWindow(contentWindow); contentWindow = value; AttachWindow(value); return this; }
    public GameItem SetExamineWindow(PixelWindow value) { DetachWindow(examineWindow); examineWindow = value; AttachWindow(value); return this; }
    void AttachWindow(PixelWindow value)
    {
        if (value == null) return;
        if (!value.parentItems.Contains(this)) value.parentItems.Add(this);
        GraphHandler.windowGraphNodeRoots.Remove(value);
    }
    void DetachWindow(PixelWindow value)
    {
        if (value == null) return;
        value.parentItems.Remove(this);
        if (value.parentItems.Count == 0 && !GraphHandler.windowGraphNodeRoots.Contains(value))
            GraphHandler.windowGraphNodeRoots.Add(value);
    }
    public void Destroy()
    {
        contentWindow?.Hide(); parentInventory?.Expel(this);
        Destroyed = true; UnityEngine.Object.Destroy(renderedObject);
    }
    public long GetBaseValue() => BaseValue;
}
public class GameInventory : GraphNodeStorage
{
    readonly List<GameItem> items = new();
    public List<GameItem> childItems => new(items);
    public bool overrideLockRemove;
    public override List<GraphNodeStorage> children => childItems.Cast<GraphNodeStorage>().ToList();
    public bool Expel(GameItem item)
    {
        if (item.ThrowOnDetach) throw new InvalidOperationException("fixture detach failure");
        if (item.CannotDetach) return false;
        bool removed = items.Remove(item);
        if (removed)
        {
            item.parentInventory = null;
            if (this == EmporiumEntry.Instance?.backInvinvElement
                && PlayerStore.instance?.currentNegociatedItem == item)
                PlayerStore.instance.currentNegociatedItem = items.LastOrDefault();
        }
        return removed;
    }
    public bool UncheckedAccept(GameItem item)
    {
        if (item.parentInventory != null || item.Destroyed) return false;
        items.Add(item); item.parentInventory = this;
        // The real weighing counter selects items even with its UI closed.
        if (this == EmporiumEntry.Instance?.backInvinvElement && PlayerStore.instance != null)
            PlayerStore.instance.currentNegociatedItem = item;
        return true;
    }
}
public class GameGridInventory : GameInventory { }
public class Dialogue { public Dialogue nextDialogue; public Action startAction, endAction; public bool isEndActionExecuted; }
public class DialogUIManager : UnityEngine.Object
{
    public static DialogUIManager Instance;
    public Dialogue currentDialog;
    public bool StartDialogue(Dialogue dialogue, bool forcedStart = false) => true;
    public void ContinueConversation() { }
}
public class StoreClient
{
    public string identifier = "test", displayName = "visitor";
    public bool isBusinessComplete, reported, isOfferWholesale = true;
    public int wholesaleDiscount = -35, budget = 1234;
    public int IntroducedCalls;
    public Dialogue mainDialogue;
    public Action OnArrival;
    public void OnIntroduced() { IntroducedCalls++; }
}
public class StoreClientInstance
{
    public StoreClient Client = new();
    public StoreClient GetClientBlueprint() => Client;
}
public static class StoreClientList
{
    public static Action NightGenerator, SpecialGenerator;
    public static int QuotaCharges;
    public static int ScavSupplyCalls;
    public static float StockMultiplier = 1f;
    public static void PlaceInventorInventory(bool isVisitingPlayerStore = false) => NightGenerator?.Invoke();
    public static void PlaceInventorSpecialStock() => SpecialGenerator?.Invoke();
    public static void RemoveCommissaryStock() { QuotaCharges++; }
    public static void AddScavGeneralStock() { ScavSupplyCalls++; PlayerStore.instance.AddDirectSellingItemToTable("scav_stock"); }
    public static float GetBMRepStockMult() => StockMultiplier;
}
public class StoreStation { public static StoreStation Instance = new(); public int dayCounter; }
public static class ItemSpawner { public static GameItem Spawn(string id) => new() { identifier = id }; }
public static class PreBuiltItemHelper
{
    public static GameItem CreateRandomStamp() => ItemSpawner.Spawn("stamp");
    public static GameItem RandomCigarette() => ItemSpawner.Spawn("cigarette");
    public static GameItem CreateRandomInjector() => ItemSpawner.Spawn("injector");
}
public static class StoreClientListSupplier
{
    public static void AddScavFoodStock() { }
    public static void AddScavHouseholdStock() { }
    public static void AddScavMaterialStock() { }
    public static void AddScavMedStock() { }
    public static void AddThiefFoodStock() { }
    public static void AddThiefHouseholdStock() { }
    public static void AddThiefMaterialStock() { }
    public static void AddThiefMedStock() { }
    public static void AddThiefLuxuryStock() { }
}
public static class TableMaster
{
    public static readonly List<string> Rolls = new();
    public static string Roll(string table) { Rolls.Add(table); return "packed_food"; }
}
public static class RNG
{
    public static readonly List<float> Chances = new();
    public static readonly List<(int min, int max)> Bounds = new();
    public static readonly Queue<int> Integers = new();
    public static bool RollResult;
    public static bool Roll(float chance) { Chances.Add(chance); return RollResult; }
    public static int GetRandomInt(int min, int max) { Bounds.Add((min, max)); return Integers.Count > 0 ? Integers.Dequeue() : min; }
    public static int GetRandomIntExculsive(int min, int max) => Integers.Count > 0 ? Integers.Dequeue() : min;
}
public static class Difficulty
{
    public static bool IsHardOrAbove() => PlayerStore.instance.HardOrAbove;
}
public class PlayerStore
{
    public static PlayerStore instance;
    public static int SingletonCreations;
    public static PlayerStore Instance { get { SingletonCreations++; return instance ??= new(); } }
    public StoreClientInstance currentClientInstance = new();
    public bool HardOrAbove;
    public int Cash;
    public readonly List<int> Payments = new();
    public GameItem currentNegociatedItem;
    public bool isClientArrived = true, isClientBeingArrested, isHandlingShootout;
    public bool isStampIntroduced, isVisualInspectionIntroduced, isInjectorIntroduced;
    public int GetCash() => Cash;
    public void PayMoney(int amount, bool playRegisterSound = false) => Payments.Add(amount);
    public void RefreshCounterItem() { }
    public void OnItemBought(GameItem item, int cost) { }
    public void OnItemsSold(List<GameItem> items) { }
    public void SellItem(GameItem item) { item.Owned = false; }
    public void AcceptBarter() { currentClientInstance.Client.isBusinessComplete = true; }
    public void BuyAllItemFromTable() { }
    public void AddDirectSellingItemToTable(string id, bool isOwend = false, bool isStolen = false, bool ignorePerk = false, int forcedHeat = 0)
        => AddDirectSellingItemToTable(ItemSpawner.Spawn(id), isOwend, isStolen, ignorePerk, forcedHeat);
    public void AddDirectSellingItemToTable(GameItem item, bool isOwend = false, bool isStolen = false, bool ignorePerk = false, int forcedHeat = 0)
    {
        item.Owned = isOwend; item.Stolen = isStolen; item.IgnorePerk = ignorePerk; item.ForcedHeat = forcedHeat;
        EmporiumEntry.Instance.frontInvinvElement.UncheckedAccept(item);
    }
}
public static class AppraisalServiceHelper
{
    public static void GiveAppraisalFee(int value, int precision, int correctValue) { }
}
public class NegociationUIManager : UnityEngine.Object
{
    public static NegociationUIManager Instance;
    public bool IsOpen;
    public void OpenUI() { }
}
public class BargainUIManager : UnityEngine.Object { public static BargainUIManager Instance; public bool IsOpen; }
public class EmporiumEntry : UnityEngine.Object
{
    public static EmporiumEntry Instance;
    public GameGridInventory frontInvinvElement = new(), backInvinvElement = new();
    public void CloseItemWindowUIWindow() { }
    public void Validate(bool force) { }
    public bool IsFrontCounterEmptyNonOwned() => frontInvinvElement.childItems.All(item => item.Owned);
}
public class StoreUIManager : UnityEngine.Object
{
    public static StoreUIManager Instance;
    public static bool Editing;
    public bool isShootingMode;
    public string LastNotification;
    public void Notify(string text) => LastNotification = text;
    public static bool IsTextFieldFocused() => Editing;
}
public class AfterhourLocation
{
    public const string DR_JACKSON = "dr_jackson";
    public string Id;
    public string Source = "vanilla";
    public bool ShowsStoreCounters;
}
public class MapUIManager : UnityEngine.Object
{
    public static MapUIManager Instance;
    public AfterhourLocation Current;
    public bool IsOutside => Current != null;
    public bool IsInTransit;
}
public static class AfterhourEvents { public static bool Special; public static bool IsActive(string id) => Special; }
public static class VanillaAfterhourEvents { public const string SPECIAL_STOCK = "special_stock"; }
public static class GeneralHelper { public static bool IsItemOwned(GameItem item) => item.Owned; }

// The behavior fixture supplies ordinary managed delegates. The separate binary
// check below audits the shipped policy against the actual game's metadata.
public class SupplyAction
{
    readonly Action action;
    public SupplyAction(Action action) { this.action = action; }
    public void Run() => action();
}
namespace StockReroll
{
    internal static class StockCallbackPolicy
    {
        internal static bool IsSupply(string type, string method) => type == nameof(SupplyAction) && method == nameof(SupplyAction.Run);
    }
}
