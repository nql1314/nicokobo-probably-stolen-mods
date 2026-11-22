using HarmonyLib;
using Nicokobo.ContentInterop;
using MechcoreProtocol.ModuleMatrix.Definitions;
using MechcoreProtocol.ModuleMatrix.Domain;

namespace MechcoreProtocol.ModuleMatrix;

internal static class MatrixEffects
{
    internal const string ArrivalTag = "nicokobo.mechcore_protocol.module_matrix.node_arrival_order.v1";
    internal static readonly string[] Attributes = { "PERFORMANCE", "EFFICIENCY", "QUALITY" };
    private static readonly Dictionary<string, ModuleEffectHelper.ModuleEffect> Owned = new(StringComparer.Ordinal);
    internal static bool Enabled(GameItem item) => !MatrixMod.Active || !ModuleIconDefinitions.ByItemId.ContainsKey(item.identifier) || MatrixMod.Enabled(item.identifier);

    // Native support/feedback effects read neighbours' permanent stats. Mask
    // only disabled items during this synchronous calculation, then restore
    // their saved growth even when a callback throws. Inventories stay intact.
    internal sealed class DisabledStats
    {
        private readonly Dictionary<GameItem, NodeStats> _saved = new();
        internal void Suspend(List<GameItem> items)
        {
            foreach (var item in items.Where(item => !Enabled(item)))
            {
                _saved.Add(item, new(item.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt(),
                    item.GetTagReadonly("BONUS_PERCENTAGE_EFFICIENCY_INT").GetInt(), item.GetTagReadonly("BONUS_PERCENTAGE_QUALITY_INT").GetInt()));
                WriteBase(item, default); WriteTemp(item, default);
            }
        }
        internal void Restore()
        {
            foreach (var pair in _saved) { WriteBase(pair.Key, pair.Value); WriteTemp(pair.Key, default); }
            _saved.Clear();
        }
        private static void WriteBase(GameItem item, NodeStats stats)
        {
            if (item.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt() != stats.Performance)
                item.ModifyTag("BONUS_PERCENTAGE_PERFORMANCE_INT", tag => tag.SetInt(stats.Performance));
            if (item.GetTagReadonly("BONUS_PERCENTAGE_EFFICIENCY_INT").GetInt() != stats.Efficiency)
                item.ModifyTag("BONUS_PERCENTAGE_EFFICIENCY_INT", tag => tag.SetInt(stats.Efficiency));
            if (item.GetTagReadonly("BONUS_PERCENTAGE_QUALITY_INT").GetInt() != stats.Quality)
                item.ModifyTag("BONUS_PERCENTAGE_QUALITY_INT", tag => tag.SetInt(stats.Quality));
        }
    }

    internal static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.ComputeAllModuleTempStat)),
            prefix: new HarmonyMethod(typeof(MatrixNodeAggregatePatch), nameof(MatrixNodeAggregatePatch.Prefix)),
            postfix: new HarmonyMethod(typeof(MatrixNodeAggregatePatch), nameof(MatrixNodeAggregatePatch.Postfix)),
            finalizer: new HarmonyMethod(typeof(MatrixNodeAggregatePatch), nameof(MatrixNodeAggregatePatch.Finalizer)));
        harmony.Patch(AccessTools.Method(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.HandleModuleEffectAction)),
            prefix: new HarmonyMethod(typeof(MatrixInactiveEffectPatch), nameof(MatrixInactiveEffectPatch.Prefix)));
        harmony.Patch(AccessTools.Method(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.OnAddedModuleToGrid)),
            prefix: new HarmonyMethod(typeof(MatrixNodeArrivalPatch), nameof(MatrixNodeArrivalPatch.Prefix)));
        harmony.Patch(AccessTools.Method(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.OnRemovedModuleFromGrid)),
            postfix: new HarmonyMethod(typeof(MatrixNodeRemovedPatch), nameof(MatrixNodeRemovedPatch.Postfix)));
        harmony.Patch(AccessTools.Method(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.OnUsedLearningAlgo)),
            prefix: new HarmonyMethod(typeof(MatrixLearningPatch), nameof(MatrixLearningPatch.Prefix)),
            postfix: new HarmonyMethod(typeof(MatrixLearningPatch), nameof(MatrixLearningPatch.Postfix)));
        harmony.Patch(AccessTools.Method(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.ActivateOnUseAction)),
            prefix: new HarmonyMethod(typeof(MatrixContinuousWorkPatch), nameof(MatrixContinuousWorkPatch.Prefix)));
        harmony.Patch(AccessTools.Method(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.CreateModuleEffectTooltip)),
            prefix: new HarmonyMethod(typeof(MatrixLearningTooltipPatch), nameof(MatrixLearningTooltipPatch.Prefix)),
            finalizer: new HarmonyMethod(typeof(MatrixLearningTooltipPatch), nameof(MatrixLearningTooltipPatch.Finalizer)));
    }

    internal static void Register()
    {
        if (Owned.Count != 0) return;
        var registry = ModuleEffectHelper.moduleEffects;
        var ids = PlacementEffectDefinitions.All.Select(def => def.Id).Concat(NodeEffectDefinitions.All.Select(def => def.Id)).ToArray();
        if (ids.Any(id => registry.ContainsKey(id) || FixedModuleAbilities.Contains(id)))
            throw new InvalidOperationException("Matrix effect ID collision");
        foreach (var definition in PlacementEffectDefinitions.All)
        {
            var effect = new ModuleEffectHelper.ModuleEffect
            {
                identifier = definition.Id, effectType = ModuleEffectHelper.ModuleEffect.ModuleEffectType.ModuleOnly,
                displayName = MatrixText.Get(definition.Id + ".name"),
                description = MatrixText.Get(definition.Id + ".description"),
                incompatibleWithEffects = definition.IncompatibleIds.ToList(),
                onAnyAction = (item, _) => Placement(definition, item)
            };
            Owned.Add(effect.identifier, effect);
            if (MatrixMod.Settings.Placement) registry.Add(effect.identifier, effect);
            else FixedModuleAbilities.Register(MatrixMod.Id, effect.identifier, _ => false,
                () => effect.displayName, () => effect.description);
        }
        foreach (var definition in NodeEffectDefinitions.All)
        {
            var effect = new ModuleEffectHelper.ModuleEffect
            {
                identifier = definition.Id, effectType = ModuleEffectHelper.ModuleEffect.ModuleEffectType.NodeOnly,
                displayName = MatrixText.Get(definition.Id + ".name"),
                description = MatrixText.Get(definition.Id + ".description"),
                incompatibleWithEffects = NodeEffectDefinitions.IncompatibleWith(definition.Id).ToList()
            };
            Owned.Add(effect.identifier, effect);
            string itemId = NodeItemDefinitions.All.Single(node => node.Code == definition.Code).Id;
            FixedModuleAbilities.Register(MatrixMod.Id, effect.identifier, item => item.identifier == itemId,
                () => effect.displayName, () => effect.description);
        }
    }
    internal static void RefreshText()
    {
        foreach (var definition in PlacementEffectDefinitions.All)
            if (Owned.TryGetValue(definition.Id, out var effect))
            {
                effect.displayName = MatrixText.Get(definition.Id + ".name");
                effect.description = MatrixText.Get(definition.Id + ".description");
            }
        foreach (var definition in NodeEffectDefinitions.All)
            if (Owned.TryGetValue(definition.Id, out var effect))
            {
                effect.displayName = MatrixText.Get(definition.Id + ".name");
                effect.description = MatrixText.Get(definition.Id + ".description");
            }
    }
    internal static void Unregister()
    {
        foreach (var pair in Owned)
            if (ModuleEffectHelper.moduleEffects.TryGetValue(pair.Key, out var effect) && effect == pair.Value)
                ModuleEffectHelper.moduleEffects.Remove(pair.Key);
        Owned.Clear();
        FixedModuleAbilities.Remove(MatrixMod.Id);
    }
    internal static void Placement(PlacementEffectDefinition effect, GameItem item)
    {
        if (!MatrixMod.Active || !MatrixMod.Settings.Placement || !Enabled(item)) return;
        string? Code(string tag) => item.IsTag(tag) ? PlacementEffectDefinitions.All
            .FirstOrDefault(def => def.Id == item.GetTagReadonly(tag).GetString())?.Code : null;
        string? first = Code("MODULE_EFFECT1_TAG"), second = Code("MODULE_EFFECT2_TAG");
        if (effect.Code != (first ?? second) || MatrixGeometry.Layout(item) is not { } layout) return;
        int percent = PlacementEffectRules.ResolveCallbackPercent(effect.Code, first, second, PlacementEffectRules.Analyze(layout));
        foreach (string attribute in Attributes)
        {
            int basis = item.GetTagReadonly("BONUS_PERCENTAGE_" + attribute + "_INT").GetInt();
            if (ContinuousModuleDefinitions.ById.TryGetValue(item.identifier, out var continuous) && continuous.Attribute == attribute)
                basis = continuous.Initial;
            int bonus = PlacementEffectRules.PositiveBaseBonus(basis, percent);
            if (bonus != 0) item.ModifyTag("TEMP_PERCENTAGE_" + attribute + "_INT", tag => tag.SetInt(checked(tag.GetInt() + bonus)));
        }
    }

    internal static void Arriving(GameItem node, GameInventory inventory)
    {
        if (!MatrixMod.Active || !MatrixMod.Settings.Nodes || !NodeItemDefinitions.All.Any(def => def.Id == node.identifier)) return;
        if (node.IsTag(ArrivalTag) && node.GetTagReadonly(ArrivalTag).GetInt() > 0) return;
        int latest = inventory.childItems.Where(item => item != node).Select(item => item.GetTagReadonly(ArrivalTag).GetInt()).DefaultIfEmpty(0).Max();
        node.ModifyTag(ArrivalTag, tag => tag.Enable().SetInt(checked(latest + 1)));
    }
    internal static void Removed(GameItem node)
    {
        if (MatrixMod.Active && NodeItemDefinitions.All.Any(def => def.Id == node.identifier))
            node.ModifyTag(ArrivalTag, tag => tag.SetEnabled(false).SetInt(0));
    }
    internal static NodeStats Temp(GameItem item) => new(item.GetTagReadonly("TEMP_PERCENTAGE_PERFORMANCE_INT").GetInt(),
        item.GetTagReadonly("TEMP_PERCENTAGE_EFFICIENCY_INT").GetInt(), item.GetTagReadonly("TEMP_PERCENTAGE_QUALITY_INT").GetInt());
    internal static void WriteTemp(GameItem item, NodeStats stats)
    {
        item.ModifyTag("TEMP_PERCENTAGE_PERFORMANCE_INT", tag => tag.SetInt(stats.Performance));
        item.ModifyTag("TEMP_PERCENTAGE_EFFICIENCY_INT", tag => tag.SetInt(stats.Efficiency));
        item.ModifyTag("TEMP_PERCENTAGE_QUALITY_INT", tag => tag.SetInt(stats.Quality));
    }
    internal static void Aggregate(List<GameItem> active)
    {
        if (!MatrixMod.Active || !MatrixMod.Settings.Nodes) return;
        active = active.Where(Enabled).ToList();
        var allowed = new HashSet<GameItem>(active);
        foreach (var grid in active.Select(item => item.parentInventory).OfType<GameGridInventory>().Distinct())
        {
            var nodes = active.Where(item => item.parentInventory == grid && item.IsGameItemType("NODE")).ToArray();
            var sources = active.Where(item => item.parentInventory == grid && item.IsGameItemType("MODULE")).ToArray();
            void Apply(bool isolation)
            {
                var candidates = nodes.Where(node => isolation
                    ? node.identifier == NodeItemDefinitions.N12.Id && MatrixGeometry.HasEffect(node, NodeEffectDefinitions.N12.Id)
                    : Conversion(node) != null).Select(node => new NodeClaimCandidate<GameItem, GameItem>(node,
                        node.IsTag(ArrivalTag) && node.GetTagReadonly(ArrivalTag).GetInt() > 0 ? node.GetTagReadonly(ArrivalTag).GetInt() : int.MaxValue,
                        node.uniqueId.ToString(System.Globalization.CultureInfo.InvariantCulture), sources.Where(source =>
                            allowed.Contains(source) && MatrixGeometry.Adjacent(node, source)).ToArray())).ToArray();
                var claims = NodeEffectRules.FirstNodeClaims(candidates);
                foreach (var candidate in candidates)
                {
                    NodeStats total = default;
                    foreach (var source in candidate.Sources)
                    {
                        if (claims[source] != candidate.Node) continue;
                        if (isolation) WriteTemp(source, NodeEffectRules.Isolate(Temp(source)));
                        else total = NodeEffectRules.Add(total, NodeEffectRules.ConversionContribution(Conversion(candidate.Node)!, Temp(source)));
                    }
                    if (!isolation) WriteTemp(candidate.Node, total);
                }
            }
            Apply(true); Apply(false);
        }
    }
    private static string? Conversion(GameItem node) => NodeItemDefinitions.All.Where(def => def.Code is "N09" or "N10" or "N11")
        .FirstOrDefault(def => def.Id == node.identifier && MatrixGeometry.HasEffect(node, NodeEffectDefinitions.ByCode[def.Code].Id))?.Code;
}

internal static class MatrixNodeAggregatePatch
{
    internal static void Prefix(List<GameItem> __0, out MatrixEffects.DisabledStats __state)
    { __state = new(); __state.Suspend(__0); }
    internal static void Postfix(List<GameItem> __0, MatrixEffects.DisabledStats __state)
    { __state.Restore(); MatrixEffects.Aggregate(__0); }
    internal static Exception? Finalizer(Exception? __exception, MatrixEffects.DisabledStats? __state)
    { __state?.Restore(); return __exception; }
}
[HarmonyPatch(typeof(ModuleHelper), nameof(ModuleHelper.ComputeModuleEffect), new[] { typeof(List<GameItem>), typeof(GameItem) })]
internal static class MatrixActiveModulePatch
{
    internal static void Prefix(ref List<GameItem> __0)
    { if (__0.Any(item => !MatrixEffects.Enabled(item))) __0 = __0.Where(MatrixEffects.Enabled).ToList(); }
}
internal static class MatrixInactiveEffectPatch
{
    internal static bool Prefix(GameItem __0) => MatrixEffects.Enabled(__0);
}
internal static class MatrixNodeArrivalPatch
{
    internal static void Prefix(GameItem __0, GameItem __1) { if (__0.parentInventory != null) MatrixEffects.Arriving(__0, __0.parentInventory); }
}
internal static class MatrixNodeRemovedPatch
{
    internal static void Postfix(GameItem __0) => MatrixEffects.Removed(__0);
}
internal static class MatrixLearningPatch
{
    internal static void Prefix(GameItem __0, out MatrixTurbo.LearningSnapshot? __state) => __state = MatrixTurbo.BeforeLearning(__0);
    internal static void Postfix(GameItem __0, MatrixTurbo.LearningSnapshot? __state) => MatrixTurbo.AfterLearning(__0, __state);
}

