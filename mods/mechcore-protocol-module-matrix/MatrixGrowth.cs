using HarmonyLib;
using MechcoreProtocol.ModuleMatrix.Definitions;
using MechcoreProtocol.ModuleMatrix.Domain;

namespace MechcoreProtocol.ModuleMatrix;

internal static class MatrixGrowth
{
    internal const string WorkDayTag = "nicokobo.mechcore_protocol.module_matrix.continuous_last_work_day.v1";
    internal const string DecayDayTag = "nicokobo.mechcore_protocol.module_matrix.continuous_last_decay_day.v1";
    private static string BaseTag(ContinuousModuleDefinition definition) => "BONUS_PERCENTAGE_" + definition.Attribute + "_INT";
    internal static void Work(GameItem item, GameInventory grid, GameItem machine)
    {
        if (!MatrixMod.Active || !MatrixMod.Settings.Continuous || item.unitCount <= 0 || machine.unitCount <= 0 ||
            item.parentInventory != grid || grid.GetParentItem() != machine || MatrixMachines.Modules(machine) != grid ||
            !GeneralHelper.IsItemOwned(item) || !GeneralHelper.IsItemOwned(machine) ||
            !ContinuousModuleDefinitions.ById.TryGetValue(item.identifier, out var definition) || MatrixMod.Store == null) return;
        int day = StoreStation.GetDayCounter();
        if (day < 0 || string.IsNullOrWhiteSpace(MatrixMod.Store.runID)) return;
        MatrixTurbo.RecordWork(item, machine);
        string tag = BaseTag(definition);
        int before = Math.Max(0, item.GetTagReadonly(tag).GetInt() - definition.Initial);
        int after = Math.Max(before, ContinuousGrowthRules.Advance(before, definition.Step, MatrixTurbo.TotalCap(definition) - definition.Initial,
            MatrixGeometry.HasLearningNode(item) ? ModuleMatrixNumbers.Effects.LearningMultiplier : 1));
        if (after != before) item.ModifyTag(tag, state => state.SetInt(checked(definition.Initial + after)));
        item.ModifyTag(WorkDayTag, state => state.Enable().SetInt(day));
    }
    internal static void Decay()
    {
        if (!MatrixMod.Settings.Continuous || MatrixMod.Store == null) return;
        int day = StoreStation.GetDayCounter();
        if (day < 0) return;
        var changed = new HashSet<GameItem>();
        foreach (var item in MatrixMod.Store.FindAllItem().Where(item => item.unitCount > 0 && GeneralHelper.IsItemOwned(item)))
        {
            if (!ContinuousModuleDefinitions.ById.TryGetValue(item.identifier, out var definition) ||
                item.IsTag(DecayDayTag) && item.GetTagReadonly(DecayDayTag).GetInt() == day) continue;
            if (!item.IsTag(WorkDayTag) || item.GetTagReadonly(WorkDayTag).GetInt() != day)
            {
                string tag = BaseTag(definition);
                int growth = Math.Max(0, item.GetTagReadonly(tag).GetInt() - definition.Initial);
                int after = ContinuousGrowthRules.Decay(growth, definition.Step, 1);
                if (after != growth)
                {
                    item.ModifyTag(tag, state => state.SetInt(checked(definition.Initial + after)));
                    if (item.parentInventory?.GetParentItem() is { } host && MatrixMachines.Modules(host) == item.parentInventory) changed.Add(host);
                }
            }
            item.ModifyTag(DecayDayTag, state => state.Enable().SetInt(day));
        }
        foreach (var machine in changed) MatrixMachines.Recompute(machine);
    }
}

internal static class MatrixContinuousWorkPatch
{
    internal static bool Prefix(GameItem __0, GameInventory __1, GameItem __2)
    {
        if (!MatrixEffects.Enabled(__0)) return false;
        MatrixGrowth.Work(__0, __1, __2); return true;
    }
}
