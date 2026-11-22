namespace MechcoreProtocol.SynthesisExpansion.Definitions;

public static class DefinitionValidator
{
    public static IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var items = SynthesisDefinitions.Items;
        var machines = SynthesisDefinitions.Machines;
        var guides = SynthesisDefinitions.Guides;
        var recipes = SynthesisDefinitions.Recipes;
        var itemById = items.GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.Ordinal);
        Unique(items.Select(item => item.Id), "item ID", errors);
        Unique(items.Select(item => item.Code), "item code", errors);
        Unique(machines.Select(machine => machine.ItemId), "machine ID", errors);
        Unique(guides.Select(guide => guide.ItemId), "guide ID", errors);
        Unique(recipes.Select(recipe => recipe.Id), "recipe ID", errors);
        Unique(recipes.Select(recipe => recipe.Code), "recipe code", errors);
        Unique(recipes.Select(recipe => recipe.Id).Concat(
            GlassRecycling.Feedstocks.Select(feedstock => feedstock.Recipe.Id)),
            "all recipe ID", errors);

        if (items.Count != 25 || machines.Count != 4 || guides.Count != 3 || recipes.Count != 26 ||
            items.Any(item => item.Id == SynthesisDefinitions.UniversalManufacturerItem.Id))
            errors.Add("Expected 25 owned items, three owned machines plus the Forge terminal, three guides and twenty-six recipes.");
        foreach (var item in items)
            if (item.BaseValue <= 0 || item.ContrabandLevel is < 0 or > 4 || item.Width is < 1 or > SynthesisNumbers.Validation.MaxItemWidth ||
                item.Height is < 1 or > SynthesisNumbers.Validation.MaxItemHeight || string.IsNullOrWhiteSpace(item.Source) ||
                string.IsNullOrWhiteSpace(item.NativeTemplateId) ||
                string.IsNullOrWhiteSpace(item.ChineseName) ||
                string.IsNullOrWhiteSpace(item.EnglishName))
                errors.Add($"Invalid item attributes: {item.Id}");
        foreach (var item in items)
        {
            var expected = SynthesisNumbers.Items.ForCode(item.Code);
            if (item.Width != expected.Width || item.Height != expected.Height)
                errors.Add($"Item footprint differs from the compiled configuration: {item.Code}");
            if (item.ContrabandLevel != expected.ContrabandLevel)
                errors.Add($"Item contraband level differs from the compiled configuration: {item.Code}");
        }
        if (SynthesisDefinitions.Quartz.Source !=
                "refiner_glass_recycling+night_shop+scavenging" ||
            GlassRecycling.Feedstocks.Count != 4 ||
            GlassRecycling.Feedstocks.Any(feedstock =>
                feedstock.RequiredCount <= 0 ||
                string.IsNullOrWhiteSpace(feedstock.ItemId) ||
                feedstock.Recipe.MachineId != SynthesisDefinitions.RefinerItem.Id ||
                !itemById.TryGetValue(feedstock.Recipe.OutputItemId, out var refinerOutput) ||
                refinerOutput.Kind != ItemKind.Material ||
                feedstock.Recipe.BaseOutputCount is < 1 or > SynthesisNumbers.Validation.MaxRecipeOutputCount ||
                feedstock.Recipe.OutputRule != OutputRule.Fixed ||
                feedstock.Recipe.Requirements.Count != 1 ||
                feedstock.Input.Basis != RecipeAmountBasis.PerBatch ||
                feedstock.Input.Conditions.Count != 0) ||
            GlassRecycling.Feedstocks.Select(feedstock => feedstock.ItemId)
                .Distinct(StringComparer.Ordinal).Count() != 4)
            errors.Add("Quartz glass recycling definition is invalid.");
        if (items.Any(item => item.Kind == ItemKind.Guide &&
                              item.NativeTemplateId != "water_guide" ||
                item.Kind == ItemKind.Machine &&
                              item.NativeTemplateId != "furnace" ||
                item.Kind == ItemKind.Storage &&
                              item.NativeTemplateId != "smuggler_bay" ||
                item.Kind == ItemKind.Robot && item.NativeTemplateId != "satchel" ||
                item.Kind == ItemKind.Tool && item.NativeTemplateId != "screwdriver"))
            errors.Add("Machine/guide templates do not match the content gate.");

        foreach (var machine in machines)
        {
            var item = machine.ItemId == SynthesisDefinitions.UniversalManufacturerItem.Id
                ? SynthesisDefinitions.UniversalManufacturerItem : itemById.GetValueOrDefault(machine.ItemId);
            if (item == null ||
                item.Kind != ItemKind.Machine ||
                !itemById.TryGetValue(machine.GuideId, out var guideItem) ||
                guideItem.Kind != ItemKind.Guide)
                errors.Add($"Machine/guide item missing: {machine.Code}");
            if (machine.InputWidth <= 0 || machine.InputHeight <= 0 ||
                machine.OutputWidth <= 0 || machine.OutputHeight <= 0 ||
                machine.ModuleWidth <= 0 || machine.ModuleHeight <= 0 ||
                machine.Width != item?.Width || machine.Height != item?.Height ||
                machine.BasePowerCost <= 0 || !machine.SupportsEfficiency ||
                machine.SupportsQuality != (machine.Quality != QualityMode.None) ||
                machine.WaterSourceSlots != (machine.Code == "K04" ? 1 : 0))
                errors.Add($"Invalid machine layout or attributes: {machine.Code}");
            var expectedQuality = machine.Code switch
            {
                "K01" or "K03" => QualityMode.Purity,
                "K02" => QualityMode.ComponentGrade,
                _ => QualityMode.None
            };
            int expectedStep = expectedQuality is QualityMode.Purity or QualityMode.ComponentGrade
                ? machine.Code switch
                {
                    "K01" => SynthesisNumbers.Machines.K01.QualityStep,
                    "K02" => SynthesisNumbers.Machines.K02.QualityStep,
                    "K03" => SynthesisNumbers.Machines.K03.QualityStep,
                    _ => 0
                } : 0;
            if (machine.Quality != expectedQuality || machine.QualityStep != expectedStep ||
                machine.Trigger != TriggerMode.Nightly)
                errors.Add($"Invalid machine trigger/quality: {machine.Code}");
        }

        foreach (var guide in guides)
        {
            if (guide.ItemId == SynthesisDefinitions.CyberneticCatalogItem.Id)
            {
                if (guide.Recipes.Count != 0)
                    errors.Add("Cybernetic catalog must contain effects instead of recipes.");
                continue;
            }
            if (guide.Recipes.Count == 0 ||
                machines.Where(machine => machine.GuideId == guide.ItemId)
                    .Any(machine => !guide.Recipes.Any(recipe => recipe.MachineId == machine.ItemId)))
                errors.Add($"Guide does not cover its machines: {guide.ItemId}");
            if (guide.Recipes.Any(recipe => !recipes.Contains(recipe)))
                errors.Add($"Guide contains a recipe outside the loaded catalog: {guide.ItemId}");
        }
        if (recipes.Any(recipe => !guides.Any(guide => guide.Recipes.Contains(recipe))))
            errors.Add("Recipe missing from the guides.");
        foreach (var recipe in recipes)
        {
            var machine = machines.SingleOrDefault(candidate => candidate.ItemId == recipe.MachineId);
            var itemInputs = recipe.ItemInputs;
            bool validOutput = recipe.OutputItemId is SynthesisDefinitions.BlueBloodBagId or
                SynthesisDefinitions.ProcessedMeatId or SynthesisDefinitions.CommonElectronicId or
                SynthesisDefinitions.MouseTrapId or "metal_ingot" ||
                itemById.TryGetValue(recipe.OutputItemId, out var output) &&
                output.Kind is ItemKind.Material or ItemKind.Cybernetic or ItemKind.Household or ItemKind.Luxury or ItemKind.Drink or ItemKind.Food or ItemKind.Storage or ItemKind.Robot or ItemKind.Tool;
            if (machine == null ||
                recipe.BaseOutputCount is < 1 or > SynthesisNumbers.Validation.MaxMachineOutputCount ||
                itemInputs.Count == 0 ||
                !validOutput)
                errors.Add($"Invalid recipe header/output: {recipe.Id}");
            Unique(recipe.Requirements.Select(input => input.ResourceId),
                $"input of {recipe.Id}", errors);
            foreach (var input in recipe.Requirements)
            {
                if (input.Amount <= 0 || input.Conditions == null)
                {
                    errors.Add($"Invalid recipe amount/conditions: {recipe.Id}/{input.ResourceId}");
                    continue;
                }
                Unique(input.Conditions.Select(condition => condition.Property.ToString()),
                    $"condition of {recipe.Id}/{input.ResourceId}", errors);
                if (input.Kind == RecipeResourceKind.Liquid)
                {
                    if (input.ResourceId != "water" || input.Basis != RecipeAmountBasis.PerBatch ||
                        input.Amount > int.MaxValue / 1000 || input.Conditions.Count != 0 ||
                        machine?.WaterSourceSlots != 1)
                        errors.Add($"Invalid water input: {recipe.Id}");
                    continue;
                }
                if (input.Kind is not (RecipeResourceKind.Item or RecipeResourceKind.TaggedItem) ||
                    input.Kind == RecipeResourceKind.TaggedItem && input.ResourceId != "CONTAINER_TAG" ||
                    input.Basis != RecipeAmountBasis.PerBatch ||
                    string.IsNullOrWhiteSpace(input.ResourceId) ||
                    input.Conditions.Any(condition => !ValidCondition(input.ResourceId, condition)))
                    errors.Add($"Invalid item rule: {recipe.Id}/{input.ResourceId}");
            }
            if (!Enum.IsDefined(typeof(OutputRule), recipe.OutputRule) || recipe.OutputRule == OutputRule.MeatValue)
                errors.Add($"Invalid output rule: {recipe.Id}");
            if (SynthesisDefinitions.CarriesMachinedPurity(recipe.OutputItemId) &&
                !itemInputs.Any(input => SynthesisDefinitions.CarriesPurity(input.ResourceId)))
                errors.Add($"Machined-purity output requires at least one purity-bearing input: {recipe.Id}");
            if (machine?.Code == "K01" && recipe.OutputItemId == "metal_ingot" &&
                !Domain.RefinerMetalRules.IsSmelting(recipe.OutputRule))
                errors.Add($"Refiner metal ingots require a metal smelting output rule: {recipe.Id}");
            if (Domain.RefinerMetalRules.IsSmelting(recipe.OutputRule) &&
                (machine?.Code != "K01" || recipe.OutputItemId != "metal_ingot" ||
                 recipe.Requirements.Any(input => input.Kind != RecipeResourceKind.Item ||
                     input.ResourceId is not ("common_ore" or "scrap_metal"))))
                errors.Add($"Metal smelting requires refiner ore/scrap inputs and metal ingot output: {recipe.Id}");
            if (recipe.Code == "M09" && (recipe.OutputItemId != SynthesisDefinitions.ScavengingRobot.Id ||
                recipe.OutputRule != OutputRule.Fixed || itemInputs.Count != 3 ||
                !itemInputs.Any(input => input.Kind == RecipeResourceKind.Item &&
                    input.ResourceId == SynthesisDefinitions.MechanicalCore.Id) ||
                !itemInputs.Any(input => input.Kind == RecipeResourceKind.Item && input.ResourceId == "metal_scanner" && input.Amount == 1 &&
                    input.Conditions.Any(condition => condition.Property == RecipeProperty.ScavScannerLevel &&
                        condition.MinimumValue == SynthesisNumbers.ScavengingRobot.InitialScannerChance)) ||
                !itemInputs.Any(input => input.Kind == RecipeResourceKind.TaggedItem && input.ResourceId == "CONTAINER_TAG" && input.Amount == 1 &&
                    input.Conditions.Any(condition => condition.Property == RecipeProperty.EmptyStorage))))
                errors.Add($"Scavenging robot requires a mechanical core, valid scav scanner and empty storage area: {recipe.Id}");
        }

        // Include native foods produced by these machines as well as custom
        // products, so an edited packing recipe cannot introduce a cycle.
        var edges = recipes.GroupBy(recipe => recipe.OutputItemId,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.SelectMany(recipe => recipe.ItemInputs)
                    .Select(input => input.ResourceId)
                    .Where(id => recipes.Any(candidate => candidate.OutputItemId == id)).Distinct().ToArray(),
                StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in edges.Keys)
            Visit(id);
        return Array.AsReadOnly(errors.ToArray());

        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id))
            {
                errors.Add($"Production cycle at {id}");
                return;
            }
            if (edges.TryGetValue(id, out var inputs))
                foreach (var input in inputs) Visit(input);
            visiting.Remove(id);
            visited.Add(id);
        }
    }

    private static bool ValidCondition(string itemId, RecipeCondition condition) =>
        condition.Property switch
        {
            RecipeProperty.MetalPurity => SynthesisDefinitions.CarriesPurity(itemId) &&
                Enum.IsDefined(typeof(IngotPurityGrade), (IngotPurityGrade)condition.MinimumValue) &&
                condition.MinimumValue > (int)IngotPurityGrade.Unknown,
            RecipeProperty.GenuineInjector => itemId == SynthesisDefinitions.ImmunivaxId &&
                condition.MinimumValue == 1,
            RecipeProperty.UsableGlowberry => itemId == "wine_berry" && condition.MinimumValue == 1,
            RecipeProperty.ScavScannerLevel => itemId == "metal_scanner" &&
                condition.MinimumValue == SynthesisNumbers.ScavengingRobot.InitialScannerChance,
            RecipeProperty.EmptyStorage => itemId == "CONTAINER_TAG" && condition.MinimumValue == 1,
            _ => false
        };

    private static void Unique(IEnumerable<string> values, string label,
        List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
                errors.Add($"Duplicate or empty {label}: {value}");
    }
}
