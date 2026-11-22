using HarmonyLib;
using System.Reflection;

namespace Nicokobo.ContentInterop;

internal sealed record NativeHook(Type TargetType, string Method, Type[] Arguments, Type ReturnType,
    Type CallbackType, string? Prefix = null, string? Postfix = null, string? Finalizer = null);
internal static class NativeHookSet
{
    private static readonly Dictionary<string, Harmony> Owners = new();
    internal static bool Install(string owner, IReadOnlyList<NativeHook> hooks, Action<string> log)
    {
        var harmony = new Harmony(owner);
        try
        {
            var verified = hooks.Select(hook => (Hook: hook, Target: (hook.TargetType==typeof(WaterHelper)
                ? AccessTools.Method(typeof(WaterHelper), hook.Method, hook.Arguments)
                : hook.TargetType==typeof(GameItem) ? AccessTools.Method(typeof(GameItem), hook.Method, hook.Arguments)
                : throw new ArgumentException("Unsupported liquid hook class"))
                ?? throw new MissingMethodException(hook.TargetType.FullName, hook.Method))).ToArray();
            if (verified.Any(entry => entry.Target.ReturnType != entry.Hook.ReturnType || entry.Target.IsAbstract)) throw new InvalidOperationException("Official method signature changed");
            foreach (var (hook, target) in verified)
            {
                HarmonyMethod? Callback(string? name) => name == null ? null : new HarmonyMethod(hook.CallbackType, name);
                harmony.Patch(target, Callback(hook.Prefix), Callback(hook.Postfix), finalizer: Callback(hook.Finalizer));
            }
            Owners.Add(owner, harmony); return true;
        }
        catch (Exception error) { harmony.UnpatchAll(harmony.Id); log("Official liquid hooks failed: " + error); throw; }
    }
    internal static void Remove(string owner, Action<string>? log)
    { if (Owners.TryGetValue(owner, out var harmony)) { harmony.UnpatchAll(harmony.Id); Owners.Remove(owner); } }
}
