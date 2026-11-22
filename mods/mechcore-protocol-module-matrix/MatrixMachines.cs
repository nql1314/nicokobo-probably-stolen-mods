using System.Runtime.CompilerServices;
using HarmonyLib;
using MechcoreProtocol.ModuleMatrix.Definitions;
using MechcoreProtocol.ModuleMatrix.Domain;

namespace MechcoreProtocol.ModuleMatrix;

internal static class MatrixMachines
{
    internal const string CacheTag = "nicokobo.mechcore_protocol.module_matrix.s09.v1.cache0";
    internal const string MarketDayTag = "nicokobo.mechcore_protocol.module_matrix.s25.v1.last_attempt_day";
    private sealed class Configured { }
    private static readonly ConditionalWeakTable<GameItem, Configured> ConfiguredMachines = new();
    internal static GameGridInventory? Modules(GameItem machine)
    {
        // Modded standard machines can use a custom layout without the native module cell at (1, 1).
        if (machine.contentWindow?.childElement is GridPixelElement { gridWidth: < 2 } or GridPixelElement { gridHeight: < 2 })
            return null;
        return machine.IsTag("STANDARD_MACHINE_TAG") || machine.identifier == "evaporator" || MachineModuleDefinitions.All.Any(def => def.MachineId == machine.identifier)
            ? MachineHelper.GetBodyStorage(machine, 1) as GameGridInventory : null;
    }
    internal static IEnumerable<GameItem> Installed(GameItem machine, string code)
    {
        if (!MatrixMod.Active || !MatrixMod.Settings.Machines || !GeneralHelper.IsItemOwned(machine) || machine.unitCount <= 0) return Array.Empty<GameItem>();
        var definition = MachineModuleDefinitions.All.Single(def => def.Code == code);
        if (definition.Retired || machine.identifier != definition.MachineId || Modules(machine) is not { } grid) return Array.Empty<GameItem>();
        return grid.childItems.Where(item => item.unitCount > 0 && GeneralHelper.IsItemOwned(item) && item.parentInventory == grid &&
            item.identifier == definition.Id && item.GetTagReadonly("MODULE_TYPE").GetString() == definition.ModuleType);
    }
    internal static void Configure(GameItem machine)
    {
        if (!MatrixMod.Active || Modules(machine) is not { } grid || ConfiguredMachines.TryGetValue(machine, out _)) return;
        var admission = grid.mayInventoryAddItemFunc;
        grid.mayInventoryAddItemFunc = (candidate, target) =>
        {
            var specific = MachineModuleDefinitions.All.FirstOrDefault(def => def.Id == candidate.identifier);
            if (specific != null) return MatrixMod.Active && MatrixMod.Enabled(specific.Id) && candidate.unitCount > 0 &&
                GeneralHelper.IsItemOwned(candidate) && GeneralHelper.IsItemOwned(machine) && target == grid && specific.MachineId == machine.identifier;
            return admission != null && admission.InvokeAllReduce(candidate, target, InvokeFuncExtensions.BoolReduceOR);
        };
        if (machine.identifier == "moisture_farm")
        {
            var cycle = machine.onCycleEndSlotItemFunc;
            machine.onCycleEndSlotItemFunc = (item, inventory, slot) =>
            {
                // A full native bottle skips Fill, including its supply
                // postfix. Drain stored water before that capacity check.
                if (MatrixMod.Active && MachineHelper.GetBodyStorage(item, 2) is GameSlotInventory { childItem: { } water })
                    Supply(water, WaterHelper.GetTotalVolume(water));
                cycle?.Invoke(item, inventory, slot);
            };
        }
        ConfiguredMachines.Add(machine, new());
    }
    internal static void Recompute(GameItem machine)
    {
        if (Modules(machine) is not { } grid || !GeneralHelper.IsItemOwned(machine)) return;
        ModuleEffectHelper.ComputeAllModuleTempStat(grid.childItems);
        ModuleHelper.ComputeModuleEffect(grid, null, machine);
        ModuleHelper.ApplyBasicModuleEffect(grid, null, machine);
    }
    internal static int Purify(GameItem machine, GameItem container, bool ignoreBonus = false)
    {
        int stacks = Installed(machine, "S24").Count();
        if (stacks == 0 || !container.IsTag("LIQUID_CONTAINER_TAG")) return 0;
        int total = WaterHelper.GetTotalVolume(container);
        int water = container.GetTagReadonly(Liquid.GetCurrentPartFromId("water")).GetInt();
        if (total <= 0 || water <= 0) return 0;
        int cap = MachinePurifier.GetMaxPurityCap(ignoreBonus ? 0 : MachineryHelper.GetCurrentQualityBonus(machine));
        // Match the native total-volume contaminant floor after its own removal.
        // Unsupported liquids still count toward this shared purity allowance.
        int allowance = total - water - (int)((long)total * (10000 - cap) / 10000);
        if (allowance <= 0) return 0;
        int removed = 0;
        foreach (var liquid in Liquid.Liquids.Where(liquid => liquid.identifier != "water" && liquid.canBeRemovedByPurifier))
        {
            if (removed >= allowance) break;
            int present = container.GetTagReadonly(Liquid.GetCurrentPartFromId(liquid.identifier)).GetInt();
            if (present <= 0) continue;
            int amount = Math.Min(OsmosisRules.ExtraRemoval(present, stacks), allowance - removed);
            removed = checked(removed + WaterHelper.RemoveContaminantFromContainer(container, liquid.identifier, amount, 0));
        }
        if (removed > 0) WaterHelper.AddLiquid(container, "water", removed);
        return removed;
    }
    internal static void PrepareWaterSupply()
    {
        if (!MatrixMod.Active || !MatrixMod.Settings.Machines || MatrixMod.Store is not { } store) return;
        // Native shared-air counting must observe every S06 bottle after the
        // same pre-drain stage, including farms whose cycle runs later.
        foreach (var farm in store.FindAllItem().Where(item => item.identifier == "moisture_farm"))
            if (MachineHelper.GetBodyStorage(farm, 2) is GameSlotInventory { childItem: { } water })
                Supply(water, WaterHelper.GetTotalVolume(water));
    }
    internal static void Supply(GameItem container, int volume)
    {
        var farm = container.parentInventory?.GetParentItem();
        if (volume <= 0 || farm == null || !Installed(farm, "S06").Any() || farm.parentInventory is not GameGridInventory grid) return;
        var neighbors = grid.childItems.Where(item => item != farm && GeneralHelper.IsItemOwned(item) && MatrixGeometry.Adjacent(farm, item))
            .OrderBy(item => item.modifiedShape.minY).ThenBy(item => item.modifiedShape.minX).ThenBy(item => item.uniqueId).ToArray();
        foreach (var purifier in neighbors.Where(item => item.identifier == "water_purifier"))
        foreach (var target in purifier.FindAllChildItems().Where(item => item.parentInventory?.GetParentItem() == purifier)) Transfer(container, target);
        foreach (var target in neighbors) Transfer(container, target);
    }
    private static void Transfer(GameItem source, GameItem target)
    {
        if (target == source || !target.IsTag("LIQUID_CONTAINER_TAG") || !GeneralHelper.IsItemOwned(target) ||
            WaterHelper.GetFreeCapacity(target) <= 0 || WaterHelper.GetTotalVolume(source) <= 0) return;
        WaterHelper.TransferLiquid(source, target);
    }
    internal static void Market()
    {
        var store = MatrixMod.Store;
        if (store == null || !MatrixMod.Settings.Machines) return;
        var alarms = store.FindAllItem().Where(item => item.identifier == "security_alarm" && !item.IsTag("BROKEN_MACHINE_TAG") && Installed(item, "S25").Any()).ToArray();
        if (alarms.Length == 0) return;
        var trackers = alarms.SelectMany(alarm => Installed(alarm, "S25")).ToArray();
        int day = StoreStation.GetDayCounter();
        if (day < 0 || trackers.All(item => item.IsTag(MarketDayTag) && item.GetTagReadonly(MarketDayTag).GetInt() == day)) return;
        foreach (var tracker in trackers) tracker.ModifyTag(MarketDayTag, tag => tag.Enable().SetInt(day));
        float chance = Math.Clamp(ModuleMatrixNumbers.Effects.MarketTrackingBaseChancePercent *
            (1f + alarms.Max(MachineryHelper.GetCurrentPerformanceBonus) / 100f), 0f, 100f);
        if (!RNG.Roll(chance))
        { store.AddNightLog(MatrixText.Get("matrix.market_tracking_no_information_obtained_tonight"), "#808b98"); return; }
        var modifiers = store.GetMarketModifierType(TypeHelper.MAJOR_TYPES_LIST);
        if (modifiers.Count == 0)
        { store.AddNightLog(MatrixText.Get("matrix.market_tracking_no_active_market_modifiers"), "#58c4d6"); return; }
        var chosen = modifiers[RNG.GetRandomIntExculsive(0, modifiers.Count)];
        store.AddNightLog(MatrixText.Get("matrix.market_tracking") + TypeHelper.GetTypeDisplayName(chosen.Key) +
            $" {(chosen.Value >= 0 ? "+" : "")}{chosen.Value}", "#58c4d6");
    }
    internal static bool CacheHit(GameItem machine)
    {
        string target = machine.GetTagReadonly("DESEQUENCER_TARGET_ID_TAG").GetString();
        var module = Installed(machine, "S09").FirstOrDefault();
        return !string.IsNullOrWhiteSpace(target) && module != null && module.GetTagReadonly(CacheTag).GetString() == target;
    }
    internal static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        if (item.identifier != MachineModuleDefinitions.All.Single(def => def.Code == "S09").Id) return;
        string target = item.GetTagReadonly(CacheTag).GetString();
        builder.AddLine(MatrixText.Get("matrix.cached_target") + (string.IsNullOrWhiteSpace(target) ? MatrixText.Get("matrix.none") : target));
    }
}

[HarmonyPatch(typeof(MachinePurifier), nameof(MachinePurifier.PurifyContainer))]
internal static class MatrixOsmosisPatch
{
    private static void Postfix(GameItem __0, GameItem __1, bool __2, ref int __result)
    { if (MatrixMod.Active) __result = checked(__result + MatrixMachines.Purify(__0, __1, __2)); }
}
[HarmonyPatch(typeof(MachineMoistureFarm), nameof(MachineMoistureFarm.Fill))]
internal static class MatrixWaterSupplyPatch
{
    private static void Postfix(GameItem __1, int __2) { if (MatrixMod.Active) MatrixMachines.Supply(__1, __2); }
}
[HarmonyPatch(typeof(MachineMoistureFarm), "GetRunningFarmCountTonight")]
internal static class MatrixWaterSupplyCountPatch
{
    private static void Prefix() => MatrixMachines.PrepareWaterSupply();
}
[HarmonyPatch(typeof(MachineDesequencer), nameof(MachineDesequencer.FinishDesequencer))]
internal static class MatrixDecodeFinishPatch
{
    internal sealed record Before(GameItem? Input, GameItem? Output, string Target);
    private static void Prefix(GameItem __0, GameSlotInventory __1, GameSlotInventory __2, out Before __state) =>
        __state = new(__1.childItem, __2.childItem, __0.GetTagReadonly("DESEQUENCER_TARGET_ID_TAG").GetString());
    private static void Postfix(GameItem __0, GameSlotInventory __1, GameSlotInventory __2, Before __state)
    {
        if (!MatrixMod.Active || __state.Input == null || __state.Input.parentInventory == __1 ||
            __2.childItem is not { } product || product == __state.Output || product.identifier != __state.Target || product.unitCount <= 0) return;
        MatrixMachines.Installed(__0, "S09").FirstOrDefault()?.ModifyTag(MatrixMachines.CacheTag, tag => tag.Enable().SetString(__state.Target));
    }
}
[HarmonyPatch(typeof(MachineProgressHelper), nameof(MachineProgressHelper.ContinueProgressTypeMachine))]
internal static class MatrixDecodeProgressPatch
{
    private static void Prefix(GameItem __0, out int __state)
    {
        __state = -1;
        if (!MatrixMod.Active || !MatrixMachines.CacheHit(__0)) return;
        int speed = __0.GetTagReadonly("CURRENT_PROCESSING_SPEED_TAG").GetInt();
        if (speed <= 0) return;
        __state = speed;
        __0.ModifyTag("CURRENT_PROCESSING_SPEED_TAG", tag => tag.SetInt((int)Math.Min(int.MaxValue, (long)speed * ModuleMatrixNumbers.Effects.DecodingCacheSpeedMultiplier)));
    }
    private static Exception? Finalizer(GameItem __0, int __state, Exception? __exception)
    {
        if (__state >= 0) __0.ModifyTag("CURRENT_PROCESSING_SPEED_TAG", tag => tag.SetInt(__state));
        return __exception;
    }
}
