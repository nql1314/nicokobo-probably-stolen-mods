using AugMechanicalAscension.Domain;
using UnityEngine;
namespace AugMechanicalAscension.Integration;
internal static class StoryIconAtlas
{
    private static readonly HashSet<string> Keys = new([.. StoryEvidence.Keys, "distress_core"], StringComparer.Ordinal);
    private static readonly Dictionary<string, Sprite> MagnifierSprites = new();
    private static Action<string>? _log;
    internal static LoadedMod? Assets => ModLoader.Mods.FirstOrDefault(mod => mod.Manifest.ID == MechcorePackage.Id);
    internal static void Configure(Action<string> log) => _log = log;
    internal static bool TryInstall() => Assets is { } mod && Keys.All(key => mod.Sprites.ContainsKey(key));
    internal static void AssignOrStage(GameItem item, string key)
    {
        if (!Keys.Contains(key)) throw new ArgumentException("Unknown story icon: " + key);
        item.SetSprite(MechcorePackage.Id, key); item.useSpriteFromMod = true;
    }
    internal static Texture2D? MagnifierTexture(string key)
    {
        if (key is not ("broken" or "photo")) throw new ArgumentException("Unknown magnifier image: " + key);
        if (Assets?.Textures.TryGetValue("magnifier_" + key, out var image) == true) return image;
        _log?.Invoke("[WARN] [Story] Official magnifier image is unavailable: " + key); return null;
    }
    internal static Sprite? MagnifierSprite(string key)
    {
        if (MagnifierSprites.TryGetValue(key, out var sprite) && sprite != null) return sprite;
        var image = MagnifierTexture(key); if (image == null) return null;
        sprite = Sprite.Create(image, new Rect(0, 0, image.width, image.height), new Vector2(.5f, .5f), 100);
        MagnifierSprites[key] = sprite; return sprite;
    }
    internal static void Clear()
    {
        foreach (var sprite in MagnifierSprites.Values) if (sprite != null) UnityEngine.Object.Destroy(sprite);
        MagnifierSprites.Clear();
    }
}
