using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace NicoItemManager
{
    internal static class CatalogExporter
    {
        internal static string Export(ItemCatalog catalog)
        {
            if (!catalog.Ready || catalog.Entries.Count == 0)
                throw new InvalidOperationException(Text.Get("wait_for_the_item_scan_to_finish"));
            var markdown = new StringBuilder(Text.Get("nico_item_manager_probably_stolen"));
            markdown.AppendLine(Text.Get("locale_registered_items", Text.LocaleCode, catalog.Entries.Count));
            markdown.AppendLine();
            markdown.AppendLine(Text.Get("id_name_unit_value_sprite_path_atlas"));
            markdown.AppendLine("|---|---|---|---|---|---|---|");
            foreach (CatalogEntry entry in catalog.Entries)
                markdown.AppendLine($"| {Cell(entry.Id)} | {Cell(entry.Name)} | {(entry.Error == null ? entry.Value.ToString() : "")} | {Cell(entry.SpritePath)} | {Cell(entry.AtlasPath)} | {Cell(string.Join(", ", entry.Categories.Select(CategoryLabels.Display)))} | {Cell(entry.Error)} |");
            // The supported data helpers keep exports inside this mod's data folder.
            ModHelper.WriteModDataFileText(NicoItemManagerMod.Id, "items_catalog.md", markdown.ToString());
            ModHelper.WriteModDataFileText(NicoItemManagerMod.Id, "item_values.json", JsonConvert.SerializeObject(
                catalog.Entries.Where(entry => entry.Error == null).ToDictionary(entry => entry.Id, entry => entry.Value), Formatting.Indented));
            ModHelper.WriteModDataFileText(NicoItemManagerMod.Id, "items_catalog.json", JsonConvert.SerializeObject(
                catalog.Entries.Select(entry => new { entry.Id, entry.Name, entry.NameZh, entry.NameEn, UnitValue = entry.Error == null ? (long?)entry.Value : null,
                    entry.SpritePath, entry.AtlasPath, CategoryTags = entry.Categories, entry.Error }), Formatting.Indented));
            return ModHelper.GetModDataFolder(NicoItemManagerMod.Id);
        }

        static string Cell(string text) => (text ?? "").Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");
    }
}
