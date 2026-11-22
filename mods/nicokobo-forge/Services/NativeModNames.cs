using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nicokobo.ModUtilities;

namespace Nicokobo.Forge;

[HarmonyPatch]
internal static class NativeModNames
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ModUIManager), "CreateRow");
        yield return AccessTools.Method(typeof(ModUIManager), "ShowMod");
        yield return AccessTools.Method(typeof(ModConfigWindow), nameof(ModConfigWindow.Open));
        yield return AccessTools.Method(typeof(ModSourceHelper), nameof(ModSourceHelper.GetModDisplayName));
        yield return AccessTools.Method(typeof(ModSourceHelper), nameof(ModSourceHelper.GetItemModName));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var manifestName = AccessTools.Field(typeof(ModManifest), nameof(ModManifest.Name));
        var displayName = AccessTools.Method(typeof(NativeModNames), nameof(DisplayName));
        var displayLabel = AccessTools.Method(typeof(NativeModNames), nameof(DisplayLabel));

        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, manifestName))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = displayName;
            }
            else if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method &&
                method.DeclaringType == typeof(ModLoader) && method.Name == nameof(ModLoader.GetModLabel))
            {
                instruction.operand = displayLabel;
            }
            yield return instruction;
        }
    }

    private static string DisplayName(ModManifest manifest) => FindName(manifest.ID) ?? manifest.Name;

    private static string DisplayLabel(ModManifest manifest) => FindName(manifest.ID) ?? ModLoader.GetModLabel(manifest);

    private static string? FindName(string? modId)
    {
        if (modId == null || !modId.StartsWith("nicokobo.", StringComparison.Ordinal)) return null;
        // Forge also carries translations for disabled mods whose own tables have not loaded.
        return ModText.Find(modId, "package.name", ModText.LocaleCode, ForgeMod.Id, "mod.name." + modId);
    }
}

[HarmonyPatch(typeof(ModUIManager), "Matches")]
internal static class NativeModNameSearch
{
    private static void Postfix(ModElement element, string ___filter, ref bool __result)
    {
        if (__result || element.ModID?.StartsWith("nicokobo.", StringComparison.Ordinal) != true) return;

        // The native search already checks the translated row name and the ID.
        var mod = ModLoader.Mods.Find(entry => entry.Manifest.ID == element.ModID);
        __result = mod?.Manifest.Name?.IndexOf(___filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
