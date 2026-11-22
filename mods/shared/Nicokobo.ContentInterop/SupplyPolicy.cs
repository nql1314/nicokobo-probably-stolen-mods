using Nicokobo.ModUtilities;

namespace Nicokobo.ContentInterop;

internal static class SupplyPolicy
{
    internal const string NativeMinerOre = "common_ore";
    internal const double NativeSupplyWeight = 1d;
    internal const double ModSupplyWeight = .25d;
    internal const double NativeMinerOreWeight = 7d / 3d;
    internal static double PerItemWeight(int count) => count > 0 ? ModSupplyWeight / count : 0d;

    internal static StockOffer? SelectDirect(IReadOnlyList<StockOffer> candidates, double sample)
    {
        if (double.IsNaN(sample) || double.IsInfinity(sample) || sample < 0 || sample >= 1 || candidates.Count == 0) return null;
        double remaining = sample * (NativeSupplyWeight + ModSupplyWeight) - NativeSupplyWeight;
        if (remaining < 0) return null;
        return candidates[Math.Min((int)(remaining / PerItemWeight(candidates.Count)), candidates.Count - 1)];
    }
    internal static string SelectOre(IReadOnlyList<StockOffer> candidates, double sample)
    {
        if (double.IsNaN(sample) || double.IsInfinity(sample) || sample < 0 || sample >= 1)
            throw new ArgumentOutOfRangeException(nameof(sample));
        double remaining = sample * (NativeMinerOreWeight + candidates.Sum(offer => (double)(offer.MinerWeight ?? offer.Weight)));
        if (remaining < NativeMinerOreWeight) return NativeMinerOre;
        remaining -= NativeMinerOreWeight;
        foreach (var offer in candidates)
        {
            remaining -= offer.MinerWeight ?? offer.Weight;
            if (remaining < 0) return offer.ItemId;
        }
        throw new InvalidOperationException("Miner stock selection exceeded its available pool");
    }
    internal static StockCategory TableCategories(string? table) => table switch
    {
        "materialTable" => StockCategory.Material | StockCategory.Ore,
        "allModuleTable" or "t1moduleTable" or "t2moduleTable" => StockCategory.Module,
        "toolTable" => StockCategory.Machine,
        "householdTable" => StockCategory.Household,
        "packedFoodTable" => StockCategory.Food,
        "medicalTable" => StockCategory.Medical,
        _ => StockCategory.None
    };
}

internal sealed class SupplySession
{
    internal readonly StockCategory Categories;
    internal readonly StockSupplier Supplier;
    internal readonly bool Miner, NightShop, RepeatPoolDraw;
    internal bool Attempted;
    internal string? Ore;
    internal readonly HashSet<GameItem> PoolItems = new();
    internal readonly HashSet<GameItem> RandomModules = new();
    internal readonly Dictionary<string, int> Draws = new(StringComparer.Ordinal);
    internal readonly HashSet<string> RepeatedDraws = new(StringComparer.Ordinal);
    internal SupplySession(StockCategory categories, StockSupplier supplier = StockSupplier.Any, bool miner = false, bool nightShop = false, bool repeatPoolDraw = false)
    { Categories = categories; Supplier = supplier; Miner = miner; NightShop = nightShop; RepeatPoolDraw = repeatPoolDraw; }
}

internal static class SupplyEngine
{
    internal static string? Roll(SupplySession session, string? table, string? itemId, List<LootEntry> entries)
    {
        // Keep a result supplied by an earlier callback outside this table.
        if (entries.Any(entry => entry.id == itemId))
        {
            var merged = Merge(session, table, entries);
            if (ReferenceEquals(merged, entries)) return itemId;
            double total = merged.Where(entry => entry.weight > 0).Sum(entry => (double)entry.weight);
            if (total > 0)
            {
                double remaining = RNG.GetRandomDouble(0, 1) * total;
                string? selected = null;
                foreach (var entry in merged)
                {
                    if (!(entry.weight > 0)) continue;
                    selected = entry.id;
                    remaining -= entry.weight;
                    if (remaining < 0) break;
                }
                return selected;
            }
            return null;
        }
        return itemId;
    }

    internal static List<LootEntry> Merge(SupplySession session, string? table, List<LootEntry> entries)
    {
        var category = SupplyPolicy.TableCategories(table) & session.Categories;
        if (session.Miner || category == StockCategory.None) return entries;
        var offers = SupplyCatalog.Available(category, session.Supplier, session.NightShop);
        var retained = offers.Where(offer => offer.PreserveLootWeight).Select(offer => offer.ItemId).ToHashSet(StringComparer.Ordinal);
        // Only a draw-local copy is edited. Preserve unrelated mods and the ores'
        // actual native-table weights, rather than giving each provider .25.
        var merged = entries.Where(entry => entry != null &&
            ((!SupplyCatalog.HasProvider(entry.sourceModId) && !SupplyCatalog.IsDeclared(entry.id)) || retained.Contains(entry.id)))
            .ToList();
        var budget = offers.Where(offer => !offer.PreserveLootWeight).ToArray();
        if (budget.Length == 0 && merged.Count == entries.Count) return entries;
        double retainedWeight = merged.Where(entry => entry.weight > 0).Sum(entry => (double)entry.weight);
        float weight = (float)(retainedWeight * SupplyPolicy.PerItemWeight(budget.Length));
        if (weight <= 0) return merged;
        foreach (var offer in budget) merged.Add(new LootEntry(offer.ItemId, weight, offer.Owner));
        return merged;
    }
    internal static void Rolled(SupplySession session, string? table, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || (SupplyPolicy.TableCategories(table) & session.Categories) == 0) return;
        session.Draws[id!] = session.Draws.TryGetValue(id!, out int count) ? count + 1 : 1;
        if (session.RepeatPoolDraw) session.RepeatedDraws.Add(id!);
    }
    internal static void PoolItem(SupplySession session, GameItem? item)
    {
        if (item == null || !session.Draws.TryGetValue(item.identifier, out int count)) return;
        if (count == 1) session.Draws.Remove(item.identifier); else session.Draws[item.identifier] = count - 1;
        session.PoolItems.Add(item);
    }
    internal static void Spawned(SupplySession session, string id, GameItem? item)
    {
        // Spawn aliases can resolve to a different actual identifier. Consume the
        // source token before examining the resulting native module/node ID.
        if (item != null && (session.RepeatedDraws.Contains(id) || session.Draws.TryGetValue(id, out int count) && count > 0))
        {
            session.PoolItems.Add(item);
            if (session.Draws.TryGetValue(id, out int pending))
            { if (pending == 1) session.Draws.Remove(id); else session.Draws[id] = pending - 1; }
        }
        else PoolItem(session, item);
        if (item != null && id is "random_performance_module" or "random_efficiency_module" or "random_quality_module" or
            "random_overclock_module" or "random_eco_module" or "random_fineness_module" or "random_node") session.RandomModules.Add(item);
    }

    internal static bool Replace(SupplySession session, ref GameItem original, bool isOwned)
    {
        if (isOwned || original == null || original.parentInventory != null || original.unitCount <= 0) return true;
        if (session.Miner) return Miner(session, ref original);
        if (session.PoolItems.Remove(original) || SupplyCatalog.IsDeclared(original.identifier)) return true;
        var category = DirectCategory(session, original) & session.Categories;
        if (category == StockCategory.None) return true;
        var selected = SupplyPolicy.SelectDirect(SupplyCatalog.Available(category, session.Supplier, session.NightShop), RNG.GetRandomDouble(0, 1));
        if (selected == null) return true;
        GameItem? replacement = null;
        try
        {
            replacement = Create(selected.ItemId, original);
            var previous = original; original = replacement; Destroy(previous);
            return true;
        }
        catch (Exception error)
        {
            if (replacement != null && replacement != original) Destroy(replacement);
            Warn("Native stock retained after replacement failure: " + error.Message);
            return true;
        }
    }
    private static bool Miner(SupplySession session, ref GameItem original)
    {
        if (original.identifier != SupplyPolicy.NativeMinerOre) return true;
        if (!session.Attempted)
        {
            session.Attempted = true;
            session.Ore = SupplyPolicy.SelectOre(SupplyCatalog.Available(StockCategory.Ore, session.Supplier, session.NightShop), RNG.GetRandomDouble(0, 1));
        }
        if (session.Ore == SupplyPolicy.NativeMinerOre) return true;
        GameItem? replacement = null;
        try
        {
            replacement = Create(session.Ore ?? throw new InvalidOperationException("Miner batch selection missing"), original);
            var previous = original; original = replacement; Destroy(previous);
            return true;
        }
        catch (Exception error)
        {
            if (replacement != null && replacement != original) Destroy(replacement);
            Destroy(original);
            Warn("Selected miner batch item could not be created; slot skipped: " + error.Message);
            return false;
        }
    }
    private static GameItem Create(string id, GameItem original)
    {
        GameItem? replacement = null;
        try
        {
            replacement = DirectoryMaster.Item(id, true);
            if (replacement == null || replacement == original || replacement.identifier != id || replacement.parentInventory != null || replacement.unitCount <= 0)
                throw new InvalidOperationException("NPC replacement factory contract failed: " + id);
            if (replacement.unitCount != original.unitCount) replacement.SetUnitCount(original.unitCount);
            if (replacement.unitCount != original.unitCount) throw new InvalidOperationException("NPC replacement quantity mismatch: " + id);
            return replacement;
        }
        catch
        {
            if (replacement != null && replacement != original) Destroy(replacement);
            throw;
        }
    }
    internal static StockCategory DirectCategory(SupplySession session, GameItem item)
    {
        if (new[] { "WEAPON", "AMMUNITION", "ARMOR", "TOOL", "MACHINE", "STORAGE", "ACCESS_CARD", "DOCUMENT" }.Any(item.IsGameItemType) ||
            new[] { "IMPORTANT_TAG", "NOT_FOR_RESALE", "CONTAINER_TAG", "LIQUID_CONTAINER_TAG" }.Any(item.IsTag)) return StockCategory.None;
        if (item.IsGameItemType("MODULE") || item.IsGameItemType("NODE"))
            return session.RandomModules.Remove(item) ? StockCategory.Module : StockCategory.None;
        if (item.IsGameItemType("MEDICAL")) return StockCategory.Medical;
        if (item.IsGameItemType("FOOD") || item.IsGameItemType("PROCESSED_FOOD") || item.IsGameItemType("BREVAGE")) return StockCategory.Food;
        if (item.IsGameItemType("MATERIAL")) return StockCategory.Material | StockCategory.Ore;
        if (item.IsGameItemType("HOUSEHOLD_GOOD") || item.IsGameItemType("LUXURY_ITEM")) return StockCategory.Household;
        return StockCategory.None;
    }
    internal static void Destroy(GameItem item)
    { try { if (item.parentInventory == null) ItemGraphCleanup.Destroy(item); } catch (Exception error) { Warn("Unused NPC stock cleanup failed: " + error.Message); } }
    internal static void Warn(string message) => UnityEngine.Debug.LogWarning("[ContentInterop/Supply] " + message);
}
