namespace Nicokobo.ContentInterop;

public static class SupplyCatalog
{
    private static readonly Dictionary<string, StockOffer[]> Entries = new(StringComparer.Ordinal);

    public static void Register(string owner, IEnumerable<StockOffer> offers)
    {
        ContentRuntime.RequireOwner(owner);
        var all = offers?.ToArray() ?? throw new ArgumentNullException(nameof(offers));
        if (all.Any(offer => offer == null || string.IsNullOrWhiteSpace(offer.ItemId) ||
                offer.Category == StockCategory.None || (offer.Category & ~StockCategory.All) != 0 ||
                !Positive(offer.Weight) || offer.MinerWeight is { } miner && !Positive(miner) ||
                offer.MinimumDay < 0 || (offer.Suppliers & ~(StockSupplier.Thief | StockSupplier.Inventor)) != 0) ||
            all.Select(offer => offer.ItemId).Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new ArgumentException("Invalid or duplicate NPC stock offer", nameof(offers));
        var foreign = Entries.Where(entry => entry.Key != owner).SelectMany(entry => entry.Value).Select(offer => offer.ItemId)
            .ToHashSet(StringComparer.Ordinal);
        if (all.Any(offer => foreign.Contains(offer.ItemId))) throw new InvalidOperationException("NPC stock item belongs to another provider");
        Entries[owner] = all.Select(offer => offer.Freeze(owner)).ToArray();
    }

    private static bool Positive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;

    public static IReadOnlyList<StockOffer> Available(StockCategory categories, StockSupplier supplier = StockSupplier.Any,
        bool nightShop = false)
    {
        if (!PlayerStore.HasInstance) return Array.Empty<StockOffer>();
        int day = StoreStation.GetDayCounter();
        var candidates = All().Where(offer => (offer.Category & categories) != 0 &&
            (offer.Suppliers == StockSupplier.Any || (offer.Suppliers & supplier) != 0) &&
            (!nightShop || offer.IncludeInNightShop) && day >= offer.MinimumDay &&
            DirectoryMaster.Has<GameItem>(offer.ItemId)).ToArray();
        var unique = candidates.Where(offer => offer.SkipWhenOwned).Select(offer => offer.ItemId).ToHashSet(StringComparer.Ordinal);
        if (unique.Count == 0) return candidates;
        var owned = PlayerStore.Instance.FindAllItem().Where(item => item.unitCount > 0 && GeneralHelper.IsItemOwned(item) && unique.Contains(item.identifier))
            .Select(item => item.identifier).ToHashSet(StringComparer.Ordinal);
        return candidates.Where(offer => !owned.Contains(offer.ItemId)).ToArray();
    }

    internal static IEnumerable<StockOffer> All() => Entries.Where(entry => ContentRuntime.IsAttached(entry.Key)).SelectMany(entry => entry.Value);
    internal static bool HasProvider(string owner) => owner != null && ContentRuntime.IsAttached(owner) && Entries.ContainsKey(owner);
    internal static bool IsDeclared(string id) => All().Any(offer => offer.ItemId == id);
    internal static void Remove(string owner) => Entries.Remove(owner);
}
