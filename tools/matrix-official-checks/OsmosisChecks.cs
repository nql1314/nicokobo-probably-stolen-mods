using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.ModuleMatrix.Definitions;

internal static class OsmosisChecks
{
    private static (GameItem Machine, GameItem Water) Prepare(int quality, int stacks,
        int water, int dirt, int fine = 0, int permanent = 0)
    {
        PlayerStore.Current = new();
        Liquid.Liquids = new()
        {
            new() { identifier = "water" }, new() { identifier = "dirt", canBeRemovedByPurifier = true },
            new() { identifier = "fine", canBeRemovedByPurifier = true }, new() { identifier = "permanent" }
        };
        var machine = new GameItem { identifier = "water_purifier" };
        machine.EnableTag("STANDARD_MACHINE_TAG");
        machine.GetTagReadonly("quality").SetInt(quality);
        var bay = new GameGridInventory { Parent = machine }; machine.Storages[1] = bay;
        PlayerStore.Instance.Items.Add(machine);
        for (int i = 0; i < stacks; i++) bay.Add(MatrixItems.Create(MachineModuleDefinitions.All.Single(def => def.Code == "S24").Id));
        var container = new GameItem(); container.EnableTag("LIQUID_CONTAINER_TAG");
        WaterHelper.AddLiquid(container, "water", water); WaterHelper.AddLiquid(container, "dirt", dirt);
        WaterHelper.AddLiquid(container, "fine", fine); WaterHelper.AddLiquid(container, "permanent", permanent);
        return (machine, container);
    }

    internal static void Run(Action<bool, string> check)
    {
        foreach (int quality in new[] { -10, 0, 15, 25, 50, 75, 100, 199, 200 })
        foreach (int stacks in new[] { 1, 3 })
        {
            int cap = MachinePurifier.GetMaxPurityCap(quality);
            var atCap = Prepare(quality, stacks, cap * 10, 100000 - cap * 10);
            check(MatrixMachines.Purify(atCap.Machine, atCap.Water) == 0 && WaterHelper.GetTotalVolume(atCap.Water) == 100000,
                $"S24 removed contaminants at the native purity cap: quality={quality}, stacks={stacks}");
            var below = Prepare(quality, stacks, cap * 10 - 1, 100001 - cap * 10);
            check(MatrixMachines.Purify(below.Machine, below.Water) == 1 &&
                below.Water.GetTagReadonly("liquid:water").GetInt() == cap * 10,
                $"S24 exceeded the shared remaining purity allowance: quality={quality}, stacks={stacks}");
        }
        var beyond = Prepare(0, 3, 99900, 100);
        check(MatrixMachines.Purify(beyond.Machine, beyond.Water) == 0, "S24 purified water already above the machine cap");
        var single = Prepare(0, 1, 200, 600, permanent: 100);
        check(MatrixMachines.Purify(single.Machine, single.Water) == 150 && WaterHelper.GetTotalVolume(single.Water) == 900 &&
            single.Water.GetTagReadonly("liquid:permanent").GetInt() == 100, "Single S24 lost its remaining-amount rule or volume conservation");
        var multiple = Prepare(0, 3, 200, 600, permanent: 100);
        check(MatrixMachines.Purify(multiple.Machine, multiple.Water) == 347,
            "Multiple S24 modules stopped filtering the remaining amount with integer truncation");
        var allComponents = Prepare(0, 1, 98900, 100, permanent: 1000);
        check(MatrixMachines.Purify(allComponents.Machine, allComponents.Water) == 25 &&
            WaterHelper.GetTotalVolume(allComponents.Water) == 100000, "S24 cap excluded an unremovable liquid component");
        var shared = Prepare(0, 3, 98998, 400, 2, 600);
        check(MatrixMachines.Purify(shared.Machine, shared.Water) == 2 &&
            shared.Water.GetTagReadonly("liquid:fine").GetInt() == 2 && shared.Water.GetTagReadonly("liquid:permanent").GetInt() == 600 &&
            WaterHelper.GetTotalVolume(shared.Water) == 100000, "Each contaminant spent the same remaining purity allowance");
        var rounding = Prepare(0, 3, 98, 3);
        check(MatrixMachines.Purify(rounding.Machine, rounding.Water) == 2 &&
            rounding.Water.GetTagReadonly("liquid:water").GetInt() == 100 && WaterHelper.GetTotalVolume(rounding.Water) == 101 &&
            MatrixMachines.Purify(rounding.Machine, rounding.Water) == 0, "S24 differs from native total-volume integer rounding");
        var large = Prepare(0, 1, 1583999999, 16000001);
        check(MatrixMachines.Purify(large.Machine, large.Water) == 1 && WaterHelper.GetTotalVolume(large.Water) == 1600000000,
            "S24 purity allowance overflowed on a large container");
        var unsupported = Prepare(200, 3, 60000, 0, permanent: 40000);
        check(MatrixMachines.Purify(unsupported.Machine, unsupported.Water) == 0,
            "S24 removed unsupported liquid components");
        var dry = Prepare(200, 1, 0, 100);
        check(MatrixMachines.Purify(dry.Machine, dry.Water) == 0, "S24 created water in a container without water");
        var absent = Prepare(0, 0, 200, 600);
        check(MatrixMachines.Purify(absent.Machine, absent.Water) == 0, "Purifier without S24 received extra removal");
        var charged = Prepare(0, 1, 98900, 1100);
        object?[] extra = { charged.Machine, charged.Water, false, 17 };
        Ports.Hook(typeof(MatrixOsmosisPatch), "Postfix", extra);
        check((int)extra[3]! == 117 && charged.Water.GetTagReadonly("liquid:water").GetInt() == 99000,
            "S24 did not return actual removal to native billing and work callbacks");
        object?[] noWork = { charged.Machine, charged.Water, false, 0 };
        Ports.Hook(typeof(MatrixOsmosisPatch), "Postfix", noWork);
        check((int)noWork[3]! == 0, "At-cap S24 reported phantom removal to billing and growth");
        var ignored = Prepare(200, 3, 99000, 1000);
        object?[] noBonus = { ignored.Machine, ignored.Water, true, 0 };
        Ports.Hook(typeof(MatrixOsmosisPatch), "Postfix", noBonus);
        check((int)noBonus[3]! == 0, "S24 ignored the native ignoreBonus purity cap");
    }
}
