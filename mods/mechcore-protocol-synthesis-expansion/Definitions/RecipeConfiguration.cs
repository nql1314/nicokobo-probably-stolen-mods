using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MechcoreProtocol.SynthesisExpansion.Definitions;

public sealed record RecipeDocument(int SchemaVersion, IReadOnlyList<RecipeDefinition> Recipes,
    IReadOnlyDictionary<string, int> ProductionMarkups);

public static class RecipeConfiguration
{
    public const int SchemaVersion = 26;
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented, Converters = { new StringEnumConverter() }
    };
    private static readonly IReadOnlyDictionary<string, int> Defaults = new ReadOnlyDictionary<string, int>(
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["K01"] = SynthesisNumbers.ProductionMarkups.K01, ["K02"] = SynthesisNumbers.ProductionMarkups.K02,
            ["K03"] = SynthesisNumbers.ProductionMarkups.K03, ["K05"] = SynthesisNumbers.ProductionMarkups.K05
        });
    private static IReadOnlyDictionary<string, int> _markups = Defaults;
    public static RecipeDocument DefaultDocument() => new(SchemaVersion,
        SynthesisDefinitions.DefaultRecipes.Concat(GlassRecycling.DefaultFeedstocks.Select(feedstock => feedstock.Recipe)).ToArray(), Defaults);
    public static string DefaultJson() => JsonConvert.SerializeObject(DefaultDocument(), Settings);
    public static int ProductionMarkupPercent(string machineId) =>
        _markups[SynthesisDefinitions.Machine(machineId).Code];

    public static RecipeDocument Parse(string json)
    {
        if (LegacyRecipeCatalog.Schema(json) == 22) json = LegacyRecipeCatalog.ImportSynthesis(json);
        var document = JsonConvert.DeserializeObject<RecipeDocument>(json, Settings)
            ?? throw new InvalidDataException("Recipe document is null");
        if (document.SchemaVersion == 23 && document.Recipes != null)
            document = document with { SchemaVersion = 24, Recipes = document.Recipes.Concat(
                SynthesisDefinitions.DefaultRecipes.Where(recipe => Domain.RefinerMetalRules.IsSmelting(recipe.OutputRule))).ToArray() };
        if (document.SchemaVersion == 24 && document.Recipes != null)
            document = document with { SchemaVersion = 25, Recipes = document.Recipes.Append(
                SynthesisDefinitions.DefaultRecipes.Single(recipe => recipe.Code == "M10")).ToArray() };
        if (document.SchemaVersion == 25 && document.Recipes != null)
            document = document with { SchemaVersion = SchemaVersion, Recipes = document.Recipes.Select(recipe =>
                recipe is { MachineId: "furnace" } && GlassRecycling.IsRecipe(recipe.Id)
                    ? recipe with { MachineId = SynthesisDefinitions.RefinerItem.Id } : recipe).ToArray() };
        if (document.SchemaVersion != SchemaVersion || document.Recipes == null || document.ProductionMarkups == null ||
            document.ProductionMarkups.Count != Defaults.Count || Defaults.Keys.Any(key => !document.ProductionMarkups.ContainsKey(key)) ||
            document.ProductionMarkups.Values.Any(value => value is < 0 or > 1000))
            throw new InvalidDataException("Expected complete synthesis schema 26 with valid K01/K02/K03/K05 markups");
        document = document with { Recipes = document.Recipes.Select(recipe => recipe == null ? recipe! : recipe with
        {
            MachineId = recipe.MachineId == SynthesisDefinitions.OwnerId + ".universal_manufacturer"
                ? SynthesisDefinitions.UniversalManufacturerItem.Id : recipe.MachineId
        }).ToArray() };
        var expected = DefaultDocument().Recipes.ToDictionary(recipe => recipe.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in document.Recipes)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.Id) || !seen.Add(recipe.Id) ||
                !expected.TryGetValue(recipe.Id, out var original) || recipe.Code != original.Code || recipe.MachineId != original.MachineId ||
                recipe.Requirements == null || recipe.Requirements.Any(rule => rule == null || rule.Conditions == null ||
                    rule.Conditions.Any(condition => condition == null)))
                throw new InvalidDataException("Invalid recipe identity or requirements");
            if (ProductionCatalog.OwnsItem(recipe.MachineId) && !ProductionCatalog.OwnsItem(recipe.OutputItemId) &&
                recipe.OutputItemId is not ("metal_ingot" or "common_electronic" or "mouse_trap"))
                throw new InvalidDataException("Production output has not been migrated: " + recipe.OutputItemId);
        }
        if (seen.Count != expected.Count) throw new InvalidDataException("Missing synthesis recipe");
        var previousRecipes = SynthesisDefinitions.Recipes.Concat(GlassRecycling.Feedstocks.Select(feedstock => feedstock.Recipe)).ToArray();
        try
        {
            SetRecipes(document.Recipes);
            var errors = DefinitionValidator.Validate();
            if (errors.Count != 0) throw new InvalidDataException(string.Join("; ", errors));
            foreach (var machine in ProductionCatalog.Machines)
                _ = RecipeOrder.Sort(machine.ItemId, ProductionCatalog.AllRecipes, 0, 0, false).ToArray();
        }
        finally { SetRecipes(previousRecipes); }
        return document;
    }

    internal static void Load()
    {
        Reset();
        try
        {
            if (!ModHelper.ModDataFileExists(SynthesisDefinitions.OwnerId, "recipes.json"))
                ModHelper.WriteModDataFileText(SynthesisDefinitions.OwnerId, "recipes.json", DefaultJson());
            var document = Parse(ModHelper.ReadModDataFileText(SynthesisDefinitions.OwnerId, "recipes.json"));
            _markups = new ReadOnlyDictionary<string, int>(document.ProductionMarkups.ToDictionary(pair => pair.Key, pair => pair.Value));
            SetRecipes(document.Recipes);
        }
        catch (Exception error) { SynthesisMod.Warn("Invalid recipes.json; using built-in recipes. File preserved. " + error.Message); }
    }

    internal static void Reset()
    {
        _markups = Defaults;
        SetRecipes(DefaultDocument().Recipes);
    }

    private static void SetRecipes(IEnumerable<RecipeDefinition> recipes)
    {
        var all = recipes.ToArray();
        SynthesisDefinitions.UseRecipes(all.Where(recipe => !GlassRecycling.IsRecipe(recipe.Id)).ToArray());
        GlassRecycling.UseRecipes(all.Where(recipe => GlassRecycling.IsRecipe(recipe.Id)).ToArray());
    }
}
