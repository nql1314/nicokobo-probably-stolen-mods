using DefinitelyStolen.Persistence;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Nicokobo.ModUtilities;

namespace DefinitelyStolen.Integration;

internal static class NativeGraph
{
    internal static IReadOnlyList<GameItem> Items(GameItem root)
    {
        var result = new List<GameItem>(); var seen = new HashSet<GraphNodeStorage>(); var queue = new Queue<GraphNodeStorage>(); queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue(); if (!seen.Add(node)) throw new InvalidDataException("Cyclic or multiply owned native item graph");
            if (node is GameItem item) result.Add(item);
            foreach (var child in node.children) queue.Enqueue(child);
        }
        return result;
    }
    internal static IReadOnlyList<GameItem> RunItems(PlayerStore store) => EmporiumEntry.Instance.GetAllItems()
        .Concat(NativeCarry.Items().SelectMany(Items)).Distinct().ToArray();
    internal static string Encode(GameItem root)
    {
        foreach (var item in Items(root)) item.GetUniqueID();
        var store = Native.Store ?? throw new InvalidOperationException("Cannot capture graph without a run");
        var original = store.savedItemFeatureList;
        try
        {
            store.savedItemFeatureList = new List<ItemFeature>();
            var state = new SaveState { saveItems = SaveManager.EncodeNodes(root) };
            var data = JObject.Parse(JsonUtility.ToJson(state));
            // ES3 owns all private feature fields. Serialize is a memory-only API.
            data["forgeItemFeatures"] = Convert.ToBase64String(ES3.Serialize(store.savedItemFeatureList, new ES3Settings(false)));
            return data.ToString(Newtonsoft.Json.Formatting.None);
        }
        finally { store.savedItemFeatureList = original; }
    }
    internal static GameItem Decode(string json, HashSet<int>? existing = null, bool frozenCandidate = false)
    {
        var identity = GraphIdentity.Nodes(json);
        if (existing != null && identity.Keys.Any(existing.Contains)) throw new InvalidDataException("Saved graph conflicts with live inventory");
        var data = JObject.Parse(json);
        var state = JsonUtility.FromJson<SaveState>(json) ?? throw new InvalidDataException("Invalid native graph");
        ValidateLinks(state);
        GameItem? result = null;
        try
        {
            result = SaveManager.DecodeNodes(state.saveItems);
            if (result == null) throw new InvalidDataException("Native graph produced no root");
            var actual = Items(result);
            if (actual.Count != identity.Count || actual.Any(x => !identity.TryGetValue(x.uniqueId, out var saved) || saved != (x.identifier, x.unitCount)))
                throw new InvalidDataException("Native graph changed its contents on decode");
            if (data.Value<string>("forgeItemFeatures") is { } features)
            {
                var byId = actual.ToDictionary(x => x.uniqueId);
                var restored = ES3.Deserialize<List<ItemFeature>>(Convert.FromBase64String(features), new ES3Settings(false));
                if (restored == null || restored.Any(x => !byId.ContainsKey(x.parentItemUniqueId))) throw new InvalidDataException("Feature refers outside saved graph");
                foreach (var item in actual) item.RemoveAllFeature();
                foreach (var feature in restored) byId[feature.parentItemUniqueId].AddItemFeature(feature);
            }
            foreach (var item in actual)
            {
                MachineHelper.LoadUpgrade(item); SpriteHelper.UpdateWaterContainerSprite(item); GeneralHelper.LoadLockItem(item);
                PlantTraitList.LoadEdibleProduce(item); PlantTraitList.LoadTasteFeature(item);
                GeneralHelper.LoadNonOwnedToolTag(item); GeneralHelper.LoadHazardousWasteTag(item); LockHelper.LoadLockedContainer(item);
                MachineBrokenHelper.LoadBrokenMachine(item); MachineHelper.OnLoad(item); MachineTurboBoosterAdv.LoadTurboBoostEligibleTag(item);
                CurrentConverterHelper.OnLoad(item); item.onLoaded?.Invoke(item);
                // The native seed callback reprices it after strain liberation.
                // Detached candidates retain their recorded price until delivery;
                // ordinary owned-item loading still uses the native callback.
                if (frozenCandidate && item.identifier == "nutrifruit_seed")
                    item.unitValue = state.saveItems.Single(node => node.uniqueId == item.uniqueId).unitValue;
            }
            return result;
        }
        catch
        {
            if (result != null) ItemGraphCleanup.Destroy(result);
            foreach (var node in state.saveItems)
                if (node.tempLink is { } item && Native.Alive(item) && item.parentInventory == null) ItemGraphCleanup.Destroy(item);
            throw;
        }
    }
    private static void ValidateLinks(SaveState state)
    {
        if (state.saveItems == null || state.saveItems.Count == 0) throw new InvalidDataException("Empty graph");
        var uuids = state.saveItems.Select(x => x.uuid).ToHashSet();
        if (uuids.Count != state.saveItems.Count || state.saveItems.Any(x => !DirectoryMaster.Has<GameItem>(x.identifier) || x.childItems.Count != x.childItemInventoryNode.Count || x.childItemInventoryNode.Any(i => i < 0)))
            throw new InvalidDataException("Invalid graph definitions or inventory links");
        var children = state.saveItems.SelectMany(x => x.childItems).ToArray();
        if (children.Length != state.saveItems.Count - 1 || children.Distinct().Count() != children.Length || children.Any(x => !uuids.Contains(x)) || children.Contains(state.saveItems[0].uuid))
            throw new InvalidDataException("Graph does not have one connected root");
        var reachable = new HashSet<long>(); var queue = new Queue<long>(); queue.Enqueue(state.saveItems[0].uuid);
        while (queue.Count > 0) { long id = queue.Dequeue(); if (!reachable.Add(id)) throw new InvalidDataException("Graph cycle"); foreach (long child in state.saveItems.Single(x => x.uuid == id).childItems) queue.Enqueue(child); }
        if (!reachable.SetEquals(uuids)) throw new InvalidDataException("Unreachable native graph nodes");
    }
}
