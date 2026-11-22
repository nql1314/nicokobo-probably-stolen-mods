using HarmonyLib;
using Nicokobo.ContentInterop;

namespace Nicokobo.Forge;

internal static class ForgeDossier
{
    internal static bool ExpansionEnabled { get; private set; }

    internal static void DefineConfig()
    {
        ExpansionEnabled = ModHelper.GetConfig(ForgeMod.Id).Define("EnableDossierExpansion", true, "forge.config.dossier",
            () => ForgeText.Get("forge.enable_dossier_expansion"),
            () => ForgeText.Get("forge.dossier_expansion_tooltip"), requiresRestart: true).Value;
    }

    internal static void Expand(GameItem? dossier, int minimumWidth = 16)
    {
        if (dossier?.contentWindow?.childElement is not GameGridInventory grid) return;
        int width = Math.Max(minimumWidth, grid.inventoryShape.width);
        int height = Math.Max(16, grid.inventoryShape.height);
        if (width == grid.inventoryShape.width && height == grid.inventoryShape.height) return;
        // Keep the original grid, its contents, and document admission callbacks.
        grid.SetShape(width, height);
        grid.Validate();
        dossier.contentWindow.Validate();
    }

    internal static void FitContents(GameItem? dossier)
    {
        if (dossier?.contentWindow?.childElement is not GameGridInventory grid) return;
        var bounds = grid.inventoryShape;
        var items = grid.childItems;
        // Native loading restores saved coordinates even outside the current grid.
        bool hasItemsOutside = items.Any(item =>
            !item.modifiedShape.FullyIntersectsRect(bounds.width, bounds.height,
                static (cell, outside) => cell != 1 || outside == 0));
        if (!hasItemsOutside) return;

        // The native sorter indexes the whole bounding box of each anchored item.
        bool anchoredItemsFit = items.Where(item => ItemPinHelper.IsPinned(item) || DebrisHelper.IsDebris(item))
            .All(item => item.modifiedShape.minX >= 0 && item.modifiedShape.minY >= 0 &&
                item.modifiedShape.maxX < bounds.width && item.modifiedShape.maxY < bounds.height);
        if (anchoredItemsFit &&
            (InventorySortHelper.Sort(grid, bigFirst: true, fromEnd: false) ||
             InventorySortHelper.Sort(grid, bigFirst: true, fromEnd: true) ||
             InventorySortHelper.Sort(grid, bigFirst: false, fromEnd: false) ||
             InventorySortHelper.Sort(grid, bigFirst: false, fromEnd: true))) return;

        Expand(dossier);
        SpriteHelper.UpdateDossierSprite(grid, dossier);
    }
}

[HarmonyPatch(typeof(MiscItemDirectory), nameof(MiscItemDirectory.Dossier))]
internal static class DossierCreatedPatch
{
    private static void Postfix(GameItem __result)
    {
        if (ForgeDossier.ExpansionEnabled) ForgeDossier.Expand(__result);
    }
}

[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.LoadGame), new Type[0])]
internal static class DossierLoadPatch
{
    private static void Prefix()
    {
        if (ForgeDossier.ExpansionEnabled) ForgeDossier.Expand(EmporiumEntry.Instance?.dossier);
    }

    private static void Postfix()
    {
        if (!ForgeDossier.ExpansionEnabled) ForgeDossier.FitContents(EmporiumEntry.Instance?.dossier);
    }
}

[HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.UpgradeDossier))]
internal static class DossierUpgradePatch
{
    private static bool Prefix(EmporiumEntry __instance)
    {
        if (!ForgeDossier.ExpansionEnabled) return true;
        // The native upgrade sets 17x10. Do not shrink an expanded occupied grid.
        ForgeDossier.Expand(__instance.dossier, 17);
        return false;
    }

    private static void Postfix(EmporiumEntry __instance)
    {
        if (!ForgeDossier.ExpansionEnabled) ForgeDossier.FitContents(__instance.dossier);
    }
}

[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.InitialSave))]
internal static class OpeningDocumentsPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(PlayerStore __instance)
    {
        if (!ForgeDossier.ExpansionEnabled) return;
        // History branches also call InitialSave, but already have a run identity.
        if (!string.IsNullOrEmpty(__instance.runID) || __instance.saveSlotId >= 0) return;
        var entry = EmporiumEntry.Instance;
        if (entry?.invElement == null || entry.dossier?.contentWindow?.childElement is not GameGridInventory grid) return;
        ForgeDossier.Expand(entry.dossier);
        foreach (var item in entry.invElement.childItems.ToArray())
            if (GeneralHelper.IsItemOwned(item) && item.IsGameItemType("DOCUMENT"))
                GraphUtils.TryAcceptAll(grid, item);
    }
}
