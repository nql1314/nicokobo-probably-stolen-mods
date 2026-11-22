using MechcoreProtocol.SynthesisExpansion;
using MechcoreProtocol.SynthesisExpansion.Definitions;

internal static class SynthesisPerf
{
    private static readonly string[] Effects = {
        SynthesisDefinitions.AiCore.Id, SynthesisDefinitions.SleepCompressionModule.Id,
        SynthesisDefinitions.KineticRecovery.Id, SynthesisDefinitions.TitaniumExoskeleton.Id,
        SynthesisDefinitions.FineMotorAssistant.Id, SynthesisDefinitions.CyberEye.Id };
    private static GameItem Owned(string id)
    { var item = new GameItem { identifier = id }; GeneralHelper.SetItemOwned(item, true); return item; }
    private static (GameItem Machine, GameItem Product, GameItem[] Implants, GameGridInventory Nested) Setup(int count)
    {
        PlayerStore.Current = new(); EmporiumEntry.Instance = new(); SynthesisMod.Active = true;
        ManufacturedEffectsRuntime.Reset();
        var store = PlayerStore.Instance;
        var container = Owned("nested_container");
        var nested = new GameGridInventory(); container.SetContentWindow(new(new MachineryPanel(nested)));
        store.Items.Add(container);
        var machine = SynthesisMachines.Create(SynthesisDefinitions.Machine(SynthesisDefinitions.RefinerItem.Id));
        GeneralHelper.SetItemOwned(machine, true); store.Items.Add(machine);
        var implants = Effects.Select(Owned).ToArray();
        foreach (var implant in implants) nested.UncheckedAccept(implant);
        var product = Owned("valuable"); product.SetGameItemType("CYBERNETIC");
        EmporiumEntry.Instance.backInvinvElement.UncheckedAccept(product); store.Items.Add(product);
        for (int index = 9; index < count; index++)
        {
            var item = Owned("ordinary_item_" + index);
            if (index % 2 == 0) nested.UncheckedAccept(item); else store.Items.Add(item);
        }
        store.currentClientInstance = new() { Client = new StoreClient() };
        return (machine, product, implants, nested);
    }
    private static void StateChecks()
    {
        var (machine, product, implants, nested) = Setup(100);
        var store = PlayerStore.Instance;
        var kinetic = implants[2]; var ai = implants[0];
        void CheckEfficiency(int expected, string message)
        {
            ManufacturedEffectsRuntime.BeforeRead(machine);
            PerfCounters.Check(machine.GetTagReadonly("TOTAL_PERCENTAGE_POWER_BONUS_INT").GetInt() == expected, message);
        }
        CheckEfficiency(50, "Initial owned machine bonus missing");
        GeneralHelper.SetItemOwned(kinetic, false); CheckEfficiency(0, "Same-frame ownership loss retained machine bonus");
        GeneralHelper.SetItemOwned(kinetic, true); CheckEfficiency(50, "Same-frame ownership gain did not restore machine bonus");
        kinetic.unitCount = 0; CheckEfficiency(0, "Same-frame zero units retained machine bonus");
        kinetic.unitCount = 1; CheckEfficiency(50, "Same-frame quantity recovery lost machine bonus");
        nested.Expel(kinetic); CheckEfficiency(0, "Same-frame removal from inventory graph retained machine bonus");
        nested.UncheckedAccept(kinetic); CheckEfficiency(50, "Same-frame nested reinsertion did not restore machine bonus");
        kinetic.identifier = "ordinary_item"; CheckEfficiency(0, "Same-frame identifier change retained machine bonus");
        kinetic.identifier = Effects[2]; CheckEfficiency(50, "Same-frame identifier restoration lost machine bonus");
        ManufacturedEffectsRuntime.Night(); CheckEfficiency(50, "Night refresh stacked or lost owned contribution");
        store.visitLeftTonight = 2; ManufacturedEffectsRuntime.Closed();
        PerfCounters.Check(store.visitLeftTonight == 3, "Night action bonus changed");
        GeneralHelper.SetItemOwned(implants[1], false); ManufacturedEffectsRuntime.Closed();
        PerfCounters.Check(store.visitLeftTonight == 3, "Same-frame loss of sleep implant retained night action bonus");
        ManufacturedEffectsRuntime.RefreshSale(product);
        PerfCounters.Check(product.itemFeatures.Single().GetActualValueModifier() == 10, "Human AI sale bonus changed");
        GeneralHelper.SetItemOwned(ai, false); ManufacturedEffectsRuntime.RefreshSale(product);
        PerfCounters.Check(product.itemFeatures.Count == 0, "Same-frame AI ownership loss retained sale bonus");
        GeneralHelper.SetItemOwned(ai, true); ManufacturedEffectsRuntime.RefreshSale(product);
        PerfCounters.Check(product.itemFeatures.Single().GetActualValueModifier() == 10, "Same-frame AI ownership gain lost sale bonus");
        store.currentClientInstance!.Client.isAug = true; ManufacturedEffectsRuntime.RefreshSale(product);
        PerfCounters.Check(product.itemFeatures.Single().GetActualValueModifier() == 25, "Aug sale bonus mixed with AI bonus");
        product.Types.Remove("CYBERNETIC"); ManufacturedEffectsRuntime.RefreshSale(product);
        PerfCounters.Check(product.itemFeatures.Count == 0, "Noncybernetic Aug sale retained bonus");
        store.currentClientInstance.Client.isAug = false; ManufacturedEffectsRuntime.RefreshSale(product);
        PerfCounters.Check(product.itemFeatures.Single().GetActualValueModifier() == 10, "Aug-to-human customer switch failed to restore AI bonus");
        EmporiumEntry.Instance!.backInvinvElement.Expel(product); ManufacturedEffectsRuntime.RefreshSale(product);
        PerfCounters.Check(product.itemFeatures.Count == 0, "Moving product off counter retained sale bonus");
        PlayerStore.Current = new(); CheckEfficiency(0, "Store replacement reused old ownership state");
        SynthesisMod.Active = false; ManufacturedEffectsRuntime.BeforeRead(machine); SynthesisMod.Active = true;
    }
    private static void Main(string[] args)
    {
        StateChecks();
        foreach (int count in new[] { 100, 1000 })
        {
            var fixture = Setup(count);
            PerfCounters.Check(PlayerStore.Instance.FindAllItem().Count == count, "Inventory cardinality differs from benchmark label");
            PerfCounters.Measure("Capture", count, () => ManufacturedEffectsRuntime.Capture());
            PerfCounters.Measure("MachineThreeReads", count, () => {
                ManufacturedEffectsRuntime.BeforeRead(fixture.Machine);
                ManufacturedEffectsRuntime.BeforeRead(fixture.Machine);
                ManufacturedEffectsRuntime.BeforeRead(fixture.Machine);
            });
            PerfCounters.Measure("Night", count, ManufacturedEffectsRuntime.Night);
            PerfCounters.Measure("Closed", count, ManufacturedEffectsRuntime.Closed);
            PerfCounters.Measure("SaleHuman", count, () => ManufacturedEffectsRuntime.RefreshSale(fixture.Product));
            PlayerStore.Instance.currentClientInstance!.Client.isAug = true;
            PerfCounters.Measure("SaleAug", count, () => ManufacturedEffectsRuntime.RefreshSale(fixture.Product));
            foreach (var implant in fixture.Implants) implant.identifier = "ordinary_implant";
            PerfCounters.Measure("CaptureNoEffects", count, () => ManufacturedEffectsRuntime.Capture());
            PerfCounters.Measure("MachineThreeReadsNoEffects", count, () => {
                ManufacturedEffectsRuntime.BeforeRead(fixture.Machine);
                ManufacturedEffectsRuntime.BeforeRead(fixture.Machine);
                ManufacturedEffectsRuntime.BeforeRead(fixture.Machine);
            });
        }
        PerfCounters.Save(args.Single());
    }
}
