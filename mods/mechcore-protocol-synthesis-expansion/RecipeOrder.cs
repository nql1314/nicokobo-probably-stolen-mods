using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class RecipeOrder
{
    internal static IEnumerable<RecipeDefinition> Sort(string machineId, IEnumerable<RecipeDefinition> allRecipes,
        long ore, long scrap, bool blast)
    {
        var all = allRecipes.ToArray();
        var producers = all.GroupBy(recipe => recipe.OutputItemId).ToDictionary(group => group.Key, group => group.ToArray());
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        int Depth(string id, HashSet<string> path)
        {
            if (depths.TryGetValue(id, out int cached)) return cached;
            if (!producers.TryGetValue(id, out var recipes)) return 0;
            if (!path.Add(id)) throw new InvalidDataException("Production cycle: " + id);
            int depth = 1 + recipes.Max(recipe => recipe.ItemInputs.Select(input => Depth(input.ResourceId, path)).DefaultIfEmpty(0).Max());
            path.Remove(id); depths[id] = depth; return depth;
        }
        foreach (var recipe in all) Depth(recipe.OutputItemId, new HashSet<string>(StringComparer.Ordinal));
        var candidates = all.Where(recipe => recipe.MachineId == machineId &&
            (!RefinerMetalRules.IsSmelting(recipe.OutputRule) || RefinerMetalRules.MatchesMode(recipe.OutputRule, blast))).ToArray();
        var metal = candidates.Where(recipe => RefinerMetalRules.IsSmelting(recipe.OutputRule) &&
                recipe.ItemInputs.All(input => input.Kind == RecipeResourceKind.Item && input.ResourceId is "common_ore" or "scrap_metal") &&
                recipe.ItemInputs.Where(input => input.ResourceId == "common_ore").Sum(input => (long)input.Amount) <= ore &&
                recipe.ItemInputs.Where(input => input.ResourceId == "scrap_metal").Sum(input => (long)input.Amount) <= scrap)
            .OrderBy(recipe =>
            {
                int batch = checked(recipe.ItemInputs.Sum(input => input.Amount));
                int selectedOre = recipe.ItemInputs.Where(input => input.ResourceId == "common_ore").Sum(input => input.Amount);
                return Math.Abs(selectedOre - RefinerMetalRules.BalancedOrePieces(ore, scrap, batch));
            }).FirstOrDefault();
        return candidates.OrderByDescending(recipe => depths[recipe.OutputItemId])
            .ThenByDescending(recipe => recipe.Id == metal?.Id)
            .ThenByDescending(recipe => recipe.ItemInputs.Count)
            .ThenByDescending(recipe => recipe.ItemInputs.Sum(input => (long)input.Amount));
    }
}
