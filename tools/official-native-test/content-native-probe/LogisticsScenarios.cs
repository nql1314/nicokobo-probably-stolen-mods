using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private object LogisticsRuntime => Reflect.Get(Reflect.Type("Nicokobo.AutomatedLogistics.Integration.NativeHooks"), "Runtime")
        ?? throw new InvalidOperationException("Ordinary logistics runtime was not installed");
    private long GridTotal => Convert.ToInt64(Reflect.Call(Logistics, "PowerTotal"));
    private JToken LogisticsState => JToken.Parse((string)Reflect.Call(Logistics, "StateJson")!);
    private void UnlockFixtureUpgrade(string id, HashSet<string>? visited = null)
    {
        visited ??= new(StringComparer.Ordinal);
        if (!visited.Add(id)) return;
        if (!Store.networkUpgrade.TryGetValue(id, out var upgrade)) throw new FixtureUnavailable("Native network fixture definition unavailable: " + id);
        foreach (string prerequisite in upgrade.prerequisite ?? new List<string>()) UnlockFixtureUpgrade(prerequisite, visited);
        if (!upgrade.IsUnlocked()) upgrade.Unlock();
    }
    private void PrepareLogisticsNetworks()
    {
        Reflect.Call(Logistics, "StateJson");
        // These are pre-action fixture prerequisites. The actual grant is still
        // performed by ordinary upgrade hooks / UnlockAvailableNetworks.
        foreach (string id in new[] { "SHOWCASE_I", "RUSTWATER_TAP", "COMMERCIAL_POWER" }) UnlockFixtureUpgrade(id);
        Reflect.Call(LogisticsRuntime, "UnlockAvailableNetworks");
        var state = LogisticsState;
        R.Check("logistics.native-prerequisite-rewards", new { ResourcesUnlocked = true, WaterUnlocked = true, PowerUnlocked = true }, state,
            (bool?)state["ResourcesUnlocked"] == true && (bool?)state["WaterUnlocked"] == true && (bool?)state["Power"]?["Unlocked"] == true,
            "Native NetworkUpgrade.Unlock fixture -> normal hook/UnlockAvailableNetworks; payment UI is outside this check");
    }
    private IEnumerable<object?> LogisticsScenarios()
    {
        R.phase = "logistics-real-network-transactions";
        PrepareLogisticsNetworks();
        object runtime = LogisticsRuntime;
        float rewardWait = Time.realtimeSinceStartup + 1.5f;
        while (Time.realtimeSinceStartup < rewardWait) yield return null;
        var reward = LogisticsState;
        int terminalBefore = Store.FindAllItem().Count(i => i.identifier == "nicokobo.automated_logistics.network_terminal");
        R.Check("logistics.terminal-receipt", "TerminalGranted and one normal opening terminal", new { state = reward, terminalCount = terminalBefore },
            (bool?)reward["TerminalGranted"] == true && terminalBefore == 1);
        // Destroy the awarded terminal as a disposable player action. A durable
        // receipt must prevent the ordinary updater from awarding another one.
        var terminal = Store.FindAllItem().SingleOrDefault(i => i.identifier == "nicokobo.automated_logistics.network_terminal");
        Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(terminal!);
        float noRegrant = Time.realtimeSinceStartup + 1.5f;
        while (Time.realtimeSinceStartup < noRegrant) yield return null;
        R.Check("logistics.destroyed-reward-not-regranted", 0, Store.FindAllItem().Count(i => i.identifier == "nicokobo.automated_logistics.network_terminal"),
            Store.FindAllItem().All(i => i.identifier != "nicokobo.automated_logistics.network_terminal") && (bool?)LogisticsState["TerminalGranted"] == true);
        var resources = (GameGridInventory?)Reflect.Call(Logistics, "ResourceRegion") ?? throw new InvalidOperationException("Rewarded real resource grid unavailable");
        var tank = (GameItem?)Reflect.Call(Logistics, "ResourceWaterTank") ?? throw new InvalidOperationException("Rewarded real water tank unavailable");
        var powerRegion = (GameGridInventory?)Reflect.Call(Logistics, "PowerRegion") ?? throw new InvalidOperationException("Rewarded real power grid unavailable");
        // Wire is not stackable: TryAcceptAll splits SetUnitCount(5) into five
        // objects, leaving the original handle with only one unit.
        var sources = Enumerable.Range(0, 5).Select(_ => Spawn("wire")).ToArray();
        foreach (var source in sources) Retain(source);
        int globalBefore = Store.FindAllItem().Where(i => i.identifier == "wire").Sum(i => i.unitCount);
        int[] globalUidsBefore = Store.FindAllItem().Where(i => i.identifier == "wire").Select(i => i.uniqueId).OrderBy(id => id).ToArray();
        int[] depositedUids = sources.Take(3).Select(i => i.uniqueId).OrderBy(id => id).ToArray();
        var deposits = sources.Take(3).Select(source => Reflect.Json(Reflect.Call(Logistics, "DepositResource", source, 1))).ToArray();
        int network = resources.childItems.Where(i => i.identifier == "wire").Sum(i => i.unitCount);
        int[] networkUids = resources.childItems.Where(i => i.identifier == "wire").Select(i => i.uniqueId).OrderBy(id => id).ToArray();
        R.Check("logistics.deposit-real-items", new { networkAdded = 3, uids = depositedUids, globalUnits = globalBefore, globalUids = globalUidsBefore },
            new { results = deposits, network, uids = networkUids, globalUnits = Store.FindAllItem().Where(i => i.identifier == "wire").Sum(i => i.unitCount),
                globalUids = Store.FindAllItem().Where(i => i.identifier == "wire").Select(i => i.uniqueId).OrderBy(id => id).ToArray() },
            deposits.All(result => (int?)result["Moved"] == 1 && (bool?)result["Indeterminate"] == false) && network == 3 && networkUids.SequenceEqual(depositedUids) &&
                Store.FindAllItem().Where(i => i.identifier == "wire").Sum(i => i.unitCount) == globalBefore &&
                Store.FindAllItem().Where(i => i.identifier == "wire").Select(i => i.uniqueId).OrderBy(id => id).SequenceEqual(globalUidsBefore));
        int[] withdrawUids = depositedUids.Take(2).ToArray();
        var withdrawals = withdrawUids.Select(uid => Reflect.Json(Reflect.Call(Logistics, "WithdrawResource", uid, 1, EmporiumEntry.Instance.invElement))).ToArray();
        int[] remainingUids = resources.childItems.Where(i => i.identifier == "wire").Select(i => i.uniqueId).OrderBy(id => id).ToArray();
        R.Check("logistics.withdraw-real-items", new { remainingNetwork = 1, remainingUids = depositedUids.Skip(2).ToArray(), withdrawnUids = withdrawUids, globalUnits = globalBefore, globalUids = globalUidsBefore },
            new { results = withdrawals, remainingNetwork = resources.childItems.Where(i => i.identifier == "wire").Sum(i => i.unitCount), remainingUids,
                withdrawn = EmporiumEntry.Instance.invElement.childItems.Where(i => withdrawUids.Contains(i.uniqueId)).Select(ItemState).ToArray(),
                globalUnits = Store.FindAllItem().Where(i => i.identifier == "wire").Sum(i => i.unitCount),
                globalUids = Store.FindAllItem().Where(i => i.identifier == "wire").Select(i => i.uniqueId).OrderBy(id => id).ToArray() },
            withdrawals.All(result => (int?)result["Moved"] == 1 && (bool?)result["Indeterminate"] == false) &&
                resources.childItems.Where(i => i.identifier == "wire").Sum(i => i.unitCount) == 1 && remainingUids.SequenceEqual(depositedUids.Skip(2)) &&
                EmporiumEntry.Instance.invElement.childItems.Where(i => withdrawUids.Contains(i.uniqueId) && i.unitCount == 1).Select(i => i.uniqueId).OrderBy(id => id).SequenceEqual(withdrawUids) &&
                Store.FindAllItem().Where(i => i.identifier == "wire").Sum(i => i.unitCount) == globalBefore &&
                Store.FindAllItem().Where(i => i.identifier == "wire").Select(i => i.uniqueId).OrderBy(id => id).SequenceEqual(globalUidsBefore));
        // SHOWCASE_I already unlocked an 8x6 native save-backed inventory. Keep
        // the two 4x4 liquid fixtures away from retained production machines.
        var jug = Spawn("water_jug"); WaterHelper.EmptyContainer(jug); WaterHelper.AddPureWater(jug, 200000); WaterHelper.AddLiquid(jug, "alcohol", 50000);
        Place(EmporiumEntry.Instance.showcaseElement, jug, "logistics-mixture-jug"); Retain(jug);
        int jugId = jug.uniqueId, tankId = tank.uniqueId;
        JToken liquidBefore = AddLiquidStates(jug, tank);
        int jugParts = WaterHelper.GetTotalVolume(jug), tankParts = WaterHelper.GetTotalVolume(tank);
        object? pour = Reflect.Call(Logistics, "PourResource", jug, 125000);
        JToken afterPour = AddLiquidStates(jug, tank);
        R.Check("logistics.pour-mixture-conservation", new { sourceDebit = 125000, targetCredit = 125000, components = liquidBefore, jugId, tankId },
            new { result = Reflect.Json(pour), sourceDebit = jugParts - WaterHelper.GetTotalVolume(jug), targetCredit = WaterHelper.GetTotalVolume(tank) - tankParts, components = afterPour, jug.uniqueId, tankUid = tank.uniqueId },
            jugParts - WaterHelper.GetTotalVolume(jug) == 125000 && WaterHelper.GetTotalVolume(tank) - tankParts == 125000 && JToken.DeepEquals(liquidBefore, afterPour) && jug.uniqueId == jugId && tank.uniqueId == tankId);
        var empty = Spawn("water_jug"); WaterHelper.EmptyContainer(empty);
        Place(EmporiumEntry.Instance.showcaseElement, empty, "logistics-empty-jug"); Retain(empty);
        JToken withdrawBefore = AddLiquidStates(jug, tank, empty);
        int initialTank = WaterHelper.GetTotalVolume(tank);
        object? liquidWithdrawal = Reflect.Call(Logistics, "WithdrawResourceLiquid", tank.uniqueId, empty, 50000);
        R.Check("logistics.withdraw-mixture-conservation", new { targetCredit = 50000, sourceDebit = 50000, components = withdrawBefore },
            new { result = Reflect.Json(liquidWithdrawal), targetCredit = WaterHelper.GetTotalVolume(empty), sourceDebit = initialTank - WaterHelper.GetTotalVolume(tank), components = AddLiquidStates(jug, tank, empty) },
            WaterHelper.GetTotalVolume(empty) == 50000 && initialTank - WaterHelper.GetTotalVolume(tank) == 50000 && JToken.DeepEquals(withdrawBefore, AddLiquidStates(jug, tank, empty)));
        foreach (object? wait in PowerCases(powerRegion)) yield return wait;
        foreach (object? wait in RealRestockAndEject(resources)) yield return wait;
        R.persistence["rewardReceiptBeforeSave"] = LogisticsState;
        R.Skip("logistics.aquaculture-full-native-night", "Aquaculture electricity and same-night generator deduplication need a complete powered RAS night fixture; no synthetic receipt/state mutation is used", false);
    }
    private static JToken AddLiquidStates(params GameItem[] items) => new JObject(Liquid.Liquids.OrderBy(l => l.identifier, StringComparer.Ordinal)
        .Select(l => new JProperty(l.identifier, items.Sum(i => (long)i.GetTagReadonly(l.currentPart).GetInt()))));
    private IEnumerable<object?> PowerCases(GameGridInventory powerRegion)
    {
        var machine = Spawn("nicokobo.mechcore.synthesis.synthesizer"); Retain(machine);
        var slot = (GameSlotInventory)MachineHelper.GetBodyStorage(machine, 0);
        var devicePower = Reflect.Type("Nicokobo.ContentInterop.DevicePower");
        bool Debit(GameItem source, int cost, out bool handled)
        {
            var args = new object?[] { source, cost, false };
            handled = (bool)Reflect.Invoke(devicePower.GetMethod("TryDebit", Reflect.All)!, null, args)!;
            return (bool)args[2]!;
        }
        void SetLocal(int charge)
        {
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(slot.childItem!); var cell = PreBuiltItemHelper.EnergyCredit(charge); Place(slot, cell, "shared-grid-local-battery", machine);
        }
        void SetGridByRealTransfers(long requested)
        {
            SetLocal(0);
            while (GridTotal > requested)
            {
                int cost = (int)Math.Min(100, GridTotal - requested);
                if (!Debit(slot.childItem, cost, out bool handled) || !handled) throw new InvalidOperationException("Cannot arrange grid balance through actual payment");
            }
            while (GridTotal < requested)
            {
                int add = checked((int)Math.Min(100, requested - GridTotal)); var donor = PreBuiltItemHelper.EnergyCredit(add);
                int physical = Energy(donor); long before = GridTotal;
                Place(powerRegion, donor, "shared-grid-donor"); Reflect.Call(LogisticsRuntime, "CollectGridEnergy");
                R.Check("logistics.collect-grid-real-energy", new { added = physical, conservation = true }, new { gridAdded = GridTotal - before, physicalDebit = physical - Energy(donor) }, GridTotal - before == physical - Energy(donor) && GridTotal == before + add);
                if (GridTotal != before + add) throw new InvalidOperationException("Grid fixture transfer failed conservation");
            }
        }
        (string Name, int Local, long Grid, int Cost, int LocalAfter, long GridAfter, bool Success)[] cases = { ("above-reserve-local", 26, 40L, 20, 6, 40L, true),
            ("exact-quarter-whole-grid", 25, 40L, 20, 25, 20L, true), ("grid-insufficient-whole-local", 25, 19L, 20, 5, 19L, true),
            ("no-combination-on-failure", 2, 3L, 4, 2, 3L, false), ("cost-exceeds-local-capacity", 100, 200L, 101, 100, 200L, false) };
        foreach (var c in cases)
        {
            SetGridByRealTransfers(c.Grid); SetLocal(c.Local);
            GameItem battery = slot.childItem; int uid = battery.uniqueId;
            bool success = Debit(battery, c.Cost, out bool handled);
            R.Check("logistics.shared-power." + c.Name, new { handled = true, success = c.Success, local = c.LocalAfter, grid = c.GridAfter, uid },
                new { handled, success, local = Energy(battery), grid = GridTotal, uid = battery.uniqueId, capacity = battery.GetTagReadonly("power_source_item_max_energy").GetInt() },
                handled && success == c.Success && Energy(battery) == c.LocalAfter && GridTotal == c.GridAfter && uid == battery.uniqueId,
                "Actual installed DevicePower.TryDebit; expected payments are independent explicit 25% boundary requirements");
            yield return null;
        }
    }
    private IEnumerable<object?> RealRestockAndEject(GameGridInventory resources)
    {
        // This stable two-output recipe gives a receipt based on real
        // consumption before replenishment is attempted.
        object view = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", "nicokobo.mechcore.synthesis.synthesizer"))
            .Single(x => Reflect.Str(x, "Id") == "nicokobo.automated_logistics.make_ejector");
        using var f = CreateRecipeFixture(view);
        Place(f.Modules!, Spawn("nicokobo.automated_logistics.auto_feeder"), "restock-module", f.Machine);
        Place(f.Modules!, Spawn("nicokobo.automated_logistics.output_ejector"), "eject-module", f.Machine);
        // Consumption tracking is part of the enabled feeder. There is no
        // pending debt yet, so this first cycle cannot refill the seed inputs.
        Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false);
        try { ObserveBatch(f, "logistics-demand-seed", true); }
        finally { Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false); }
        int[] outputIds = f.Output.childItems.Select(i => i.uniqueId).ToArray();
        var available = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (object rule in Reflect.Each(Reflect.Get(view, "Ingredients")))
        {
            int amount = Reflect.Int(rule, "Amount");
            var ingredients = Enumerable.Range(0, amount).Select(_ => Ingredient(rule)).ToArray();
            foreach (var ingredient in ingredients) Retain(ingredient);
            string id = ingredients[0].identifier; available[id] = Store.FindAllItem().Where(i => i.identifier == id).Sum(i => i.unitCount);
            foreach (var ingredient in ingredients)
            {
                int uid = ingredient.uniqueId;
                JToken deposited = Reflect.Json(Reflect.Call(Logistics, "DepositResource", ingredient, 1));
                R.Check("logistics.restock-source-deposit." + id + "." + uid, new { moved = 1, uid, globalUnits = available[id] },
                    new { result = deposited, item = ItemState(ingredient), globalUnits = Store.FindAllItem().Where(i => i.identifier == id).Sum(i => i.unitCount) },
                    (int?)deposited["Moved"] == 1 && (bool?)deposited["Indeterminate"] == false && ingredient.uniqueId == uid &&
                        ingredient.unitCount == 1 && ingredient.parentInventory == resources &&
                        Store.FindAllItem().Where(i => i.identifier == id).Sum(i => i.unitCount) == available[id]);
            }
        }
        Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, false);
        Reflect.Call(Logistics, "FillOnce", f.Machine);
        var expected = Reflect.Each(Reflect.Get(view, "Ingredients")).ToDictionary(i => Reflect.Str(i, "ItemId"), i => Reflect.Int(i, "Amount"));
        var input = f.Input.childItems.GroupBy(i => i.identifier).ToDictionary(g => g.Key, g => g.Sum(i => i.unitCount));
        R.Check("logistics.real-consumption-restock", expected, input, expected.All(pair => input.TryGetValue(pair.Key, out int count) && count == pair.Value) && available.All(pair => Store.FindAllItem().Where(i => i.identifier == pair.Key).Sum(i => i.unitCount) == pair.Value),
            "Real native cycle records consumption -> actual DepositResource -> FillOnce; no fabricated demand state");
        Reflect.Call(Logistics, "ConfigureMachine", f.Machine, true, true);
        Reflect.Call(Logistics, "EjectOnce", f.Machine);
        var physical = Store.FindAllItem().Where(i => outputIds.Contains(i.uniqueId)).ToArray();
        R.Check("logistics.real-output-eject", new { outputBay = 0, expectedUids = outputIds }, new { outputBay = f.Output.childItems.Count, actual = physical.Select(ItemState).ToArray() },
            f.Output.childItems.Count == 0 && physical.Length == outputIds.Length && physical.All(i => i.parentInventory != f.Output));
        Reflect.Call(Logistics, "ConfigureMachine", f.Machine, false, false);
        f.Retained = true; Retain(f.Machine);
        yield return null;
    }
}
