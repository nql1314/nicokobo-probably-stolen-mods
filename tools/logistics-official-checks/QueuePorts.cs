// Deliberately small native boundaries for the actual scheduling source.
// These checks do not execute Unity, scan/activate a Mod or read/write a save.
public sealed class GameItem
{
    public string identifier = "";
    public int Id;
    public bool Owned = true;
    public GameInventory parentInventory = new();
    public Action<GameItem, GameInventory, SlotMarker>? onCycleEndSlotItemFunc { get; set; }
    public Action<GameItem, GameInventory, SlotMarker>? onCycleEndEarlySlotItemFunc { get; set; }
    public Action<GameItem, GameInventory, SlotMarker>? onCycleEndLateSlotItemFunc { get; set; }
    public Action<GameItem, GameItem>? onThisTargetItemFunc;
    public Dictionary<int, GameInventory> Body = new();
    public Dictionary<string, TagState> Tags = new();
    public int StolenUpdates, Energy = 100;
    public bool IsTag(string id) => Tags.ContainsKey(id);
    public TagState GetTagReadonly(string id) => Tags.TryGetValue(id, out var tag) ? tag : new();
    public int GetUniqueID() => Id;
    public SlotMarker GetIncompleteSlotInInventory() => new();
}
public class GameInventory { public GameItem? Parent; public GameItem? GetParentItem() => Parent; }
public sealed class GameGridInventory : GameInventory { }
public sealed class GameSlotInventory : GameInventory { public GameItem? childItem; }
public sealed class TagState { public int valueInt; }
public sealed class SlotMarker { }
public sealed class PlayerStore
{
    public static PlayerStore Instance = new();
    public static bool HasInstance => Instance != null;
    public string runID = "run-A";
    public int saveSlotId = 2;
    public Dictionary<string, string> modData = new();
    public List<GameItem> Items = new();
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void EndNight()
    {
        CurrentConverterHelper.isNightCycle = true;
        HopperHelper.RunWindow(1);
        Items.ForEach(item =>
        {
            StolenHelper.UpdateStolenItem(item);
            item.onCycleEndSlotItemFunc?.Invoke(item, item.parentInventory, item.GetIncompleteSlotInInventory());
        });
        HopperHelper.RunWindow(2);
        Items.ForEach(item => item.onCycleEndLateSlotItemFunc?.Invoke(item, item.parentInventory, item.GetIncompleteSlotInInventory()));
        HopperHelper.RunWindow(3);
        CurrentConverterHelper.isNightCycle = false;
    }
}
public static class HopperHelper
{
    public static readonly List<string> Trace = new();
    public static void RunWindow(int window) => Trace.Add("hopper-" + window);
}
public static class StolenHelper { public static void UpdateStolenItem(GameItem item) => item.StolenUpdates++; }
public static class MachineTurboBoosterAdv
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void BoostMachine(GameItem item)
    {
        item.onCycleEndEarlySlotItemFunc?.Invoke(item, item.parentInventory, item.GetIncompleteSlotInInventory());
        item.onCycleEndSlotItemFunc?.Invoke(item, item.parentInventory, item.GetIncompleteSlotInInventory());
    }
}
public sealed class ToolDirectory
{
    public GameItem CreateTurboBooster()
    {
        var tool = new GameItem();
        tool.onThisTargetItemFunc = (_, item) =>
        {
            item.onCycleEndEarlySlotItemFunc?.Invoke(item, item.parentInventory, item.GetIncompleteSlotInInventory());
            item.onCycleEndSlotItemFunc?.Invoke(item, item.parentInventory, item.GetIncompleteSlotInInventory());
        };
        return tool;
    }
}
public static class DebugPanel
{
    public static GameItem Target = new();
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void SpawnGunKit() => Target.onCycleEndSlotItemFunc?.Invoke(Target, Target.parentInventory, Target.GetIncompleteSlotInInventory());
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void SpawnHydroponicKit()
    {
        for (int i = 0; i < 6; i++) Target.onCycleEndSlotItemFunc?.Invoke(Target, Target.parentInventory, Target.GetIncompleteSlotInInventory());
        Target.onCycleEndSlotItemFunc?.Invoke(Target, Target.parentInventory, Target.GetIncompleteSlotInInventory());
    }
    public static void SkipNightMachine()
    {
        PlayerStore.Instance.Items.ForEach(item => item.onCycleEndEarlySlotItemFunc?.Invoke(item, item.parentInventory, item.GetIncompleteSlotInInventory()));
        PlayerStore.Instance.Items.ForEach(item => item.onCycleEndSlotItemFunc?.Invoke(item, item.parentInventory, item.GetIncompleteSlotInInventory()));
        PlayerStore.Instance.Items.ForEach(item => item.onCycleEndLateSlotItemFunc?.Invoke(item, item.parentInventory, item.GetIncompleteSlotInInventory()));
    }
}
public static class MachineHelper { public static GameInventory? GetBodyStorage(GameItem machine, int index) => machine.Body.GetValueOrDefault(index); }
public static class PowerHelper
{
    public static int GetAvailableEnergyFromItem(GameItem item, bool includeFurniture) => item.Energy;
    public static bool CanDrawPowerSource(GameItem item, int amount) => item.Energy >= amount;
    public static bool DrawPowerSource(GameItem item, int amount) { item.Energy -= amount; return true; }
}
public static class StoreStation { public static int Day = 8; public static int GetDayCounter() => Day; }
public static class CurrentConverterHelper { public static bool isNightCycle; }
public static class DirectoryMaster { public static GameItem? Item(string id) => null; }
namespace Nicokobo.AutomatedLogistics.Native
{ internal static class NativeInventoryApi { internal static bool IsPlayerOwned(GameItem item) => item.Owned; } }
namespace Nicokobo.AutomatedLogistics.Integration
{
    internal static class NativeHooks { internal static object? Runtime = new(); }
    internal static class Content
    {
        internal const string Owner = "nicokobo.automated_logistics";
        internal const string ElectromechanicalWorkbench = "nicokobo.mechcore.synthesis.synthesizer";
        internal const string ManufacturingTerminal = "nicokobo.forge.machines.universal_manufacturing_terminal";
    }
}

public static class GeneralHelper { public static bool IsItemOwned(GameItem item) => item.Owned; }
namespace Nicokobo.ContentInterop
{
    internal static class ContentRuntime
    { internal static void RequireOwner(string owner) { if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException(nameof(owner)); } }
    // These tests use unregistered native machines; their battery comes from
    // MachineHelper. Custom catalog construction is outside this boundary.
    internal static class MachineCatalog
    { internal static MachinePorts? Ports(GameItem machine) => null; internal static bool Contains(string id) => false; }
    internal sealed class MachinePorts
    { internal GameInventory? Input => null; internal GameInventory Output => null!; internal GameInventory? Modules => null; internal GameSlotInventory? Battery => null; internal GameSlotInventory? Water => null; }
}
namespace UnityEngine
{
    public class Object { }
    public static class Debug { public static void LogWarning(object message) { } }
}
