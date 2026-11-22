internal static class MechcorePackage
{
    internal const string Id = "nicokobo.mechcore_protocol";

    internal static bool IsPreviousPackage(string id) => id is
        "nicokobo.mechcore.synthesis" or "nicokobo.mechcore_protocol.module_matrix" or "nicokobo.aug";

    internal static string SpriteOwner(string owner) => IsPreviousPackage(owner) ? Id : owner;

    internal static bool PreviousBoolean(string owner, string key, bool fallback)
    {
        // Read the existing component's setting as the new package's default.
        // ModConfig.Define retains a saved value already present in the new package.
        var config = ModHelper.GetConfig(owner);
        return config.Get(key) is ConfigEntry<bool> saved ? saved.Value :
            config.Define(key, fallback, "Content", () => key).Value;
    }
}
