namespace Nicokobo.ContentInterop;

// Protocol 1 carries only BCL values, delegates and native game objects. Views
// are compiled into each participant and never cross assembly boundaries.
internal static partial class ForgeWire
{
    internal static object[] Ingredient(IngredientView value) =>
        new object[] { value.ItemId, value.Amount, value.Matches, value.Chinese, value.English, value.LocalizedLabel! };
    internal static IngredientView Ingredient(object[] value) =>
        new((string)value[0], (int)value[1], (Func<GameItem, bool>)value[2], (string)value[3], (string)value[4])
        { LocalizedLabel = value.Length > 5 ? (Func<string>?)value[5] : null };
    internal static object[] Recipe(RecipeView value) => new object[] { value.Id, value.MachineId, value.Ingredients.Select(Ingredient).ToArray(),
        value.OutputItemId, value.OutputCount, value.WaterParts, value.IsAvailableFor,
        value is ExtraRecipe extra ? new object[] { extra.ChineseOutput, extra.EnglishOutput, extra.IsAvailable,
            extra.ResolveOutputId, extra.Priority, extra.PrepareOutput!, extra.LocalizedOutputName! } : null!, value.Preview! };
    internal static RecipeView Recipe(object[] value)
    {
        var inputs = ((object[][])value[2]).Select(Ingredient);
        return value[7] is object[] extra
            ? new ExtraRecipe((string)value[0], (string)value[1], inputs, (string)value[3], (string)extra[0], (string)extra[1],
                (Func<bool>)extra[2], (Func<string>)extra[3], (int)value[4], (Func<GameItem, bool>)value[6],
                (Func<GameItem, string>?)value[8])
                {
                    Priority = (int)extra[4],
                    PrepareOutput = (Action<GameItem, GameItem, GameItem[], int[]>?)extra[5],
                    LocalizedOutputName = extra.Length > 6 ? (Func<string>?)extra[6] : null
                }
            : new RecipeView((string)value[0], (string)value[1], inputs, (string)value[3], (int)value[4], (int)value[5], (Func<GameItem, bool>)value[6],
                (Func<GameItem, string>?)value[8]);
    }
    internal static object[] Ports(MachinePorts value) => new object[] { value.Input, value.Output, value.Modules!, value.Battery!, value.Water! };
    internal static MachinePorts Ports(object[] value) => new((GameInventory)value[0], (GameInventory)value[1],
        (GameInventory?)value[2], (GameSlotInventory?)value[3], (GameSlotInventory?)value[4]);
    internal static object[] Context(WorkContext value) => new object[] { value.Machine, Ports(value.Ports), value.Recipe == null ? null! : Recipe(value.Recipe) };
    internal static WorkContext Context(object[] value) => new((GameItem)value[0], Ports((object[])value[1]), value[2] is object[] recipe ? Recipe(recipe) : null);
    internal static object[] Result(WorkResult value) => new object[] { Context(value.Context), value.Committed,
        value.Consumed.Select(item => new object[] { item.Item, item.Amount }).ToArray(), value.WaterPartsConsumed };
    internal static WorkResult Result(object[] value) => new(Context((object[])value[0]), (bool)value[1],
        ((object[][])value[2]).Select(item => new ConsumedItem((GameItem)item[0], (int)item[1])).ToArray(), (int)value[3]);
    internal static object[] Offer(StockOffer value) => new object[] { value.ItemId, (int)value.Category, value.Weight, value.Owner,
        value.MinerWeight!, value.PreserveLootWeight, value.MinimumDay, value.SkipWhenOwned, (int)value.Suppliers, value.IncludeInNightShop };
    internal static StockOffer Offer(object[] value) => new((string)value[0], (StockCategory)(int)value[1], (float)value[2])
    { Owner = (string)value[3], MinerWeight = (float?)value[4], PreserveLootWeight = (bool)value[5], MinimumDay = (int)value[6],
        SkipWhenOwned = (bool)value[7], Suppliers = (StockSupplier)(int)value[8], IncludeInNightShop = (bool)value[9] };
}
