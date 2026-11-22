using HarmonyLib;
using Nicokobo.ContentInterop;

namespace AugMechanicalAscension.Official;

internal static class OfficialInventoryApi
{
    internal static IReadOnlyList<GameItem> CaptureRunItems(PlayerStore store, bool ownedOnly = true)
    {
        if (!PlayerStore.HasInstance || PlayerStore.Instance != store) throw new InvalidOperationException("Current store required");
        var result = new List<GameItem>();
        var seen = new HashSet<GameItem>();
        var queue = new Queue<GameItem>(store.FindAllItem(ownedOnly));
        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            if (item == null || !seen.Add(item)) continue;
            if (seen.Count > 65536) throw new InvalidOperationException("Inventory traversal limit");
            result.Add(item);
            if (item.children != null)
                foreach (var inventory in item.children.OfType<GameInventory>())
                    foreach (var child in inventory.childItems) queue.Enqueue(child);
            if (OfficialModuleApi.GetInventory(item) is { } modules)
                foreach (var child in modules.childItems) queue.Enqueue(child);
        }
        return result;
    }
    internal static bool IsPlayerOwned(GameItem? item) => item != null && GeneralHelper.IsItemOwned(item);
    internal static bool IsItemDragActive => ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem;
    internal static IReadOnlyDictionary<string,int> CaptureOwnedItemCounts(PlayerStore store,IReadOnlyCollection<string>? ids = null) =>
        CaptureRunItems(store).Where(item => item.unitCount > 0 && IsPlayerOwned(item) && (ids == null || ids.Contains(item.identifier)))
            .GroupBy(item => item.identifier,StringComparer.Ordinal).ToDictionary(g => g.Key,g => (int)Math.Min(int.MaxValue,g.Sum(item => (long)item.unitCount)),StringComparer.Ordinal);
}
internal static class OfficialModuleApi
{
    internal static GameGridInventory? GetInventory(GameItem machine)
    {
        if (machine.contentWindow?.childElement is GridPixelElement { gridWidth: < 2 } or GridPixelElement { gridHeight: < 2 })
            return null;
        return machine.IsTag("STANDARD_MACHINE_TAG") || machine.identifier == "evaporator"
            ? MachineHelper.GetBodyStorage(machine, 1) as GameGridInventory : null;
    }
    internal static void Recompute(GameItem machine)
    {
        if (GetInventory(machine) is not { } grid) return;
        ModuleEffectHelper.ComputeAllModuleTempStat(grid.childItems);
        ModuleHelper.ComputeModuleEffect(grid,null,machine);
        ModuleHelper.ApplyBasicModuleEffect(grid,null,machine);
    }
}
internal static class OfficialMachineRuntimeApi
{
    internal sealed record InventoryView(GameInventory ItemInput,GameInventory ItemOutput);
    internal static bool TryGetInventory(GameItem machine,out InventoryView? inventory)
    { var ports=MachineCatalog.Ports(machine);inventory=ports==null?null:new(ports.Input,ports.Output);return inventory!=null; }
    internal static bool TryGetInventory(GameItem machine,string port,out GameInventory? inventory)
    {
        inventory = MachineCatalog.Ports(machine) is { } ports ? port switch
        { "input" => ports.Input, "output" => ports.Output, "modules" => ports.Modules, "battery" => ports.Battery, _ => null } : null;
        return inventory != null;
    }
}
internal static class OfficialManufacturingTerminal
{
    internal const string ItemId="nicokobo.forge.machines.universal_manufacturing_terminal";
    internal const int BaseValue=400;
}
internal sealed record NativeHook(Type Type,string Method,Type[] Parameters,Type ReturnType,Type CallbackType,string? Prefix=null,string? Postfix=null,string? Finalizer=null);
internal static class NativeHookSet
{
    // A closed set of game classes replaces unrestricted runtime-type lookups.
    // Add a class here only when an adapter needs a verified game method.
    internal static System.Reflection.MethodInfo Find(Type type,string name,Type[] parameters,bool declaredOnly=false)
    {
        System.Reflection.MethodInfo? method;
        if(type==typeof(PlayerStore)) method=AccessTools.Method(typeof(PlayerStore),name,parameters);
        else if(type==typeof(MainMenuUIController)) method=AccessTools.Method(typeof(MainMenuUIController),name,parameters);
        else if(type==typeof(GameItem)) method=AccessTools.Method(typeof(GameItem),name,parameters);
        else if(type==typeof(GameItemElement)) method=AccessTools.Method(typeof(GameItemElement),name,parameters);
        else if(type==typeof(GameGridInventory)) method=AccessTools.Method(typeof(GameGridInventory),name,parameters);
        else if(type==typeof(GameSlotInventory)) method=AccessTools.Method(typeof(GameSlotInventory),name,parameters);
        else if(type==typeof(GameGridScrollableInventory)) method=AccessTools.Method(typeof(GameGridScrollableInventory),name,parameters);
        else if(type==typeof(GameCharacterRaidInventory)) method=AccessTools.Method(typeof(GameCharacterRaidInventory),name,parameters);
        else if(type==typeof(NegociationUIManager)) method=AccessTools.Method(typeof(NegociationUIManager),name,parameters);
        else if(type==typeof(StealPixelElement)) method=AccessTools.Method(typeof(StealPixelElement),name,parameters);
        else if(type==typeof(InputActionManager)) method=AccessTools.Method(typeof(InputActionManager),name,parameters);
        else if(type==typeof(CustomEventHandler)) method=AccessTools.Method(typeof(CustomEventHandler),name,parameters);
        else if(type==typeof(StoreUIManager)) method=AccessTools.Method(typeof(StoreUIManager),name,parameters);
        else if(type==typeof(ScavHelper)) method=AccessTools.Method(typeof(ScavHelper),name,parameters);
        else if(type==typeof(HealthData)) method=AccessTools.Method(typeof(HealthData),name,parameters);
        else if(type==typeof(BargainUIManager)) method=AccessTools.Method(typeof(BargainUIManager),name,parameters);
        else if(type==typeof(AugHelper)) method=AccessTools.Method(typeof(AugHelper),name,parameters);
        else if(type==typeof(StoreClientManager)) method=AccessTools.Method(typeof(StoreClientManager),name,parameters);
        else if(type==typeof(DialogUIManager)) method=AccessTools.Method(typeof(DialogUIManager),name,parameters);
        else if(type==typeof(PhoneUIManager)) method=AccessTools.Method(typeof(PhoneUIManager),name,parameters);
        else if(type==typeof(PhoneBookUIManager)) method=AccessTools.Method(typeof(PhoneBookUIManager),name,parameters);
        else if(type==typeof(StorePhoneClient)) method=AccessTools.Method(typeof(StorePhoneClient),name,parameters);
        else if(type==typeof(MultiPageUIManager)) method=AccessTools.Method(typeof(MultiPageUIManager),name,parameters);
        else if(type==typeof(PaperUIManager)) method=AccessTools.Method(typeof(PaperUIManager),name,parameters);
        else throw new ArgumentException("Unsupported native hook class: "+type.FullName,nameof(type));
        if(method==null || declaredOnly && method.DeclaringType!=type) throw new MissingMethodException(type.FullName,name);
        RequireNativeEntryPoint(method);
        return method;
    }
    internal static System.Reflection.MethodInfo Require(Type type,string name,Type[] parameters,Type returnType)
    {
        var method = Find(type,name,parameters,declaredOnly:true);
        if (method.ReturnType != returnType || !OfficialHookApi.TryValidateNativeTarget(method,out var reason)) throw new InvalidOperationException("Invalid target " + type.Name + "." + name);
        return method;
    }
    internal static void RequireNativeEntryPoint(System.Reflection.MethodBase method)
    { if (!OfficialHookApi.TryValidateNativeTarget(method,out var reason)) throw new InvalidOperationException(reason); }
    internal static void Remove(string owner,Action<string>? log=null) => new Harmony(owner).UnpatchSelf();
}
internal static class OwnedCallbacks<TKey,TValue> where TKey : notnull
{
    internal static bool ValidId(string owner,string id) => !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(id) && id.StartsWith(owner + ".",StringComparison.Ordinal);
}
internal static class OfficialCapabilities
{
    internal static readonly View Current = new();
    internal sealed class View
    { internal bool NativeItemDirectoryHook => OfficialItemApi.Ready; internal bool NativeEffectRegistration => OfficialEffectApi.Ready; internal bool KnownGameBuild => true; }
}
