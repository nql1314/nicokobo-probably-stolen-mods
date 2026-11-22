using System;
using System.Collections.Generic;
using System.Linq;

namespace NicoItemManager
{
    internal sealed class ItemNames
    {
        readonly Dictionary<string, Dictionary<string, (string Zh, string En)?>> sources =
            new Dictionary<string, Dictionary<string, (string Zh, string En)?>>(StringComparer.Ordinal);

        internal void AddSource(string source, IReadOnlyDictionary<string, string> current,
            IReadOnlyDictionary<string, string> chinese, IReadOnlyDictionary<string, string> english)
        {
            var index = new Dictionary<string, (string Zh, string En)?>(StringComparer.Ordinal);
            foreach (string key in current.Keys.Concat(chinese.Keys).Concat(english.Keys).Distinct(StringComparer.Ordinal))
            {
                current.TryGetValue(key, out string display);
                chinese.TryGetValue(key, out string zh);
                english.TryGetValue(key, out string en);
                var names = (Zh: zh ?? "", En: en ?? "");
                if (names.Zh.Length == 0 && names.En.Length == 0) continue;
                AddAlias(index, display, names);
                AddAlias(index, zh, names);
                AddAlias(index, en, names);
            }
            sources[source] = index;
        }

        static void AddAlias(Dictionary<string, (string Zh, string En)?> index, string display, (string Zh, string En) names)
        {
            if (string.IsNullOrWhiteSpace(display)) return;
            if (!index.TryGetValue(display, out var previous)) index[display] = names;
            else if (!previous.HasValue || previous.Value != names) index[display] = null;
        }

        internal (string Zh, string En) Resolve(string source, string display, string locale)
        {
            if (!string.IsNullOrEmpty(source) && TryResolve(source, display, out var names)) return names;
            if (TryResolve("", display, out names)) return names;
            // A mod may provide only its current-language display name. Keep the
            // other language empty rather than fabricating a translation or an ID.
            return ((locale ?? "").StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? display : "",
                (locale ?? "").StartsWith("en", StringComparison.OrdinalIgnoreCase) ? display : "");
        }

        bool TryResolve(string source, string display, out (string Zh, string En) names)
        {
            names = default;
            if (!string.IsNullOrEmpty(display) && sources.TryGetValue(source, out var index)
                && index.TryGetValue(display, out var value) && value.HasValue)
            {
                names = value.Value;
                return true;
            }
            return false;
        }
    }
}
