using HarmonyLib;
using Nicokobo.FoodProcessing.Definitions;

namespace Nicokobo.FoodProcessing;

internal static class FoodStorageExpansion
{
    internal const string LevelTag = "nicokobo.food_processing.storage_expansion";
    internal const int MaxLevel = 4;
    private const string IngotId = "metal_ingot";

    internal static int Level(GameItem machine) => Math.Clamp(machine.GetTagReadonly(LevelTag).GetInt(), 0, MaxLevel);

    internal static bool CanUpgrade(GameItem ingot, GameItem machine) =>
        ingot.identifier == IngotId && ingot.unitCount > 0 &&
        (machine.identifier == FoodDefinitions.FoodProcessorItem.Id || machine.identifier == FoodDefinitions.InfusionTowerItem.Id) &&
        GeneralHelper.IsItemEffectivelyOwned(ingot) && GeneralHelper.IsItemEffectivelyOwned(machine) && Level(machine) < MaxLevel;

    internal static void Attach(GameItem machine, MachineDefinition definition, GameGridInventory input, GameGridInventory output)
    {
        void Resize(int level)
        {
            // Grow the existing grids so saved slot indices and placed items stay valid.
            input.SetShape(definition.InputWidth, definition.InputHeight + level);
            output.SetShape(definition.OutputWidth, definition.OutputHeight + level);
            machine.contentWindow!.Validate();
        }

        machine.onLoaded += item =>
        {
            int level = Level(item);
            if (level > 0) Resize(level);
        };
        machine.onItemTargetThisFunc += (ingot, target) =>
        {
            if (!CanUpgrade(ingot, target)) return;
            int level = Level(target) + 1;
            Resize(level);
            target.ModifyTag(LevelTag, tag => tag.Enable().SetInt(level));
            if (ingot.unitCount > 1) ingot.SetUnitCount(ingot.unitCount - 1);
            else ingot.Destroy();
            EmporiumAudioPlayer.Instance.ratchet.Play();
        };
    }
}

// The ingot's native source-side predicate must also accept the new target.
[HarmonyPatch(typeof(MaterialDirectory), nameof(MaterialDirectory.CanIngotTarget))]
internal static class FoodIngotTargetPatch
{
    private static void Postfix(GameItem source, GameItem target, ref bool __result)
    {
        if (!__result) __result = FoodStorageExpansion.CanUpgrade(source, target);
    }
}
