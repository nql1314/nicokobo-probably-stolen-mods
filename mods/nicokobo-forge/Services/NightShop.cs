namespace Nicokobo.ContentInterop;

public static class NightShop
{
    private static readonly Dictionary<string, Func<GameItem[], string[]>> Providers = new(StringComparer.Ordinal);
    public static void Register(string owner, Func<GameItem[], string[]> stock)
    { ContentRuntime.RequireOwner(owner); Providers[owner] = stock ?? throw new ArgumentNullException(nameof(stock)); }
    internal static void Remove(string owner) => Providers.Remove(owner);
    internal static void Attach() { ModHook.OnPlaceInventorInventoryItemLate += Stock; ContentRuntime.Detaching += Remove; }
    internal static void Detach() { ModHook.OnPlaceInventorInventoryItemLate -= Stock; ContentRuntime.Detaching -= Remove; Providers.Clear(); }
    private static void Stock(List<GameItem> owned)
    {
        if (ModHelper.GetCurrentAfterhourLocationId() != AfterhourLocation.DR_JACKSON || !PlayerStore.HasInstance) return;
        var snapshot = owned.ToArray();
        foreach (var provider in Providers.Values.ToArray())
            foreach (string id in provider(snapshot).Distinct(StringComparer.Ordinal))
                if (DirectoryMaster.Has<GameItem>(id)) PlayerStore.Instance.AddDirectSellingItemToTable(id);
    }
}
