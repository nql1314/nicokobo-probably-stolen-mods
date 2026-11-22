using MechcoreProtocol.ModuleMatrix.Definitions;

namespace MechcoreProtocol.ModuleMatrix.Domain;

internal static class NightShopModuleSelection
{
    private static readonly (string Code, string Id)[] Items = FixedModuleDefinitions.All
        .Select(item => (item.Code, item.Id))
        .Concat(ContinuousModuleDefinitions.All.Select(item => (item.Module.Code, item.Module.Id)))
        .Concat(NodeItemDefinitions.All.Select(item => (item.Code, item.Id)))
        .Concat(MachineModuleDefinitions.All.Where(item => !item.Retired).Select(item => (item.Code, item.Id)))
        .ToArray();

    internal static IReadOnlyList<string> Draw(IReadOnlyCollection<string> appliedItemIds,
        Func<int, int> nextIndex)
    {
        var candidates = Items.Where(item => appliedItemIds.Contains(item.Id)).ToArray();
        var basic = candidates.Where(item => ModuleTierDefinitions.Find(item.Id)?.Tier == ModuleTier.Basic).ToList();
        var rare = candidates.Where(item => ModuleTierDefinitions.Find(item.Id)?.Tier is
            ModuleTier.Rare or ModuleTier.Functional).ToList();
        var selected = new List<string>();
        while (selected.Count < ModuleMatrixNumbers.Distribution.NightShopDistinctItems &&
               basic.Count + rare.Count > 0)
        {
            // Pick the tier first so its odds do not depend on the number of
            // registered kinds. Empty tiers yield the slot to the remaining tier.
            var pool = basic.Count == 0 ? rare : rare.Count == 0 ? basic :
                nextIndex(ModuleMatrixNumbers.Distribution.NightShopBasicWeight +
                    ModuleMatrixNumbers.Distribution.NightShopRareWeight) <
                    ModuleMatrixNumbers.Distribution.NightShopBasicWeight ? basic : rare;
            int index = nextIndex(pool.Count);
            selected.Add(pool[index].Code);
            pool.RemoveAt(index);
        }
        return selected;
    }
}
