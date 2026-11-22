using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Nicokobo.Forge;

[HarmonyPatch(typeof(LocHelper), nameof(LocHelper.SetLocale), MethodType.Enumerator)]
internal static class NativeLocaleSwitch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var body = instructions.ToList();
        var setters = body.Where(instruction => instruction.opcode == OpCodes.Call &&
            instruction.operand is MethodInfo method && method.DeclaringType == typeof(LocalizationSettings) &&
            method.Name == "set_" + nameof(LocalizationSettings.SelectedLocale)).ToArray();
        if (setters.Length != 1)
        {
            Debug.LogWarning("[Nicokobo Forge] Locale switch patch skipped: expected one locale setter.");
            return body;
        }

        setters[0].operand = AccessTools.Method(typeof(NativeLocaleSwitch), nameof(ApplyLocale));
        return body;
    }

    private static void ApplyLocale(Locale locale)
    {
        // The game's coroutine has awaited the previous locale. Complete the new preload
        // before a UI lookup can queue the same table and invalidate a group dependency.
        Debug.Log("[Nicokobo Forge] Applying game locale " + locale.Identifier.Code);
        bool previous = LocalizationSettings.InitializeSynchronously;
        LocalizationSettings.InitializeSynchronously = true;
        try { LocalizationSettings.SelectedLocale = locale; }
        finally { LocalizationSettings.InitializeSynchronously = previous; }
    }
}
