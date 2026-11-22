using Nicokobo.ContentInterop;

internal static class InteropProtocolChecks
{
    private const string Prefix = "checks.interop.protocol.";

    private static GameItem Owned(string id, int count = 1)
    {
        var item = new GameItem { identifier = Prefix + id, unitCount = count };
        GeneralHelper.SetItemOwned(item, true);
        return item;
    }

    private static RecipeView Recipe(string id, string machine, Func<GameItem, bool>? available = null) =>
        new(Prefix + id, machine, new[]
        {
            new IngredientView("scrap_metal", 2, item => item.identifier == "scrap_metal", "废金属", "Scrap metal")
        }, "metal_ingot", 1, 500, available);

    internal static void Run(Action<bool, string> check)
    {
        var attached = new HashSet<string>(StringComparer.Ordinal);
        string Attach(string suffix)
        {
            string owner = Prefix + suffix;
            ContentRuntime.Attach(owner); attached.Add(owner); return owner;
        }
        void Detach(string owner) { ContentRuntime.Detach(owner); attached.Remove(owner); }
        try
        {
            // Refill from real stock before the snapshot, then consume from two
            // stacks and produce both a new item and an increment to an old stack.
            {
                string owner = Attach("movement");
                var machine = Owned("movement.machine");
                var input = new GameGridInventory(); var output = new GameGridInventory();
                var supply = new GameGridInventory(); var waterSlot = new GameSlotInventory();
                var present = Owned("feedstock", 2); var refill = Owned("feedstock", 5);
                var oldProduct = Owned("product", 4); var newProduct = Owned("product");
                var water = Owned("water"); var supplyWater = Owned("water");
                input.UncheckedAccept(present); supply.UncheckedAccept(refill);
                output.UncheckedAccept(oldProduct); waterSlot.UncheckedAccept(water);
                water.GetTagReadonly("water").SetInt(1000); supplyWater.GetTagReadonly("water").SetInt(2000);
                var context = new WorkContext(machine, new MachinePorts(input, output, water: waterSlot),
                    Recipe("movement.recipe", machine.identifier));
                var results = new List<WorkResult>(); int before = 0;
                ProductionHooks.Subscribe(owner, owner + ".stock", current =>
                {
                    if (current.Machine != machine) return;
                    before++;
                    if (!input.UncheckedAccept(refill)) throw new InvalidOperationException("Fixture refill rejected");
                    WaterHelper.Remove(supplyWater, 500);
                    water.GetTagReadonly("water").SetInt(WaterHelper.GetTotalVolume(water) + 500);
                }, result => { if (result.Context.Machine == machine) results.Add(result); });
                using (var attempt = ProductionHooks.Begin(context))
                {
                    check(!attempt.HasInputConsumption && refill.parentInventory == input && supply.childItems.Count == 0,
                        "Before-work refill was counted as work consumption or did not move real stock");
                    present.SetUnitCount(1); refill.SetUnitCount(2);
                    oldProduct.SetUnitCount(6); output.UncheckedAccept(newProduct);
                    WaterHelper.Remove(water, 300);
                    check(attempt.HasInputConsumption, "Real stack consumption was not visible to the production attempt");
                    attempt.Commit();
                }
                var result = results.Single();
                check(before == 1 && result.Committed && result.Context == context,
                    "Production callbacks lost the committed attempt or its original context");
                check(result.Consumed.Count == 2 && result.Consumed.Single(take => take.Item == present).Amount == 1 &&
                    result.Consumed.Single(take => take.Item == refill).Amount == 3,
                    "Consumption followed the recipe estimate instead of the actual post-refill stack changes");
                check(output.childItems.Count == 2 && oldProduct.unitCount == 6 && newProduct.parentInventory == output,
                    "Committed output did not retain the previous stack and new product");
                check(result.WaterPartsConsumed == 300 && WaterHelper.GetTotalVolume(water) == 1200 &&
                    WaterHelper.GetTotalVolume(supplyWater) == 1500,
                    "Water receipt included the refill, the recipe estimate, or an invented balance");
                Detach(owner);
            }

            // A native wrapper and its managed recipe adapter share one attempt.
            {
                string owner = Attach("nested");
                var machine = Owned("nested.machine"); var input = new GameGridInventory(); var output = new GameGridInventory();
                var source = Owned("ore", 3); input.UncheckedAccept(source);
                var context = new WorkContext(machine, new MachinePorts(input, output), null);
                int before = 0; var results = new List<WorkResult>();
                ProductionHooks.Subscribe(owner, owner + ".observe", current => { if (current.Machine == machine) before++; },
                    result => { if (result.Context.Machine == machine) results.Add(result); });
                var outer = ProductionHooks.Begin(context);
                try
                {
                    var inner = ProductionHooks.Begin(new WorkContext(machine, context.Ports, Recipe("nested.recipe", machine.identifier)));
                    try
                    {
                        source.SetUnitCount(2); output.UncheckedAccept(Owned("ingot"));
                        inner.Commit();
                    }
                    finally { inner.Dispose(); inner.Dispose(); }
                    check(before == 1 && results.Count == 0, "A nested same-machine scope invoked callbacks independently");
                }
                finally { outer.Dispose(); outer.Dispose(); }
                check(before == 1 && results.Count == 1 && results[0].Committed && results[0].Consumed.Single().Amount == 1 &&
                    output.childItems.Single().unitCount == 1, "Nested commit or repeated Dispose duplicated a production receipt");
                Detach(owner);
            }

            foreach (bool rollback in new[] { false, true })
            {
                string owner = Attach(rollback ? "rollback" : "rejected");
                var machine = Owned(rollback ? "rollback.machine" : "rejected.machine");
                var input = new GameGridInventory(); var output = new GameGridInventory(); var waterSlot = new GameSlotInventory();
                var source = Owned("feedstock", 2); var water = Owned("water"); var provisional = Owned("provisional");
                input.UncheckedAccept(source); waterSlot.UncheckedAccept(water); water.GetTagReadonly("water").SetInt(1000);
                WorkResult? result = null;
                ProductionHooks.Subscribe(owner, owner + ".observe", null,
                    current => { if (current.Context.Machine == machine) result = current; });
                using (var attempt = ProductionHooks.Begin(new(machine, new(input, output, water: waterSlot), null)))
                {
                    if (rollback)
                    {
                        try
                        {
                            input.Expel(source); output.UncheckedAccept(provisional); WaterHelper.Remove(water, 250);
                            throw new InvalidOperationException("Fixture final debit refused");
                        }
                        catch (InvalidOperationException)
                        {
                            provisional.Destroy(); input.UncheckedAccept(source); water.GetTagReadonly("water").SetInt(1000);
                        }
                    }
                    check(!attempt.HasInputConsumption, "A refused or fully restored attempt retained logical input consumption");
                }
                check(result is { Committed: false } && result.Consumed.Count == 0 && result.WaterPartsConsumed == 0,
                    "Rejected/rolled-back production emitted a successful resource receipt");
                check(source.parentInventory == input && source.unitCount == 2 && output.childItems.Count == 0 &&
                    WaterHelper.GetTotalVolume(water) == 1000 && (!rollback || provisional.Destroyed),
                    "The rollback fixture did not restore actual resources");
                Detach(owner);
            }

            // Observer failure happens after commit; it must not reverse the
            // producer's result or starve another provider's observer.
            {
                string failedOwner = Attach("after.failure"); string liveOwner = Attach("after.live");
                var machine = Owned("after.machine"); var input = new GameGridInventory(); var output = new GameGridInventory();
                var source = Owned("feedstock"); var product = Owned("product"); input.UncheckedAccept(source);
                int failedBefore = 0, failedAfter = 0, liveBefore = 0, liveAfter = 0;
                WorkResult? observed = null;
                ProductionHooks.Subscribe(failedOwner, failedOwner + ".observe", context =>
                { if (context.Machine == machine) failedBefore++; }, result =>
                {
                    if (result.Context.Machine != machine) return;
                    failedAfter++; throw new InvalidOperationException("Fixture observer failed after commit");
                });
                ProductionHooks.Subscribe(liveOwner, liveOwner + ".observe", context =>
                { if (context.Machine == machine) liveBefore++; }, result =>
                { if (result.Context.Machine == machine) { liveAfter++; observed = result; } });
                using (var attempt = ProductionHooks.Begin(new(machine, new(input, output), null)))
                { input.Expel(source); source.Destroy(); output.UncheckedAccept(product); attempt.Commit(); }
                check(failedAfter == 1 && liveAfter == 1 && observed is { Committed: true } &&
                    observed.Consumed.Single().Amount == 1 && product.parentInventory == output,
                    "After-work observer failure escaped or prevented another observer from seeing the real commit");
                check(source.Destroyed && product.parentInventory == output, "Observer failure rolled back already committed production");
                Detach(failedOwner);
                using (ProductionHooks.Begin(new(machine, new(input, output), null))) { }
                check(failedBefore == 1 && failedAfter == 1 && liveBefore == 2 && liveAfter == 2 &&
                    !ContentRuntime.IsAttached(failedOwner) && ContentRuntime.IsAttached(liveOwner),
                    "Detaching one owner removed a foreign subscription or left its own subscription active");
                Detach(liveOwner);
            }

            // A failed pre-work hook must release its scope so a later attempt
            // is not treated as a child of an abandoned attempt.
            {
                string failedOwner = Attach("before.failure"); string liveOwner = Attach("before.live");
                var machine = Owned("before.machine"); var input = new GameGridInventory(); var output = new GameGridInventory();
                var context = new WorkContext(machine, new(input, output), null); int liveBefore = 0, liveAfter = 0;
                ProductionHooks.Subscribe(failedOwner, failedOwner + ".observe", current =>
                { if (current.Machine == machine) throw new InvalidOperationException("Fixture pre-work failure"); }, null);
                bool failed = false;
                try { using var attempt = ProductionHooks.Begin(context); }
                catch (InvalidOperationException) { failed = true; }
                Detach(failedOwner);
                ProductionHooks.Subscribe(liveOwner, liveOwner + ".observe", current =>
                { if (current.Machine == machine) liveBefore++; }, result =>
                { if (result.Context.Machine == machine) liveAfter++; });
                using (ProductionHooks.Begin(context)) { }
                check(failed && liveBefore == 1 && liveAfter == 1,
                    "A failed Before-work callback left an abandoned root scope active");
                Detach(liveOwner);
            }

            {
                string primary = Attach("catalog.primary"); string contributor = Attach("catalog.contributor");
                var machine = Owned("catalog.machine"); var input = new GameGridInventory(); var output = new GameGridInventory();
                var ports = new MachinePorts(input, output);
                var main = Recipe("catalog.main", machine.identifier); var added = Recipe("catalog.added", machine.identifier);
                MachineCatalog.Register(primary, machine.identifier, _ => ports, () => new[] { main });
                MachineCatalog.AddNativeRecipes(contributor, machine.identifier, () => new[] { added });
                check(MachineCatalog.Ports(machine) == ports && MachineCatalog.Recipes(machine).Select(recipe => recipe.Id)
                    .OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(new[] { main.Id, added.Id }.OrderBy(id => id, StringComparer.Ordinal)),
                    "The machine catalog lost a provider's recipe or its actual inventory ports");
                Detach(primary);
                check(!MachineCatalog.Contains(machine.identifier) && MachineCatalog.Ports(machine) == null &&
                    MachineCatalog.Recipes(machine).Single().Id == added.Id,
                    "Removing the host registration also removed a foreign recipe contribution");
                Detach(contributor);
                check(MachineCatalog.Recipes(machine).Count == 0, "A detached recipe contributor remained in the machine catalog");
            }

            {
                string glassOwner = Attach("furnace.glass"); string metalOwner = Attach("furnace.metal");
                var furnace = Owned("unused"); furnace.identifier = "furnace";
                var input = new GameGridInventory(); var output = new GameGridInventory(); var modules = new GameGridInventory(); var battery = new GameSlotInventory();
                furnace.Storages[0] = battery; furnace.Storages[1] = modules; furnace.Storages[2] = input; furnace.Storages[3] = output;
                var glass = Recipe("furnace.glass.recipe", "furnace");
                var metal = Recipe("furnace.metal.recipe", "furnace", item => item.IsTag(Prefix + "blast"));
                MachineCatalog.AddNativeRecipes(glassOwner, "furnace", () => new[] { glass });
                MachineCatalog.AddNativeRecipes(metalOwner, "furnace", () => new[] { metal });
                var ports = MachineCatalog.Ports(furnace);
                check(ports != null && ports.Input == input && ports.Output == output && ports.Modules == modules && ports.Battery == battery,
                    "Native furnace contributions did not expose the actual stable inventory slots");
                check(MachineCatalog.Recipes(furnace).Any(recipe => recipe.Id == glass.Id) &&
                    !MachineCatalog.Recipes(furnace).Any(recipe => recipe.Id == metal.Id), "Machine-dependent native recipe eligibility was ignored");
                furnace.EnableTag(Prefix + "blast");
                check(MachineCatalog.Recipes(furnace).Any(recipe => recipe.Id == glass.Id) &&
                    MachineCatalog.Recipes(furnace).Any(recipe => recipe.Id == metal.Id), "Multiple native recipe contributors did not compose");
                Detach(glassOwner);
                check(!MachineCatalog.Recipes(furnace).Any(recipe => recipe.Id == glass.Id) &&
                    MachineCatalog.Recipes(furnace).Any(recipe => recipe.Id == metal.Id), "Detaching glass recipes removed another furnace provider");
                Detach(metalOwner);
                check(!MachineCatalog.Recipes("furnace").Any(recipe => recipe.Id.StartsWith(Prefix, StringComparison.Ordinal)),
                    "Protocol checks leaked a native furnace recipe contribution");
            }
        }
        finally
        {
            foreach (string owner in attached.ToArray()) ContentRuntime.Detach(owner);
        }
    }
}
