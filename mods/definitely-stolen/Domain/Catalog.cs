using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nicokobo.ModUtilities;

namespace DefinitelyStolen.Domain;

internal sealed class Catalog
{
    public List<RoleDefinition> Roles { get; set; } = new();
    public List<RegionDefinition> Regions { get; set; } = new();
    public List<ItemDefinition> Items { get; set; } = new();
    internal IReadOnlyList<RoleDefinition> Enabled => Roles.Where(x => x.Enabled).ToArray();
    internal IReadOnlyList<RoleDefinition> OpeningRoles => Roles.Where(x => x.Enabled && x.OpeningAllowed).ToArray();
    internal HashSet<string> DropIds => Enabled.SelectMany(x => x.Items)
        .Concat(SpecialNpcs.All.Select(x => x.RareItem).OfType<string>()).ToHashSet(StringComparer.Ordinal);
    internal RoleDefinition Role(string id) => Roles.Single(x => x.Id == id && x.Enabled);
    internal ItemDefinition Item(string id) => Items.Single(x => x.Id == id);

    internal static Catalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DefinitelyStolen.Data.catalog.json")
            ?? throw new InvalidDataException("Missing candidate catalog");
        var catalog = JsonConvert.DeserializeObject<Catalog>(new StreamReader(stream).ReadToEnd()) ?? throw new InvalidDataException("Invalid catalog");
        catalog.Validate();
        return catalog;
    }

    internal void Validate()
    {
        if (!Regions.Select(x => x.Id).OrderBy(x => x).SequenceEqual(new[] { 0, 1, 2 }))
            throw new InvalidDataException("Invalid playable region identities");
        if (Roles.Select(x => x.Id).Distinct().Count() != Roles.Count || Items.Select(x => x.Id).Distinct().Count() != Items.Count)
            throw new InvalidDataException("Duplicate catalog identities");
        foreach (var role in Roles)
        {
            if (role.Enabled && role.Items.Count == 0 || role.Items.Distinct().Count() != role.Items.Count ||
                role.Items.Any(id => !Items.Any(x => x.Id == id && x.HasImage)) ||
                role.CoreItems.Distinct().Count() != role.CoreItems.Count || role.CoreItems.Any(id => !role.Items.Contains(id)) ||
                role.Tier is < 1 or > 3 || !Numbers.Finite(role.Success) || role.Success is <= 0 or > 1)
                throw new InvalidDataException("Invalid candidates: " + role.Id);
        }
        if (SpecialNpcs.All.Any(x => x.RareItem != null && !Items.Any(item => item.Id == x.RareItem && item.HasImage)))
            throw new InvalidDataException("Invalid special NPC candidates");
        if (Enabled.Any(x => x.Items.Contains("scav_token") && x.Id != "scavGeneral") || DropIds.Contains("kotton_fiber") ||
            DropIds.Overlaps(new[] { "blank_keycard", "combat_knife", "sec_keycard", "crypto_module_sec" }))
            throw new InvalidDataException("Hidden, imageless or exclusive candidate leaked");
        foreach (var region in Regions)
            if (region.Roles.Count < 3 || region.Roles.Select(x => x.Id).Distinct().Count() != region.Roles.Count ||
                region.Roles.Any(x => !Enabled.Any(r => r.Id == x.Id) || x.Weight <= 0))
                throw new InvalidDataException("Invalid region identities");
    }
}

internal sealed class RoleDefinition
{
    public string Id { get; set; } = "";
    public string Name => CatalogText.GetFor("role." + Id + ".name", false);
    public string English => CatalogText.GetFor("role." + Id + ".name", true);
    internal string LocalizedName => string.IsNullOrEmpty(LocaleKey)
        ? CatalogText.GetFor("role." + Id + ".name")
        : LocHelper.GetLocalizedOrDefault("ClientName", LocaleKey, CatalogText.GetFor("role." + Id + ".name"));
    public string LocaleKey { get; set; } = "";
    public string Source { get; set; } = "";
    public string Faction { get; set; } = "";
    public string SpriteGroup { get; set; } = "lower";
    public int Tier { get; set; }
    public double Success { get; set; }
    public string Background => CatalogText.GetFor("role." + Id + ".background", false);
    public string EnglishBackground => CatalogText.GetFor("role." + Id + ".background", true);
    internal string LocalizedBackground => CatalogText.GetFor("role." + Id + ".background");
    public bool Enabled { get; set; } = true;
    public bool OpeningAllowed { get; set; } = true;
    public List<string> CoreItems { get; set; } = new();
    public List<string> Items { get; set; } = new();
}

internal sealed class RegionDefinition
{
    public int Id { get; set; }
    public string Name => CatalogText.GetFor("region." + Id + ".name", false);
    public string English => CatalogText.GetFor("region." + Id + ".name", true);
    public string Description => CatalogText.GetFor("region." + Id + ".description", false);
    public string EnglishDescription => CatalogText.GetFor("region." + Id + ".description", true);
    public List<RoleWeight> Roles { get; set; } = new();
}

internal sealed class RoleWeight
{
    public string Id { get; set; } = "";
    public int Weight { get; set; }
}

internal sealed class ItemDefinition
{
    public string Id { get; set; } = "";
    public string Name => CatalogText.GetFor("item." + Id + ".name", false);
    public string English => CatalogText.GetFor("item." + Id + ".name", true);
    public string Sprite { get; set; } = "";
    public string Atlas { get; set; } = "";
    public bool HasImage { get; set; }
    public bool OpeningAllowed { get; set; } = true;
    public long ReferenceValue { get; set; }
}

internal static class CatalogText
{
    internal static string GetFor(string key, bool? english = null, params object[] args) =>
        english.HasValue ? ModText.GetFor(Identity.Owner, "theft." + key, english.Value ? "en" : "zh", args)
            : ModText.Get(Identity.Owner, "theft." + key, args);
}
