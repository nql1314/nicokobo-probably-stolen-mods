global using Nicokobo.AutomatedLogistics.Native;
global using Nicokobo.ContentInterop;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
namespace Nicokobo.AutomatedLogistics.Native
{
    // Runtime identity only. Persistent references continue to use native GetUniqueID().
    internal static class ManagedIdentity
    {
        private sealed class Identity { internal readonly IntPtr Value = new(System.Threading.Interlocked.Increment(ref _next)); }
        private static long _next;
        private static readonly ConditionalWeakTable<object, Identity> Identities = new();
        internal static IntPtr Handle(this object? value) => value == null ? IntPtr.Zero : Identities.GetValue(value, _ => new Identity()).Value;
        internal static T? TryCast<T>(this object? value) where T : class => value as T;
        internal static T Cast<T>(this object value) => (T)value;
        internal static IEnumerable<T> DistinctBy<T, K>(this IEnumerable<T> source, Func<T, K> key)
        { var keys = new HashSet<K>(); foreach (var item in source) if (keys.Add(key(item))) yield return item; }
    }
    internal static class NativeDelegate
    {
        internal static T Convert<T>(Delegate callback) where T : Delegate => callback is T typed ? typed :
            (T)Delegate.CreateDelegate(typeof(T), callback.Target, callback.Method);
    }
    internal sealed class CallbackLease(Action dispose) : IDisposable
    { private Action? _dispose = dispose; public void Dispose() { var action = _dispose; _dispose = null; action?.Invoke(); } }

    public static class NativeTransactionApi
    {
        public static NativeTransactionResult Run(IReadOnlyList<NativeTransactionStep> steps)
        {
            if (steps.Count == 0 || steps.Any(step => string.IsNullOrWhiteSpace(step.Name) || step.Apply == null || step.Restore == null))
                throw new ArgumentException("A nonempty transaction is required");
            return ForgeTransactions.Run(steps);
        }
    }
    public interface INativePowerTransaction : IDisposable
    { bool Validate(); void Debit(); bool Verify(); bool Restore(); void Fault(string reason); }
    internal sealed class NativeMachineAutomationContext(GameItem machine, NativeMachineInventory inventory)
    {
        internal GameItem Machine { get; } = machine;
        internal NativeMachineInventory Inventory { get; } = inventory;
        internal bool Blocked { get; private set; }
        internal string? Result { get; set; }
        internal WorkResult? Work { get; set; }
        internal void Block() => Blocked = true;
    }
    internal static class NativeItemApi
    {
        internal static bool Exists(string id) => DirectoryMaster.Has<GameItem>(id);
        internal static GameItem Create(string id) => DirectoryMaster.Item(id) ?? throw new InvalidOperationException("Item factory missing: " + id);
    }
    internal static class NativeModuleApi
    {
        internal static GameGridInventory? GetInventory(GameItem machine) => MachineCatalog.Ports(machine)?.Modules as GameGridInventory ?? MachineHelper.GetBodyStorage(machine, 1) as GameGridInventory;
        internal static IReadOnlyList<GameItem> FindInstalled(GameItem machine, string id) => GetInventory(machine)?.childItems.Where(item => item.identifier == id && item.unitCount > 0).ToArray() ?? Array.Empty<GameItem>();
    }
    internal static class NativeMachineRuntimeApi
    {
        internal static bool TryGetInventory(GameItem machine, out NativeMachineInventory? inventory)
        {
            var ports = MachineCatalog.Ports(machine); inventory = null;
            if (ports?.Modules == null) return false;
            inventory = new(ports.Battery, ports.Modules, ports.Input, ports.Output,
                ports.Water == null ? new Dictionary<string, GameSlotInventory>() : new Dictionary<string, GameSlotInventory> { ["water"] = ports.Water });
            return true;
        }
        internal static string ProcessBatch(GameItem machine)
        { NativeMachineAccess.Process(machine); return "native-cycle-invoked"; }
    }
    internal static class NativeInventoryApi
    {
        internal static bool IsPlayerOwned(GameItem? item) => item != null && GeneralHelper.IsItemOwned(item);
        internal static bool IsItemDragActive => ItemMouseDragHandler.current?.IsDraggingItem == true;
        internal static IReadOnlyList<GameItem> CaptureRunItems(PlayerStore store) => store.FindAllItem(true);
    }
    internal static class NativeSaveGraphApi
    {
        internal static IReadOnlyList<GameItem> Items(GameItem root) => root.FindAllChildItems().Concat(new[] { root }).Distinct().ToArray();
        internal static void Validate(GameItem root)
        {
            var items = Items(root);
            if (items.Any(item => item.GetUniqueID() <= 0 || item.unitCount <= 0 || !NativeItemApi.Exists(item.identifier)) ||
                items.Select(item => item.GetUniqueID()).Distinct().Count() != items.Count) throw new InvalidDataException("Invalid item graph");
            // The original graph remains inside the device's native inventory;
            // PlayerStore.SaveGame serializes that graph and its feature table.
        }
    }
    internal static class NativePowerApi
    {
        internal static int GetEnergy(GameItem item) => ForgePower.RawEnergy(item);
        internal static int GetMachineCost(GameItem machine) => MachineryHelper.GetMachinePowerUsage(machine);
    }
    internal static class NativeItemInspectionApi
    {
        internal static long? CurrentUnitValue(GameItem item) => item.GetCurrentValue();
        private static readonly Func<GameItemElement, RichTextBuilder> Tooltip = (Func<GameItemElement, RichTextBuilder>)HarmonyLib.AccessTools.Method(typeof(GameItemElement), "GetTooltipBasic").CreateDelegate(typeof(Func<GameItemElement, RichTextBuilder>));
        internal static RichTextBuilder? BuildTooltip(GameItem item) => item is GameItemElement element ? Tooltip(element) : null;
    }
    internal static class NativePresentationApi
    {
    }
}
