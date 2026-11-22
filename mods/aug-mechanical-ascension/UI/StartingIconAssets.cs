using UnityEngine;

namespace AugMechanicalAscension.UI;

internal static class StartingIconAssets
{
    internal static void Load(LoadedMod mod)
    {
        if (mod.Manifest.ID != MechcorePackage.Id) return;
        if (!mod.PaperSprites.TryGetValue("aug-start", out var icon))
            throw new InvalidDataException("Missing Aug starting icon: aug-start");
        icon.texture.filterMode = FilterMode.Point;
    }
}
