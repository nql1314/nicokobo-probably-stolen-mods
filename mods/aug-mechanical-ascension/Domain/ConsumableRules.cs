namespace AugMechanicalAscension.Domain;

internal static class ConsumableRules
{
    internal static bool IsMaterialMedical(string? itemId)
        => itemId is "common_medical" or "bandage_item" or
            "hemostatic_bandage_item" or "topical_bandage_item" or "salve" or
            "blood_bag" or "blue_blood_bag" or "tilapia_skin_graft" or BionicRepairGelDefinition.Id or
            "nicokobo.mechcore.synthesis.compressed_nutrient_liquid";

    internal static bool IsManufacturedMedical(string? itemId) =>
        itemId is BionicRepairGelDefinition.Id or
            "nicokobo.mechcore.synthesis.compressed_nutrient_liquid";

    internal static long MedicalRecovery(long correctBaseValue)
        => correctBaseValue is > 0 and <= int.MaxValue ? correctBaseValue : 0;
}
