namespace Nicokobo.ContentInterop;

public static class ExtraRecipes
{
    private static readonly Dictionary<string, (string Owner, ExtraRecipe Recipe)> Entries = new(StringComparer.Ordinal);
    public static void Register(string owner, ExtraRecipe recipe)
    {
        ContentRuntime.RequireOwner(owner);
        if (Entries.TryGetValue(recipe.Id, out var old) && old.Owner != owner)
            throw new InvalidOperationException("Extra recipe ID belongs to another provider: " + recipe.Id);
        Entries[recipe.Id] = (owner, recipe);
    }
    public static IReadOnlyList<ExtraRecipe> ForMachine(string machineId) => Entries.Values
        .Where(entry => ContentRuntime.IsAttached(entry.Owner) && entry.Recipe.MachineId == machineId && entry.Recipe.IsAvailable())
        .OrderByDescending(entry => entry.Recipe.Priority).ThenBy(entry => entry.Recipe.Id, StringComparer.Ordinal).Select(entry => entry.Recipe).ToArray();
    public static IReadOnlyList<string> MachineIds() => Entries.Values
        .Where(entry => ContentRuntime.IsAttached(entry.Owner) && entry.Recipe.IsAvailable())
        .Select(entry => entry.Recipe.MachineId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    internal static void Remove(string owner)
    { foreach (string id in Entries.Where(entry => entry.Value.Owner == owner).Select(entry => entry.Key).ToArray()) Entries.Remove(id); }
}
