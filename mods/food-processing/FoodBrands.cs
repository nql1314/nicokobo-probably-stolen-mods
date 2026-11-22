using HarmonyLib;

namespace Nicokobo.FoodProcessing;

internal static class FoodBrands
{
    internal const string Prefix = "nicokobo.food.";
    internal static readonly string[] Names = { "pastel", "davre", "redimp", "beis", "stardeer", "tang", "colaco", "starnest", "nineorbits" };
    internal static string Label(string brand) => FoodText.Get("food.brand." + brand);

}

// Only our namespaced manufacturer values are handled here; native brands retain
// the game's normal presentation. The native tooltip owns the position below name.
[HarmonyPatch(typeof(GameItem), nameof(GameItem.DisplayManufacturer))]
internal static class FoodManufacturerPatch
{
    private static bool Prefix(RichTextBuilder builder, string manufacturer)
    {
        if (!manufacturer.StartsWith(FoodBrands.Prefix, StringComparison.Ordinal)) return true;
        var brand = manufacturer.Substring(FoodBrands.Prefix.Length);
        if (!FoodBrands.Names.Contains(brand)) return true;
        builder.AddLine(FoodBrands.Label(brand));
        return false;
    }
}
