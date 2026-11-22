using DefinitelyStolen.Domain;
using DefinitelyStolen.Integration;

internal static class PerkChecks
{
    internal static void Run(Action<bool, string> check)
    {
        bool allowTheftWithoutPerk = false;
        StartingPerkList.Perks.Clear(); Perks.Register(() => allowTheftWithoutPerk);
        var streetwise = StartingPerkList.Perks.Single(x => x.id == Identity.Streetwise);
        var habitual = StartingPerkList.Perks.Single(x => x.id == Identity.Habitual);
        check(streetwise.cost == 1 && habitual.cost == 2, "The official selectable definitions retain their costs");
        var store = new PlayerStore { startScenarioId = Identity.StartId, startType = NewGameData.StartType.MODDED };
        PlayerStore.Instance = store;
        check(StartingPerk.GetPerkById(Identity.Streetwise) == null, "The native active-perk lookup is empty before the built-in grant");
        Perks.EnsureBuiltIn(store); Perks.EnsureBuiltIn(store);
        check(store.startingPerks.Count == 1 && store.startingPerks[0].id == Identity.Streetwise && store.startingPerks[0].cost == 0 && store.startingPerks[0].maxSlot == 0,
            "A Pickpocket receives exactly one free built-in perk from the definition directory");
        check(streetwise.cost == 1, "Granting the built-in copy cannot change the selectable definition's cost");
        NewGameData.Instance = new NewGameData { startScenarioId = Identity.StartId, startType = NewGameData.StartType.MODDED, startingPerks = new() { streetwise, habitual } };
        Perks.NormalizeMenu();
        check(NewGameData.Instance.startingPerks.SequenceEqual(new[] { habitual }) && Perks.MenuLocked(Identity.Streetwise) && !Perks.MenuLocked(Identity.Habitual),
            "The current official perk screen charges no redundant Streetwise choice for a Pickpocket and still permits Habitual");
        NewGameData.Instance = new NewGameData();
        check(Perks.MenuLocked(Identity.Habitual) && !Perks.MenuLocked(Identity.Streetwise), "Other starts must obtain Streetwise before selecting Habitual");
        NewGameData.Instance.startingPerks.AddRange(new[] { streetwise, habitual }); Perks.NormalizeMenu();
        check(NewGameData.Instance.startingPerks.Count == 2 && !Perks.MenuLocked(Identity.Habitual), "A qualified other start can retain both paid choices");
        NewGameData.Instance.startingPerks.Remove(streetwise); Perks.NormalizeMenu();
        check(NewGameData.Instance.startingPerks.Count == 0, "Removing prerequisite Streetwise also removes a stale Habitual choice before the official UI rebuild");
        var unrelated = new PlayerStore(); Perks.EnsureBuiltIn(unrelated);
        check(unrelated.startingPerks.Count == 0, "Other careers do not receive the built-in perk");
        var existing = new RunState { Experience = 150, Wind = 17, RandomState = 123, Revision = 4, Night = new NightState { Day = 7 } };
        var night = existing.Night;
        Perks.ApplyTheftAccess(existing);
        check(!Perks.CanSteal(existing) && existing.QualificationSource == "none" && !Perks.CanSteal(null),
            "With the setting off, unrelated saves and missing run state cannot steal");
        allowTheftWithoutPerk = true;
        Perks.ApplyTheftAccess(existing);
        check(Perks.CanSteal(existing) && existing.QualificationSource == "config" && !existing.OpeningGranted && unrelated.startingPerks.Count == 0,
            "The setting unlocks an existing character without granting perks or opening rewards");
        check(existing.Experience == 150 && existing.Wind == 17 && existing.RandomState == 123 && existing.Revision == 4 && ReferenceEquals(existing.Night, night),
            "Configuration access preserves progress, random state and the existing night");
        NewGameData.Instance.startingPerks.AddRange(new[] { streetwise, habitual }); Perks.NormalizeMenu();
        check(NewGameData.Instance.startingPerks.SequenceEqual(new[] { habitual }) && Perks.MenuLocked(Identity.Streetwise) && !Perks.MenuLocked(Identity.Habitual),
            "Configuration access permits Habitual directly and removes the redundant paid Streetwise choice");
        allowTheftWithoutPerk = false;
        Perks.ApplyTheftAccess(existing);
        check(!Perks.CanSteal(existing) && existing.Experience == 150 && ReferenceEquals(existing.Night, night),
            "Disabling configuration access blocks theft without discarding progress or the existing night");
        Perks.NormalizeMenu();
        check(NewGameData.Instance.startingPerks.Count == 0 && Perks.MenuLocked(Identity.Habitual),
            "Disabling configuration access restores the Habitual prerequisite for other starts");
        foreach (string source in new[] { "perk", "career" })
        {
            var qualified = new RunState { Qualified = true, QualificationSource = source };
            allowTheftWithoutPerk = true; Perks.ApplyTheftAccess(qualified); allowTheftWithoutPerk = false;
            check(Perks.CanSteal(qualified) && qualified.QualificationSource == source,
                "Toggling configuration preserves native qualification: " + source);
        }
        allowTheftWithoutPerk = true;
        Perks.ApplyTheftAccess(existing);
        check(Perks.CanSteal(existing) && existing.Experience == 150 && ReferenceEquals(existing.Night, night),
            "Re-enabling configuration resumes the same theft progress and night");
        allowTheftWithoutPerk = false;
        NewGameData.Instance = null;
    }
}
