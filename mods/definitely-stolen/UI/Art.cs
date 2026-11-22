using DefinitelyStolen.Domain;
using UnityEngine;

namespace DefinitelyStolen.UI;

internal static class Art
{
    private static LoadedMod? _loaded;
    internal static void Load(LoadedMod loaded)
    {
        _loaded = loaded;
        var required = Enumerable.Range(0, 3).SelectMany(region => Enumerable.Range(0, 3).Select(variant => SceneLayout.SceneName(region, variant)))
            .Concat(Catalog.Load().Enabled.SelectMany(role => Enumerable.Range(0, 3).Select(v => SceneLayout.PortraitName(role.Id, v))))
            .Concat(SpecialNpcs.All.Select(x => x.Portrait)).Concat(new[] { "streetwise", "habitual", "pickpocket" }).Distinct();
        foreach (string key in required) if (!loaded.PaperSprites.ContainsKey(key)) throw new InvalidDataException("Missing theft artwork: " + key);
        foreach (string key in new[] { "pickpocket", "streetwise", "habitual" })
            loaded.PaperSprites[key].texture.filterMode = FilterMode.Point;
        if (!loaded.Sprites.ContainsKey("escape-smoke")) throw new InvalidDataException("Missing escape smoke item sprite");
    }
    internal static Sprite? TryGet(string key) => _loaded != null && _loaded.PaperSprites.TryGetValue(key, out var sprite) ? sprite : null;
    internal static Sprite Get(string key) => TryGet(key) ?? throw new InvalidDataException("Theft artwork has not loaded: " + key);
    internal static void Clear() => _loaded = null;
}
