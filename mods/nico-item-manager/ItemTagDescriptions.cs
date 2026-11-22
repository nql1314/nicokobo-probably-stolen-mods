using System;
using System.Collections.Generic;

namespace NicoItemManager
{
    internal sealed class ItemTagDescription
    {
        private readonly string textKey;
        internal Func<bool?, string> NameProvider;
        internal readonly TagValueKind? ValueKind;
        internal readonly int MinimumInt, MaximumInt;

        internal ItemTagDescription(string key,
            TagValueKind? valueKind = null, int minimumInt = int.MinValue, int maximumInt = int.MaxValue)
        {
            textKey = key;
            NameProvider = english => Text.GetFor(textKey, english);
            ValueKind = valueKind;
            MinimumInt = minimumInt;
            MaximumInt = maximumInt;
        }

        internal string Name => NameProvider(null);
        internal string ChineseName => NameProvider(false);
        internal string EnglishName => NameProvider(true);
        internal string EnglishHelp => Text.GetFor(textKey + ".help", true);
        internal string Tooltip(string key) => Text.Get("tag.tooltip", Name, Text.Get(textKey + ".help"), key);
    }

    internal static class ItemTagDescriptions
    {
        // Display metadata established on 2026-10-30 for Build 26284217. Current
        // Build 26317870 compilation/checks do not certify every tag in-game.
        // Each group follows the corresponding
        // native helper's tag protocol; matching a familiar key alone is insufficient.
        // HusbandryHelper.InitAnimalItem/CreateItemTooltip and GeneticHelper.GetCurrentStat.
        static readonly Dictionary<string, ItemTagDescription> Animals = new(StringComparer.Ordinal)
        {
            ["ANIMAL_ITEM_TAG"] = new("tag.farm_animal"),
            ["ANIMAL_SPECIES_TAG"] = new("tag.animal_species"),
            ["ANIMAL_SEXE_TAG"] = new("tag.animal_sex"),
            ["ANIMAL_AGE_TAG"] = new("tag.age_days"),
            ["ANIMAL_WEIGHT_TAG"] = new("tag.body_weight_g"),
            ["ANIMAL_HUNGER_TAG"] = new("tag.satiety"),
            ["ANIMAL_MAX_HUNGER_TAG"] = new("tag.maximum_satiety"),
            ["ANIMAL_GROWTH_RATE_TAG"] = new("tag.growth_rate"),
            ["ANIMAL_HUNGER_RATE_TAG"] = new("tag.hunger_rate"),
            ["ANIMAL_HEALTH_TAG"] = new("tag.current_health"),
            ["ANIMAL_MAX_HEALTH_TAG"] = new("tag.maximum_health"),
            ["ANIMAL_LONGEVITY_TAG"] = new("tag.longevity_days"),
            ["ANIMAL_LITTER_SIZE_TAG"] = new("tag.expected_litter_size"),
            ["ANIMAL_LITTER_SIZE_VARIANCE_TAG"] = new("tag.litter_size_variance"),
            ["ANIMAL_FERTILITY_TAG"] = new("tag.base_fertility"),
            ["ANIMAL_CURRENT_FERTILITY_TAG"] = new("tag.current_fertility"),
            ["ANIMAL_GESTATION_PERIOD_TAG"] = new("tag.gestation_period_days"),
            ["ANIMAL_PREGNANT_PROGRESS_TAG"] = new("tag.pregnancy_progress_days"),
            ["ANIMAL_MAX_MATING_COUNT_TAG"] = new("tag.mating_limit_per_cycle"),
            ["ANIMAL_CURRENT_MATING_COUNT_TAG"] = new("tag.matings_this_cycle"),
            ["ANIMAL_IMMUNITY_TAG"] = new("tag.immunity"),
            ["ANIMAL_PREGNANT_TAG"] = new("tag.pregnant"),
            ["ANIMAL_IS_DEAD_TAG"] = new("tag.dead_animal"),
            ["ANIMAL_IS_STARVING_TAG"] = new("tag.starving"),
            ["ANIMAL_DISEASED_TAG"] = new("tag.diseased"),
            ["ANIMAL_NEUTERED_TAG"] = new("tag.neutered")
        };

        // AquacultureHelper: age is days; diet shares are 0..1, not 0..100.
        static readonly Dictionary<string, ItemTagDescription> Fish = new(StringComparer.Ordinal)
        {
            ["FISH_SPECIES_TAG"] = new("tag.fish_species"),
            ["FISH_AGE_TAG"] = new("tag.fish_age_days"),
            ["FISH_HUNGER_TAG"] = new("tag.food_reserve_kcal"),
            ["FISH_HEALTH_TAG"] = new("tag.fish_health"),
            ["FISH_SEX_TAG"] = new("tag.fish_sex"),
            ["FISH_GROUP_COUNT_TAG"] = new("tag.roe_or_fry_count"),
            ["FISH_GROWTH_PROGRESS_TAG"] = new("tag.growth_day_progress"),
            ["FISH_KCAL_EATEN_TAG"] = new("tag.total_food_absorbed_kcal"),
            ["FISH_KCAL_GROWN_TAG"] = new("tag.growth_accounted_energy_kcal"),
            ["FISH_KCAL_PELLET_TAG"] = new("tag.pellet_energy_absorbed_kcal"),
            ["FISH_KCAL_INDUSTRIAL_TAG"] = new("tag.industrial_feed_absorbed_kcal"),
            ["FISH_RECENT_KCAL_TAG"] = new("tag.recent_food_absorbed_kcal"),
            ["FISH_RECENT_INDUSTRIAL_TAG"] = new("tag.recent_industrial_feed_kcal"),
            ["FISH_RECENT_MEAT_TAG"] = new("tag.recent_meat_food_kcal"),
            ["FISH_INDUSTRIAL_DIET_TAG"] = new("tag.recent_industrial_feed_share"),
            ["FISH_MEAT_DIET_TAG"] = new("tag.recent_meat_share"),
            ["FISH_EGG_GRAMS_TAG"] = new("tag.egg_reserve_g"),
            ["FISH_SURGERY_JARS_TAG"] = new("tag.egg_surgery_jar_tally"),
            ["FISH_CARCASS_KCAL_TAG"] = new("tag.carcass_energy_remaining_kcal"),
            ["FISH_CARCASS_MAX_KCAL_TAG"] = new("tag.initial_carcass_energy_kcal"),
            ["FISH_FERTILIZED_TAG"] = new("tag.fertilized_roe"),
            ["FISH_IS_DEAD_TAG"] = new("tag.dead_fish")
        };

        // HydroponicHelper.StampBaseStats/GetRequiredProgress and PlantGeneticsHelper.
        static readonly Dictionary<string, ItemTagDescription> Plants = new(StringComparer.Ordinal)
        {
            ["PLANT_ID_STRING"] = new("tag.plant_identifier"),
            ["REQUIRED_PROGRESS"] = new("tag.base_growth_requirement"),
            ["HARVEST_QUANTITY"] = new("tag.base_harvest_quantity"),
            ["HARVEST_ITEM_ID"] = new("tag.harvest_item_identifier"),
            ["REQUIRED_NUTRIMENT_LEVEL_TAG"] = new("tag.preferred_nutrient_level"),
            ["REQUIRED_NUTRIMENT_RANGE_TAG"] = new("tag.nutrient_tolerance_range"),
            ["NUTRIMENT_USE_RATE"] = new("tag.base_nutrient_consumption"),
            ["WATER_USE_RATE"] = new("tag.base_water_consumption"),
            ["CONTAMINANT_EFFECT_MULT"] = new("tag.contamination_effect_multiplier"),
            ["GROWING_PROGRESS"] = new("tag.current_growth_progress"),
            ["PLANT_HEALTH_INT"] = new("tag.plant_health"),
            ["PERENNIAL_ESTABLISHED_INT"] = new("tag.perennial_established"),
            ["GENETIC_HEAT_INT"] = new("tag.genetic_operation_heat")
        };

        static readonly Dictionary<string, string> PlantTraits = new(StringComparer.Ordinal)
        {
            ["edible"] = "tag.plant.edible", ["fast_grower"] = "tag.plant.fast_grower",
            ["slow_grower"] = "tag.plant.slow_grower", ["bountiful"] = "tag.plant.bountiful",
            ["sparse"] = "tag.plant.sparse", ["delicious"] = "tag.plant.delicious",
            ["unappetizing"] = "tag.plant.unappetizing", ["drought_resistant"] = "tag.plant.drought_resistant",
            ["thirsty"] = "tag.plant.thirsty", ["hardy"] = "tag.plant.hardy",
            ["frail"] = "tag.plant.frail", ["nutritious"] = "tag.plant.nutritious",
            ["nutrient_poor"] = "tag.plant.nutrient_poor", ["fertile"] = "tag.plant.fertile",
            ["infertile"] = "tag.plant.infertile", ["simple_genome"] = "tag.plant.simple_genome",
            ["complex_genome"] = "tag.plant.complex_genome", ["perennial"] = "tag.plant.perennial",
            ["purifying"] = "tag.plant.purifying", ["polluting"] = "tag.plant.polluting",
            ["dense"] = "tag.plant.dense", ["potent"] = "tag.plant.potent", ["weak"] = "tag.plant.weak"
        };

        // GunHelper and GunModHelper share keys but store gun attributes versus attachment modifiers.
        static readonly Dictionary<string, ItemTagDescription> Guns = new(StringComparer.Ordinal)
        {
            ["GUN_STYLE_TAG"] = new("tag.base_style"),
            ["GUN_RECOIL_TAG"] = new("tag.base_recoil_control"),
            ["GUN_ERGONOMICS_TAG"] = new("tag.base_ergonomics"),
            ["GUN_REPAIRBILITY_TAG"] = new("tag.base_repairability"),
            ["GUN_CURRENT_REPAIRBILITY_TAG"] = new("tag.effective_repairability"),
            ["GUN_CALIBER_TAG"] = new("tag.caliber_identifier"),
            ["SERVICE_GUN_PARTS_USED_INT"] = new("tag.repair_parts_used"),
            ["SERVICE_GUN_REPAIR_VALUE_INT"] = new("tag.recorded_repair_value")
        };

        static readonly Dictionary<string, ItemTagDescription> GunMods = new(StringComparer.Ordinal)
        {
            ["GUN_FIREPOWER_TAG"] = new("tag.firepower_modifier"),
            ["GUN_STYLE_TAG"] = new("tag.style_modifier"),
            ["GUN_RELIABILITY_TAG"] = new("tag.reliability_modifier"),
            ["GUN_RECOIL_TAG"] = new("tag.recoil_control_modifier"),
            ["GUN_ERGONOMICS_TAG"] = new("tag.ergonomics_modifier"),
            ["GUN_ACCURACY_TAG"] = new("tag.accuracy_modifier"),
            ["MOD_BASE_FIREPOWER_TAG"] = new("tag.base_attachment_firepower"),
            ["MOD_BASE_STYLE_TAG"] = new("tag.base_attachment_style"),
            ["MOD_BASE_RELIABILITY_TAG"] = new("tag.base_attachment_reliability"),
            ["MOD_BASE_RECOIL_TAG"] = new("tag.base_attachment_recoil_control"),
            ["MOD_BASE_ERGONOMICS_TAG"] = new("tag.base_attachment_ergonomics"),
            ["MOD_BASE_ACCURACY_TAG"] = new("tag.base_attachment_accuracy")
        };

        // MachineryHelper, MachineProgressHelper and PowerHelper; native power/energy units are kW/kJ.
        static readonly Dictionary<string, ItemTagDescription> Machines = new(StringComparer.Ordinal)
        {
            ["machinery_drawn_power"] = new("tag.machine_power_draw_kw"),
            ["MACHINE_STATE_TAG"] = new("tag.processing_state"),
            ["MACHINE_PROGRESS_CURRENT_TAG"] = new("tag.current_processing_progress"),
            ["MACHINE_PROGRESS_TARGET_TAG"] = new("tag.target_processing_progress"),
            ["BASE_PROCESSING_SPEED_TAG"] = new("tag.base_processing_speed"),
            ["CURRENT_PROCESSING_SPEED_TAG"] = new("tag.current_processing_speed"),
            ["CHEMICAL_MACHINE_UPGRADE_COUNT"] = new("tag.chemical_machine_upgrades")
        };

        static readonly Dictionary<string, ItemTagDescription> WorkItems = new(StringComparer.Ordinal)
        {
            ["PROGRESS_ITEM_TARGET_TAG"] = new("tag.required_processing_work"),
            ["PROGRESS_ITEM_TARGET_ITEM_ID_TAG"] = new("tag.processing_output_identifier"),
            ["PROGRESS_ITEM_TARGET_ITEM_COUNT_TAG"] = new("tag.processing_output_count"),
            ["REQUIRED_ITEM_TYPE_COUNT_TAG"] = new("tag.additional_ingredient_types")
        };

        static readonly Dictionary<string, ItemTagDescription> Power = new(StringComparer.Ordinal)
        {
            ["power_source_item_recharge"] = new("tag.automatic_recharge"),
            ["POWER_SOURCE_ITEM_VALUE_PERCENT_INT"] = new("tag.power_source_value"),
            ["POWER_SOURCE_ITEM_DISPOSABLE_TAG"] = new("tag.disposable_power_source")
        };

        static readonly Dictionary<string, ItemTagDescription> Uses = new(StringComparer.Ordinal)
        {
            ["USE_COUNT_BASE_VALUE_INT"] = new("tag.uses_base_value"),
            ["USE_COUNT_VALUE_PER_USE_INT"] = new("tag.value_per_remaining_use"),
            ["USE_COUNT_AUTO_VALUE_BOOL"] = new("tag.value_by_remaining_uses"),
            ["USE_COUNT_REFILL_DAYS_INT"] = new("tag.refill_interval_days"),
            ["USE_COUNT_REFILL_PROGRESS_INT"] = new("tag.refill_progress_days")
        };

        static readonly Dictionary<string, ItemTagDescription> Aging = new(StringComparer.Ordinal)
        {
            ["AGE_INT"] = new("tag.aging_days"),
            ["AGE_VALUE_MOD_PER_DAY_INT"] = new("tag.aging_value_per_day")
        };

        // ChemicalProductHelper.GradeProduct stores percent, whereas genetic bonuses store ratios.
        static readonly Dictionary<string, ItemTagDescription> Chemicals = new(StringComparer.Ordinal)
        {
            ["CHEMICAL_PRODUCT_PURITY_TAG"] = new("tag.chemical_product_purity"),
            ["ACTIVATE_INGREDIENT_TAG"] = new("tag.active_ingredient_identifier")
        };

        static readonly Dictionary<string, ItemTagDescription> Containers = new(StringComparer.Ordinal)
        {
            ["CONTAINER_TEMP_TAG"] = new("tag.container_temperature_c"),
            ["CHEMICAL_INTENT_TAG"] = new("tag.chemical_intent"),
            ["LIQUID_CONTAINER_BASE_PRICE_TAG"] = new("tag.liquid_container_base_price"),
            ["HEATING_TAG"] = new("tag.heating")
        };

        static readonly Dictionary<string, ItemTagDescription> Converter = new(StringComparer.Ordinal)
        {
            ["CURRENT_CONVERTER_RATE_FLOAT"] = new("tag.energy_cost_credits_kj"),
            ["CURRENT_CONVERTER_UPGRADE_COUNT_INT"] = new("tag.converter_upgrade_count"),
            ["CURRENT_CONVERTER_ON_BOOL"] = new("tag.converter_switched_on")
        };

        static readonly Dictionary<string, ItemTagDescription> Fridge = new(StringComparer.Ordinal)
        {
            ["FRIDGE_STORAGE_UPGRADE_COUNT_TAG"] = new("tag.fridge_storage_upgrades"),
            ["FRIDGE_POWER_UPGRADE_COUNT_TAG"] = new("tag.fridge_power_upgrades")
        };

        // InsStampHelper/StampUI use zero-based appearance indices, not serial numbers.
        // Redemption reads CATEGORY_GENUINE_STAMP.realCondition independently of these tags.
        static readonly Dictionary<string, ItemTagDescription> FoodStamps = new(StringComparer.Ordinal)
        {
            ["FOOD_STAMP_TAG"] = new("tag.food_stamp"),
            ["INSPECTABLE_TAG"] = new("tag.visually_inspectable"),
            ["STAMP_AMOUNT"] = new("tag.food_stamp_amount", TagValueKind.Int),
            ["STAMP_SEAL"] = new("tag.seal_appearance_index", TagValueKind.Int, -1, 1),
            ["STAMP_LOGO"] = new("tag.logo_appearance_index", TagValueKind.Int, -1, 5),
            ["STAMP_CHIP"] = new("tag.chip_appearance_index", TagValueKind.Int, -1, 3),
            ["STAMP_LETTERING"] = new("tag.lettering_appearance_index", TagValueKind.Int, -1, 8),
            ["STAMP_SEAL_ISSUE"] = new("tag.seal_issue"),
            ["STAMP_LOGO_ISSUE"] = new("tag.logo_issue"),
            ["STAMP_CHIP_ISSUE"] = new("tag.chip_issue"),
            ["STAMP_LETTERING_ISSUE"] = new("tag.lettering_issue"),
            ["STAMP_AMOUNT_ISSUE"] = new("tag.amount_issue")
        };

        static readonly Dictionary<string, Dictionary<string, ItemTagDescription>> Items = new(StringComparer.Ordinal)
        {
            ["current_converter"] = Converter,
            ["fridge"] = Fridge,
            ["food_stamp"] = FoodStamps
        };

        static readonly (string Marker, string Type, Dictionary<string, ItemTagDescription> Entries)[] Groups =
        {
            ("ANIMAL_ITEM_TAG", null, Animals), ("FISH_SPECIES_TAG", null, Fish),
            ("SEED_ITEM_TAG", "SEED", Plants), (null, "MODIFIED_SEED", Plants),
            ("ADV_GUN_TAG", null, Guns), ("WEAPON_MOD_TAG", "GUN_MOD", GunMods),
            ("PROGRESS_TYPE_MACHINE_TAG", "MACHINE", Machines), ("machinery_drawn_power", null, Machines),
            ("PROGRESS_ITEM_TAG", null, WorkItems), ("power_source_item", "ENERGY_CELL", Power),
            ("USE_COUNT_ITEM_TAG", null, Uses), ("AGE_VALUE_MOD_PER_DAY_INT", null, Aging),
            ("CHEMICAL_PRODUCT_TAG", null, Chemicals), ("LIQUID_CONTAINER_TAG", null, Containers),
            ("CONTAINER_TEMP_TAG", null, Containers), ("CURRENT_CONVERTER_TAG", null, Converter),
            ("FRIDGE_TAG", null, Fridge), ("FOOD_STAMP_TAG", null, FoodStamps)
        };

        static readonly ItemTagDescription RoeAge = new("tag.roe_development_days");

        static readonly string[] GeneticTraits = { "ANIMAL_GROWTH_RATE_TAG", "ANIMAL_HUNGER_RATE_TAG",
            "ANIMAL_FERTILITY_TAG", "ANIMAL_LITTER_SIZE_TAG", "ANIMAL_IMMUNITY_TAG",
            "ANIMAL_MAX_HEALTH_TAG", "ANIMAL_LONGEVITY_TAG" };

        // Longer partner suffixes must be checked before their allele suffixes.
        static readonly (string Suffix, string Key)[] GeneParts =
        {
            ("_PARTNER_ALLELE_A", "tag.gene.partner_allele_a"),
            ("_PARTNER_ALLELE_B", "tag.gene.partner_allele_b"),
            ("_ALLELE_A", "tag.gene.allele_a"),
            ("_ALLELE_B", "tag.gene.allele_b"),
            ("_BONUS_PERCENT", "tag.gene.bonus_percent"),
            ("_BONUS", "tag.gene.bonus")
        };

        internal static ItemTagDescription Find(GameItem item, string key)
        {
            if (Items.TryGetValue(item.identifier, out var specific) && specific.TryGetValue(key, out var description))
                return description;
            if (key == "FISH_AGE_TAG" && item.IsTag("FISH_ROE_TAG")) return RoeAge;
            foreach (var group in Groups)
                if ((group.Marker != null && item.IsTag(group.Marker) || group.Type != null && item.IsGameItemType(group.Type))
                    && group.Entries.TryGetValue(key, out description)) return description;
            if (item.IsTag("ANIMAL_ITEM_TAG"))
                foreach (var part in GeneParts)
                    if (key.EndsWith(part.Suffix, StringComparison.Ordinal))
                    {
                        string trait = key.Substring(0, key.Length - part.Suffix.Length);
                        if (Array.IndexOf(GeneticTraits, trait) >= 0)
                        {
                            var basis = Animals[trait];
                            return new ItemTagDescription(part.Key)
                            {
                                NameProvider = english => Text.GetFor("tag.gene_label", english,
                                    basis.NameProvider(english), Text.GetFor(part.Key, english))
                            };
                        }
                    }
            if ((item.IsTag("SEED_ITEM_TAG") || item.IsGameItemType("SEED") || item.IsGameItemType("MODIFIED_SEED"))
                && key.StartsWith("PLANT_TRAIT_", StringComparison.Ordinal)
                && PlantTraits.TryGetValue(key.Substring("PLANT_TRAIT_".Length), out var plantTrait))
                return new ItemTagDescription("tag.plant_rank")
                {
                    NameProvider = english => Text.GetFor("tag.plant_rank", english, Text.GetFor(plantTrait, english))
                };
            return null;
        }
    }
}
