extern alias logistics;
using System.Reflection;
using System.Runtime.CompilerServices;
using LogisticsState = logistics::Nicokobo.AutomatedLogistics.Domain.AutomatedLogisticsState;
using RestockItem = logistics::Nicokobo.AutomatedLogistics.Domain.RestockItem;

internal static class LogisticsRuntimeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var runtimeType = typeof(logistics::Nicokobo.AutomatedLogistics.Plugin).Assembly
            .GetType("Nicokobo.AutomatedLogistics.Integration.AutomatedLogisticsRuntime", true)!;
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static;
        var materialState = runtimeType.GetMethod("ConsumedMaterialState", hidden)!;
        var matches = runtimeType.GetMethod("MatchesConsumed", hidden)!;
        var perfume = Item("neuroactive_perfume", 1);
        perfume.modifiedState.dict[UseCountHelper.USE_COUNT_ITEM_TAG] =
            new TagState(UseCountHelper.USE_COUNT_ITEM_TAG, UseCountHelper.USE_COUNT_ITEM_TAG).Enable().SetBool(true);
        var maximum = new TagState(UseCountHelper.MAX_USE_COUNT_INT, UseCountHelper.MAX_USE_COUNT_INT).Enable().SetInt(5);
        var remaining = new TagState(UseCountHelper.CURRENT_USE_COUNT_INT, UseCountHelper.CURRENT_USE_COUNT_INT).Enable().SetInt(5);
        perfume.modifiedState.dict[UseCountHelper.MAX_USE_COUNT_INT] = maximum;
        perfume.modifiedState.dict[UseCountHelper.CURRENT_USE_COUNT_INT] = remaining;
        var demand = new RestockItem { ItemId = perfume.identifier, MaterialState = (string)materialState.Invoke(null, [perfume])!, Remaining = 1, TargetCount = 1 };
        for (int uses = 5; uses >= 0; uses--)
        {
            remaining.SetInt(uses);
            check((bool)matches.Invoke(null, [demand, perfume])! == (uses == 5),
                "Restocking full perfume must preserve actual uses: " + uses + "/5");
        }
        remaining.SetInt(5); maximum.SetInt(6);
        check(!(bool)matches.Invoke(null, [demand, perfume])!, "Restocking must preserve the consumed maximum use count");
        remaining.SetInt(6);
        check(!(bool)matches.Invoke(null, [demand, perfume])!, "A different full-bottle specification cannot repay a recorded perfume demand");
        perfume.modifiedState.dict.Remove(UseCountHelper.USE_COUNT_ITEM_TAG);
        check(!(bool)matches.Invoke(null, [demand, perfume])!, "An untagged perfume cannot repay a full-bottle demand");

        // Existing v6 journals predate the use-count signature. Keep their
        // quantity and admit only a real full bottle for the perfume recipe.
        var previousDemand = new RestockItem { ItemId = perfume.identifier, Remaining = 2, TargetCount = 3 };
        perfume.modifiedState.dict[UseCountHelper.USE_COUNT_ITEM_TAG] =
            new TagState(UseCountHelper.USE_COUNT_ITEM_TAG, UseCountHelper.USE_COUNT_ITEM_TAG).Enable().SetBool(true);
        maximum.SetInt(5); remaining.SetInt(5);
        check((bool)matches.Invoke(null, [previousDemand, perfume])!, "A full bottle must repay an existing v6 perfume debt without a signature");
        remaining.SetInt(4);
        check(!(bool)matches.Invoke(null, [previousDemand, perfume])!, "A partial bottle cannot repay an existing v6 perfume debt");
        remaining.SetInt(0);
        check(!(bool)matches.Invoke(null, [previousDemand, perfume])!, "An exhausted bottle cannot repay an existing v6 perfume debt");
        maximum.SetInt(7); remaining.SetInt(7);
        check((bool)matches.Invoke(null, [previousDemand, perfume])!, "An old debt uses the real current maximum without inventing a historical maximum");
        previousDemand.Purity = 6;
        check(!(bool)matches.Invoke(null, [previousDemand, perfume])!, "An old perfume debt must retain its recorded purity requirement");
        previousDemand.Purity = 0; previousDemand.Quality = 1;
        check(!(bool)matches.Invoke(null, [previousDemand, perfume])!, "An old perfume debt must retain its recorded grade requirement");
        previousDemand.Quality = 0; maximum.SetInt(0); remaining.SetInt(0);
        check(!(bool)matches.Invoke(null, [previousDemand, perfume])!, "A zero-capacity bottle is not a full perfume bottle");
        maximum.SetInt(5); remaining.SetInt(5);
        perfume.modifiedState.dict.Remove(UseCountHelper.USE_COUNT_ITEM_TAG);
        check(!(bool)matches.Invoke(null, [previousDemand, perfume])!, "Missing use-count state cannot satisfy an old perfume debt");
        check(previousDemand.Remaining == 2 && previousDemand.TargetCount == 3 && previousDemand.MaterialState == "",
            "Matching an old perfume debt must preserve its pending quantity and source record");

        const string prefix = "nicokobo.automated_logistics.";
        string[] types = [prefix + "void_container", prefix + "void_tank", prefix + "void_grid"];
        string[] fields = ["_resourceRoot", "_waterRoot", "_powerRoot"];
        var state = new LogisticsState
        {
            TerminalGranted = true, ResourcesUnlocked = true, WaterUnlocked = true,
            ResourceInstanceId = 11, WaterInstanceId = 21,
            Resources = new() { Rows = 8, WaterCapacityParts = 20000 },
            Power = new() { Unlocked = true, InstanceId = 31, Width = 16, Height = 6, StoredEnergy = 123 }
        };
        var runtime = RuntimeHelpers.GetUninitializedObject(runtimeType);
        runtimeType.GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, state);
        var bind = runtimeType.GetMethod("BindNetworkRoots", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originals = types.Select((type, index) => Item(type, index * 10 + 11)).ToArray();
        var replacements = types.Select((type, index) => Item(type, index * 10 + 12)).ToArray();
        var alternatives = types.Select((type, index) => Item(type, index * 10 + 13)).ToArray();
        GameItem? Root(int index) => (GameItem?)runtimeType.GetField(fields[index], BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime);
        int[] SavedIds() => [state.ResourceInstanceId, state.WaterInstanceId, state.Power.InstanceId];
        bind.Invoke(runtime, [alternatives.Concat(replacements).Concat(originals).ToArray()]);
        for (int index = 0; index < types.Length; index++)
            check(ReferenceEquals(Root(index), originals[index]), "An available saved root must remain selected: " + types[index]);
        foreach (var original in originals) original.modifiedState.dict.Remove("IS_OWNED_TAG");
        bind.Invoke(runtime, [originals.Concat(alternatives).Concat(replacements).ToArray()]);
        for (int index = 0; index < types.Length; index++)
        {
            check(ReferenceEquals(Root(index), replacements[index]), "A lost/unowned root must yield to an owned same-type device: " + types[index]);
            check(SavedIds()[index] == replacements[index].uniqueId, "The replacement UID must persist: " + types[index]);
        }
        bind.Invoke(runtime, [Array.Empty<GameItem>()]);
        for (int index = 0; index < types.Length; index++)
            check(Root(index) == null, "No matching device must leave the network offline: " + types[index]);
        foreach (var replacement in replacements) replacement.parentInventory = null;
        bind.Invoke(runtime, [replacements.Concat(alternatives).ToArray()]);
        for (int index = 0; index < types.Length; index++)
            check(ReferenceEquals(Root(index), alternatives[index]), "A newly available attached device must replace a detached root: " + types[index]);
        check(state.TerminalGranted && state.ResourcesUnlocked && state.WaterUnlocked && state.Power.Unlocked &&
            state.Resources.Rows == 8 && state.Resources.WaterCapacityParts == 20000 && state.Power.Height == 6 && state.Power.StoredEnergy == 123,
            "Root replacement must retain grant receipts, shared capacity and stored power");
        var loaded = RuntimeHelpers.GetUninitializedObject(runtimeType);
        runtimeType.GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(loaded, state);
        bind.Invoke(loaded, [Enumerable.Reverse(alternatives).ToArray()]);
        for (int index = 0; index < types.Length; index++)
            check(ReferenceEquals(runtimeType.GetField(fields[index], BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(loaded), alternatives[index]),
                "A fresh runtime must honor the replacement UID: " + types[index]);
    }

    private static GameItem Item(string id, int uid)
    {
        var item = (GameItem)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
        item.identifier = id; item.uniqueId = uid; item.unitCount = 1;
        item.parentInventory = (GameInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory));
        item.modifiedState = new TagSystem();
        item.modifiedState.dict["IS_OWNED_TAG"] = new TagState("IS_OWNED_TAG", "IS_OWNED_TAG").Enable().SetBool(true);
        return item;
    }
}
