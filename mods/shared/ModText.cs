#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Nicokobo.ModUtilities;

internal static partial class ModText
{
    private const string TraditionalChinese = "zh-TW";

    internal static string LocaleCode
    {
        get
        {
            string code = SelectedLocaleCode() ?? "en";
            return UsesTraditionalOverlay(code) ? TraditionalChinese : code;
        }
    }

    // Legacy providers may supply just Chinese and English display strings.
    internal static bool IsChinese
    {
        get
        {
            string code = LocaleCode;
            return string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("zh-", StringComparison.OrdinalIgnoreCase);
        }
    }

    // SelectedLocale waits synchronously. Text lookups must not force Unity to load a locale at startup.
    private static string? SelectedLocaleCode()
    {
        if (!LocalizationSettings.HasSettings) return null;
        var selection = LocalizationSettings.SelectedLocaleAsync;
        return selection.IsValid() && selection.IsDone && selection.Status == AsyncOperationStatus.Succeeded
            ? selection.Result?.Identifier.Code : null;
    }

    // Windows also use native item names; wait for the new locale's tables before rebuilding them.
    internal static bool NativeTablesReady
    {
        get
        {
            if (!LocalizationSettings.HasSettings) return false;
            var initialization = LocalizationSettings.InitializationOperation;
            return initialization.IsValid() && initialization.IsDone &&
                initialization.Status == AsyncOperationStatus.Succeeded;
        }
    }

    internal static string Get(string modId, string key, params object[] args) =>
        GetFor(modId, key, LocaleCode, args);

    internal static string GetFor(string modId, string key, string locale, params object[] args) =>
        Format(Find(modId, key, locale) ?? key, args);

    internal static string? Find(string modId, string key, string locale,
        string? fallbackModId = null, string? fallbackKey = null)
    {
        var mods = ModLoader.Mods;
        var tables = mods.Find(entry => entry.Manifest.ID == modId)?.Localization;
        var fallbackTables = fallbackModId == null ? null :
            mods.Find(entry => entry.Manifest.ID == fallbackModId)?.Localization;
        foreach (string code in LocaleFallbacks(locale))
        {
            string? text = FindInLocale(tables, modId, key, code);
            if (text == null && fallbackModId != null && fallbackKey != null)
                text = FindInLocale(fallbackTables, fallbackModId, fallbackKey, code);
            if (text != null) return text;
        }
        return null;
    }

    private static string? FindInLocale(Dictionary<string, Dictionary<string, string>>? tables,
        string modId, string key, string locale)
    {
        if (tables?.TryGetValue(locale, out var translations) == true &&
            translations.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text))
            return text;

        // The compiled copy is available before the loader has read the CSVs.
        string? authored = null;
        FindAuthored(modId, key, locale, ref authored);
        return authored;
    }

    static partial void FindAuthored(string modId, string key, string locale, ref string? value);

    internal static IEnumerable<string> LocaleFallbacks(string locale)
    {
        while (locale.Length != 0)
        {
            yield return locale;
            string code = locale.ToLowerInvariant();
            if (code == "en") yield break;

            // Keep Traditional Chinese distinct from the game's Simplified Chinese table.
            if (code is "zh-tw" or "zh-hant")
            {
                yield return code == "zh-tw" ? "zh-Hant" : TraditionalChinese;
                break;
            }
            if (code == "ko") yield return "ko-KR";
            if (code is "ptbr" or "pt-br")
            {
                yield return code == "ptbr" ? "pt-BR" : "ptBR";
                locale = "pt";
                continue;
            }

            int separator = locale.LastIndexOf('-');
            if (separator < 0) break;
            locale = locale.Substring(0, separator);
        }
        yield return "en";
    }

    private static string Format(string text, object[] args)
    {
        if (args == null || args.Length == 0) return text;
        try { return string.Format(text, args); }
        catch (FormatException) { return text; }
    }

    // These Workshop patches keep the original Chinese locale selected.
    private static bool UsesTraditionalOverlay(string code) =>
        string.Equals(code, "zh", StringComparison.OrdinalIgnoreCase) &&
        ModLoader.Mods.Exists(mod => mod.IsEnabled && mod.Instance != null &&
            mod.Manifest.ID is "seacucu.zh-tw" or "dbfoxtw.zh-hant");
}
