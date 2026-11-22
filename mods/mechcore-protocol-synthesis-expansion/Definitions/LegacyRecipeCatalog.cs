using Newtonsoft.Json.Linq;

namespace MechcoreProtocol.SynthesisExpansion.Definitions;

// Only imports text deliberately placed in this official mod's data directory.
// The source Demo file and player saves are never discovered or changed.
internal static class LegacyRecipeCatalog
{
    private const string Prefix = "nicokobo.mechcore.synthesis";
    private static readonly HashSet<string> FoodIds = new(new[]
    {
        "meat_from_raw_meat", "meat_from_small_meat", "nutrifruit_juice", "meat_sauce_noodles",
        "chocolate_nutrition_bar", "canned_coffee", "nutrifruit_milk", "chocolate_milk", "vanilla_fruit_shake",
        "cheese_noodles", "cheese_meat_patty", "blue_blood_bag", "neon_berry_soda", "compressed_nutrient_liquid", "glowberry_special"
    }.Select(suffix => Prefix + ".recipe." + suffix), StringComparer.Ordinal);

    private static JToken? Get(JObject value, string name) => value.GetValue(name, StringComparison.OrdinalIgnoreCase);
    internal static int Schema(string json)
    {
        var root = JObject.Parse(json);
        return Get(root, "SchemaVersion")?.Value<int>() ?? throw new InvalidDataException("Recipe schema version is missing");
    }
    internal static string ImportSynthesis(string json)
    {
        var root = JObject.Parse(json);
        var recipes = Get(root, "Recipes") as JArray;
        var markups = Get(root, "ProductionMarkups") as JObject;
        if (Get(root, "SchemaVersion")?.Value<int>() != 22 || recipes == null || recipes.Count != 37 || markups == null || markups.Count != 6 ||
            new[] { "K01", "K02", "K03", "K04", "K05", "K06" }.Any(key =>
                markups[key]?.Type != JTokenType.Integer || markups[key]!.Value<int>() is < 0 or > 1000))
            throw new InvalidDataException("Expected the complete schema 22 catalog and six machine markups");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in recipes)
        {
            if (node is not JObject recipe || Get(recipe, "Id")?.Value<string>() is not { Length: > 0 } id || !seen.Add(id))
                throw new InvalidDataException("Legacy recipe IDs are missing or duplicated");
            if (FoodIds.Contains(id) && Get(recipe, "MachineId")?.Value<string>() is not
                (Prefix + ".food_processor" or Prefix + ".liquid_infusion_tower"))
                throw new InvalidDataException("Legacy food machine identity changed");
        }
        if (!FoodIds.All(seen.Contains)) throw new InvalidDataException("Legacy food recipes are incomplete");
        return new JObject
        {
            ["SchemaVersion"] = 23,
            ["Recipes"] = new JArray(recipes.OfType<JObject>().Where(recipe => !FoodIds.Contains(Get(recipe, "Id")!.Value<string>()!))),
            ["ProductionMarkups"] = new JObject(markups.Properties().Where(property => property.Name is not ("K04" or "K06")))
        }.ToString();
    }
}

