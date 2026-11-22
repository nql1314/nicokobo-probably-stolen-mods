using Nicokobo.ModUtilities;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class SynthesisText
{
    internal static string Get(string key, params object[] args) => ModText.Get(MechcorePackage.Id, key, args);
    internal static string GetFor(string key, bool? english, params object[] args) =>
        english.HasValue ? ModText.GetFor(MechcorePackage.Id, key, english.Value ? "en" : "zh", args) : Get(key, args);
}
