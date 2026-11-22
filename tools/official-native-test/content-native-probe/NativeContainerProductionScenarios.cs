using Newtonsoft.Json.Linq;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private readonly JArray _cbxFixtures = new(), _cbxActions = new(), _cbxRecipes = new();
    private readonly HashSet<int> _cbxCommitted = new();
    private readonly Dictionary<int, (string Id, int Count)> _cbxIdentity = new();
    private GameItem? _cbxStore;
    private static readonly (string Id, int Cost, int Capacity, long ContainerPrice)[] CbxBottleRecipes =
    {
        ("small_bottled_water", 4, 450000, 4),
        ("bottled_water", 6, 750000, 5),
        ("large_bottled_water", 11, 2000000, 9)
    };

    internal IEnumerable<object?> NativeContainerProduction(bool includeBox = true)
    {
        R.phase = "containerproduction-real-native-buttons";
        R.environment["nativeContainerProductionScope"] = "Build26317870: three BottlePrinter buttons and one BoxDispenser button, native common_electronic targeting for all nine bottle upgrades, actual empty battery and physically full output controls, repeat output, exact energy/cash/UID/capacity ledgers, then normal ES3 and another process. All machines, batteries, electronics and output blockers are explicit registered-factory test inputs; economic acquisition and physical mouse gestures are not implied. No output, upgrade, power or completion tags are assigned.";
        R.environment["nativeContainerProductionIncludesBox"] = includeBox;
        if (!includeBox)
            R.environment["nativeContainerProductionScope"] = "Independent three-bottle production and native ES3 family. Same original BottlePrinter controls, nine real upgrades, no-power/full-output/repeat/UID/price/capacity/cash checks. The native BoxDispenser is outside this family's denominator and retains its separate required failure in containerproduction; bottle success cannot mark it passed.";
        R.environment["nativeContainerProductionFixtures"] = _cbxFixtures;
        R.environment["nativeContainerProductionActions"] = _cbxActions;
        R.environment["nativeContainerProductionRecipes"] = _cbxRecipes;
        foreach (var recipe in CbxBottleRecipes)
            _cbxRecipes.Add(new JObject { ["id"] = recipe.Id, ["machine"] = "bottle_printer", ["cost"] = recipe.Cost, ["capacity"] = recipe.Capacity, ["status"] = "not-tested" });
        if (includeBox) _cbxRecipes.Add(new JObject { ["id"] = "ammo_box", ["machine"] = "box_dispenser", ["cost"] = 1, ["status"] = "not-tested" });
        _cbxStore = CbxFixture("storage_bay_large", "native storage for produced original UIDs");
        foreach (object? wait in CbxCase("bottle-printer", CbxBottlePrinter)) yield return wait;
        if (includeBox) foreach (object? wait in CbxCase("box-dispenser", CbxBoxDispenser)) yield return wait;
        foreach (JObject row in _cbxRecipes)
            if ((string?)row["status"] != "passed") R.Skip("containerproduction.missing." + row["id"], "The actual production path did not complete; retain the first required failure and this recipe as not-tested.");
        int expectedUidCount = includeBox ? 11 : 7;
        CbxRequire("save-exact-promised-uid-count", expectedUidCount, _cbxCommitted.Count,
            _cbxCommitted.Count == expectedUidCount && _cbxIdentity.Count == expectedUidCount && _cbxCommitted.SetEquals(_cbxIdentity.Keys));
        foreach (object? wait in CaptureEvidenceFrame("native-container-production")) yield return wait;
        var items = CbxSnapshot(_cbxCommitted);
        CbxRequire("save-exact-item-row-uid-set", _cbxCommitted.OrderBy(uid => uid).ToArray(), items.Select(item => (int)item["uid"]!).ToArray(),
            items.Count == expectedUidCount && _cbxCommitted.SetEquals(items.Select(item => (int)item["uid"]!)) && items.Select(item => (int)item["uid"]!).Distinct().Count() == expectedUidCount);
        R.persistence["nativeContainerProduction"] = new JObject { ["schema"] = 1, ["includeBox"] = includeBox, ["uids"] = new JArray(_cbxCommitted.OrderBy(uid => uid)),
            ["items"] = items, ["recipes"] = _cbxRecipes.DeepClone(), ["actions"] = _cbxActions.DeepClone() };
    }

    private IEnumerable<object?> CbxCase(string name, Func<IEnumerable<object?>> body)
    {
        using var iterator = body().GetEnumerator();
        while (true)
        {
            object? next = null; bool more = false; Exception? error = null;
            try { more = iterator.MoveNext(); if (more) next = iterator.Current; }
            catch (Exception ex) { error = ex; }
            if (error != null) { R.Check("containerproduction.case." + name, "Actual native business path completes", null, false, R.phase, error.ToString()); yield break; }
            if (!more) yield break;
            yield return next;
        }
    }
    private void CbxRequire(string name, object? expected, object? observed, bool passed)
    {
        R.Check("containerproduction." + name, expected, observed, passed);
        if (!passed) throw new InvalidOperationException("Native container production: " + name);
    }
    private GameItem CbxFixture(string id, string purpose)
    {
        GameItem item = Spawn(id); Retain(item); CbxPledge(item);
        _cbxFixtures.Add(new JObject { ["purpose"] = purpose, ["item"] = FlItem(item) }); return item;
    }
    private void CbxPledge(GameItem root)
    {
        foreach (GameItem item in new[] { root }.Concat(root.FindAllChildItems()).Distinct())
        {
            bool existing = _cbxIdentity.TryGetValue(item.uniqueId, out var identity);
            bool valid = item.uniqueId > 0 && item.unitCount > 0 && GeneralHelper.IsItemOwned(item) &&
                (!existing || identity.Id == item.identifier && identity.Count == item.unitCount);
            CbxRequire("commit-original-uid." + item.uniqueId, "Positive owned UID; an existing commitment retains ID/count", FlItem(item), valid);
            if (!existing) _cbxIdentity.Add(item.uniqueId, (item.identifier, item.unitCount));
            _cbxCommitted.Add(item.uniqueId);
        }
    }
    private static MachineryPanelRow CbxPanels(GameItem machine) => machine.contentWindow?.childElement as MachineryPanelRow
        ?? throw new InvalidOperationException("Native machinery panel row missing: " + machine.identifier);
    private static MachineryButton[] CbxButtons(GameItem machine)
    {
        var content = CbxPanels(machine).GetPanel("control")?.content;
        return content is MachineryButton button ? new[] { button } : content is MachineryStack stack
            ? stack.elements.OfType<MachineryButton>().ToArray() : throw new InvalidOperationException("Actual native control panel missing");
    }
    private void CbxPress(GameItem machine, MachineryButton button, string label)
    {
        bool active = machine.contentWindow.IsVisible() && button.handler.gameObject.activeInHierarchy && !button.isHidden && !button.isDisabled && !button.requiresHold;
        CbxRequire("actual-button-enabled." + label, "Visible actual native button, enabled, no hold", new { active, button.text, button.isHidden, button.isDisabled, button.requiresHold }, active);
        _cbxActions.Add(JObject.FromObject(new { action = "actual-MachineryButton.Select", label, machineUid = machine.uniqueId, frame = UnityEngine.Time.frameCount }));
        bool selected = button.Select();
        CbxRequire("actual-button-select." + label, new { selected = true, deselectedByNativeCallback = true }, new { selected, button.isSelected }, selected && !button.isSelected);
    }
    private IEnumerable<object?> CbxBottlePrinter()
    {
        R.phase = "containerproduction-bottle-upgrades-and-production";
        GameItem machine = CbxFixture("bottle_printer", "native original bottle printer");
        machine.contentWindow.Show(); yield return null;
        var buttons = CbxButtons(machine);
        var output = MachineHelper.GetSlot(machine, 1);
        CbxRequire("bottle-three-controls-and-default-lock", new { buttons = 3, upgrades = 0, normalLocked = true, largeLocked = true },
            new { buttons = buttons.Length, upgrades = MachineBottlePrinter.GetUpgradeCount(machine), normalLocked = buttons.Length > 1 && buttons[1].isDisabled, largeLocked = buttons.Length > 2 && buttons[2].isDisabled },
            buttons.Length == 3 && MachineBottlePrinter.GetUpgradeCount(machine) == 0 && buttons[1].isDisabled && buttons[2].isDisabled);
        GameItem battery = PreBuiltItemHelper.EnergyCredit(100);
        Place(MachineHelper.GetBatterySlot(machine), battery, "container-bottle-battery");
        CbxPledge(battery);
        for (int upgrade = 1; upgrade <= 9; upgrade++)
        {
            GameItem part = Spawn("common_electronic"); Retain(part);
            long value = machine.unitValue; int cash = Store.playerCash, uid = part.uniqueId;
            bool may = part.MayTarget(machine), can = part.CanTarget(machine);
            CbxRequire("upgrade-admission." + upgrade, true, new { may, can }, may && can);
            part.Target(machine);
            CbxRequire("upgrade-transaction." + upgrade, new { upgrade, cash, machineValue = value + 33, partConsumed = true },
                new { count = MachineBottlePrinter.GetUpgradeCount(machine), cash = Store.playerCash, machine.unitValue, consumed = !Store.FindAllItem(false).Any(item => item.uniqueId == uid) },
                MachineBottlePrinter.GetUpgradeCount(machine) == upgrade && Store.playerCash == cash && machine.unitValue == value + 33 && !Store.FindAllItem(false).Any(item => item.uniqueId == uid));
            CbxRequire("upgrade-ui-gates." + upgrade, new { normalLocked = upgrade < 3, largeLocked = upgrade < 9 },
                new { normalLocked = buttons[1].isDisabled, largeLocked = buttons[2].isDisabled }, buttons[1].isDisabled == (upgrade < 3) && buttons[2].isDisabled == (upgrade < 9));
            yield return null;
        }
        foreach (object? wait in CbxEmptyPower(machine, buttons[0], output, battery, "bottle")) yield return wait;
        foreach (object? wait in CbxFullOutput(machine, buttons, output, battery, CbxBottleRecipes.Select(recipe => recipe.Id).ToArray())) yield return wait;
        for (int index = 0; index < CbxBottleRecipes.Length; index++)
        {
            var recipe = CbxBottleRecipes[index];
            GameItem product = CbxProduce(machine, buttons[index], output, battery, recipe.Id, recipe.Cost, recipe.Capacity, recipe.ContainerPrice);
            CbxStoreProduct(product, output);
            ((JObject)_cbxRecipes[index])["status"] = "passed";
            ((JObject)_cbxRecipes[index])["uid"] = product.uniqueId;
            yield return null;
        }
        GameItem repeated = CbxProduce(machine, buttons[0], output, battery, "small_bottled_water", 4, 450000, 4);
        CbxRequire("bottle-repeat-new-original-uid", "Different positive UID from first bottle", repeated.uniqueId, repeated.uniqueId != (int)_cbxRecipes[0]["uid"]!);
        machine.contentWindow.Hide();
    }
    private IEnumerable<object?> CbxBoxDispenser()
    {
        R.phase = "containerproduction-box-dispenser";
        GameItem machine = CbxFixture("box_dispenser", "native original box dispenser");
        machine.contentWindow.Show(); yield return null;
        var buttons = CbxButtons(machine); var output = MachineHelper.GetSlot(machine, 1);
        CbxRequire("box-one-native-control", 1, buttons.Length, buttons.Length == 1);
        GameItem battery = PreBuiltItemHelper.EnergyCredit(100); Place(MachineHelper.GetBatterySlot(machine), battery, "container-box-battery");
        CbxPledge(battery);
        foreach (object? wait in CbxEmptyPower(machine, buttons[0], output, battery, "box")) yield return wait;
        if (!DirectoryMaster.Has<GameItem>("ammo_box"))
        {
            // The current native dispenser requests an inactive directory's
            // output. Exercise its real button; never add a replacement factory.
            int energy = Energy(battery), cash = Store.playerCash;
            Exception? callbackError = null;
            try { CbxPress(machine, buttons[0], "box-missing-native-output-registration"); }
            catch (Exception error) { callbackError = error; }
            R.Check("containerproduction.box-native-output-registration-and-real-powered-button",
                new { registered = true, expectedId = "ammo_box", positiveOwnedOriginalOutput = true, energyCost = 1, cash },
                new { registered = DirectoryMaster.Has<GameItem>("ammo_box"), products = output.childItems.Select(FlItem).ToArray(), energyBefore = energy,
                    energyAfter = Energy(battery), cash = Store.playerCash, callbackError = callbackError?.ToString() }, false,
                "The actual registered BoxDispenser button ran with a real native battery. The absent original output factory is a current native-game defect; no dummy item counts as the promised ammo box.");
            machine.contentWindow.Hide();
            yield break;
        }
        foreach (object? wait in CbxFullOutput(machine, buttons, output, battery, new[] { "ammo_box" })) yield return wait;
        GameItem product = CbxProduce(machine, buttons[0], output, battery, "ammo_box", 1, null, null);
        CbxStoreProduct(product, output);
        ((JObject)_cbxRecipes[3])["status"] = "passed"; ((JObject)_cbxRecipes[3])["uid"] = product.uniqueId;
        GameItem repeated = CbxProduce(machine, buttons[0], output, battery, "ammo_box", 1, null, null);
        CbxRequire("box-repeat-new-original-uid", "Different positive UID from first box", repeated.uniqueId, repeated.uniqueId != product.uniqueId);
        machine.contentWindow.Hide();
    }
    private void CbxStoreProduct(GameItem item, GameInventory output)
    {
        int uid = item.uniqueId;
        var storage = (GameInventory)_cbxStore!.contentWindow.childElement;
        var slot = storage.TryFindOneValidInventorySlot(item);
        bool containerAdmits = slot != null && slot.IsValid() && slot.inventory == storage && slot.targetItem == null;
        if (containerAdmits) Place(storage, item, "container-store-" + item.identifier);
        else
        {
            // Native containers may reject another non-nestable container.
            // This is fixture custody, so use the ordinary top-level move.
            ModHelper.MoveItemToInventory(item);
            if (item.parentInventory == output) ModHelper.MoveItemToBackCounter(item);
        }
        bool moved = item.uniqueId == uid && item.unitCount == 1 && GeneralHelper.IsItemOwned(item) && item.parentInventory != null && item.parentInventory != output;
        CbxRequire("native-product-custody." + item.identifier, new { uid, moved = true }, new { item = FlItem(item), containerAdmits }, moved);
        CbxPledge(item);
        _cbxActions.Add(new JObject { ["action"] = "native-fixture-custody", ["uid"] = uid, ["containerAdmitted"] = containerAdmits, ["after"] = FlItem(item) });
    }
    private GameItem CbxProduce(GameItem machine, MachineryButton button, GameInventory output, GameItem battery, string id, int cost, int? capacity, long? containerPrice)
    {
        int energy = Energy(battery), cash = Store.playerCash;
        CbxRequire("empty-output-before." + id, 0, output.childItems.Count, output.childItems.Count == 0);
        CbxPress(machine, button, id);
        GameItem[] products = output.childItems.ToArray();
        bool valid = products.Length == 1 && products[0].identifier == id && products[0].unitCount == 1 && products[0].uniqueId > 0 && GeneralHelper.IsItemOwned(products[0]) &&
            products[0].parentInventory == output && Energy(battery) == energy - cost && Store.playerCash == cash &&
            (!capacity.HasValue || products[0].GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt() == capacity && WaterHelper.GetTotalVolume(products[0]) == 0) &&
            (!containerPrice.HasValue || products[0].unitValue == 0 && WaterHelper.GetContainerPrice(products[0]) == containerPrice.Value);
        CbxRequire("actual-production." + id, new { id, count = 1, cost, capacity, empty = capacity.HasValue, unitValue = containerPrice.HasValue ? 0L : (long?)null, containerPrice, cash },
            new { products = products.Select(FlItem).ToArray(), containerPrices = products.Select(WaterHelper.GetContainerPrice).ToArray(), energyBefore = energy, energyAfter = Energy(battery), cash = Store.playerCash }, valid);
        CbxPledge(products[0]);
        return products[0];
    }
    private IEnumerable<object?> CbxEmptyPower(GameItem machine, MachineryButton button, GameInventory output, GameItem battery, string label)
    {
        var slot = MachineHelper.GetBatterySlot(machine);
        bool expelled = slot.Expel(battery);
        CbxRequire("expel-supplied-battery." + label, true, expelled, expelled && battery.parentInventory == null);
        Retain(battery);
        GameItem empty = PreBuiltItemHelper.EnergyCredit(0); Place(slot, empty, "container-empty-battery-" + label);
        JArray before = new(output.childItems.Select(FlItem)); int cash = Store.playerCash;
        CbxPress(machine, button, "empty-power-" + label);
        R.Check("containerproduction.no-power-no-debit-or-output." + label, new { output = before, cash, energy = 0 },
            new { output = new JArray(output.childItems.Select(FlItem)), cash = Store.playerCash, energy = Energy(empty) },
            JToken.DeepEquals(before, new JArray(output.childItems.Select(FlItem))) && Store.playerCash == cash && Energy(empty) == 0);
        bool removed = slot.Expel(empty);
        CbxRequire("remove-empty-battery." + label, true, removed, removed && empty.parentInventory == null);
        Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(empty);
        Place(slot, battery, "container-restore-battery-" + label);
        yield return null;
    }
    private IEnumerable<object?> CbxFullOutput(GameItem machine, MachineryButton[] buttons, GameInventory output, GameItem battery, string[] ids)
    {
        var blockers = new List<GameItem>();
        try
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                GameItem sample = Spawn(ids[0]);
                var slot = output.TryFindOneValidInventorySlot(sample);
                if (slot == null || !slot.IsValid() || slot.inventory != output || slot.targetItem != null)
                { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(sample); break; }
                int transferred = slot.TryAcceptOnce();
                CbxRequire("full-output-real-blocker." + machine.identifier + "." + attempt, 1, transferred, transferred == 1 && sample.parentInventory == output);
                blockers.Add(sample);
            }
            CbxRequire("full-output-bounded-fill." + machine.identifier, "At least one real blocker and fewer than100", blockers.Count, blockers.Count > 0 && blockers.Count < 100);
            for (int index = 0; index < ids.Length; index++)
            {
                GameItem sample = Spawn(ids[index]);
                bool blocked;
                try { var slot = output.TryFindOneValidInventorySlot(sample); blocked = slot == null || !slot.IsValid(); }
                finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(sample); }
                CbxRequire("full-output-native-admission-rejects." + ids[index], true, blocked, blocked);
                JArray before = new(output.childItems.OrderBy(item => item.uniqueId).Select(FlItem)); int energy = Energy(battery), cash = Store.playerCash;
                CbxPress(machine, buttons[index], "full-" + ids[index]);
                JArray after = new(output.childItems.OrderBy(item => item.uniqueId).Select(FlItem));
                R.Check("containerproduction.full-output-preserves-stock-energy-cash." + ids[index], new { before, energy, cash },
                    new { after, energy = Energy(battery), cash = Store.playerCash }, JToken.DeepEquals(before, after) && Energy(battery) == energy && Store.playerCash == cash,
                    "Physically full native output, independently rejected by native slot admission before the actual enabled button. Native prototype UID allocation is not described as inventory output.");
                yield return null;
            }
        }
        finally { foreach (GameItem item in blockers) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); }
        yield return null;
        CbxRequire("full-output-own-blockers-cleaned." + machine.identifier, 0, output.childItems.Count, output.childItems.Count == 0);
    }
    private JArray CbxSnapshot(IEnumerable<int> required)
    {
        var all = Store.FindAllItem(false).Where(item => item != null).ToArray(); var snapshot = new JArray();
        foreach (int uid in required.OrderBy(uid => uid))
        {
            GameItem[] matches = all.Where(item => item.uniqueId == uid).ToArray();
            bool known = _cbxIdentity.TryGetValue(uid, out var expected);
            bool valid = matches.Length == 1 && matches[0].unitCount > 0 && GeneralHelper.IsItemOwned(matches[0]) &&
                (!known || matches[0].identifier == expected.Id && matches[0].unitCount == expected.Count);
            CbxRequire("promised-uid-unique-owned." + uid, "Exactly one positive-count owned same UID", new { uid, matches = matches.Length, valid }, valid);
            snapshot.Add(FlItem(matches[0]));
        }
        return snapshot;
    }
    internal IEnumerable<object?> NativeContainerProductionReload(JObject baseline)
    {
        R.phase = "containerproduction-independent-process-readback";
        var prior = baseline["persistence"]?["nativeContainerProduction"] as JObject ?? throw new InvalidOperationException("Native container production baseline missing");
        CbxRequire("reload-schema", 1, prior["schema"], (int?)prior["schema"] == 1);
        bool includeBox = O.Family == "containerproduction";
        CbxRequire("reload-exact-family-denominator", includeBox, prior["includeBox"], (bool?)prior["includeBox"] == includeBox);
        int[] required = ((JArray)prior["uids"]!).Values<int>().ToArray();
        int expectedUidCount = includeBox ? 11 : 7;
        CbxRequire("reload-uid-set-complete", new { expectedUidCount, positiveUnique = true }, required,
            required.Length == expectedUidCount && required.All(uid => uid > 0) && required.Distinct().Count() == expectedUidCount);
        var recorded = (JArray)prior["items"]!;
        int[] rowUids = recorded.Select(item => (int)item["uid"]!).ToArray();
        CbxRequire("reload-exact-item-row-uid-set", required, rowUids,
            recorded.Count == expectedUidCount && rowUids.Distinct().Count() == expectedUidCount && required.OrderBy(uid => uid).SequenceEqual(rowUids.OrderBy(uid => uid)));
        foreach (var item in recorded) _cbxIdentity.Add((int)item["uid"]!, ((string)item["id"]!, (int)item["count"]!));
        JArray observed = CbxSnapshot(required);
        R.Check("containerproduction.reload-native-uids-geometry-tags-values-and-energy", prior["items"], observed,
            JToken.DeepEquals(FlReloadProjection(prior["items"]!), FlReloadProjection(observed)),
            "Readback projection permits only exact native SaveItemNode disabled-all-default tag omission; no ambient UID allowlist is supplied, so temperatures and every nondefault payload remain exact. Printed bottles were actually empty, never filled or temperature-edited.");
        var recipes = (JArray)prior["recipes"]!;
        string[] expectedIds = CbxBottleRecipes.Select(recipe => recipe.Id).Concat(includeBox ? new[] { "ammo_box" } : Array.Empty<string>()).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        CbxRequire("reload-produced-recipe-denominator", expectedIds, recipes.Select(row => (string?)row["id"]).ToArray(),
            recipes.Count == expectedIds.Length && recipes.All(row => (string?)row["status"] == "passed") && expectedIds.SequenceEqual(recipes.Select(row => (string)row["id"]!).OrderBy(id => id, StringComparer.Ordinal)));
        foreach (var recipe in recipes)
        {
            int uid = (int)recipe["uid"]!;
            var item = Store.FindAllItem(false).SingleOrDefault(item => item.uniqueId == uid);
            CbxRequire("reload-produced-original." + recipe["id"], new { uid, id = (string?)recipe["id"] }, item == null ? null : FlItem(item), item != null && item.identifier == (string?)recipe["id"] && item.unitCount == 1 && GeneralHelper.IsItemOwned(item));
        }
        R.persistence["nativeContainerProductionReload"] = new JObject { ["items"] = observed, ["recipes"] = recipes.DeepClone() };
        yield return null;
    }
}
