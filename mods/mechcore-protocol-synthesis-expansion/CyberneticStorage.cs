using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class CyberneticStorage
{
    internal static GameItem Create()
    {
        var item = ItemDirectory.CreateEmptyItem(SynthesisDefinitions.CyberneticStorageBox.Id);
        var (window, inventory) = DirectoryUtils.CreateInventoryWindow(
            SynthesisNumbers.CyberneticStorage.Width, SynthesisNumbers.CyberneticStorage.Height, true);
        inventory.identifier = "smuggler_bay";
        item.SetContentWindow(window);
        item.SetGameItemType("STORAGE"); item.EnableTag("ITEM_HIDDEN_TAG");
        ContainerHelper.InitSmugglerBay(inventory, item);
        var nativeAdmission = inventory.mayInventoryAddItemFunc
            ?? throw new InvalidOperationException("Native concealed storage admission was not initialized");
        inventory.mayInventoryAddItemFunc = (candidate, target) => candidate.IsGameItemType("CYBERNETIC") &&
            nativeAdmission.InvokeAllReduce(candidate, target, InvokeFuncExtensions.BoolReduceOR);
        return item;
    }
}
