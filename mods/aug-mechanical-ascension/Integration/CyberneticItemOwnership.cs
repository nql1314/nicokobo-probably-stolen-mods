using AugMechanicalAscension.Domain;
using MechcoreProtocol;
namespace AugMechanicalAscension.Integration;

internal static class CyberneticItemOwnership
{
    private static readonly string[] ProductIds = CyberneticProductDefinitions.Products.Select(product => product.Id).ToArray();
    private static readonly OwnedItemCandidates Products = new(
        store => OfficialInventoryApi.CaptureRunItems(store, false), item => ProductIds.Contains(item.identifier));
    internal static OwnedCyberneticEffects Capture(PlayerStore store)
    {
        if (store == null || NativeObject.Handle(store) == IntPtr.Zero ||
            string.IsNullOrWhiteSpace(store.runID) || store.saveSlotId < 0) return default;
        var counts = Products.Read(store).Where(item => item.unitCount > 0 && OfficialInventoryApi.IsPlayerOwned(item))
            .GroupBy(item => item.identifier, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (int)Math.Min(int.MaxValue, group.Sum(item => (long)item.unitCount)), StringComparer.Ordinal);
        return OwnedCyberneticEffects.FromOwnedCounts(counts);
    }

}
