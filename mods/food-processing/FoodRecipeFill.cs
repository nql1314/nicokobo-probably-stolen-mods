using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing.Domain;

namespace Nicokobo.FoodProcessing;

internal static class FoodRecipeFill
{
    private const string LiquidContainerTag = "LIQUID_CONTAINER_TAG";

    internal static string Fill(GameItem machine, RecipeView recipe)
    {
        if (!PlayerStore.HasInstance || EmporiumEntry.Instance?.invElement is not { } warehouse ||
            ItemMouseDragHandler.current?.IsDraggingItem == true || machine.parentInventory == null ||
            !GeneralHelper.IsItemEffectivelyOwned(machine) || recipe.MachineId != machine.identifier ||
            !FoodProduction.QualityAllows(machine, recipe.Id) || recipe is ExtraRecipe extra && !extra.IsAvailable() ||
            MachineHelper.GetBodyStorage(machine, 2) is not GameGridInventory input)
            return FoodText.Get("food.fill_unavailable");

        if (FoodProduction.HasIngredients(machine, recipe)) return FoodText.Get("food.fill_ready");
        var definition = FoodProduction.DefinitionFor(recipe);
        var rules = FoodProduction.InputRules(definition, recipe as ExtraRecipe).ToArray();
        var (waterParts, waterQuality) = FoodProduction.WaterNeeds(definition);
        var candidates = PlayerStore.Instance.FindAllItem()
            .Where(item => (waterParts > 0 && item.IsTag(LiquidContainerTag)) || rules.Any(rule => rule.Matches(item)));
        var sources = FoodIngredientStock.OrderedSources(candidates, warehouse);

        bool moved = FillItems(input, rules, sources, warehouse, recipe is not ExtraRecipe);
        if (waterParts > 0 && MachineHelper.GetBodyStorage(machine, 5) is GameSlotInventory water)
            moved |= FillWater(water, waterParts, waterQuality, sources, warehouse);

        return FoodProduction.HasIngredients(machine, recipe) ? FoodText.Get("food.fill_ready")
            : FoodText.Get(moved ? "food.fill_incomplete" : "food.fill_no_transfer");
    }

    private static bool FillItems(GameGridInventory input, IReadOnlyList<IngredientView> rules,
        IReadOnlyList<GameItem> sources, GameGridInventory warehouse, bool builtIn)
    {
        bool moved = false;
        for (int index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var poolTypes = builtIn && FoodProcessingRules.IsMedicalPool(rule.ItemId)
                ? FoodIngredientStock.MedicalPoolTypes(input.childItems, rule) : null;
            int missing = poolTypes == null ? FoodIngredientStock.MissingItems(input, rules, index) : rule.Amount - poolTypes.Count;
            foreach (var source in sources)
            {
                if (missing == 0) break;
                if (source.parentInventory == null || source.identifier is FoodIngredientStock.VoidContainer or FoodIngredientStock.VoidTank ||
                    !rule.Matches(source) || poolTypes?.Contains(source.identifier) == true) continue;
                while (missing > 0 && FoodIngredientStock.SourcePriority(source, warehouse) >= 0)
                {
                    var transfer = NativeItemTransferApi.Move(source, input, poolTypes == null ? missing : 1);
                    if (transfer.Indeterminate) throw new InvalidOperationException("Ingredient move: " + transfer.Status);
                    if (transfer.Moved == 0) break;
                    moved = true;
                    missing -= transfer.Moved;
                    if (poolTypes != null)
                    {
                        poolTypes.Add(source.identifier);
                        break;
                    }
                }
            }
        }
        return moved;
    }

    private static bool FillWater(GameSlotInventory slot, int parts, int quality,
        IReadOnlyList<GameItem> sources, GameGridInventory warehouse)
    {
        if (slot.IsInsertLocked()) return false;
        bool UsableVessel(GameItem item) => item.parentInventory != null && item.unitCount == 1 && item.IsTag(LiquidContainerTag) &&
            WaterHelper.GetFilterItem(item) == null;
        bool SuppliesWater(GameItem item) => UsableVessel(item) && WaterHelper.GetTotalVolume(item) > 0 &&
            FoodMachines.WaterQuality(item) >= quality && FoodIngredientStock.SourcePriority(item, warehouse) >= 0;

        var target = slot.childItem;
        bool moved = false;
        var supply = sources.Where(SuppliesWater).ToArray();
        if (target == null && supply.Length > 0)
        {
            foreach (var vessel in sources)
            {
                if (vessel.identifier == FoodIngredientStock.VoidTank || !UsableVessel(vessel) ||
                    FoodIngredientStock.SourcePriority(vessel, warehouse) < 0) continue;
                int volume = WaterHelper.GetTotalVolume(vessel);
                if (volume + WaterHelper.GetFreeCapacity(vessel) < parts ||
                    volume > 0 && FoodMachines.WaterQuality(vessel) < quality) continue;
                var transfer = NativeItemTransferApi.Move(vessel, slot, 1);
                if (transfer.Indeterminate) throw new InvalidOperationException("Water vessel move: " + transfer.Status);
                if (transfer.Moved == 0) continue;
                target = slot.childItem;
                moved = true;
                break;
            }
        }
        if (target == null || !UsableVessel(target)) return moved;
        foreach (var source in supply)
        {
            int volume = WaterHelper.GetTotalVolume(target);
            int missing = Math.Min(parts - volume, WaterHelper.GetFreeCapacity(target));
            if (missing <= 0 || volume > 0 && FoodMachines.WaterQuality(target) < quality) break;
            if (source == target || !SuppliesWater(source)) continue;
            var transfer = NativeLiquidTransferApi.Pour(source, target, missing);
            if (transfer.Indeterminate) throw new InvalidOperationException("Water transfer: " + transfer.Status);
            moved |= transfer.PartsMoved > 0;
        }
        return moved;
    }

}
