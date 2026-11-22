using DefinitelyStolen.Domain;
using DefinitelyStolen.UI;

namespace DefinitelyStolen.Integration;

internal static class Perks
{
    private static Func<bool> _allowTheftWithoutPerk = () => false;
    internal static bool AllowTheftWithoutPerk => _allowTheftWithoutPerk();
    internal static bool MenuCareer => NewGameData.Instance != null && (NewGameData.Instance.startScenarioId == Identity.StartId || (int)NewGameData.Instance.startType == Identity.NativeStartType);
    internal static bool MenuQualified => AllowTheftWithoutPerk || MenuCareer || NewGameData.Instance?.startingPerks.Any(x => x.id == Identity.Streetwise) == true;
    internal static bool CanSteal(RunState? state) => state?.Qualified == true && (state.QualificationSource != "config" || AllowTheftWithoutPerk);
    internal static void ApplyTheftAccess(RunState state)
    {
        if (state.Qualified || !AllowTheftWithoutPerk) return;
        state.Qualified = true; state.QualificationSource = "config";
    }
    internal static void Register(Func<bool> allowTheftWithoutPerk)
    {
        _allowTheftWithoutPerk = allowTheftWithoutPerk;
        ModHelper.AddStartingPerk(Identity.Owner, Identity.Streetwise, 1, StartingPerk.StartingPerkType.POSITIVE,
            () => Native.Localize("streetwise"),
            () => Native.Localize("unlock_night_pickpocketing_at_proficiency_0_built"), Identity.Owner + ":streetwise");
        ModHelper.AddStartingPerk(Identity.Owner, Identity.Habitual, 2, StartingPerk.StartingPerkType.POSITIVE,
            () => Native.Localize("habitual_thief"),
            () => Native.Localize("requires_streetwise_or_the_theft_without_streetwise"), Identity.Owner + ":habitual");
    }
    internal static void EnsureNewCareer()
    { if (PlayerStore.Instance != null) EnsureBuiltIn(PlayerStore.Instance); }
    internal static void EnsureBuiltIn(PlayerStore store)
    {
        if (!Native.IsCareer(store) || store.startingPerks.Any(x => x.id == Identity.Streetwise)) return;
        var original = StartingPerkList.Perks?.SingleOrDefault(x => x.id == Identity.Streetwise) ?? throw new InvalidOperationException("Streetwise definition unavailable");
        store.startingPerks.Add(new StartingPerk { id = Identity.Streetwise, cost = 0, maxSlot = 0, source = Identity.Owner,
            type = StartingPerk.StartingPerkType.POSITIVE, nameProvider = original.nameProvider, descriptionProvider = original.descriptionProvider, iconKey = original.iconKey });
    }
    internal static bool MenuLocked(string id) => id == Identity.Habitual && !MenuQualified || id == Identity.Streetwise && (MenuCareer || AllowTheftWithoutPerk);
    internal static void NormalizeMenu()
    {
        if (NewGameData.Instance == null) return;
        var selected = NewGameData.Instance.startingPerks;
        if (MenuCareer || AllowTheftWithoutPerk) selected.RemoveAll(x => x.id == Identity.Streetwise);
        if (!MenuQualified) selected.RemoveAll(x => x.id == Identity.Habitual);
    }
    internal static void InstallIcons()
    {
        if (StartingPerkIconLoader.perkIcons == null) return;
        if (Art.TryGet("streetwise") is { } streetwise) StartingPerkIconLoader.perkIcons[Identity.Streetwise] = streetwise;
        if (Art.TryGet("habitual") is { } habitual) StartingPerkIconLoader.perkIcons[Identity.Habitual] = habitual;
    }
}
