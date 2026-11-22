using Newtonsoft.Json;
using MechcoreProtocol.SynthesisExpansion.Domain;

// Native feature and inventory ports. These model the game's value stages;
// current native purity modifiers are independently verified from game IL.
public sealed class ItemFeature
{
    public enum FeatureType { Normale, TemporarySelling, Event }
    public enum ValueStage { Innate, Market, Final }
    public string identifier = "", category = "";
    public ValueStage valueStage;
    public FeatureType featureType;
    public bool isExposable, isPublicHidden, isFeatureMatch, useCondition, isDisabled;
    public ItemCondition? realCondition;
    public string Display = "";
    [JsonProperty] private int _actual, _shown;
    public ItemFeature(FeatureType type) => featureType = type;
    public ValueStage GetValueStage() => valueStage;
    public int GetActualValueModifier() => _actual;
    public int GetFakeValueModifier() => _shown;
    public void SetValueModifier(int value) => _actual = value;
    public void SetPreExposedValueMod(int value, bool force = false) => _shown = value;
    public void SetPublicDisplay(string value) { }
    public void SetActualDisplay(string value) => Display = value;
    public string GetBaseRealDisplay() => Display;
}

public sealed class ItemCondition
{
    public string identifier = "", category = "";
    public bool isTransformative;
}

public static class IngotPurityHelper
{
    public static void InitIngotItem(GameItem item)
    {
        if (item.FindItemFeatureByID("INGOT_PURITY_TAG") == null)
            item.AddItemFeature(new(ItemFeature.FeatureType.Normale) { identifier = "INGOT_PURITY_TAG" });
        SetPurity(item, "INGOT_PURITY_FAIR");
    }
    public static string GetPurity(GameItem item) => item.GetTagReadonly("INGOT_PURITY_TAG").valueString ?? "";
    public static void SetPurity(GameItem item, string purity)
    {
        item.ModifyTag("INGOT_PURITY_TAG", tag => tag.Enable().SetString(purity));
        var feature = item.FindItemFeatureByID("INGOT_PURITY_TAG")!;
        int modifier = IngotPurityRules.ValueFactorPercent(IngotPurityRules.FromNativeTag(purity)) - 100;
        feature.SetValueModifier(modifier); feature.SetPreExposedValueMod(modifier); feature.useCondition = true;
    }
}

public static class MachineHelper
{
    public const string DEFAULT_MODULE_BAY_CONFIG = "10000010001000001110000010001000001";
    public const int DEFAULT_MODULE_BAY_WIDTH = 7;
    public static object? GetBodyStorage(GameItem machine, int index) => machine.Storages[index];
    public static GameGridInventory GetModuleInv(GameItem machine) => (GameGridInventory)machine.Storages[1]!;
    public static void OnMachineActioned(GameItem machine, GameGridInventory modules) => machine.ActionCalls++;
    public static ItemWindow CreateMachineryWindow(bool unused, params MachineryPanel[] panels) => new(panels);
    public static MachineryPanel PowerPanel(GameSlotInventory inventory) => new(inventory);
    public static MachineryPanel Panel(string? label, object element, string icon) => new(element);
    public static MachineryPanel NotePanel(GameSlotInventory inventory) => new(inventory);
    public static void SetupManualSlot(GameSlotInventory inventory) { }
    public static void SetupBatterySlot(GameSlotInventory inventory, GameItem machine) { }
    public static void SetupModuleBay(GameInventory inventory, GameItem machine, Action? onChanged = null, string[]? allowedModuleTypes = null)
        => MechcoreProtocol.SynthesisExpansion.ManufacturedEffectsRuntime.RegisterModuleBay(inventory, machine);
}
public sealed class SlotMarker
{
    private readonly GameInventory? _inventory;
    private readonly GameItem? _item;
    private readonly int _index;
    public GameItem? targetItem;
    public int numTransfer;
    public SlotMarker() { }
    public SlotMarker(GameInventory inventory, GameItem item, int index)
    { _inventory = inventory; _item = item; _index = index; numTransfer = item.unitCount; }
    public bool IsValid() => _inventory != null && _item != null;
    public void AcceptUnchecked()
    {
        if (!_inventory!.UncheckedAccept(_item!)) throw new InvalidOperationException("Port refused placement");
        _inventory.childItems.Remove(_item!); _inventory.childItems.Insert(Math.Min(_index, _inventory.childItems.Count), _item!);
    }
}
public static class MachineFurnace { public static GameItem Furnace() => new(); }

namespace MechcoreProtocol.SynthesisExpansion
{
    internal static class SynthesisMod
    {
        internal static bool English => true;
        internal static bool Active = true;
        internal static PlayerStore? Store => PlayerStore.HasInstance ? PlayerStore.Instance : null;
        internal static string T(string chinese, string english) => english;
        internal static void Warn(string message) { }
    }
    // Appearance is a Unity-only port. Device behavior and saved shape are exercised;
    // render-node patch targets are checked separately against the real DLL.
    internal static class RobotAppearance { internal static void Apply(GameItem item) { } }
}
