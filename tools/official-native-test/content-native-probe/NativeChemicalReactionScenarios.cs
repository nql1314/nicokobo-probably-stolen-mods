using System.Reflection;
using DseCecil;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private sealed class CrSpec(string method, int target, double minimum, double maximum, string missing,
        Dictionary<string, int> input, Dictionary<string, int> output, string formula)
    {
        internal readonly string Method = method, Missing = missing, Formula = formula;
        internal readonly int Target = target;
        internal readonly double Minimum = minimum, Maximum = maximum;
        internal readonly Dictionary<string, int> Input = input, Output = output;
        internal bool Heated => Target > 26;
        internal JObject Definition() => new() { ["method"] = Method, ["targetUiCelsius"] = Target,
            ["observedReactionTemperatureMinimum"] = Minimum, ["observedReactionTemperatureMaximum"] = Maximum,
            ["maximumInclusive"] = Method == "HandleNomoreReaction", ["minimumStrict"] = Method == "HandleBoilingReaction",
            ["input"] = CrFixed(Input), ["fixedOutput"] = Method.EndsWith("Degradation", StringComparison.Ordinal) ? null : CrFixed(Output),
            ["temperatureDependentOutput"] = Method.EndsWith("Degradation", StringComparison.Ordinal), ["missingComponent"] = Missing, ["formula"] = Formula,
            ["coldControl"] = Heated ? "two ordinary cold ticks, unchanged" : "not-applicable: this native rule has no temperature gate; cold is its positive case" };
    }
    private static Dictionary<string, int> CrAmounts(params (string Id, int Parts)[] parts) => parts.ToDictionary(part => part.Id, part => part.Parts, StringComparer.Ordinal);
    // Acceptance vectors are fixed independently of the called native methods.
    // Temperature-dependent degradation uses only the observed PRE-reaction T;
    // no native handler, split helper or current post-state generates an oracle.
    private static readonly CrSpec[] CrSpecs =
    {
        new("HandleMicrobeHeat", 96, 70, 100, "microbe", CrAmounts(("microbe",1000)), CrAmounts(("microbe",800)), "70<=T<100: floor(1000*0.8)=800; cold T<70 unchanged"),
        new("HandleMicrobeAlcohol", 26, 0, 40, "alcohol", CrAmounts(("alcohol",1000),("microbe",1000)), CrAmounts(("alcohol",1000)), "alcohol=50% >=40%; min(1000,1000) microbes removed, alcohol unchanged"),
        new("HandleOrganicWasteHeat", 96, 80, 100, "organic_waste", CrAmounts(("organic_waste",1000)), CrAmounts(("organic_waste",850),("physical_contaminant",50)), "80<=T<100: ceil(1000*.15)=150 removed; RoundToEven(150*.33)=50 physical_contaminant"),
        new("HandleDreamCapMushReaction", 46, 40, 50, "alcohol", CrAmounts(("dream_cap_mush",1000),("alcohol",5000)), CrAmounts(("dream_cap_extract",500),("plant_matter",500),("alcohol",5000)), "T>=40; minimum2000 clamped to1000; solvent/mush=5 ->50% extract; T<50 isolates degradation"),
        new("HandleNightmareCapMushReaction", 46, 40, 50, "alcohol", CrAmounts(("nightmare_cap_mush",1000),("alcohol",5000)), CrAmounts(("nightmare_extract",500),("plant_matter",500),("alcohol",5000)), "T>=40; 1000 mush ->500 extract+500 plant, solvent unchanged; T<50 isolates both degradations"),
        new("HandleCoffeeBeanExtractionSolvent", 56, 50, 78.37, "alcohol", CrAmounts(("coffee_mush",2000),("alcohol",5000)), CrAmounts(("coffee_mush",1700),("caffeine",225),("plant_matter",75),("alcohol",5000)), "Round(2000*.15)=300; excess solvent ->75% yield=225; below alcohol boiling"),
        new("HandleCoffeeBeanExtractionWater", 96, 80, 100, "water", CrAmounts(("coffee_mush",2000),("water",5000)), CrAmounts(("coffee_mush",1900),("caffeine",12),("plant_matter",88),("water",5000)), "Round(2000*.05)=100; excess water ->RoundToEven(12.5)=12 caffeine,88 plant; below water boiling"),
        new("HandleNomoreReaction", 56, 50, 55, "reducing_agent", CrAmounts(("dream_cap_extract",4000),("reducing_agent",4000),("lipobond_agent",1000)), CrAmounts(("dream_cap_extract",3600),("reducing_agent",3600),("lipobond_agent",1000),("nomore",240),("debris",160)), "50<=T<=55, no alcohol; lipobond*4/extract=1; Round(4000*.1)=400,240 nomore+160 debris"),
        new("HandleDreamCapExtractDegradation", 76, 60, 70, "dream_cap_extract", CrAmounts(("dream_cap_extract",1000)), CrAmounts(), "60<=T<70: loss=floor(1000*.1*((T-60)/10)); no coproduct"),
        new("HandleDreamCapMushDegradation", 61, 50, 70, "dream_cap_mush", CrAmounts(("dream_cap_mush",1000)), CrAmounts(), "50<=T<70: loss=floor(1000*.1*((T-50)/10)); same amount becomes plant_matter"),
        new("HandleNightmareCapExtractDegradation", 61, 50, 70, "nightmare_extract", CrAmounts(("nightmare_extract",1000)), CrAmounts(), "50<=T<70: native coefficient is .1*((T-50)/2), not /10; loss=floor(1000*coefficient)"),
        new("HandleNightmareCapMushDegradation", 61, 50, 70, "nightmare_cap_mush", CrAmounts(("nightmare_cap_mush",1000)), CrAmounts(), "50<=T<70: loss=floor(1000*.1*((T-50)/10)); same amount becomes plant_matter"),
        new("HandleOrganicWasteMineralization", 26, 0, 40, "acid", CrAmounts(("organic_waste",1000),("acid",10)), CrAmounts(("organic_waste",600),("mineral",40)), "min(1000,10*40)=400 organic used; ceil(400/40)=10 acid;400/10=40 mineral"),
        new("HandleAcidBaseMicrobeKill", 26, 0, 40, "acid", CrAmounts(("microbe",1000),("acid",10)), CrAmounts(("microbe",800),("acid",8)), "min(1000,10*20)=200 killed; ceil(200/100)=2 acid used"),
        new("HandleStabilizingAgentSedimentation", 26, 0, 40, "acid", CrAmounts(("stabilizing_agent",4000),("acid",2500)), CrAmounts(("stabilizing_agent",3000),("acid",2375),("debris",1000)), "min(2500,max(1000,floor(2500*.2)))=1000 sediment; max(100,floor(2500*.05))=125 acid used"),
        new("HandleProteinPrecipitation", 26, 0, 40, "acid", CrAmounts(("protein",4000),("acid",5000)), CrAmounts(("protein",3200),("acid",4200),("debris",800)), "acid/base imbalance5000; min4000*.2=800 denatured and800 acid consumed"),
        new("HandleLipobondAgentSedimentation", 26, 0, 40, "lipobond_agent", CrAmounts(("fat",10000),("lipobond_agent",1000)), CrAmounts(("fat",8400),("lipobond_agent",800),("debris",1800)), "min(10000,1000*8)=8000; max(1000,8000*.2)=1600 fat +200 agent ->1800 debris"),
        new("HandleAcidBaseNeutralization", 26, 0, 40, "base", CrAmounts(("acid",2000),("base",3000)), CrAmounts(("acid",1000),("base",2000)), "minPair2000, max(1000,floor(2000*.1))=1000 from each; no new water"),
        new("HandleFantaHeat", 160, 130, 161, "fanta", CrAmounts(("fanta",1000)), CrAmounts(("fanta",980)), "T>=130: floor(1000*.98)=980; cold writes same amount and cannot count as conversion"),
        new("HandleMethylolHeatReaction", 126, 120, 130, "reducing_agent", CrAmounts(("methylol",11000),("reducing_agent",10000),("stabilizing_agent",1000)), CrAmounts(("methylol",9500),("reducing_agent",8875),("stabilizing_agent",1000),("fanta",150)), "120<=T<130: available=min(11000-1000,10000)=10000; .15=>1500;1125 reducing used;150 fanta"),
        new("HandleChlorine", 26, 0, 40, "water", CrAmounts(("chlorine",1000),("water",1000),("chemical_contaminant",1000),("microbe",1000)), CrAmounts(("water",1000),("chemical_contaminant",28)), "4000*.007f ->28 chemical floor;1000 chlorine potency5100 removes972 chemicals then1000 microbes; chlorine0"),
        new("HandleBoilingReaction", 126, 100, 161, "water", CrAmounts(("water",10000)), CrAmounts(("water",9800)), "T>100: max(floor(10000*.02),100)=200 water evaporated; cold/below boiling unchanged")
    };
    private readonly JArray _crRules = new(), _crCases = new(), _crUi = new(), _crDiscard = new(), _crWitnesses = new(), _crRoutes = new();
    private readonly HashSet<int> _crPromised = new();
    private sealed class CrStopped(string name) : Exception(name) { }
    private void CrRequire(string name, object? expected, object? observed, bool passed, string evidence = "")
    { R.Check("chemicalreactions." + name, expected, observed, passed, evidence); if (!passed) throw new CrStopped(name); }
    private sealed class CrFixture : IDisposable
    {
        internal GameItem Plate = null!, Blender = null!, Carrier = null!, Cup = null!, Battery = null!;
        internal GameGridInventory PlateModules = null!, BlenderModules = null!, BlenderInput = null!, Storage = null!;
        internal GameSlotInventory PlateInput = null!, BlenderCup = null!;
        internal MachineryRadioList Modes = null!;
        internal MachineryButton Minus = null!, Plus = null!, Blend = null!;
        internal bool Keep;
        public void Dispose()
        {
            Plate?.contentWindow?.Hide(); Blender?.contentWindow?.Hide(); Carrier?.contentWindow?.Hide();
            if (!Keep) { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(Plate); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(Blender); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(Carrier); }
        }
    }

    internal IEnumerable<object?> NativeChemicalReactions()
    {
        CrDescribe(); CrDenominator();
        using var f = CrCreate(); using var observation = new CrObservation(this);
        foreach (GameItem root in new[] { f.Plate, f.Blender, f.Carrier })
            foreach (GameItem member in new[] { root }.Concat(root.FindAllChildItems())) _crPromised.Add(member.uniqueId);
        foreach (object? wait in CrCase("native-blender-and-soluble-target", () => CrMaterialRoutes(f))) yield return wait;
        foreach (CrSpec spec in CrSpecs)
        {
            var row = spec.Definition(); row["positive"] = "not-tested"; row["missing"] = "not-tested";
            row["cold"] = spec.Heated ? "not-tested" : "not-applicable-no-temperature-gate"; _crRules.Add(row);
            foreach (object? wait in CrCase(spec.Method, () => CrRule(f, observation, spec, row))) yield return wait;
        }
        CrRequire("all-22-required-business-vectors", new { positive = 22, missing = 22, cold = 14 },
            new { positive = _crRules.Count(row => (string?)row["positive"] == "passed"), missing = _crRules.Count(row => (string?)row["missing"] == "passed"),
                cold = _crRules.Count(row => (string?)row["cold"] == "passed"), rules = _crRules },
            _crRules.Count == 22 && _crRules.All(row => (string?)row["positive"] == "passed" && (string?)row["missing"] == "passed") &&
            _crRules.Count(row => (string?)row["cold"] == "passed") == 14);
        CrMode(f, 0); CrValidateWitnesses(_crWitnesses, f.Storage, "before-save");
        CrRequire("routes-both-real-entries", new[] { "native-blender", "native-soluble-target" }, _crRoutes,
            _crRoutes.Count == 2 && _crRoutes.All(row => (bool?)row["passed"] == true));
        CrRequire("ordinary-update-observer", "No observer error; all calls came from ordinary native Update", observation.Errors, observation.Errors.Count == 0);
        int[] promised = _crPromised.OrderBy(uid => uid).ToArray();
        CrRequire("promised-35-uid-denominator", 35, promised, promised.Length == 35);
        JArray projection = CrProjection(promised);
        Retain(f.Plate); Retain(f.Blender); Retain(f.Carrier); f.Keep = true;
        R.persistence["nativeChemicalReactions"] = new JObject { ["schema"] = 1, ["steamBuild"] = 26317870,
            ["gameRunId"] = Store.runID, ["slotId"] = Store.saveSlotId, ["definitions"] = new JArray(CrSpecs.Select(spec => spec.Definition())),
            ["rules"] = _crRules.DeepClone(), ["routes"] = _crRoutes.DeepClone(), ["discards"] = _crDiscard.DeepClone(),
            ["plateUid"] = f.Plate.uniqueId, ["blenderUid"] = f.Blender.uniqueId, ["carrierUid"] = f.Carrier.uniqueId,
            ["cupUid"] = f.Cup.uniqueId, ["batteryUid"] = f.Battery.uniqueId, ["promisedUids"] = new JArray(promised),
            ["projection"] = projection, ["config"] = CrConfig(f), ["emptyWitnesses"] = _crWitnesses.DeepClone(),
            ["cash"] = Store.playerCash, ["energy"] = VcEnergy(), ["grid"] = GridTotal,
            ["projectionScope"] = "UID/id/count/ownership/parent/capacity/all36 Parts/energy plus explicit machine UI settings. Every reaction liquid was really drained. No raw-tag, temperature or thermal-state equivalence is claimed." };
    }
    internal IEnumerable<object?> NativeChemicalReactionsReload(JObject baseline)
    {
        CrDescribe(); CrDenominator();
        var saved = baseline["persistence"]?["nativeChemicalReactions"] as JObject ?? throw new InvalidOperationException("Native reaction persistence baseline missing");
        CrRequire("reload-strict-bound-process", "Passed original-slot chemicalreactions-save from a distinct process/run", saved,
            (string?)baseline["mode"] == "chemicalreactions-save" && (bool?)baseline["passed"] == true && (int?)saved["schema"] == 1 &&
            (int?)saved["steamBuild"] == 26317870 && (int?)saved["slotId"] == Store.saveSlotId && (string?)saved["gameRunId"] == Store.runID &&
            (string?)baseline["runId"] != O.RunId && (int?)baseline["processId"] != R.processId && (string?)baseline["processToken"] != R.processToken && !O.UseCopy,
            "Common Reload also requires actual unchanged ES3 bytes, matching candidates and native LoadGame; no failed baseline admission.");
        CrRequire("reload-canonical-22-definitions", new JArray(CrSpecs.Select(spec => spec.Definition())), saved["definitions"],
            JToken.DeepEquals(saved["definitions"], new JArray(CrSpecs.Select(spec => spec.Definition()))));
        var promised = (saved["promisedUids"] as JArray)?.Values<int>().ToArray() ?? Array.Empty<int>();
        var projection = saved["projection"] as JArray ?? throw new InvalidOperationException("Reaction save projection missing");
        CrRequire("reload-complete-projection-denominator", "35 unique positive promised UIDs, each recorded once", new { promised, projection },
            promised.Length == 35 && promised.All(uid => uid > 0) && promised.Distinct().Count() == 35 && projection.Count == 35 &&
            promised.OrderBy(uid => uid).SequenceEqual(projection.Select(item => (int)item["uid"]!).OrderBy(uid => uid)));
        using var f = CrBind(CrFind((int)saved["plateUid"]!), CrFind((int)saved["blenderUid"]!), CrFind((int)saved["carrierUid"]!),
            CrFind((int)saved["cupUid"]!), CrFind((int)saved["batteryUid"]!)); f.Keep = true;
        JArray actual = CrProjection(promised);
        CrRequire("reload-explicit-stable-projection", projection, actual, JToken.DeepEquals(projection, actual),
            "Explicit projection only. Empty original vessels are stable resource witnesses; raw temperature, HEATING_TAG, derived unitValue and all-raw-tag equivalence are deliberately not asserted.");
        var witnesses = saved["emptyWitnesses"] as JArray ?? throw new InvalidOperationException("22 emptied original reaction witnesses missing");
        CrValidateWitnesses(witnesses, f.Storage, "reload-immediate");
        CrRequire("reload-real-machine-configuration", saved["config"], CrConfig(f), JToken.DeepEquals(saved["config"], CrConfig(f)));
        CrRequire("reload-cash-and-energy", new { cash = (int)saved["cash"]!, energy = saved["energy"], grid = (long)saved["grid"]! },
            new { cash = Store.playerCash, energy = VcEnergy(), grid = GridTotal }, Store.playerCash == (int)saved["cash"]! && JToken.DeepEquals(saved["energy"], VcEnergy()) && GridTotal == (long)saved["grid"]!);
        string file = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId)), hash = Reflect.HashFile(file);
        f.Plate.contentWindow.Show(); f.Blender.contentWindow.Show(); yield return null;
        CrRequire("reload-ui-native-upgrades-off", "Four legal native modules on each machine; heating UI Off and original target restored", CrConfig(f),
            f.Modes.selected == 0 && f.Plate.GetTagReadonly(MachineHeatingPlate.HEATING_PLATE_STATE).GetInt() == 0 &&
            MachineBlender.GetUpgradeCount(f.Plate) == 4 && MachineBlender.GetUpgradeCount(f.Blender) == 4);
        GameItem witness = CrFind((int)witnesses[0]!["uid"]!);
        CrPlace(f.PlateInput, witness, "reload-original-empty-vessel-reuse");
        CrRequire("reload-empty-vessel-remains-empty", CrFixed(CrAmounts()), VcParts(witness), WaterHelper.GetTotalVolume(witness) == 0 && JToken.DeepEquals(VcParts(witness), CrFixed(CrAmounts())));
        CrPlace(f.Storage, witness, "reload-return-original-uid");
        actual = CrProjection(promised); CrValidateWitnesses(witnesses, f.Storage, "reload-after-real-slot-reuse");
        CrRequire("reload-no-save-no-reaction-replay", new { hash, projection }, new { hash = Reflect.HashFile(file), projection = actual },
            hash == Reflect.HashFile(file) && JToken.DeepEquals(projection, actual));
        R.persistence["nativeChemicalReactions"] = saved.DeepClone();
    }

    private void CrDescribe()
    {
        R.phase = "chemicalreactions-ordinary-native-update"; R.environment["nativeChemicalReactionRules"] = _crRules;
        R.environment["nativeChemicalReactionCases"] = _crCases; R.environment["nativeChemicalReactionUi"] = _crUi;
        R.environment["nativeChemicalReactionDiscards"] = _crDiscard; R.environment["nativeChemicalMaterialRoutes"] = _crRoutes;
        R.environment["nativeChemicalReactionScope"] = new { build = 26317870, fixedRules = 22, heatedColdControls = 14, missingReactantControls = 22,
            source = "Exact current HandleTemperatureReaction IL call sequence + 22 naturally observed calls per complete liquid tick",
            action = "Real Blender button, soluble GameItem.MayTarget/CanTarget/Target, HeatingPlate radio/temperature buttons and four legally installed chem_module items; ordinary EmporiumEntry.Update only",
            oracle = "Fixed 36-component input/output vectors; four degradation outputs depend only on pre-reaction observed T and an independent explicit formula",
            grants = "Native factories and WaterHelper reagent grants are fixture preparation, not economic acquisition or natural reagent synthesis",
            persistence = "Actual drained original vessels + native machines/modules/UI configuration; explicit projection only, no thermal or all-raw-tag equivalence",
            excludes = "No direct reaction/HandleHeating/Finish/NativeCycle/EndNight invocation, timer/date/temperature/flag assignment, mocked write or disabled chemistry. Threshold families are bounded representatives, not every temperature/mixture/RNG cross product." };
    }
    private void CrDenominator()
    {
        MethodInfo root = typeof(ChemicalHelper).GetMethod(nameof(ChemicalHelper.HandleTemperatureReaction), new[] { typeof(GameItem) })
            ?? throw new MissingMethodException("Native chemical reaction entry missing");
        string[] called = CrNativeRuleCalls(root);
        CrRequire("runtime-exact-22-call-sequence", CrSpecs.Select(spec => spec.Method).ToArray(), called,
            CrSpecs.Length == 22 && called.SequenceEqual(CrSpecs.Select(spec => spec.Method)) && called.Distinct().Count() == 22);
        CrRequire("runtime-complete-36-component-denominator", VcSpecies.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Liquid.Liquids.Select(liquid => liquid.identifier).OrderBy(id => id, StringComparer.Ordinal).ToArray(), VcSpecies.Length == 36 &&
            VcSpecies.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(Liquid.Liquids.Select(liquid => liquid.identifier).OrderBy(id => id, StringComparer.Ordinal)));
        foreach (string id in new[] { "heating_plate", "blender", "chem_module", "25ml_cylinder", "storage_bay_large", "birth_control_pill" })
        {
            var factory = DirectoryMaster.GetFactory<GameItem>(id);
            CrRequire("native-factory." + id, "Registered Assembly-CSharp factory", factory?.Method.DeclaringType?.FullName,
                factory != null && factory.Method.DeclaringType?.Assembly == typeof(GameItem).Assembly);
        }
        CrRequire("passive-steam-off", false, SteamManager.Initialized, !SteamManager.Initialized);
    }
    private static string[] CrNativeRuleCalls(MethodInfo method)
    {
        Type nativeType = typeof(ChemicalHelper);
        var loaded = nativeType.Assembly;
        if (loaded.IsDynamic || method.DeclaringType != nativeType || method.Module.Assembly != loaded ||
            method.Module.ModuleVersionId != loaded.ManifestModule.ModuleVersionId || !method.IsStatic || method.ReturnType != typeof(void) ||
            !method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(GameItem) }))
            throw new InvalidOperationException("Expected original native chemical entry identity/signature is unavailable");
        string path = loaded.Location;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path))
            throw new InvalidOperationException("Loaded native chemical assembly has no readable physical metadata file");
        // Read only the exact loaded assembly's metadata. No runtime IL API,
        // code emission, external assembly resolution or metadata writes.
        using var metadata = AssemblyDefinition.ReadAssembly(path);
        var module = metadata.MainModule;
        if (metadata.Modules.Count != 1 || module.Mvid != method.Module.ModuleVersionId ||
            !string.Equals(metadata.Name.FullName, loaded.FullName, StringComparison.Ordinal))
            throw new InvalidOperationException("On-disk chemical metadata differs from the actual loaded assembly/MVID");
        var entry = module.LookupToken(method.MetadataToken) as MethodDefinition;
        if (entry == null || entry.MetadataToken.ToInt32() != method.MetadataToken || !ReferenceEquals(entry.Module, module) ||
            entry.DeclaringType.FullName != nativeType.FullName || entry.DeclaringType.MetadataToken.ToInt32() != nativeType.MetadataToken ||
            entry.Name != method.Name || !entry.IsStatic || entry.HasGenericParameters || entry.ReturnType.FullName != "System.Void" ||
            entry.Parameters.Count != 1 || entry.Parameters[0].ParameterType.FullName != typeof(GameItem).FullName || !entry.HasBody)
            throw new InvalidOperationException("Native chemical entry token/declaring type/signature/body binding failed");
        var instructions = entry.Body.Instructions;
        if (instructions.Count == 0 || instructions[0].Offset != 0 || instructions[instructions.Count - 1].OpCode.Code != DseCecil.Cil.Code.Ret)
            throw new InvalidOperationException("Native chemical metadata body is empty or incomplete");
        var names = new List<string>();
        foreach (var instruction in instructions)
        {
            if (instruction.OpCode.Code != DseCecil.Cil.Code.Call && instruction.OpCode.Code != DseCecil.Cil.Code.Callvirt) continue;
            if (instruction.Operand is not MethodReference reference)
                throw new InvalidOperationException("Native chemical call instruction has no method operand");
            if (reference.DeclaringType.FullName != nativeType.FullName) continue;
            var called = module.LookupToken(reference.MetadataToken) as MethodDefinition;
            if (called == null || !ReferenceEquals(called.Module, module) || called.MetadataToken != reference.MetadataToken ||
                called.DeclaringType.MetadataToken.ToInt32() != nativeType.MetadataToken || called.DeclaringType.FullName != nativeType.FullName ||
                called.FullName != reference.FullName || !called.IsStatic || instruction.OpCode.Code == DseCecil.Cil.Code.Callvirt || called.HasGenericParameters ||
                called.ReturnType.FullName != "System.Int32" || called.Parameters.Count != 1 ||
                called.Parameters[0].ParameterType.FullName != typeof(GameItem).FullName || !called.HasBody)
                throw new InvalidOperationException("Native chemical rule is not a same-module static int(GameItem) method definition");
            names.Add(called.Name);
        }
        return names.ToArray();
    }
    private CrFixture CrCreate()
    {
        GameItem? plate = null, blender = null, carrier = null;
        try
        {
            plate = Spawn("heating_plate"); blender = Spawn("blender"); carrier = Spawn("storage_bay_large");
            VcPlaceTop(plate); VcPlaceTop(blender); VcPlaceTop(carrier);
            GameItem cup = Spawn("25ml_cylinder"), battery = PreBuiltItemHelper.EnergyCredit(100);
            var f = CrBind(plate, blender, carrier, cup, battery);
            CrPlace(f.BlenderCup, cup, "native-blender-cup"); CrPlace(f.Storage, battery, "native-energy-control");
            foreach (var pair in new[] { (Machine: plate, Grid: f.PlateModules), (Machine: blender, Grid: f.BlenderModules) })
            {
                for (int index = 0; index < 4; index++) CrPlace(pair.Grid, Spawn("chem_module"), "legal-native-chemical-upgrade." + pair.Machine.identifier + "." + index);
                CrRequire("four-legal-upgrades." + pair.Machine.identifier, "Four owned native modules, native upgrade callback count4", pair.Grid.childItems.Select(VcItem).ToArray(),
                    MachineBlender.GetUpgradeCount(pair.Machine) == 4 && pair.Grid.childItems.Count == 4 && pair.Grid.childItems.All(item => item.identifier == "chem_module" && item.unitCount == 1 &&
                        item.IsTag("MODULE_TYPE_CHEM") && GeneralHelper.IsItemOwned(item) && ReferenceEquals(item.parentInventory, pair.Grid)) &&
                    pair.Machine.GetTagReadonly(ChemicalHelper.CHEMICAL_MACHINE_UPGRADE_COUNT).GetInt() == 4);
            }
            return f;
        }
        catch { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(plate); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(blender); Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(carrier); throw; }
    }
    private CrFixture CrBind(GameItem plate, GameItem blender, GameItem carrier, GameItem cup, GameItem battery)
    {
        var plateRow = plate.contentWindow?.childElement as MachineryPanelRow ?? throw new InvalidOperationException("Native heating panel row missing");
        var blendRow = blender.contentWindow?.childElement as MachineryPanelRow ?? throw new InvalidOperationException("Native blender panel row missing");
        var target = plateRow.GetPanel("target")?.content as MachineryStack ?? throw new InvalidOperationException("Native heating target controls missing");
        return new CrFixture { Plate = plate, Blender = blender, Carrier = carrier, Cup = cup, Battery = battery,
            PlateModules = MachineHelper.GetBodyStorage(plate, 0) as GameGridInventory ?? throw new InvalidOperationException("Native heating module grid0 missing"),
            PlateInput = MachineHelper.GetBodyStorage(plate, 1) as GameSlotInventory ?? throw new InvalidOperationException("Native heating vessel slot1 missing"),
            BlenderModules = MachineHelper.GetBodyStorage(blender, 0) as GameGridInventory ?? throw new InvalidOperationException("Native blender module grid0 missing"),
            BlenderInput = MachineHelper.GetBodyStorage(blender, 1) as GameGridInventory ?? throw new InvalidOperationException("Native blender raw-input grid1 missing"),
            BlenderCup = MachineHelper.GetBodyStorage(blender, 2) as GameSlotInventory ?? throw new InvalidOperationException("Native blender liquid slot2 missing"),
            Storage = carrier.contentWindow?.childElement as GameGridInventory ?? throw new InvalidOperationException("Native witness carrier grid missing"),
            Modes = plateRow.GetPanel("control")?.content as MachineryRadioList ?? throw new InvalidOperationException("Native heating mode radios missing"),
            Minus = target.elements[0] as MachineryButton ?? throw new InvalidOperationException("Native minus button missing"),
            Plus = target.elements[2] as MachineryButton ?? throw new InvalidOperationException("Native plus button missing"),
            Blend = blendRow.GetPanel("control")?.content as MachineryButton ?? throw new InvalidOperationException("Native blender start button missing") };
    }
    private IEnumerable<object?> CrMaterialRoutes(CrFixture f)
    {
        f.Blender.contentWindow.Show(); yield return null;
        CrRequire("native-blender-zero-loss", 0, MachineBlender.GetLoss(f.Blender), MachineBlender.GetLoss(f.Blender) == 0);
        foreach (string route in new[] { "native-blender", "native-soluble-target" })
        {
            GameItem raw = Spawn("birth_control_pill");
            try
            {
                CrPlace(route == "native-blender" ? f.BlenderInput : f.Storage, raw, "factory-raw-material." + route);
                JObject input = VcParts(raw); int uid = raw.uniqueId; JObject resources = CrMoney();
                CrRequire("raw-material-is-fixture." + route, "Owned count1 native raw tablet, exactly100 chemical parts, blendable and soluble", VcItem(raw),
                    raw.unitCount == 1 && GeneralHelper.IsItemOwned(raw) && raw.IsTag(ChemicalHelper.BLENDABLE_TAG) && raw.IsTag(ChemicalHelper.SOLUBLE_TAG) &&
                    input.Properties().Sum(property => (int)property.Value) == 100 && WaterHelper.GetTotalVolume(f.Cup) == 0,
                    "This factory tablet is explicitly a RAW MATERIAL grant, never evidence that a finished recipe was produced. Its pre-action component invoice is the measured input, not a post-action expected result.");
                if (route == "native-blender") CrClick(f.Blender, f.Blend, route);
                else
                {
                    CrRequire("soluble-native-target-guards", new { may = true, can = true }, new { may = raw.MayTarget(f.Cup), can = raw.CanTarget(f.Cup) }, raw.MayTarget(f.Cup) && raw.CanTarget(f.Cup));
                    raw.Target(f.Cup);
                }
                bool pass = !Store.FindAllItem(false).Any(item => item.uniqueId == uid) && JToken.DeepEquals(input, VcParts(f.Cup)) && WaterHelper.GetTotalVolume(f.Cup) == 100 &&
                    GeneralHelper.IsItemOwned(f.Cup) && ReferenceEquals(f.Cup.parentInventory, f.BlenderCup) && JToken.DeepEquals(resources, CrMoney());
                var row = new JObject { ["route"] = route, ["rawFixtureUid"] = uid, ["input"] = input, ["expectedReceiverParts"] = input.DeepClone(),
                    ["receiverUid"] = f.Cup.uniqueId, ["after"] = VcParts(f.Cup), ["expectedConsumedCount"] = 1, ["passed"] = pass };
                _crRoutes.Add(row); CrRequire("real-entry-and-complete-invoice." + route, "Raw UID consumed once; all36 measured input components received without loss; no cash/energy change", row, pass);
                CrDrain(f.Cup, route + ".discard-intermediate");
            }
            finally { if (!VcDestroyed(raw)) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(raw); }
        }
    }
    private IEnumerable<object?> CrRule(CrFixture f, CrObservation observation, CrSpec spec, JObject row)
    {
        CrMode(f, 0); f.Plate.contentWindow.Show();
        float ready = Time.realtimeSinceStartup + 8;
        while (f.Minus.isHidden || f.Plus.isHidden)
        { if (Time.realtimeSinceStartup >= ready) CrRequire("upgrade-controls-visible." + spec.Method, "Normal one-second callback reveals legal upgraded temperature buttons", null, false); yield return null; }
        CrTarget(f, spec.Target);
        GameItem? main = null, missing = null;
        try
        {
            main = CrVessel(f, spec.Input, spec.Method + ".main"); JObject money = CrMoney();
            if (spec.Heated)
            {
                foreach (object? wait in CrObserve(observation, main, spec, "cold", spec.Input, row)) yield return wait;
                row["cold"] = "passed";
            }
            CrMode(f, spec.Heated ? 2 : 0);
            foreach (object? wait in CrObserve(observation, main, spec, "positive", spec.Input, row)) yield return wait;
            row["positive"] = "passed";
            CrMode(f, 0); CrDrain(main, spec.Method + ".positive-real-disposal");
            CrPlace(f.Storage, main, spec.Method + ".retain-original-empty-vessel");
            _crPromised.Add(main.uniqueId);
            _crWitnesses.Add(new JObject { ["method"] = spec.Method, ["uid"] = main.uniqueId, ["expectedParts"] = CrFixed(CrAmounts()), ["capacity"] = 25000, ["count"] = 1 });
            var missingInput = new Dictionary<string, int>(spec.Input, StringComparer.Ordinal);
            int removed = missingInput[spec.Missing]; missingInput.Remove(spec.Missing); missingInput["physical_contaminant"] = removed;
            missing = CrVessel(f, missingInput, spec.Method + ".missing-" + spec.Missing);
            CrMode(f, spec.Heated ? 2 : 0);
            foreach (object? wait in CrObserve(observation, missing, spec, "missing", missingInput, row)) yield return wait;
            row["missing"] = "passed";
            CrMode(f, 0); CrDrain(missing, spec.Method + ".missing-real-disposal");
            CrRequire("zero-cash-energy-debit." + spec.Method, money, CrMoney(), JToken.DeepEquals(money, CrMoney()),
                "Heating plate and blender have no battery draw in this build. Actual world power-source UID/energy balances, network grid and cash remain fixed; fixture reagent conversion has its explicit non-conservative reaction invoice.");
        }
        finally
        {
            observation.Stop(); CrMode(f, 0);
            foreach (GameItem? vessel in new[] { main, missing })
                if (vessel != null && !_crPromised.Contains(vessel.uniqueId))
                {
                    if (!WaterHelper.IsEmpty(vessel)) CrDrain(vessel, spec.Method + ".failed-or-control-cleanup-not-rollback");
                    Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(vessel);
                }
        }
    }
    private GameItem CrVessel(CrFixture f, Dictionary<string, int> parts, string stage)
    {
        CrRequire("original-heating-slot-empty." + stage, null, f.PlateInput.childItem?.identifier, f.PlateInput.childItem == null);
        var vessel = Spawn("25ml_cylinder");
        try
        {
            CrPlace(f.PlateInput, vessel, stage);
            foreach (var part in parts) WaterHelper.AddLiquid(vessel, part.Key, part.Value);
            double temp = vessel.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble();
            CrRequire("independent-fixture." + stage, new { parts = CrFixed(parts), capacity = 25000, initialTemperature = "24..26 native ambient", count = 1 },
                new { item = VcItem(vessel), temp }, JToken.DeepEquals(CrFixed(parts), VcParts(vessel)) && WaterHelper.GetTotalVolume(vessel) == parts.Values.Sum() &&
                WaterHelper.GetFreeCapacity(vessel) + WaterHelper.GetTotalVolume(vessel) == 25000 && temp >= 24 && temp <= 26 &&
                vessel.unitCount == 1 && GeneralHelper.IsItemOwned(vessel) && ReferenceEquals(vessel.parentInventory, f.PlateInput));
            return vessel;
        }
        catch { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(vessel); throw; }
    }
    private IEnumerable<object?> CrObserve(CrObservation observation, GameItem vessel, CrSpec spec, string stage, Dictionary<string, int> input, JObject rule)
    {
        JArray identitiesBefore = CrIdentities();
        CrWatch watch = observation.Start(vessel, spec.Method, stage); int inspected = 0, qualifying = 0;
        var evidence = new JArray(); rule[stage + "Ticks"] = evidence; float until = Time.realtimeSinceStartup + 35;
        try
        {
            while (qualifying < (stage == "positive" ? 1 : 2))
            {
                while (inspected < watch.Ticks.Count)
                {
                    CrTick tick = watch.Ticks[inspected++]; evidence.Add(tick.Json());
                    CrRequire("ordinary-complete-native-entry." + spec.Method + "." + stage + "." + inspected,
                        "Normal native Update called all22 handlers in fixed order", tick.Json(), tick.Complete && tick.FromOrdinaryUpdate &&
                        tick.Calls.Select(step => step.Method).SequenceEqual(CrSpecs.Select(ruleSpec => ruleSpec.Method)) && observation.Errors.Count == 0);
                    bool warm = spec.Heated && tick.Temperature >= spec.Minimum && (spec.Method != "HandleBoilingReaction" || tick.Temperature > 100);
                    if (spec.Heated && (tick.Temperature > spec.Maximum || (spec.Method != "HandleNomoreReaction" && tick.Temperature == spec.Maximum)))
                        CrRequire("bounded-native-temperature." + spec.Method + "." + stage, new { spec.Minimum, spec.Maximum }, tick.Temperature, false,
                            "Observed temperature skipped the isolated representative band. No temperature writes, reaction suppression or broad tolerance may rescue this fixture.");
                    if (stage == "cold") CrRequire("real-cold-temperature." + spec.Method, "Native ambient below the rule's activation threshold", tick.Temperature, tick.Temperature < spec.Minimum);
                    JObject expected = stage == "positive" && (!spec.Heated || warm) ? CrExpected(spec, tick.Temperature) : CrFixed(input);
                    CrAssertTick(spec, stage, tick, CrFixed(input), expected);
                    if (stage == "cold" || !spec.Heated || warm) qualifying++;
                    if (qualifying >= (stage == "positive" ? 1 : 2)) break;
                }
                if (qualifying >= (stage == "positive" ? 1 : 2)) break;
                if (Time.realtimeSinceStartup >= until) CrRequire("actual-tick-timeout." + spec.Method + "." + stage,
                    "Ordinary native tick reaches the required independent observation", new { qualifying, ticks = evidence, errors = observation.Errors }, false);
                yield return null;
            }
            if (stage == "positive") CrRequire("actual-component-transition." + spec.Method, "At least one component really changed by the independent rule invoice",
                evidence, !JToken.DeepEquals(watch.Ticks[inspected - 1].Before, watch.Ticks[inspected - 1].After));
            CrRequire("no-uninspected-native-tick." + spec.Method + "." + stage, inspected, watch.Ticks.Count, inspected == watch.Ticks.Count,
                "The first fixed-input reaction may not silently adopt a later reaction iteration before cleanup.");
            JArray identitiesAfter = CrIdentities();
            CrRequire("all-uid-count-ownership-and-parent-invariants." + spec.Method + "." + stage, identitiesBefore, identitiesAfter,
                JToken.DeepEquals(identitiesBefore, identitiesAfter), "Native chemical conversion changes component quantities, never item identities, ownership or item counts.");
        }
        finally { observation.Stop(); }
    }
    private void CrAssertTick(CrSpec spec, string stage, CrTick tick, JObject before, JObject after)
    {
        bool pass = JToken.DeepEquals(before, tick.Before) && JToken.DeepEquals(after, tick.After) && tick.Uid > 0 && tick.Count == 1 && tick.Owned;
        JObject current = (JObject)before.DeepClone();
        foreach (CrStep step in tick.Calls)
        {
            pass &= JToken.DeepEquals(current, step.Before);
            if (step.Method == spec.Method) current = (JObject)after.DeepClone();
            pass &= JToken.DeepEquals(current, step.After);
        }
        CrRequire("whole-36-components-and-isolated-rule." + spec.Method + "." + stage + ".frame" + tick.Frame,
            new { before, after, nativeUid = tick.Uid, count = 1, owned = true, unchangedOther21Rules = true, formula = spec.Formula }, tick.Json(), pass,
            "Expected vectors use fixed inputs and literal rates/rounding; only pre-reaction temperature supplies thermal formula input. All other native rules still run and must preserve their full component arrays. Natural reaction loss is explicit, not mistaken for transfer loss.");
    }
    private static JObject CrExpected(CrSpec spec, double temperature)
    {
        if (!spec.Method.EndsWith("Degradation", StringComparison.Ordinal)) return CrFixed(spec.Output);
        bool extract = spec.Method.Contains("Extract", StringComparison.Ordinal), nightmare = spec.Method.Contains("Nightmare", StringComparison.Ordinal);
        string component = nightmare ? extract ? "nightmare_extract" : "nightmare_cap_mush" : extract ? "dream_cap_extract" : "dream_cap_mush";
        double lower = !nightmare && extract ? 60d : 50d;
        double denominator = nightmare && extract ? 2d : 10d;
        int amount = (int)(1000d * (0.1d * ((temperature - lower) / denominator)));
        var result = CrAmounts((component, 1000 - amount)); if (!extract) result["plant_matter"] = amount;
        return CrFixed(result);
    }
    private static JObject CrFixed(IReadOnlyDictionary<string, int> parts) => new(VcSpecies.OrderBy(id => id, StringComparer.Ordinal)
        .Select(id => new JProperty(id, parts.TryGetValue(id, out int value) ? value : 0)));
    private void CrClick(GameItem machine, SelectableElement control, string action)
    {
        var row = new JObject { ["action"] = action, ["machineUid"] = machine.uniqueId, ["visible"] = machine.contentWindow.IsVisible(),
            ["active"] = control.handler.gameObject.activeInHierarchy, ["hidden"] = control.isHidden, ["disabled"] = control.isDisabled,
            ["hold"] = control.requiresHold, ["selectedBefore"] = control.isSelected, ["frame"] = Time.frameCount };
        _crUi.Add(row);
        CrRequire("enabled-real-ui." + action, "Visible active enabled nonhidden native control, no hold or selection; Steam offline", row,
            machine.contentWindow.IsVisible() && control.handler.gameObject.activeInHierarchy && !control.isHidden && !control.isDisabled && !control.requiresHold && !control.isSelected && !SteamManager.Initialized);
        bool selected = control.Select();
        CrRequire("native-ui-selected." + action, true, selected, selected);
    }
    private void CrMode(CrFixture f, int mode)
    {
        f.Plate.contentWindow.Show();
        if (f.Modes.selected != mode) CrClick(f.Plate, f.Modes.Get(mode), "heating-mode-" + mode);
        CrRequire("normal-heating-mode-" + mode, mode, f.Plate.GetTagReadonly(MachineHeatingPlate.HEATING_PLATE_STATE).GetInt(),
            f.Modes.selected == mode && f.Plate.GetTagReadonly(MachineHeatingPlate.HEATING_PLATE_STATE).GetInt() == mode,
            "Only the real native radio event sets mode; the probe never writes heating flags.");
    }
    private void CrTarget(CrFixture f, int target)
    {
        int Read() => f.Plate.GetTagReadonly(MachineHeatingPlate.CURRENT_TARGET_TEMP).GetInt();
        for (int clicks = 0; Read() > 26 && clicks < 40; clicks++) CrClick(f.Plate, f.Minus, "target-minus-to26");
        CrRequire("target-native-lower-clamp", 26, Read(), Read() == 26);
        for (int clicks = 0; Read() < target && clicks < 40; clicks++) CrClick(f.Plate, f.Plus, "target-plus-to" + target);
        CrRequire("target-native-ui-setting", target, Read(), Read() == target,
            "UI thermostat setpoint is player configuration, not direct liquid temperature. Four legally installed upgrades enable the native +/- controls and sixteen-fold high-mode heating factor.");
    }
    private void CrDrain(GameItem vessel, string reason)
    {
        JObject before = VcParts(vessel); int total = WaterHelper.GetTotalVolume(vessel), uid = vessel.uniqueId; JObject money = CrMoney();
        var drain = EmporiumEntry.Instance.drain ?? throw new InvalidOperationException("Real scene drain is unavailable");
        CrRequire("native-drain-guards." + reason, new { may = true, can = true }, new { may = vessel.MayTarget(drain), can = vessel.CanTarget(drain) }, vessel.MayTarget(drain) && vessel.CanTarget(drain));
        vessel.Target(drain);
        var row = new JObject { ["reason"] = reason, ["uid"] = uid, ["discardedParts"] = total, ["componentsDiscarded"] = before,
            ["after"] = VcParts(vessel), ["temperatureObservedAfter"] = vessel.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble(),
            ["entry"] = "GameItem.MayTarget/CanTarget/Target(real EmporiumEntry drain) -> native EmptyContainer" };
        _crDiscard.Add(row);
        CrRequire("real-drained-original-vessel." + reason, "Exact pre-action liquid discarded, same owned UID/count1 and zero all36 components; cash/energy unchanged", row,
            vessel.uniqueId == uid && vessel.unitCount == 1 && GeneralHelper.IsItemOwned(vessel) && WaterHelper.GetTotalVolume(vessel) == 0 &&
            JToken.DeepEquals(VcParts(vessel), CrFixed(CrAmounts())) && JToken.DeepEquals(money, CrMoney()));
    }
    private JObject CrMoney() => new() { ["cash"] = Store.playerCash, ["energy"] = VcEnergy(), ["grid"] = GridTotal };
    private JArray CrIdentities() => new(Store.FindAllItem(false).Distinct().OrderBy(item => item.uniqueId).Select(item => new JObject
    { ["uid"] = item.uniqueId, ["id"] = item.identifier, ["count"] = item.unitCount, ["owned"] = GeneralHelper.IsItemOwned(item),
        ["parentUid"] = item.parentInventory?.GetParentItem()?.uniqueId ?? 0, ["parentStorage"] = item.parentInventory?.identifier ?? "" }));
    private void CrPlace(GameInventory inventory, GameItem item, string action) => CrRequire("legal-slot." + action,
        "Actual unchanged native SlotMarker accepts the whole item into this exact inventory", new { item.identifier, item.uniqueId }, VcTryPlace(inventory, item));
    private GameItem CrFind(int uid)
    {
        GameItem[] found = Store.FindAllItem(false).Distinct().Where(item => item.uniqueId == uid).ToArray();
        CrRequire("uid-custody." + uid, "One owned count-positive native item", found.Select(VcItem).ToArray(),
            uid > 0 && found.Length == 1 && GeneralHelper.IsItemOwned(found[0]) && found[0].unitCount > 0 && !VcDestroyed(found[0]));
        return found.Single();
    }
    private JArray CrProjection(IEnumerable<int> promised) => new(promised.OrderBy(uid => uid).Select(uid =>
    {
        GameItem item = CrFind(uid);
        return new JObject { ["uid"] = uid, ["id"] = item.identifier, ["count"] = item.unitCount, ["owned"] = GeneralHelper.IsItemOwned(item),
            ["parentUid"] = item.parentInventory?.GetParentItem()?.uniqueId ?? 0, ["parentStorage"] = item.parentInventory?.identifier ?? "",
            ["capacity"] = item.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt(), ["parts"] = VcParts(item), ["energy"] = Energy(item) };
    }));
    private JObject CrConfig(CrFixture f) => new() { ["plateUid"] = f.Plate.uniqueId,
        ["plateModules"] = new JArray(f.PlateModules.childItems.Select(item => item.uniqueId).OrderBy(uid => uid)),
        ["plateUpgradeCount"] = MachineBlender.GetUpgradeCount(f.Plate), ["mode"] = f.Plate.GetTagReadonly(MachineHeatingPlate.HEATING_PLATE_STATE).GetInt(),
        ["uiSelectedMode"] = f.Modes.selected, ["targetUiCelsius"] = f.Plate.GetTagReadonly(MachineHeatingPlate.CURRENT_TARGET_TEMP).GetInt(),
        ["plateInput"] = f.PlateInput.childItem?.uniqueId, ["blenderUid"] = f.Blender.uniqueId,
        ["blenderModules"] = new JArray(f.BlenderModules.childItems.Select(item => item.uniqueId).OrderBy(uid => uid)),
        ["blenderUpgradeCount"] = MachineBlender.GetUpgradeCount(f.Blender), ["blenderLoss"] = MachineBlender.GetLoss(f.Blender),
        ["blenderInput"] = new JArray(f.BlenderInput.childItems.Select(item => item.uniqueId)), ["blenderCupUid"] = f.BlenderCup.childItem?.uniqueId };
    private void CrValidateWitnesses(JArray rows, GameInventory expectedParent, string stage)
    {
        CrRequire("22-original-empty-witnesses." + stage, CrSpecs.Select(spec => spec.Method).ToArray(), rows,
            rows.Count == 22 && rows.Select(row => (string)row["method"]!).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(CrSpecs.Select(spec => spec.Method).OrderBy(id => id, StringComparer.Ordinal)) &&
            rows.Select(row => (int)row["uid"]!).Distinct().Count() == 22);
        foreach (JToken row in rows)
        {
            GameItem item = CrFind((int)row["uid"]!);
            CrRequire("fixed-empty-projection." + stage + "." + (string)row["method"]!, new { parts = CrFixed(CrAmounts()), count = 1, capacity = 25000 }, VcItem(item),
                JToken.DeepEquals(row["expectedParts"], CrFixed(CrAmounts())) && (int?)row["count"] == 1 && (int?)row["capacity"] == 25000 &&
                item.identifier == "25ml_cylinder" && item.unitCount == 1 && ReferenceEquals(item.parentInventory, expectedParent) &&
                item.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt() == 25000 && JToken.DeepEquals(VcParts(item), CrFixed(CrAmounts())),
                "Liquid reaction history is proven by ordinary-tick evidence and real drain receipts. This fixed zero-component witness cannot adopt a changed or missing component baseline.");
        }
    }
    private IEnumerable<object?> CrCase(string name, Func<IEnumerable<object?>> action)
    {
        int first = R.checks.Count; var row = new JObject { ["name"] = name, ["firstCheck"] = first, ["status"] = "running" };
        _crCases.Add(row); R.phase = "chemicalreactions:" + name; IEnumerator<object?>? work = null;
        try
        {
            try { work = action().GetEnumerator(); } catch (Exception error) { R.Check("chemicalreactions.case." + name, "Required native case entered", null, false, error: error.ToString()); }
            while (work != null)
            {
                bool more = false; object? current = null; Exception? failure = null;
                try { more = work.MoveNext(); if (more) current = work.Current; } catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    row["exception"] = failure.ToString();
                    if (failure is not CrStopped) R.Check("chemicalreactions.case." + name + ".exception", "Required native rule completes", null, false, error: failure.ToString());
                    break;
                }
                if (!more) break; yield return current;
            }
        }
        finally
        {
            try { work?.Dispose(); } catch (Exception error) { R.Check("chemicalreactions.case." + name + ".cleanup", "Owned cleanup completes", null, false, error: error.ToString()); }
            if (R.checks.Count == first) R.Check("chemicalreactions.case." + name + ".empty", "At least one required observation", null, false);
            row["status"] = R.checks.Skip(first).Any(check => check.status == "failed") ? "failed" : "passed"; row["lastCheckExclusive"] = R.checks.Count;
        }
    }

    private sealed class CrStep(string method, JObject before)
    {
        internal readonly string Method = method; internal readonly JObject Before = before; internal JObject After = new(); internal int Result;
        internal JObject Json() => new() { ["method"] = Method, ["before"] = Before, ["after"] = After, ["nativeResult"] = Result };
    }
    private sealed class CrTick(GameItem item)
    {
        internal readonly int Uid = item.uniqueId, Count = item.unitCount, Frame = Time.frameCount;
        internal readonly bool Owned = GeneralHelper.IsItemOwned(item);
        internal readonly JObject Before = VcParts(item); internal JObject After = new();
        internal readonly double Temperature = item.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble();
        internal readonly List<CrStep> Calls = new(); internal bool Complete, FromOrdinaryUpdate; internal string[] Callers = Array.Empty<string>();
        internal JObject Json() => new() { ["uid"] = Uid, ["count"] = Count, ["owned"] = Owned, ["frame"] = Frame,
            ["preReactionTemperature"] = Temperature, ["before"] = Before, ["after"] = After, ["complete"] = Complete,
            ["ordinaryUpdate"] = FromOrdinaryUpdate, ["callers"] = new JArray(Callers), ["steps"] = new JArray(Calls.Select(step => step.Json())) };
    }
    private sealed class CrWatch(int uid, string method, string stage)
    { internal readonly int Uid = uid; internal readonly string Method = method, Stage = stage; internal readonly List<CrTick> Ticks = new(); }
    private sealed class CrObservation : IDisposable
    {
        private static CrObservation? Active; private readonly Harmony _hooks; private CrWatch? _watch; private CrTick? _tick;
        internal readonly JArray Errors = new();
        internal CrObservation(ProbeContext context)
        {
            if (Active != null) throw new InvalidOperationException("Native chemical observation reentry");
            _hooks = new Harmony("nicokobo.content_native_probe.native_reactions." + context.O.RunId);
            try
            {
                Active = this;
                foreach (CrSpec spec in CrSpecs)
                {
                    var method = typeof(ChemicalHelper).GetMethod(spec.Method, new[] { typeof(GameItem) }) ?? throw new MissingMethodException(spec.Method);
                    _hooks.Patch(method, prefix: new HarmonyMethod(typeof(CrObservation), nameof(StepStarting)), postfix: new HarmonyMethod(typeof(CrObservation), nameof(StepCompleted)));
                }
                var root = typeof(ChemicalHelper).GetMethod(nameof(ChemicalHelper.HandleTemperatureReaction), new[] { typeof(GameItem) }) ?? throw new MissingMethodException("Native reaction root");
                _hooks.Patch(root, prefix: new HarmonyMethod(typeof(CrObservation), nameof(Starting)), postfix: new HarmonyMethod(typeof(CrObservation), nameof(Completed)));
            }
            catch { Dispose(); throw; }
        }
        internal CrWatch Start(GameItem item, string method, string stage)
        { if (_watch != null) throw new InvalidOperationException("Concurrent native reaction watches"); return _watch = new CrWatch(item.uniqueId, method, stage); }
        internal void Stop() { _watch = null; _tick = null; }
        private static void Starting(GameItem __0, out CrTick? __state)
        {
            __state = null; if (Active is not { } observer || observer._watch?.Uid != __0.uniqueId) return;
            try
            {
                if (observer._tick != null) throw new InvalidOperationException("Nested chemical reaction root");
                var tick = new CrTick(__0);
                tick.Callers = new System.Diagnostics.StackTrace().GetFrames().Select(frame => frame.GetMethod()).Where(method => method != null).Take(20)
                    .Select(method => method!.DeclaringType?.FullName + "." + method.Name).ToArray();
                tick.FromOrdinaryUpdate = tick.Callers.Any(caller => caller.Contains("EmporiumEntry", StringComparison.Ordinal) && caller.Contains("Update", StringComparison.Ordinal)) &&
                    !tick.Callers.Any(caller => caller.Contains("RunNightlyChemicalSimulation", StringComparison.Ordinal));
                observer._tick = __state = tick;
            }
            catch (Exception error) { observer.Errors.Add(error.ToString()); }
        }
        private static void Completed(GameItem __0, CrTick? __state)
        {
            if (__state == null || Active is not { } observer) return;
            try { __state.After = VcParts(__0); __state.Complete = true; observer._watch?.Ticks.Add(__state); observer._tick = null; }
            catch (Exception error) { observer.Errors.Add(error.ToString()); observer._tick = null; }
        }
        private static void StepStarting(MethodBase __originalMethod, GameItem __0, out CrStep? __state)
        {
            __state = null; if (Active is not { } observer || observer._tick?.Uid != __0.uniqueId) return;
            try { __state = new CrStep(__originalMethod.Name, VcParts(__0)); }
            catch (Exception error) { observer.Errors.Add(error.ToString()); }
        }
        private static void StepCompleted(GameItem __0, int __result, CrStep? __state)
        {
            if (__state == null || Active is not { } observer || observer._tick?.Uid != __0.uniqueId) return;
            try { __state.After = VcParts(__0); __state.Result = __result; observer._tick.Calls.Add(__state); }
            catch (Exception error) { observer.Errors.Add(error.ToString()); }
        }
        public void Dispose() { Stop(); if (ReferenceEquals(Active, this)) Active = null; _hooks.UnpatchAll(_hooks.Id); }
    }
}
