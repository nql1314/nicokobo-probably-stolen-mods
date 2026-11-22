namespace AugMechanicalAscension.Domain;

internal static class BionicRepairGelDefinition
{
    internal const string Id = "nicokobo.aug.bionic_repair_gel";
    internal const string RecipeId = "nicokobo.aug.recipe.bionic_repair_gel";
    internal const string SkinGraftRecipeId = RecipeId + ".skin_graft";
    internal const string MachineId = "nicokobo.mechcore.synthesis.liquid_infusion_tower";
    internal static string ChineseName => AugText.GetFor(Id + ".name", false);
    internal static string EnglishName => AugText.GetFor(Id + ".name", true);
    internal static IReadOnlyList<WorkshopMaterialCost> Ingredients { get; } =
        Array.AsReadOnly(new[]
        {
            new WorkshopMaterialCost("blood_bag", AugNumbers.BionicRepairGel.BloodBagCount),
            new WorkshopMaterialCost("bandage_item", AugNumbers.BionicRepairGel.BandageCount)
        });
    internal static IReadOnlyList<WorkshopMaterialCost> SkinGraftIngredients { get; } =
        Array.AsReadOnly(new[]
        {
            new WorkshopMaterialCost("tilapia_skin_graft", 1),
            new WorkshopMaterialCost("bandage_item", AugNumbers.BionicRepairGel.BandageCount)
        });
}
