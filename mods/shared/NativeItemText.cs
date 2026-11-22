namespace Nicokobo.ModUtilities;

internal static class NativeItemText
{
    internal static string Name(string id, string fallback)
    {
        // Some directory IDs are distinct from the name keys used by their factories.
        return id switch
        {
            "water" => LocHelper.GetLocalizedMechanic("liquid_water"),
            "serum_green" => LocHelper.GetLocalizedMechanic("liquid_livestock_immunity"),
            "serum_red" => LocHelper.GetLocalizedMechanic("liquid_livestock_steroid"),
            "bandage_item" => Item("item_bandage_name", fallback),
            "hemostatic_bandage_item" => Item("item_hemostatic_bandage_name", fallback),
            "topical_bandage_item" => Item("item_topical_bandage_name", fallback),
            "mouse_trap" => Item("husbandry_mouse_trap_name", fallback),
            "plant_dna_disk" => Item("item_dna_disk_name", fallback),
            "cassette1" => LocHelper.GetLocalizedItem("item_cassette_track_perseverance") +
                LocHelper.GetLocalizedItem("item_cassette_pristine"),
            _ => Item("item_" + id + "_name", fallback)
        };
    }

    private static string Item(string key, string fallback) =>
        LocHelper.GetLocalizedOrDefault("Item", key, fallback);
}
