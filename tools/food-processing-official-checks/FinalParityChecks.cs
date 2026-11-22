using Nicokobo.ContentInterop;
using Nicokobo.FoodProcessing;
using Nicokobo.FoodProcessing.Definitions;

internal static class FinalParityChecks
{
    internal static void Run(Action<bool, string> check, string source)
    {
        LocHelper.LocaleCode = "zh";
        try { FoodReleaseChecks.Run(check); }
        finally { LocHelper.LocaleCode = "en"; }
        var texts = new Dictionary<string, (string, string)>();
        foreach (var item in FoodDefinitions.Items)
        {
            texts[item.Id + ".name"] = (item.ChineseName, item.EnglishName);
            texts[item.Id + ".description"] = ContentText.Description(item);
            texts[item.Id + ".flavor"] = ContentText.FlavorText(item);
        }
        foreach (var pair in ContentText.InterfaceText) texts.Add(pair.Key, pair.Value);
        LocalizationChecks.Run(check, texts);
        const string owner = "checks.food.power";
        foreach (string mode in new[] { "success", "rejected", "insufficient" })
        {
            ContentRuntime.Attach(owner);
            PowerHelper.RefuseDraw = PowerHelper.ThrowAfterDebit = DirectoryMaster.FailCleanup = false;
            int available = mode == "insufficient" ? 5 : 40, calls = 0;
            var recipe = FoodDefinitions.Recipes.Single(value => value.Code == "F03");
            var machine = new GameItem { identifier = recipe.MachineId };
            machine.GetTagReadonly("is_machinery_powered").SetBool(true);
            machine.GetTagReadonly("power").SetInt(10);
            var battery = new GameSlotInventory(); var cell = new GameItem(); battery.UncheckedAccept(cell);
            var input = new GameGridInventory(); var fruit = new GameItem { identifier = "nutrifruit", unitCount = 3, unitValue = 10 }; input.UncheckedAccept(fruit);
            var output = new GameGridInventory(); var waterSlot = new GameSlotInventory(); var water = new GameItem();
            water.GetTagReadonly("water").SetInt(500000); water.GetTagReadonly("quality").SetInt(2); waterSlot.UncheckedAccept(water);
            machine.Storages[0] = battery; machine.Storages[1] = new GameGridInventory(); machine.Storages[2] = input; machine.Storages[3] = output; machine.Storages[5] = waterSlot;
            DevicePower.Register(owner, item => item == cell ? available : null, (item, cost) =>
            { calls++; if (mode == "rejected") return false; available -= cost; return true; });
            bool processed = FoodProduction.TryProcess(machine, battery, input, output, waterSlot, recipe);
            check(processed == (mode == "success") && calls == (mode == "insufficient" ? 0 : 1), "Food shared power did not honor provider result: " + mode);
            check(available == (mode == "success" ? 30 : mode == "insufficient" ? 5 : 40) && cell.GetTagReadonly("energy").GetInt() == 0,
                "Food debited a local source after shared power handling: " + mode);
            check(mode == "success" ? fruit.Destroyed && output.childItems.Count == 1 : fruit.unitCount == 3 && fruit.parentInventory == input &&
                output.childItems.Count == 0 && WaterHelper.GetTotalVolume(water) == 500000, "Food shared debit broke material/water rollback: " + mode);
            ContentRuntime.Detach(owner);
        }
    }
}
