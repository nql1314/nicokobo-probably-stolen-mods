using UnityEngine;
namespace AugMechanicalAscension.Integration;
internal static class StoryPortraits
{
    private static readonly Dictionary<string, Sprite> Portraits = new();
    internal static string ForEvent(string? id) => id switch
    {
        "E01" => "returning_customer", "E03" or "E05" or "E08" => "lin_cen",
        "E09" or "E10" => "shao_heng", _ => "grayline"
    };
    internal static string Register(string character)
    {
        if (character is not ("returning_customer" or "lin_cen" or "grayline" or "shao_heng"))
            throw new ArgumentException("Unknown story character: " + character);
        var assets = StoryIconAtlas.Assets;
        if (SpriteDict.Instance?.spriteDictionary == null || assets == null || !assets.Textures.TryGetValue(character, out var texture))
            throw new InvalidOperationException("Official story portrait is unavailable: " + character);
        string key = "nicokobo_aug_story_" + character;
        if (!Portraits.TryGetValue(key, out var sprite) || sprite == null)
            Portraits[key] = sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
        var dictionary = SpriteDict.Instance.spriteDictionary;
        if (dictionary.TryGetValue(key, out var existing) && existing != sprite)
            throw new InvalidOperationException("Story portrait key belongs to another provider: " + key);
        dictionary[key] = sprite; return key;
    }
    internal static void Clear()
    {
        var dictionary = SpriteDict.Instance?.spriteDictionary;
        foreach (var pair in Portraits)
        {
            if (dictionary?.TryGetValue(pair.Key, out var existing) == true && existing == pair.Value) dictionary.Remove(pair.Key);
            if (pair.Value != null) UnityEngine.Object.Destroy(pair.Value);
        }
        Portraits.Clear();
    }
}
