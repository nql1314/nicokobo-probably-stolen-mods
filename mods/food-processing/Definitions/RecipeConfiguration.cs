using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Nicokobo.FoodProcessing.Definitions;

public sealed record RecipeDocument(int SchemaVersion, IReadOnlyList<RecipeDefinition> Recipes,
    IReadOnlyDictionary<string, int> ProductionMarkups);

public static class RecipeConfiguration
{
    public const int SchemaVersion = 12;
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new StringEnumConverter() }
    };
    private static readonly IReadOnlyDictionary<string, int> DefaultMarkups =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>
        { ["K04"] = FoodNumbers.ProductionMarkups.K04, ["K06"] = FoodNumbers.ProductionMarkups.K06 });
    private static IReadOnlyDictionary<string, int> _markups = DefaultMarkups;

    public static RecipeDocument DefaultDocument() => new(SchemaVersion, FoodDefinitions.DefaultRecipes, DefaultMarkups);
    public static string DefaultJson() => JsonConvert.SerializeObject(DefaultDocument(), Settings);
    public static int ProductionMarkupPercent(string machineId) => _markups[FoodDefinitions.Machine(machineId).Code];
    public static int RecipeMarkup(RecipeDefinition recipe) => recipe.Code == "EXT" ? recipe.MarkupPercent ?? ProductionMarkupPercent(recipe.MachineId) : FoodExpansion.StarMarkup(recipe.Stars);

    public static RecipeDocument Parse(string json)
    {
        var document = JsonConvert.DeserializeObject<RecipeDocument>(json, Settings)
            ?? throw new InvalidDataException("Recipe document is null");
        if (document.SchemaVersion is not (7 or 8 or 9 or 10 or 11 or SchemaVersion))
            throw new InvalidDataException($"Expected food processing schema 7 through {SchemaVersion}");
        if (document.ProductionMarkups == null || document.ProductionMarkups.Count != 2 ||
            !document.ProductionMarkups.ContainsKey("K04") || !document.ProductionMarkups.ContainsKey("K06") ||
            document.ProductionMarkups.Values.Any(value => value is < 0 or > 1000))
            throw new InvalidDataException("Invalid K04/K06 production markups");
        var defaults = FoodDefinitions.DefaultRecipes.ToDictionary(recipe => recipe.Id, StringComparer.Ordinal);
        document = document with { Recipes = ValidateIdentities(document.Recipes, defaults, document.SchemaVersion), SchemaVersion = SchemaVersion };
        var errors = DefinitionValidator.ValidateRecipes(document.Recipes);
        if (errors.Count != 0) throw new InvalidDataException(string.Join("; ", errors));
        return document;
    }

    private static IReadOnlyList<RecipeDefinition> ValidateIdentities(IReadOnlyList<RecipeDefinition>? recipes, IReadOnlyDictionary<string, RecipeDefinition> defaults, int schemaVersion)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (recipes == null) throw new InvalidDataException("Recipes are missing");
        foreach (var recipe in recipes)
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.Id) || !seen.Add(recipe.Id) ||
                !defaults.TryGetValue(recipe.Id, out var original) || recipe.Code != original.Code ||
                recipe.MachineId != original.MachineId || recipe.OutputItemId != original.OutputItemId || recipe.Requirements == null ||
                recipe.Requirements.Any(input => input == null || input.Conditions == null ||
                    input.Conditions.Any(condition => condition == null)))
                throw new InvalidDataException("Invalid recipe identity or requirements");
        if (schemaVersion < 12)
            recipes = recipes.Select(recipe => FoodDefinitions.IsPooledMedicalKit(recipe.OutputItemId) ? defaults[recipe.Id] : recipe).ToArray();
        var missing = defaults.Values.Where(recipe => !seen.Contains(recipe.Id)).ToArray();
        IEnumerable<string> optionalCodes = Array.Empty<string>();
        if (schemaVersion < 10)
            optionalCodes = FoodExpansion.FarmAndPharmacyProducts.Select(product => product.Recipe.Code);
        if (schemaVersion < 11) optionalCodes = optionalCodes.Concat(FoodExpansion.MedicalKitProducts.Select(product => product.Recipe.Code));
        if (schemaVersion < 9) optionalCodes = optionalCodes.Concat(new[] { "F39", "F40", "F41" });
        // Schema 7 shipped with either 22 or 25 recipes.
        if (schemaVersion == 7)
            optionalCodes = optionalCodes.Concat(new[] { "F36", "F37", "F38" }).Concat(FoodExpansion.Products.Select(product => product.Recipe.Code));
        if (missing.Any(recipe => !optionalCodes.Contains(recipe.Code)))
            throw new InvalidDataException("Missing food processing recipe");
        if (schemaVersion != 7)
        {
            foreach (var recipe in recipes)
            {
                var original = defaults[recipe.Id];
                if (recipe.Stars != original.Stars || recipe.Requirements.Count != original.Requirements.Count ||
                    original.Requirements.Any(expected => !recipe.Requirements.Any(input => input.Kind == expected.Kind &&
                        input.ResourceId == expected.ResourceId && expected.Conditions.All(condition => input.Conditions.Any(actual =>
                            actual.Property == condition.Property && actual.MinimumValue >= condition.MinimumValue)))))
                    throw new InvalidDataException("Recipe stars or ingredient eligibility changed: " + recipe.Code);
            }
            return missing.Length == 0 ? recipes : recipes.Concat(missing).ToArray();
        }
        // Keep quantities for ingredients still in the recipe. Removed ingredients
        // are dropped; replacement ingredients use the new catalog quantities.
        return recipes.Select(recipe =>
        {
            var original = defaults[recipe.Id];
            return original with
            {
                BaseOutputCount = recipe.BaseOutputCount,
                Requirements = original.Requirements.Select(input => input with
                {
                    Amount = recipe.Requirements.FirstOrDefault(old => old.Kind == input.Kind && old.ResourceId == input.ResourceId)?.Amount ?? input.Amount
                }).ToArray()
            };
        }).Concat(missing).ToArray();
    }

    internal static void Load()
    {
        _markups = DefaultMarkups;
        FoodDefinitions.UseRecipes(FoodDefinitions.DefaultRecipes);
        try
        {
            if (!ModHelper.ModDataFileExists(FoodDefinitions.ModId, "recipes.json"))
                ModHelper.WriteModDataFileText(FoodDefinitions.ModId, "recipes.json", DefaultJson());
            string json = ModHelper.ReadModDataFileText(FoodDefinitions.ModId, "recipes.json");
            var document = Parse(json);
            _markups = document.ProductionMarkups;
            FoodDefinitions.UseRecipes(document.Recipes);
        }
        catch (Exception error)
        {
            FoodMod.Warn("Invalid recipes.json; using built-in recipes. File preserved. " + error.Message);
        }
    }
}
