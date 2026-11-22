internal static class LocalizationChecks
{
    internal static void Run(Action<bool, string> check, IReadOnlyDictionary<string, (string Chinese, string English)> expected)
    {
        var locales = new[] { "en", "zh", "de", "es", "fr", "ja", "pl", "ru", "zh-TW", "zh-CN", "zh-Hans", "zh-Hant" };
        HashSet<string>? allKeys = null;
        foreach (string locale in locales)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Localization", locale + ".csv");
            var values = File.ReadLines(path).ToDictionary(line => line[..line.IndexOf(',')],
                line => line[(line.IndexOf(',') + 2)..^1].Replace("\"\"", "\"").Replace("\\n", "\n"), StringComparer.Ordinal);
            allKeys ??= values.Keys.ToHashSet(StringComparer.Ordinal);
            check(allKeys.SetEquals(values.Keys) && expected.Keys.All(values.ContainsKey), "Missing generated localization keys: " + locale);
            if (locale is "en" or "zh")
                check(expected.All(pair => values[pair.Key] == (locale == "en" ? pair.Value.English : pair.Value.Chinese)),
                    "Generated resource and compiled text differ: " + locale);
        }
    }
}
