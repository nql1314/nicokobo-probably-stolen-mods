using System.Text.Json;
using System.Reflection;
using DefinitelyStolen.Domain;
using DefinitelyStolen.UI;

internal static class SceneLayoutChecks
{
    internal static void Run(Action<bool, string> check)
    {
        bool stable = true, unchanged = true, inBounds = true, separate = true, perspective = true;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DefinitelyStolen.Assets.Scenes.standing-points.json")!;
        using var standingPoints = JsonDocument.Parse(stream);
        var pointsByScene = standingPoints.RootElement.GetProperty("Scenes");
        bool grounded = true;
        var backgrounds = new HashSet<int>[3] { new(), new(), new() };
        var ranks = new HashSet<int>[3] { new(), new(), new() };
        var orders = new HashSet<string>();
        var heights = new HashSet<float>[3] { new(), new(), new() };
        for (int day = 0; day < 128; day++)
        {
            var night = new NightState { Day = day, RandomState = 123456789 };
            for (int region = 0; region < 3; region++)
                for (int slot = 0; slot < 3; slot++)
                    night.Npcs.Add(new NpcState { Key = $"{day}:{region}:{slot}", Region = region, Position = slot, RoleId = "role" + slot });
            string saved = JsonSerializer.Serialize(night);
            var reloaded = JsonSerializer.Deserialize<NightState>(saved)!;
            reloaded.Npcs.Reverse();
            for (int region = 0; region < 3; region++)
            {
                var scene = SceneLayout.Create("layout-check-run", night, region);
                string sceneName = new[] { "night-market", "transit", "upper-commercial" }[region] + "-" + (scene.Background + 1).ToString("00");
                var fixedFeet = pointsByScene.GetProperty(sceneName).EnumerateArray().Select(p =>
                    (X: (p.GetProperty("X").GetSingle() / 1536 - .5f) * 1920,
                     Y: (.5f - p.GetProperty("Y").GetSingle() / 864) * 1080,
                     Scale: p.GetProperty("Scale").GetSingle())).ToArray();
                grounded &= scene.People.All(p => fixedFeet.Any(f => Math.Abs(p.X - f.X) < .001f && Math.Abs(p.Y - f.Y) < .001f && p.Scale == f.Scale));
                var reopened = SceneLayout.Create("layout-check-run", reloaded, region);
                stable &= scene.Background == reopened.Background && scene.People.SequenceEqual(reopened.People);
                backgrounds[region].Add(scene.Background);
                var order = Enumerable.Range(0, 3).OrderBy(i => scene.People[i].X).ToArray();
                orders.Add(string.Join(",", order));
                for (int rank = 0; rank < order.Length; rank++) ranks[order[rank]].Add(rank);
                for (int i = 0; i < scene.People.Length; i++) heights[i].Add(scene.People[i].Y);
            }
            unchanged &= JsonSerializer.Serialize(night) == saved;
        }
        bool footAligned = true, labelsBelowFeet = true, humanScale = true;
        var roles = Catalog.Load().Enabled;
        var portraits = roles.SelectMany(role => Enumerable.Range(0, SceneLayout.PortraitVariants)
            .Select(variant => SceneLayout.PortraitName(role.Id, variant))).Concat(SpecialNpcs.All.Select(x => x.Portrait)).Distinct().ToArray();
        for (int region = 0; region < 3; region++)
            for (int variant = 0; variant < SceneLayout.BackgroundVariants; variant++)
            {
                var points = SceneLayout.StandingPoints(region, variant);
                check(points.Count == 3 && points.Distinct().Count() == 3, $"Three distinct authored floor points: {SceneLayout.SceneName(region, variant)}");
                for (int i = 0; i < points.Count; i++)
                    foreach (string portrait in portraits)
                    {
                        var point = points[i];
                        humanScale &= point.Scale is >= .65f and <= .90f;
                        var center = PortraitAnchors.Center(point, portrait);
                        var foot = PortraitAnchors.Local(portrait);
                        footAligned &= Math.Abs(center.X + foot.X * center.Scale - point.X) < .001f &&
                            Math.Abs(center.Y + foot.Y * center.Scale - point.Y) < .001f;
                        float halfWidth = PortraitAnchors.Width / 2 * center.Scale;
                        inBounds &= center.X - halfWidth >= -940 && center.X + halfWidth < 395 &&
                            center.Y + 321 * center.Scale < 484 && center.Y - 310 * center.Scale > -360;
                        labelsBelowFeet &= point.Y - 52 > -320;
                        for (int j = i + 1; j < points.Count; j++)
                        {
                            var otherPoint = points[j];
                            perspective &= (point.Y - otherPoint.Y) * (point.Scale - otherPoint.Scale) <= 0;
                            foreach (string otherPortrait in portraits)
                            {
                                var other = PortraitAnchors.Center(otherPoint, otherPortrait);
                                separate &= Math.Abs(center.X - other.X) > 150 * (center.Scale + other.Scale);
                            }
                        }
                    }
            }
        check(grounded, "Every NPC foot uses one of the three authored ground points for its actual background");
        check(footAligned, "Every original and additional portrait shoe anchor coincides with each scene ground point after scaling");
        check(humanScale, "Authored perspective scales keep people below the previous oversized baseline");
        check(labelsBelowFeet, "Role names and selection underlines remain below the shoe contact point and clear of the lower edge");
        check(stable, "Reopening and JSON reload retain the same background and positions regardless of roster list order");
        check(unchanged, "Visual layout leaves saved NPC state and gameplay RNG unchanged");
        check(inBounds, "Randomized portraits, names and alerts stay on the walkway and clear of screen edges and controls");
        check(separate, "Every NPC retains an unobstructed, non-overlapping click target");
        check(perspective, "Characters further back are smaller");
        check(orders.Count == 6 && ranks.All(x => x.Count == 3), "Every NPC can occupy left, middle or right in all six orders");
        check(heights.All(x => x.Count >= 3), "Each NPC can occupy different authored ground heights across nights");
        check(backgrounds.All(x => x.SetEquals(Enumerable.Range(0, SceneLayout.BackgroundVariants))), "Every region can select all three current backgrounds");
        var sameNight = new NightState { Day = 5 };
        var first = SceneLayout.Create("first-run", sameNight, 0);
        var second = SceneLayout.Create("second-run", sameNight, 0);
        check(!first.People.SequenceEqual(second.People), "Different runs receive independent visual layouts");

        bool portraitStable = true, appearanceIgnoresAttempts = true, portraitStateUnchanged = true, allVariants = true;
        foreach (var role in roles)
        {
            var seen = new HashSet<string>();
            for (int day = 0; day < 128; day++)
            {
                var npc = new NpcState { Key = $"{day}:1:2", Region = 1, Position = 2, RoleId = role.Id, Sprite = "saved-native-portrait" };
                string saved = JsonSerializer.Serialize(npc);
                string portrait = SceneLayout.PortraitName("portrait-check-run", npc);
                if (SpecialNpcs.Select("portrait-check-run", npc) is not { DedicatedRole: false }) seen.Add(portrait);
                var reloaded = JsonSerializer.Deserialize<NpcState>(saved)!;
                portraitStable &= portrait == SceneLayout.PortraitName("portrait-check-run", reloaded);
                portraitStateUnchanged &= saved == JsonSerializer.Serialize(npc);
                reloaded.Attempts = 3; reloaded.SuccessfulThefts = 1; reloaded.Alert = true;
                appearanceIgnoresAttempts &= portrait == SceneLayout.PortraitName("portrait-check-run", reloaded);
            }
            allVariants &= seen.SetEquals(Enumerable.Range(0, SceneLayout.PortraitVariants).Select(v => SceneLayout.PortraitName(role.Id, v)));
        }
        check(portraitStable, "A saved NPC keeps the same portrait after JSON reload");
        check(appearanceIgnoresAttempts, "Attempts and alerts do not change an NPC's appearance");
        check(portraitStateUnchanged, "Portrait selection leaves saved NPC state and native sprite keys untouched");
        check(allVariants, "Every enabled role can show its original portrait and both additions across nights");
    }
}
