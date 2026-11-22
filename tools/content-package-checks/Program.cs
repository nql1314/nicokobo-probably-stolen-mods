using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using DseCecil;

// Forge and the four current content packages. For each manifest, the
// scanner receives its actual compiled main DLL and the listed sibling DLLs.
// Mod assemblies are read by the native scanner as bytes, never executed.
internal static class Program
{
    private static readonly HashSet<string> ExpectedIds = new(StringComparer.Ordinal)
    {
        "nicokobo.food_processing",
        "nicokobo.mechcore_protocol",
        "nicokobo.automated_logistics",
        "nicokobo.definitely_stolen",
        "nicokobo.forge"
    };
    private static readonly Dictionary<string, string> DependencyHashes = new(StringComparer.Ordinal);

    private static int Main(string[] args)
    {
        bool single = args.Length != 0 && args[0] == "--single";
        if (single) args = args.Skip(1).ToArray();
        if (args.Length != 1 + (single ? 1 : ExpectedIds.Count) * 2)
        {
            Console.Error.WriteLine("Usage: ContentPackage.Checks [--single] <game-Managed-dir> (<manifest> <main.dll> for each package; all five unless --single)");
            return 2;
        }
        try
        {
            args = args.Select(Path.GetFullPath).ToArray();
            string managed = args[0];
            ModLoader.UseManagedDirectory(managed);
            // In the game UnityEngine.Object lives in Managed. The headless
            // host copies that reference into its bin, so Cecil also needs the
            // actual Managed directory to resolve enum/attribute dependencies.
            Directory.SetCurrentDirectory(managed);
            Console.WriteLine("Scanner: source linked from GameCodeDir. Environment: supplied Managed DLL names and Cecil dependency search directory.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            DependencyHashes.Clear();
            bool passed = true;
            for (int index = 1; index < args.Length; index += 2)
                passed &= Check(args[index], args[index + 1], seen);
            if (!single && !seen.SetEquals(ExpectedIds)) throw new ArgumentException("Input must contain each of the five current content/Forge packages exactly once");
            Console.WriteLine(passed
                ? $"PASS: {(single ? "1 package preflight" : "all 5 compiled packages")} with one IMod entry, consistent shared DLLs and no scanner Error, HardFlags or undeclared capabilities. No game launch or trust changes."
                : "FAIL: at least one compiled package would be refused by scanner or capability declaration checks.");
            return passed ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
    }

    private static bool Check(string manifestPath, string mainDllPath, HashSet<string> seen)
    {
        string manifest = Path.GetFullPath(manifestPath), mainDll = Path.GetFullPath(mainDllPath);
        var root = XDocument.Load(manifest).Root;
        if (root?.Name.LocalName != "Mod") throw new ArgumentException("Invalid Mod manifest: " + manifest);
        string id = root.Element("ID")?.Value.Trim();
        if (id == null || !ExpectedIds.Contains(id) || !seen.Add(id)) throw new ArgumentException("Unexpected or duplicate package ID: " + id);
        var capabilities = root.Element("Capabilities") ?? throw new ArgumentException("Missing Capabilities element: " + id);
        var declared = capabilities.Elements("Capability").Select(element => element.Value.Trim().ToLowerInvariant())
            .Where(value => value.Length != 0).ToHashSet(StringComparer.Ordinal);
        var assemblyNames = root.Element("Assemblies")?.Elements("Assembly").Select(element => element.Value.Trim()).ToArray()
            ?? throw new ArgumentException("Missing Assemblies element: " + id);
        string directory = Path.GetDirectoryName(mainDll);
        if (assemblyNames.Length != 1) throw new ArgumentException("Standalone Forge and content packages must each bundle only their own entry DLL: " + id);
        if (id != "nicokobo.forge" && root.Element("Prerequisites")?.Elements("Mod").Any(element => element.Value == "nicokobo.forge") != true)
            throw new ArgumentException("Content package is missing its Forge prerequisite: " + id);
        var dlls = assemblyNames.Select(name => Path.GetFullPath(Path.Combine(directory, name))).ToArray();
        if (!dlls.Contains(mainDll, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Main DLL is not listed in manifest: " + id);
        if (assemblyNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != assemblyNames.Length)
            throw new ArgumentException("Duplicate declared assembly: " + id);
        string boundary = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        int entryCount = 0;
        foreach (string dll in dlls)
        {
            if (!dll.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(dll) != ".dll")
                throw new ArgumentException("Declared assembly escapes compiled package: " + dll);
            if (!File.Exists(dll)) throw new FileNotFoundException("Compiled manifest assembly missing", dll);
            var identity = AssemblyName.GetAssemblyName(dll);
            string assemblyName = identity.Name;
            if (ModLoader.IsGameAssemblyName(assemblyName)) throw new ArgumentException("Package shadows game assembly: " + dll);
            if (assemblyName != Path.GetFileNameWithoutExtension(dll)) throw new ArgumentException("DLL filename differs from assembly identity: " + dll);
            using var assembly = AssemblyDefinition.ReadAssembly(dll);
            if (assembly.MainModule.AssemblyReferences.Any(reference => reference.Name is "Nicokobo.ContentInterop" or "Nicokobo.Forge"))
                throw new ArgumentException("A static cross-Mod dependency remains: " + id);
            var entries = Types(assembly.MainModule.Types).Where(type => !type.IsAbstract &&
                type.Interfaces.Any(contract => contract.InterfaceType.FullName == "IMod")).ToArray();
            entryCount += entries.Length;
            if (entries.Length != 0 && !dll.Equals(mainDll, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Dependency DLL adds another IMod entry: " + dll);
            if (dll.Equals(mainDll, StringComparison.OrdinalIgnoreCase))
            {
                string version = root.Element("ModVersion")?.Value.Trim();
                if (!Version.TryParse(version, out var packageVersion)) throw new ArgumentException("Invalid ModVersion: " + id);
                var normalized = new Version(packageVersion.Major, packageVersion.Minor, Math.Max(0, packageVersion.Build), Math.Max(0, packageVersion.Revision));
                if (identity.Version != normalized) throw new ArgumentException("Main assembly version differs from manifest: " + id);
            }
            else
            {
                string fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)));
                if (DependencyHashes.TryGetValue(assemblyName, out string previous) && previous != fingerprint)
                    throw new ArgumentException("Shared dependency bytes differ across packages: " + assemblyName);
                DependencyHashes[assemblyName] = fingerprint;
            }
        }
        if (entryCount != 1) throw new ArgumentException("Package must have exactly one IMod entry: " + id);
        var scan = ModScanner.Scan(dlls, assemblyNames.Select(Path.GetFileNameWithoutExtension));
        var undeclared = scan.Capabilities.Where(capability => !declared.Contains(capability)).OrderBy(value => value).ToArray();
        Console.WriteLine($"{id}: methods={scan.MethodsScanned}; detected=[{string.Join(", ", scan.Capabilities.OrderBy(value => value))}]; declared=[{string.Join(", ", declared.OrderBy(value => value))}]; hardFlags={scan.HardFlags.Count}; undeclared={undeclared.Length}");
        foreach (var finding in scan.Findings) Console.WriteLine("  " + finding);
        if (scan.Error != null) Console.Error.WriteLine("  scanner error: " + scan.Error);
        foreach (var finding in scan.HardFlags) Console.Error.WriteLine("  " + finding);
        foreach (string capability in undeclared)
        {
            Console.Error.WriteLine("  undeclared: " + capability);
        }
        return scan.Error == null && scan.HardFlags.Count == 0 && undeclared.Length == 0;
    }

    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }
}
