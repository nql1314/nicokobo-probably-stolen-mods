using System;
using System.Collections.Generic;
using Nicokobo.ModUtilities;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace NicoItemManager
{
    internal static class NativeItemNames
    {
        internal static ItemNames Load()
        {
            var names = new ItemNames();
            Locale current = ModHelper.GetCurrentLocale();
            Locale zh = LocalizationSettings.AvailableLocales.Locales.Find(locale => locale.Identifier.Code == "zh");
            Locale en = LocalizationSettings.AvailableLocales.Locales.Find(locale => locale.Identifier.Code == "en");
            names.AddSource("", ReadTable(current), ReadTable(zh), ReadTable(en));
            string localeCode = Text.LocaleCode;
            foreach (LoadedMod mod in ModLoader.Mods)
            {
                if (!mod.IsEnabled || mod.Localization.Count == 0) continue;
                // Index the same per-key locale fallback used by item factories, including partial translations.
                var localized = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string code in ModText.LocaleFallbacks(localeCode))
                    if (mod.Localization.TryGetValue(code, out var translations))
                        foreach (var entry in translations)
                            if (!string.IsNullOrWhiteSpace(entry.Value)) localized.TryAdd(entry.Key, entry.Value);
                mod.Localization.TryGetValue("zh", out var chinese);
                mod.Localization.TryGetValue("en", out var english);
                names.AddSource(mod.Manifest.ID, localized,
                    chinese ?? new Dictionary<string, string>(), english ?? new Dictionary<string, string>());
            }
            return names;
        }

        static Dictionary<string, string> ReadTable(Locale locale)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (locale == null) return result;
            try
            {
                // Read each requested locale directly; never change SelectedLocale,
                // recreate all item factories in another language, or reload the UI.
                var table = LocalizationSettings.StringDatabase.GetTable("Item", locale);
                if (table == null) return result;
                foreach (var entry in table.Values)
                {
                    string key = table.SharedData.GetEntry(entry.KeyId)?.Key;
                    if (key != null && key.EndsWith("_name", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(entry.Value))
                        result[key] = entry.Value;
                }
            }
            catch (Exception error) { NicoItemManagerMod.Log?.Warning($"Item names ({locale.Identifier.Code}): {error.Message}"); }
            return result;
        }
    }
}
