using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion;

internal sealed record CyberEyeScan(long Value, int Cost, string Fingerprint, IReadOnlyList<string> RealDisplays)
{
    internal static bool IsSupportedItemId(string id) => id is "plasma_fuel" or "hand" or "ribwich" or
        "packet_red_cigarette" or "food_stamp" or "large_purple_injector" or "wine_berry";

    internal static CyberEyeScan? Capture(GameItem item)
    {
        // Story rewards with the same native ID remain eligible; tags cannot admit other IDs.
        if (!IsSupportedItemId(item.identifier))
            return null;
        if (item.IsTag("APPRAISAL_ITEM"))
        {
            // Native appraisal only implements these three tag families.
            if (!item.IsTag("PLASMA_CELL_TAG") && !item.IsTag("RIBWICH_TAG") && !item.IsTag("HAND_TAG"))
                return null;
            int correct = AppraisalHelper.GetCorrectBaseValue(item);
            return correct < 0 ? null : new(correct, SynthesisNumbers.Effects.EyeScanBatteryCost,
                $"{item.uniqueId}:appraisal:{correct}", Array.Empty<string>());
        }
        if (!item.IsTag("INSPECTABLE_TAG")) return null;

        long basis = SynthesisProduction.ReadBasis(item, item.unitValue);
        long innate = 0;
        double final = 1;
        var states = new List<string>();
        var displays = new List<string>();
        int nativeConditions = 0;
        if (item.itemFeatures != null)
        {
            foreach (var feature in item.itemFeatures)
            {
                if (feature == null || feature.isDisabled || feature.featureType == ItemFeature.FeatureType.Event)
                    continue;
                var stage = feature.GetValueStage();
                if (stage == ItemFeature.ValueStage.Market) continue;
                var real = feature.realCondition;
                if (feature.useCondition && real == null) return null;
                if (feature.useCondition && real != null)
                {
                    switch (real.category)
                    {
                        case "CATEGORY_GENUINE_CIGARETTE":
                            basis = real.identifier switch
                            {
                                "genuineCigarette" => SynthesisNumbers.NativeInspectionValues.GenuineCigarette,
                                "counterfeitCigarette" => SynthesisNumbers.NativeInspectionValues.CounterfeitCigarette,
                                _ => -1
                            };
                            nativeConditions++;
                            break;
                        case "CATEGORY_GENUINE_STAMP":
                            var amount = item.GetTagReadonly("STAMP_AMOUNT");
                            basis = real.identifier switch
                            {
                                "counterfeitStamp" => SynthesisNumbers.NativeInspectionValues.CounterfeitStamp,
                                "genuineStamp" when amount != null && amount.valueInt > 0 =>
                                    (long)(amount.valueInt * SynthesisNumbers.NativeInspectionValues.GenuineStampValueFactor),
                                _ => -1
                            };
                            nativeConditions++;
                            break;
                        case "CATEGORY_GENUINE_INJECTOR":
                            basis = real.identifier switch
                            {
                                "genuineInjector" => SynthesisNumbers.NativeInspectionValues.GenuineImmunivax,
                                "expiredInjector" => SynthesisNumbers.NativeInspectionValues.ExpiredImmunivax,
                                "counterfeitInjector" => SynthesisNumbers.NativeInspectionValues.CounterfeitImmunivax,
                                "unusableInjector" => SynthesisNumbers.NativeInspectionValues.UnusableImmunivax,
                                _ => -1
                            };
                            nativeConditions++;
                            break;
                        default:
                            // An unrecognized native value-replacing action cannot be priced by its public label.
                            if (real.isTransformative) return null;
                            break;
                    }
                    if (basis < 0 || nativeConditions > 1) return null;
                }
                int modifier = feature.GetActualValueModifier();
                if (stage == ItemFeature.ValueStage.Innate) innate = checked(innate + modifier);
                else if (stage == ItemFeature.ValueStage.Final) final *= Math.Max(0d, 1d + modifier / 100d);
                states.Add($"{feature.identifier}:{real?.identifier}:{stage}:{modifier}");
                if (feature.isExposable)
                {
                    string display = feature.GetBaseRealDisplay();
                    if (!string.IsNullOrWhiteSpace(display)) displays.Add(display);
                }
            }
        }
        if (nativeConditions == 0 && (item.IsTag("CIGARETTE_TAG") || item.IsTag("FOOD_STAMP_TAG") ||
            item.IsTag("INS_INJECTOR_TAG"))) return null;
        if (item.IsTag("BERRY_TAG"))
        {
            var condition = item.GetTagReadonly("BERRY_CONDITION_INT");
            if (condition == null) return null;
            string display = condition.valueInt switch
            {
                -2 => SynthesisText.Get("synthesis.moldy_fruit"),
                -1 => SynthesisText.Get("synthesis.unripe_dried_fruit"),
                0 or 1 or 2 => SynthesisText.Get("synthesis.normal_fruit"),
                3 => SynthesisText.Get("synthesis.small_fruit"),
                _ => ""
            };
            if (display.Length == 0) return null;
            displays.Add(display);
            states.Add($"berry:{condition.valueInt}:{item.GetTagReadonly("BERRY_VISUAL_VARIANT_INT")?.valueInt}");
        }
        long value = Math.Max(0, SynthesisProduction.ComposeRealValue(basis, innate, final));
        // State IDs, rather than label text, keep relabeling and language changes free.
        states.Sort(StringComparer.Ordinal);
        return new(value, SynthesisNumbers.Effects.EyeInspectionBatteryCost,
            $"{item.uniqueId}:inspection:{value}:{string.Join(";", states)}", displays.Distinct().ToArray());
    }
}
