namespace DefinitelyStolen.Domain;

internal sealed record SpecialNpc(string Id, string Name, string English, string RoleId, int Region, bool DedicatedRole = false,
    string? RareItem = null)
{
    internal string Portrait => "special-" + Id;
}

internal static class SpecialNpcs
{
    // These are theft gameplay roles, not replacements for the characters' story identities.
    internal static readonly IReadOnlyList<SpecialNpc> All = new[]
    {
        new SpecialNpc("mentor",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.mentor", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.mentor", "en"),
            "merchant",
            1,
            RareItem: "portable_water_purifier"),
        new SpecialNpc("sawyer",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.sawyer", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.sawyer", "en"),
            "streetCleaner",
            0,
            true),
        new SpecialNpc("joeWilde",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.joeWilde", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.joeWilde", "en"),
            "merchant",
            2,
            RareItem: "crypto_module_cmd"),
        new SpecialNpc("jackson",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.jackson", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.jackson", "en"),
            "lowerInventor",
            0,
            RareItem: "system_capped_neural_core"),
        new SpecialNpc("landlord",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.landlord", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.landlord", "en"),
            "upperLowerVisitorLuxury",
            2),
        new SpecialNpc("georgio",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.georgio", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.georgio", "en"),
            "scavGeneral",
            1),
        new SpecialNpc("officerRen",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.officerRen", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.officerRen", "en"),
            "offDutySecurity",
            1,
            true),
        new SpecialNpc("eleanor",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.eleanor", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.definitely_stolen", "theft.person.eleanor", "en"),
            "upperLevelHedonist",
            2)
    };

    internal static SpecialNpc? Dedicated(string roleId) => All.SingleOrDefault(x => x.DedicatedRole && x.RoleId == roleId);

    internal static IEnumerable<string> Items(string runId, NpcState npc, RoleDefinition role) =>
        Select(runId, npc)?.RareItem is { } rare ? role.Items.Append(rare).Distinct(StringComparer.Ordinal) : role.Items;

    internal static SpecialNpc? Select(string runId, NpcState npc)
    {
        var dedicated = Dedicated(npc.RoleId);
        if (dedicated != null) return dedicated;
        // A named person has one eligible region; region rosters draw each role at most once.
        var special = All.SingleOrDefault(x => x.RoleId == npc.RoleId && x.Region == npc.Region);
        if (special == null) return null;
        // Independent, deterministic appearance roll: never consumes the loot or timing RNG.
        ulong seed = 14695981039346656037UL;
        foreach (char c in $"{runId}:{npc.Key}:{npc.RoleId}:special") seed = unchecked((seed ^ c) * 1099511628211UL);
        return new StableRandom(seed).Unit() < .12 ? special : null;
    }

    internal static string DisplayName(string runId, NpcState npc, RoleDefinition role, bool english)
    {
        string occupation = english ? role.English : role.Name;
        var special = Select(runId, npc);
        return special == null ? occupation : (english ? special.English : special.Name) + " · " + occupation;
    }
}
