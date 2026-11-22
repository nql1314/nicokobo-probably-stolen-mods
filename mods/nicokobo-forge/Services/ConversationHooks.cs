using HarmonyLib;
using System.Reflection;

namespace Nicokobo.ContentInterop;

internal static class OfficialStoryGame
{
    internal static PlayerStore? Store => PlayerStore.HasInstance ? PlayerStore.Instance : null;
}

internal static class OfficialStoryHooks
{
    internal static bool ValidateTarget(MethodBase? method, out string reason)
    {
        if (method == null || method.DeclaringType?.Assembly != typeof(PlayerStore).Assembly || method.IsAbstract)
        { reason = "The exact managed game method is unavailable"; return false; }
        reason = "";
        return true;
    }

    internal static MethodInfo Require(Type type, string name, Type[] arguments)
    {
        var method = type == typeof(DialogUIManager) ? AccessTools.Method(typeof(DialogUIManager), name, arguments) :
            type == typeof(StorePhoneClient) ? AccessTools.Method(typeof(StorePhoneClient), name, arguments) :
            type == typeof(PhoneBookUIManager) ? AccessTools.Method(typeof(PhoneBookUIManager), name, arguments) :
            type == typeof(PhoneUIManager) ? AccessTools.Method(typeof(PhoneUIManager), name, arguments) :
            throw new ArgumentException("Unsupported presentation hook type", nameof(type));
        if (!ValidateTarget(method, out var reason)) throw new MissingMethodException(type.FullName, name + ": " + reason);
        return method!;
    }
}
