using HarmonyLib;
using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing.Definitions;
using Nicokobo.ModUtilities;
using UnityEngine;

namespace Nicokobo.FoodProcessing;

internal static class FoodCareKit
{
    private static bool _creatingRecipeKit;

    internal static void Initialize(GameItem kit)
    {
        var machine = FoodDefinitions.Machine(FoodDefinitions.FoodProcessorItem.Id);
        bool pooled = FoodDefinitions.IsPooledMedicalKit(kit.identifier);
        var (window, inventory) = pooled
            ? DirectoryUtils.CreateInventoryWindow(machine.InputWidth, machine.InputHeight + FoodStorageExpansion.MaxLevel)
            : DirectoryUtils.CreateInventoryWindow(6, 4);
        window.isLocked = true;
        kit.SetContentWindow(window);
        kit.EnableTag("CONTAINER_TAG");
        kit.EnableTag("IMPORTANT_TAG");
        inventory.LockInv();
        AudioHelper.InitPackagedAudio(kit);
        kit.activateDefault = false;
        kit.mayActivateSlotItemFunc = kit.canActivateSlotItemFunc =
            (item, _, _) => GeneralHelper.IsItemOwned(item) && inventory.childItems.Count > 0;
        kit.onActivateSlotItemFunc = (item, _, _) =>
        {
            if (TryUnpack(item, EmporiumEntry.Instance.invElement)) return;
            GenericConfirmationUIManager.Instance.OpenPanelSingleButton(
                FoodText.Get("food.care_kit_unpack_space"), LocHelper.GetLocalizedOrDefault("MainMenu", "close", "OK"));
        };
    }

    internal static void AddSpawnContents(GameItem kit, bool isOwned)
    {
        if (_creatingRecipeKit || SaveManager.IsDecoding) return;
        string[]? defaults = kit.identifier switch
        {
            FoodDefinitions.NomoreCareKitId => ["pink_alt_injector", "bandage_item", FoodDefinitions.RubbingAlcoholId],
            FoodDefinitions.BasicMedicalKitId => ["bandage_item", FoodDefinitions.RubbingAlcoholId, "smelling_salt"],
            FoodDefinitions.AdvancedMedicalKitId => ["hemostatic_bandage_item", "salve", "tilapia_skin_graft"],
            FoodDefinitions.RestrictedMedicalKitId => ["unlicensed_phagimycin_pill", "fanta_pill", "dream_dust"],
            _ => null
        };
        if (defaults == null) return;

        var contents = Contents(kit);
        contents.overrideLockInsert = false;
        try
        {
            foreach (string id in defaults)
            {
                var item = DirectoryMaster.Item(id, isOwned);
                try { Move(item, contents).Apply(); }
                finally { if (item.parentInventory == null) ItemGraphCleanup.Destroy(item); }
            }
        }
        catch { ItemGraphCleanup.Destroy(kit); throw; }
        finally { contents.overrideLockInsert = true; }
    }

    internal static bool TryPack(string kitId, GameItem battery, GameGridInventory input, GameGridInventory output,
        IReadOnlyList<BatchInput> takes, int power, long packingFee)
    {
        if (takes.Any(take => take.Count != 1 || take.Before != 1 || take.Item.unitCount != 1 || take.Item.parentInventory != input))
            return false;

        GameItem kit;
        _creatingRecipeKit = true;
        try { kit = DirectoryMaster.Item(kitId); }
        finally { _creatingRecipeKit = false; }
        var contents = Contents(kit);
        var batteryState = battery.state.Clone();
        var admission = output.mayInventoryAddItemFunc;
        output.mayInventoryAddItemFunc = (_, _) => true;
        contents.overrideLockInsert = false;
        try
        {
            // The game already prices container contents. Store only the packing fee on the case.
            FoodProduction.SetBasis(kit, packingFee);
            var steps = new List<NativeTransactionStep> { Move(kit, output) };
            bool pooled = FoodDefinitions.IsPooledMedicalKit(kitId);
            steps.AddRange(takes.Select(take => Move(take.Item, contents, preservePosition: pooled)));
            steps.Add(new("care kit power", () =>
            {
                if (!(DevicePower.TryDebit(battery, power, out bool paid) ? paid : PowerHelper.DrawPowerSource(battery, power)))
                    throw new InvalidOperationException("Care kit power debit failed");
            }, () =>
            {
                battery.state = batteryState;
                battery.SyncModifiedState();
                PowerHelper.UpdateEnergyCreditSprite(battery);
                return true;
            }));
            return Completed(ForgeTransactions.Run(steps));
        }
        finally
        {
            output.mayInventoryAddItemFunc = admission;
            contents.overrideLockInsert = true;
            if (kit.parentInventory == null && contents.childItems.Count == 0) ItemGraphCleanup.Destroy(kit);
        }
    }

    internal static bool TryUnpack(GameItem kit, GameGridInventory destination)
    {
        if (!GeneralHelper.IsItemEffectivelyOwned(kit) || !kit.MayRemove()) return false;
        var contents = Contents(kit);
        var parts = contents.childItems.ToArray();
        if (parts.Length == 0) return false;
        contents.overrideLockRemove = false;
        try
        {
            if (!Completed(ForgeTransactions.Run(parts.Select(item => Move(item, destination)).ToArray())))
                return false;
        }
        finally { contents.overrideLockRemove = true; }
        ItemGraphCleanup.Destroy(kit);
        return true;
    }

    private static GameGridInventory Contents(GameItem kit) => kit.contentWindow?.childElement as GameGridInventory
        ?? throw new InvalidOperationException("Care kit inventory missing");

    private static NativeTransactionStep Move(GameItem item, GameGridInventory destination, bool preservePosition = false)
    {
        var source = item.parentInventory;
        var shape = item.modifiedShape.Clone();
        return new(item.identifier, () =>
        {
            // Use a whole, unstacked slot so the original native item and its features survive.
            // Pooled kits mirror the input grid. A non-finite pointer position tells
            // the native slot check to use the item's existing grid coordinates.
            var slot = preservePosition
                ? destination.TryInventorySlot(item, 1, new Vector2(float.NaN, 0), shape, item.modifiedState)
                : destination.TryFindOneValidInventorySlot(item, keepOrientation: false);
            if (slot == null || slot.inventory != destination || slot.targetItem != null ||
                slot.TryAcceptOnce(1) != 1 || item.parentInventory != destination)
                throw new InvalidOperationException("No space for " + item.identifier);
        }, () =>
        {
            if (item.parentInventory == source) return true;
            if (item.parentInventory != null && !item.parentInventory.Expel(item)) return false;
            item.modifiedShape = shape;
            return source == null || source.UncheckedAccept(item);
        });
    }

    private static bool Completed(NativeTransactionResult result)
    {
        if (!result.Restored) throw new InvalidOperationException("Care kit transfer could not be restored: " + result.Status);
        return result.Committed;
    }
}

[HarmonyPatch(typeof(DirectoryMaster), nameof(DirectoryMaster.Item), new[] { typeof(string), typeof(bool) })]
internal static class FoodCareKitSpawnPatch
{
    // The native factory removes prefilled children before returning the item.
    private static void Postfix(GameItem __result, bool isOwned) => FoodCareKit.AddSpawnContents(__result, isOwned);
}
