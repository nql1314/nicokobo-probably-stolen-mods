using System.Runtime.CompilerServices;
using DefinitelyStolen.Domain;

namespace DefinitelyStolen.Persistence;

internal static class CustodyRules
{
    private static readonly ConditionalWeakTable<NightState, HashSet<int>> OwnedIdsByNight = new();

    // Loot changes are published as a new night by the controller. Inventory
    // hooks can reuse its IDs without decoding every saved graph on each query.
    internal static HashSet<int> OwnedIds(NightState? night) => night is { Started: true, Ended: false }
        ? OwnedIdsByNight.GetValue(night, static current => current.Loot.SelectMany(x => GraphIdentity.Ids(x.Graph)).ToHashSet())
        : new();

    internal static void FinishReturn(NightState night, HashSet<int> returnedIds)
    {
        foreach (var item in night.Loot.Where(x => !returnedIds.Contains(x.UniqueId)).ToArray())
        { night.Loot.Remove(item); night.Abandoned.Add(item); }
        night.Ended = night.Started; night.Carry.Clear(); night.Outside = false;
    }

    internal static bool CanMove(NightState? night, HashSet<int> source, IEnumerable<int> destinationParents,
        bool carryDestination, bool emptyDestination)
    {
        var owned = OwnedIds(night);
        // Do not mix personal goods or another loot root into a stolen container.
        if (destinationParents.Any(owned.Contains)) return false;
        if (!source.Overlaps(owned)) return true;
        // Keep a stolen container's contents together until returning. Otherwise
        // extracting a child would remove its ownership from the next root graph.
        return carryDestination && emptyDestination && night!.Loot.Any(x => source.Contains(x.UniqueId));
    }
}
