namespace AugMechanicalAscension.Domain;

internal sealed record WorkshopMaterialCost(string ItemId, int Quantity);

internal sealed record CyberneticProductDefinition(string Suffix, string Chinese, string English,
    int Width, int Height, int BaseValue, string TemplateId, WorkshopMaterialCost[] Ingredients,
    int MusicCassetteCount = 0, int ContrabandLevel = 0)
{
    internal string Id => CyberneticProductDefinitions.ItemId(Suffix);
    internal string RecipeId => "nicokobo.aug.recipe.cybernetic." + Suffix;
    internal (string Chinese, string English) EffectDescription => TextPair(Id + ".description");
    internal (string Chinese, string English) FlavorText => TextPair(Id + ".flavor");

    private static (string Chinese, string English) TextPair(string key) =>
        (AugText.GetFor(key, false), AugText.GetFor(key, true));
}

internal static class CyberneticProductDefinitions
{
    internal static string ItemId(string suffix) => "nicokobo.aug.cybernetic." + suffix;
    private static string Syn(string suffix) => "nicokobo.mechcore.synthesis." + suffix;
    private static WorkshopMaterialCost Need(string id, int count = 1) => new(id, count);
    internal static IReadOnlyList<CyberneticProductDefinition> Products { get; } = Array.AsReadOnly(new[]
    {
        new CyberneticProductDefinition("artificial_heart",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.artificial_heart.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.artificial_heart.name", "en"),
            AugNumbers.Products.ArtificialHeart.Width,
            AugNumbers.Products.ArtificialHeart.Height,
            AugNumbers.Products.ArtificialHeart.BaseValue,
            "common_medical",
            [Need(Syn("precision_parts"), AugNumbers.RecipeArtificialHeart.PrecisionPartsCount), Need(Syn("power_regulator"), AugNumbers.RecipeArtificialHeart.PowerRegulatorCount),
                Need(Syn("mechanical_core"), AugNumbers.RecipeArtificialHeart.MechanicalCoreCount),
                Need("blue_blood_bag", AugNumbers.RecipeArtificialHeart.BlueBloodBagCount)]),
        new CyberneticProductDefinition("mimetic_skin",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.mimetic_skin.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.mimetic_skin.name", "en"),
            AugNumbers.Products.MimeticSkin.Width,
            AugNumbers.Products.MimeticSkin.Height,
            AugNumbers.Products.MimeticSkin.BaseValue,
            "common_medical",
            [Need("tilapia_skin_graft", AugNumbers.RecipeMimeticSkin.SkinGraftCount),
                Need("kotton_fabric", AugNumbers.RecipeMimeticSkin.KottonFabricCount),
                Need("neuroactive_perfume", AugNumbers.RecipeMimeticSkin.NeuroactivePerfumeCount)],
            ContrabandLevel: AugNumbers.Products.MimeticSkin.ContrabandLevel),
        new CyberneticProductDefinition("artificial_ankle",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.artificial_ankle.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.artificial_ankle.name", "en"),
            AugNumbers.Products.ArtificialAnkle.Width,
            AugNumbers.Products.ArtificialAnkle.Height,
            AugNumbers.Products.ArtificialAnkle.BaseValue,
            "rare_electronic",
            [Need(Syn("precision_parts"), AugNumbers.RecipeArtificialAnkle.PrecisionPartsCount), Need(Syn("titanium_alloy"), AugNumbers.RecipeArtificialAnkle.TitaniumAlloyCount),
                Need("nuts_metal", AugNumbers.RecipeArtificialAnkle.NutsMetalCount)],
            ContrabandLevel: AugNumbers.Products.ArtificialAnkle.ContrabandLevel),
        new CyberneticProductDefinition("infrared_vision",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.infrared_vision.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.infrared_vision.name", "en"),
            AugNumbers.Products.InfraredVision.Width,
            AugNumbers.Products.InfraredVision.Height,
            AugNumbers.Products.InfraredVision.BaseValue,
            "rare_electronic",
            [Need(Syn("advanced_circuit"), AugNumbers.RecipeInfraredVision.AdvancedCircuitCount), Need(Syn("silicon"), AugNumbers.RecipeInfraredVision.SiliconCount), Need("flashlight", AugNumbers.RecipeInfraredVision.FlashlightCount)],
            ContrabandLevel: AugNumbers.Products.InfraredVision.ContrabandLevel),
        new CyberneticProductDefinition("communication_receiver",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.communication_receiver.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.communication_receiver.name", "en"),
            AugNumbers.Products.CommunicationReceiver.Width,
            AugNumbers.Products.CommunicationReceiver.Height,
            AugNumbers.Products.CommunicationReceiver.BaseValue,
            "rare_electronic",
            [Need(Syn("control_chip"), AugNumbers.RecipeCommunicationReceiver.ControlChipCount), Need(Syn("advanced_circuit"), AugNumbers.RecipeCommunicationReceiver.AdvancedCircuitCount),
                Need("alarm_module_transmitter", AugNumbers.RecipeCommunicationReceiver.AlarmModuleTransmitterCount)],
            ContrabandLevel: AugNumbers.Products.CommunicationReceiver.ContrabandLevel),
        new CyberneticProductDefinition("synthetic_vocal_cords",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.synthetic_vocal_cords.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.synthetic_vocal_cords.name", "en"),
            AugNumbers.Products.SyntheticVocalCords.Width,
            AugNumbers.Products.SyntheticVocalCords.Height,
            AugNumbers.Products.SyntheticVocalCords.BaseValue,
            "common_medical",
            [Need(Syn("precision_parts"), AugNumbers.RecipeSyntheticVocalCords.PrecisionPartsCount), Need(Syn("control_chip"), AugNumbers.RecipeSyntheticVocalCords.ControlChipCount)],
            AugNumbers.RecipeSyntheticVocalCords.MusicCassetteCount,
            AugNumbers.Products.SyntheticVocalCords.ContrabandLevel),
        new CyberneticProductDefinition("cognitive_turbo",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.cognitive_turbo.name", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "nicokobo.aug.cybernetic.cognitive_turbo.name", "en"),
            AugNumbers.Products.CognitiveTurbo.Width,
            AugNumbers.Products.CognitiveTurbo.Height,
            AugNumbers.Products.CognitiveTurbo.BaseValue,
            "rare_electronic",
            [Need(Syn("ai_core"), AugNumbers.RecipeCognitiveTurbo.AiCoreCount),
                Need(Syn("power_regulator"), AugNumbers.RecipeCognitiveTurbo.PowerRegulatorCount),
                Need("turbo_booster_adv", AugNumbers.RecipeCognitiveTurbo.AdvancedTurboBoosterCount)])
    });

    internal static bool IsMusicCassette(bool cassetteTag, bool musicCategory, string? track) =>
        cassetteTag && musicCategory && !string.IsNullOrWhiteSpace(track);
}
