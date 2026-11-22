using Nicokobo.ModUtilities;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using Nicokobo.ContentInterop;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class SynthesisGuide
{
    private static CustomUIWindow? _window;
    internal static void Close() { _window?.Close(); _window = null; }

    private static IEnumerable<RecipeDefinition> OwnRecipes(bool refining) =>
        ProductionCatalog.AllRecipes.Where(recipe =>
            refining == (recipe.MachineId == SynthesisDefinitions.RefinerItem.Id) &&
            DirectoryMaster.Has<GameItem>(recipe.OutputItemId));

    private static IEnumerable<ExtraRecipe> AdditionalSynthesisRecipes() =>
        SynthesisDefinitions.Machines.SelectMany(machine => ExtraRecipes.ForMachine(machine.ItemId))
            .Where(recipe => !ProductionCatalog.Recipes.Any(own => own.Id == recipe.Id));

    internal static bool HasContent(string guideId) =>
        guideId == SynthesisDefinitions.CyberneticCatalogItem.Id ||
        (guideId == SynthesisDefinitions.RefiningGuideItem.Id
            ? OwnRecipes(true).Any()
            : OwnRecipes(false).Any() || AdditionalSynthesisRecipes().Any());

    internal static void Open(string guideId)
    {
        Close();
        if (CustomUIManager.Instance == null || !HasContent(guideId)) return;
        var guide = SynthesisDefinitions.Item(guideId);
        var ui = CustomUIManager.Window(SynthesisDefinitions.OwnerId + ".guide",
            ContentText.LocalizedName(guide.Id), CustomUIManager.LAYER_PAPER)
            .SetSize(700, 560).SetCloseOnEscape().UseMachineryStyle();
        ui.BeginScroll(460);
        if (guideId == SynthesisDefinitions.CyberneticCatalogItem.Id)
        {
            ui.AddLabel(SynthesisText.Get("synthesis.duplicate_owned_cybernetics_do_not_stack_install"));
            foreach (var item in CyberneticCatalog.Read())
            {
                ui.BeginRow();
                if (item.Sprite != null)
                {
                    ui.AddImage(item.Sprite, 48, 48);
                    ui.Last.SetFixedWidth(48);
                }
                ui.AddLabel(item.Name + " · " + CyberneticCatalog.Classification(item.Contraband) + "\n" + item.Description);
                ui.Last.SetFlexible(1, 0);
                ui.Last.text.enableWordWrapping = true;
                ui.End();
            }
            if (CyberneticCatalog.ItemIds.Any(id => !DirectoryMaster.Has<GameItem>(id)))
                ui.AddLabel(SynthesisText.Get("synthesis.enable_mechanical_ascension_to_include_its_seven"));
            ui.End(); ui.AddButton(LocHelper.GetLocalizedMainMenu("close"), Close); _window = ui.Show(); return;
        }
        bool refining = guideId == SynthesisDefinitions.RefiningGuideItem.Id;
        ui.AddLabel(SynthesisText.Get("synthesis.machines_process_overnight_every_100_performance_adds"));
        foreach (var recipe in OwnRecipes(refining))
        {
            ui.BeginRow();
            var product = RecipePresentation.ReadItem(recipe.OutputItemId);
            if (product.Sprite != null) ui.AddImage(product.Sprite, 48, 48);
            string inputs = string.Join(" + ", recipe.Requirements.Select(ContentText.LocalizedRequirement));
            string mode = recipe.OutputRule switch
            {
                OutputRule.MetalSmelting => " (" + ModText.Get(MechcorePackage.Id, "synthesis.normal_smelting") + ")",
                OutputRule.BlastMetalSmelting => " (" + ModText.Get(MechcorePackage.Id, "synthesis.blast_required") + ")",
                _ => ""
            };
            string title = ContentText.LocalizedName(recipe.MachineId) + mode;
            ui.AddLabel($"{title}\n{inputs} → {ContentText.LocalizedName(recipe.OutputItemId)} ×{recipe.BaseOutputCount}");
            ui.End();
            if (recipe.ItemInputs.Any(input => input.ResourceId.StartsWith("nicokobo.mechcore_protocol.module_matrix.", StringComparison.Ordinal) &&
                !DirectoryMaster.Has<GameItem>(input.ResourceId)))
                ui.AddLabel(SynthesisText.Get("synthesis.requires_the_continuous_modules_supplied_by_module"));
        }
        if (!refining)
        foreach (var recipe in AdditionalSynthesisRecipes())
        {
            var machine = SynthesisDefinitions.Machines.Single(definition => definition.ItemId == recipe.MachineId);
            var product = RecipePresentation.ReadItem(recipe.OutputItemId);
            ui.BeginRow();
            if (product.Sprite != null) ui.AddImage(product.Sprite, 48, 48);
            ui.AddLabel(ContentText.LocalizedName(machine.ItemId) + "\n" +
                string.Join(" + ", recipe.Ingredients.Select(RecipePresentation.Ingredient)) +
                " → " + RecipePresentation.Output(recipe, product.Name) + " ×" + recipe.OutputCount);
            ui.End();
        }
        ui.End(); ui.AddButton(LocHelper.GetLocalizedMainMenu("close"), Close); _window = ui.Show();
    }
}
