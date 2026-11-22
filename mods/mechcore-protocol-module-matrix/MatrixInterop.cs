using Nicokobo.ModUtilities;
using Nicokobo.ContentInterop;
using MechcoreProtocol.ModuleMatrix.Definitions;

namespace MechcoreProtocol.ModuleMatrix;

internal static class MatrixInterop
{
    private static bool Available(ModuleTierDefinition definition) =>
        (!definition.IsMatrix || MatrixMod.Enabled(definition.Id)) && DirectoryMaster.Has<GameItem>(definition.Id);
    internal static bool IsBasic(GameItem item) => MatrixMod.Active && item.unitCount > 0 && GeneralHelper.IsItemOwned(item) &&
        (item.IsGameItemType("MODULE") || item.IsGameItemType("NODE")) &&
        ModuleTierDefinitions.Find(item.identifier) is { Tier: ModuleTier.Basic } definition && Available(definition);
    internal static IReadOnlyList<string> RarePool() => ModuleTierDefinitions.RarePool(Available);
    internal static void Register()
    {
        var ingredient = new IngredientView("system_module_performance",
            ModuleTierDefinitions.UpgradeInputCount,
            IsBasic,
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.any_two_basic_modules_nodes_identical_or", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.any_two_basic_modules_nodes_identical_or", "en"))
        {
            LocalizedLabel = () => ModText.Get(MechcorePackage.Id, "matrix.upgrade_input", ModuleTierDefinitions.UpgradeInputCount)
        };
        ExtraRecipes.Register(MatrixMod.Id, new ExtraRecipe(ModuleTierDefinitions.UpgradeRecipeId,
            ModuleTierDefinitions.UpgradeMachineId,
            new[] { ingredient },
            "system_module_overclock",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.random_rare_module_node_excludes_functional_modules", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "matrix.random_rare_module_node_excludes_functional_modules", "en"),
            () => MatrixMod.Active && MachineCatalog.Contains(ModuleTierDefinitions.UpgradeMachineId) && RarePool().Count != 0,
            () => { var pool = RarePool(); return ModuleTierDefinitions.PickRare(pool, RNG.GetRandomIntExculsive(0, pool.Count)); })
        {
            LocalizedOutputName = () => ModText.Get(MechcorePackage.Id, "matrix.upgrade_output")
        });
        SupplyCatalog.Register(MatrixMod.Id, ModuleIconDefinitions.All.Where(icon => MatrixMod.Enabled(icon.ItemId)).Select(icon =>
            new StockOffer(icon.ItemId, StockCategory.Module) { Suppliers = StockSupplier.Thief | StockSupplier.Inventor, IncludeInNightShop = false }));
    }
}
