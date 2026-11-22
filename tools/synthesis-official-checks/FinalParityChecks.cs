using Nicokobo.ContentInterop;
using MechcoreProtocol.SynthesisExpansion;
using MechcoreProtocol.SynthesisExpansion.Definitions;

internal static class FinalParityChecks
{
    internal static void Run(Action<bool, string> check, string source)
    {
        RecipeMigrationChecks.Run(check);
        var texts = new Dictionary<string, (string, string)>();
        foreach (var item in ProductionCatalog.Items)
        {
            texts[item.Id + ".name"] = (item.ChineseName, item.EnglishName);
            texts[item.Id + ".description"] = ContentText.Description(item);
            texts[item.Id + ".flavor"] = ContentText.FlavorText(item);
        }
        LocalizationChecks.Run(check, texts);
        const string owner = "checks.synthesis.power";
        foreach (string mode in new[] { "success", "rejected", "insufficient", "throws" })
        {
            ContentRuntime.Attach(owner); SynthesisPower.Reset(); PlayerStore.Current = new();
            PowerHelper.RefuseDraw = PowerHelper.ThrowAfterDebit = PowerHelper.RefuseRestore = false; PowerHelper.DrawCalls = 0;
            var battery = new GameSlotInventory(); var cell = new GameItem(); GeneralHelper.SetItemOwned(cell, true); battery.UncheckedAccept(cell);
            int available = mode == "insufficient" ? 5 : 40, calls = 0;
            DevicePower.Register(owner, item => item == cell ? available : null, (item, cost) =>
            { calls++; if (mode == "throws") throw new InvalidOperationException("uncertain provider"); if (mode == "rejected") return false; available -= cost; return true; });
            check(SynthesisPower.Energy(cell) == available, "Device power UI ignored authoritative availability");
            var result = SynthesisPower.Debit(battery, 10);
            check(result == (mode == "success" ? PowerDebit.Committed : mode == "throws" ? PowerDebit.Faulted : PowerDebit.Rejected) &&
                PowerHelper.DrawCalls == 0 && calls == (mode == "insufficient" ? 0 : 1), "Device power used native fallback after shared handling: " + mode);
            check(available == (mode == "success" ? 30 : mode == "insufficient" ? 5 : 40) && cell.GetTagReadonly("energy").GetInt() == 0,
                "Device shared debit modified local energy or charged twice: " + mode);
            if (mode == "throws") check(SynthesisPower.Debit(battery, 10) == PowerDebit.Faulted && calls == 1, "Faulted shared source was retried");
            ContentRuntime.Detach(owner);
        }
        foreach (bool reject in new[] { false, true })
        {
            ContentRuntime.Attach(owner); PowerHelper.DrawCalls = 0;
            var machine = new GameItem { identifier = SynthesisDefinitions.SynthesizerItem.Id };
            machine.GetTagReadonly("is_machinery_powered").SetBool(true); machine.GetTagReadonly("power").SetInt(10);
            machine.GetTagReadonly(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).SetInt(10);
            var battery = new GameSlotInventory(); var cell = new GameItem(); battery.UncheckedAccept(cell);
            var input = new GameGridInventory(); var material = new GameItem { identifier = "common_electronic", unitCount = 2, unitValue = 100 }; input.UncheckedAccept(material);
            var output = new GameGridInventory(); machine.Storages = new object?[] { battery, new GameGridInventory(), input, output, null };
            int available = 40;
            DevicePower.Register(owner, item => item == cell ? available : null, (item, cost) => { if (reject) return false; available -= cost; return true; });
            var extra = new ExtraRecipe("checks.extra.two", machine.identifier,
                new[] { new IngredientView("common_electronic", 2, item => item.identifier == "common_electronic", "电子元件×2", "Electronics ×2") },
                "mouse_trap", "捕鼠夹", "Mouse Trap", () => true, () => "mouse_trap", outputCount: 2);
            DirectoryMaster.Factory = null; DirectoryMaster.AvailableFactory = null;
            bool processed = SynthesisProduction.TryProcess(machine, battery, input, output, extra);
            check(processed == !reject && output.childItems.Count == (reject ? 0 : 2) && available == (reject ? 40 : 30) && PowerHelper.DrawCalls == 0,
                "Shared production ignored two-output recipe or provider rejection");
            check(reject ? material.unitCount == 2 && material.parentInventory == input : material.Destroyed &&
                output.childItems.All(item => item.unitValue == 145), "Two-output production pricing or rollback differs from actual output count");
            ContentRuntime.Detach(owner);
        }
        var prototypes = new List<GameItem>();
        var icon = new UnityEngine.Sprite();
        DirectoryMaster.AvailableFactory = id => CyberneticCatalog.ItemIds.Contains(id);
        DirectoryMaster.Factory = id =>
        {
            var item = new GameItemElement { identifier = id, name = "Actual " + id, shortDescription = "Current effect " + id, Icon = icon,
                spriteAtlasPath = "provider", spritePath = "icon", useSpriteFromMod = true };
            item.EnableTag("CONTRABAND_ITEM_TAG"); item.GetTagReadonly("CONTRABAND_LEVEL").SetInt(3); prototypes.Add(item); return item;
        };
        var entries = CyberneticCatalog.Read();
        check(entries.Count == 14 && entries.All(item => item.Name.StartsWith("Actual ") && item.Description.StartsWith("Current effect ") && item.Contraband == 3 && ReferenceEquals(item.Sprite, icon)),
            "Cybernetic catalog does not use all fourteen actual provider definitions");
        check(prototypes.All(item => item.Destroyed && item.parentInventory == null), "Catalog prototypes leaked into a run");
        DirectoryMaster.AvailableFactory = id => ProductionCatalog.Items.Any(item => item.Id == id);
        check(CyberneticCatalog.Read().Count == 6, "Missing Aug provider produced placeholder catalog entries");
        DirectoryMaster.Factory = null; DirectoryMaster.AvailableFactory = null;
    }
}
