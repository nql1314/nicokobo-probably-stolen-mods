using HarmonyLib;

namespace MechcoreProtocol.ModuleMatrix;

[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.ExpandModule))]
internal static class MatrixExpansionPatch
{
    private static void Prefix(GameItem __1, out int __state) =>
        __state = __1.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt();

    private static void Postfix(GameGridInventory __0, GameItem __1, int __state)
    {
        // SetShape validates the bay UI without dispatching module add/remove callbacks.
        if (MatrixMod.Active && __state is 0 or 1 &&
            __1.GetTagReadonly("MODULE_UPGRADE_STAGE_INT").GetInt() == __state + 1 && MatrixMachines.Modules(__1) == __0)
            MatrixMachines.Recompute(__1);
    }
}
