namespace DefinitelyStolen.Domain;

internal sealed record NightEventDefinition(string Suffix, int Region, string Name, string English, string Description,
    string EnglishDescription, int Chance, IReadOnlyDictionary<string, double> RoleBonuses, double ExposurePercent = 0)
{
    internal string Id => Identity.Owner + ".event." + Suffix;
}

internal static class NightEvents
{
    internal const int RuleVersion = 1;
    internal static string RoleKey(string role) => Identity.Owner + ".role." + role;
    internal const string ExposureKey = Identity.Owner + ".exposure_percent";
    internal static readonly IReadOnlyList<NightEventDefinition> All = new[]
    {
        new NightEventDefinition("restock_night",
            0,
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.restock_night.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.restock_night.name", "en"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.restock_night.description", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.restock_night.description", "en"),
            20,
            new Dictionary<string, double> { ["merchant"] = 50, ["aquacultureTechnician"] = 50, ["retiredAquafarmer"] = 50 }),
        new NightEventDefinition("harvest_market",
            0,
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.harvest_market.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.harvest_market.name", "en"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.harvest_market.description", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.harvest_market.description", "en"),
            12,
            new Dictionary<string, double> { ["hydroponicGrower"] = 100, ["familyMother"] = 50, ["spacerChef"] = 50 }),
        new NightEventDefinition("shift_change",
            1,
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.shift_change.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.shift_change.name", "en"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.shift_change.description", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.shift_change.description", "en"),
            18,
            new Dictionary<string, double> { ["maintenanceWorker"] = 75, ["printTechnician"] = 75,
                ["waterQualityInspector"] = 75, ["clinicCourier"] = 75 },
            -15),
        new NightEventDefinition("cargo_transfer",
            1,
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.cargo_transfer.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.cargo_transfer.name", "en"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.cargo_transfer.description", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.cargo_transfer.description", "en"),
            12,
            new Dictionary<string, double> { ["merchant"] = 75, ["salvagePilot"] = 75, ["aquacultureTechnician"] = 75 }),
        new NightEventDefinition("tech_exchange",
            2,
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.tech_exchange.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.tech_exchange.name", "en"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.tech_exchange.description", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.tech_exchange.description", "en"),
            15,
            new Dictionary<string, double> { ["printTechnician"] = 100, ["plantBreeder"] = 100,
                ["stationEngineer"] = 50, ["aiEngineer"] = 50 }),
        new NightEventDefinition("security_check",
            2,
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.security_check.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.security_check.name", "en"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.security_check.description", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.event.security_check.description", "en"),
            15,
            new Dictionary<string, double>(),
            25)
    };

    internal static RegionEventState Snapshot(int region, IEnumerable<string> activeIds)
    {
        var active = activeIds.ToHashSet(StringComparer.Ordinal);
        var result = new RegionEventState { Region = region, RuleVersion = RuleVersion };
        foreach (var definition in All.Where(x => x.Region == region && active.Contains(x.Id)))
        {
            result.EventIds.Add(definition.Id);
            result.ExposurePercent += definition.ExposurePercent;
            foreach (var bonus in definition.RoleBonuses)
                result.RoleBonuses[bonus.Key] = result.RoleBonuses.TryGetValue(bonus.Key, out double value) ? value + bonus.Value : bonus.Value;
        }
        return result;
    }

    internal static double Weight(NightState night, int region, RoleWeight role)
    {
        var snapshot = night.RegionEvents.Single(x => x.Region == region);
        return role.Weight * (1 + (snapshot.RoleBonuses.TryGetValue(role.Id, out double bonus) ? bonus : 0) / 100);
    }

    internal static string Summary(RegionEventState snapshot, Catalog catalog, bool? english = null, bool details = false)
    {
        if (snapshot.RuleVersion == 0)
            return CatalogText.GetFor("tonight_s_targets_are_fixed_no_event", english);
        string names = string.Join(", ", snapshot.EventIds.Select(id => All.Single(x => x.Id == id))
            .Select(x => CatalogText.GetFor("event." + x.Suffix + ".name", english)));
        var lines = new List<string> { CatalogText.GetFor("tonight", english) + (names.Length == 0 ? CatalogText.GetFor("quiet_night", english) : names) };
        if (snapshot.ExposurePercent != 0)
            lines.Add(CatalogText.GetFor("event.exposure", english, snapshot.ExposurePercent));
        if (details)
            foreach (var bonus in snapshot.RoleBonuses)
            {
                var role = catalog.Role(bonus.Key);
                string name = english.HasValue ? english.Value ? role.English : role.Name : role.LocalizedName;
                lines.Add(CatalogText.GetFor("event.weight", english, name, bonus.Value));
            }
        return string.Join("\n", lines);
    }

    internal static void Validate(IReadOnlyList<RegionEventState> snapshots, Catalog catalog)
    {
        if (!snapshots.Select(x => x.Region).OrderBy(x => x).SequenceEqual(new[] { 0, 1, 2 }))
            throw new InvalidDataException("Invalid regional event snapshots");
        foreach (var snapshot in snapshots)
        {
            if (snapshot.RuleVersion is not (0 or RuleVersion) || !Numbers.Finite(snapshot.ExposurePercent) || snapshot.ExposurePercent < -100 ||
                snapshot.EventIds.Distinct().Count() != snapshot.EventIds.Count ||
                snapshot.EventIds.Any(id => !All.Any(e => e.Id == id && e.Region == snapshot.Region)) ||
                snapshot.RoleBonuses.Any(b => !Numbers.Finite(b.Value) || b.Value <= -100 ||
                    !catalog.Regions.Single(r => r.Id == snapshot.Region).Roles.Any(r => r.Id == b.Key)) ||
                snapshot.RuleVersion == 0 && (snapshot.EventIds.Count != 0 || snapshot.RoleBonuses.Count != 0 || snapshot.ExposurePercent != 0))
                throw new InvalidDataException("Invalid saved night event rules");
        }
    }
}
