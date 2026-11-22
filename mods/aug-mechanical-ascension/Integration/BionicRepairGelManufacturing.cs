using AugMechanicalAscension.Domain;
using Nicokobo.ModUtilities;
using ProbablyStolen.ModSupport;

namespace AugMechanicalAscension.Integration;

internal static class BionicRepairGelManufacturing
{
    private const string OwnerId = "nicokobo.aug";
    private static bool _staged, _recipesStaged;

    internal static bool Install(Action<string> log)
    {
        var result = OfficialItemApi.RegisterItem(OwnerId, BionicRepairGelDefinition.Id, Create,
            new NativeItemOptions(Name: new(BionicRepairGelDefinition.Id + ".name"),
                ShortDescription: new("aug.right_click_repair_body_to_restore_aug"),
                FlavorText: new("aug.seal_the_cracks_and_get_back_to"))
            {
                NpcTrade = new(NpcTradeStockCategory.Medical)
            });
        _staged = result.Status is SubmitStatus.Accepted or SubmitStatus.AlreadyPresent;
        log($"[Manufacturing] medical={BionicRepairGelDefinition.Id}; stage={result.Status}; reason={result.Reason}");
        return _staged;
    }
    internal static void RegisterLoot()=>LootRegistry.AddEntry(OwnerId,TableMaster.medicalTable,BionicRepairGelDefinition.Id,AugNumbers.Distribution.RepairGelLootWeight);

    internal static void RegisterRecipes()
    {
        if (!_staged || _recipesStaged) return;
        foreach (var route in new[]
        {
            (Id: BionicRepairGelDefinition.RecipeId,
                Chinese: AugText.GetFor("aug.bionic_repair_gel_blood_bag", false),
                English: AugText.GetFor("aug.bionic_repair_gel_blood_bag", true),
                Ingredients: BionicRepairGelDefinition.Ingredients,
                LabelItemId: "blood_bag"),
            (Id: BionicRepairGelDefinition.SkinGraftRecipeId,
                Chinese: AugText.GetFor("aug.bionic_repair_gel_skin_graft", false),
                English: AugText.GetFor("aug.bionic_repair_gel_skin_graft", true),
                Ingredients: BionicRepairGelDefinition.SkinGraftIngredients,
                LabelItemId: "tilapia_skin_graft")
        })
        {
            var ingredients = route.Ingredients.Select(ingredient => new Nicokobo.ContentInterop.IngredientView(
                ingredient.ItemId, ingredient.Quantity, item => item.identifier == ingredient.ItemId, ingredient.ItemId, ingredient.ItemId));
            Nicokobo.ContentInterop.ExtraRecipes.Register(OwnerId, new(route.Id, BionicRepairGelDefinition.MachineId,
                ingredients, BionicRepairGelDefinition.Id, route.Chinese, route.English,
                () => OfficialItemApi.Exists(BionicRepairGelDefinition.MachineId) &&
                    OfficialItemApi.Exists(BionicRepairGelDefinition.Id), () => BionicRepairGelDefinition.Id,
                AugNumbers.BionicRepairGel.OutputCount)
            {
                LocalizedOutputName = () => ModText.Get(MechcorePackage.Id, BionicRepairGelDefinition.Id + ".name") +
                    " (" + NativeItemText.Name(route.LabelItemId, route.LabelItemId) + ")"
            });
        }
        _recipesStaged=true;
    }

    private static GameItem Create()
    {
        var item = DirectoryMaster.Create<GameItem>("common_medical");
        if (item == null || NativeObject.Handle(item) == IntPtr.Zero || item.identifier != "common_medical" || item.unitCount != 1)
            throw new InvalidOperationException("Repair gel template unavailable");
        try
        {
            item.SetSpriteAndShapeFromMod(MechcorePackage.Id, ItemArtwork.BionicRepairGel);
            item.SetShape(new GridShapeBuilder().SetData(new string('1',
                AugNumbers.BionicRepairGel.Width * AugNumbers.BionicRepairGel.Height),
                AugNumbers.BionicRepairGel.Width).Build());
            item.RemoveAllGameItemType();
            item.SetGameItemType("MEDICAL");
            item.identifier = BionicRepairGelDefinition.Id;
            item.name = AugText.Get(item.identifier + ".name");
            item.shortDescription = AugText.Get("aug.right_click_repair_body_to_restore_aug");
            item.flavorText = AugText.Get("aug.seal_the_cracks_and_get_back_to");
            item.unitBaseValue = 0;
            item.unitValue = AugNumbers.BionicRepairGel.BaseValue;
            item.mayStackItemFunc = null;
            item.mayActivateSlotItemFunc = null;
            item.canActivateSlotItemFunc = null;
            item.onActivateSlotItemFunc = null;
            item.onLoaded = null;
            item.forceDisableUse = false;
            item.forceDisableActivate = false;
            item.activateDefault = false;
            if (!item.IsGameItemType("MEDICAL") || item.shape?.width != AugNumbers.BionicRepairGel.Width ||
                item.shape?.height != AugNumbers.BionicRepairGel.Height)
                throw new InvalidOperationException("Repair gel factory mismatch");
            return item;
        }
        catch
        {
            if (item.parentInventory == null) item.Destroy();
            throw;
        }
    }
}
