using System.Reflection;
using HarmonyLib;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class RobotResources
{
    internal static bool IsPair(GameItem source, GameItem target) =>
        ((source.identifier is "metal_ingot" or "common_electronic") && SynthesisRobots.IsRobot(target)) ||
        (source.identifier == SynthesisDefinitions.TitaniumAlloy.Id && SynthesisRobots.IsDrill(target));

    internal static bool CanApply(GameItem source, GameItem target)
    {
        if (!IsPair(source, target) || target.unitCount != 1 || source.parentInventory == null ||
            !source.MayRemove() || !GeneralHelper.IsItemOwned(source) || !GeneralHelper.IsItemOwned(target)) return false;
        if (SynthesisRobots.IsDrill(target))
            return source.unitCount >= SynthesisNumbers.MiningDrill.UpgradeTitaniumAlloyCount &&
                MiningDrillRules.CanUpgrade(target.IsTag(MiningDrillRules.UpgradedTag),
                    IngotPurityRules.FromNativeTag(IngotPurityHelper.GetPurity(source)));
        if (source.identifier == "metal_ingot") return source.unitCount >= SynthesisNumbers.ScavengingRobot.RepairIngotCount &&
            target.IsTag(ScavengingRobotRules.BrokenTag);
        int chance = target.GetTagReadonly(ScavengingRobotRules.ScannerChanceTag).valueInt;
        return source.unitCount >= SynthesisNumbers.ScavengingRobot.UpgradeElectronicCount &&
            ScavengingRobotRules.IsValidScannerChance(chance) && chance < ScavengingRobotRules.MaxScannerChance;
    }

    internal static bool Apply(GameItem source, GameItem target)
    {
        if (!CanApply(source, target)) return false;
        var inventory = source.parentInventory;
        var originalSlot = source.GetIncompleteSlotInInventory();
        if (inventory == null || originalSlot == null) return false;
        int units = source.unitCount;
        bool drill = SynthesisRobots.IsDrill(target), repair = source.identifier == "metal_ingot";
        int count = drill ? SynthesisNumbers.MiningDrill.UpgradeTitaniumAlloyCount : repair
            ? SynthesisNumbers.ScavengingRobot.RepairIngotCount : SynthesisNumbers.ScavengingRobot.UpgradeElectronicCount;
        var state = target.state.Clone();
        long value = target.unitValue;
        int chance = repair || drill ? 0 : target.GetTagReadonly(ScavengingRobotRules.ScannerChanceTag).valueInt;
        long basis = value;
        if (!repair)
        {
            basis = SynthesisProduction.ReadBasis(target, target.unitValue);
            if (drill) basis = checked(basis + checked((long)SynthesisProduction.InputValue(source) * count));
            else basis = checked(basis + SynthesisNumbers.ScavengingRobot.UpgradeValueBonus);
        }
        bool committed = false;
        try
        {
            if (units == count)
            {
                if (originalSlot == null || !inventory.Expel(source) || source.parentInventory != null)
                    throw new InvalidOperationException("Resource removal failed");
            }
            else
            {
                source.SetUnitCount(units - count);
                if (source.parentInventory != inventory || source.unitCount != units - count)
                    throw new InvalidOperationException("Resource count readback failed");
            }
            if (drill)
            {
                SynthesisProduction.SetBasis(target, basis);
                target.ModifyTag(MiningDrillRules.UpgradedTag, tag => tag.Enable());
            }
            else if (repair) target.ModifyTag(ScavengingRobotRules.BrokenTag, tag => tag.SetEnabled(false));
            else
            {
                SynthesisRobots.SetScannerChance(target, ScavengingRobotRules.UpgradeScanner(chance));
                SynthesisProduction.SetBasis(target, basis);
            }
            committed = true;
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            try { target.state = state; target.unitValue = value; target.SyncModifiedState(); }
            catch (Exception restore) { failures.Add(restore); }
            try
            {
                if (source.unitCount != units) source.SetUnitCount(units);
                if (source.parentInventory == null) originalSlot.AcceptUnchecked();
                if (source.parentInventory != inventory || source.unitCount != units)
                    throw new InvalidOperationException("Resource restore readback failed");
            }
            catch (Exception restore) { failures.Add(restore); }
            SynthesisMod.Warn(new AggregateException("Robot resource action failed", failures).ToString());
            return false;
        }
        finally
        {
            if (committed && units == count)
                try { source.Destroy(); } catch (Exception error) { SynthesisMod.Warn("Consumed resource cleanup failed: " + error.Message); }
        }
        RobotAppearance.Apply(target);
        OverlayHandler.current?.UpdateInventoryStateUI();
        return true;
    }
}

[HarmonyPatch]
internal static class RobotResourceEligibilityPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.MayTarget), new[] { typeof(GameItem) });
        yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.CanTarget), new[] { typeof(GameItem) });
    }
    private static bool Prefix(GameItem __instance, GameItem __0, ref bool __result)
    {
        if (!SynthesisMod.Active || !RobotResources.IsPair(__instance, __0)) return true;
        __result = RobotResources.CanApply(__instance, __0); return false;
    }
}

[HarmonyPatch(typeof(GameItem), nameof(GameItem.Target), new[] { typeof(GameItem) })]
internal static class RobotResourceApplyPatch
{
    private static bool Prefix(GameItem __instance, GameItem __0)
    {
        if (!SynthesisMod.Active || !RobotResources.IsPair(__instance, __0)) return true;
        try { RobotResources.Apply(__instance, __0); }
        catch (Exception error) { SynthesisMod.Warn("Robot resource action rejected: " + error.Message); }
        return false;
    }
}
