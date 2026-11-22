namespace Nicokobo.ContentInterop;

// Routes an actual installed power source to its resource owner. A provider
// must recognize a source without changing it, and commit a debit atomically.
public static class DevicePower
{
    private sealed class Provider
    {
        internal Func<GameItem, int?> Query = null!;
        internal Func<GameItem, int, bool?> Debit = null!;
    }

    private static readonly Dictionary<string, Provider> Providers = new(StringComparer.Ordinal);

    public static void Register(string owner, Func<GameItem, int?> query, Func<GameItem, int, bool?> debit)
    {
        ContentRuntime.RequireOwner(owner);
        Providers[owner] = new Provider
        {
            Query = query ?? throw new ArgumentNullException(nameof(query)),
            Debit = debit ?? throw new ArgumentNullException(nameof(debit))
        };
    }

    public static void RegisterConsumer(string owner, string id, Func<GameItem, GameItem?> source) => PowerConsumers.Register(owner, id, source);

    public static bool TryEnergy(GameItem source, out int energy)
        => Find(source, out energy) != null;

    public static bool TryDebit(GameItem source, int amount, out bool success)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var provider = Find(source, out int available);
        success = false;
        if (provider == null) return false;
        if (available < amount) return true;
        bool? result = provider.Debit(source, amount);
        if (!result.HasValue)
            throw new InvalidOperationException("The power provider did not handle its recognized source");
        success = result.Value;
        return true;
    }

    private static Provider? Find(GameItem source, out int energy)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        Provider? selected = null;
        energy = 0;
        foreach (var provider in Providers.Values)
        {
            int? value = provider.Query(source);
            if (!value.HasValue) continue;
            if (value.Value < 0) throw new InvalidOperationException("Power availability cannot be negative");
            if (selected != null)
                throw new InvalidOperationException("Multiple power providers claim the same source");
            selected = provider;
            energy = value.Value;
        }
        return selected;
    }

    internal static void Remove(string owner) { Providers.Remove(owner); PowerConsumers.Remove(owner); }
}
