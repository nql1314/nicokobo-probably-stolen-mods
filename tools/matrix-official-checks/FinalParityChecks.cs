using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.ModuleMatrix.Definitions;
using Nicokobo.ContentInterop;

internal static class FinalParityChecks
{
    internal static void Run(Action<bool, string> check, string source)
    {
        const string flushOwner = "checks.matrix.flush_order";
        var stages = new List<string>();
        void Settlement() => stages.Add("settlement");
        ContentRuntime.Attach(flushOwner);
        try
        {
            ProductionHooks.RegisterNightFlush(flushOwner, () => stages.Add("production"));
            ModHook.OnGoingSleepLate += Settlement;
            ModHook.Night();
            check(stages.SequenceEqual(new[] { "production", "settlement" }), "Night production flush ran after later settlement");
            stages.Clear(); ContentRuntime.Attach(flushOwner); ContentRuntime.Attach(flushOwner); ModHook.Night();
            check(stages.SequenceEqual(new[] { "production", "settlement" }), "Repeated provider attachment duplicated night flush");
            stages.Clear(); ContentRuntime.Detach(flushOwner); ModHook.Night();
            check(stages.SequenceEqual(new[] { "settlement" }), "Detached provider retained its night production callback");
        }
        finally { ModHook.OnGoingSleepLate -= Settlement; ContentRuntime.Detach(flushOwner); }
        RunLocalization(check);
    }

    internal static void RunLocalization(Action<bool, string> check)
    {
        var texts = new Dictionary<string, (string, string)>();
        foreach (var item in FixedModuleDefinitions.All.Concat(ContinuousModuleDefinitions.All.Select(value => value.Module)))
            texts[item.Id + ".name"] = (item.ChineseName, item.EnglishName);
        foreach (var item in NodeItemDefinitions.All) texts[item.Id + ".name"] = (item.ChineseName, item.EnglishName);
        foreach (var item in MachineModuleDefinitions.All)
        {
            texts[item.Id + ".name"] = (item.ChineseName, item.EnglishName);
            texts[item.Id + ".ability"] = (item.ChineseAbility, item.EnglishAbility);
        }
        foreach (var effect in PlacementEffectDefinitions.All)
        {
            texts[effect.Id + ".name"] = (effect.ChineseName, effect.EnglishName);
            texts[effect.Id + ".description"] = (effect.ChineseDescription, effect.EnglishDescription);
        }
        foreach (var effect in NodeEffectDefinitions.All)
        {
            texts[effect.Id + ".name"] = (effect.ChineseName, effect.EnglishName);
            texts[effect.Id + ".description"] = (effect.ChineseDescription, effect.EnglishDescription);
        }
        LocalizationChecks.Run(check, texts);
        var assets = new LoadedMod { Manifest = new() { ID = MechcorePackage.Id }, Localization = new() { ["en"] = texts.ToDictionary(pair => pair.Key, pair => pair.Value.Item2) } };
        foreach (var icon in ModuleIconDefinitions.All) assets.Sprites[icon.SpriteKey] = new();
        ModHelper.Localize = (id, key) => texts[key].Item1;
        ModHelper.Locale.Identifier.Code = "zh-CN";
        LocHelper.LocaleCode = "zh-CN";
        MatrixMod.Assets(assets);
        var module = MatrixItems.Create(MachineModuleDefinitions.All[0].Id); ModHook.Tooltip(new(), module);
        check(module.name == MachineModuleDefinitions.All[0].ChineseName && module.GetTagReadonly("ADDITIONAL_EFFECT_STRING").GetString() == MachineModuleDefinitions.All[0].ChineseAbility,
            "Matrix factory/tooltip did not consume localized names and abilities");
        check(PlacementEffectDefinitions.All.All(effect => ModuleEffectHelper.moduleEffects[effect.Id].displayName == effect.ChineseName),
            "Matrix native effect registry retained stale language");
        foreach (var node in NodeItemDefinitions.All)
        {
            var definition = NodeEffectDefinitions.ByCode[node.Code];
            var item = MatrixItems.Create(node.Id);
            string savedEffect = item.GetTagReadonly("MODULE_EFFECT1_TAG").GetString();
            check(item.shortDescription == definition.ChineseDescription,
                "Chinese node factory description is missing: " + node.Code);
            item.shortDescription = "old saved description";
            item.onLoaded!(item);
            check(item.shortDescription == definition.ChineseDescription,
                "Loaded node retained its saved description: " + node.Code);
            foreach (bool english in new[] { true, false })
            {
                ModHelper.Locale.Identifier.Code = english ? "en" : "zh-CN";
                LocHelper.LocaleCode = ModHelper.Locale.Identifier.Code;
                ModHelper.Localize = (_, key) => english ? texts[key].Item2 : texts[key].Item1;
                var tooltip = new RichTextBuilder();
                ModHook.Tooltip(tooltip, item);
                check(item.shortDescription == (english ? definition.EnglishDescription : definition.ChineseDescription) &&
                    item.name == (english ? node.EnglishName : node.ChineseName),
                    "Node tooltip did not refresh both name and description after changing language: " + node.Code);
                check(FixedModuleAbilities.CreateTooltip(tooltip, item) &&
                    tooltip.Lines.Contains(english ? definition.EnglishName : definition.ChineseName) &&
                    !tooltip.Lines.Contains(item.shortDescription),
                    "Fixed ability block duplicated the standalone node description: " + node.Code);
                check(item.GetTagReadonly("MODULE_EFFECT1_TAG").GetString() == savedEffect,
                    "Refreshing a node description changed its saved ability: " + node.Code);
            }
        }
        LocHelper.LocaleCode = "en";
        ModHelper.Locale.Identifier.Code = "en"; ModHelper.Localize = null; MatrixText.Assets = null; MatrixEffects.RefreshText();
    }
}
