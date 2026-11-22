using Nicokobo.ModUtilities;
using MechcoreProtocol.ModuleMatrix.Definitions;

namespace MechcoreProtocol.ModuleMatrix;

internal static class MatrixText
{
    internal static LoadedMod? Assets;
    internal static string Get(string key, params object[] args) => ModText.Get(MechcorePackage.Id, key, args);
    internal static string NodeDescription(NodeItemDefinition node)
    {
        var effect = NodeEffectDefinitions.ByCode[node.Code];
        return Get(effect.Id + ".description");
    }
    internal static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        var fixedItem = FixedModuleDefinitions.All.FirstOrDefault(value => value.Id == item.identifier);
        var continuous = ContinuousModuleDefinitions.All.FirstOrDefault(value => value.Module.Id == item.identifier)?.Module;
        var node = NodeItemDefinitions.All.FirstOrDefault(value => value.Id == item.identifier);
        var machine = MachineModuleDefinitions.All.FirstOrDefault(value => value.Id == item.identifier);
        if (fixedItem != null || continuous != null)
        {
            var definition = fixedItem ?? continuous!;
            item.name = Get(definition.Id + ".name");
        }
        if (node != null)
        {
            item.name = Get(node.Id + ".name");
            item.shortDescription = NodeDescription(node);
        }
        if (machine != null)
        {
            item.name = Get(machine.Id + ".name");
            item.ModifyTag("ADDITIONAL_EFFECT_STRING", tag => tag.Enable().SetString(Get(machine.Id + ".ability")));
        }
        MatrixEffects.RefreshText();
    }
}
