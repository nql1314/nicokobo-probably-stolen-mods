using HarmonyLib;

namespace MechcoreProtocol;

// Existing item saves store the previous package ID as the mod sprite atlas.
[HarmonyPatch(typeof(GameItem), nameof(GameItem.SetSprite), new[] { typeof(string), typeof(string) })]
internal static class PreviousSpriteOwnerPatch
{
    private static void Prefix(ref string __0) => __0 = MechcorePackage.SpriteOwner(__0);
}
