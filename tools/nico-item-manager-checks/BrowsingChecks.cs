using NicoItemManager;

internal static class BrowsingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(ItemTooltipText.Plain("<color=#c4c4b0><b>1毫升滴管</b></color>\n<color=#42a2aa>0 ml / 1 ml</color>")
            == "1毫升滴管\n0 ml / 1 ml", "Preview descriptions must display native markup as readable text.");
        check(ItemTooltipText.Plain("<b>温度 < 10</b><br>含量 < 20 > 0") == "温度 < 10\n含量 < 20 > 0",
            "Removing tooltip formatting must preserve literal comparison signs and line breaks.");
        check(CatalogFilterGroups.Resolve(new[] { "ARMOR" }).Contains("armor")
            && CatalogFilterGroups.Resolve(new[] { "ENERGY_CELL" }).Contains("energy")
            && CatalogFilterGroups.Resolve(new[] { "HOUSEHOLD_GOOD" }).Contains("household"),
            "Native armor, energy and household types must have useful categories instead of Other.");
        var catalog = new ItemCatalog();
        catalog.Entries.Add(new CatalogEntry { Id = "hybrid", NameEn = "Hybrid unit",
            Groups = CatalogFilterGroups.Resolve(new[] { "MACHINE", "MODULE", "ARMOR" }) });
        catalog.Entries.Add(new CatalogEntry { Id = "food", Groups = CatalogFilterGroups.Resolve(new[] { "FOOD" }) });
        var results = new List<CatalogEntry>();
        catalog.FilterInto(results, "", null, "production");
        check(results.Count == 1 && results[0].Id == "hybrid",
            "A collection must include a multi-category item once rather than duplicate it for each category.");
        catalog.FilterInto(results, "HYBRID", "armor", "equipment");
        check(results.Count == 1 && results[0].Id == "hybrid", "Collection, category and search must intersect.");
        catalog.FilterInto(results, "", "food", "equipment");
        check(results.Count == 0, "A category outside a selected collection must not leak results.");
        catalog.FilterInto(results, "", null, "supplies");
        check(results.Count == 1 && results[0].Id == "food", "Changing the collection must show its own items.");
    }
}
