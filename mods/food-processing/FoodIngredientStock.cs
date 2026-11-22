using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing.Domain;

namespace Nicokobo.FoodProcessing;

internal static class FoodIngredientStock
{
    internal const string VoidContainer = "nicokobo.automated_logistics.void_container";
    internal const string VoidTank = "nicokobo.automated_logistics.void_tank";

    internal static GameItem[] OrderedSources(IEnumerable<GameItem> candidates, GameGridInventory warehouse) =>
        candidates.Select(item => (Item: item, Priority: SourcePriority(item, warehouse)))
            .Where(source => source.Priority >= 0).OrderBy(source => source.Priority).Select(source => source.Item).ToArray();

    internal static int SourcePriority(GameItem item, GameGridInventory warehouse)
    {
        if (item.unitCount <= 0 || !item.MayRemove()) return -1;
        int depth = 0;
        bool inVoidStorage = item.identifier == VoidTank;
        for (var current = item; current.parentInventory is { } inventory;)
        {
            if (!GeneralHelper.IsItemOwned(current) || inventory.IsRemoveLocked() ||
                NativeInventoryFreezeApi.IsFrozen(current)) return -1;

            var container = inventory.GetParentItem();
            if (container == null)
            {
                if (inVoidStorage) return 0;
                if (depth > 0) return depth + 1;
                return 1; // Owned counter and showcase stock is as accessible as warehouse stock.
            }
            // Refrigerators store ingredients. Production inventories belong to their own machines.
            if (container.IsGameItemType("MACHINE") && !container.IsTag(MachineFridge.FRIDGE_TAG)) return -1;
            inVoidStorage |= container.identifier == VoidContainer;
            current = container;
            depth++;
        }
        return -1;
    }

    internal static int MissingItems(GameGridInventory input, IReadOnlyList<IngredientView> rules, int index)
    {
        var available = input.childItems.ToDictionary(item => item, item => item.unitCount);
        int missing = 0;
        // Reserve earlier requirements in production order so one unit cannot count twice.
        for (int current = 0; current <= index; current++)
        {
            var rule = rules[current];
            missing = rule.Amount;
            foreach (var item in input.childItems)
            {
                int units = Math.Min(missing, available[item]);
                if (units <= 0 || !rule.Matches(item)) continue;
                available[item] -= units;
                missing -= units;
                if (missing == 0) break;
            }
        }
        return missing;
    }

    internal static HashSet<string> MedicalPoolTypes(IEnumerable<GameItem> items, IngredientView pool) =>
        items.Where(pool.Matches).Select(item => item.identifier).ToHashSet(StringComparer.Ordinal);

    internal static bool CanSupply(GameItem machine, RecipeView recipe, IReadOnlyList<GameItem> sources)
    {
        if (!FoodProduction.QualityAllows(machine, recipe.Id) || recipe is ExtraRecipe extra && !extra.IsAvailable()) return false;
        var definition = FoodProduction.DefinitionFor(recipe);
        var input = MachineHelper.GetBodyStorage(machine, 2) as GameGridInventory;
        var items = (input?.childItems.AsEnumerable() ?? Enumerable.Empty<GameItem>()).Concat(sources)
            .Where(item => item.identifier is not VoidContainer and not VoidTank).Distinct().ToArray();
        var remaining = items.ToDictionary(item => item, item => item.unitCount);
        long calories = 0;
        foreach (var rule in FoodProduction.InputRules(definition, recipe as ExtraRecipe))
        {
            if (recipe is not ExtraRecipe && FoodProcessingRules.IsMedicalPool(rule.ItemId))
                return MedicalPoolTypes(items, rule).Count >= rule.Amount;
            int missing = rule.Amount;
            foreach (var item in items)
            {
                if (remaining[item] <= 0 || !rule.Matches(item)) continue;
                int count = Math.Min(missing, remaining[item]);
                remaining[item] -= count;
                missing -= count;
                calories += (long)count * item.GetTagReadonly("CALORIE_VALUE_TAG").GetInt();
                if (missing == 0) break;
            }
            if (missing > 0) return false;
        }
        if (recipe.OutputItemId == Definitions.FoodDefinitions.IndustrialFishFeedId && calories < recipe.OutputCount) return false;
        var (parts, quality) = FoodProduction.WaterNeeds(definition);
        if (parts == 0) return true;
        var water = (MachineHelper.GetBodyStorage(machine, 5) as GameSlotInventory)?.childItem;
        if (water != null && WaterHelper.GetTotalVolume(water) > 0 && FoodMachines.WaterQuality(water) < quality) return false;
        long volume = water == null ? 0 : WaterHelper.GetTotalVolume(water);
        foreach (var source in sources)
        {
            if (source == water || !source.IsTag("LIQUID_CONTAINER_TAG") ||
                remaining.TryGetValue(source, out int left) && left == 0 || FoodMachines.WaterQuality(source) < quality) continue;
            volume += WaterHelper.GetTotalVolume(source);
        }
        return volume >= parts;
    }
}
