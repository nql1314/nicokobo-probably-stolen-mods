using System.Reflection;
using Nicokobo.ContentInterop;

// Test-only native/Unity ports. Identity binding, JSON, CAS, ledger reconciliation
// and value refresh are the unchanged source-linked production implementations.
public sealed class PlayerStore
{
    public static bool HasInstance => Instance != null;
    public static PlayerStore Instance = null!;
    public string runID = "run-a";
    public int saveSlotId, playerCash;
    public bool IsPropertyPaid;
    public RevisionDictionary modData = new();
    public ClientInstance? currentClientInstance;
    public List<GameItem> Items = [];
    public List<GameItem> FindAllItem() => Items;
    public void StartNewGame() { }
    public void LoadGame() { }
    public void InitialSave() { }
    public void SaveGame() { }
    public void OnStoreOwned() { }
}
public sealed class RevisionDictionary
{
    private readonly Dictionary<string, string> _values = [];
    public Action<string>? BeforeRead;
    public int Writes;
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public string this[string key]
    {
        get { BeforeRead?.Invoke(key); return _values[key]; }
        set { _values[key] = value; Writes++; }
    }
    public void Add(string key, string value) { _values.Add(key, value); Writes++; }
}
public sealed class ClientInstance { public object GetClientBlueprint() => this; }
public sealed class EmporiumEntry
{
    public static EmporiumEntry? Instance = new();
    public object? invElement = new();
}
public static class ModHook
{
    public static event Action? OnGameLoadedInit, OnGameLoadedNormal;
    public static void Init() => OnGameLoadedInit?.Invoke();
    public static void Normal() => OnGameLoadedNormal?.Invoke();
}
public sealed class CustomUIManager
{
    public static CustomUIManager Instance = new();
    public UnityEngine.Sprite? GetSprite(string id) => null;
}
public static class ModHelper
{
    public static Locale? GetCurrentLocale() => null;
    public static string GetLocalized(string modId, string key, params object[] args) => key;
    public sealed class Locale { public Identifier Identifier = new(); }
    public sealed class Identifier { public string Code = "en"; }
}
public static class GeneralHelper { public static bool IsItemOwned(GameItem item) => item.Owned; }
public static class DirectoryMaster
{
    public static int Created;
    public static bool Has<T>(string id) => true;
    public static GameItem Item(string id, bool assignUid)
    { Created++; return new() { identifier = id, uniqueId = 1000 + Created }; }
}
public sealed class GameItem
{
    public string identifier = "container";
    public int uniqueId = 1, unitCount = 1, Capacity = 1_000_000;
    public bool Owned = true;
    public long unitValue;
    public object? parentInventory;
    public ItemState? state = new();
    public int Syncs;
    public IntPtr Handle() => new(1);
    public int GetUniqueID() => uniqueId;
    public bool IsTag(string name) => name == "LIQUID_CONTAINER_TAG";
    public TagState? GetTagReadonly(string name) => state?.dict.GetValueOrDefault(name);
    public void SyncModifiedState() => Syncs++;
    public long GetRefreshedValue() => unitValue;
}
public sealed class ItemState { public Dictionary<string, TagState> dict = []; }
public sealed class TagState(string name, string description)
{
    public string Name = name, Description = description;
    public string valueString = "";
    public int valueInt;
    public void Enable() { }
    public void SetString(string value) => valueString = value;
}
public static class Liquid
{
    public static List<Kind> Liquids = [new("water", "water_parts"), new("oil", "oil_parts"), new("wine", "wine_parts")];
    public sealed record Kind(string identifier, string currentPart);
}
public static class WaterHelper
{
    public static int GetFreeCapacity(GameItem item) => item.Capacity - GetTotalVolume(item);
    public static int GetTotalVolume(GameItem item) => Liquid.Liquids.Sum(part => item.GetTagReadonly(part.currentPart)?.valueInt ?? 0);
    public static int GetWaterPurity(GameItem item) => 0;
    public static void TransferLiquid(GameItem source, GameItem target) { }
    public static void AddLiquid(GameItem target, string id, int volume) { }
    public static void EmptyContainer(GameItem item) { }
}
public static class WaterFeatureHelper
{
    public static long GetWaterPrice(GameItem item) => (long)decimal.Ceiling(WaterHelper.GetTotalVolume(item) / 25000m);
    public static Liquid.Kind? GetConditionFromPurity(int purity) => null;
}
public static class ItemConditionList
{
    public static Liquid.Kind CreatePureWater() => new("pure", "");
    public static Liquid.Kind CreateHighQualityWater() => new("high", "");
    public static Liquid.Kind CreateBaseWater() => new("base", "");
}
namespace UnityEngine
{
    public sealed class Sprite { }
    public static class Debug { public static void LogWarning(object message) { } }
}
namespace HarmonyLib
{
    public sealed class Harmony(string id)
    {
        public string Id => id;
        public void Patch(MethodInfo target, HarmonyMethod? prefix, HarmonyMethod? postfix) { }
        public void UnpatchAll(string owner) { }
    }
    public sealed class HarmonyMethod(Type type, string name) { public Type Type = type; public string Name = name; }
    public static class AccessTools
    {
        public static MethodInfo DeclaredMethod(Type type, string name, Type[] parameters) =>
            type.GetMethod(name, parameters)!;
    }
}
namespace Nicokobo.ContentInterop
{
    internal static class OfficialStoryHooks
    { internal static bool ValidateTarget(MethodInfo? target, out string reason) { reason = ""; return target != null; } }
    internal static class NativeVictoryBudget
    {
        internal static bool Installed, Active;
        internal static void Install() => Installed = true;
        internal static void Uninstall() { Installed = false; Reset(); }
        internal static void Reset() => Active = false;
        internal static void SetActive(bool claimed, object? client) => Active = claimed;
    }
    internal static class OfficialWorkshopApi
    {
        internal static Func<OfficialWorkshopSnapshot?> Snapshot = null!;
        internal static Action<string> Claim = null!;
        internal static Action Opened = null!;
        internal static SubmitResult RegisterChain(string owner, string id, string zh, string en,
            Func<OfficialWorkshopSnapshot?> snapshot, Action<string> claim, Action opened, Func<string> localizedTitle)
        { Snapshot = snapshot; Claim = claim; Opened = opened; return new(SubmitStatus.Accepted, ""); }
        internal static void SetDefaultChain(string id) { }
        internal static void CloseWindow() { }
    }
    internal static class NativeAchievementAdapter
    {
        internal static AchievementObservation Capture(PlayerStore store, Dictionary<string, string> unavailable) => new([], 0, 0, 0);
    }
    internal static class InventoryPlacement
    { internal static object? PlanWholeGrid(object inventory, IEnumerable<GameItem> products) => null; }
    internal static class WorkshopRewardDelivery
    {
        internal static bool Apply(object inventory, object plan) => false;
        internal static bool Recall(IEnumerable<GameItem> products) => true;
    }
    internal static class NativeHookSet
    {
        internal sealed record Hook(Type Type, string Name, Type[] Parameters, Type ReturnType, Type Handler,
            string? Prefix = null, string? Postfix = null);
        internal static bool Install(string owner, Hook[] hooks, Action<string> log) => true;
        internal static void Remove(string owner, Action<string>? log) { }
    }
}
