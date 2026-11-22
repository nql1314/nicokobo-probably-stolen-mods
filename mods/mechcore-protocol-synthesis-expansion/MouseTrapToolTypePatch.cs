using HarmonyLib;

namespace MechcoreProtocol.SynthesisExpansion;

[HarmonyPatch(typeof(RatTrap), nameof(RatTrap.CreateMouseTrap))]
internal static class MouseTrapToolTypePatch
{
    private static void Postfix(GameItem __result)
    {
        if (!SynthesisMod.Active) return;
        __result.SetGameItemType(TypeHelper.TOOL);
        // Decoding a save replaces the factory's item types before invoking onLoaded.
        __result.onLoaded += item => item.SetGameItemType(TypeHelper.TOOL);
    }
}
