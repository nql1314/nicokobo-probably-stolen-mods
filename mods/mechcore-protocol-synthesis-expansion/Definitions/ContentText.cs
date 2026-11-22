using System.Text;
using System.Globalization;
using Nicokobo.ModUtilities;

namespace MechcoreProtocol.SynthesisExpansion.Definitions;

public static class ContentText
{
    private static readonly IReadOnlyDictionary<string, string>
        NativeIngredientNameKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["magnifier"] = "synthesis.magnifier",
            ["system_capped_neural_core"] = "synthesis.neural_core_module_capped",
            ["caffeine_pill"] = "synthesis.caffeine_pill",
            ["recharger"] = "synthesis.recharger",
            ["energy_credit_ext"] = "synthesis.type_ii_battery",
            [SynthesisDefinitions.ModuleMatrixId("continuous_efficiency_module")] = "synthesis.continuous_efficiency_module",
            [SynthesisDefinitions.ModuleMatrixId("continuous_acceleration_module")] = "synthesis.continuous_acceleration_module",
            [SynthesisDefinitions.ModuleMatrixId("continuous_calibration_module")] = "synthesis.continuous_calibration_module",
            ["metal_ingot"] = "synthesis.metal_ingot",
            ["common_ore"] = "synthesis.metal_ore",
            ["scrap_metal"] = "synthesis.scrap_metal",
            ["metal_scanner"] = "synthesis.scav_scanner",
            ["storage_bay"] = "synthesis.storage_bay",
            ["storage_bay_large"] = "synthesis.storage_bay_large",
            ["common_electronic"] = "synthesis.common_electronic_component",
            ["wire"] = "synthesis.wire",
            [SynthesisDefinitions.MouseTrapId] = "synthesis.empty_mouse_trap",
            ["printer_plastic"] = "synthesis.3d_printing_filament",
            ["blood_bag"] = "synthesis.blood_bag",
            ["bandage_item"] = "synthesis.basic_bandage",
            ["soda_red"] = "synthesis.soda",
            ["wine_berry"] = "synthesis.glowberry",
            ["processed_cheese"] = "synthesis.processed_cheese",
            ["cup_noodle"] = "synthesis.instant_ramen",
            ["cat_bar"] = "synthesis.cat_chocolate_bar",
            ["li_eat_snackbar"] = "synthesis.li_eat_snack_bar",
            ["beis_icecream"] = "synthesis.bei_s_vanilla_ice_cream",
            ["coffee_bean"] = "synthesis.coffee_beans",
            ["processed_milk"] = "synthesis.pilk",
            ["nutrifruit"] = "synthesis.nutrifruit",
            ["processed_juice"] = "synthesis.puice",
            ["processed_meat"] = "synthesis.peat",
            ["raw_meat"] = "synthesis.raw_meat_standard",
            ["small_raw_meat"] = "synthesis.raw_meat_small",
            ["water"] = "synthesis.water",
            ["red_beer"] = "synthesis.red_imp_beer",
            ["blue_blood_bag"] = "synthesis.blue_blood_bag",
            ["large_purple_injector"] = "synthesis.immunivax_injector",
            ["empty_beer_bottle"] = "synthesis.empty_beer_bottle",
            ["glass_shard_shiv"] = "synthesis.glass_shiv",
            ["glass_shard"] = "synthesis.glass_shard",
            ["wine_bottle"] = "synthesis.wine_bottle",
            ["neuroactive_perfume"] = "synthesis.sync_neuroactive_perfume",
            ["common_chemical"] = "synthesis.chemical_supplies_common",
            ["nuts_metal"] = "synthesis.metal_screws",
            ["flashlight"] = "synthesis.flashlight",
            ["alarm_module_transmitter"] = "synthesis.alarm_transmitter_module",
            ["cassette1"] = "synthesis.music_cassette"
        };

    private static (string Chinese, string English) Pair(string key) =>
        (ModText.GetFor(MechcorePackage.Id, key, "zh"), ModText.GetFor(MechcorePackage.Id, key, "en"));

    public static (string Chinese, string English) Description(ItemDefinition item) =>
        Pair(item.Id + ".description");

    public static (string Chinese, string English) FlavorText(ItemDefinition item) =>
        Pair(item.Id + ".flavor");

    internal static string Name(string id, bool english)
    {
        if (id == "CONTAINER_TAG")
            return string.Join(" / ", StorageShape.SupportedBayIds.Select(bayId => Name(bayId, english)));
        if (id == SynthesisDefinitions.UniversalManufacturerItem.Id)
            return english ? SynthesisDefinitions.UniversalManufacturerItem.EnglishName
                : SynthesisDefinitions.UniversalManufacturerItem.ChineseName;
        var item = SynthesisDefinitions.Items.FirstOrDefault(candidate => candidate.Id == id);
        if (item != null) return english ? item.EnglishName : item.ChineseName;
        return NativeIngredientNameKeys.TryGetValue(id, out var key)
            ? SynthesisText.GetFor(key, english) : id;
    }

    internal static string LocalizedName(string id)
    {
        if (id == "CONTAINER_TAG")
            return string.Join(" / ", StorageShape.SupportedBayIds.Select(LocalizedName));
        if (id.StartsWith(SynthesisDefinitions.OwnerId + ".", StringComparison.Ordinal) ||
            id.StartsWith(SynthesisDefinitions.ModuleMatrixId(""), StringComparison.Ordinal) ||
            id == SynthesisDefinitions.UniversalManufacturerItem.Id)
        {
            string key = id + ".name";
            string owner = id == SynthesisDefinitions.UniversalManufacturerItem.Id ? "nicokobo.forge" : MechcorePackage.Id;
            string locale = ModText.LocaleCode;
            return NativeIngredientNameKeys.TryGetValue(id, out var alias)
                ? ModText.Find(owner, key, locale, MechcorePackage.Id, alias) ?? id
                : ModText.GetFor(owner, key, locale);
        }
        string fallback = NativeIngredientNameKeys.TryGetValue(id, out var nameKey) ? SynthesisText.Get(nameKey) : id;
        return NativeItemText.Name(id, fallback);
    }

    internal static string LocalizedRequirement(RecipeRequirement rule)
    {
        string name = LocalizedName(rule.ResourceId);
        if (rule.Kind == RecipeResourceKind.Liquid)
            return name + " " + rule.Amount + " ml (≥ " + LocHelper.GetLocalizedMechanic("mech_condition_basewater") +
                " · " + ModText.Get(MechcorePackage.Id, "synthesis.container_retained") + ")";
        var conditions = rule.Conditions.Select(condition => condition.Property switch
        {
            RecipeProperty.MetalPurity => "≥ " + LocHelper.GetLocalizedMechanic((IngotPurityGrade)condition.MinimumValue switch
            {
                IngotPurityGrade.VeryLow => "mech_condition_very_low_purity",
                IngotPurityGrade.Low => "mech_condition_low_purity",
                IngotPurityGrade.Fair => "mech_condition_fair_purity",
                IngotPurityGrade.High => "mech_condition_high_purity",
                IngotPurityGrade.VeryHigh => "mech_condition_very_high_purity",
                IngotPurityGrade.Perfect => "mech_condition_perfect_purity",
                _ => throw new InvalidOperationException("Unsupported ingot purity rule")
            }),
            RecipeProperty.GenuineInjector => LocHelper.GetLocalizedMechanic("mech_condition_genuine"),
            RecipeProperty.UsableGlowberry => ModText.Get(MechcorePackage.Id, "synthesis.usable_berry"),
            RecipeProperty.ScavScannerLevel => ModText.Get(MechcorePackage.Id, "synthesis.inherit_level"),
            RecipeProperty.EmptyStorage => ModText.Get(MechcorePackage.Id, "synthesis.empty_storage"),
            _ => ""
        }).Where(condition => condition.Length != 0).ToArray();
        return name + (conditions.Length == 0 ? "" : " (" + string.Join(", ", conditions) + ")") + " ×" + rule.Amount;
    }

    internal static string RequirementText(RecipeRequirement rule, bool english)
    {
        if (rule.Kind == RecipeResourceKind.Liquid)
            return Name(rule.ResourceId, english) + " " + rule.Amount + " ml" +
                (SynthesisText.GetFor("synthesis.recipe_legacy.basic_water_or_better_container_retained", english));
        var purity = rule.Conditions.FirstOrDefault(condition =>
            condition.Property == RecipeProperty.MetalPurity);
        var suffix = purity == null ? "" : english
            ? " (" + IngotPurityText((IngotPurityGrade)purity.MinimumValue,
                true) + ")"
            : "（" + IngotPurityText(
                (IngotPurityGrade)purity.MinimumValue, false) + "）";
        if (rule.Conditions.Any(condition => condition.Property == RecipeProperty.GenuineInjector))
            suffix += SynthesisText.GetFor("synthesis.recipe_legacy.genuine", english);
        if (rule.Conditions.Any(condition => condition.Property == RecipeProperty.UsableGlowberry))
            suffix += SynthesisText.GetFor("synthesis.recipe_legacy.bad_berries_unusable", english);
        if (rule.Conditions.Any(condition => condition.Property == RecipeProperty.ScavScannerLevel))
            suffix += SynthesisText.GetFor("synthesis.recipe_legacy.level_inherited", english);
        if (rule.Conditions.Any(condition => condition.Property == RecipeProperty.EmptyStorage))
        {
            string storage = SynthesisText.GetFor("synthesis.empty_storage", english);
            suffix += english ? " (" + storage + ")" : "（" + storage + "）";
        }
        return Name(rule.ResourceId, english) + suffix +
            (english ? " x" : "×") + rule.Amount;
    }

    private static string IngotPurityText(IngotPurityGrade grade,
        bool english) => grade switch
    {
        IngotPurityGrade.VeryLow => SynthesisText.GetFor("synthesis.recipe_legacy.very_low_purity_or_better", english),
        IngotPurityGrade.Low => SynthesisText.GetFor("synthesis.recipe_legacy.low_purity_or_better", english),
        IngotPurityGrade.Fair => SynthesisText.GetFor("synthesis.recipe_legacy.fair_purity_or_better", english),
        IngotPurityGrade.High => SynthesisText.GetFor("synthesis.recipe_legacy.high_purity_or_better", english),
        IngotPurityGrade.VeryHigh => SynthesisText.GetFor("synthesis.recipe_legacy.very_high_purity_or_better", english),
        IngotPurityGrade.Perfect => SynthesisText.GetFor("synthesis.recipe_legacy.perfect_purity", english),
        _ => throw new InvalidOperationException("Unsupported ingot purity rule")
    };
}
