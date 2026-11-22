using HarmonyLib;
using Nicokobo.ContentInterop;
using MechcoreProtocol.ModuleMatrix.Definitions;
using MechcoreProtocol.ModuleMatrix.Domain;

namespace MechcoreProtocol.ModuleMatrix;

internal static class MatrixItems
{
    internal const string PriceTag = "nicokobo.mechcore_protocol.module_matrix.price_basis_v1";
    internal static string Code(string id) => FixedModuleDefinitions.All.FirstOrDefault(item => item.Id == id)?.Code ??
        ContinuousModuleDefinitions.All.FirstOrDefault(item => item.Module.Id == id)?.Module.Code ??
        NodeItemDefinitions.All.FirstOrDefault(item => item.Id == id)?.Code ??
        MachineModuleDefinitions.All.Single(item => item.Id == id).Code;

    internal static GameItem Create(string id)
    {
        var item = ItemDirectory.CreateEmptyItem(id);
        try
        {
            string chinese, english, data; int width, value;
            var basic = FixedModuleDefinitions.All.FirstOrDefault(def => def.Id == id);
            var continuous = ContinuousModuleDefinitions.All.FirstOrDefault(def => def.Module.Id == id);
            var node = NodeItemDefinitions.All.FirstOrDefault(def => def.Id == id);
            var machine = MachineModuleDefinitions.All.FirstOrDefault(def => def.Id == id);
            if (basic != null || continuous != null)
            {
                var definition = basic ?? continuous!.Module;
                if (!FixedModuleRules.Validate(definition).Valid) throw new InvalidOperationException("Invalid module definition: " + id);
                chinese = definition.ChineseName; english = definition.EnglishName; data = definition.ShapeData; width = definition.Width; value = definition.UnitValue;
                ModuleHelper.InitModuleItem(item, FixedModuleDefinitions.UniversalModuleType,
                    definition.Performance, definition.Efficiency, definition.Quality);
                if (continuous != null)
                {
                    item.ModifyTag(MatrixGrowth.WorkDayTag, tag => tag.Enable().SetInt(-1));
                    item.ModifyTag(MatrixGrowth.DecayDayTag, tag => tag.Enable().SetInt(-1));
                }
            }
            else if (node != null)
            {
                chinese = node.ChineseName; english = node.EnglishName; data = node.ShapeData; width = node.Width; value = node.UnitValue;
                ModuleEffectHelper.InitNode(item);
                FixedModuleAbilities.Add(item, NodeEffectDefinitions.ByCode[node.Code].Id);
                item.shortDescription = MatrixText.NodeDescription(node);
                // Save data restores the old description over the factory text.
                item.onLoaded += loaded => loaded.shortDescription = MatrixText.NodeDescription(node);
            }
            else if (machine != null)
            {
                chinese = machine.ChineseName; english = machine.EnglishName; data = machine.ShapeData; width = machine.Width; value = machine.UnitValue;
                ModuleHelper.InitModuleItem(item, machine.ModuleType, additionalEffectDisplay: MatrixText.Get(machine.Id + ".ability"));
                if (machine.Code == "S09") item.ModifyTag(MatrixMachines.CacheTag, tag => tag.Enable().SetString(""));
                if (machine.Code == "S25") item.ModifyTag(MatrixMachines.MarketDayTag, tag => tag.Enable().SetInt(-1));
            }
            else throw new InvalidOperationException("Unknown Matrix item: " + id);
            item.name = MatrixText.Get(id + ".name");
            item.unitBaseValue = 0; item.unitValue = value;
            item.ModifyTag(PriceTag, tag => tag.Enable().SetInt(value));
            item.SetSpriteAndShapeFromMod(MechcorePackage.Id, ModuleIconDefinitions.ByItemId[id].SpriteKey);
            item.SetShape(new GridShapeBuilder().SetData(data, width).Build());
            item.mayStackItemFunc = null;
            // Only fixed modules roll native effects; continuous and machine modules keep their fixed ability.
            if (basic != null) ModuleEffectHelper.InitRandomEffect(item);
            ModuleEffectHelper.ResetAllTempStat(item);
            ModHelper.SetSource(item, MechcorePackage.Id, MatrixText.Get("package.name"));
            item.onLoaded += Reprice;
            return item;
        }
        catch { if (item.parentInventory == null) item.Destroy(); throw; }
    }

    internal static void Reprice(GameItem item)
    {
        if (!MatrixMod.Active || ModuleTierDefinitions.Find(item.identifier) is not { } definition) return;
        int? previous = item.IsTag(PriceTag) ? item.GetTagReadonly(PriceTag).GetInt() : null;
        if (!definition.IsMatrix)
        {
            if (previous == null || definition.PreviousUnitValue == null) return;
            item.unitValue = ModulePriceRules.Rebase(definition, item.unitValue, previous);
            item.ModifyTag(PriceTag, tag => tag.SetEnabled(false));
            return;
        }
        if (definition.UnitValue is not { } basis || !MatrixMod.Enabled(item.identifier) || previous == basis) return;
        long rebased = ModulePriceRules.Rebase(definition, item.unitValue, previous);
        item.ModifyTag(PriceTag, tag => tag.Enable().SetInt(basis));
        item.unitValue = rebased;
    }
}

[HarmonyPatch(typeof(DirectoryMaster), nameof(DirectoryMaster.Item), new[] { typeof(string), typeof(bool) })]
internal static class MatrixCreatedPatch
{
    private static void Postfix(GameItem __result)
    {
        if (!MatrixMod.Active || __result == null) return;
        MatrixItems.Reprice(__result);
        MatrixMachines.Configure(__result);
    }
}
