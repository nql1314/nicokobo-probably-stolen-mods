using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace NicoItemManager
{
    internal sealed class CatalogEntry
    {
        internal string Id;
        internal string Name;
        internal string NameZh = "";
        internal string NameEn = "";
        internal string[] Categories = Array.Empty<string>();
        internal long Value;
        internal string SpritePath;
        internal string AtlasPath;
        internal string Error;
        internal Sprite Icon;
        internal bool Resolved;
        internal int Revision;
        internal string[] Groups = Array.Empty<string>();
    }

    internal sealed class ItemCatalog
    {
        internal readonly List<CatalogEntry> Entries = new List<CatalogEntry>();
        internal string[] Categories { get; private set; } = Array.Empty<string>();
        internal bool Ready { get; private set; }
        internal int Scanned { get; private set; }
        internal int Total { get; private set; }
        internal int Revision { get; private set; }
        readonly Dictionary<string, CatalogEntry> byId = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        readonly Queue<string> priority = new Queue<string>();
        readonly HashSet<string> queued = new HashSet<string>(StringComparer.Ordinal);

        internal void Prioritize(IEnumerable<string> ids)
        {
            priority.Clear();
            queued.Clear();
            foreach (string id in ids)
                if (byId.TryGetValue(id, out CatalogEntry entry) && !entry.Resolved && queued.Add(id))
                    priority.Enqueue(id);
        }

        internal static bool AdvanceScan(IEnumerator<object> scan, double budgetMilliseconds)
        {
            long started = Stopwatch.GetTimestamp();
            // Item factories use Unity objects and must stay on the main thread.
            // Yield by elapsed time rather than imposing a fixed item count.
            while (true)
            {
                if (!scan.MoveNext()) return true;
                if ((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency >= budgetMilliseconds)
                    return false;
            }
        }

        internal IEnumerator<object> Scan()
        {
            // Publish identifiers first. Ready continues to gate complete exports.
            var ids = DirectoryMaster.GetIdentifierList<GameItem>()
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Total = ids.Length;
            Scanned = 0;
            Ready = false;
            Entries.Clear();
            byId.Clear();
            priority.Clear();
            queued.Clear();
            Categories = Array.Empty<string>();
            foreach (string id in ids)
            {
                var entry = new CatalogEntry { Id = id, Name = id };
                Entries.Add(entry);
                byId.Add(id, entry);
            }
            Revision++;
            yield return null;
            ItemNames names = NativeItemNames.Load();
            yield return null;
            var groups = new HashSet<string>();
            int next = 0;
            while (Scanned < Total)
            {
                CatalogEntry entry = null;
                while (priority.Count > 0 && entry == null)
                {
                    string id = priority.Dequeue();
                    queued.Remove(id);
                    if (!byId[id].Resolved) entry = byId[id];
                }
                if (entry == null)
                {
                    while (next < ids.Length && byId[ids[next]].Resolved) next++;
                    entry = byId[ids[next++]];
                }
                Read(entry, names);
                entry.Groups = CatalogFilterGroups.Resolve(entry.Categories);
                bool categoriesChanged = false;
                foreach (string group in entry.Groups) categoriesChanged |= groups.Add(group);
                if (categoriesChanged) Categories = CatalogFilterGroups.OrderedKeys.Where(groups.Contains).ToArray();
                entry.Resolved = true;
                entry.Revision++;
                Scanned++;
                Revision++;
                yield return null;
            }
            Ready = true;
            Revision++;
        }

        static void Read(CatalogEntry entry, ItemNames names)
        {
            string id = entry.Id;
            GameItem item = null;
            try
            {
                // Prototype reads must not assign a run UID or fire permit events.
                item = ItemSpawner.CreateItem(id);
                if (item == null || string.IsNullOrWhiteSpace(item.identifier))
                    throw new InvalidOperationException("Item factory returned no valid item.");
                entry.Name = item.GetDisplayName();
                TagState sourceTag = null;
                if (item.state?.dict != null) item.state.dict.TryGetValue("MOD_ID_TAG", out sourceTag);
                if (entry.Name != entry.Id)
                    (entry.NameZh, entry.NameEn) = names.Resolve(sourceTag?.valueString, entry.Name, Text.LocaleCode);
                entry.Categories = (item.itemTypes ?? new List<string>())
                    .Where(type => !string.IsNullOrWhiteSpace(type)).Distinct(StringComparer.Ordinal).ToArray();
                entry.Value = item.unitValue;
                entry.SpritePath = item.spritePath;
                entry.AtlasPath = item.spriteAtlasPath;
                entry.Icon = item is GameItemElement element ? element.ResolveSpriteByName(item.spritePath)
                    : RenderHandler.LoadFromAtlas(item.spriteAtlasPath, item.spritePath);
            }
            catch (Exception error)
            {
                entry.Error = error.Message;
                NicoItemManagerMod.Log?.Warning($"Catalog '{id}': {error.Message}");
            }
            finally { ItemSpawner.Release(item); }
        }

        internal void FilterInto(List<CatalogEntry> output, string query, string category, string collection = null)
        {
            output.Clear();
            query = (query ?? "").Trim();
            foreach (CatalogEntry entry in Entries)
            {
                if (!string.IsNullOrEmpty(category) && !entry.Groups.Contains(category))
                    continue;
                if (!string.IsNullOrEmpty(collection) && !entry.Groups.Any(group => CatalogFilterGroups.CollectionFor(group) == collection))
                    continue;
                if (query.Length == 0 || Contains(entry.Id, query) || Contains(entry.Name, query)
                    || Contains(entry.NameZh, query) || Contains(entry.NameEn, query)
                    || entry.Categories.Any(type => Contains(type, query) || Contains(CategoryLabels.Display(type), query))
                    || entry.Groups.Any(group => Contains(CatalogFilterGroups.Display(group), query)))
                    output.Add(entry);
            }
        }

        static bool Contains(string value, string query) =>
            !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
