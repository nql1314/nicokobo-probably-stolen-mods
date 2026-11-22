using Nicokobo.ContentInterop;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class SynthesisInterop
{
    internal static RecipeView View(RecipeDefinition recipe) => new(recipe.Id, recipe.MachineId,
        recipe.ItemInputs.Select(rule => new IngredientView(rule.ResourceId, rule.Amount, item => SynthesisIngredients.Matches(item, rule),
            ContentText.RequirementText(rule, false), ContentText.RequirementText(rule, true))
        { LocalizedLabel = () => ContentText.LocalizedRequirement(rule) }), recipe.OutputItemId, recipe.BaseOutputCount,
        isAvailableFor: machine => !RefinerMetalRules.IsSmelting(recipe.OutputRule) ||
            RefinerMetalRules.MatchesMode(recipe.OutputRule, SynthesisIngredients.HasBlast(machine)));
    internal static void Register()
    {
        foreach (var definition in ProductionCatalog.Machines)
            MachineCatalog.Register(SynthesisDefinitions.OwnerId, definition.ItemId, machine => Ports(machine),
                () => ProductionCatalog.Recipes.Where(recipe => recipe.MachineId == definition.ItemId).Select(View).ToArray(),
                nightOrder: definition.Code == "K01" ? 10 : definition.Code == "K02" ? 20 : 30);
        int priority = 1000;
        foreach (var recipe in RecipeOrder.Sort(SynthesisDefinitions.UniversalManufacturerItem.Id, ProductionCatalog.AllRecipes, 0, 0, false))
        {
            var view = View(recipe);
            ExtraRecipes.Register(SynthesisDefinitions.OwnerId, new ExtraRecipe(view.Id, view.MachineId, view.Ingredients, view.OutputItemId,
                ContentText.Name(view.OutputItemId, false), ContentText.Name(view.OutputItemId, true), () => DirectoryMaster.Has<GameItem>(view.OutputItemId), () => view.OutputItemId, view.OutputCount)
            {
                Priority = priority--,
                LocalizedOutputName = () => ContentText.LocalizedName(view.OutputItemId),
                PrepareOutput = (machine, product, items, counts) =>
                {
                    var inputs = items.Select((item, index) => (item, counts[index])).ToArray();
                    long basis = checked((long)decimal.Ceiling((inputs.Sum(input => SynthesisProduction.InputValue(input.item) * input.Item2) *
                        (100m + RecipeConfiguration.ProductionMarkupPercent(machine.identifier)) / 100m +
                        machine.GetTagReadonly(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).GetInt()) / recipe.BaseOutputCount));
                    ProductionValue.SetBasis(product, basis);
                    SynthesisQuality.Prepare(machine, recipe, product, inputs);
                    ManufacturedProducts.Prepare(recipe, product, inputs);
                }
            });
        }
        DevicePower.RegisterConsumer(SynthesisDefinitions.OwnerId, SynthesisDefinitions.ScavengingRobot.Id, item => (MachineHelper.GetBodyStorage(item, 0) as GameSlotInventory)?.childItem);
        DevicePower.RegisterConsumer(SynthesisDefinitions.OwnerId, SynthesisDefinitions.CyberEye.Id, item => AugScanner.GetBatterySlot(item)?.childItem);
        SupplyCatalog.Register(SynthesisDefinitions.OwnerId, ProductionCatalog.Items.Select(Offer).Where(offer => offer != null).Cast<StockOffer>());
    }
    private static GameInventory Required(GameItem machine, int index) => MachineHelper.GetBodyStorage(machine, index) as GameInventory
        ?? throw new InvalidOperationException("Synthesis inventory missing: " + index);
    internal static MachinePorts Ports(GameItem machine) => new(
        Required(machine, 2), Required(machine, 3),
        MachineHelper.GetBodyStorage(machine, 1) as GameInventory, MachineHelper.GetBodyStorage(machine, 0) as GameSlotInventory);
    private static StockOffer? Offer(ItemDefinition item) => item.Id == SynthesisDefinitions.Quartz.Id || item.Id == SynthesisDefinitions.TitaniumOre.Id
        ? new StockOffer(item.Id, StockCategory.Ore, SynthesisNumbers.Distribution.OreLootWeight)
            { MinerWeight = SynthesisNumbers.Distribution.OreMinerWeight, PreserveLootWeight = true }
        : item.Kind == ItemKind.Material ? new StockOffer(item.Id, StockCategory.Material)
            { MinimumDay = SynthesisNumbers.Distribution.RefinedMaterialMinimumStockDay } : null;
}
