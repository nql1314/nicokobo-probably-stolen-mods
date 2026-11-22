using NicoItemManager;

internal static class PropertyChecks
{
    static GameItemElement Item(string id, params (string Key, int Value)[] tags)
    {
        var item = new GameItemElement { identifier = id };
        foreach (var tag in tags) item.ModifyTag(tag.Key, state => state.Enable().SetInt(tag.Value));
        return item;
    }

    static GameItemElement Battery() => Item("energy_credit", ("power_source_item", 0),
        ("power_source_item_energy", 60), ("power_source_item_max_energy", 100), ("power_source_item_bandwidth", 10));

    static GameItemElement Durable()
    {
        var item = Item("durable", ("DURABILITY_TAG", 70), ("DURABILITY_FAKE_TAG", 90),
            ("MAX_DURABILITY_TAG", 100), ("DEFAULT_DURABILITY_TAG", 100));
        item.AddItemFeature(new ItemFeature { identifier = "DURABILITY_TAG" });
        DurabilityHelper.SetDurability(item, 70, 90, 100, 100);
        return item;
    }

    static GameItemElement Armor() => Item("armor", ("armor_item", 0), ("current_armor_value", 60),
        ("current_max_armor_value", 100), ("default_armor_value", 100), ("armor_protection", 4), ("armor_mitigation", 30));

    static GameItemElement Gun()
    {
        var item = Item("gun", ("ADV_GUN_TAG", 0), ("GUN_FIREPOWER_TAG", 100), ("GUN_ACCURACY_TAG", 100),
            ("GUN_RELIABILITY_TAG", 100), ("GUN_STYLE_TAG", 100), ("GUN_RECOIL_TAG", 100), ("GUN_ERGONOMICS_TAG", 100));
        GunHelper.SetEquipmentCondition(item, ItemFeatureList.EquipmentCondition.Fair);
        return item;
    }

    static GameItemElement Uses()
    {
        var item = Item("uses", ("USE_COUNT_ITEM_TAG", 0), ("CURRENT_USE_COUNT_INT", 3), ("MAX_USE_COUNT_INT", 5),
            ("USE_COUNT_BASE_VALUE_INT", 40), ("USE_COUNT_VALUE_PER_USE_INT", 10));
        item.ModifyTag("USE_COUNT_AUTO_VALUE_BOOL", tag => tag.Enable().SetBool(true));
        UseCountHelper.SetCurrentUseCount(item, 3);
        return item;
    }

    static GameItemElement Food() => Item("food", ("FRESH_FOOD_TAG", 0), ("FRESHNESS_TAG", 10), ("CALORIE_VALUE_TAG", 500));

    internal static void Run(Action<bool, string> check)
    {
        DirectoryMaster.Created.Clear();
        DirectoryMaster.SpawnCalls = 0;
        DirectoryMaster.Factories = new()
        {
            ["energy_credit"] = Battery, ["durable"] = Durable, ["armor"] = Armor,
            ["gun"] = Gun, ["uses"] = Uses, ["food"] = Food,
            ["canned"] = () => Item("canned", ("CALORIE_VALUE_TAG", 300), ("CANNED_FOOD_TAG", 0))
        };
        void Rejected(Action action, Func<bool> unchanged, string message)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && unchanged(), message);
        }
        using (var draft = new ItemEditor("energy_credit"))
        {
            draft.SetBatteryCapacity(50);
            check(draft.Int("power_source_item_energy") == 50 && draft.Item.BatteryRefreshes == 1,
                "Reducing battery capacity must clamp stored energy and invoke the native sprite refresh.");
            draft.SetBatteryCapacity(80);
            draft.SetBatteryOutput(17);
            draft.SetBatteryEnergy(80);
            draft.SetBatteryEnergy(0);
            var copy = Battery();
            draft.Capture().ApplyTo(copy);
            check(copy.GetTagReadonly("power_source_item_energy").GetInt() == 0
                && copy.GetTagReadonly("power_source_item_max_energy").GetInt() == 80
                && copy.GetTagReadonly("power_source_item_bandwidth").GetInt() == 17
                && copy.BatteryRefreshes == 1 && copy.spritePath == "battery0",
                "A generated empty battery must retain edited capacity and output and refresh its empty appearance.");
            Rejected(() => draft.SetBatteryEnergy(81), () => draft.Int("power_source_item_energy") == 0,
                "Over-capacity energy must be rejected before changing the preview.");
            Rejected(() => draft.SetBatteryCapacity(0), () => draft.Int("power_source_item_max_energy") == 80,
                "A zero battery capacity must not reach native percentage calculations.");
            check(!draft.ReadTags().Any(tag => ItemEditor.BatteryTags.Contains(tag.Key)),
                "Managed battery fields must not also appear as independent raw tags.");
            draft.Item.ModifyTag("EXTRA_COUNT_INT", tag => tag.Enable().SetInt(0));
            draft.Item.ModifyTag("EXTRA_FLOAT", tag => tag.Enable().SetFloat(0));
            draft.Item.ModifyTag("EXTRA_DOUBLE", tag => tag.Enable().SetDouble(1.2345678901234567));
            draft.Item.ModifyTag("DISABLED_FLOAT", tag => tag.SetFloat(2.5f));
            draft.Item.ModifyTag("FLAG", tag => tag.Enable().SetBool(true));
            draft.Item.ModifyTag("LABEL", tag => tag.Enable().SetString("label"));
            var outerFields = draft.ReadNumericTags();
            check(outerFields.Select(field => field.Key).SequenceEqual(new[] { "EXTRA_COUNT_INT", "EXTRA_DOUBLE", "EXTRA_FLOAT" }),
                "The outer numeric fields must show this item's enabled integer, float and double values, without duplicating specialized fields or exposing disabled and nonnumeric tags.");
            var outerFloat = outerFields.Single(field => field.Key == "EXTRA_FLOAT");
            outerFloat.Text = "0.625";
            draft.ApplyTag(outerFloat);
            check(draft.ReadNumericTags().Single(field => field.Key == "EXTRA_FLOAT").Text == "0.625"
                && draft.Item.GetTagReadonly("EXTRA_FLOAT").valueFloat == 0.625f
                && draft.Item.GetTagReadonly("EXTRA_DOUBLE").valueDouble == 1.2345678901234567,
                "An outer numeric edit must keep the existing floating-point type and preserve the item's other values.");
        }
        using (var draft = new ItemEditor("durable"))
        {
            draft.SetRealDurability(20);
            draft.SetApparentDurability(80);
            var feature = draft.Item.FindItemFeatureByID("DURABILITY_TAG");
            check(!feature.isFeatureMatch && feature.publicDisplay == "80/100" && feature.actualDisplay == "20/100",
                "Real and apparent durability must update the native appraisal feature, not just numeric tags.");
            draft.SetValue(999);
            var setup = draft.Capture();
            draft.RepairDurability();
            var copy = Durable();
            setup.ApplyTo(copy);
            var copiedFeature = copy.FindItemFeatureByID("DURABILITY_TAG");
            check(copiedFeature.actualDisplay == "20/100" && copiedFeature.publicDisplay == "80/100"
                && !ReferenceEquals(feature, copiedFeature) && copiedFeature.pricingBase == 999,
                "Generated copies must rebuild independent durability features against the captured price and retain the earlier configuration.");
            draft.SetMaximumDurability(50);
            check(draft.Int("DURABILITY_TAG") == 50 && draft.Int("DURABILITY_FAKE_TAG") == 50
                && draft.Int("MAX_DURABILITY_TAG") == 50 && draft.Int("DEFAULT_DURABILITY_TAG") == 100,
                "Lowering maximum durability must clamp both visible and real values without replacing the factory durability baseline.");
            draft.SetRealDurability(0);
            draft.RepairDurability();
            check(draft.Int("DURABILITY_TAG") == 50 && draft.Item.FindItemFeatureByID("DURABILITY_TAG").isFeatureMatch,
                "Repair must restore real and apparent durability together and synchronize appraisal state.");
            Rejected(() => draft.SetMaximumDurability(0), () => draft.Int("MAX_DURABILITY_TAG") == 50,
                "Zero maximum durability must be rejected before native value calculations.");
        }
        using (var draft = new ItemEditor("armor"))
        {
            draft.SetArmorCurrent(20);
            draft.SetArmorMaximum(10);
            draft.SetArmorProtection(12);
            draft.SetArmorMitigation(75);
            check(draft.Int("current_armor_value") == 10, "Lowering armor capacity must clamp current armor.");
            draft.SetArmorCurrent(0);
            draft.RepairArmor();
            check(draft.Int("current_armor_value") == 10 && draft.Int("current_max_armor_value") == 10,
                "Armor repair must use its current maximum without reducing that maximum.");
            draft.SetArmorCurrent(0);
            var copy = Armor();
            draft.Capture().ApplyTo(copy);
            check(copy.GetTagReadonly("current_armor_value").GetInt() == 0
                && copy.GetTagReadonly("current_max_armor_value").GetInt() == 10
                && copy.GetTagReadonly("armor_protection").GetInt() == 12
                && copy.GetTagReadonly("armor_mitigation").GetInt() == 75,
                "Generated armor must retain an explicitly depleted state and all edited protection parameters.");
            Rejected(() => draft.SetArmorMitigation(101), () => draft.Int("armor_mitigation") == 75,
                "Mitigation percentages outside 0-100 must not change the preview.");
        }
        using (var draft = new ItemEditor("gun"))
        {
            draft.SetGunStat("GUN_FIREPOWER_TAG", 200);
            draft.SetGunStat("GUN_ACCURACY_TAG", 80);
            draft.SetGunStat("GUN_RELIABILITY_TAG", 60);
            draft.SetGunCondition(ItemFeatureList.EquipmentCondition.New);
            check(draft.Int("GUN_CURRENT_FIREPOWER_TAG") == 260 && draft.Int("GUN_CURRENT_ACCURACY_TAG") == 104
                && draft.Int("GUN_CURRENT_RELIABILITY_TAG") == 78,
                "Base stat and condition edits must immediately refresh condition-adjusted native gun statistics.");
            var setup = draft.Capture();
            draft.SetGunCondition(ItemFeatureList.EquipmentCondition.Poor);
            var copy = Gun();
            setup.ApplyTo(copy);
            check(GunFrameHelper.GetCondition(copy) == ItemFeatureList.EquipmentCondition.New
                && copy.GetTagReadonly("GUN_CURRENT_FIREPOWER_TAG").GetInt() == 260
                && copy.GetTagReadonly("GUN_CURRENT_ACCURACY_TAG").GetInt() == 104
                && !ReferenceEquals(copy.FindItemFeatureByCategory("CATEGORY_EQUIPMENT_CONDITION"),
                    draft.Item.FindItemFeatureByCategory("CATEGORY_EQUIPMENT_CONDITION")),
                "Captured gun condition must survive later preview changes and recreate independent features and derived stats on the generated copy.");
            Rejected(() => draft.SetGunStat("GUN_FIREPOWER_TAG", -1), () => draft.Int("GUN_FIREPOWER_TAG") == 200,
                "Invalid gun statistics must be rejected without changing the base value.");
        }
        using (var draft = new ItemEditor("uses"))
        {
            draft.SetMaximumUseCount(2);
            check(draft.Int("CURRENT_USE_COUNT_INT") == 2 && draft.Item.unitValue == 60,
                "Reducing maximum uses must clamp the current count and recalculate native automatic pricing.");
            draft.SetUseCount(0);
            var setup = draft.Capture();
            draft.SetUseCount(draft.Int("MAX_USE_COUNT_INT"));
            check(draft.Int("CURRENT_USE_COUNT_INT") == 2 && draft.Item.unitValue == 60,
                "Refilling must restore maximum uses and native automatic pricing.");
            var copy = Uses();
            setup.ApplyTo(copy);
            check(copy.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt() == 0
                && copy.GetTagReadonly("MAX_USE_COUNT_INT").GetInt() == 2 && copy.unitValue == 40 && !copy.Destroyed,
                "A generated zero-use consumable must retain its captured price and count without being consumed or destroyed by editing.");
            Rejected(() => draft.SetUseCount(3), () => draft.Int("CURRENT_USE_COUNT_INT") == 2,
                "Uses above the maximum must not change the preview.");
        }
        using (var draft = new ItemEditor("food"))
        {
            draft.SetCalories(0);
            draft.SetFreshness(0);
            var setup = draft.Capture();
            draft.SetFreshness(13);
            var copy = Food();
            setup.ApplyTo(copy);
            check(copy.GetTagReadonly("FRESHNESS_TAG").GetInt() == 0 && copy.GetTagReadonly("CALORIE_VALUE_TAG").GetInt() == 0
                && draft.Int("FRESHNESS_TAG") == 13,
                "Generated food must retain explicit rotten and zero-calorie states from the captured configuration.");
            Rejected(() => draft.SetCalories(-1), () => draft.Int("CALORIE_VALUE_TAG") == 0,
                "Negative calories must be rejected before changing food state.");
        }
        using (var draft = new ItemEditor("canned"))
        {
            check(draft.HasCalories && !draft.HasFreshness, "Non-perishable food must expose calories without inventing a freshness mechanic.");
            Rejected(() => draft.SetFreshness(13), () => !draft.Item.state.dict.ContainsKey("FRESHNESS_TAG"),
                "Freshness edits must only apply to items that natively have freshness state.");
            Rejected(() => draft.SetBatteryOutput(5), () => !draft.Item.state.dict.ContainsKey("power_source_item_bandwidth"),
                "Specialized fields must not be applied to unsupported item types.");
        }
        check(DirectoryMaster.SpawnCalls == 0 && DirectoryMaster.Created.All(item => item.Destroyed),
            "All six property editors must remain prototype-only and release their previews on disposal.");

        var converter = new GameItemElement { identifier = "current_converter" };
        var energyCost = ItemTagDescriptions.Find(converter, "CURRENT_CONVERTER_RATE_FLOAT");
        check(energyCost != null && energyCost.ChineseName.Contains("信用点/kJ")
            && energyCost.EnglishName == "Energy cost (credits/kJ)"
            && energyCost.Tooltip("CURRENT_CONVERTER_RATE_FLOAT").Contains("CURRENT_CONVERTER_RATE_FLOAT")
            && energyCost.Tooltip("CURRENT_CONVERTER_RATE_FLOAT").Contains(energyCost.Name),
            "A specific item identifier must resolve both language names and the original tag key without requiring an enabled marker.");
        var gun = Gun();
        var attachment = new GameItemElement { itemTypes = new() { "GUN_MOD" } };
        var gunRecoil = ItemTagDescriptions.Find(gun, "GUN_RECOIL_TAG");
        var attachmentRecoil = ItemTagDescriptions.Find(attachment, "GUN_RECOIL_TAG");
        check(gunRecoil?.EnglishName == "Base recoil control" && attachmentRecoil?.EnglishName == "Recoil control modifier",
            "A shared gun tag must distinguish a firearm's base attribute from an attachment's modifier using item context.");
        check(ItemTagDescriptions.Find(new GameItemElement(), "GUN_RECOIL_TAG") == null,
            "A familiar tag on an unrelated item must remain unidentified instead of borrowing another type's meaning.");
        var animal = Item("rat", ("ANIMAL_ITEM_TAG", 0));
        var partnerGene = ItemTagDescriptions.Find(animal, "ANIMAL_GROWTH_RATE_TAG_PARTNER_ALLELE_A");
        var geneticBonus = ItemTagDescriptions.Find(animal, "ANIMAL_GROWTH_RATE_TAG_BONUS_PERCENT");
        check(partnerGene?.EnglishName.EndsWith("partner allele A") == true
            && geneticBonus?.EnglishHelp.Contains("0.1") == true
            && ItemTagDescriptions.Find(animal, "UNKNOWN_TRAIT_ALLELE_A") == null,
            "Animal gene descriptions must distinguish partner alleles and ratio bonuses while refusing unknown traits.");
        var seed = new GameItemElement { itemTypes = new() { "SEED" } };
        check(ItemTagDescriptions.Find(seed, "PLANT_TRAIT_fast_grower")?.EnglishName == "Fast grower rank"
            && ItemTagDescriptions.Find(seed, "PLANT_TRAIT_unverified_custom") == null,
            "Seed types must resolve verified plant trait ranks without inventing descriptions for unverified custom traits.");
        var roe = Item("tilapia_roe", ("FISH_ROE_TAG", 0), ("FISH_SPECIES_TAG", 0));
        check(ItemTagDescriptions.Find(roe, "FISH_AGE_TAG")?.EnglishName == "Roe development days",
            "Roe-specific context must override the general fish age description.");
    }
}
