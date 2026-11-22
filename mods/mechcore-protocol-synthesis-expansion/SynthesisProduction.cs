using Nicokobo.ContentInterop;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class SynthesisProduction
{
    internal const string BasisTag = "NICOKOBO_FORGE_PRODUCTION_BASIS_V1";

    internal static IEnumerable<RecipeDefinition> OrderRecipes(GameItem machine, GameGridInventory input) =>
        RecipeOrder.Sort(machine.identifier, ProductionCatalog.AllRecipes,
            input.childItems.Where(item => item.identifier == "common_ore").Sum(item => (long)item.unitCount),
            input.childItems.Where(item => item.identifier == "scrap_metal").Sum(item => (long)item.unitCount),
            machine.identifier == SynthesisDefinitions.RefinerItem.Id && SynthesisIngredients.HasBlast(machine));

    internal static bool TryProcess(GameItem machine, GameSlotInventory batterySlot, GameGridInventory input,
        GameGridInventory output, RecipeDefinition recipe)
        => TryProcess(machine, batterySlot, input, output, recipe, null);

    internal static bool TryProcess(GameItem machine, GameSlotInventory batterySlot, GameGridInventory input,
        GameGridInventory output, ExtraRecipe extra)
    {
        if (extra.MachineId != machine.identifier || !extra.IsAvailable()) return false;
        var recipe = new RecipeDefinition("EXT", extra.Id, extra.MachineId,
            extra.Ingredients.Select(rule => new RecipeRequirement(RecipeResourceKind.Item, rule.ItemId, rule.Amount,
                RecipeAmountBasis.PerBatch, Array.Empty<RecipeCondition>())).ToArray(), extra.OutputItemId, extra.OutputCount, OutputRule.Fixed);
        return TryProcess(machine, batterySlot, input, output, recipe, extra);
    }

    private static bool TryProcess(GameItem machine, GameSlotInventory batterySlot, GameGridInventory input,
        GameGridInventory output, RecipeDefinition recipe, ExtraRecipe? extra)
    {
        if (recipe.MachineId != machine.identifier) return false;
        using var work = ProductionHooks.Begin(new WorkContext(machine, new MachinePorts(input, output,
            MachineHelper.GetBodyStorage(machine, 1) as GameInventory, batterySlot), extra ?? SynthesisInterop.View(recipe)));
        if (extra != null && !extra.IsAvailable()) return false;
        if (!machine.GetTagReadonly("is_machinery_powered").GetBool()) return false;
        if (RefinerMetalRules.IsSmelting(recipe.OutputRule) &&
            !RefinerMetalRules.MatchesMode(recipe.OutputRule, SynthesisIngredients.HasBlast(machine))) return false;
        var battery = batterySlot.childItem;
        int power = MachineryHelper.GetMachinePowerUsage(machine);
        if (battery == null || (DevicePower.TryEnergy(battery, out int sharedEnergy)
            ? sharedEnergy < power : !PowerHelper.CanDrawPowerSource(battery, power))) return false;
        var takes = extra == null ? SelectInputs(input, recipe) : SelectInputs(input, extra.Ingredients);
        if (takes == null) return false;
        decimal value = takes.Sum(take => checked(InputValue(take.Item) * take.Count));
        long basis = checked((long)decimal.Ceiling((value * (100m + RecipeConfiguration.ProductionMarkupPercent(machine.identifier)) / 100m +
            machine.GetTagReadonly(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).GetInt()) / recipe.BaseOutputCount));
        if (!ProductionBatch.Run(battery, input, output, takes, recipe.BaseOutputCount,
            () => extra == null ? recipe.OutputItemId : extra.ResolveOutputId(), (product, index) =>
            {
                if (!RefinerMetalRules.IsSmelting(recipe.OutputRule)) ProductionValue.SetBasis(product, basis);
                SynthesisQuality.Prepare(machine, recipe, product, takes.Select(take => (take.Item, take.Count)).ToArray());
                ManufacturedProducts.Prepare(recipe, product, takes.Select(take => (take.Item, take.Count)).ToArray());
            }, power)) return false;
        work.Commit();
        return true;
    }

    private static List<BatchInput>? SelectInputs(GameGridInventory inventory, RecipeDefinition recipe) =>
        ProductionBatch.SelectInputs(inventory, recipe.ItemInputs.OrderBy(rule => rule.Kind == RecipeResourceKind.TaggedItem).Select(rule =>
            new IngredientView(rule.ResourceId, rule.Amount, item => SynthesisIngredients.Matches(item, rule), "", "")));
    private static List<BatchInput>? SelectInputs(GameGridInventory inventory, IReadOnlyList<IngredientView> rules) =>
        ProductionBatch.SelectInputs(inventory, rules);

    internal static decimal InputValue(GameItem item) => ProductionValue.InputValue(item);
    internal static void SetBasis(GameItem item, long basis) => ProductionValue.SetBasis(item, basis);
    internal static void RestoreBasis(GameItem item) => ProductionValue.RestoreBasis(item);
    internal static long ReadBasis(GameItem item, long fallback) => ProductionValue.ReadBasis(item, fallback);
    internal static long ComposeRealValue(long basis, long innate, double final) => ProductionValue.ComposeRealValue(basis, innate, final);
}
