using HarmonyLib;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;
using UnityEngine;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class RobotAppearance
{
    internal static void Apply(GameItem item)
    {
        if (item is not GameItemElement element || SynthesisMod.Assets == null) return;
        string? key = SynthesisRobots.IsRobot(item) ? item.IsTag(ScavengingRobotRules.BrokenTag)
            ? "scavenging_robot_broken" : "scavenging_robot"
            : SynthesisRobots.IsDrill(item) ? item.IsTag(MiningDrillRules.UpgradedTag) ? "mining_drill_upgraded" : "mining_drill" : null;
        if (key == null || !SynthesisMod.Assets.Sprites.TryGetValue(key, out var sprite)) return;
        // Render nodes only: persisted sprite paths and saved inventory shape stay stable.
        if (element.background?.image != null) element.background.image.sprite = sprite;
        if (element.fakeBackground?.image != null) element.fakeBackground.image.sprite = sprite;
    }

    internal static bool LowPower(GameItem item)
    {
        if (!SynthesisRobots.IsRobot(item) || item.IsTag(ScavengingRobotRules.BrokenTag)) return false;
        var battery = SynthesisRobots.Battery(item).childItem;
        return battery == null || SynthesisPower.Energy(battery) < SynthesisNumbers.ScavengingRobot.NightlyPowerCost;
    }

    internal static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        if (SynthesisRobots.IsDrill(item))
        {
            builder.AddLine(SynthesisText.Get("synthesis.extra_ore_chance") +
                MiningDrillRules.ChancePercent(item.IsTag(MiningDrillRules.UpgradedTag)) + "%");
            if (!item.IsTag(MiningDrillRules.UpgradedTag))
                builder.AddLine(SynthesisText.Get("synthesis.upgrade_once_with_perfect_purity_titanium_alloy"));
        }
        if (!SynthesisRobots.IsRobot(item)) return;
        var shape = item.GetTagReadonly(ScavengingRobotRules.StorageShapeTag);
        builder.AddLine(SynthesisText.Get("synthesis.storage_2") + $"{shape.valueInt} × {shape.valueString.Length / shape.valueInt}");
        builder.AddLine(SynthesisText.Get("synthesis.scanner_extra_loot") +
            item.GetTagReadonly(ScavengingRobotRules.ScannerChanceTag).valueInt + "%");
        var battery = SynthesisRobots.Battery(item).childItem;
        builder.AddLine(battery == null ? SynthesisText.Get("synthesis.no_battery_installed") :
            SynthesisText.Get("synthesis.available_power") + SynthesisPower.Energy(battery));
        if (item.IsTag(ScavengingRobotRules.BrokenTag))
            builder.AddLine(SynthesisText.Get("synthesis.broken_repair_with_one_metal_ingot"));
        builder.AddLine(SynthesisText.Get("synthesis.upgrade_the_scanner_with_common_electronics"));
    }
}

[HarmonyPatch(typeof(GameItem), nameof(GameItem.SetSprite), new[] { typeof(string), typeof(string) })]
internal static class RobotDecodeShapePatch
{
    private static void Postfix(GameItem __instance)
    {
        if (!SynthesisMod.Active) return;
        // SaveManager restores state and calls SetSprite before attaching saved cargo.
        if (SynthesisRobots.IsRobot(__instance)) SynthesisRobots.RestoreStorage(__instance);
        RobotAppearance.Apply(__instance);
    }
}

[HarmonyPatch(typeof(MachineBrokenHelper), nameof(MachineBrokenHelper.LoadBrokenMachine))]
internal static class RobotBrokenLoadPatch
{
    private static bool Prefix(GameItem __0) => !SynthesisMod.Active || !SynthesisRobots.IsRobot(__0);
}

[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ValidateSelect), new[] { typeof(bool) })]
internal static class RobotSelectionAppearancePatch
{
    private static void Postfix(GameItemElement __instance) { if (SynthesisMod.Active) RobotAppearance.Apply(__instance); }
}

[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ValidatePartialSprite))]
internal static class RobotPartialAppearancePatch
{
    private static void Postfix(GameItemElement __instance) { if (SynthesisMod.Active) RobotAppearance.Apply(__instance); }
}

[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ToggleBackground))]
internal static class RobotLowPowerTintPatch
{
    private static void Prefix(GameItemElement __instance, ref float __2)
    { if (SynthesisMod.Active && RobotAppearance.LowPower(__instance)) __2 *= 0.55f; }
}

[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ToggleFakeBackground), new[] { typeof(bool), typeof(Vector4) })]
internal static class RobotLowPowerDragTintPatch
{
    private static void Prefix(GameItemElement __instance, ref Vector4 __1)
    {
        if (!SynthesisMod.Active || !RobotAppearance.LowPower(__instance)) return;
        __1.x *= 0.55f; __1.y *= 0.55f; __1.z *= 0.55f;
    }
}
