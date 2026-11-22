using HarmonyLib;
using Nicokobo.FoodProcessing.Definitions;

namespace Nicokobo.FoodProcessing;

[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), "ClickAction")]
internal static class FoodCareKitClickPatch
{
    private static void Prefix(GameItem newItem, bool ___isDoubleClick, ref bool interact, ref bool activate)
    {
        if (!FoodDefinitions.IsCareKit(newItem.identifier)) return;

        // The locked window stores the ingredients. Unpack on the native double-click regardless of single-click settings.
        interact = false;
        activate = ___isDoubleClick;
    }
}
