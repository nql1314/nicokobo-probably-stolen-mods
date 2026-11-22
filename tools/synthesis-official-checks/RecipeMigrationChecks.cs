using Newtonsoft.Json.Linq;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;

internal static class RecipeMigrationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        void Reject(string text, string label)
        {
            bool rejected = false; try { RecipeConfiguration.Parse(text); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Synthesis migration accepted " + label);
        }
        string fixture = Path.Combine(AppContext.BaseDirectory, "golden", "LegacyRecipes.schema22.json");
        var combined = JObject.Parse(File.ReadAllText(fixture));
        combined["Recipes"]!.Single(recipe => (string?)recipe["Code"] == "P01")["Requirements"]![0]!["Amount"] = 7;
        combined["ProductionMarkups"]!["K02"] = 36;
        var legacy = RecipeConfiguration.Parse(combined.ToString());
        check(legacy.SchemaVersion == 26 && legacy.Recipes.Count == 30 && legacy.ProductionMarkups["K02"] == 36 &&
            legacy.Recipes.Single(recipe => recipe.Code == "P01").ItemInputs.Single().Amount == 7,
            "Combined schema 22 did not preserve synthesis edits while adding smelting and drill routes");
        foreach (int schema in new[] { 23, 24, 25 })
        {
            var root = JObject.Parse(RecipeConfiguration.DefaultJson()); root["SchemaVersion"] = schema;
            var recipes = (JArray)root["Recipes"]!;
            var smelting = SynthesisDefinitions.DefaultRecipes.Where(recipe => RefinerMetalRules.IsSmelting(recipe.OutputRule)).Select(recipe => recipe.Id).ToHashSet();
            foreach (var row in recipes.Where(row => schema < 25 && (string?)row["Code"] == "M10" || schema == 23 && smelting.Contains((string)row["Id"]!)).ToArray()) row.Remove();
            foreach (var row in recipes.Where(row => GlassRecycling.IsRecipe((string)row["Id"]!)))
            {
                row["MachineId"] = "furnace";
                row["Requirements"]![0]!["Amount"] = 7;
                row["BaseOutputCount"] = 3;
            }
            recipes.Single(row => (string?)row["Code"] == "P01")["Requirements"]![0]!["Amount"] = 5;
            root["ProductionMarkups"]!["K03"] = 18;
            foreach (var row in recipes.Where(row => (string?)row["MachineId"] == SynthesisDefinitions.UniversalManufacturerItem.Id))
                row["MachineId"] = SynthesisDefinitions.OwnerId + ".universal_manufacturer";
            var migrated = RecipeConfiguration.Parse(root.ToString());
            check(migrated.SchemaVersion == 26 && migrated.Recipes.Count == 30 && migrated.ProductionMarkups["K03"] == 18 &&
                migrated.Recipes.Single(recipe => recipe.Code == "P01").ItemInputs.Single().Amount == 5 &&
                migrated.Recipes.Where(recipe => recipe.Code.StartsWith("M")).All(recipe => recipe.MachineId == SynthesisDefinitions.UniversalManufacturerItem.Id),
                "Synthesis schema " + schema + " lost custom settings or stable terminal identity");
            check(migrated.Recipes.Where(recipe => GlassRecycling.IsRecipe(recipe.Id)).All(recipe =>
                recipe.MachineId == SynthesisDefinitions.RefinerItem.Id && recipe.ItemInputs.Single().Amount == 7 && recipe.BaseOutputCount == 3),
                "Synthesis schema " + schema + " lost custom glass quantities or kept the furnace target");
            recipes.RemoveAt(0); Reject(root.ToString(), "partial schema " + schema);
        }
        var duplicate = JObject.Parse(RecipeConfiguration.DefaultJson()); duplicate["SchemaVersion"] = 24;
        Reject(duplicate.ToString(), "schema 24 already containing new M10");
        var incomplete = JObject.Parse(File.ReadAllText(fixture)); ((JArray)incomplete["Recipes"]!).RemoveAt(0);
        Reject(incomplete.ToString(), "partial combined schema 22");
        check(SynthesisDefinitions.Recipes.Count == 26 && GlassRecycling.Feedstocks.Count == 4,
            "Parsing legacy synthesis config leaked into active recipes");
    }
}
