#nullable enable
using HarmonyLib;

namespace Nicokobo.ModUtilities;

[HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
internal static class ConfigSectionLocalization
{
    // Unlike the setting label and tooltip, the native section is a plain string.
    // Keep the key for grouping and translate only the header when the window opens.
    private static void Postfix(LoadedMod mod, CustomUIWindow? __result)
    {
        if (__result == null || mod.Instance?.GetType().Assembly != typeof(ConfigSectionLocalization).Assembly) return;
        var config = ModConfig.Find(mod.Manifest.ID);
        string locale = ModText.LocaleCode;
        foreach (string section in config.Sections)
            if (ModText.Find(mod.Manifest.ID, section, locale) is string title)
                __result.Get("section_" + section)?.SetText(title);
    }
}
