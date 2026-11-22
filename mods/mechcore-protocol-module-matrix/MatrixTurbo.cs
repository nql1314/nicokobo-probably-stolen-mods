using HarmonyLib;
using MechcoreProtocol.ModuleMatrix.Definitions;
using MechcoreProtocol.ModuleMatrix.Domain;
using Nicokobo.ContentInterop;
using Nicokobo.ModUtilities;

namespace MechcoreProtocol.ModuleMatrix;

internal static class MatrixTurbo
{
    internal sealed class LearningSnapshot
    {
        internal GameInventory Inventory = null!;
        internal GameItem Machine = null!;
        internal int[] Before = Array.Empty<int>();
        internal int[] Caps = Array.Empty<int>();
        internal int Multiplier;
    }
    private sealed class GrowthSnapshot
    {
        internal ContinuousModuleDefinition? Continuous;
        internal int BeforeGrowth, MaximumGrowth, Multiplier;
        internal int[]? LearningBefore, LearningCaps;
        internal bool Worked;
    }
    internal sealed class Scope
    {
        internal PlayerStore Store = null!;
        internal int Slot;
        internal string Run = "";
        internal GameItem Machine = null!;
        internal GameGridInventory Inventory = null!;
        internal Scope? Previous;
        private readonly Dictionary<GameItem, GrowthSnapshot> _growth = new();
        private bool _completed;
        internal void Capture(GameItem item)
        {
            var snapshot = new GrowthSnapshot { Multiplier = checked(2 * LearningMultiplier(item)) };
            if (MatrixMod.Settings.Continuous && ContinuousModuleDefinitions.ById.TryGetValue(item.identifier, out var definition))
            {
                snapshot.Continuous = definition;
                snapshot.BeforeGrowth = Math.Max(0, item.GetTagReadonly(BaseTag(definition.Attribute)).GetInt() - definition.Initial);
                snapshot.MaximumGrowth = CognitiveTurboRules.Cap(definition.Cap, true) - definition.Initial;
            }
            if (MatrixGeometry.HasEffect(item, NodeEffectDefinitions.NativeLearning))
            {
                snapshot.LearningBefore = ReadBase(item);
                snapshot.LearningCaps = ReadLearningCaps(item, true);
            }
            if (snapshot.Continuous != null || snapshot.LearningBefore != null) _growth.Add(item, snapshot);
        }
        internal void Record(GameItem item)
        { if (_growth.TryGetValue(item, out var snapshot)) snapshot.Worked = true; }
        internal void Complete()
        {
            if (_completed) return;
            _completed = true;
            var store = MatrixMod.Store;
            if (!HasImplant() || store != Store || store.saveSlotId != Slot || store.runID != Run ||
                !Owned(Machine) || MatrixMachines.Modules(Machine) != Inventory || Inventory.GetParentItem() != Machine) return;
            bool changed = false;
            foreach (var pair in _growth)
            {
                var item = pair.Key; var snapshot = pair.Value;
                if (!snapshot.Worked || !Owned(item) || item.parentInventory != Inventory) continue;
                if (snapshot.LearningBefore != null && snapshot.LearningCaps != null)
                    changed |= TopUpLearning(item, snapshot.LearningBefore, snapshot.LearningCaps, snapshot.Multiplier);
                if (snapshot.Continuous is not { } definition) continue;
                string tag = BaseTag(definition.Attribute);
                int current = Math.Max(0, item.GetTagReadonly(tag).GetInt() - definition.Initial);
                int desired = ContinuousGrowthRules.Advance(snapshot.BeforeGrowth, definition.Step,
                    snapshot.MaximumGrowth, snapshot.Multiplier);
                // Ordinary completed batches may already have supplied this growth.
                if (desired <= current) continue;
                item.ModifyTag(tag, state => state.SetInt(checked(definition.Initial + desired)));
                changed = true;
            }
            if (changed) MatrixMachines.Recompute(Machine);
        }
    }

    [ThreadStatic] private static Scope? Current;
    internal static void Reset() => Current = null;
    internal const string CognitiveTurbo = "nicokobo.aug.cybernetic.cognitive_turbo";
    internal static bool HasImplant() => MatrixMod.Active && MatrixMod.Store != null && !string.IsNullOrWhiteSpace(MatrixMod.Store.runID) &&
        MatrixMod.Store.FindAllItem().Any(item => item.identifier == CognitiveTurbo &&
            item.unitCount > 0 && GeneralHelper.IsItemOwned(item));
    internal static int TotalCap(ContinuousModuleDefinition definition) => CognitiveTurboRules.Cap(definition.Cap, HasImplant());
    private static string BaseTag(string attribute) => "BONUS_PERCENTAGE_" + attribute + "_INT";
    private static bool Owned(GameItem item) => item.unitCount > 0 && GeneralHelper.IsItemOwned(item) && MatrixEffects.Enabled(item);
    private static int[] ReadBase(GameItem item) => MatrixEffects.Attributes.Select(attribute => item.GetTagReadonly(BaseTag(attribute)).GetInt()).ToArray();
    private static int[] ReadLearningCaps(GameItem item, bool augmented) => MatrixEffects.Attributes.Select(attribute =>
        CognitiveTurboRules.Cap(item.GetTagReadonly("LEARNING_ALGO_MAX_" + attribute + "_INT").GetInt(), augmented)).ToArray();
    private static bool TryHost(GameItem item, out GameItem machine)
    {
        machine = item.parentInventory?.GetParentItem()!;
        return Owned(item) && machine != null && Owned(machine) && MatrixMachines.Modules(machine) == item.parentInventory;
    }
    internal static int LearningMultiplier(GameItem item) => TryHost(item, out _) && MatrixGeometry.HasLearningNode(item)
        ? ModuleMatrixNumbers.Effects.LearningMultiplier : 1;

    internal static Scope? Begin(GameItem tool, GameItem machine)
    {
        var store = MatrixMod.Store;
        if (tool.identifier is not ("turbo_booster" or "turbo_booster_adv" or "turbo_booster_hypertech") || !HasImplant() || store == null ||
            store.saveSlotId < 0 || string.IsNullOrWhiteSpace(store.runID) || !Owned(tool) || !Owned(machine) ||
            MatrixMachines.Modules(machine) is not { } grid || grid.GetParentItem() != machine) return null;
        var scope = new Scope { Store = store, Slot = store.saveSlotId, Run = store.runID, Machine = machine,
            Inventory = grid, Previous = Current };
        foreach (var item in grid.childItems)
            if (Owned(item) && item.parentInventory == grid) scope.Capture(item);
        Current = scope;
        return scope;
    }
    internal static void RecordWork(GameItem item, GameItem machine)
    { if (Current?.Machine == machine && item.parentInventory == Current.Inventory) Current.Record(item); }
    internal static void End(Scope? scope, bool completedNormally)
    {
        if (scope == null) return;
        try { if (completedNormally) scope.Complete(); }
        finally { if (Current == scope) Current = scope.Previous; }
    }

    internal static LearningSnapshot? BeforeLearning(GameItem item)
    {
        if (!MatrixMod.Active || !MatrixGeometry.HasEffect(item, NodeEffectDefinitions.NativeLearning) || !TryHost(item, out var machine)) return null;
        bool augmented = HasImplant();
        int multiplier = LearningMultiplier(item);
        if (!augmented && multiplier == 1) return null;
        RecordWork(item, machine);
        return new() { Inventory = item.parentInventory!, Machine = machine, Before = ReadBase(item),
            Caps = ReadLearningCaps(item, augmented), Multiplier = multiplier };
    }
    internal static void AfterLearning(GameItem item, LearningSnapshot? before)
    {
        if (before == null || !Owned(item) || !Owned(before.Machine) || item.parentInventory != before.Inventory ||
            before.Inventory.GetParentItem() != before.Machine || MatrixMachines.Modules(before.Machine) != before.Inventory) return;
        TopUpLearning(item, before.Before, before.Caps, before.Multiplier);
    }
    private static bool TopUpLearning(GameItem item, int[] before, int[] caps, int multiplier)
    {
        bool changed = false;
        for (int index = 0; index < MatrixEffects.Attributes.Length; index++)
        {
            if (before[index] >= caps[index]) continue;
            int desired = (int)Math.Min(caps[index], (long)before[index] + multiplier);
            string tag = BaseTag(MatrixEffects.Attributes[index]);
            if (desired <= item.GetTagReadonly(tag).GetInt()) continue;
            item.ModifyTag(tag, state => state.SetInt(desired)); changed = true;
        }
        return changed;
    }

    internal static string ContinuousDescription(GameItem item, ContinuousModuleDefinition definition)
    {
        int step = checked(definition.Step * LearningMultiplier(item));
        bool augmented = HasImplant();
        int cap = CognitiveTurboRules.Cap(definition.Cap, augmented);
        string attribute = AttributeName(definition.Attribute);
        string ordinary = MatrixText.Get("matrix.each_completed_operation_current_total_cap_an", attribute, step, cap, definition.Step);
        return augmented ? ordinary + MatrixText.Get("matrix.cognitive_turbo_disposable_advanced_hypertech_booster_work", step * 2) : ordinary;
    }
    internal static string LearningDescription(GameItem item)
    {
        int step = LearningMultiplier(item);
        var caps = ReadLearningCaps(item, true);
        string limits = string.Join(ModText.IsChinese ? "、" : ", ", MatrixEffects.Attributes.Select((attribute, index) =>
            AttributeName(attribute) + " " + caps[index].ToString("+#;-#;0", System.Globalization.CultureInfo.InvariantCulture) + "%"));
        return MatrixText.Get("matrix.each_completed_operation_adds_percentage_points_below", step, limits, step * 2);
    }
    private static string AttributeName(string attribute) =>
        LocHelper.GetLocalizedMechanic("mech_module_stat_" + attribute.ToLowerInvariant());
}

[HarmonyPatch(typeof(GameItem), nameof(GameItem.Target), new[] { typeof(GameItem) })]
internal static class MatrixTurboTargetPatch
{
    private static void Prefix(GameItem __instance, GameItem __0, out MatrixTurbo.Scope? __state) => __state = MatrixTurbo.Begin(__instance, __0);
    private static Exception? Finalizer(MatrixTurbo.Scope? __state, Exception? __exception)
    {
        try { MatrixTurbo.End(__state, __exception == null); }
        catch (Exception error) { MatrixMod.Warn("Cognitive Turbo growth failed after native work: " + error); }
        return __exception;
    }
}

internal static class MatrixLearningTooltipPatch
{
    internal sealed record Before(ModuleEffectHelper.ModuleEffect Effect, string Original, string Replacement);
    internal static void Prefix(GameItem __1, out Before? __state)
    {
        __state = null;
        if (!MatrixMod.Active || !MatrixGeometry.HasEffect(__1, NodeEffectDefinitions.NativeLearning) || !MatrixTurbo.HasImplant() ||
            !ModuleEffectHelper.moduleEffects.TryGetValue(NodeEffectDefinitions.NativeLearning, out var effect)) return;
        string replacement = MatrixTurbo.LearningDescription(__1);
        __state = new(effect, effect.description, replacement);
        effect.description = replacement;
    }
    internal static Exception? Finalizer(Before? __state, Exception? __exception)
    {
        if (__state != null && __state.Effect.description == __state.Replacement) __state.Effect.description = __state.Original;
        return __exception;
    }
}
