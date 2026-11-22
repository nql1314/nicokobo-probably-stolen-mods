using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion;

// Forge owns the terminal; this package contributes its manufacturing recipes.
internal static class ProductionCatalog
{
    internal static IReadOnlyList<ItemDefinition> Items { get; } = Array.AsReadOnly(
        SynthesisDefinitions.Items.ToArray());
    private static readonly HashSet<string> ItemIds = new(Items.Select(item => item.Id), StringComparer.Ordinal);
    internal static bool OwnsItem(string id) => ItemIds.Contains(id);
    internal static IEnumerable<MachineDefinition> Machines => SynthesisDefinitions.Machines.Where(machine => OwnsItem(machine.ItemId));
    internal static IEnumerable<RecipeDefinition> Recipes => SynthesisDefinitions.Recipes
        .Concat(GlassRecycling.Feedstocks.Select(feedstock => feedstock.Recipe))
        .Where(recipe => OwnsItem(recipe.MachineId) || recipe.MachineId == SynthesisDefinitions.UniversalManufacturerItem.Id);
    internal static IEnumerable<RecipeDefinition> AllRecipes => Recipes;
    internal static string SpriteKey(ItemDefinition item) => item.Id == SynthesisDefinitions.UniversalManufacturerItem.Id
        ? "universal_manufacturing_terminal" : item.Id.Substring(SynthesisDefinitions.OwnerId.Length + 1).Replace('.', '_');
}
