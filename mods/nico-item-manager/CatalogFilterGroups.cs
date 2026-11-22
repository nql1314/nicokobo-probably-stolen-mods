using System;
using System.Collections.Generic;
using System.Linq;
namespace NicoItemManager
{
    internal static class CatalogFilterGroups
    {
        const string OtherKey = "other";

        static readonly string[] Collections =
        {
            ("production"),
            ("equipment"),
            ("supplies"),
            ("trade"),
        };

        // Usage collections organize browsing only. An item can match several categories;
        // CatalogEntry.Categories retains every original type for native display/export.
        static readonly (string Key, string Collection, string[] Tags)[] Groups =
        {
            ("machine", "production", new[] { "MACHINE", "机器" }),
            ("module", "production", new[] { "MODULE", "NODE", "ALARM_MODULE", "HYDROPONIC_MODULE", "PRINTER_MODULE", "EVAPORATOR_MODULE", "模块" }),
            ("tool", "production", new[] { "TOOL", "工具" }),
            ("material", "production", new[] { "MATERIAL", "BASIC_MATERIAL", "REFINED_MATERIAL", "材料" }),
            ("energy", "production", new[] { "ENERGY_CELL", "电池" }),
            ("hydroponic", "production", new[] { "FARMING_SUPPLIES", "SEED", "MODIFIED_SEED", "HYDROPONIC_MODULE", "种植水培", "种植/水培" }),
            ("wine", "production", new[] { "ALCOHOL", "酿酒" }),
            ("chemical", "production", new[] { "SUBSTANCE", "CHEMICAL_SUPPLIES" }),
            ("husbandry", "production", new[] { "养殖", "养殖/牲畜" }),
            ("weapon", "equipment", new[] { "WEAPON", "FIREARM", "MELEE_WEAPON", "武器" }),
            ("ammo", "equipment", new[] { "AMMUNITION", "MUNITION", "弹药" }),
            ("part", "equipment", new[] { "GUN_MOD", "武器配件", "武器/配件" }),
            ("armor", "equipment", new[] { "ARMOR", "护甲" }),
            ("backpack", "equipment", new[] { "背包" }),
            ("food", "supplies", new[] { "FOOD", "FRESH_FOOD", "TREAT", "PROCESSED_FOOD", "BREVAGE", "食物" }),
            ("medical", "supplies", new[] { "MEDICAL", "MEDICAL_TOOL", "医疗" }),
            ("water", "supplies", new[] { "WATER", "WATER_RATION", "WATER_PURIFICATION_SUPPLY", "OXYGEN_SUPPLY", "FILTER" }),
            ("household", "supplies", new[] { "HOUSEHOLD_GOOD", "日用品" }),
            ("container", "supplies", new[] { "STORAGE", "SUPPLY_CRATE", "容器设施", "容器/设施" }),
            ("document", "trade", new[] { "DOCUMENT", "ACCESS_CARD", "文书", "文书/杂项" }),
            ("valuable", "trade", new[] { "ART", "LUXURY_ITEM", "LOTTERY" }),
            ("special", "trade", new[] { "NARCOTIC", "POISON", "ORGAN" }),
            ("junk", "trade", new[] { "JUNK", "废品" }),
            ("misc", "trade", new[] { "杂项" }),
        };

        public static IEnumerable<string> OrderedKeys => Groups.Select(group => group.Key).Append(OtherKey);
        public static IEnumerable<string> CollectionKeys => Collections;

        public static string CollectionFor(string key) => key == OtherKey ? "trade"
            : Groups.FirstOrDefault(group => group.Key == key).Collection;

        public static string CollectionDisplay(string key)
        {
            if (string.IsNullOrEmpty(key)) return Text.Get("all_collections");
            return Collections.Contains(key) ? Text.Get("collection." + key) : key;
        }

        public static string[] Resolve(IEnumerable<string> categories)
        {
            var tags = new HashSet<string>(categories ?? Array.Empty<string>(), StringComparer.Ordinal);
            // Each group is returned once even when several of its tags match.
            string[] matches = Groups.Where(group => group.Tags.Any(tags.Contains))
                .Select(group => group.Key)
                .ToArray();
            return matches.Length > 0 ? matches : new[] { OtherKey };
        }

        public static string Display(string key)
        {
            if (string.IsNullOrEmpty(key))
                return Text.Get("all_categories");
            if (key == OtherKey)
                return Text.Get("other");
            foreach (var group in Groups)
            {
                if (group.Key == key)
                    return Text.Get("category." + key);
            }
            return key;
        }
    }
}
