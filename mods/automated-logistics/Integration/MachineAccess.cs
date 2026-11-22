using Nicokobo.ContentInterop;
using Nicokobo.ModUtilities;

namespace Nicokobo.AutomatedLogistics.Integration;

/// <summary>Reads supported machines' existing slots without selecting recipes.</summary>
internal static class MachineAccess
{
    internal const string PurifierWaterSlot = "purifier_water";
    internal const string HydroponicWaterSlot = "hydroponic_water";
    private static readonly IReadOnlyDictionary<string, GameSlotInventory> NoLiquids = new Dictionary<string, GameSlotInventory>();
    internal static bool IsFeedDispenser(GameItem item) => item.identifier == "livestock_feed_dispenser";
    internal static bool IsNative(string id) => id is "furnace" or "water_purifier" or "moisture_farm" or "desequencer" or
        "printer" or "evaporator" or "hydroponic" or "plant_dna_manipulator";
    internal static bool HasRestockInput(GameItem machine) => machine.identifier is not ("moisture_farm" or "evaporator" or "plant_dna_manipulator");
    internal static bool Eligible(GameItem machine) => (IsFeedDispenser(machine) || IsNative(machine.identifier) || MachineCatalog.Contains(machine.identifier)) &&
        machine.Handle() != IntPtr.Zero && machine.GetUniqueID() > 0 && machine.parentInventory != null &&
        !machine.parentInventory.IsRemoveLocked() && !NativeInventoryFreezeApi.IsFrozen(machine) &&
        NativeInventoryApi.IsPlayerOwned(machine) && (IsFeedDispenser(machine)
            ? MachineFeedDispenser.GetStorageGrid(machine) != null : NativeModuleApi.GetInventory(machine) != null);
    internal static bool TryInventory(GameItem machine, out NativeMachineInventory? inventory)
    { inventory = NativeMachineAccess.Capture(machine); return inventory != null; }

    internal static string DisplayName(GameItem item) => Content.LocalizedName(item.identifier) ?? item.GetDisplayName();

    internal static string Name(string id)
    {
        var registered = Content.LocalizedName(id);
        if (registered != null) return registered;
        if (id.IndexOf('.') >= 0) return RecipePresentation.ReadItem(id).Name;
        return NativeItemText.Name(id, id);
    }
}
