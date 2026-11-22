using System.Collections;
using UnityEngine;

namespace Nicokobo.FoodProcessing;

internal sealed class FoodRecipeAvailability : MonoBehaviour
{
    internal void Refresh(FoodRecipeEntry[] entries, GameItem? machine, Action<Dictionary<string, bool>> complete)
    {
        StopAllCoroutines();
        StartCoroutine(Scan(entries, machine, complete));
    }

    // Unity inventories stay on the main thread. Yield before traversal so the window can render.
    private IEnumerator Scan(FoodRecipeEntry[] entries, GameItem? machine, Action<Dictionary<string, bool>> complete)
    {
        yield return null;
        yield return null;
        var ownedItems = new List<GameItem>();
        // Traverse the same shop roots as PlayerStore.FindAllItem, yielding through large nested inventories.
        if (PlayerStore.HasInstance && EmporiumEntry.Instance is { } shop)
        {
            var queue = new Queue<GraphNodeStorage>();
            foreach (var root in new GraphNodeStorage[] { shop.frontInvWindow, shop.backInvWindow,
                shop.backInvinvElementCounter, shop.invWindow, shop.showcaseWindow, shop.trashInvWindow,
                shop.docInvWindow, shop.hiddenWindow, shop.afterhourWindow, shop.vendingFountainWindow, shop.vendingMachineWindow })
                if (root != null) queue.Enqueue(root);
            var visited = new HashSet<GraphNodeStorage>();
            int processed = 0;
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (!visited.Add(node)) continue;
                if (node is GameItem item && item.IsTag("IS_OWNED_TAG")) ownedItems.Add(item);
                foreach (var child in node.children) queue.Enqueue(child);
                if (++processed % 64 == 0) yield return null;
            }
        }
        var owned = ownedItems.ToArray();
        var ranked = new List<(GameItem Item, int Priority)>();
        if (EmporiumEntry.Instance?.invElement is { } warehouse)
            for (int i = 0; i < owned.Length; i++)
            {
                int priority = FoodIngredientStock.SourcePriority(owned[i], warehouse);
                if (priority >= 0) ranked.Add((owned[i], priority));
                if (i % 64 == 63) yield return null;
            }
        var sources = ranked.OrderBy(value => value.Priority).Select(value => value.Item).ToArray();
        var machines = machine == null ? owned : new[] { machine };
        var result = new Dictionary<string, bool>();
        foreach (var entry in entries)
        {
            result[entry.Recipe.Id] = machines.Any(candidate => candidate.identifier == entry.Recipe.MachineId &&
                FoodIngredientStock.CanSupply(candidate, entry.Recipe, sources));
            yield return null;
        }
        complete(result);
    }
}
