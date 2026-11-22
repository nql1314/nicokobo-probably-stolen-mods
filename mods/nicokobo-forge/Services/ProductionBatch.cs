using Nicokobo.ModUtilities;

namespace Nicokobo.ContentInterop;

public static class ProductionBatch
{
    public static List<BatchInput>? SelectInputs(GameGridInventory inventory, IEnumerable<IngredientView> rules)
    {
        var available = inventory.childItems.ToDictionary(item => item, item => item.unitCount);
        var selected = new Dictionary<GameItem, int>();
        foreach (var rule in rules)
        {
            int needed = rule.Amount;
            foreach (var item in inventory.childItems)
            {
                if (!rule.Matches(item)) continue;
                int count = Math.Min(needed, available[item]);
                if (count <= 0) continue;
                selected[item] = selected.TryGetValue(item, out int old) ? checked(old + count) : count;
                available[item] -= count; needed -= count;
                if (needed == 0) break;
            }
            if (needed != 0) return null;
        }
        return selected.Select(pair => new BatchInput(pair.Key, pair.Value, pair.Key.unitCount)).ToList();
    }
    public static bool Run(GameItem battery, GameGridInventory input, GameGridInventory output,
        IReadOnlyList<BatchInput> takes, int outputCount, Func<string> outputId,
        Action<GameItem, int> prepare, int power, GameItem? water = null, int waterParts = 0)
    {
        if (outputCount <= 0 || power < 0 || waterParts < 0 || waterParts > 0 && water == null)
            throw new ArgumentException("Invalid batch resources");
        var products = new List<GameItem>();
        var detached = new List<GameItem>();
        var waterState = waterParts == 0 ? null : water!.state.Clone();
        var batteryState = battery.state.Clone();
        bool committed = false;
        var admission = output.mayInventoryAddItemFunc;
        output.mayInventoryAddItemFunc = (_, _) => true;
        try
        {
            // All output must fit before any input is consumed. Keep the new
            // items separate so rollback can remove precisely this batch.
            for (int index = 0; index < outputCount; index++)
            {
                string id = outputId();
                if (string.IsNullOrWhiteSpace(id) || !DirectoryMaster.Has<GameItem>(id))
                    throw new InvalidOperationException("Output factory unavailable: " + id);
                var product = DirectoryMaster.Item(id) ?? throw new InvalidOperationException("Output factory unavailable: " + id);
                products.Add(product);
                if (product.identifier != id || product.unitCount != 1 || product.parentInventory != null)
                    throw new InvalidOperationException("Invalid product factory result: " + id);
                product.mayStackItemFunc = null;
                prepare(product, index);
                if (GraphUtils.TryAcceptOnce(output, product) != 0 || product.parentInventory != output) return false;
            }
            foreach (var take in takes)
            {
                if (take.Item.parentInventory != input || take.Item.unitCount != take.Before)
                    throw new InvalidOperationException("Input changed during batch");
                if (take.Count == take.Before)
                {
                    if (!input.Expel(take.Item) || take.Item.parentInventory != null)
                        throw new InvalidOperationException("Input removal failed");
                    detached.Add(take.Item);
                }
                else take.Item.SetUnitCount(take.Before - take.Count);
            }
            if (waterParts != 0)
            {
                int before = WaterHelper.GetTotalVolume(water!);
                WaterHelper.Remove(water!, waterParts);
                if (WaterHelper.GetTotalVolume(water!) != before - waterParts)
                    throw new InvalidOperationException("Water debit mismatch");
            }
            // Drawing power is the last operation that can reject the batch.
            if (!(DevicePower.TryDebit(battery, power, out bool paid) ? paid : PowerHelper.DrawPowerSource(battery, power))) return false;
            committed = true;
        }
        finally
        {
            output.mayInventoryAddItemFunc = admission;
            if (!committed)
            {
                var failures = new List<Exception>();
                void Restore(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
                foreach (var product in products) Restore(() => ItemGraphCleanup.Destroy(product));
                foreach (var take in takes)
                    Restore(() =>
                    {
                        if (take.Item.unitCount != take.Before) take.Item.SetUnitCount(take.Before);
                        if (take.Item.parentInventory == null && !input.UncheckedAccept(take.Item))
                            throw new InvalidOperationException("Failed to restore batch input: " + take.Item.identifier);
                        if (take.Item.parentInventory != input || take.Item.unitCount != take.Before)
                            throw new InvalidOperationException("Batch input restore mismatch");
                    });
                if (waterState != null) Restore(() =>
                {
                    water!.state = waterState; water.SyncModifiedState();
                    WaterFeatureHelper.OnLiquidContainerPourOutOf(water);
                });
                Restore(() => { battery.state = batteryState; battery.SyncModifiedState(); PowerHelper.UpdateEnergyCreditSprite(battery); });
                if (failures.Count != 0) throw new AggregateException("Batch rollback incomplete", failures);
            }
        }
        foreach (var item in detached)
            try { ItemGraphCleanup.Destroy(item); } catch (Exception error) { UnityEngine.Debug.LogWarning("[Forge] Consumed item cleanup failed: " + error); }
        return true;
    }
}
