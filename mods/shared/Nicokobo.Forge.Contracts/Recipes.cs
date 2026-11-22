namespace Nicokobo.ContentInterop;

public sealed class IngredientView
{
    public string ItemId { get; }
    public int Amount { get; }
    public Func<GameItem, bool> Matches { get; }
    public string Chinese { get; }
    public string English { get; }
    // Evaluated at display time in the provider's current game locale.
    public Func<string>? LocalizedLabel { get; init; }
    public IngredientView(string itemId, int amount, Func<GameItem, bool> matches, string chinese, string english)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        ItemId = itemId; Amount = amount; Matches = matches ?? throw new ArgumentNullException(nameof(matches));
        Chinese = chinese; English = english;
    }
}

public class RecipeView
{
    public string Id { get; }
    public string MachineId { get; }
    public IReadOnlyList<IngredientView> Ingredients { get; }
    public string OutputItemId { get; }
    public int OutputCount { get; }
    // Native water parts, rather than rounded litres; 1000 parts = one recipe ml.
    public int WaterParts { get; }
    public Func<GameItem, bool> IsAvailableFor { get; }
    public Func<GameItem, string>? Preview { get; }
    public RecipeView(string id, string machineId, IEnumerable<IngredientView> ingredients,
        string outputItemId, int outputCount, int waterParts = 0, Func<GameItem, bool>? isAvailableFor = null,
        Func<GameItem, string>? preview = null)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(machineId) || outputCount <= 0 || waterParts < 0)
            throw new ArgumentException("Invalid production recipe");
        Id = id; MachineId = machineId; Ingredients = Array.AsReadOnly(ingredients.ToArray());
        OutputItemId = outputItemId; OutputCount = outputCount; WaterParts = waterParts;
        IsAvailableFor = isAvailableFor ?? (_ => true);
        Preview = preview;
    }
}

public sealed class ExtraRecipe : RecipeView
{
    public int Priority { get; init; }
    public Action<GameItem, GameItem, GameItem[], int[]>? PrepareOutput { get; init; }

    public Func<bool> IsAvailable { get; }
    public Func<string> ResolveOutputId { get; }
    public string ChineseOutput { get; }
    public string EnglishOutput { get; }
    public Func<string>? LocalizedOutputName { get; init; }
    public ExtraRecipe(string id, string machineId, IEnumerable<IngredientView> ingredients, string representativeOutputId,
        string chineseOutput, string englishOutput, Func<bool> isAvailable, Func<string> resolveOutputId, int outputCount = 1,
        Func<GameItem, bool>? isAvailableFor = null, Func<GameItem, string>? preview = null)
        : base(id, machineId, ingredients, representativeOutputId, outputCount, isAvailableFor: isAvailableFor, preview: preview)
    {
        ChineseOutput = chineseOutput; EnglishOutput = englishOutput;
        IsAvailable = isAvailable ?? throw new ArgumentNullException(nameof(isAvailable));
        ResolveOutputId = resolveOutputId ?? throw new ArgumentNullException(nameof(resolveOutputId));
    }
}

