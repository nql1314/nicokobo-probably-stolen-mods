namespace AugMechanicalAscension.Domain;

internal readonly record struct CyberneticEffectItem(string ItemId, int Units, bool PlayerOwned);

// An event's detached ownership observation. Workshop receipts remain progression
// data and never enable item effects. The final ascension survival exemption is
// checked separately. Do not persist this snapshot as an unlock flag.
internal readonly record struct OwnedCyberneticEffects(
    bool ArtificialHeart, bool MimeticSkin, bool ArtificialAnkle,
    bool InfraredVision, bool CommunicationReceiver, bool SyntheticVocalCords)
{
    internal static OwnedCyberneticEffects FromOwnedCounts(IReadOnlyDictionary<string, int> counts)
    {
        bool Has(string suffix) => counts.TryGetValue(CyberneticProductDefinitions.ItemId(suffix), out int units) && units > 0;
        return new(Has("artificial_heart"), Has("mimetic_skin"), Has("artificial_ankle"),
            Has("infrared_vision"), Has("communication_receiver"), Has("synthetic_vocal_cords"));
    }

    internal static OwnedCyberneticEffects FromItems(IEnumerable<CyberneticEffectItem> items)
    {
        var ids = items.Where(item => item.PlayerOwned && item.Units > 0)
            .Select(item => item.ItemId).ToHashSet(StringComparer.Ordinal);
        return new(ids.Contains(CyberneticProductDefinitions.ItemId("artificial_heart")),
            ids.Contains(CyberneticProductDefinitions.ItemId("mimetic_skin")),
            ids.Contains(CyberneticProductDefinitions.ItemId("artificial_ankle")),
            ids.Contains(CyberneticProductDefinitions.ItemId("infrared_vision")),
            ids.Contains(CyberneticProductDefinitions.ItemId("communication_receiver")),
            ids.Contains(CyberneticProductDefinitions.ItemId("synthetic_vocal_cords")));
    }

    internal int ScavengingMaximum(int nativeMaximum, CyberneticConfig config) =>
        !ArtificialAnkle || nativeMaximum <= 0 ? nativeMaximum : (int)Math.Clamp((long)nativeMaximum + config.AnkleExtraScavAttempts,
            0, int.MaxValue);

    internal float ScavengingWoundChance(float nativeChance, CyberneticConfig config) =>
        InfraredVision ? nativeChance * config.InfraredWoundChanceMultiplierBp / 10_000f : nativeChance;

    internal int ExposureThresholdShift(CyberneticConfig config) =>
        SyntheticVocalCords ? config.ExposureThresholdShift : 0;
}
