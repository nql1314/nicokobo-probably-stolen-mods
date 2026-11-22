using Nicokobo.ModUtilities;

namespace Nicokobo.ContentInterop;

internal static class ForgeText
{
    internal static string Get(string key, params object[] args) =>
        ModText.Get("nicokobo.forge", key, args);
}
