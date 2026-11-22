using Nicokobo.ModUtilities;
using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion;

internal sealed record CatalogEffect(string Id, string Name, string Description, UnityEngine.Sprite? Sprite, int Contraband);

// Stable identities only. Names, effects, classification and art are read from
// each enabled owner's actual factory, so this handbook never defines Aug play.
internal static class CyberneticCatalog
{
    internal const string NeuralInterfaceId = "nicokobo.aug.brain_computer_interface_module";
    internal static IReadOnlyList<string> ItemIds { get; } = ProductionCatalog.Items.Where(item => item.Kind == ItemKind.Cybernetic)
        .Select(item => item.Id).Concat(new[] { "artificial_heart", "mimetic_skin", "artificial_ankle", "infrared_vision",
            "communication_receiver", "synthetic_vocal_cords", "cognitive_turbo" }.Select(suffix => "nicokobo.aug.cybernetic." + suffix))
        .Append(NeuralInterfaceId).ToArray();

    internal static IReadOnlyList<CatalogEffect> Read()
    {
        var entries = new List<CatalogEffect>();
        foreach (string id in ItemIds.Where(DirectoryMaster.Has<GameItem>))
        {
            GameItem? item = null;
            try
            {
                item = DirectoryMaster.Create<GameItem>(id);
                if (item == null || item.identifier != id) throw new InvalidOperationException("Invalid cybernetic catalog factory: " + id);
                entries.Add(new(id, item.GetDisplayName(), item.shortDescription ?? "",
                    item is GameItemElement element ? element.ResolveSpriteByName(item.spritePath)
                        : RenderHandler.LoadFromAtlas(item.spriteAtlasPath, item.spritePath),
                    item.IsTag("CONTRABAND_ITEM_TAG") ? item.GetTagReadonly("CONTRABAND_LEVEL").GetInt() : 0));
            }
            finally { if (item != null && item.parentInventory == null) item.Destroy(); }
        }
        return entries;
    }

    internal static string Classification(int level) => level switch
    {
        0 => ModText.Get(MechcorePackage.Id, "synthesis.legal_item"),
        1 => LocHelper.GetLocalizedMechanic("mech_contraband_low"),
        2 => LocHelper.GetLocalizedMechanic("mech_contraband_mid"),
        3 => LocHelper.GetLocalizedMechanic("mech_contraband_high"),
        4 => LocHelper.GetLocalizedMechanic("mech_contraband_critical"),
        _ => throw new InvalidOperationException("Invalid cybernetic contraband level")
    };
}
