namespace AugMechanicalAscension.Domain;

internal sealed record OpeningModuleIdentityItem(string ItemId, int UniqueId,
    int Units = 0, bool Owned = false, long Value = 0,
    int Width = 0, int Height = 0);

internal static class OpeningModuleIdentityRules
{
    internal static int? FindModule(IEnumerable<OpeningModuleIdentityItem?> items,
        int expectedId)
    {
        if (expectedId <= 0) return null;
        bool found = false;
        foreach (var item in items)
        {
            if (item == null) return null;
            if (item.UniqueId != expectedId) continue;
            if (found || item.ItemId != NeuralModuleRules.ItemId || item.Units != 1 ||
                !item.Owned || item.Value != 100 ||
                item.Width != 1 || item.Height != 1) return null;
            found = true;
        }
        return found ? expectedId : null;
    }
}
