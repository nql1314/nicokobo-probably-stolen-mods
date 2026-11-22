namespace AugMechanicalAscension.Official;

internal sealed record LocalizedItemText(string Key)
{ internal string For() => AugText.Get(Key); }
internal enum NightShopStockPolicy { Repeatable }
internal enum NpcTradeStockCategory { Medical }
internal sealed record NpcTradeOptions(NpcTradeStockCategory Category);
internal sealed record NativeItemOptions(LocalizedItemText? Name = null,
    LocalizedItemText? ShortDescription = null, LocalizedItemText? FlavorText = null)
{
    internal NpcTradeOptions? NpcTrade { get; init; }
    internal void Apply(GameItem item)
    {
        if (Name != null) item.name = Name.For();
        if (ShortDescription != null) item.shortDescription = ShortDescription.For();
        if (FlavorText != null) item.flavorText = FlavorText.For();
    }
}
internal sealed record NativeNodeOptions(NightShopStockPolicy Stock,
    Func<bool> CanOfferAtNightShop, LocalizedItemText? Name = null,
    LocalizedItemText? ShortDescription = null, LocalizedItemText? FlavorText = null);
