using Nicokobo.FoodProcessing.Definitions;
using Nicokobo.FoodProcessing.Domain;

namespace Nicokobo.FoodProcessing;

internal static class FoodItems
{
    internal static string SpriteKey(ItemDefinition definition) =>
        definition.Id.Substring(FoodDefinitions.IdPrefix.Length + 1).Replace('.', '_');

    internal static GameItem Create(ItemDefinition definition)
    {
        var item = definition.Kind switch
        {
            ItemKind.Machine => FoodMachines.Create(FoodDefinitions.Machine(definition.Id)),
            ItemKind.Guide => ItemDirectory.CreateEmptyItem(definition.Id),
            _ => ItemDirectory.CreateEmptyItem(definition.Id)
        };
        if (item == null || item.unitCount != 1)
            throw new InvalidOperationException("Food template unavailable: " + definition.Id);
        try
        {
            if (FoodDefinitions.IsCustomFood(definition.Id))
            {
                FoodItemHelper.InitFoodItem(item, FoodNumbers.ReferenceCalories(definition.Code),
                    definition.Category == "canned", FoodNumbers.IsMeat(definition.Code));
                item.AddAmenities("amenities_nutrition", definition.Code == "D05" ? 3 : definition.Code == "D09" ? 2 : 4,
                    definition.Code == "D05" ? 1 : definition.Code == "D09" ? 20 : 2);
                AudioHelper.InitEdibleAudio(item);
                if (definition.IsAlcohol)
                    item.ModifyTag("ALCOHOL_VALUE", tag => tag.Enable().SetInt(1));
            }
            item.identifier = definition.Id;
            string spriteKey = SpriteKey(definition);
            item.SetSpriteAndShapeFromMod(FoodDefinitions.ModId, spriteKey);
            var footprint = new GridShapeBuilder().SetData(new string('1', checked(definition.Width * definition.Height)), definition.Width).Build();
            item.SetShape(footprint);
            // Machine footprints include sprite padding, which must also receive pointer hits.
            if (definition.Kind == ItemKind.Machine)
                ((GameItemElement)item).background.image.alphaHitTestMinimumThreshold = 0f;
            AssignText(item, definition);
            item.unitBaseValue = 0;
            item.unitValue = definition.BaseValue;
            item.mayStackItemFunc = null;
            item.forceDisableActivate = false;
            if (definition.Kind is ItemKind.Food or ItemKind.Medical) item.RemoveAllGameItemType();
            foreach (var type in FoodProcessingRules.ItemTypes(definition)) item.SetGameItemType(type);
            if (definition.ContrabandLevel != 0) ContrabandHelper.InitContrabandItem(item, definition.ContrabandLevel);
            ModHelper.SetSource(item, FoodDefinitions.ModId, FoodText.Get("food.food_processing"));
            item.onLoaded += loaded =>
            {
                // The save decoder restores the old base shape after the factory runs.
                // SetShape revalidates the footprint at the item's saved position and orientation.
                if ((spriteKey is "nutrifruit_soda" or "glowberry_soda" or
                    "coffee_beer" or "dockside_shandy" or "berry_spring_fizz") &&
                    (loaded.shape.width != definition.Width || loaded.shape.height != definition.Height))
                    loaded.SetShape(footprint);
                AssignText(loaded, definition);
                FoodStateValue.RestoreBasis(loaded);
            };
            if (definition.Kind == ItemKind.Guide)
            {
                item.EnableTag("DOCUMENT");
                item.EnableTag("GUIDE_PAPER_TAG");
                item.SetGameItemType("DOCUMENT");
                BookHelper.InitPaperGuide(item);
                item.canActivateSlotItemFunc = (_, _, _) => HealthData.CanRead();
                item.mayActivateSlotItemFunc = (_, _, _) => HealthData.CanRead();
                item.onActivateSlotItemFunc = (_, _, _) => FoodGuide.Open();
            }
            if (FoodDefinitions.IsCareKit(definition.Id)) FoodCareKit.Initialize(item);
            return item;
        }
        catch { if (item.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); throw; }
    }

    private static void AssignText(GameItem item, ItemDefinition definition)
    {
        item.name = FoodText.Name(definition);
        if (definition.Brand.Length != 0) item.EnableManufacturer(FoodBrands.Prefix + definition.Brand);
        item.shortDescription = FoodText.Get(definition.Id + ".description");
        item.flavorText = FoodText.Get(definition.Id + ".flavor");
    }

    internal static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        var definition = FoodDefinitions.Items.FirstOrDefault(value => value.Id == item.identifier);
        if (definition == null) return;
        AssignText(item, definition);
        if (definition.Kind == ItemKind.Machine)
            builder.AddLine(FoodSelection.Label(item) + "\n" + FoodText.Get("food.quality_current", MachineryHelper.GetCurrentQualityBonus(item)) + "\n" + FoodText.Get("food.use_the_machine_recipe_button_to_preview") + "\n" +
                FoodInterop.Restriction + "\n" + FoodText.Get("food.storage_upgrade", FoodStorageExpansion.Level(item), FoodStorageExpansion.MaxLevel));
    }
}
