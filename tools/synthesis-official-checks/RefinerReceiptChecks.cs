using MechcoreProtocol.SynthesisExpansion;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using Nicokobo.ContentInterop;

internal static class RefinerReceiptChecks
{
    private const string Owner = "checks.synthesis.refiner.receipts";

    private static GameItem Owned(string id, int count = 1)
    {
        var item = new GameItem { identifier = id, unitCount = count };
        GeneralHelper.SetItemOwned(item, true);
        return item;
    }

    internal static void Run(Action<bool, string> check)
    {
        ContentRuntime.Attach(Owner);
        try
        {
            foreach (string mode in new[] { "glass", "full", "power", "idle", "limited" })
            {
                PowerHelper.RefuseDraw = PowerHelper.ThrowAfterDebit = PowerHelper.RefuseRestore = DirectoryMaster.FailCleanup = false;
                var definition = SynthesisDefinitions.Machine(SynthesisDefinitions.RefinerItem.Id);
                var machine = SynthesisMachines.Create(definition);
                GeneralHelper.SetItemOwned(machine, true);
                var battery = (GameSlotInventory)machine.Storages[0]!;
                var cell = Owned("battery"); battery.UncheckedAccept(cell);
                cell.GetTagReadonly("energy").SetInt(mode == "power" ? 0 : 100);
                var input = (GameGridInventory)machine.Storages[2]!;
                var output = (GameGridInventory)machine.Storages[3]!;
                var stock = new GameGridInventory(); var delivered = new GameGridInventory();
                var feedstock = GlassRecycling.Feedstocks[0];
                int supplied = mode == "idle" ? 0 : feedstock.RequiredCount * (mode == "limited" ? 2 : 1);
                if (supplied > 0) stock.UncheckedAccept(Owned(feedstock.ItemId, supplied));
                if (mode == "full") output.Capacity = 0;
                int before = 0;
                var receipts = new List<WorkResult>();
                ProductionHooks.Subscribe(Owner, Owner, context =>
                {
                    if (context.Machine != machine) return;
                    before++;
                    foreach (var source in stock.childItems.ToArray())
                        if (!input.UncheckedAccept(source)) throw new InvalidOperationException("Refiner refill refused");
                }, receipt =>
                {
                    if (receipt.Context.Machine != machine) return;
                    receipts.Add(receipt);
                    foreach (var product in output.childItems.ToArray())
                        if (!delivered.UncheckedAccept(product)) throw new InvalidOperationException("Refiner dispatch refused");
                });
                machine.onCycleEndSlotItemFunc!(machine, input, new());

                bool completed = mode is "glass" or "limited";
                int produced = completed ? feedstock.Recipe.BaseOutputCount : 0;
                int consumed = completed ? feedstock.RequiredCount : 0;
                check(before > 0 && before == receipts.Count && receipts.Count(result => result.Committed) == (completed ? 1 : 0),
                    "Refiner attempts omitted or duplicated work receipts: " + mode);
                check(receipts.Sum(result => result.Consumed.Sum(item => item.Amount)) == consumed &&
                    receipts.All(result => result.WaterPartsConsumed == 0) &&
                    delivered.childItems.Sum(item => item.unitCount) == produced,
                    "Refiner receipts disagreed with actual resource changes: " + mode);
                check(stock.childItems.Count == 0 && input.childItems.Sum(item => item.unitCount) == supplied - consumed &&
                    output.childItems.Count == 0 && delivered.childItems.All(product => product.parentInventory == delivered) &&
                    machine.ActionCalls == (completed ? 1 : 0) &&
                    cell.GetTagReadonly("energy").GetInt() == (mode == "power" ? 0 : 100 - (completed ? definition.BasePowerCost : 0)),
                    "Refiner refill, per-night limit, dispatch or power settlement changed: " + mode);
            }
        }
        finally { ContentRuntime.Detach(Owner); }
        WineBottleSafety(check);
    }

    private static void WineBottleSafety(Action<bool, string> check)
    {
        var original = GlassRecycling.Feedstocks.Select(feedstock => feedstock.Recipe).ToArray();
        var wine = GlassRecycling.Feedstocks.Single(feedstock => feedstock.ItemId == "wine_bottle");
        var shard = GlassRecycling.Feedstocks.Single(feedstock => feedstock.ItemId == "glass_shard");
        // An invalid wine batch must not mask another eligible glass recipe.
        GlassRecycling.UseRecipes(new[] { wine.Recipe }.Concat(original.Where(recipe => recipe.Id != wine.Recipe.Id)).ToArray());
        try
        {
            foreach (string mode in new[] { "empty", "liquid", "fermenting", "finished", "ingredients", "mixed", "other_glass" })
            {
                PowerHelper.RefuseDraw = PowerHelper.ThrowAfterDebit = false;
                var definition = SynthesisDefinitions.Machine(SynthesisDefinitions.RefinerItem.Id);
                var machine = SynthesisMachines.Create(definition);
                GeneralHelper.SetItemOwned(machine, true);
                var battery = (GameSlotInventory)machine.Storages[0]!;
                var cell = Owned("battery"); battery.UncheckedAccept(cell);
                cell.GetTagReadonly("energy").SetInt(100);
                var input = (GameGridInventory)machine.Storages[2]!;
                var output = (GameGridInventory)machine.Storages[3]!;
                var bottle = Owned("wine_bottle", wine.RequiredCount);
                bottle.GetTagReadonly("WINE_CONTAINER_STATE").SetInt(mode == "fermenting" ? 1 : mode is "finished" or "mixed" or "other_glass" ? 2 : 0);
                if (mode == "liquid") bottle.GetTagReadonly("water").SetInt(1);
                if (mode == "ingredients")
                {
                    var contents = new GameGridInventory(); contents.UncheckedAccept(Owned("wine_berry"));
                    bottle.SetContentWindow(new(new MachineryPanel(contents)));
                }
                input.UncheckedAccept(bottle);
                GameItem? eligible = mode == "mixed" ? Owned("wine_bottle", wine.RequiredCount) :
                    mode == "other_glass" ? Owned(shard.ItemId, shard.RequiredCount) : null;
                if (eligible != null) input.UncheckedAccept(eligible);
                bool empty = mode == "empty";
                check(input.mayInventoryAddItemFunc!(bottle, input) == empty &&
                    SynthesisIngredients.Matches(bottle, wine.Input) == empty &&
                    SynthesisInterop.View(wine.Recipe).Ingredients.Single().Matches(bottle) == empty,
                    "Wine admission, production selection or logistics accepted nonempty wine: " + mode);
                machine.onCycleEndSlotItemFunc!(machine, input, new());
                bool produced = empty || eligible != null;
                int expectedOutput = mode == "other_glass" ? shard.Recipe.BaseOutputCount : wine.Recipe.BaseOutputCount;
                check(output.childItems.Count == (produced ? expectedOutput : 0) &&
                    cell.GetTagReadonly("energy").GetInt() == (produced ? 100 - definition.BasePowerCost : 100),
                    "Wine safety changed refiner production or energy: " + mode);
                check(empty ? bottle.Destroyed : !bottle.Destroyed && bottle.parentInventory == input && bottle.unitCount == wine.RequiredCount,
                    "Refiner consumed an ineligible wine bottle: " + mode);
                if (eligible != null) check(eligible.Destroyed, "Invalid wine masked eligible glass: " + mode);
                if (mode == "ingredients") check(bottle.FindAllChildItems().Single().identifier == "wine_berry",
                    "Wine recycling discarded the bottle contents");
            }
        }
        finally { GlassRecycling.UseRecipes(original); }
    }
}
