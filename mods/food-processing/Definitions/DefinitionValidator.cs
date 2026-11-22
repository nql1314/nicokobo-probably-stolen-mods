namespace Nicokobo.FoodProcessing.Definitions;

public static class DefinitionValidator
{
    public static IReadOnlyList<string> ValidateRecipes(IReadOnlyList<RecipeDefinition> recipes)
    {
        var errors = new List<string>();
        var outputs = FoodDefinitions.Items.Where(item => item.Kind is ItemKind.Food or ItemKind.Drink or ItemKind.Medical)
            .Select(item => item.Id).Concat(new[] { FoodDefinitions.BlueBloodBagId,
                FoodDefinitions.LivestockImmuneSerumId, FoodDefinitions.LivestockSteroidSerumId, FoodDefinitions.RubbingAlcoholId,
                FoodDefinitions.NativeCheeseId, FoodDefinitions.NativeAnimalFatId, FoodDefinitions.NativeInstantRamenId,
                FoodDefinitions.NativeSodaId, FoodDefinitions.NativeEnergyDrinkId, FoodDefinitions.ProcessedMeatId,
                FoodDefinitions.IndustrialFishFeedId })
            .ToHashSet(StringComparer.Ordinal);
        foreach (var recipe in recipes)
        {
            bool pooledKit = FoodDefinitions.IsPooledMedicalKit(recipe.OutputItemId);
            int itemAmount = pooledKit ? FoodDefinitions.MedicalKitMinimum : 1;
            if (!FoodDefinitions.Machines.Any(machine => machine.ItemId == recipe.MachineId) ||
                recipe.Stars is < 1 or > 3 || recipe.MarkupPercent != (recipe.Stars == 3 ? 100 : recipe.Stars == 2 ? 50 : 25) ||
                recipe.Requirements.Count > 3 || (recipe.Stars < 3 && recipe.Requirements.Count == 3 &&
                    !recipe.Requirements.Any(input => input.Kind == RecipeResourceKind.Liquid && input.ResourceId == "water" || input.ResourceId == "bottled_water_premium")) ||
                !outputs.Contains(recipe.OutputItemId) || recipe.BaseOutputCount is < 1 or > 24 ||
                (FoodDefinitions.IsCareKit(recipe.OutputItemId) &&
                    (recipe.BaseOutputCount != 1 || recipe.Requirements.Any(input => input.Kind != RecipeResourceKind.Item || input.Amount != itemAmount))) ||
                recipe.ItemInputs.Count == 0 || recipe.Requirements.Select(input => input.ResourceId).Distinct().Count() != recipe.Requirements.Count)
                errors.Add($"Invalid recipe header/output: {recipe.Code}");
            foreach (var input in recipe.Requirements)
            {
                if (string.IsNullOrWhiteSpace(input.ResourceId) || input.Amount < 1 || input.Basis != RecipeAmountBasis.PerBatch ||
                    input.Kind is not (RecipeResourceKind.Item or RecipeResourceKind.Liquid or RecipeResourceKind.TaggedItem) ||
                    input.Conditions.Select(condition => condition.Property).Distinct().Count() != input.Conditions.Count)
                    errors.Add($"Invalid ingredient: {recipe.Code}");
                if (input.Kind == RecipeResourceKind.Liquid && (recipe.MachineId != FoodDefinitions.InfusionTowerItem.Id ||
                    input.ResourceId != "water" || input.Amount > int.MaxValue / 1000))
                    errors.Add($"Invalid water input: {recipe.Code}");
                if (input.Kind == RecipeResourceKind.TaggedItem && (input.ResourceId != FoodDefinitions.RawMeatTag ||
                    !input.Conditions.Any(condition => condition.Property == RecipeProperty.RawMeat)))
                    errors.Add($"Invalid raw meat category: {recipe.Code}");
                if (input.Conditions.Any(condition => condition.Property switch
                    {
                        RecipeProperty.WineScore => input.Kind != RecipeResourceKind.Item || input.ResourceId != "wine_bottle" || condition.MinimumValue is < 5 or > 6,
                        RecipeProperty.GenuineInjector => input.Kind != RecipeResourceKind.Item ||
                            input.ResourceId != FoodDefinitions.ImmunivaxId || condition.MinimumValue != 1,
                        RecipeProperty.UsableGlowberry or RecipeProperty.RipeGlowberry => input.Kind != RecipeResourceKind.Item ||
                            input.ResourceId != "wine_berry" || condition.MinimumValue != 1,
                        RecipeProperty.SmallOrStandardNutrifruit or RecipeProperty.LargeOrHugeNutrifruit =>
                            input.Kind != RecipeResourceKind.Item || input.ResourceId != "nutrifruit" || condition.MinimumValue != 1,
                        RecipeProperty.SmallFishFillet or RecipeProperty.StandardFishFillet =>
                            input.Kind != RecipeResourceKind.Item || input.ResourceId != "fish_fillet" || condition.MinimumValue != 1,
                        RecipeProperty.SingleItem => input.Kind != RecipeResourceKind.Item || condition.MinimumValue != 1,
                        RecipeProperty.RawMeat => input.Kind != RecipeResourceKind.TaggedItem ||
                            input.ResourceId != FoodDefinitions.RawMeatTag || condition.MinimumValue != 1,
                        RecipeProperty.WaterQuality => input.Kind != RecipeResourceKind.Liquid ||
                            condition.MinimumValue is < 1 or > 3,
                        _ => true
                    }))
                    errors.Add($"Invalid ingredient condition: {recipe.Code}");
            }
            if (recipe.Code is "F01" or "F02" || recipe.OutputRule != OutputRule.Fixed)
                errors.Add($"Retired or invalid output rule: {recipe.Code}");
            if (recipe.Code is "F03" or "F06")
            {
                string required = recipe.Code == "F03" ? "nutrifruit" : "coffee_bean";
                string output = recipe.Code == "F03" ? FoodDefinitions.NutrifruitJuice.Id : FoodDefinitions.CannedCoffee.Id;
                if (recipe.OutputItemId != output || recipe.Requirements.Count != 2 ||
                    !recipe.ItemInputs.Any(input => input.ResourceId == required) ||
                    !recipe.Requirements.Any(input => input.Kind == RecipeResourceKind.Liquid && input.ResourceId == "water"))
                    errors.Add($"{recipe.Code} requires fruit/coffee and water");
            }
            if (recipe.Code == "P10" && (recipe.OutputItemId != FoodDefinitions.BlueBloodBagId ||
                !recipe.ItemInputs.Any(input => input.ResourceId == FoodDefinitions.BloodBagId) ||
                !recipe.ItemInputs.Any(input => input.ResourceId == FoodDefinitions.ImmunivaxId &&
                    input.Conditions.Any(condition => condition.Property == RecipeProperty.GenuineInjector && condition.MinimumValue == 1))))
                errors.Add("Blue blood requires a blood bag and a genuine injector");
            if (recipe.Code == "F12" && !recipe.ItemInputs.Any(input =>
                    input.Kind == RecipeResourceKind.TaggedItem && input.ResourceId == FoodDefinitions.RawMeatTag &&
                    input.Conditions.Any(condition => condition.Property == RecipeProperty.RawMeat)))
                errors.Add($"{recipe.Code} requires the small/large raw meat category");
            if (recipe.Code is "F15" or "F16" or "F17" && (recipe.MachineId != FoodDefinitions.InfusionTowerItem.Id ||
                !recipe.Requirements.Any(input => input.Kind == RecipeResourceKind.Liquid && input.ResourceId == "water")))
                errors.Add($"{recipe.Code} requires a bottler water input");
            if (recipe.Code == "F17" && !recipe.Requirements.Any(input => input.Kind == RecipeResourceKind.Liquid &&
                    Nicokobo.FoodProcessing.Domain.FoodProcessingRules.MinimumWaterQuality(input) >= 2))
                errors.Add("Rubbing alcohol requires high-quality water or better");
            if (recipe.Code is "F27" or "F28" or "F29" or "F30" or "F31" or "F32" or "F33" or "F34" or "F35" or "F36" or "F37" or "F38")
            {
                var original = FoodDefinitions.DefaultRecipes.Single(value => value.Code == recipe.Code);
                if (recipe.OutputItemId != original.OutputItemId || !recipe.ItemInputs.Select(value => value.ResourceId)
                    .OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(original.ItemInputs.Select(value => value.ResourceId).OrderBy(id => id, StringComparer.Ordinal)) ||
                    recipe.Requirements.Any(value => value.Kind != RecipeResourceKind.Item))
                    errors.Add("Invalid food ingredient group or output: " + recipe.Code);
            }
        }
        var combinations = recipes.GroupBy(recipe => recipe.MachineId + ":" + string.Join("|", recipe.Requirements
            .OrderBy(input => input.ResourceId, StringComparer.Ordinal).Select(input => input.Kind + ":" + input.ResourceId + ":" +
                string.Join(",", input.Conditions.OrderBy(condition => condition.Property).Select(condition => condition.Property + "=" + condition.MinimumValue)))));
        foreach (var group in combinations.Where(group => group.Count() > 1))
            errors.Add("Ambiguous ingredient combination: " + string.Join(", ", group.Select(recipe => recipe.Code)));
        var edges = recipes.GroupBy(recipe => recipe.OutputItemId).ToDictionary(group => group.Key,
            group => group.SelectMany(recipe => recipe.ItemInputs).Select(input => input.ResourceId).Where(outputs.Contains).ToArray());
        var visiting = new HashSet<string>(); var visited = new HashSet<string>();
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) { errors.Add("Production cycle at " + id); return; }
            if (edges.TryGetValue(id, out var inputs)) foreach (var input in inputs) Visit(input);
            visiting.Remove(id); visited.Add(id);
        }
        foreach (var id in edges.Keys) Visit(id);
        return errors.AsReadOnly();
    }
}
