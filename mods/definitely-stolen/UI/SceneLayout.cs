using DefinitelyStolen.Domain;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DefinitelyStolen.UI;

internal sealed record SceneLayout(int Background, (float X, float Y, float Scale)[] People)
{
    internal const int BackgroundVariants = 3;
    internal const int PortraitVariants = 3;
    private static readonly string[] RegionNames = { "night-market", "transit", "upper-commercial" };
    private static readonly Dictionary<string, (float X, float Y, float Scale)[]> GroundPoints = LoadStandingPoints();

    internal static string SceneName(int region, int variant) => RegionNames[region] + "-" + (variant + 1).ToString("00");
    internal static string PortraitName(string roleId, int variant) => SpecialNpcs.Dedicated(roleId)?.Portrait
        ?? roleId + (variant == 0 ? "" : "-" + variant.ToString("00"));

    internal static string PortraitName(string runId, NpcState npc)
    {
        if (SpecialNpcs.Select(runId, npc) is { } special) return special.Portrait;
        // A character keeps the same appearance through attempts, reopening and save/load.
        int variant = Random($"{runId}:{npc.Key}:{npc.RoleId}:portrait").Index(PortraitVariants);
        return PortraitName(npc.RoleId, variant);
    }
    internal static IReadOnlyList<(float X, float Y, float Scale)> StandingPoints(int region, int variant) => GroundPoints[SceneName(region, variant)];

    internal static SceneLayout Create(string runId, NightState night, int region)
    {
        // Presentation owns its RNG: reopening or loading never advances loot/timing rolls.
        string key = FormattableString.Invariant($"{runId}:{night.Day}:{region}");
        int background = Random(key + ":background").Index(BackgroundVariants);
        var roster = night.Npcs.Where(x => x.Region == region).OrderBy(x => x.Position);
        var random = Random(key + ":people:" + string.Join(":", roster.Select(x => x.Key + ":" + x.RoleId)));
        var order = new[] { 0, 1, 2 };
        for (int i = order.Length - 1; i > 0; i--)
        {
            int other = random.Index(i + 1);
            (order[i], order[other]) = (order[other], order[i]);
        }

        // Randomize who occupies each authored ground point, never the ground itself.
        var points = StandingPoints(region, background);
        var people = new (float X, float Y, float Scale)[3];
        for (int i = 0; i < people.Length; i++) people[order[i]] = points[i];
        return new SceneLayout(background, people);
    }

    private sealed record GroundPoint(float X, float Y, float Scale);
    private sealed record GroundCatalog(float ImageWidth, float ImageHeight, Dictionary<string, GroundPoint[]> Scenes);

    private static Dictionary<string, (float X, float Y, float Scale)[]> LoadStandingPoints()
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("DefinitelyStolen.Assets.Scenes.standing-points.json")
            ?? throw new InvalidDataException("Missing scene standing points");
        var catalog = JsonConvert.DeserializeObject<GroundCatalog>(new StreamReader(input).ReadToEnd())
            ?? throw new InvalidDataException("Invalid scene standing points");
        if (!Numbers.Finite(catalog.ImageWidth) || !Numbers.Finite(catalog.ImageHeight) || catalog.ImageWidth <= 0 || catalog.ImageHeight <= 0)
            throw new InvalidDataException("Invalid standing point image dimensions");
        var points = new Dictionary<string, (float X, float Y, float Scale)[]>(StringComparer.Ordinal);
        for (int region = 0; region < RegionNames.Length; region++)
            for (int variant = 0; variant < BackgroundVariants; variant++)
            {
                string name = SceneName(region, variant);
                if (!catalog.Scenes.TryGetValue(name, out var entries) || entries.Length != 3 ||
                    entries.Any(p => !Numbers.Finite(p.X) || !Numbers.Finite(p.Y) || !Numbers.Finite(p.Scale) ||
                        p.X < 0 || p.X > catalog.ImageWidth || p.Y < 0 || p.Y > catalog.ImageHeight || p.Scale <= 0))
                    throw new InvalidDataException("Invalid standing points: " + name);
                points.Add(name, entries.Select(p => ((p.X / catalog.ImageWidth - .5f) * 1920,
                    (.5f - p.Y / catalog.ImageHeight) * 1080, p.Scale)).ToArray());
            }
        return points;
    }

    private static StableRandom Random(string key)
    {
        ulong seed = 14695981039346656037UL;
        foreach (char c in key) seed = unchecked((seed ^ c) * 1099511628211UL);
        return new StableRandom(seed);
    }
}
