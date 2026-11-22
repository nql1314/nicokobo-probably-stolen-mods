using DefinitelyStolen.Domain;
using DefinitelyStolen.Runtime;

namespace DefinitelyStolen.Integration;

internal static class NightEventBridge
{
    internal static void Register(RunController run, Catalog catalog)
    {
        foreach (var definition in NightEvents.All)
        {
            var effects = new List<AfterhourEffect>();
            foreach (var bonus in definition.RoleBonuses)
            {
                var role = catalog.Role(bonus.Key);
                effects.Add(ModHelper.AfterhourEffect(
                    () => Frozen(run, definition.Region) ? Native.Localize("tonight_s_targets_use_their_recorded_modifiers") :
                        Native.Localize("event.weight", role.LocalizedName, bonus.Value),
                    NightEvents.RoleKey(role.Id), (float)bonus.Value, AfterhourEffectPolarity.Good));
            }
            if (definition.ExposurePercent != 0)
                effects.Add(ModHelper.AfterhourEffect(
                    () => Frozen(run, definition.Region) ? Native.Localize("tonight_s_risk_uses_its_recorded_modifier") :
                        Native.Localize("event.exposure", definition.ExposurePercent),
                    NightEvents.ExposureKey, (float)definition.ExposurePercent,
                    definition.ExposurePercent > 0 ? AfterhourEffectPolarity.Bad : AfterhourEffectPolarity.Good));
            ModHelper.AddAfterhourEvent(Identity.Owner, definition.Id, Locations.Id(definition.Region),
                () => Native.Localize("event." + definition.Suffix + ".name"), durationDays: 1,
                description: () => Frozen(run, definition.Region)
                    ? Native.Localize("targets_are_already_fixed_the_scene_shows")
                    : Native.Localize("event." + definition.Suffix + ".description"),
                effects: effects, dailyChancePercent: definition.Chance);
        }
    }

    internal static RegionEventState[] Capture() => Enumerable.Range(0, 3)
        .Select(region => NightEvents.Snapshot(region, ModHelper.GetActiveAfterhourEventIds(Locations.Id(region)))).ToArray();

    private static RegionEventState? Recorded(RunController run, int region) => run.State?.Night is { } night && night.Day == Native.Day
        ? night.RegionEvents.Single(x => x.Region == region) : null;

    private static bool Frozen(RunController run, int region)
    {
        if (Recorded(run, region) is not { } snapshot) return false;
        var current = NightEvents.Snapshot(region, ModHelper.GetActiveAfterhourEventIds(Locations.Id(region)));
        return snapshot.RuleVersion == 0 || !snapshot.EventIds.ToHashSet(StringComparer.Ordinal).SetEquals(current.EventIds) ||
            snapshot.ExposurePercent != current.ExposurePercent || snapshot.RoleBonuses.Count != current.RoleBonuses.Count ||
            snapshot.RoleBonuses.Any(b => !current.RoleBonuses.TryGetValue(b.Key, out double value) || value != b.Value);
    }

    internal static string Title(RunController run, int region)
    {
        if (!Frozen(run, region) || Recorded(run, region) is not { } snapshot) return Locations.Title(region);
        string events = string.Join(", ", snapshot.EventIds.Select(id => NightEvents.All.Single(e => e.Id == id))
            .Select(e => Native.Localize("event." + e.Suffix + ".name")));
        return Locations.Title(region) + (events.Length == 0
            ? Native.Localize("event.none") : Native.Localize("event.fixed", events));
    }
}
