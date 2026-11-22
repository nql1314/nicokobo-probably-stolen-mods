namespace AugMechanicalAscension.Integration;
internal static class JacksonNoteIconRuntime
{
    private static Action<string>? _log;
    private static bool _reported;
    internal static void Configure(Action<string> log) => _log = log;
    internal static void TryInstall()
    {
        var assets = StoryIconAtlas.Assets;
        if (_reported || assets == null) return;
        _reported = true;
        _log?.Invoke(assets.Sprites.ContainsKey("jackson_note")
            ? "[Story] Opening hint artwork available from official mod assets"
            : "[WARN] [Story] Opening hint artwork is missing from official Textures/Items");
    }
    internal static void AssignOrStage(GameItem item)
    {
        item.SetSprite("nicokobo.aug", "jackson_note"); item.useSpriteFromMod = true;
    }
}
