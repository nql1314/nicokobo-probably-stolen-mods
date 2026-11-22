namespace Nicokobo.ContentInterop;
internal static class PowerConsumers
{
    private static readonly Dictionary<string, (string Owner, Func<GameItem, GameItem?> Source)> Entries = new(StringComparer.Ordinal);
    internal static void Register(string owner, string id, Func<GameItem, GameItem?> source)
    { ContentRuntime.RequireOwner(owner); Entries.Add(id, (owner, source)); }
    internal static bool Contains(string id) => Entries.ContainsKey(id);
    internal static GameItem? Source(GameItem item) => Entries.TryGetValue(item.identifier, out var entry) ? entry.Source(item) : null;
    internal static void Remove(string owner)
    { foreach (var id in Entries.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray()) Entries.Remove(id); }
}
