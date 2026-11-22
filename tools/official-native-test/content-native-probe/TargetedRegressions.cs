using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private IEnumerable<object?> TargetedRegressions()
    {
        R.phase = "regression-logistics-missing-tags";
        try { MissingTagAndInjectorRegression(); }
        catch (Exception error) { R.Check("regression.logistics.real-material-classification", "Native missing-tag and real-condition regression executes", null, false, "Actual private production classifiers", error.ToString()); }
        yield return null;
        R.phase = "regression-aug-fixed-random-effect";
        try { FixedEffectCandidateRegression(); }
        catch (Exception error) { R.Check("regression.fixed-abilities", "Forge private fixed abilities and native mixed tooltip execute", null, false, "Actual loaded Nicokobo.Forge ownership; no removed candidate-filter API", error.ToString()); }
        for (int sample = 0; sample < 8; sample++)
        {
            GameItem? node = null;
            try
            {
                node = Spawn("node_small");
                ModuleEffectHelper.InitRandomEffect(node, 1);
                string[] effects = new[] { "MODULE_EFFECT1_TAG", "MODULE_EFFECT2_TAG" }.Where(node.IsTag)
                    .Select(tag => node.GetTagReadonly(tag).GetString()).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
                R.Check("regression.aug.native-node-random-effect.sample-" + sample,
                    new { item = "node_small", node = true, effects = "At least one registered effect; fixed Aug Overclock excluded" },
                    new { item = node.identifier, node = node.IsGameItemType("NODE"), effects },
                    node.identifier == "node_small" && node.IsGameItemType("NODE") && effects.Length > 0 &&
                        effects.All(id => id != "nicokobo.aug.effect.overclock" && ModuleEffectHelper.moduleEffects.ContainsKey(id)),
                    "Actual native node_small factory -> ModuleEffectHelper.InitRandomEffect; eight smoke samples, no probability-distribution claim");
            }
            catch (Exception error) { R.Check("regression.aug.native-node-random-effect.sample-" + sample, "Real native random-effect operation", null, false, "No candidate-pool replacement or RNG patching", error.ToString()); }
            finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(node!); }
            yield return null;
        }
        bool isolated = !ModuleEffectHelper.moduleEffects.ContainsKey("nicokobo.aug.effect.overclock") &&
            (bool)Reflect.Call(Reflect.Type("Nicokobo.ContentInterop.FixedModuleAbilities"), "Contains", "nicokobo.aug.effect.overclock")!;
        R.Check("regression.aug.fixed-effect-registry-after-native-samples", "Retained in Forge private registry and absent from native random pool", isolated, isolated);
        R.untested.Add("Aug fixed-effect regression: eight actual InitRandomEffect calls are integration smoke coverage; probability distributions and all natural spawns are not measured");
    }

    private void MissingTagAndInjectorRegression()
    {
        const string gradeTag = "nicokobo.mechcore.synthesis.component_grade", scannerTag = "SCAV_SCANNER_DOUBLE_CHANCE";
        var runtimeType = Reflect.Type("Nicokobo.AutomatedLogistics.Integration.AutomatedLogisticsRuntime");
        GameItem? ordinary = null, genuine = null, concealedCounterfeit = null;
        try
        {
            ordinary = Spawn("wire");
            bool gradePresent = ordinary.IsTag(gradeTag), scannerPresent = ordinary.IsTag(scannerTag);
            bool gradeGetterNonNull = ordinary.GetTagReadonly(gradeTag) != null, scannerGetterNonNull = ordinary.GetTagReadonly(scannerTag) != null;
            int grade = (int)Reflect.Call(runtimeType, "ComponentGrade", ordinary)!;
            string classification = (string)Reflect.Call(runtimeType, "ConsumedMaterialState", ordinary)!;
            R.Check("regression.logistics.native-missing-tag-is-not-presence",
                new { gradePresent = false, scannerPresent = false, missingGradeGetterNonNull = true, missingScannerGetterNonNull = true, componentGrade = 0, materialState = "" },
                new { item = ordinary.identifier, gradePresent, scannerPresent, missingGradeGetterNonNull = gradeGetterNonNull,
                    missingScannerGetterNonNull = scannerGetterNonNull, componentGrade = grade, materialState = classification },
                !gradePresent && !scannerPresent && gradeGetterNonNull && scannerGetterNonNull && grade == 0 && classification == "" &&
                    !ordinary.IsTag(gradeTag) && !ordinary.IsTag(scannerTag),
                "Native ordinary GameItem, real GetTagReadonly behavior, then actual private ComponentGrade/ConsumedMaterialState; no copied classifier");
            genuine = PreBuiltItemHelper.CreateRealGenuineInjector();
            concealedCounterfeit = PreBuiltItemHelper.CreateInjector(ItemFeatureList.InjectorConditionType.Genuine, ItemFeatureList.InjectorConditionType.Counterfeit);
            string genuineState = (string)Reflect.Call(runtimeType, "ConsumedMaterialState", genuine)!;
            string counterfeitState = (string)Reflect.Call(runtimeType, "ConsumedMaterialState", concealedCounterfeit)!;
            string[] Conditions(GameItem item) => item.itemFeatures.Where(f => f != null && f.useCondition &&
                    f.realCondition?.category == "CATEGORY_GENUINE_INJECTOR").Select(f => f.realCondition.identifier).ToArray();
            string[] realGenuine = Conditions(genuine), realCounterfeit = Conditions(concealedCounterfeit);
            R.Check("regression.logistics.native-injector-real-condition-classification",
                new { genuinePrefix = "injector:", counterfeitPrefix = "injector:", genuineCondition = "genuineInjector", counterfeitCondition = "counterfeitInjector", classificationsDiffer = true, scannerTagPresent = false },
                new { genuineState, counterfeitState, realGenuine, realCounterfeit, genuineScannerPresent = genuine.IsTag(scannerTag),
                    counterfeitScannerPresent = concealedCounterfeit.IsTag(scannerTag), bothAbsentGettersNonNull = genuine.GetTagReadonly(scannerTag) != null && concealedCounterfeit.GetTagReadonly(scannerTag) != null },
                genuine.identifier == "large_purple_injector" && concealedCounterfeit.identifier == "large_purple_injector" &&
                    !genuine.IsTag(scannerTag) && !concealedCounterfeit.IsTag(scannerTag) &&
                    realGenuine.SequenceEqual(new[] { "genuineInjector" }) && realCounterfeit.SequenceEqual(new[] { "counterfeitInjector" }) &&
                    genuineState.StartsWith("injector:", StringComparison.Ordinal) && counterfeitState.StartsWith("injector:", StringComparison.Ordinal) &&
                    genuineState.Contains("genuineInjector") && counterfeitState.Contains("counterfeitInjector") && genuineState != counterfeitState,
                "Native genuine and genuine-labelled/real-counterfeit constructors -> actual private material classifier; checks semantic branch and distinct real condition, not a reimplemented encoding algorithm");
        }
        finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(ordinary!); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(genuine!); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(concealedCounterfeit!); }
    }

    private void FixedEffectCandidateRegression()
    {
        const string fixedId = "nicokobo.aug.effect.overclock";
        var fixedApi = Reflect.Type("Nicokobo.ContentInterop.FixedModuleAbilities");
        string[] registryBefore = ModuleEffectHelper.moduleEffects.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string nativeCanary = registryBefore.First(id => !id.StartsWith("nicokobo.", StringComparison.Ordinal));
        var pairs = new[] {
            (Item: "nicokobo.aug.brain_computer_interface_module", Effect: fixedId),
            (Item: "nicokobo.mechcore_protocol.module_matrix.performance_conversion_node", Effect: "nicokobo.mechcore_protocol.module_matrix.effect.convert_performance"),
            (Item: "nicokobo.mechcore_protocol.module_matrix.efficiency_conversion_node", Effect: "nicokobo.mechcore_protocol.module_matrix.effect.convert_efficiency"),
            (Item: "nicokobo.mechcore_protocol.module_matrix.quality_conversion_node", Effect: "nicokobo.mechcore_protocol.module_matrix.effect.convert_quality"),
            (Item: "nicokobo.mechcore_protocol.module_matrix.isolation_node", Effect: "nicokobo.mechcore_protocol.module_matrix.effect.isolation"),
            (Item: "nicokobo.mechcore_protocol.module_matrix.learning_acceleration_node", Effect: "nicokobo.mechcore_protocol.module_matrix.effect.learning_acceleration") };
        foreach (var pair in pairs)
        {
            GameItem? item = null;
            try
            {
                item = Spawn(pair.Item);
                bool registered = (bool)Reflect.Call(fixedApi, "Contains", pair.Effect)!;
                string[] tags = new[] { "MODULE_EFFECT1_TAG", "MODULE_EFFECT2_TAG" }.Where(item.IsTag).Select(t => item.GetTagReadonly(t).GetString()).ToArray();
                var builder = new RichTextBuilder(); ModuleEffectHelper.CreateModuleEffectTooltip(builder, item);
                string text = builder.Build().nakedText;
                R.Check("regression.fixed-abilities." + pair.Item, "Correct saved ability, Forge registration, no native random entry, rendered tooltip",
                    new { pair.Item, pair.Effect, item.uniqueId, tags, registered, native = ModuleEffectHelper.moduleEffects.ContainsKey(pair.Effect), text },
                    tags.Contains(pair.Effect) && registered && !ModuleEffectHelper.moduleEffects.ContainsKey(pair.Effect) && !string.IsNullOrWhiteSpace(text));
                if (pair.Effect == fixedId)
                {
                    ModuleEffectHelper.AddEffectToModule(item, nativeCanary);
                    var mixed = new RichTextBuilder(); ModuleEffectHelper.CreateModuleEffectTooltip(mixed, item);
                    string mixedText = mixed.Build().nakedText;
                    string nativeName = ModuleEffectHelper.moduleEffects[nativeCanary].displayName;
                    R.Check("regression.fixed-abilities.mixed-native-tooltip", new { fixedText = text, nativeName }, mixedText,
                        mixedText.Contains(text) && !string.IsNullOrWhiteSpace(nativeName) && mixedText.Contains(nativeName),
                        "Real native tooltip entry with a private fixed ability and a native random ability on the same native item");
                }
            }
            finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item!); }
        }
        string[] registryAfter = ModuleEffectHelper.moduleEffects.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        R.Check("regression.fixed-abilities.native-registry-unchanged", registryBefore, registryAfter, registryBefore.SequenceEqual(registryAfter));
    }
}
