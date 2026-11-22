using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DefinitelyStolen.UI;

internal static class PortraitAnchors
{
    internal const float Width = 346.6667f, Height = 520, OffsetY = -10;
    private static readonly Dictionary<string, (float X, float Y)> Feet = Load();

    internal static (float X, float Y) Local(string roleId) => Feet[roleId];

    internal static (float X, float Y, float Scale) Center((float X, float Y, float Scale) ground, string roleId)
    {
        var foot = Local(roleId);
        return (ground.X - foot.X * ground.Scale, ground.Y - foot.Y * ground.Scale, ground.Scale);
    }

    private sealed record Foot(float X, float Y);

    private static Dictionary<string, (float X, float Y)> Load()
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("DefinitelyStolen.Assets.Portraits.foot-anchors.json")
            ?? throw new InvalidDataException("Missing portrait foot anchors");
        var anchors = JsonConvert.DeserializeObject<Dictionary<string, Foot>>(new StreamReader(input).ReadToEnd())
            ?? throw new InvalidDataException("Invalid portrait foot anchors");
        if (anchors.Values.Any(p => !Numbers.Finite(p.X) || !Numbers.Finite(p.Y) || p.X is < 0 or > 1 || p.Y is < 0 or > 1))
            throw new InvalidDataException("Invalid normalized portrait foot anchor");
        return anchors.ToDictionary(p => p.Key, p => ((p.Value.X - .5f) * Width, OffsetY + (.5f - p.Value.Y) * Height), StringComparer.Ordinal);
    }
}
