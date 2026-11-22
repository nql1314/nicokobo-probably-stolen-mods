using Nicokobo.ModUtilities;
using System;

namespace NicoItemManager
{
    internal static class Text
    {
        internal static string LocaleCode => ModText.LocaleCode;
        internal static string Get(string key, params object[] args) =>
            ModText.Get(NicoItemManagerMod.Id, "manager." + key, args);
        internal static string GetFor(string key, bool? english, params object[] args) =>
            english.HasValue ? ModText.GetFor(NicoItemManagerMod.Id, "manager." + key, english.Value ? "en" : "zh", args)
            : Get(key, args);
    }

    internal static class CategoryLabels
    {
        internal static string Display(string key)
        {
            if (string.IsNullOrEmpty(key)) return Text.Get("all");
            string name = TypeHelper.GetTypeDisplayName(key);
            return string.IsNullOrWhiteSpace(name) ? key : name;
        }
    }
}
