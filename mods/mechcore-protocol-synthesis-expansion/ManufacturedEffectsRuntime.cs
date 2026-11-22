using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class ManufacturedEffectsRuntime
{
    private sealed class Applied { internal ManufacturedMachineBonuses Bonuses; }
    private sealed class ModuleBay { internal GameGridInventory Inventory = null!; }
    private static ConditionalWeakTable<GameItem, Applied> Machines = new();
    private static readonly ConditionalWeakTable<GameItem, ModuleBay> ModuleBays = new();
    private static bool _recomputing, _selling;
    private static ManufacturedEffects? _current;
    private static readonly OwnedItemCandidates EffectItems = new(
        store => store.FindAllItem(false), item => ManufacturedEffects.IsRelevant(item.identifier));
    private static readonly string AiFeature = SynthesisDefinitions.OwnerId + ".bargain.ai_core";
    private static readonly string CyberneticFeature = SynthesisDefinitions.OwnerId + ".bargain.cybernetic";
    internal const string PeerClientId = "lemocuber_probablymultiplayer_human";

    internal static void Reset()
    {
        Machines = new(); _current = null; _recomputing = _selling = false;
        EffectItems.Clear(); ItemGraphChanges.Invalidate();
    }
    internal static ManufacturedEffects Capture() => SynthesisMod.Store is { } store ? Capture(EffectItems.Read(store)) : default;
    private static ManufacturedEffects Capture(IEnumerable<GameItem> items) => ManufacturedEffects.FromItems(
        items.Select(item => new ManufacturedEffectItem(item.identifier, item.unitCount,
            item.unitCount > 0 && ManufacturedEffects.IsRelevant(item.identifier) && GeneralHelper.IsItemOwned(item))));

    internal static void RegisterModuleBay(GameInventory inventory, GameItem machine)
    {
        if (inventory is GameGridInventory grid) ModuleBays.GetValue(machine, _ => new()).Inventory = grid;
    }

    private static GameGridInventory? Modules(GameItem machine)
    {
        if (!GeneralHelper.IsItemOwned(machine)) return null;
        if (ModuleBays.TryGetValue(machine, out var bay)) return bay.Inventory;
        if (machine.contentWindow?.childElement is GridPixelElement { gridWidth: < 2 } or GridPixelElement { gridHeight: < 2 })
            return null;
        return machine.IsTag("STANDARD_MACHINE_TAG") || machine.identifier == "evaporator"
            ? MachineHelper.GetBodyStorage(machine, 1) as GameGridInventory : null;
    }

    internal static void Closed()
    {
        var store = SynthesisMod.Store;
        if (store == null) return;
        store.visitLeftTonight = (int)Math.Min(int.MaxValue, (long)store.visitLeftTonight + Capture().NightActionBonus);
    }

    internal static void Night()
    {
        var store = SynthesisMod.Store;
        if (store == null) return;
        // Capture and refresh use the same immediate snapshot; no inventory mutation occurs between them.
        var items = store.FindAllItem();
        var effects = Capture(items);
        Machines = new();
        foreach (var machine in items) if (Modules(machine) != null) Refresh(machine, effects);
    }

    internal static void BeforeRead(GameItem machine)
    {
        if (!SynthesisMod.Active || _recomputing || SynthesisMod.Store == null || Modules(machine) == null) return;
        Refresh(machine, Capture());
    }

    private static void Refresh(GameItem machine, ManufacturedEffects effects)
    {
        if (_recomputing || Modules(machine) is not { } modules) return;
        if (Machines.TryGetValue(machine, out var previous) && previous.Bonuses == effects.MachineBonuses) return;
        _recomputing = true; _current = effects;
        try
        {
            // Native aggregation resets totals; the postfix adds one owned contribution.
            ModuleEffectHelper.ComputeAllModuleTempStat(modules.childItems);
            ModuleHelper.ComputeModuleEffect(modules, null, machine);
            ModuleHelper.ApplyBasicModuleEffect(modules, null, machine);
        }
        catch { Machines.Remove(machine); throw; }
        finally { _current = null; _recomputing = false; }
    }

    internal static void AfterAggregate(GameItem machine)
    {
        if (!SynthesisMod.Active || Modules(machine) == null || SynthesisMod.Store == null) return;
        var bonuses = (_current ?? Capture()).MachineBonuses;
        void Add(string tag, int value)
        {
            if (value != 0) machine.ModifyTag(tag, state => state.SetInt(checked(state.GetInt() + value)));
        }
        Add("TOTAL_PERCENTAGE_POWER_BONUS_INT", bonuses.Efficiency);
        Add("TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT", bonuses.Performance);
        Add("TOTAL_PERCENTAGE_QUALITY_BONUS_INT", bonuses.Quality);
        if (bonuses != default) machine.ModifyTag("IS_RECEIVING_BONUS_FROM_MODULE_BOOL", tag => tag.SetBool(true));
        Machines.GetValue(machine, _ => new()).Bonuses = bonuses;
    }

    internal static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        if (Modules(item) == null || !Machines.TryGetValue(item, out var state)) return;
        var bonuses = state.Bonuses;
        if (bonuses.Efficiency != 0) builder.AddLine(SynthesisText.Get("synthesis.kinetic_recovery_efficiency") + bonuses.Efficiency + "%");
        if (bonuses.Performance != 0) builder.AddLine(SynthesisText.Get("synthesis.titanium_exoskeleton_performance") + bonuses.Performance + "%");
        if (bonuses.Quality != 0) builder.AddLine(SynthesisText.Get("synthesis.fine_motor_assistant_quality") + bonuses.Quality + "%");
    }

    internal static bool OnCounter(GameItem item)
    {
        var store = EmporiumEntry.Instance;
        return store != null && item.parentInventory != null &&
            (item.parentInventory == store.frontInvinvElement || item.parentInventory == store.backInvinvElement);
    }

    internal static void ClearSale(GameItem item)
    {
        if (item.FindItemFeatureByID(AiFeature) != null) item.RemoveItemFeatureByID(AiFeature);
        if (item.FindItemFeatureByID(CyberneticFeature) != null) item.RemoveItemFeatureByID(CyberneticFeature);
    }

    internal static void RefreshSale(GameItem item)
    {
        if (!SynthesisMod.Active || _selling) return;
        _selling = true;
        try
        {
            var client = SynthesisMod.Store?.currentClientInstance?.GetClientBlueprint();
            if (client == null || client.identifier == PeerClientId || !GeneralHelper.IsItemOwned(item) || !OnCounter(item))
            { ClearSale(item); return; }
            Ensure(item, CyberneticFeature, SynthesisNumbers.Effects.CyberneticAugSaleBonusPercent,
                SynthesisText.Get("synthesis.cybernetics_for_aug"), client.isAug && item.IsGameItemType("CYBERNETIC"));
            int bonus = client.isAug ? 0 : Capture().SaleBonus(false);
            Ensure(item, AiFeature, bonus, SynthesisText.Get("synthesis.ai_core"), bonus != 0 && item.unitCount > 0);
        }
        finally { _selling = false; }
    }

    private static void Ensure(GameItem item, string id, int modifier, string display, bool enabled)
    {
        var previous = item.FindItemFeatureByID(id);
        if (!enabled)
        {
            if (previous != null) item.RemoveItemFeatureByID(id);
            return;
        }
        if (previous != null && previous.featureType == ItemFeature.FeatureType.TemporarySelling &&
            previous.valueStage == ItemFeature.ValueStage.Market && previous.category == id && !previous.useCondition &&
            !previous.isDisabled && previous.GetActualValueModifier() == modifier && previous.GetFakeValueModifier() == modifier &&
            previous.GetBaseRealDisplay() == display && item.itemFeatures.Count(feature => feature.identifier == id) == 1) return;
        if (previous != null) item.RemoveItemFeatureByID(id);
        var feature = new ItemFeature(ItemFeature.FeatureType.TemporarySelling)
        {
            identifier = id, category = id, valueStage = ItemFeature.ValueStage.Market,
            isFeatureMatch = true, isExposable = true, isPublicHidden = false, useCondition = false
        };
        feature.SetPublicDisplay(display); feature.SetActualDisplay(display);
        feature.SetValueModifier(modifier); feature.SetPreExposedValueMod(modifier, true);
        item.AddItemFeature(feature);
    }
}

[HarmonyPatch(typeof(ModuleHelper), nameof(ModuleHelper.ComputeModuleEffect), new[] { typeof(List<GameItem>), typeof(GameItem) })]
internal static class ManufacturedModuleAggregatePatch
{
    private static void Postfix(GameItem __1) => ManufacturedEffectsRuntime.AfterAggregate(__1);
}

[HarmonyPatch]
internal static class ManufacturedMachineReadPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { nameof(MachineryHelper.GetMachinePowerUsage), nameof(MachineryHelper.GetCurrentPerformanceBonus),
                     nameof(MachineryHelper.GetCurrentQualityBonus) })
            yield return AccessTools.Method(typeof(MachineryHelper), name, new[] { typeof(GameItem) });
    }
    private static void Prefix(GameItem __0) => ManufacturedEffectsRuntime.BeforeRead(__0);
}

[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.GetCurrentStoreAttractiveness))]
internal static class RobotCatAttractivenessPatch
{
    private static void Postfix(ref int __result)
    {
        if (!SynthesisMod.Active || EmporiumEntry.Instance?.showcaseElement == null) return;
        if (EmporiumEntry.Instance.showcaseElement.childItems.Any(item => item.identifier == SynthesisDefinitions.RobotCat.Id &&
            item.unitCount > 0 && GeneralHelper.IsItemOwned(item)))
            __result = (int)Math.Min(int.MaxValue, (long)__result + SynthesisNumbers.Effects.RobotCatAttractivenessBonus);
    }
}

[HarmonyPatch]
internal static class ManufacturedSaleValuePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.GetCurrentValue));
        yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.GetNegociatedValue));
        yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.OnAddedToWeightedArea));
    }
    private static void Prefix(GameItem __instance) => ManufacturedEffectsRuntime.RefreshSale(__instance);
}

[HarmonyPatch(typeof(GameItem), nameof(GameItem.OnRemovedFromWeightedArea))]
internal static class ManufacturedSaleRemovedPatch
{
    private static void Postfix(GameItem __instance) { if (SynthesisMod.Active) ManufacturedEffectsRuntime.ClearSale(__instance); }
}

[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.SetupModuleBay))]
internal static class ManufacturedModuleBayPatch
{
    private static void Postfix(GameInventory __0, GameItem __1) => ManufacturedEffectsRuntime.RegisterModuleBay(__0, __1);
}
