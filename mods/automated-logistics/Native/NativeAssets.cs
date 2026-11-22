using System.Reflection;
using UnityEngine;

namespace Nicokobo.AutomatedLogistics.Native;

internal sealed record NativeSpriteDefinition(string Key, string Resource, int Width, int Height);
internal static class NativeAssetsApi
{
    internal static NativeSpriteAtlas CreateEmbeddedSpriteAtlas(string owner, string atlas, Assembly assembly, IReadOnlyList<NativeSpriteDefinition> sprites) => new(atlas, assembly, sprites);
}
internal sealed class NativeSpriteAtlas(string atlas, Assembly assembly, IReadOnlyList<NativeSpriteDefinition> sprites)
{
    internal bool Installed { get; private set; }
    internal int Count => Installed ? sprites.Count : 0;
    internal void TryInstall(Action? success, Action<Exception>? failure)
    {
        if (Installed) return;
        try
        {
            var cache = new Dictionary<string, Sprite>();
            foreach (var definition in sprites)
            {
                using var stream = assembly.GetManifestResourceStream(definition.Resource) ?? throw new InvalidOperationException("Sprite missing: " + definition.Key);
                using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                if (!ImageConversion.LoadImage(texture, bytes.ToArray(), false) || texture.width != definition.Width || texture.height != definition.Height)
                    throw new InvalidDataException("Invalid sprite: " + definition.Key);
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
                sprite.hideFlags = HideFlags.HideAndDontSave; cache.Add(definition.Key, sprite);
            }
            if (NativeAccess.Atlases.ContainsKey(atlas)) throw new InvalidOperationException("Sprite atlas ID already owned: " + atlas);
            NativeAccess.Atlases.Add(atlas, cache); Installed = true; success?.Invoke();
        }
        catch (Exception error) { failure?.Invoke(error); throw; }
    }
    internal void Assign(GameItem item, string key, bool allowStaging)
    { if (!Installed) TryInstall(null, null); item.SetSprite(atlas, key); }
    internal void Clear()
    {
        if (!Installed || !NativeAccess.Atlases.TryGetValue(atlas, out var cache)) return;
        foreach (var sprite in cache.Values) { UnityEngine.Object.Destroy(sprite.texture); UnityEngine.Object.Destroy(sprite); }
        NativeAccess.Atlases.Remove(atlas); Installed = false;
    }
}
