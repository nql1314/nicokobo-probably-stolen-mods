using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class SynthesisQuality
{
    private const string NativePricingTag = "nicokobo.mechcore.synthesis.native_quality_pricing";
    internal static void Initialize(GameItem item)
    {
        if (SynthesisDefinitions.CarriesPurity(item.identifier))
        {
            IngotPurityHelper.InitIngotItem(item);
            RefreshPurityDisplay(item);
        }
        if (SynthesisDefinitions.CarriesComponentGrade(item.identifier)) SetGrade(item, ComponentGrade.Ordinary);
    }

    internal static void Prepare(GameItem machine, RecipeDefinition recipe, GameItem product,
        IReadOnlyList<(GameItem Item, int Count)> inputs)
    {
        int quality = MachineryHelper.GetCurrentQualityBonus(machine);
        bool smelts = RefinerMetalRules.IsSmelting(recipe.OutputRule);
        if (machine.identifier == SynthesisDefinitions.RefinerItem.Id &&
            (smelts || product.identifier == SynthesisDefinitions.Silicon.Id || product.identifier == SynthesisDefinitions.TitaniumAlloy.Id))
        {
            string baseline = smelts ? RefinerMetalRules.BaselineTag(inputs.Where(take => take.Item.identifier == "common_ore").Sum(take => take.Count))
                : product.identifier == SynthesisDefinitions.Silicon.Id ? RefinerPurityRules.SiliconBaselineTag
                : IngotPurityHelper.GetPurity(inputs.First(take => take.Item.identifier == "metal_ingot").Item);
            if (IngotPurityRules.FromNativeTag(baseline) == IngotPurityGrade.Unknown)
                throw new InvalidOperationException("Refiner input purity unavailable");
            int levels = smelts ? RefinerMetalRules.PurityLevels(baseline, quality, recipe.OutputRule == OutputRule.BlastMetalSmelting)
                : RefinerPurityRules.PurityLevels(baseline, quality);
            string final = IngotPurityRules.ToNativeTag((IngotPurityGrade)((int)IngotPurityRules.FromNativeTag(baseline) + levels));
            SetPurity(product, final, smelts ? null : baseline);
        }
        if (SynthesisDefinitions.CarriesMachinedPurity(product.identifier))
        {
            var purities = inputs.Where(take => SynthesisDefinitions.CarriesPurity(take.Item.identifier))
                .Select(take => IngotPurityHelper.GetPurity(take.Item)).ToArray();
            SetPurity(product, IngotPurityRules.MachinedTag(purities, quality), IngotPurityRules.MachinedTag(purities, 0));
        }
        if (SynthesisDefinitions.CarriesComponentGrade(product.identifier))
            SetGrade(product, ComponentQualityRules.FromQuality(quality));
    }

    private static void SetPurity(GameItem item, string purity, string? baseline)
    {
        if (baseline != null)
        {
            SynthesisProduction.RestoreBasis(item);
            SynthesisProduction.SetBasis(item, IngotPurityRules.BaseValue(item.unitValue, baseline));
        }
        ApplyPurity(item, purity);
        if (baseline != null) item.ModifyTag(NativePricingTag, tag => tag.Enable().SetInt(1));
    }

    private static void ApplyPurity(GameItem item, string purity)
    {
        if (item.FindItemFeatureByID("INGOT_PURITY_TAG") == null) IngotPurityHelper.InitIngotItem(item);
        IngotPurityHelper.SetPurity(item, purity);
        var feature = item.FindItemFeatureByID("INGOT_PURITY_TAG")
            ?? throw new InvalidOperationException("Product purity feature missing");
        int modifier = feature.GetActualValueModifier();
        feature.SetValueModifier(modifier); feature.SetPreExposedValueMod(modifier, true);
        RefreshPurityDisplay(item);
        if (IngotPurityHelper.GetPurity(item) != purity) throw new InvalidOperationException("Output purity readback mismatch");
    }

    private static void RefreshPurityDisplay(GameItem item)
    {
        if (!SynthesisDefinitions.CarriesMachinedPurity(item.identifier)) return;
        var feature = item.FindItemFeatureByID("INGOT_PURITY_TAG");
        if (feature == null) return;
        string purity = IngotPurityHelper.GetPurity(item);
        string label = SynthesisText.Get("synthesis.grade") + IngotPurityRules.MachinedGradeName(purity);
        int actual = feature.GetActualValueModifier(), shown = feature.GetFakeValueModifier();
        feature.useCondition = false;
        feature.SetValueModifier(actual); feature.SetPreExposedValueMod(shown, true);
        feature.SetPublicDisplay(label); feature.SetActualDisplay(label);
    }

    private static void SetGrade(GameItem item, ComponentGrade grade)
    {
        item.ModifyTag(ComponentQualityRules.SchemaTag, tag => tag.Enable().SetInt(ComponentQualityRules.SchemaVersion));
        item.ModifyTag(ComponentQualityRules.GradeTag, tag => tag.Enable().SetInt((int)grade));
        var feature = item.FindItemFeatureByID(ComponentQualityRules.GradeTag);
        if (feature == null)
        {
            feature = new ItemFeature(ItemFeature.FeatureType.Normale)
            {
                identifier = ComponentQualityRules.GradeTag, category = ComponentQualityRules.GradeTag,
                valueStage = ItemFeature.ValueStage.Innate, isExposable = true, isPublicHidden = false, isFeatureMatch = true
            };
            item.AddItemFeature(feature);
        }
        int modifier = ComponentQualityRules.ValueBonusPercent(grade);
        string display = SynthesisText.Get("synthesis.grade") + ComponentQualityRules.Name(grade);
        feature.useCondition = false;
        feature.SetPublicDisplay(display); feature.SetActualDisplay(display);
        feature.SetValueModifier(modifier); feature.SetPreExposedValueMod(modifier, true);
        item.ModifyTag(NativePricingTag, tag => tag.Enable().SetInt(1));
    }

    internal static void Restore(GameItem item)
    {
        SynthesisProduction.RestoreBasis(item);
        if (SynthesisDefinitions.CarriesComponentGrade(item.identifier) && item.IsTag(ComponentQualityRules.SchemaTag))
        {
            int grade = item.GetTagReadonly(ComponentQualityRules.GradeTag).valueInt;
            if (!ComponentQualityRules.IsValid(grade)) throw new InvalidOperationException("Invalid saved component grade");
            SetGrade(item, (ComponentGrade)grade);
        }
        if (SynthesisDefinitions.CarriesPurity(item.identifier))
        {
            string purity = IngotPurityHelper.GetPurity(item);
            if (IngotPurityRules.FromNativeTag(purity) == IngotPurityGrade.Unknown)
                throw new InvalidOperationException("Invalid saved material purity");
            ApplyPurity(item, purity);
        }
    }
}
