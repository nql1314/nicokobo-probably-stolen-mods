namespace Nicokobo.ContentInterop;
internal static class MachineSchedule
{
    private static readonly Dictionary<string, (string Owner, int Order)> Entries = new(StringComparer.Ordinal);
    internal static void Set(string owner, string machine, int order) => Entries[machine] = (owner, order);
    internal static int Order(string machine) => Entries.TryGetValue(machine, out var entry) ? entry.Order : -1;
    internal static void Remove(string owner)
    { foreach (var id in Entries.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray()) Entries.Remove(id); }
}
