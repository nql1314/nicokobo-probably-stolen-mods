using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing.Definitions;
using Nicokobo.FoodProcessing.Domain;
using Nicokobo.ModUtilities;

namespace Nicokobo.FoodProcessing;

internal static class FoodInterop
{
    internal static RecipeView View(RecipeDefinition recipe)
    {
        return new(recipe.Id, recipe.MachineId,
            recipe.ItemInputs.Select((rule, index) => new IngredientView(rule.ResourceId, rule.Amount, item => FoodMachines.Matches(item, rule),
                Requirement(rule, false), Requirement(rule, true))
            {
                LocalizedLabel = () => (index == 0 ? new string('★', recipe.Stars) + " · " + FoodText.Get("food.quality_requirement", recipe.RequiredQuality) + " · " + FoodText.Get("food.markup", RecipeConfiguration.RecipeMarkup(recipe)) + "\n" : "") + LocalizedRequirement(rule)
            }),
            recipe.OutputItemId, recipe.BaseOutputCount, FoodProduction.WaterNeeds(recipe).Parts,
            machine => FoodProduction.AllowsRecipe(machine, recipe.Id),
            machine => FoodProduction.QualityAllows(machine, recipe.Id) ? FoodProduction.Preview(machine, recipe) : "");
    }

    internal static string Restriction => ModText.Get(FoodDefinitions.ModId, "food.ingredient_restriction");

    internal static string LocalizedRequirement(RecipeRequirement rule)
    {
        if (rule.Kind == RecipeResourceKind.Liquid)
        {
            string grade = LocHelper.GetLocalizedMechanic(FoodProcessingRules.MinimumWaterQuality(rule) switch
            {
                3 => "mech_condition_pure_water",
                2 => "mech_condition_high_quality_water",
                _ => "mech_condition_basewater"
            });
            return NativeItemText.Name(rule.ResourceId, rule.ResourceId) + " " + rule.Amount + " ml (≥ " + grade + ")";
        }

        var ids = rule.ResourceId == FoodDefinitions.RawMeatTag ? new[] { "small_raw_meat", "raw_meat" }
            : FoodProcessingRules.ItemInputIds(rule);
        string Name(string id) => id switch
        {
            "wine_bottle" => FoodText.Get("food.finished_wine"),
            FoodDefinitions.ImmunivaxId when rule.ResourceId == FoodDefinitions.AdvancedMedicalPool => FoodText.Get("food.genuine_immunivax"),
            _ => FoodDefinitions.IsCustomFood(id) ? FoodText.Name(FoodDefinitions.Item(id)) : ContentText.LocalizedNativeName(id)
        };
        string names = string.Join(" / ", ids.Select(Name));
        if (FoodProcessingRules.IsMedicalPool(rule.ResourceId))
            return FoodText.Get("food.medical_kit_pool_minimum", rule.Amount) + "\n" + names;
        if (ids.Count > 1) names = "(" + names + ")";
        string condition = string.Join(", ", rule.Conditions.Select(value => value.Property switch
        {
            RecipeProperty.WineScore => FoodText.Get("food.wine_score", value.MinimumValue),
            RecipeProperty.GenuineInjector => LocHelper.GetLocalizedMechanic("mech_condition_genuine"),
            RecipeProperty.UsableGlowberry => ModText.Get(FoodDefinitions.ModId, "food.usable_berry"),
            _ => ConditionLabel(value.Property)
        }).Where(value => value.Length != 0));
        return names + " ×" + rule.Amount + (condition.Length == 0 ? "" : " (" + condition + ")");
    }

    private static string Requirement(RecipeRequirement rule, bool english)
    {
        if (FoodProcessingRules.IsMedicalPool(rule.ResourceId))
            return string.Format(ModText.GetFor(FoodDefinitions.ModId, "food.medical_kit_pool_minimum", english ? "en" : "zh"), rule.Amount);
        string condition = string.Join(english ? ", " : "、", rule.Conditions.Select(value => value.Property switch
        {
            RecipeProperty.WineScore => FoodText.Get("food.wine_score", value.MinimumValue),
            RecipeProperty.GenuineInjector => FoodText.Get("food.genuine"),
            RecipeProperty.UsableGlowberry => FoodText.Get("food.usable_berries"),
            RecipeProperty.RawMeat => FoodText.Get("food.small_or_large_raw_meat"),
            _ => ConditionLabel(value.Property, english)
        }).Where(value => value.Length != 0));
        var name = rule.Conditions.Any(value => value.Property == RecipeProperty.SmallFishFillet)
            ? string.Join(" / ", FoodProcessingRules.ItemInputIds(rule).Select(id => ContentText.Name(id, english)))
            : rule.Conditions.Any(value => value.Property == RecipeProperty.StandardFishFillet)
                ? ModText.GetFor(FoodDefinitions.ModId, "food.standard_fish_fillet", english ? "en" : "zh")
                : ContentText.Name(rule.ResourceId, english);
        return name + " ×" + rule.Amount +
            (condition.Length == 0 ? "" : " (" + condition + ")");
    }

    private static string ConditionLabel(RecipeProperty property, bool? english = null)
    {
        string? key = property switch
        {
            RecipeProperty.SmallOrStandardNutrifruit => "food.small_or_standard_crop",
            RecipeProperty.LargeOrHugeNutrifruit => "food.large_or_huge_crop",
            RecipeProperty.RipeGlowberry => "food.ripe_bloomberry",
            RecipeProperty.SingleItem => "food.unstacked_ingredient",
            _ => null
        };
        if (key == null) return "";
        return english.HasValue ? ModText.GetFor(FoodDefinitions.ModId, key, english.Value ? "en" : "zh") : FoodText.Get(key);
    }
    internal static void Register()
    {
        foreach (var definition in FoodDefinitions.Machines)
            MachineCatalog.Register(FoodDefinitions.ModId, definition.ItemId, machine => new MachinePorts(
                Required(machine, 2), Required(machine, 3),
                MachineHelper.GetBodyStorage(machine, 1) as GameInventory, MachineHelper.GetBodyStorage(machine, 0) as GameSlotInventory,
                definition.WaterSourceSlots == 0 ? null : MachineHelper.GetBodyStorage(machine, 5) as GameSlotInventory),
                () => FoodProduction.OrderRecipes(definition.ItemId).Select(View)
                    .Concat(ExtraRecipes.ForMachine(definition.ItemId).Select(extra => new ExtraRecipe(extra.Id, extra.MachineId,
                        extra.Ingredients, extra.OutputItemId, extra.ChineseOutput, extra.EnglishOutput, extra.IsAvailable,
                        extra.ResolveOutputId, extra.OutputCount, machine => FoodProduction.AllowsRecipe(machine, extra.Id),
                        machine => FoodProduction.QualityAllows(machine, extra.Id) ? FoodProduction.Preview(machine, extra) : "")
                    { LocalizedOutputName = extra.LocalizedOutputName })).ToArray(), nightOrder: definition.Code == "K06" ? 10 : 40);
        SupplyCatalog.Register(FoodDefinitions.ModId, FoodDefinitions.Items.Where(item => item.Kind is ItemKind.Food or ItemKind.Drink)
            .Select(item => new StockOffer(item.Id, StockCategory.Food)));
    }
    private static GameInventory Required(GameItem machine, int index) => MachineHelper.GetBodyStorage(machine, index) as GameInventory
        ?? throw new InvalidOperationException("Food inventory missing: " + index);
}
