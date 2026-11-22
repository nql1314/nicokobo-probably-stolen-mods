using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class ManufacturedProducts
{
    internal static void Prepare(RecipeDefinition recipe, GameItem product, IReadOnlyList<(GameItem Item, int Count)> inputs)
    {
        if (product.identifier != SynthesisDefinitions.ScavengingRobot.Id) return;
        var scanners = inputs.Where(take => take.Item.identifier == "metal_scanner").ToArray();
        var stores = inputs.Where(take => take.Item.IsTag("CONTAINER_TAG")).ToArray();
        if (scanners.Length != 1 || scanners[0].Count != 1 || stores.Length != 1 || stores[0].Count != 1 ||
            stores[0].Item.FindAllChildItems().Count != 0 || !StorageShape.TryCapture(stores[0].Item, out int width, out string data))
            throw new InvalidOperationException("Robot manufacture requires one scanner and one empty storage area");
        int chance = scanners[0].Item.GetTagReadonly(ScavengingRobotRules.ScannerChanceTag).valueInt;
        SynthesisRobots.SetScannerChance(product, chance);
        SynthesisRobots.SetStorageShape(product, width, data);
    }
}
