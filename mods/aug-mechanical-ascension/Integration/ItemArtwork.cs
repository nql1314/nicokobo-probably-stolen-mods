using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Integration;

internal static class ItemArtwork
{
    internal const string NeuralInterface = "nicokobo_aug_neural_interface";
    internal const string BionicRepairGel = "nicokobo_aug_bionic_repair_gel";

    // Keep the sprite keys stored by existing official saves.
    internal static string Cybernetic(string suffix) => "nicokobo_aug_cybernetic_" + suffix;

    internal static void Validate(LoadedMod mod)
    {
        foreach (string key in CyberneticProductDefinitions.Products.Select(product => Cybernetic(product.Suffix))
                     .Append(NeuralInterface).Append(BionicRepairGel))
            if (!mod.Sprites.ContainsKey(key))
                throw new InvalidDataException("Missing Aug item sprite in Textures/Items: " + key);
    }
}
