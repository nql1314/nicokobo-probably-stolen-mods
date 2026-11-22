using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing.Definitions;
using Nicokobo.FoodProcessing.Domain;

namespace Nicokobo.FoodProcessing;

internal static class FoodProduction
{
    internal sealed record PreviewOutput(RecipeView Recipe, int Batches, long Quantity);
    internal sealed record CyclePreview(int Capacity, int Batches, IReadOnlyList<PreviewOutput> Outputs,
        IReadOnlyList<BatchInput> Inputs, long Calories, long WaterParts, long Power);

    internal static int BatchCapacity(GameItem machine) => 1 + Math.Max(0, MachineryHelper.GetCurrentPerformanceBonus(machine)) /
        FoodNumbers.Machines.PerformancePerExtraBatch;

    internal static CyclePreview PreviewCycle(GameItem machine)
    {
        int capacity = BatchCapacity(machine), batches = 0;
        long calories = 0, waterUsed = 0, powerUsed = 0;
        var outputs = new List<PreviewOutput>();
        var consumed = new Dictionary<GameItem, int>();
        var recipes = RecipeChoices(machine.identifier).Where(recipe => AllowsRecipe(machine, recipe.Id))
            .Select(recipe => (View: recipe, Definition: recipe is ExtraRecipe extra ? ExtraDefinition(extra)
                : FoodDefinitions.Recipes.First(value => value.Id == recipe.Id))).ToArray();
        var water = (MachineHelper.GetBodyStorage(machine, 5) as GameSlotInventory)?.childItem;
        int remainingWater = water == null ? 0 : WaterHelper.GetTotalVolume(water);
        var battery = (MachineHelper.GetBodyStorage(machine, 0) as GameSlotInventory)?.childItem;
        int power = MachineryHelper.GetMachinePowerUsage(machine);
        for (; batches < capacity; batches++)
        {
            if (!machine.GetTagReadonly("is_machinery_powered").GetBool() || battery == null) break;
            long requiredPower = powerUsed + power;
            if (DevicePower.TryEnergy(battery, out int energy))
            {
                if (energy < requiredPower) break;
            }
            else
            {
                // Native hypertech cells are output-limited, and converters use a
                // service during the night. Neither is a finite charge reservoir.
                long draw = battery.IsTag("HYPERTECH_BATTERY_TAG") || battery.IsTag("CURRENT_CONVERTER_TAG")
                    ? power : requiredPower;
                if (draw > int.MaxValue || !PowerHelper.CanDrawPowerSource(battery, (int)draw)) break;
            }
            bool matched = false;
            foreach (var recipe in recipes)
            {
                if (PreviewInputs(machine, recipe.Definition, recipe.View as ExtraRecipe, out var takes,
                    out _, out int waterParts, out long batchCalories, consumed, remainingWater) != null) continue;
                foreach (var take in takes!)
                    consumed[take.Item] = (consumed.TryGetValue(take.Item, out int previous) ? previous : 0) + take.Count;
                remainingWater -= waterParts;
                if (FoodDefinitions.IsFoodOutput(recipe.View.OutputItemId) || recipe.View.OutputItemId == FoodDefinitions.IndustrialFishFeedId)
                    calories += batchCalories;
                waterUsed += waterParts; powerUsed += power;
                int index = outputs.FindIndex(output => output.Recipe.OutputItemId == recipe.View.OutputItemId);
                if (index < 0) outputs.Add(new PreviewOutput(recipe.View, 1, recipe.View.OutputCount));
                else outputs[index] = outputs[index] with
                { Batches = outputs[index].Batches + 1, Quantity = outputs[index].Quantity + recipe.View.OutputCount };
                matched = true;
                break;
            }
            if (!matched) break;
        }
        return new CyclePreview(capacity, batches, outputs,
            consumed.Select(pair => new BatchInput(pair.Key, pair.Value, pair.Key.unitCount)).ToArray(), calories, waterUsed, powerUsed);
    }

    internal const string BasisTag = "NICOKOBO_FORGE_PRODUCTION_BASIS_V1";
    internal const string SelectionTag = "NICOKOBO_FOOD_RECIPE_SELECTION_V1";
    internal static bool AllowsRecipe(GameItem machine, string recipeId) =>
        QualityAllows(machine, recipeId) && (string.IsNullOrEmpty(machine.GetTagReadonly(SelectionTag).GetString()) || machine.GetTagReadonly(SelectionTag).GetString() == recipeId);

    internal static bool QualityAllows(GameItem machine, string recipeId) =>
        MachineryHelper.GetCurrentQualityBonus(machine) >= (FoodDefinitions.Recipes.FirstOrDefault(recipe => recipe.Id == recipeId)?.RequiredQuality ?? 0);
    internal static bool AcceptsIngredient(GameItem item) => FoodIngredients.Rejection(item) == null;

    internal static IEnumerable<RecipeDefinition> OrderRecipes(string machineId)
    {
        // Conditioned ingredients such as fine wine keep priority. Size only
        // limits eligibility so fruit snacks do not take ingredients from meals.
        return FoodDefinitions.Recipes.Where(recipe => recipe.MachineId == machineId)
            .OrderByDescending(recipe => recipe.ItemInputs.Sum(input => input.Conditions.Count(condition =>
                condition.Property is RecipeProperty.WineScore or RecipeProperty.GenuineInjector or
                    RecipeProperty.UsableGlowberry or RecipeProperty.RipeGlowberry)))
            .ThenByDescending(recipe => recipe.ItemInputs.Count)
            .ThenByDescending(recipe => recipe.ItemInputs.Sum(input => (long)input.Amount))
            .ThenByDescending(recipe => recipe.Requirements.Where(input => input.Kind == RecipeResourceKind.Liquid)
                .Sum(input => (long)input.Amount))
            .ThenBy(recipe => recipe.ItemInputs.Count(input => input.Kind == RecipeResourceKind.TaggedItem));
    }

    internal static string Preview(GameItem machine, RecipeDefinition recipe)
        => Preview(machine, recipe, null, true);

    internal static string Preview(GameItem machine, ExtraRecipe recipe)
        => Preview(machine, ExtraDefinition(recipe), recipe, true);

    internal static string PreviewForSelection(GameItem machine, RecipeView recipe, bool includeRecipeSummary = true)
        => Preview(machine, DefinitionFor(recipe), recipe as ExtraRecipe, false, includeRecipeSummary);

    internal static bool HasIngredients(GameItem machine, RecipeView recipe)
        => PreviewInputs(machine, DefinitionFor(recipe), recipe as ExtraRecipe, out _, out _, out _, out _) == null;

    internal static RecipeView? OutputPreview(GameItem machine)
    {
        var recipes = RecipeChoices(machine.identifier).Where(recipe => QualityAllows(machine, recipe.Id));
        string? selected = machine.GetTagReadonly(SelectionTag).GetString();
        // A selected recipe remains visible while waiting for ingredients. Automatic
        // mode only previews a matching batch, using the production attempt order.
        return string.IsNullOrEmpty(selected)
            ? recipes.FirstOrDefault(recipe => HasIngredients(machine, recipe))
            : recipes.FirstOrDefault(recipe => recipe.Id == selected);
    }

    internal static IEnumerable<RecipeView> RecipeChoices(string machineId) => OrderRecipes(machineId)
        .Select(FoodInterop.View).Concat(ExtraRecipes.ForMachine(machineId));

    internal static RecipeDefinition DefinitionFor(RecipeView recipe) => recipe is ExtraRecipe extra
        ? ExtraDefinition(extra) : FoodDefinitions.Recipes.First(value => value.Id == recipe.Id);

    internal static (int Parts, int Quality) WaterNeeds(RecipeDefinition recipe)
    {
        int parts = 0, quality = 0;
        foreach (var rule in recipe.Requirements.Where(rule => rule.Kind == RecipeResourceKind.Liquid))
        {
            parts = checked(parts + rule.Amount * 1000);
            quality = Math.Max(quality, FoodProcessingRules.MinimumWaterQuality(rule));
        }
        return (parts, quality);
    }

    private static string? PreviewInputs(GameItem machine, RecipeDefinition recipe, ExtraRecipe? extra,
        out List<BatchInput>? takes, out GameItem? water, out int waterParts, out long calories,
        IReadOnlyDictionary<GameItem, int>? consumed = null, int? remainingWater = null)
    {
        water = null; waterParts = 0; calories = 0; takes = null;
        if (!QualityAllows(machine, recipe.Id)) return FoodText.Get("food.quality_locked", recipe.RequiredQuality, MachineryHelper.GetCurrentQualityBonus(machine));
        var input = MachineHelper.GetBodyStorage(machine, 2) as GameGridInventory;
        takes = input == null ? null : SelectInputs(input, recipe, extra, consumed);
        if (takes == null)
        {
            string? reason = input?.childItems.Select(FoodIngredients.Rejection).FirstOrDefault(value => value != null);
            return reason ?? FoodText.Get("food.missing_eligible_ingredients_calories_depend_on_the");
        }
        var waterNeeds = WaterNeeds(recipe);
        waterParts = waterNeeds.Parts;
        water = waterParts == 0 ? null : (MachineHelper.GetBodyStorage(machine, 5) as GameSlotInventory)?.childItem;
        if (waterParts > 0 && (water == null || (remainingWater ?? WaterHelper.GetTotalVolume(water)) < waterParts ||
            FoodMachines.WaterQuality(water) < waterNeeds.Quality))
            return FoodText.Get("food.not_enough_water_or_water_quality_is");
        calories = FoodProcessingRules.TotalCalories(takes.Select(take => (take.Count, take.Item.GetTagReadonly(FoodProcessingRules.CalorieTag).GetInt())));
        if (recipe.OutputItemId == FoodDefinitions.IndustrialFishFeedId && calories < recipe.BaseOutputCount)
            return FoodText.Get("food.not_enough_calories_to_produce_nonempty_feed");
        return null;
    }

    private static string Preview(GameItem machine, RecipeDefinition recipe, ExtraRecipe? extra, bool checkSelection, bool includeRecipeSummary = true)
    {
        if (!QualityAllows(machine, recipe.Id)) return FoodText.Get("food.quality_locked", recipe.RequiredQuality, MachineryHelper.GetCurrentQualityBonus(machine));
        if (checkSelection && !AllowsRecipe(machine, recipe.Id)) return FoodText.Get("food.a_different_recipe_is_selected");
        string? missing = PreviewInputs(machine, recipe, extra, out var takes, out var water, out int waterParts, out long calories);
        if (missing != null) return missing;
        int power = MachineryHelper.GetMachinePowerUsage(machine);
        var battery = (MachineHelper.GetBodyStorage(machine, 0) as GameSlotInventory)?.childItem;
        if (!machine.GetTagReadonly("is_machinery_powered").GetBool() || battery == null ||
            (DevicePower.TryEnergy(battery, out int energy) ? energy < power : !PowerHelper.CanDrawPowerSource(battery, power)))
            return FoodText.Get("food.no_power_source_or_not_enough_power");
        long basis = BatchBasis(machine, recipe, takes!, waterParts, water);
        long outputValue = FoodRecipeValue.ReadOutputs(recipe, basis, calories)?.Total ?? basis;
        string details = (FoodDefinitions.IsFoodOutput(recipe.OutputItemId) || recipe.OutputItemId == FoodDefinitions.IndustrialFishFeedId
                ? calories + " kcal · " : "") +
            (includeRecipeSummary || waterParts > 0 ? waterParts / 1000 + " ml · " : "") + power +
            FoodText.Get("food.power_output_space_is_checked_when_processing") +
            "\n" + FoodText.Get("food.batch_production_basis") + outputValue;
        if (!includeRecipeSummary) return details;
        string materials = string.Join(" + ", takes!.Select(take => take.Item.GetDisplayName() + " ×" + take.Count));
        return materials + "\n" + FoodText.Get("food.batch") + recipe.BaseOutputCount + FoodText.Get("food.portions") + details;
    }

    internal static bool TryProcess(GameItem machine, GameSlotInventory batterySlot, GameGridInventory input,
        GameGridInventory output, GameSlotInventory? waterSlot, RecipeDefinition recipe)
        => TryProcess(machine, batterySlot, input, output, waterSlot, recipe, null);

    internal static bool TryProcess(GameItem machine, GameSlotInventory batterySlot, GameGridInventory input,
        GameGridInventory output, GameSlotInventory? waterSlot, ExtraRecipe extra)
    {
        if (extra.MachineId != machine.identifier || !extra.IsAvailable()) return false;
        return TryProcess(machine, batterySlot, input, output, waterSlot, ExtraDefinition(extra), extra);
    }

    private static RecipeDefinition ExtraDefinition(ExtraRecipe extra) => new("EXT", extra.Id, extra.MachineId,
        extra.Ingredients.Select(rule => new RecipeRequirement(RecipeResourceKind.Item, rule.ItemId, rule.Amount,
            RecipeAmountBasis.PerBatch, Array.Empty<RecipeCondition>())).ToArray(), extra.OutputItemId, extra.OutputCount, OutputRule.Fixed);

    private static long BatchBasis(GameItem machine, RecipeDefinition recipe, List<BatchInput> takes, int waterParts, GameItem? water,
        bool packingFeeOnly = false)
    {
        decimal value = takes.Sum(take => checked(InputValue(take.Item) * take.Count));
        if (waterParts != 0) value += FoodRecipeValue.WaterValue(waterParts, InnateFactor(water!));
        long basis = FoodRecipeValue.CalculateBatch(recipe, value,
            machine.GetTagReadonly(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).GetInt());
        return packingFeeOnly ? basis - checked((long)decimal.Ceiling(value)) : basis;
    }

    private static bool TryProcess(GameItem machine, GameSlotInventory batterySlot, GameGridInventory input,
        GameGridInventory output, GameSlotInventory? waterSlot, RecipeDefinition recipe, ExtraRecipe? extra)
    {
        if (!AllowsRecipe(machine, recipe.Id)) return false;
        using var work = ProductionHooks.Begin(new WorkContext(machine, new MachinePorts(input, output,
            MachineHelper.GetBodyStorage(machine, 1) as GameInventory, batterySlot, waterSlot), extra ?? FoodInterop.View(recipe)));
        if (extra != null && !extra.IsAvailable()) return false;
        if (!machine.GetTagReadonly("is_machinery_powered").GetBool()) return false;
        var battery = batterySlot.childItem;
        int power = MachineryHelper.GetMachinePowerUsage(machine);
        if (battery == null || (DevicePower.TryEnergy(battery, out int sharedEnergy)
            ? sharedEnergy < power : !PowerHelper.CanDrawPowerSource(battery, power))) return false;
        var takes = SelectInputs(input, recipe, extra);
        if (takes == null) return false;
        var waterNeeds = WaterNeeds(recipe);
        int waterParts = waterNeeds.Parts;
        var water = waterSlot?.childItem;
        if (waterParts != 0 && (water == null || WaterHelper.GetTotalVolume(water) < waterParts ||
            FoodMachines.WaterQuality(water) < waterNeeds.Quality))
            return false;

        if (extra == null && FoodDefinitions.IsCareKit(recipe.OutputItemId))
        {
            long packingFee = BatchBasis(machine, recipe, takes, 0, null, packingFeeOnly: true);
            if (!FoodCareKit.TryPack(recipe.OutputItemId, battery, input, output, takes, power, packingFee)) return false;
            work.Commit();
            return true;
        }

        string? meatType = ResolveMeatType(takes);
        long calories = FoodProcessingRules.TotalCalories(takes.Select(take =>
            (take.Count, take.Item.GetTagReadonly(FoodProcessingRules.CalorieTag).GetInt())));
        if (recipe.OutputItemId == FoodDefinitions.IndustrialFishFeedId && calories < recipe.BaseOutputCount) return false;
        long basis = BatchBasis(machine, recipe, takes, waterParts, water);
        if (!ProductionBatch.Run(battery, input, output, takes, recipe.BaseOutputCount,
            () => extra == null ? recipe.OutputItemId : extra.ResolveOutputId(), (product, index) =>
            {
                string brand = extra == null ? FoodExpansion.Brand(recipe) : "";
                if (brand.Length != 0 && product.identifier != FoodDefinitions.ProcessedMeatId)
                    product.EnableManufacturer("nicokobo.food." + brand);
                long portionBasis = FoodRecipeValue.PortionBasis(product, basis, calories, recipe.BaseOutputCount, index);
                if (FoodStateValue.Supported(product)) FoodStateValue.SetBasis(product, portionBasis);
                else SetBasis(product, portionBasis);
                if (FoodDefinitions.IsFoodOutput(product.identifier))
                {
                    product.ModifyTag(FoodProcessingRules.CalorieTag, tag => tag.Enable().SetInt(
                        checked((int)FoodProcessingRules.Share(calories, recipe.BaseOutputCount, index))));
                    if (FoodDefinitions.IsCustomFood(product.identifier) ||
                        product.identifier is FoodDefinitions.NativeAnimalFatId or FoodDefinitions.ProcessedMeatId)
                    {
                        if (takes.Any(take => take.Item.IsTag("MEAT_FOOD_TAG"))) product.EnableTag("MEAT_FOOD_TAG");
                        if (meatType != null) product.ModifyTag("MEAT_TYPE_TAG", tag => tag.Enable().SetString(meatType));
                        if (FoodDefinitions.Items.Any(item => item.Id == product.identifier && item.IsAlcohol))
                        {
                            var alcohol = takes.Where(take => take.Item.IsTag("ALCOHOL_VALUE")).ToArray();
                            // Match native integer strength; weight only the units consumed from each stack.
                            int strength = checked((int)(alcohol.Sum(take => (long)take.Item.GetTagReadonly("ALCOHOL_VALUE").GetInt() * take.Count) /
                                Math.Max(1, alcohol.Sum(take => (long)take.Count))));
                            product.ModifyTag("ALCOHOL_VALUE", tag => tag.Enable().SetInt(strength));
                        }
                    }
                }
            }, power, water, waterParts)) return false;
        work.Commit();
        return true;
    }

    private static string? ResolveMeatType(IEnumerable<BatchInput> takes)
    {
        // Harvested caviar uses MEAT_TYPE_TAG to record which fish produced the roe.
        var meatTypes = takes.Where(take => take.Item.identifier != "caviar")
            .Select(take => take.Item.GetTagReadonly("MEAT_TYPE_TAG").GetString())
            .Where(type => !string.IsNullOrEmpty(type)).Distinct(StringComparer.Ordinal).ToArray();
        // Mixed batches must not claim a single species.
        return meatTypes.Length == 1 ? meatTypes[0] : null;
    }

    internal static IEnumerable<IngredientView> InputRules(RecipeDefinition recipe, ExtraRecipe? extra) => extra == null
        ? recipe.ItemInputs.OrderBy(rule => rule.Kind == RecipeResourceKind.TaggedItem).Select(rule =>
            new IngredientView(rule.ResourceId, rule.Amount, item => FoodMachines.Matches(item, rule), "", ""))
        : extra.Ingredients.Select(rule => new IngredientView(rule.ItemId, rule.Amount,
            item => AcceptsIngredient(item) && rule.Matches(item), rule.Chinese, rule.English));

    private static List<BatchInput>? SelectInputs(GameGridInventory inventory, RecipeDefinition recipe, ExtraRecipe? extra,
        IReadOnlyDictionary<GameItem, int>? consumed = null)
    {
        var rules = InputRules(recipe, extra);
        if (extra == null && FoodDefinitions.IsPooledMedicalKit(recipe.OutputItemId))
        {
            var pool = rules.Single();
            var takes = inventory.childItems.Where(item => consumed?.ContainsKey(item) != true && pool.Matches(item))
                .Select(item => new BatchInput(item, 1, item.unitCount)).ToList();
            int typeCount = takes.Select(take => take.Item.identifier).Distinct(StringComparer.Ordinal).Count();
            return typeCount >= pool.Amount ? takes : null;
        }
        if (consumed == null || consumed.Count == 0) return ProductionBatch.SelectInputs(inventory, rules);
        // Reserve earlier batches by identity before asking the real selector for
        // the next batch. This reuses its overlap/stack ordering without editing items.
        var reserved = consumed.Select(pair => new IngredientView(pair.Key.identifier, pair.Value,
            item => ReferenceEquals(item, pair.Key), "", ""));
        return ProductionBatch.SelectInputs(inventory, reserved.Concat(rules))?
            .Select(take => new BatchInput(take.Item,
                take.Count - (consumed.TryGetValue(take.Item, out int count) ? count : 0), take.Before))
            .Where(take => take.Count > 0).ToList();
    }

    internal static decimal InputValue(GameItem item) { FoodStateValue.Refresh(item); return ProductionValue.InputValue(item); }
    internal static void SetBasis(GameItem item, long basis) => ProductionValue.SetBasis(item, basis);
    private static decimal InnateFactor(GameItem item) => ProductionValue.InnateFactor(item);
}
