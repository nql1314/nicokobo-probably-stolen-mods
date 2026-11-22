using System.Collections;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private sealed class VcRecipe(string id, string active, int volume, int retainedActive, int impurity)
    {
        internal readonly string Id = id, Active = active;
        internal readonly int Volume = volume, RetainedActive = retainedActive, Impurity = impurity;
        internal JObject Definition() => new() { ["id"] = Id, ["active"] = Active, ["volumeParts"] = Volume,
            ["requireDry"] = true, ["outputCount"] = 1, ["unmodifiedLossPercent"] = 12,
            ["outputActiveParts"] = RetainedActive, ["outputPhysicalContaminantParts"] = Impurity,
            ["outputPurityPercent"] = 88, ["outputGrade"] = "near_pharma", ["powerDebit"] = 0, ["cashDebit"] = 0 };
    }
    // Fixed acceptance invoices, not values obtained from CanOutput/GetCount or
    // a product factory. These seven UI recipes are outside Forge's 70 recipes.
    private static readonly VcRecipe[] VcRecipes =
    {
        new("dream_dust", "dream_cap_extract", 2000, 1760, 240),
        new("nightmare_dust", "nightmare_extract", 4000, 3520, 480),
        new("caffeine_pill", "caffeine", 5000, 4400, 600),
        new("zyanide_pill", "zyanide", 600, 528, 72),
        new("birth_control_pill", "contraceptive", 100, 88, 12),
        new("fanta_pill", "fanta", 500, 440, 60),
        new("pink_alt_injector", "nomore", 4000, 3520, 480)
    };
    private static readonly string[] VcSpecies =
    {
        "water", "heavy_metal", "organic_waste", "microplastic", "microbe", "chemical_contaminant", "physical_contaminant", "mineral",
        "dream_cap_mush", "dream_cap_extract", "plant_matter", "alcohol", "caffeine", "protein", "fat", "starch", "sugar", "coffee_mush",
        "contraceptive", "zyanide", "nightmare_extract", "nightmare_cap_mush", "chlorine", "synaprest", "fanta", "methylol", "reducing_agent",
        "acid", "base", "stabilizing_agent", "debris", "lipobond_agent", "livestock_steroid", "livestock_immunity", "wine_extract", "nomore"
    };
    private readonly JArray _vcCases = new(), _vcActions = new(), _vcWitnesses = new(), _vcDisposals = new(), _vcCapacity = new();
    private readonly HashSet<int> _vcPromised = new();
    private sealed class VcStopped(string reason) : Exception(reason) { }
    private void VcRequire(string name, object? expected, object? observed, bool pass, string evidence = "")
    {
        R.Check("vanillachemistry." + name, expected, observed, pass, evidence);
        if (!pass) throw new VcStopped(name);
    }
    private sealed class VcFixture : IDisposable
    {
        internal GameItem Machine = null!, Carrier = null!, Battery = null!;
        internal GameGridInventory Modules = null!, Output = null!, Collected = null!;
        internal GameSlotInventory Input = null!;
        internal MachineryButton Configure = null!, Produce = null!;
        internal MachineryText Target = null!, Status = null!;
        internal GameItem? LastProduct;
        internal bool Keep;
        public void Dispose()
        {
            if (ChemFinisherUIManager.Instance != null && ReferenceEquals(ChemFinisherUIManager.Instance.currentItem, Machine))
                ChemFinisherUIManager.Instance.HideUI();
            Machine?.contentWindow?.Hide(); Carrier?.contentWindow?.Hide();
            if (!Keep) { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(Machine); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(Carrier); }
        }
    }

    internal IEnumerable<object?> VanillaChemistry()
    {
        VcDescribe("vanillachemistry", false); VcDenominator();
        using var f = VcCreateFixture(); using var observer = new VcObservation(this, f.Machine);
        foreach (GameItem root in new[] { f.Machine, f.Carrier, f.Battery }) _vcPromised.Add(root.uniqueId);
        foreach (VcRecipe recipe in VcRecipes)
        {
            GameItem? source = null;
            try
            {
                foreach (object? wait in VcCase("setup." + recipe.Id, () => VcAction(() => source = VcSource(f, recipe)))) yield return wait;
                if (source == null)
                {
                    R.Check("vanillachemistry.recipe-missing-fixture." + recipe.Id, "All three refusals and successful production", null, false);
                    continue;
                }
                foreach (object? wait in VcCase("select-ui." + recipe.Id, () => VcSelectRecipe(f, recipe))) yield return wait;
                foreach (string variant in new[] { "empty", "one-part-short", "wet-water-101" })
                    foreach (object? wait in VcCase("reject." + recipe.Id + "." + variant,
                        () => VcRefusal(f, observer, source, recipe, variant))) yield return wait;
                foreach (object? wait in VcCase("produce-and-collect." + recipe.Id,
                    () => VcProduce(f, observer, source, recipe, true))) yield return wait;
            }
            finally
            {
                foreach (GameItem created in observer.Created.ToArray())
                    if (!_vcPromised.Contains(created.uniqueId)) VcDispose(created, "Unretained attempted native product after recorded evidence; not rollback");
                if (source != null && !_vcPromised.Contains(source.uniqueId)) VcDispose(source, "Unretained failed recipe fixture; not a transaction rollback");
                VcDisposeOutput(f, "Before next independent recipe; every unexpected product was recorded before cleanup");
            }
        }
        foreach (object? wait in VcWaitNativeStatus(f, observer, "before-save")) yield return wait;
        VcValidateWitnesses(_vcWitnesses, f.Collected, "before-save");
        VcRequire("saved-machine-configuration", "Last recipe selected; zero installed modules; empty original input/output; no power slot",
            VcMachine(f), VcSelected(f) == VcRecipes.Last().Id && f.Modules.childItems.Count == 0 && f.Input.childItem == null &&
                f.Output.childItems.Count == 0 && MachineHelper.GetBatterySlot(f.Machine) == null);
        VcRequire("passive-observer", "Native callbacks observed without observer errors; Steam remains uninitialized", observer.Evidence,
            observer.Errors.Count == 0 && !SteamManager.Initialized);
        var promised = _vcPromised.OrderBy(uid => uid).ToArray();
        VcRequire("promised-seventeen-uid-denominator", 17, promised, promised.Length == 17);
        JArray snapshots = VcPromisedSnapshot(promised, "save");
        Retain(f.Machine); Retain(f.Carrier); f.Keep = true;
        R.persistence["vanillaChemistry"] = new JObject { ["schema"] = 1, ["steamBuild"] = 26317870,
            ["gameRunId"] = Store.runID, ["slotId"] = Store.saveSlotId, ["cash"] = Store.playerCash,
            ["definitions"] = new JArray(VcRecipes.Select(spec => spec.Definition())), ["species"] = new JArray(VcSpecies),
            ["machineUid"] = f.Machine.uniqueId, ["carrierUid"] = f.Carrier.uniqueId, ["batteryUid"] = f.Battery.uniqueId,
            ["machine"] = VcMachine(f), ["promisedUids"] = new JArray(promised), ["snapshots"] = snapshots,
            ["witnesses"] = _vcWitnesses.DeepClone(), ["energy"] = VcEnergy(), ["grid"] = GridTotal,
            ["provenance"] = "Native factory-granted machine/cylinders and WaterHelper reagent grants; enabled native UI actions; no economic acquisition, progress assignment or synthetic Finish. Outer SaveGame/new-process LoadGame owns actual ES3." };
    }

    internal IEnumerable<object?> VanillaChemistryReload(JObject baseline)
    {
        VcDescribe("vanillachemistry-reload", false); VcDenominator();
        var saved = baseline["persistence"]?["vanillaChemistry"] as JObject ?? throw new InvalidOperationException("Seven-recipe ES3 baseline missing");
        VcRequire("reload-bound-baseline", "Passed same-family save, same run/slot/build, distinct process/run, original slot",
            new { baselineMode = (string?)baseline["mode"], saved },
            (string?)baseline["mode"] == "vanillachemistry-save" && (bool?)baseline["passed"] == true && (int?)saved["schema"] == 1 &&
            (int?)saved["steamBuild"] == 26317870 && (string?)saved["gameRunId"] == Store.runID && (int?)saved["slotId"] == Store.saveSlotId &&
            (string?)baseline["runId"] != O.RunId && (int?)baseline["processId"] != R.processId && (string?)baseline["processToken"] != R.processToken && !O.UseCopy,
            "Common Reload must also bind candidate SHA, actual ES3 hash/path and normal native LoadGame; this method does not accept failed capacity diagnostics as a save baseline.");
        VcRequire("reload-independent-definitions", new JArray(VcRecipes.Select(spec => spec.Definition())), saved["definitions"],
            JToken.DeepEquals(new JArray(VcRecipes.Select(spec => spec.Definition())), saved["definitions"]));
        int[] promised = (saved["promisedUids"] as JArray)?.Values<int>().ToArray() ?? Array.Empty<int>();
        var recorded = saved["snapshots"] as JArray ?? throw new InvalidOperationException("Promised UID snapshots missing");
        VcRequire("reload-full-uid-denominator", "Exactly seventeen positive unique UIDs and one snapshot per promise", new { promised, recorded },
            promised.Length == 17 && promised.All(uid => uid > 0) && promised.Distinct().Count() == 17 && recorded.Count == 17 &&
            promised.OrderBy(uid => uid).SequenceEqual(recorded.Select(item => (int)item["uid"]!).OrderBy(uid => uid)));
        var witnesses = saved["witnesses"] as JArray ?? throw new InvalidOperationException("Seven fixed product/source witnesses missing");
        GameItem machine = VcFind((int)saved["machineUid"]!), carrier = VcFind((int)saved["carrierUid"]!);
        var f = VcBind(machine, carrier, VcFind((int)saved["batteryUid"]!)); f.Keep = true;
        using (f)
        using (var observer = new VcObservation(this, f.Machine))
        {
            VcValidateWitnesses(witnesses, f.Collected, "reload-immediate");
            JArray immediate = VcPromisedSnapshot(promised, "reload-immediate");
            VcRequire("reload-physical-state", recorded, immediate, JToken.DeepEquals(recorded, immediate));
            foreach (object? wait in VcWaitNativeStatus(f, observer, "reload-after-native-callback")) yield return wait;
            VcValidateWitnesses(witnesses, f.Collected, "reload-after-native-callback");
            VcRequire("reload-machine-selection-and-ports", saved["machine"], VcMachine(f), JToken.DeepEquals(saved["machine"], VcMachine(f)));
            VcRequire("reload-cash-and-energy", new { cash = (int)saved["cash"]!, energy = saved["energy"], grid = (long)saved["grid"]! },
                new { cash = Store.playerCash, energy = VcEnergy(), grid = GridTotal }, Store.playerCash == (int)saved["cash"]! &&
                JToken.DeepEquals(saved["energy"], VcEnergy()) && GridTotal == (long)saved["grid"]!);
            string file = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId)), hash = Reflect.HashFile(file);
            // Reopen the real restored configuration UI. Selection is the native
            // player action; no extra product or material is granted on reload.
            foreach (object? wait in VcCase("reload-native-ui-restored-selection", () => VcSelectRecipe(f, VcRecipes.Last()))) yield return wait;
            JArray final = VcPromisedSnapshot(promised, "reload-final");
            VcRequire("reload-no-save-or-new-products", new { hash, snapshots = recorded },
                new { hash = Reflect.HashFile(file), snapshots = final }, hash == Reflect.HashFile(file) && JToken.DeepEquals(recorded, final));
        }
        R.persistence["vanillaChemistry"] = saved.DeepClone();
    }

    // Separate required family keeps a genuine capacity defect red without
    // teaching the ordinary reload gate to accept a failed production baseline.
    internal IEnumerable<object?> VanillaChemistryCapacity()
    {
        VcDescribe("vanillachemistrycapacity", true); VcDenominator();
        foreach (VcRecipe recipe in VcRecipes)
            foreach (object? wait in VcCase("full-output." + recipe.Id, () => VcCapacityCase(recipe))) yield return wait;
        VcRequire("capacity-seven-attempt-denominator", VcRecipes.Select(recipe => recipe.Id).ToArray(), _vcCapacity,
            _vcCapacity.Count == 7 && _vcCapacity.Select(row => (string)row["id"]!).OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(VcRecipes.Select(recipe => recipe.Id).OrderBy(id => id, StringComparer.Ordinal)));
        R.persistence["vanillaChemistryCapacity"] = new JObject { ["schema"] = 1, ["steamBuild"] = 26317870,
            ["required"] = true, ["recipes"] = new JArray(VcRecipes.Select(recipe => recipe.Definition())), ["attempts"] = _vcCapacity.DeepClone(),
            ["reloadBaseline"] = false, ["provenance"] = "Separate required negative family; red native capacity evidence is never downgraded or used as a passing persistence baseline" };
    }

    private void VcDescribe(string family, bool capacity)
    {
        R.phase = family; R.environment["vanillaChemistryCases"] = _vcCases; R.environment["vanillaChemistryActions"] = _vcActions;
        R.environment["vanillaChemistryDisposals"] = _vcDisposals; R.environment["vanillaChemistryCapacityAttempts"] = _vcCapacity;
        R.environment["vanillaChemistryScope"] = new { build = 26317870, family, recipes = 7, liquidComponents = 36, capacity,
            origin = "ChemFinisherRecipe.chemFinisherRecipes and native ChemFinisherUIManager rows; distinct from the 70 registered Forge recipes",
            entry = "Native visible/enabled MachineryButton.Select and actual instantiated recipe Unity Button; programmatic native UI events, not a physical mouse/raycast test",
            power = "Native finisher has module/container/output/control panels, no power port and no timed job. Expected synchronous output debit is zero; a real 100-energy control cell and all live source energies are checked",
            water = "Dry input is the contract: native WaterHelper.AddLiquid reagent fixture; adding 101 water parts must reject, not consume process water",
            provenance = "Factories/material grants are test fixtures, not economic acquisition. No direct output, purity, ownership, progress, completion, temperature, reaction flag, clock or date assignment",
            limits = "Unmodified finisher (zero chemical modules, 12% replacement impurity). All upgrade loss tiers, solvent synthesis, natural reagent acquisition and full EndNight are outside this family. Steam account writes are never invoked by the probe." };
    }
    private void VcDenominator()
    {
        var live = ChemFinisherRecipe.chemFinisherRecipes ?? throw new InvalidOperationException("Native finisher recipe collection is null");
        JArray actual = new(live.Select(recipe => new JObject { ["id"] = recipe.identifier, ["active"] = recipe.activeIngredientId,
            ["volumeParts"] = recipe.requiredVolume, ["requireDry"] = recipe.isRequireDry }));
        var expected = new JArray(VcRecipes.Select(recipe => new JObject { ["id"] = recipe.Id, ["active"] = recipe.Active,
            ["volumeParts"] = recipe.Volume, ["requireDry"] = true }));
        VcRequire("runtime-seven-exact-definitions", expected, actual, JToken.DeepEquals(expected, actual) && live.Count == 7 &&
            live.All(recipe => recipe.GetType().Assembly == typeof(GameItem).Assembly) && live.Select(recipe => recipe.identifier).Distinct().Count() == 7);
        string[] liquids = Liquid.Liquids.Select(liquid => liquid.identifier).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        VcRequire("runtime-36-species", VcSpecies.OrderBy(id => id, StringComparer.Ordinal).ToArray(), liquids,
            liquids.Length == 36 && liquids.SequenceEqual(VcSpecies.OrderBy(id => id, StringComparer.Ordinal)));
        string[] directories = DirectoryMaster.GetDictionaryClassNameList<GameItem>().ToArray();
        R.environment["vanillaChemistryRuntimeDirectories"] = directories;
        R.environment["vanillaChemistryExcludedSourceExamples"] = new[] { "UnusedDirectory.CreateCraftingTable (six example recipes)",
            "UnusedDirectory.CreateTestVendingMachine (two example recipes)", "UnusedDirectory.AutomaticMeatProcessorExample (three examples)", "UnusedDirectory.CreateRecycler (one example)" };
        foreach (string id in VcRecipes.Select(recipe => recipe.Id).Concat(new[] { "chem_finisher", "25ml_cylinder", "1ml_dropper", "storage_bay_large" }))
        {
            var factory = DirectoryMaster.GetFactory<GameItem>(id);
            VcRequire("native-factory." + id, "Registered Assembly-CSharp factory", factory?.Method.DeclaringType?.FullName,
                factory != null && factory.Method.DeclaringType?.Assembly == typeof(GameItem).Assembly);
        }
        VcRequire("steam-remains-offline", false, SteamManager.Initialized, !SteamManager.Initialized);
    }
    private VcFixture VcCreateFixture()
    {
        GameItem? machine = null, carrier = null;
        try
        {
            machine = Spawn("chem_finisher"); carrier = Spawn("storage_bay_large"); VcPlaceTop(machine); VcPlaceTop(carrier);
            GameItem battery = PreBuiltItemHelper.EnergyCredit(100);
            var f = VcBind(machine, carrier, battery);
            VcPlace(f.Collected, battery, "control-battery");
            VcRequire("unmodified-native-machine." + machine.uniqueId, new { modules = 0, loss = 12, powerPort = false, cycleJob = false, energy = 100 },
                new { modules = f.Modules.childItems.Count, loss = MachineChemFinisher.GetLoss(machine), powerPort = MachineHelper.GetBatterySlot(machine) != null,
                    cycleJob = machine.onCycleEndSlotItemFunc != null, energy = Energy(battery) }, f.Modules.childItems.Count == 0 && MachineChemFinisher.GetLoss(machine) == 12 &&
                    MachineHelper.GetBatterySlot(machine) == null && machine.onCycleEndSlotItemFunc == null && Energy(battery) == 100);
            return f;
        }
        catch { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(machine); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(carrier); throw; }
    }
    private VcFixture VcBind(GameItem machine, GameItem carrier, GameItem battery)
    {
        var row = machine.contentWindow?.childElement as MachineryPanelRow ?? throw new InvalidOperationException("Native finisher panel row missing");
        var control = row.GetPanel("control")?.content as MachineryStack ?? throw new InvalidOperationException("Native finisher control stack missing");
        if (control.elements.Count != 4 || control.elements[0] is not MachineryButton configure || control.elements[1] is not MachineryText target ||
            control.elements[2] is not MachineryText status || control.elements[3] is not MachineryButton produce)
            throw new InvalidOperationException("Native finisher configure/target/status/output controls changed");
        var f = new VcFixture { Machine = machine, Carrier = carrier, Battery = battery,
            Modules = MachineHelper.GetBodyStorage(machine, 0) as GameGridInventory ?? throw new InvalidOperationException("Native module grid 0 missing"),
            Input = MachineHelper.GetBodyStorage(machine, 1) as GameSlotInventory ?? throw new InvalidOperationException("Native liquid slot 1 missing"),
            Output = MachineHelper.GetBodyStorage(machine, 2) as GameGridInventory ?? throw new InvalidOperationException("Native output grid 2 missing"),
            Collected = carrier.contentWindow?.childElement as GameGridInventory ?? throw new InvalidOperationException("Native collection carrier grid missing"),
            Configure = configure, Produce = produce, Target = target, Status = status };
        VcRequire("native-port-geometry." + machine.uniqueId, "Three native inventories; 6x4 output; 17x10 collection carrier", VcMachine(f),
            row.Panels.Count == 4 && GraphUtils.FindAllChildrenType<GameInventory>(machine).Count == 3 && f.Output.inventoryShape.width == 6 &&
            f.Output.inventoryShape.height == 4 && f.Collected.inventoryShape.width == 17 && f.Collected.inventoryShape.height == 10);
        return f;
    }
    private GameItem VcSource(VcFixture f, VcRecipe recipe)
    {
        VcRequire("empty-input-for-fixture." + recipe.Id, null, f.Input.childItem?.identifier, f.Input.childItem == null);
        GameItem source = Spawn("25ml_cylinder");
        try
        {
            VcPlace(f.Input, source, "native-reagent-cylinder." + recipe.Id);
            VcRequire("native-input-identity." + recipe.Id, "Owned count1 native 25ml cylinder in original liquid slot", VcItem(source),
                source.unitCount == 1 && GeneralHelper.IsItemOwned(source) && ReferenceEquals(source.parentInventory, f.Input) &&
                WaterHelper.GetFreeCapacity(source) + WaterHelper.GetTotalVolume(source) == 25000);
            return source;
        }
        catch { VcDispose(source, "Failed native source setup"); throw; }
    }
    private IEnumerable<object?> VcSelectRecipe(VcFixture f, VcRecipe recipe)
    {
        f.Machine.contentWindow.Show(); f.Machine.contentWindow.Validate(); yield return null;
        VcClick(f, f.Configure, "configure." + recipe.Id, false);
        yield return null; // Unity Destroy of previous rows must really finish.
        var ui = ChemFinisherUIManager.Instance ?? throw new InvalidOperationException("Actual native chemical UI singleton missing");
        ChemFinishElement[] rows = ui.LayoutGroup.GetComponentsInChildren<ChemFinishElement>(false);
        string[] actualIds = rows.Select(element => element.identifier).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        VcRequire("actual-ui-seven-rows." + recipe.Id, VcRecipes.Select(spec => spec.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(), actualIds,
            ui.UIPanel.activeInHierarchy && ReferenceEquals(ui.currentItem, f.Machine) && rows.Length == 7 &&
            actualIds.SequenceEqual(VcRecipes.Select(spec => spec.Id).OrderBy(id => id, StringComparer.Ordinal)));
        ChemFinishElement selected = rows.Single(element => element.identifier == recipe.Id);
        Button[] buttons = selected.GetComponentsInChildren<Button>(false).Where(button => Enumerable.Range(0, button.onClick.GetPersistentEventCount())
            .Any(index => ReferenceEquals(button.onClick.GetPersistentTarget(index), selected) && button.onClick.GetPersistentMethodName(index) == nameof(ChemFinishElement.OnButtonClick))).ToArray();
        VcRequire("native-recipe-button-wiring." + recipe.Id, "Exactly one active/interactable Unity Button wired to this native ChemFinishElement.OnButtonClick",
            new { buttons = buttons.Length, active = buttons.Select(button => button.IsActive()).ToArray(), interactable = buttons.Select(button => button.IsInteractable()).ToArray() },
            buttons.Length == 1 && buttons[0].gameObject.activeInHierarchy && buttons[0].IsActive() && buttons[0].IsInteractable());
        VcRequire("offline-before-recipe-ui." + recipe.Id, false, SteamManager.Initialized, !SteamManager.Initialized);
        buttons[0].onClick.Invoke(); // The real prefab event, after its actual guards above.
        VcRequire("native-ui-selected-target." + recipe.Id, recipe.Id, VcSelected(f), VcSelected(f) == recipe.Id,
            "ChemFinishElement.OnButtonClick -> ChemFinisherUIManager.OnChemRecipeSelected owns the selected tag. Probe never assigns it.");
        if (ui.UIPanel.activeSelf) ui.HideUI();
        yield return null;
    }
    private IEnumerable<object?> VcRefusal(VcFixture f, VcObservation observer, GameItem source, VcRecipe recipe, string variant)
    {
        int active = variant == "empty" ? 0 : variant == "one-part-short" ? recipe.Volume - 1 : recipe.Volume * 3;
        int water = variant == "wet-water-101" ? 101 : 0;
        VcPrepareSource(f, source, recipe, active, water, variant);
        foreach (object? wait in VcWaitNativeStatus(f, observer, recipe.Id + "." + variant)) yield return wait;
        VcRequire("refusal-prerequisite." + recipe.Id + "." + variant, new { canOutput = false, active, water, zeroExistingOutputs = true },
            new { canOutput = MachineChemFinisher.CanOutput(f.Machine, f.Input), actual = VcParts(source), outputs = f.Output.childItems.Count },
            VcSelected(f) == recipe.Id && !MachineChemFinisher.CanOutput(f.Machine, f.Input) && f.Output.childItems.Count == 0);
        JObject before = VcAudit(); VcAttempt(f, observer, recipe, "reject." + variant, true); JObject after = VcAudit();
        var liveCreated = observer.Created.Where(item => !VcDestroyed(item) && item.unitCount > 0).ToArray();
        R.Check("vanillachemistry.reject-atomic." + recipe.Id + "." + variant, before, after,
            JToken.DeepEquals(before, after) && liveCreated.Length == 0 && f.Output.childItems.Count == 0,
            "Real enabled output selection or observed disabled guard. Full component/UID/count/ownership/energy/cash inventory is compared in this same action; no fabricated recipe result.");
        VcDisposeAttempt(observer, "Unexpected refusal product after immutable evidence capture; cleanup is not rollback");
        VcDisposeOutput(f, "Unexpected refusal output after failure evidence");
    }
    private IEnumerable<object?> VcProduce(VcFixture f, VcObservation observer, GameItem source, VcRecipe recipe, bool promise)
    {
        f.LastProduct = null; VcPrepareSource(f, source, recipe, recipe.Volume * 3, 0, "success");
        foreach (object? wait in VcWaitNativeStatus(f, observer, recipe.Id + ".ready")) yield return wait;
        VcRequire("success-prerequisites." + recipe.Id, "Selected exact recipe, three dry batches, empty 6x4 output, zero modules", VcMachine(f),
            VcSelected(f) == recipe.Id && MachineChemFinisher.CanOutput(f.Machine, f.Input) && !WaterHelper.IsWet(source) &&
            f.Output.childItems.Count == 0 && f.Modules.childItems.Count == 0);
        JObject before = VcAudit(); int generator = Store.currentUniqueId;
        VcAttempt(f, observer, recipe, "produce", false);
        GameItem[] output = f.Output.childItems.ToArray();
        VcRequire("one-actual-native-output." + recipe.Id, new { count = 1, newUid = true, factoryCalls = 1, nativeAchievementCheckCalls = 1 },
            new { products = output.Select(VcItem).ToArray(), factory = observer.CreationEvidence(), checks = observer.Checks },
            output.Length == 1 && output[0].unitCount == 1 && output[0].uniqueId > generator && observer.Created.Count == 1 &&
            ReferenceEquals(observer.Created[0], output[0]) && observer.Checks.Count == 1,
            "Only the machine's native output callback creates this product; no output fixture is spawned by the probe.");
        GameItem product = output.Single(); VcAssertProduct(recipe, product, f.Output, "produced");
        VcAssertSource(recipe, source, recipe.Volume * 2, 0, f.Input, "after-production");
        JObject after = VcAudit();
        VcRequire("successful-complete-resource-ledger." + recipe.Id,
            new { inputDebit = recipe.Volume, outputCount = 1, outputParts = recipe.Volume, powerDebit = 0, cashDebit = 0,
                activeReplaced = recipe.Impurity, physicalContaminantAdded = recipe.Impurity }, new { before, after },
            (int)before["cash"]! == (int)after["cash"]! && JToken.DeepEquals(before["energy"], after["energy"]) &&
            (long)before["grid"]! == (long)after["grid"]! && VcWorldUnitCount(recipe.Id, after) - VcWorldUnitCount(recipe.Id, before) == 1 &&
            VcUnrelated(before, after, source.uniqueId, product.uniqueId),
            "Native 12% impurity replaces active ingredient rather than destroying batch volume. Exact output/input 36-component invoices are asserted separately; all unrelated physical objects remain identical.");
        VcCollect(f, product, recipe.Id); f.LastProduct = product;
        if (promise)
        {
            VcPlace(f.Collected, source, "retain-native-source." + recipe.Id);
            _vcPromised.Add(source.uniqueId); _vcPromised.Add(product.uniqueId);
            _vcWitnesses.Add(new JObject { ["id"] = recipe.Id, ["sourceUid"] = source.uniqueId, ["productUid"] = product.uniqueId,
                ["expectedSourceParts"] = VcFixedParts(recipe.Active, recipe.Volume * 2), ["expectedProductParts"] = VcProductParts(recipe),
                ["expectedProductCount"] = 1, ["expectedPurity"] = 88, ["expectedGrade"] = "near_pharma" });
        }
    }
    private IEnumerable<object?> VcCapacityCase(VcRecipe recipe)
    {
        using var f = VcCreateFixture(); using var observer = new VcObservation(this, f.Machine);
        GameItem source = VcSource(f, recipe);
        foreach (object? wait in VcSelectRecipe(f, recipe)) yield return wait;
        foreach (object? wait in VcProduce(f, observer, source, recipe, false)) yield return wait;
        GameItem seed = f.LastProduct ?? throw new InvalidOperationException("No genuinely produced seed for full-output geometry");
        var fillers = new List<GameItem>();
        try
        {
            while (f.Output.HasFreeCell() && fillers.Count < 24)
            {
                var filler = Spawn("1ml_dropper");
                try { VcPlace(f.Output, filler, "capacity-filler." + recipe.Id + "." + fillers.Count); fillers.Add(filler); }
                catch { VcDispose(filler, "Unaccepted geometric filler"); throw; }
            }
            SlotMarker? marker = f.Output.TryFindOneValidInventorySlot(seed);
            JObject geometry = VcGeometry(f.Output);
            VcRequire("genuine-full-output." + recipe.Id, "6x4 occupied cells, twelve owned count1 native 1x2 droppers; actual produced product has no valid slot", geometry,
                fillers.Count == 12 && !f.Output.HasFreeCell() && geometry["cells"]!.All(cell => ((JArray)cell["uids"]!).Count == 1) &&
                fillers.All(item => item.identifier == "1ml_dropper" && item.unitCount == 1 && GeneralHelper.IsItemOwned(item) && ReferenceEquals(item.parentInventory, f.Output)) &&
                (marker == null || !marker.IsValid() || marker.numTransfer <= 0));
            VcPrepareSource(f, source, recipe, recipe.Volume * 3, 0, "capacity-ready");
            foreach (object? wait in VcWaitNativeStatus(f, observer, recipe.Id + ".capacity-full")) yield return wait;
            VcRequire("capacity-real-materials." + recipe.Id, "Selected dry three-batch reagent with original guard/geometry", VcMachine(f),
                VcSelected(f) == recipe.Id && !WaterHelper.IsWet(source) && WaterHelper.GetTotalVolume(source) == recipe.Volume * 3 && f.Modules.childItems.Count == 0);
            JObject before = VcAudit(); bool canOutputBefore = MachineChemFinisher.CanOutput(f.Machine, f.Input);
            VcAttempt(f, observer, recipe, "full-output", true); JObject after = VcAudit();
            JArray created = observer.CreationEvidence();
            var unattached = observer.Created.Where(item => !VcDestroyed(item) && item.unitCount > 0 && item.parentInventory == null).ToArray();
            bool conserved = JToken.DeepEquals(before, after) && unattached.Length == 0;
            var row = new JObject { ["id"] = recipe.Id, ["required"] = true, ["canOutputBefore"] = canOutputBefore,
                ["guardAndAction"] = _vcActions.Last!.DeepClone(), ["geometryBefore"] = geometry, ["before"] = before, ["after"] = after,
                ["createdProductsBeforeCleanup"] = created, ["detachedOwnedProducts"] = new JArray(unattached.Select(VcItem)),
                ["classification"] = conserved ? "native-refusal-conserved" : "native-capacity-attempt-changed-resources-or-left-detached-output",
                ["passed"] = conserved };
            _vcCapacity.Add(row);
            R.Check("vanillachemistry.capacity-required-refusal." + recipe.Id, "Full output must refuse without any reagent, existing output, energy, cash or ownership/UID/count change", row,
                conserved, "Visible real UI guards were read before selecting. A disabled output is observed without invoking it. Otherwise the native player's Select entry executes unchanged. Factory/achievement hooks are read-only and preserve escaped/detached product evidence before cleanup.");
            VcDisposeAttempt(observer, "Post-evidence cleanup of actual capacity-attempt outputs; never compensates ingredients or reclassifies failure");
        }
        finally { foreach (GameItem filler in fillers) VcDispose(filler, "Actual full-output filler cleanup after recorded result"); }
    }

    private void VcPrepareSource(VcFixture f, GameItem source, VcRecipe recipe, int active, int water, string stage)
    {
        WaterHelper.EmptyContainer(source);
        if (active > 0) WaterHelper.AddLiquid(source, recipe.Active, active);
        if (water > 0) WaterHelper.AddLiquid(source, "water", water);
        VcAssertSource(recipe, source, active, water, f.Input, "fixture." + stage);
    }
    private IEnumerable<object?> VcWaitNativeStatus(VcFixture f, VcObservation observer, string stage)
    {
        long before = observer.NativeStatusCallbacks; int firstFrame = Time.frameCount; float until = Time.realtimeSinceStartup + 7f;
        while (observer.NativeStatusCallbacks == before)
        {
            if (Time.realtimeSinceStartup >= until)
                VcRequire("native-status-timeout." + stage, "One ordinary native machine one-second callback returns normally", observer.Evidence, false);
            yield return null;
        }
        yield return null;
        VcRequire("native-status-observed." + stage, "A normal native finisher status callback ran through ordinary EmporiumEntry.Update",
            new { before, after = observer.NativeStatusCallbacks, firstFrame, frame = Time.frameCount, target = f.Target.GetText(), status = f.Status.GetText() },
            observer.NativeStatusCallbacks > before && Time.frameCount > firstFrame && observer.Errors.Count == 0,
            "Read-only postfix of the machine factory's exact native callback. No direct native cycle, reaction, timer, Finish, BeforeNight or EndNight call; this observes status refresh, not a timed manufacturing job.");
    }
    private bool VcClick(VcFixture f, MachineryButton button, string stage, bool allowDisabled)
    {
        var guard = new JObject { ["stage"] = stage, ["windowVisible"] = f.Machine.contentWindow.IsVisible(),
            ["active"] = button.handler.gameObject.activeInHierarchy, ["hidden"] = button.isHidden, ["disabled"] = button.isDisabled,
            ["requiresHold"] = button.requiresHold, ["selectedBefore"] = button.isSelected, ["frame"] = Time.frameCount,
            ["nativeEntry"] = "SelectableElement.Select -> MachineryButton.OnSelect -> unchanged factory callback" };
        _vcActions.Add(guard);
        VcRequire("ui-visible-and-offline." + stage, "Visible active native control, no hold/selection, Steam disabled", guard,
            f.Machine.contentWindow.IsVisible() && button.handler.gameObject.activeInHierarchy && !button.isHidden && !button.requiresHold && !button.isSelected && !SteamManager.Initialized);
        if (button.isDisabled)
        {
            guard["invoked"] = false; guard["reason"] = "Native disabled guard respected";
            VcRequire("ui-disabled-only-for-refusal." + stage, allowDisabled, true, allowDisabled);
            return false;
        }
        bool selected = button.Select(); guard["invoked"] = selected;
        VcRequire("real-ui-selection." + stage, true, selected, selected);
        return true;
    }
    private void VcAttempt(VcFixture f, VcObservation observer, VcRecipe recipe, string stage, bool allowDisabled)
    {
        observer.Begin(recipe.Id + "." + stage, recipe.Id);
        try { VcClick(f, f.Produce, recipe.Id + "." + stage, allowDisabled); }
        finally { observer.End(); }
        _vcActions.Last!["factoryProducts"] = observer.CreationEvidence(); _vcActions.Last!["nativeAchievementCalls"] = observer.Checks.DeepClone();
        VcRequire("observer-and-steam-guards." + recipe.Id + "." + stage, "No observer errors and Steam remains uninitialized", observer.Evidence,
            observer.Errors.Count == 0 && !SteamManager.Initialized);
    }
    private static string VcSelected(VcFixture f) => f.Machine.GetTagReadonly(MachineChemFinisher.SELECTED_CHEM_ID_TAG).GetString();
    private static bool VcDestroyed(GameItem item) => item is GameItemElement element && element.IsDestroyed();
    private static JObject VcFixedParts(string active, int amount, int water = 0, int dirt = 0) => new(VcSpecies.OrderBy(id => id, StringComparer.Ordinal)
        .Select(id => new JProperty(id, id == active ? amount : id == "water" ? water : id == "physical_contaminant" ? dirt : 0)));
    private static JObject VcProductParts(VcRecipe recipe) => VcFixedParts(recipe.Active, recipe.RetainedActive, 0, recipe.Impurity);
    private static JObject VcParts(GameItem item) => new(VcSpecies.OrderBy(id => id, StringComparer.Ordinal)
        .Select(id => new JProperty(id, item.GetTagReadonly(Liquid.GetCurrentPartFromId(id)).GetInt())));
    private JObject VcItem(GameItem item) => new() { ["uid"] = item.uniqueId, ["id"] = item.identifier, ["count"] = item.unitCount,
        ["owned"] = GeneralHelper.IsItemOwned(item), ["destroyed"] = VcDestroyed(item), ["parentUid"] = item.parentInventory?.GetParentItem()?.uniqueId ?? 0,
        ["parentType"] = item.parentInventory?.GetType().Name ?? "", ["parentStorage"] = item.parentInventory?.identifier ?? "",
        ["top"] = item.parentInventory == null ? "detached" : ReferenceEquals(item.parentInventory, EmporiumEntry.Instance.invElement) ? "main" :
            ReferenceEquals(item.parentInventory, EmporiumEntry.Instance.backInvinvElement) ? "back" : ReferenceEquals(item.parentInventory, EmporiumEntry.Instance.showcaseElement) ? "showcase" : "nested",
        ["width"] = item.modifiedShape.width, ["height"] = item.modifiedShape.height,
        ["energy"] = Energy(item), ["capacity"] = item.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt(),
        ["parts"] = VcParts(item), ["activeIngredient"] = item.GetTagReadonly(ChemicalProductHelper.ACTIVATE_INGREDIENT_TAG).GetString(),
        ["purity"] = item.GetTagReadonly(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG).GetDouble(),
        ["grade"] = ChemicalProductHelper.GetCurrentChemicalConditionId(item),
        ["selectedRecipe"] = item.GetTagReadonly(MachineChemFinisher.SELECTED_CHEM_ID_TAG).GetString() };
    private JObject VcMachine(VcFixture f) => new() { ["uid"] = f.Machine.uniqueId, ["selected"] = VcSelected(f),
        ["modules"] = new JArray(f.Modules.childItems.Select(item => item.uniqueId)), ["input"] = f.Input.childItem?.uniqueId,
        ["output"] = new JArray(f.Output.childItems.Select(item => item.uniqueId)), ["outputWidth"] = f.Output.inventoryShape.width,
        ["outputHeight"] = f.Output.inventoryShape.height, ["batterySlot"] = MachineHelper.GetBatterySlot(f.Machine) != null };
    private JArray VcEnergy() => new(Store.FindAllItem(false).Distinct().Where(item => item.IsTag("power_source_item"))
        .OrderBy(item => item.uniqueId).Select(item => new JObject { ["uid"] = item.uniqueId, ["id"] = item.identifier, ["count"] = item.unitCount,
            ["owned"] = GeneralHelper.IsItemOwned(item), ["energy"] = Energy(item) }));
    private JObject VcAudit() => new() { ["cash"] = Store.playerCash, ["grid"] = GridTotal, ["energy"] = VcEnergy(),
        ["items"] = new JArray(Store.FindAllItem(false).Distinct().OrderBy(item => item.uniqueId).Select(VcItem)) };
    private static int VcWorldUnitCount(string id, JObject state) => state["items"]!.Where(item => (string?)item["id"] == id).Sum(item => (int)item["count"]!);
    private static bool VcUnrelated(JObject before, JObject after, params int[] allowed) => JToken.DeepEquals(
        new JArray(before["items"]!.Where(item => !allowed.Contains((int)item["uid"]!))),
        new JArray(after["items"]!.Where(item => !allowed.Contains((int)item["uid"]!))));
    private void VcAssertSource(VcRecipe recipe, GameItem source, int active, int water, GameInventory parent, string stage)
    {
        JObject expected = VcFixedParts(recipe.Active, active, water);
        double temperature = source.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble();
        VcRequire("source-fixed-invoice." + stage + "." + recipe.Id, new { parts = expected, capacity = 25000, count = 1, owned = true },
            new { state = VcItem(source), temperature }, source.identifier == "25ml_cylinder" && source.unitCount == 1 && source.uniqueId > 0 &&
            GeneralHelper.IsItemOwned(source) && ReferenceEquals(source.parentInventory, parent) && JToken.DeepEquals(expected, VcParts(source)) &&
            WaterHelper.GetTotalVolume(source) == active + water && WaterHelper.GetFreeCapacity(source) + WaterHelper.GetTotalVolume(source) == 25000 &&
            temperature >= 24 && temperature < 40,
            "All 36 components and native capacity are checked. Ambient temperature may refresh naturally; it is observed, never assigned or used as a durable equality baseline.");
    }
    private void VcAssertProduct(VcRecipe recipe, GameItem product, GameInventory parent, string stage)
    {
        ItemFeature? feature = ChemicalProductHelper.GetChemicalFeature(product);
        VcRequire("product-fixed-invoice." + stage + "." + recipe.Id, recipe.Definition(), VcItem(product),
            product.identifier == recipe.Id && product.uniqueId > 0 && product.unitCount == 1 && GeneralHelper.IsItemOwned(product) &&
            ReferenceEquals(product.parentInventory, parent) && product.IsTag(ChemicalHelper.CHEM_ITEM_TAG) && product.IsTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG) &&
            product.GetTagReadonly(ChemicalProductHelper.ACTIVATE_INGREDIENT_TAG).GetString() == recipe.Active &&
            JToken.DeepEquals(VcProductParts(recipe), VcParts(product)) && ChemicalHelper.GetTotalVolume(product) == recipe.Volume &&
            Math.Abs(ChemicalProductHelper.GetPurity(product) - 88d) <= 1e-9 && ChemicalProductHelper.GetCurrentChemicalConditionId(product) == "near_pharma" &&
            feature != null && feature.parentItemUniqueId == product.uniqueId,
            "Independent explicit retained-active/physical-contaminant constants, count1 and near_pharma at 88%; only a 1e-9 double-representation allowance, no resource tolerance.");
    }
    private void VcCollect(VcFixture f, GameItem product, string stage)
    {
        JObject before = VcAudit(); int uid = product.uniqueId;
        VcPlace(f.Collected, product, "collect-real-product." + stage);
        JObject after = VcAudit();
        VcRequire("collection-identity-and-invoice." + stage, new { uid, count = 1, owned = true, outputEmpty = true, unchangedResources = true },
            new { product = VcItem(product), before, after }, product.uniqueId == uid && product.unitCount == 1 && GeneralHelper.IsItemOwned(product) &&
            ReferenceEquals(product.parentInventory, f.Collected) && f.Output.childItems.Count == 0 && VcUnrelated(before, after, uid) &&
            (int)before["cash"]! == (int)after["cash"]! && JToken.DeepEquals(before["energy"], after["energy"]) && (long)before["grid"]! == (long)after["grid"]!,
            "Actual native SlotMarker.TryAcceptOnce(1) moves the genuinely produced item. No direct parent/tree assignment, replacement factory or receipt fabrication.");
    }
    private void VcValidateWitnesses(JArray rows, GameInventory parent, string stage)
    {
        VcRequire("seven-witness-denominator." + stage, VcRecipes.Select(recipe => recipe.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(), rows,
            rows.Count == 7 && rows.Select(row => (string)row["id"]!).OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(VcRecipes.Select(recipe => recipe.Id).OrderBy(id => id, StringComparer.Ordinal)) &&
            rows.SelectMany(row => new[] { (int)row["sourceUid"]!, (int)row["productUid"]! }).Distinct().Count() == 14);
        foreach (VcRecipe recipe in VcRecipes)
        {
            JToken row = rows.Single(row => (string?)row["id"] == recipe.Id);
            VcRequire("canonical-saved-invoice." + stage + "." + recipe.Id, recipe.Definition(), row,
                JToken.DeepEquals(row["expectedSourceParts"], VcFixedParts(recipe.Active, recipe.Volume * 2)) &&
                JToken.DeepEquals(row["expectedProductParts"], VcProductParts(recipe)) && (int?)row["expectedProductCount"] == 1 &&
                (int?)row["expectedPurity"] == 88 && (string?)row["expectedGrade"] == "near_pharma");
            VcAssertSource(recipe, VcFind((int)row["sourceUid"]!), recipe.Volume * 2, 0, parent, stage);
            VcAssertProduct(recipe, VcFind((int)row["productUid"]!), parent, stage);
        }
    }
    private GameItem VcFind(int uid)
    {
        GameItem[] found = Store.FindAllItem(false).Distinct().Where(item => item.uniqueId == uid).ToArray();
        VcRequire("uid-custody." + uid, "Exactly one owned live count-positive item", found.Select(VcItem).ToArray(),
            uid > 0 && found.Length == 1 && GeneralHelper.IsItemOwned(found[0]) && found[0].unitCount > 0 && !VcDestroyed(found[0]));
        return found.Single();
    }
    private JArray VcPromisedSnapshot(IEnumerable<int> uids, string stage)
    {
        int[] promised = uids.ToArray();
        VcRequire("snapshot-promises." + stage, "Seventeen unique positive explicitly promised UIDs", promised,
            promised.Length == 17 && promised.Distinct().Count() == 17 && promised.All(uid => uid > 0));
        return new JArray(promised.OrderBy(uid => uid).Select(uid => VcItem(VcFind(uid))));
    }
    private static bool VcTryPlace(GameInventory inventory, GameItem item)
    {
        SlotMarker? slot = inventory.TryFindOneValidInventorySlot(item);
        return slot != null && slot.IsValid() && ReferenceEquals(slot.inventory, inventory) && slot.numTransfer >= item.unitCount &&
            slot.TryAcceptOnce(item.unitCount) == item.unitCount && ReferenceEquals(item.parentInventory, inventory);
    }
    private void VcPlace(GameInventory inventory, GameItem item, string stage) => VcRequire("legal-placement." + stage,
        "Unchanged native SlotMarker accepted whole count into exact requested inventory", new { item.identifier, item.uniqueId }, VcTryPlace(inventory, item));
    private void VcPlaceTop(GameItem item)
    {
        if (!new GameInventory[] { EmporiumEntry.Instance.invElement, EmporiumEntry.Instance.backInvinvElement, EmporiumEntry.Instance.showcaseElement }
            .Any(inventory => VcTryPlace(inventory, item))) throw new InvalidOperationException("Native top-level inventory has no legal position for " + item.identifier);
    }
    private JObject VcGeometry(GameGridInventory grid) => new() { ["width"] = grid.inventoryShape.width, ["height"] = grid.inventoryShape.height,
        ["items"] = new JArray(grid.childItems.Select(VcItem)),
        ["cells"] = new JArray(Enumerable.Range(0, grid.inventoryShape.height).SelectMany(y => Enumerable.Range(0, grid.inventoryShape.width).Select(x =>
            new JObject { ["x"] = x, ["y"] = y, ["uids"] = new JArray(grid.childItems.Where(item => item.modifiedShape.Intersects(x, y)).Select(item => item.uniqueId)) }))) };
    private void VcDispose(GameItem? item, string reason)
    {
        if (item == null || VcDestroyed(item)) return;
        _vcDisposals.Add(new JObject { ["reason"] = reason, ["before"] = VcItem(item), ["liquidPartsDiscarded"] = item.IsTag("LIQUID_CONTAINER_TAG") ? WaterHelper.GetTotalVolume(item) : 0 });
        Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item);
    }
    private void VcDisposeOutput(VcFixture f, string reason) { foreach (GameItem item in f.Output.childItems.ToArray()) VcDispose(item, reason); }
    private void VcDisposeAttempt(VcObservation observer, string reason)
    { foreach (GameItem item in observer.Created.ToArray()) if (!VcDestroyed(item)) VcDispose(item, reason); }
    private static IEnumerable<object?> VcAction(Action action) { action(); yield break; }
    private IEnumerable<object?> VcCase(string name, Func<IEnumerable<object?>> action)
    {
        int first = R.checks.Count; var row = new JObject { ["name"] = name, ["firstCheck"] = first, ["status"] = "running" };
        _vcCases.Add(row); R.phase = "vanillachemistry:" + name; IEnumerator<object?>? work = null;
        try
        {
            try { work = action().GetEnumerator(); }
            catch (Exception error) { R.Check("vanillachemistry.case." + name, "Required native case entered", null, false, error: error.ToString()); }
            while (work != null)
            {
                bool more = false; object? current = null; Exception? failure = null;
                try { more = work.MoveNext(); if (more) current = work.Current; } catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    row["exception"] = failure.ToString();
                    if (failure is not VcStopped) R.Check("vanillachemistry.case." + name + ".exception", "Required native case completes", null, false, error: failure.ToString());
                    break;
                }
                if (!more) break;
                yield return current;
            }
        }
        finally
        {
            try { work?.Dispose(); } catch (Exception error) { R.Check("vanillachemistry.case." + name + ".cleanup", "Native fixture cleanup completes", null, false, error: error.ToString()); }
            if (first == R.checks.Count) R.Check("vanillachemistry.case." + name + ".empty", "At least one actual required observation", null, false);
            row["lastCheckExclusive"] = R.checks.Count; row["status"] = R.checks.Skip(first).Any(check => check.status == "failed") ? "failed" : "passed";
        }
    }

    // Passive observers retain the actual native-created product long enough to
    // classify an escaped/detached output. They never change arguments/results,
    // output admission, Steam state, timers, delegates or business callbacks.
    private sealed class VcObservation : IDisposable
    {
        private static VcObservation? Active;
        private readonly ProbeContext _context; private readonly Harmony _hooks; private readonly object _statusTarget;
        private string? _action, _product;
        internal readonly List<GameItem> Created = new();
        internal readonly JArray Checks = new(), Errors = new(), FactoryCalls = new();
        internal long NativeStatusCallbacks;
        internal JObject Evidence => new() { ["nativeStatusCallbacks"] = NativeStatusCallbacks, ["factoryCalls"] = FactoryCalls.DeepClone(),
            ["checkCalls"] = Checks.DeepClone(), ["errors"] = Errors.DeepClone(), ["steamInitialized"] = SteamManager.Initialized, ["accountWrites"] = "not-tested/not-invoked" };
        internal VcObservation(ProbeContext context, GameItem machine)
        {
            if (Active != null) throw new InvalidOperationException("Chemical UI observation reentry");
            _context = context; _hooks = new Harmony("nicokobo.content_native_probe.vanilla_chem." + context.O.RunId);
            var status = machine.onOneSecondItemFunc ?? throw new InvalidOperationException("Native finisher one-second status callback missing");
            _statusTarget = status.Target ?? throw new InvalidOperationException("Expected captured native finisher callback target missing");
            if (status.Method.IsStatic || status.Method.DeclaringType?.Assembly != typeof(GameItem).Assembly || AchievementTracker.Instance == null || SteamManager.Initialized)
                throw new InvalidOperationException("Native callback/achievement singleton or disabled Steam prerequisite differs from this build");
            try
            {
                var factory = typeof(DirectoryMaster).GetMethod(nameof(DirectoryMaster.Item), new[] { typeof(string), typeof(bool) }) ?? throw new MissingMethodException("Native Item factory unavailable");
                var check = typeof(AchievementTracker).GetMethod(nameof(AchievementTracker.CheckLabPure), new[] { typeof(GameItem) }) ?? throw new MissingMethodException("Native CheckLabPure unavailable");
                Active = this;
                _hooks.Patch(factory, postfix: new HarmonyMethod(typeof(VcObservation), nameof(CreatedPostfix)));
                _hooks.Patch(check, prefix: new HarmonyMethod(typeof(VcObservation), nameof(CheckPrefix)));
                _hooks.Patch(status.Method, postfix: new HarmonyMethod(typeof(VcObservation), nameof(StatusPostfix)));
            }
            catch { Dispose(); throw; }
        }
        internal void Begin(string action, string product)
        {
            if (_action != null) throw new InvalidOperationException("Nested native output UI action");
            _action = action; _product = product; Created.Clear(); Checks.Clear(); FactoryCalls.Clear();
        }
        internal void End() { _action = null; _product = null; }
        private static void StatusPostfix(object __instance)
        { if (Active is { } observer && ReferenceEquals(__instance, observer._statusTarget)) observer.NativeStatusCallbacks++; }
        private static string[] Callers() => new System.Diagnostics.StackTrace().GetFrames().Select(frame => frame.GetMethod())
            .Where(method => method != null).Take(12).Select(method => method!.DeclaringType?.FullName + "." + method.Name).ToArray();
        private static void CreatedPostfix(string __0, GameItem __result)
        {
            if (Active is not { } observer || observer._action == null || __0 != observer._product) return;
            try
            {
                string[] callers = Callers();
                if (!callers.Any(caller => caller.StartsWith("MachineChemFinisher+", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Expected recipe product was created outside native MachineChemFinisher UI callback");
                observer.Created.Add(__result);
                observer.FactoryCalls.Add(new JObject { ["action"] = observer._action, ["id"] = __0, ["uid"] = __result.uniqueId,
                    ["frame"] = Time.frameCount, ["callers"] = new JArray(callers) });
            }
            catch (Exception error) { observer.Errors.Add(error.ToString()); }
        }
        private static void CheckPrefix(GameItem __0)
        {
            if (Active is not { } observer || observer._action == null) return;
            try { observer.Checks.Add(new JObject { ["action"] = observer._action, ["product"] = observer._context.VcItem(__0),
                ["steamInitialized"] = SteamManager.Initialized, ["callers"] = new JArray(Callers()) }); }
            catch (Exception error) { observer.Errors.Add(error.ToString()); }
        }
        internal JArray CreationEvidence() => new(Created.Select(item => new JObject { ["state"] = VcDestroyed(item)
                ? new JObject { ["uid"] = item.uniqueId, ["id"] = item.identifier, ["destroyed"] = true } : _context.VcItem(item),
            ["inLiveGraph"] = _context.Store.FindAllItem(false).Any(live => ReferenceEquals(live, item)),
            ["classification"] = VcDestroyed(item) ? "destroyed-or-merged" : item.parentInventory == null ? "detached-live-native-product" : "placed-native-product" }));
        public void Dispose() { if (ReferenceEquals(Active, this)) Active = null; _hooks.UnpatchAll(_hooks.Id); }
    }
}
