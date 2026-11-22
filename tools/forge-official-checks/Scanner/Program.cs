using System.Xml.Linq;

if (args.Length < 2) throw new ArgumentException("Expected Managed directory and Mod source directories");
ModLoader.GameAssemblies.UnionWith(Directory.EnumerateFiles(args[0], "*.dll").Select(Path.GetFileNameWithoutExtension));
foreach (string source in args.Skip(1))
{
    var manifest = XDocument.Load(Path.Combine(source, "manifest.xml")).Root;
    var dlls = manifest.Element("Assemblies").Elements("Assembly").Select(entry => Path.Combine(source, "bin", "Release", "netstandard2.1", entry.Value)).ToArray();
    var result = ModScanner.Scan(dlls);
    var declared = manifest.Element("Capabilities").Elements("Capability").Select(entry => entry.Value).ToHashSet();
    Console.WriteLine($"{manifest.Element("ID").Value}: detected=[{string.Join(",", result.Capabilities)}], hard={result.HardFlags.Count}, methods={result.MethodsScanned}");
    if (result.Refused || !result.Capabilities.IsSubsetOf(declared)) throw new InvalidOperationException(result.Error ?? result.FormatFindings(100));
}

// Game/Unity boundaries only. The scanner source and its policy are unchanged;
// this is the same assembly-name set the loader derives from Managed.
public static class ModLoader { public static readonly HashSet<string> GameAssemblies = new(StringComparer.OrdinalIgnoreCase); public static bool IsGameAssemblyName(string name) => GameAssemblies.Contains(name); }
public static class LocHelper { public static string GetLocalizedUIOrDefault(string key, string fallback) => fallback; }
namespace UnityEngine { public class Object { } }
