using NicoItemManager;

internal static class PresetChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // Existing editor checks apply in-memory configurations only. These cover the
        // saved JSON round trip and restoring/re-capturing a draft for repeated use.
        ModHelper.DataFiles.Clear();
        DirectoryMaster.Factories["preset_module"] = () =>
        {
            var item = new GameItemElement { identifier = "preset_module", spriteAtlasPath = "Items/items_module", spritePath = "module" };
            item.ModifyTag("MODULE_TAG", tag => tag.Enable());
            item.ModifyTag("BONUS_PERCENTAGE_PERFORMANCE_INT", tag => tag.Enable().SetInt(10));
            return item;
        };
        RenderHandler.AvailableSprites.UnionWith(new[] { "module", "module_l" });
        var presets = new ItemPresets();
        ItemPreset saved;
        using (var editor = new ItemEditor("preset_module"))
        {
            editor.SetModuleStat("BONUS_PERCENTAGE_PERFORMANCE_INT", 50);
            ModuleEditing.SetEffect(editor.Item, 1, "MODULE_EFFECT_LEARNING_ALGORITHM");
            editor.SetShape(editor.Shapes.FindIndex(shape => shape.Sprite == "module_l"));
            editor.SetTier(5);
            editor.SetValue(12345);
            editor.ApplyTag(new TagEdit { Key = "ZERO", Kind = TagValueKind.Float, Text = "0", IsNew = true });
            editor.ApplyTag(new TagEdit { Key = "PRECISION", Kind = TagValueKind.Double, Text = "0.12345678901234567", IsNew = true });
            saved = presets.Save("异形学习模块", "preset_module", editor.Capture());
        }
        var reloaded = new ItemPresets();
        reloaded.Load();
        check(reloaded.Entries.Count == 1 && reloaded.Entries[0].Name == "异形学习模块" && reloaded.Entries[0].Id == saved.Id,
            "Saving and reloading JSON must preserve the custom item name, ID and configuration.");
        ItemConfiguration recaptured;
        using (var editor = new ItemEditor(saved.ItemId))
        {
            editor.Restore(reloaded.Entries[0].Configuration);
            check(editor.Item.spritePath == "module_l" && editor.Int("MODULE_TIER_INT") == 5
                && editor.Item.unitValue == 12345 && ModuleEditing.Effect(editor.Item, 1) == "MODULE_EFFECT_LEARNING_ALGORITHM"
                && editor.Int("LEARNING_ALGO_MAX_PERFORMANCE_INT") == 100,
                "Restoring a saved draft must retain its irregular shape, effects, learning cap, tier and value.");
            check(editor.ReadTags().Single(tag => tag.Key == "ZERO").Kind == TagValueKind.Float
                && editor.Item.GetTagReadonly("PRECISION").valueDouble == 0.12345678901234567,
                "A saved zero-valued float must retain its type and double values must retain their precision.");
            recaptured = editor.Capture();
        }
        var inventory = new GameGridInventory();
        inventory.Find = item => new() { inventory = inventory, Accept = () => { item.parentInventory = inventory; return item.unitCount; } };
        EmporiumEntry.Instance = new() { invElement = inventory };
        DirectoryMaster.Created.Clear();
        check(ItemSpawner.Spawn(saved.ItemId, 2, configure: recaptured.ApplyTo).Success,
            "A restored and recaptured preset must generate repeatedly through the real spawn implementation.");
        var copies = DirectoryMaster.Created.Where(item => item.parentInventory == inventory).ToList();
        check(copies.Count == 2 && copies.All(item => item.spritePath == "module_l" && item.unitValue == 12345
            && item.GetTagReadonly("LEARNING_ALGO_MAX_PERFORMANCE_INT").valueInt == 100),
            "Re-capturing a restored draft must not drop its shape flag or reroll its learned stats.");
        copies[0].GetTagReadonly("LEARNING_ALGO_MAX_PERFORMANCE_INT").SetInt(1);
        check(copies[1].GetTagReadonly("LEARNING_ALGO_MAX_PERFORMANCE_INT").valueInt == 100,
            "Spawns from saved presets must have independent mutable tags.");
        var second = reloaded.Save("第二套配置", saved.ItemId, recaptured);
        reloaded.Save("重命名配置", saved.ItemId, recaptured, saved.Id);
        check(reloaded.Entries.Count == 2 && reloaded.Entries[0].Name == "重命名配置" && reloaded.Entries[1].Id == second.Id,
            "Multiple presets for one item must coexist; updating a preset must preserve the others.");
        ModHelper.RejectWrite = true;
        bool failed = false;
        try { reloaded.Delete(saved.Id); } catch (IOException) { failed = true; }
        finally { ModHelper.RejectWrite = false; }
        check(failed && reloaded.Entries.Count == 2, "A failed file write must leave the active preset list unchanged.");
        reloaded.Delete(saved.Id);
        var afterDelete = new ItemPresets();
        afterDelete.Load();
        check(afterDelete.Entries.Count == 1 && afterDelete.Entries[0].Id == second.Id,
            "Deleting a custom item must persist and preserve other saved presets.");

        DirectoryMaster.Factories["preset_bottle"] = () =>
        {
            var item = new GameItemElement { identifier = "preset_bottle" };
            item.ModifyTag("LIQUID_CONTAINER_TAG", tag => tag.Enable());
            item.ModifyTag("LIQUID_CONTAINER_CAPACITY", tag => tag.Enable().SetInt(1000000));
            item.ModifyTag("water", tag => tag.Enable().SetInt(500000));
            return item;
        };
        using (var editor = new ItemEditor("preset_bottle"))
        {
            editor.SetCapacity(50);
            editor.EmptyLiquid();
            afterDelete.Save("空瓶", "preset_bottle", editor.Capture());
        }
        var liquidPresets = new ItemPresets();
        liquidPresets.Load();
        using (var editor = new ItemEditor("preset_bottle"))
        {
            editor.Restore(liquidPresets.Entries.Last().Configuration);
            var bottle = DirectoryMaster.Factories["preset_bottle"]();
            editor.Capture().ApplyTo(bottle);
            check(WaterHelper.GetCurrentCapacityUL(bottle) == 0 && WaterHelper.GetTotalCapacityML(bottle) == 50
                && bottle.WaterFeatureInitialized, "An emptied container must stay empty after saving, reloading and recapturing its preset.");
        }
        DirectoryMaster.Factories["preset_gun"] = () =>
        {
            var item = new GameItemElement { identifier = "preset_gun" };
            item.ModifyTag("ADV_GUN_TAG", tag => tag.Enable());
            GunHelper.SetEquipmentCondition(item, ItemFeatureList.EquipmentCondition.Fair);
            return item;
        };
        using (var editor = new ItemEditor("preset_gun"))
        {
            editor.SetGunCondition(ItemFeatureList.EquipmentCondition.New);
            liquidPresets.Save("全新品相", "preset_gun", editor.Capture());
        }
        var propertyPresets = new ItemPresets();
        propertyPresets.Load();
        using (var editor = new ItemEditor("preset_gun"))
        {
            editor.Restore(propertyPresets.Entries.Last().Configuration);
            var copy = DirectoryMaster.Factories["preset_gun"]();
            editor.Capture().ApplyTo(copy);
            check(GunFrameHelper.GetCondition(copy) == ItemFeatureList.EquipmentCondition.New,
                "Gun condition and its edited flag must survive serialization and re-capturing a restored draft.");
        }
        string path = ModHelper.DataFiles.Keys.Single();
        ModHelper.DataFiles[path] = "{\"Version\":99,\"Items\":[]}";
        bool rejected = false;
        try { new ItemPresets().Save("无效写入", saved.ItemId, recaptured); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && ModHelper.DataFiles[path].Contains("99"), "An unsupported saved format must be rejected without replacing the file.");
        RenderHandler.AvailableSprites.Clear();
    }
}
