using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class SynthesisItems
{
    internal static GameItem Create(ItemDefinition definition)
    {
        if (!ProductionCatalog.OwnsItem(definition.Id)) throw new InvalidOperationException("Content not migrated: " + definition.Id);
        var item = definition.Kind switch
        {
            ItemKind.Machine => SynthesisMachines.Create(SynthesisDefinitions.Machine(definition.Id)),
            ItemKind.Robot => SynthesisRobots.Create(),
            ItemKind.Storage => CyberneticStorage.Create(),
            ItemKind.Guide => ItemDirectory.CreateEmptyItem(definition.Id),
            _ => DirectoryMaster.Create<GameItem>(definition.NativeTemplateId)
        };
        if (item == null || item.unitCount != 1) throw new InvalidOperationException("Synthesis template unavailable: " + definition.Id);
        try
        {
            if (definition.Kind is not (ItemKind.Machine or ItemKind.Robot or ItemKind.Storage)) ClearActions(item);
            item.identifier = definition.Id;
            item.SetSpriteAndShapeFromMod(MechcorePackage.Id, ProductionCatalog.SpriteKey(definition));
            item.SetShape(new GridShapeBuilder().SetData(new string('1', checked(definition.Width * definition.Height)), definition.Width).Build());
            AssignText(item, definition);
            item.unitBaseValue = 0;
            item.unitValue = definition.BaseValue;
            item.mayStackItemFunc = null;
            item.forceDisableActivate = false;
            if (definition.Kind == ItemKind.Material)
            {
                // Use the occupied rectangle for mouse hits, including transparent pixels.
                if (item is GameItemElement element)
                    element.background.image.alphaHitTestMinimumThreshold = 0f;
                item.SetGameItemType("MATERIAL");
                SynthesisQuality.Initialize(item);
            }
            else if (definition.Kind == ItemKind.Cybernetic)
            {
                item.SetGameItemType("CYBERNETIC");
                if (definition.Id == SynthesisDefinitions.CyberEye.Id)
                {
                    item.DisableTag("AUG_SCANNER_TAG"); item.DisableTag("NOT_FOR_RESALE");
                    item.forceDisableUse = false;
                    var slot = AugScanner.GetBatterySlot(item);
                    if (slot == null) throw new InvalidOperationException("Cyber eye battery slot missing");
                    slot.mayInventoryAddItemFunc = (candidate, _) => candidate.unitCount == 1 && GeneralHelper.IsItemOwned(candidate) &&
                        candidate.IsTag("power_source_item");
                }
            }
            else if (definition.Kind is ItemKind.Household or ItemKind.Luxury)
            {
                item.RemoveAllGameItemType(); item.SetGameItemType(definition.Kind == ItemKind.Household ? "HOUSEHOLD_GOOD" : "LUXURY_ITEM");
            }
            if (definition.ContrabandLevel != 0) ContrabandHelper.InitContrabandItem(item, definition.ContrabandLevel);
            if (definition.Id == SynthesisDefinitions.MiningDrill.Id)
                item.ModifyTag(Domain.MiningDrillRules.UpgradedTag, tag => tag.SetEnabled(false));
            ModHelper.SetSource(item, MechcorePackage.Id, SynthesisText.Get("package.name"));
            item.onLoaded += SynthesisQuality.Restore;
            if (definition.Kind == ItemKind.Guide)
            {
                item.EnableTag("DOCUMENT");
                item.EnableTag("GUIDE_PAPER_TAG");
                item.SetGameItemType("DOCUMENT");
                BookHelper.InitPaperGuide(item);
                item.canActivateSlotItemFunc = (_, _, _) => HealthData.CanRead();
                item.mayActivateSlotItemFunc = (_, _, _) => HealthData.CanRead();
                item.onActivateSlotItemFunc = (_, _, _) => SynthesisGuide.Open(definition.Id);
            }
            return item;
        }
        catch { if (item.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); throw; }
    }

    private static void AssignText(GameItem item, ItemDefinition definition)
    {
        item.name = SynthesisText.Get(definition.Id + ".name");
        item.shortDescription = SynthesisText.Get(definition.Id + ".description");
        item.flavorText = SynthesisText.Get(definition.Id + ".flavor");
    }

    internal static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        var definition = ProductionCatalog.Items.FirstOrDefault(value => value.Id == item.identifier);
        if (definition != null) AssignText(item, definition);
    }

    private static void ClearActions(GameItem item)
    {
        item.mayActivateSlotItemFunc = null; item.canActivateSlotItemFunc = null; item.onActivateSlotItemFunc = null;
        item.mayToggleSlotItemFunc = null; item.canToggleSlotItemFunc = null; item.onToggleSlotItemFunc = null;
        item.mayItemTargetItemFunc = null; item.canItemTargetItemFunc = null; item.onItemTargetItemFunc = null;
        item.mayThisTargetItemFunc = null; item.canThisTargetItemFunc = null; item.onThisTargetItemFunc = null;
        item.mayItemTargetThisFunc = null; item.canItemTargetThisFunc = null; item.onItemTargetThisFunc = null;
        item.maySlotItemTargetItemFunc = null; item.canSlotItemTargetItemFunc = null; item.onSlotItemTargetItemFunc = null;
        item.mayItemTargetSlotItemFunc = null; item.canItemTargetSlotItemFunc = null; item.onItemTargetSlotItemFunc = null;
        item.onCycleEndEarlySlotItemFunc = null; item.onCycleEndSlotItemFunc = null; item.onCycleEndLateSlotItemFunc = null;
        item.onTurnEndSlotItemFunc = null; item.onOneSecondItemFunc = null; item.onLoaded = null;
        item.activateDefault = false;
    }
}
