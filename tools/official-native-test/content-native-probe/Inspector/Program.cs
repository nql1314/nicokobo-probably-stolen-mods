using DseCecil;
using System.Text.Json;
using System.Xml.Linq;

internal static class Program
{
    private static readonly List<object> Checks = new();
    private static readonly List<AssemblyDefinition> Assemblies = new();
    private static int failures;
    private static void Check(string name, object expected, object observed, bool passed)
    { Checks.Add(new { name, expected, observed, passed }); if (!passed) failures++; }
    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Types(t.NestedTypes)));
    private static TypeDefinition Find(string name)
    {
        string owner = name.StartsWith("Nicokobo.ContentInterop.", StringComparison.Ordinal) ? "Nicokobo.Forge" :
            name.StartsWith("Nicokobo.FoodProcessing.", StringComparison.Ordinal) ? "Nicokobo.FoodProcessing" :
            name.StartsWith("MechcoreProtocol.", StringComparison.Ordinal) ? "MechcoreProtocol" :
            name.StartsWith("Nicokobo.AutomatedLogistics.", StringComparison.Ordinal) ? "Nicokobo.AutomatedLogistics" :
            name.StartsWith("AugMechanicalAscension.", StringComparison.Ordinal) ? "MechcoreProtocol" :
            name.StartsWith("DefinitelyStolen.", StringComparison.Ordinal) ? "DefinitelyStolen" :
            name.StartsWith("NicoItemManager.", StringComparison.Ordinal) ? "NicoItemManager" : "Assembly-CSharp";
        var types = Assemblies.Where(a => a.Name.Name == owner).SelectMany(a => Types(a.MainModule.Types)).Where(t => t.FullName == name).ToArray();
        Check("type:" + name, 1, types.Length, types.Length == 1);
        return types.SingleOrDefault();
    }
    private static void Members(string name, params string[] members)
    {
        var type = Find(name); if (type == null) return;
        foreach (string member in members)
        {
            bool method = member.EndsWith("()", StringComparison.Ordinal); string key = method ? member[..^2] : member;
            string[] matches = method ? type.Methods.Where(m => m.Name == key).Select(m => m.FullName).ToArray()
                : type.Fields.Where(f => f.Name == key).Select(f => f.FullName).Concat(type.Properties.Where(p => p.Name == key).Select(p => p.FullName)).ToArray();
            Check(name + "." + key, "Actual compiled " + (method ? "method" : "field/property"), matches, matches.Length > 0);
        }
    }
    private static int Main(string[] args)
    {
        if (args.Length == 5 && args[0] == "--probe-only") return ProbeOnly(args[1], args[2], args[3], args[4]);
        if (args.Length != 6 || args[0] != "--candidates")
        { Console.Error.WriteLine("Usage: Inspector --probe-only <probe.dll> <manifest.xml> <output.json> <Managed> OR --candidates <probe.dll> <manifest.xml> <output.json> <Managed> <Mods-directory>"); return 2; }
        string probePath = Path.GetFullPath(args[1]), manifestPath = Path.GetFullPath(args[2]), outputPath = Path.GetFullPath(args[3]);
        string managed = Path.GetFullPath(args[4]), candidateRoot = Path.GetFullPath(args[5]);
        string[] required = { "nicokobo.forge", "nicokobo.food_processing", "nicokobo.mechcore_protocol",
            "nicokobo.automated_logistics", "nicokobo.definitely_stolen", "nicokobo.item-manager", "nicokobo.speed-up", "nicokobo.stock-reroll" };
        var pathsList = new List<string> { Path.Combine(managed, "Assembly-CSharp.dll"), Path.Combine(managed, "Assembly-CSharp-firstpass.dll") };
        foreach (string id in required)
        {
            var found = Directory.EnumerateDirectories(candidateRoot).Select(directory => Path.Combine(directory, "manifest.xml"))
                .Where(File.Exists).Select(path => new { path, doc = XDocument.Load(path) })
                .Where(entry => (string)entry.doc.Root.Element("ID") == id).ToArray();
            Check("candidate-manifest:" + id, "Exactly one installed candidate", found.Select(entry => entry.path).ToArray(), found.Length == 1);
            if (found.Length != 1) continue;
            foreach (var assembly in found[0].doc.Root.Element("Assemblies").Elements("Assembly"))
                pathsList.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(found[0].path), assembly.Value)));
        }
        string[] paths = pathsList.ToArray();
        try
        {
            foreach (string path in paths) { Check("compiled:" + Path.GetFileName(path), "Exists", path, File.Exists(path)); if (File.Exists(path)) Assemblies.Add(AssemblyDefinition.ReadAssembly(path)); }
            Members("PlayerStore", "SaveGame()", "LoadGame()", "InitialSave()", "FindAllItem()", "ModData()", "currentUniqueId", "runID", "saveSlotId");
            Members("SaveFiles", "DuplicateSlot()", "Slot()", "AbsolutePath()", "IndexSave()", "LoadHistory()", "ReadStoreForPreview()");
            Members("Nicokobo.ContentInterop.MachineCatalog", "MachineIds()", "Ports()", "Recipes()");
            Members("Nicokobo.ContentInterop.RecipeView", "Id", "MachineId", "Ingredients", "OutputItemId", "OutputCount", "WaterParts");
            Members("Nicokobo.ContentInterop.IngredientView", "ItemId", "Amount", "Matches");
            Members("Nicokobo.ContentInterop.MachinePorts", "Input", "Output", "Modules", "Battery", "Water");
            Members("Nicokobo.ContentInterop.DevicePower", "TryDebit()");
            Members("Nicokobo.ContentInterop.FixedModuleAbilities", "Contains()", "CreateTooltip()");
            Members("Nicokobo.FoodProcessing.Definitions.RecipeConfiguration", "Parse()", "Load()", "DefaultJson()");
            Members("Nicokobo.FoodProcessing.Definitions.FoodDefinitions", "Recipes");
            Members("Nicokobo.FoodProcessing.FoodProduction", "TryProcess()");
            Members("MechcoreProtocol.SynthesisExpansion.ProductionCatalog", "AllRecipes");
            Members("MechcoreProtocol.SynthesisExpansion.SynthesisProduction", "TryProcess()");
            Members("MechcoreProtocol.SynthesisExpansion.SynthesisRobots", "SetScannerChance()");
            Members("MechcoreProtocol.ModuleMatrix.MatrixGrowth", "Decay()");
            Members("MechcoreProtocol.ModuleMatrix.MatrixInterop", "RarePool()");
            Members("MechcoreProtocol.ModuleMatrix.Definitions.ModuleTierDefinitions", "All");
            Members("NicoItemManager.NicoItemManagerMod", "Active", "Panel", "Catalog");
            Members("NicoItemManager.ManagerPanel", "Toggle()", "OpenEditor()", "Rebuild()", "BuildEditor()", "RefreshEditor()", "Close()", "editor", "editorRoot", "editorSpawnButtons", "previewGraphic", "previewInfo", "showAdvanced", "status");
            Members("NicoItemManager.ItemEditor", "ReadNumericTags()", "ApplyTag()", "Capture()", "Item");
            Members("NicoItemManager.ItemSpawner", "Spawn()");
            Members("NicoItemManager.ItemPreviewGraphic", "baseImage");
            Members("NicoItemManager.ItemTagDescriptions", "Find()");
            Members("NicoItemManager.ItemTagDescription", "ChineseName", "EnglishName", "ChineseHelp", "EnglishHelp", "Name", "Tooltip()");
            Members("Nicokobo.AutomatedLogistics.AutomatedLogisticsApi", "StateJson()", "ConfigureMachine()", "FillOnce()", "EjectOnce()", "ResourceRegion()", "ResourceWaterTank()", "PowerRegion()", "PowerTotal()", "DepositResource()", "WithdrawResource()", "PourResource()", "WithdrawResourceLiquid()");
            Members("Nicokobo.AutomatedLogistics.Integration.NativeHooks", "Runtime");
            Members("Nicokobo.AutomatedLogistics.Integration.AutomatedLogisticsRuntime", "UnlockAvailableNetworks()", "CollectGridEnergy()");
            Members("DefinitelyStolen.Plugin", "Run", "Panel");
            Members("DefinitelyStolen.Runtime.RunController", "State", "EnsureNight()", "BeginPractice()", "BeginTheft()", "StopTheft()", "SelectMode()", "CaptureItems()", "Browsing");
            Members("DefinitelyStolen.Domain.RunState", "OpeningItems", "OpeningCashDelta", "Qualified", "QualificationSource", "Experience", "OpeningGranted", "LastAction", "Night");
            Members("DefinitelyStolen.Integration.NativeCarry", "Snapshot()");
            Members("MechcoreProtocol.MechcoreMod", "_aug", "_synthesis", "_matrix");
            Members("AugMechanicalAscension.AugMod", "Bridge", "Story");
            Members("AugMechanicalAscension.Integration.NativeSessionBridge", "CurrentSession");
            Members("AugMechanicalAscension.Official.OfficialRunForkApi", "LastLoadedRebind");
            Members("AugMechanicalAscension.Domain.RunState", "InitPhase", "FailureReason", "Opening", "Health", "Exposure", "GameRunId", "SaveSlotId", "LastCommittedOperation");
            Members("AugMechanicalAscension.Integration.ConsumableRuntimeFlow", "_active", "ConsumeRecovery()");
            Members("AugMechanicalAscension.Domain.CyberneticProductDefinitions", "Products");
            Members("AugMechanicalAscension.Integration.NicoWorkshop", "Update()", "Claim()", "_state");
            Members("AugMechanicalAscension.Domain.WorkshopAchievements", "Definitions");
            Members("AugMechanicalAscension.Integration.StoryRuntimeFlow", "Update()", "State", "_graylineState");
            using var probe = AssemblyDefinition.ReadAssembly(probePath);
            var entries = Types(probe.MainModule.Types).Where(t => !t.IsAbstract && t.Interfaces.Any(i => i.InterfaceType.FullName == "IMod")).ToArray();
            Check("single-IMod-entry", 1, entries.Select(t => t.FullName).ToArray(), entries.Length == 1);
            var foreign = probe.MainModule.AssemblyReferences.Where(a => !Directory.EnumerateFiles(managed, "*.dll").Any(f => Path.GetFileNameWithoutExtension(f).Equals(a.Name, StringComparison.OrdinalIgnoreCase))).Select(a => a.FullName).ToArray();
            Check("game-only-static-references", Array.Empty<string>(), foreign, foreign.Length == 0);
            var manifest = XDocument.Load(manifestPath);
            var declared = manifest.Root.Element("Capabilities").Elements("Capability").Select(e => e.Value).ToHashSet(StringComparer.Ordinal);
            ModLoader.UseManagedDirectory(managed); Directory.SetCurrentDirectory(managed);
#if STEAM_SCANNER_V2
            var scan = ModScanner.Scan(new[] { probePath }, new[] { probe.Name.Name }, Path.GetDirectoryName(probePath), true);
#else
            var scan = ModScanner.Scan(new[] { probePath }, new[] { probe.Name.Name });
#endif
            Check("official-source-linked-scanner", "No Error, HardFlags or undeclared capability", new { scan.MethodsScanned, scan.Error, hardFlags = scan.HardFlags.Select(f => new { f.Kind, f.Location, f.Target }).ToArray(), scan.Capabilities, undeclared = scan.Capabilities.Where(c => !declared.Contains(c)).ToArray() },
                scan.Error == null && scan.HardFlags.Count == 0 && scan.Capabilities.All(declared.Contains));
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new { passed = failures == 0, scope = "Compiled metadata and source-linked scanner only; no game/candidate assembly execution; candidates are read from the supplied installed Mods directory; this is not game execution", candidatePreflightPaths = paths, checks = Checks }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")}: {Checks.Count} static checks, failures={failures}; scanner methods={scan.MethodsScanned}; capabilities={string.Join(",", scan.Capabilities)}; hardFlags={scan.HardFlags.Count}");
            foreach (var item in Checks) if ((bool)item.GetType().GetProperty("passed").GetValue(item) == false) Console.WriteLine(JsonSerializer.Serialize(item));
            return failures == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { foreach (var a in Assemblies) a.Dispose(); }
    }
    private static int ProbeOnly(string probePath, string manifestPath, string outputPath, string managed)
    {
        try
        {
            managed = Path.GetFullPath(managed);
            probePath = Path.GetFullPath(probePath); manifestPath = Path.GetFullPath(manifestPath); outputPath = Path.GetFullPath(outputPath);
            using var probe = AssemblyDefinition.ReadAssembly(probePath);
            var entries = Types(probe.MainModule.Types).Where(t => !t.IsAbstract && t.Interfaces.Any(i => i.InterfaceType.FullName == "IMod")).ToArray();
            Check("single-IMod-entry", 1, entries.Select(t => t.FullName).ToArray(), entries.Length == 1);
            ModLoader.UseManagedDirectory(managed);
            string[] foreign = probe.MainModule.AssemblyReferences.Where(a => !ModLoader.IsGameAssemblyName(a.Name)).Select(a => a.FullName).ToArray();
            Check("game-only-static-references", Array.Empty<string>(), foreign, foreign.Length == 0);
            var manifest = XDocument.Load(manifestPath);
            string[] listed = manifest.Root.Element("Assemblies").Elements("Assembly").Select(e => e.Value).ToArray();
            Check("single-listed-probe-dll", new[] { probe.Name.Name + ".dll" }, listed, listed.SequenceEqual(new[] { probe.Name.Name + ".dll" }));
            var declared = manifest.Root.Element("Capabilities").Elements("Capability").Select(e => e.Value).ToHashSet(StringComparer.Ordinal);
            Directory.SetCurrentDirectory(managed);
#if STEAM_SCANNER_V2
            var scan = ModScanner.Scan(new[] { Path.GetFullPath(probePath) }, new[] { probe.Name.Name }, Path.GetDirectoryName(Path.GetFullPath(probePath)), true);
#else
            var scan = ModScanner.Scan(new[] { Path.GetFullPath(probePath) }, new[] { probe.Name.Name });
#endif
            Check("official-source-linked-probe-scanner", "No Error, HardFlags or undeclared capability", new { scan.MethodsScanned, scan.Error, hardFlags = scan.HardFlags.Select(f => new { f.Kind, f.Location, f.Target }).ToArray(), scan.Capabilities, undeclared = scan.Capabilities.Where(c => !declared.Contains(c)).ToArray() },
                scan.Error == null && scan.HardFlags.Count == 0 && scan.Capabilities.All(declared.Contains));
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new { passed = failures == 0,
                artifactPath = probePath, manifestPath, managedDirectory = managed,
                scope = "Current probe only: compiled entry/reference/package checks and the explicitly supplied game-code official scanner. No candidate DLL/API preflight in this mode; no game/candidate execution.",
                checks = Checks }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")}: probe-only checks={Checks.Count}, failures={failures}; scanner methods={scan.MethodsScanned}; capabilities={string.Join(",", scan.Capabilities)}; hardFlags={scan.HardFlags.Count}");
            return failures == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

public static class ModLoader
{
    private static HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
    public static void UseManagedDirectory(string path) => names = Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public static bool IsGameAssemblyName(string name) => names.Contains(name);
}

public static class LocHelper { public static string GetLocalizedUIOrDefault(string key, string fallback) => fallback; }
