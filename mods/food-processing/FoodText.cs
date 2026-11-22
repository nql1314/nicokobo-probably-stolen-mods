using Nicokobo.ModUtilities;
using Nicokobo.FoodProcessing.Definitions;

namespace Nicokobo.FoodProcessing;

internal static class FoodText
{
    internal static string Get(string key, params object[] args) => ModText.Get(FoodDefinitions.ModId, key, args);
    internal static string Name(ItemDefinition item) => Get(item.Id + ".name");
}
