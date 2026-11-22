namespace Nicokobo.ContentInterop;

[Flags]
public enum StockCategory
{
    None = 0, Material = 1, Ore = 2, Module = 4, Machine = 8,
    Household = 16, Food = 32, Medical = 64, All = 127
}

[Flags]
public enum StockSupplier { Any = 0, Thief = 1, Inventor = 2 }

public sealed class StockOffer
{
    public string ItemId { get; }
    public StockCategory Category { get; }
    public float Weight { get; }
    public string Owner { get; internal set; } = "";
    public float? MinerWeight { get; set; }
    public bool PreserveLootWeight { get; set; }
    public int MinimumDay { get; set; }
    public bool SkipWhenOwned { get; set; }
    public StockSupplier Suppliers { get; set; }
    public bool IncludeInNightShop { get; set; } = true;

    public StockOffer(string itemId, StockCategory category, float weight = 1f)
    { ItemId = itemId; Category = category; Weight = weight; }

    internal StockOffer Freeze(string owner) => new(ItemId, Category, Weight)
    {
        Owner = owner, MinerWeight = MinerWeight, PreserveLootWeight = PreserveLootWeight,
        MinimumDay = MinimumDay, SkipWhenOwned = SkipWhenOwned, Suppliers = Suppliers,
        IncludeInNightShop = IncludeInNightShop
    };
}

