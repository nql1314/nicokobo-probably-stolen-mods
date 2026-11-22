using NicoItemManager;

internal static class ExistingItemChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var inventory = new GameGridInventory();
        EmporiumEntry.Instance = new() { invElement = inventory, backInvinvElement = new GameGridInventory() };
        DirectoryMaster.Factories["existing"] = () => new GameItemElement { identifier = "existing" };

        GameItemElement Item(int quantity = 1)
        {
            var item = new GameItemElement { identifier = "existing", uniqueId = 123, unitCount = quantity, parentInventory = inventory };
            inventory.childItems.Add(item);
            item.ModifyTag("EXTRA_INT", tag => tag.Enable().SetInt(17));
            return item;
        }
        bool Rejected(Action action)
        {
            try { action(); return false; }
            catch (InvalidOperationException) { return true; }
        }

        var original = Item(8);
        var opaqueFeature = new ItemFeature { identifier = "opaque", parentItemUniqueId = 123, valueModifier = 37, isFeatureExposed = true,
            realCondition = new() { identifier = "real" }, fakeCondition = new() { identifier = "fake" } };
        original.AddItemFeature(opaqueFeature);
        using (var draft = new ItemEditor(original))
        {
            check(draft.IsExisting && !ReferenceEquals(draft.Item, original) && draft.Int("EXTRA_INT") == 17 && draft.Item.unitCount == 8,
                "Existing editing must start from the selected instance's current values and quantity.");
            var feature = draft.Item.FindItemFeatureByID("opaque");
            feature.realCondition.identifier = "draft";
            check(opaqueFeature.realCondition.identifier == "real" && feature.parentItemUniqueId == 0
                && feature.valueModifier == 37 && feature.isFeatureExposed,
                "A draft must own independent feature/condition records without borrowing the real item's UID.");
            var tag = draft.ReadNumericTags().Single(value => value.Key == "EXTRA_INT");
            tag.Text = "42";
            draft.ApplyTag(tag);
            draft.SetValue(99);
            check(original.GetTagReadonly("EXTRA_INT").GetInt() == 17 && original.unitValue == 10,
                "Editing a draft must not change the real item before Apply.");
        }
        check(!original.Destroyed && original.unitCount == 8 && original.unitValue == 10 && original.parentInventory == inventory,
            "Cancelling must release only the preview and preserve the real stack and its placement.");

        using (var draft = new ItemEditor(original))
        {
            var tag = draft.ReadNumericTags().Single(value => value.Key == "EXTRA_INT");
            tag.Text = "42";
            draft.ApplyTag(tag);
            original.ModifyTag("RUNTIME_VALUE", state => state.Enable().SetInt(73));
            original.unitValue = 18;
            draft.ApplyExisting();
            check(original.GetTagReadonly("EXTRA_INT").GetInt() == 42 && original.GetTagReadonly("RUNTIME_VALUE").GetInt() == 73
                && original.unitValue == 18 && original.uniqueId == 123 && original.unitCount == 8
                && original.itemFeatures.Single() == opaqueFeature && original.parentInventory == inventory,
                "Apply must update only edited fields on the original instance, retaining live values, identity and opaque features.");
            check(Rejected(() => draft.SetShape(0)) && Rejected(() => draft.ApplyTag(new TagEdit { Key = "IS_OWNED_TAG" })),
                "Existing-item editing must reject shape and ownership changes.");
        }

        var battery = Item();
        foreach (var tag in new[] { ("power_source_item", 0), ("power_source_item_energy", 60), ("power_source_item_max_energy", 100), ("power_source_item_bandwidth", 10) })
            battery.ModifyTag(tag.Item1, state => state.Enable().SetInt(tag.Item2));
        using (var draft = new ItemEditor(battery))
        {
            draft.SetBatteryCapacity(90);
            battery.ModifyTag("power_source_item_energy", state => state.SetInt(20));
            draft.ApplyExisting();
            check(battery.GetTagReadonly("power_source_item_max_energy").GetInt() == 90 && battery.GetTagReadonly("power_source_item_energy").GetInt() == 20,
                "Editing battery capacity must preserve energy consumed while the draft is open.");
        }

        var uses = Item();
        foreach (var tag in new[] { ("USE_COUNT_ITEM_TAG", 0), ("CURRENT_USE_COUNT_INT", 3), ("MAX_USE_COUNT_INT", 5), ("USE_COUNT_BASE_VALUE_INT", 40), ("USE_COUNT_VALUE_PER_USE_INT", 10) })
            uses.ModifyTag(tag.Item1, state => state.Enable().SetInt(tag.Item2));
        uses.ModifyTag("USE_COUNT_AUTO_VALUE_BOOL", state => state.Enable().SetBool(true));
        using (var draft = new ItemEditor(uses))
        {
            draft.SetMaximumUseCount(9);
            uses.ModifyTag("CURRENT_USE_COUNT_INT", state => state.SetInt(1));
            draft.ApplyExisting();
            check(uses.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt() == 1 && uses.unitValue == 50,
                "Editing a use limit must retain live remaining uses and native repricing instead of restoring draft values.");
        }

        var armor = Item();
        armor.ModifyTag("armor_item", tag => tag.Enable());
        armor.ModifyTag("current_armor_value", tag => tag.Enable().SetInt(20));
        armor.ModifyTag("current_max_armor_value", tag => tag.Enable().SetInt(100));
        using (var draft = new ItemEditor(armor))
        {
            draft.SetArmorMaximum(60);
            armor.ModifyTag("current_armor_value", tag => tag.SetInt(90));
            draft.ApplyExisting();
            check(armor.GetTagReadonly("current_armor_value").GetInt() == 60,
                "A reduced armor limit must clamp a live armor value that changed while editing.");
        }

        var liquid = Item();
        liquid.ModifyTag("LIQUID_CONTAINER_TAG", tag => tag.Enable());
        liquid.ModifyTag("LIQUID_CONTAINER_CAPACITY", tag => tag.Enable().SetInt(100000));
        liquid.ModifyTag("water", tag => tag.Enable().SetInt(10000));
        using (var draft = new ItemEditor(liquid))
        {
            draft.SetCapacity(60);
            liquid.ModifyTag("water", tag => tag.SetInt(90000));
            draft.ApplyExisting();
            check(WaterHelper.GetCurrentCapacityUL(liquid) == 60000 && liquid.WaterFeatureInitialized,
                "A reduced liquid capacity must trim live contents and refresh native water features.");
        }

        var gun = Item();
        gun.ModifyTag("ADV_GUN_TAG", tag => tag.Enable());
        GunHelper.SetEquipmentCondition(gun, ItemFeatureList.EquipmentCondition.Poor);
        using (var draft = new ItemEditor(gun))
        {
            check(GunFrameHelper.GetCondition(draft.Item) == ItemFeatureList.EquipmentCondition.Poor,
                "Gun editing must display the existing condition instead of the factory condition.");
            draft.SetGunCondition(ItemFeatureList.EquipmentCondition.Excellent);
            check(GunFrameHelper.GetCondition(gun) == ItemFeatureList.EquipmentCondition.Poor,
                "A gun condition change must remain isolated until applied.");
            draft.ApplyExisting();
            check(GunFrameHelper.GetCondition(gun) == ItemFeatureList.EquipmentCondition.Excellent,
                "Applying condition must use the native feature update on the original gun.");
        }

        var container = Item();
        var contents = new GameInventory();
        var child = new GameItemElement { parentInventory = contents };
        contents.childItems.Add(child);
        var window = new PixelWindow { Child = contents };
        container.SetContentWindow(window);
        container.NestedItems.Add(child);
        using (var draft = new ItemEditor(container))
        {
            draft.SetValue(60);
            draft.ApplyExisting();
        }
        check(container.contentWindow == window && !child.Destroyed && !window.handler.gameObject.Destroyed && container.unitValue == 60,
            "Editing and disposing a container draft must preserve its real contents and window.");
        check(Rejected(() => ExistingItems.Delete(container, 1)) && !container.Destroyed && !child.Destroyed,
            "Deleting a nonempty container must be refused before touching it or its contents.");

        var stack = Item(8);
        int quantityCallbacks = 0;
        stack.onChangeAmountItemFunc = _ => quantityCallbacks++;
        stack.onSlotChangeAmountItemFunc = (_, _, _) => quantityCallbacks++;
        inventory.onSlotChangeAmountItemFunc = (_, _, _) => quantityCallbacks++;
        ExistingItems.Delete(stack, 3);
        check(stack.unitCount == 5 && stack.uniqueId == 123 && stack.parentInventory == inventory && !stack.Destroyed && quantityCallbacks == 3,
            "Partial deletion must keep the stack's identity and invoke the native amount-change boundaries.");
        check(Rejected(() => ExistingItems.Delete(stack, 0)) && Rejected(() => ExistingItems.Delete(stack, 6)) && stack.unitCount == 5,
            "Invalid deletion quantities must not change the real stack.");
        stack.MaxRemovable = 1;
        check(Rejected(() => ExistingItems.Delete(stack, 2)) && stack.unitCount == 5,
            "Deletion must respect the native maximum removable quantity.");
        stack.MaxRemovable = int.MaxValue;
        inventory.RejectExpel = true;
        check(Rejected(() => ExistingItems.Delete(stack, 5)) && !stack.Destroyed && stack.parentInventory == inventory,
            "A rejected native expulsion must not destroy the item.");
        inventory.RejectExpel = false;

        var emptyWindow = new PixelWindow();
        stack.SetContentWindow(emptyWindow);
        ExistingItems.Delete(stack, 5);
        check(stack.Destroyed && stack.parentInventory == null && !inventory.childItems.Contains(stack)
            && emptyWindow.handler.gameObject.Destroyed && !GraphHandler.windowGraphNodeRoots.Contains(emptyWindow),
            "Whole-stack deletion must detach the real item and clean up its empty native window.");

        var inaccessible = Item(4);
        using (var draft = new ItemEditor(inaccessible))
        {
            draft.SetValue(80);
            inaccessible.Owned = false;
            check(Rejected(draft.ApplyExisting) && Rejected(() => ExistingItems.Delete(inaccessible, 1))
                && Rejected(() => ExistingItems.Copy(inaccessible, 1)) && inaccessible.unitValue == 10 && inaccessible.unitCount == 4,
                "Apply, Copy and Delete must recheck ownership at the mutation boundary.");
            inaccessible.Owned = true;
            inaccessible.parentInventory = new GameInventory();
            check(Rejected(draft.ApplyExisting) && Rejected(() => ExistingItems.Delete(inaccessible, 1))
                && Rejected(() => ExistingItems.Copy(inaccessible, 1)),
                "Items moved out of the backpack or desktop must be refused.");
            inaccessible.parentInventory = inventory;
            inaccessible.Locked = true;
            check(Rejected(draft.ApplyExisting) && Rejected(() => ExistingItems.Delete(inaccessible, 1))
                && Rejected(() => ExistingItems.Copy(inaccessible, 1)),
                "Locked items must not be edited or deleted.");
        }

        var source = Item(6);
        source.name = "Customized container";
        source.unitValue = 91;
        source.unitBaseValue = 47;
        source.lateUnitValue = 13;
        source.backupUnitValue = 23;
        source.customText = "Handwritten note";
        source.shortDescription = "Custom description";
        source.SetSprite("mod.atlas", "custom_shape");
        source.useSpriteFromMod = true;
        source.SetContentWindow(new PixelWindow());
        source.SetExamineWindow(new PixelWindow());
        var sourceFeature = new ItemFeature { identifier = "appraisal", parentItemUniqueId = source.uniqueId,
            valueModifier = 37, isFeatureExposed = true, realCondition = new() { identifier = "real" },
            fakeCondition = new() { identifier = "fake" } };
        source.AddItemFeature(sourceFeature);
        int nextIdentity = 900;
        DirectoryMaster.Factories["existing"] = () =>
        {
            var item = new GameItemElement { identifier = "existing", uniqueId = ++nextIdentity };
            item.SetContentWindow(new PixelWindow());
            item.SetExamineWindow(new PixelWindow());
            return item;
        };
        EmporiumEntry.Instance.backInvinvElement.Find = _ => throw new Exception("Backpack copy must not query the desktop.");
        inventory.Find = item => new() { inventory = inventory, numTransfer = item.unitCount,
            Accept = () => { inventory.childItems.Add(item); item.parentInventory = inventory; return item.unitCount; } };
        var result = ExistingItems.Copy(source, 2, SpawnDestination.Backpack);
        var copy = DirectoryMaster.Created.Last();
        check(result.Success && copy.parentInventory == inventory && copy.unitCount == 2 && source.unitCount == 6
            && copy.uniqueId != source.uniqueId && !source.Destroyed,
            "Copying a chosen quantity must deliver a new instance without consuming the selected stack.");
        check(copy.name == source.name && copy.unitValue == 91 && copy.unitBaseValue == 47
            && copy.lateUnitValue == 13 && copy.backupUnitValue == 23 && copy.customText == source.customText
            && copy.shortDescription == source.shortDescription && copy.spriteAtlasPath == "mod.atlas"
            && copy.spritePath == "custom_shape" && copy.useSpriteFromMod && !ReferenceEquals(copy.shape, source.shape),
            "A copy must retain the real item's customized values, text and appearance.");
        var copiedFeature = copy.itemFeatures.Single();
        check(copiedFeature.parentItemUniqueId == copy.uniqueId && !ReferenceEquals(copiedFeature, sourceFeature)
            && copiedFeature.valueModifier == 37 && copiedFeature.isFeatureExposed
            && !ReferenceEquals(copy.contentWindow, source.contentWindow)
            && !ReferenceEquals(copy.examineWindow, source.examineWindow),
            "Copies must retain hidden appraisal data, rebind feature ownership and keep their factory's independent windows.");
        copy.ModifyTag("EXTRA_INT", tag => tag.SetInt(99));
        copiedFeature.realCondition.identifier = "changed";
        copiedFeature.fakeCondition.identifier = "changed";
        copy.itemTypes.Add("COPY_ONLY");
        check(source.GetTagReadonly("EXTRA_INT").GetInt() == 17 && sourceFeature.realCondition.identifier == "real"
            && sourceFeature.fakeCondition.identifier == "fake" && !source.itemTypes.Contains("COPY_ONLY"),
            "Mutating a copy must not modify the original's tags, appraisal conditions or types.");

        int createdBefore = DirectoryMaster.Created.Count;
        check(Rejected(() => ExistingItems.Copy(source, 0)) && Rejected(() => ExistingItems.Copy(source, 7))
            && DirectoryMaster.Created.Count == createdBefore && source.unitCount == 6,
            "Invalid copy quantities must be rejected before creating any item.");
        source.NestedItems.Add(new GameItemElement());
        check(Rejected(() => ExistingItems.Copy(source, 1)) && DirectoryMaster.Created.Count == createdBefore,
            "Nonempty containers must be rejected without producing a partial copy or touching their contents.");
        source.NestedItems.Clear();
        inventory.Find = _ => null;
        result = ExistingItems.Copy(source, 1, SpawnDestination.Backpack);
        var failedCopy = DirectoryMaster.Created.Last();
        check(!result.Success && failedCopy.Destroyed && failedCopy.contentWindow == null && failedCopy.examineWindow == null
            && source.unitCount == 6 && !source.Destroyed && !source.contentWindow.handler.gameObject.Destroyed
            && !source.examineWindow.handler.gameObject.Destroyed,
            "A full destination must release only the undelivered copy and its windows, preserving the original.");

        inventory.Find = item => new() { inventory = inventory, targetItem = source, numTransfer = item.unitCount,
            Accept = () => { int units = item.unitCount; source.unitCount += units; item.unitCount = 0; return units; } };
        result = ExistingItems.Copy(source, source.unitCount, SpawnDestination.Backpack);
        check(result.Success && source.unitCount == 12 && DirectoryMaster.Created.Last().Destroyed
            && !source.Destroyed && source.itemFeatures.Single() == sourceFeature,
            "Copying a full stack through native stacking must add exactly that quantity and release the empty copy.");
    }
}
