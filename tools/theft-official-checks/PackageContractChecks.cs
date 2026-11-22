using System.Xml.Linq;
using Mono.Cecil;
using DefinitelyStolen.Domain;
using DefinitelyStolen.UI;

internal static class PackageContractChecks
{
    internal static void Run(string[] args, Action<bool, string> check)
    {
        if (args.Length != 2) throw new ArgumentException("Expected source directory and compiled DLL paths");
        string source = Path.GetFullPath(args[0]), binary = Path.GetFullPath(args[1]);
        var manifest = XDocument.Load(Path.Combine(source, "manifest.xml")).Root!;
        check(manifest.Name == "Mod" && manifest.Element("ID")?.Value == Identity.Owner, "Native manifest root and stable owner match");
        check(manifest.Element("Assemblies")!.Elements("Assembly").Select(x => x.Value).SequenceEqual(new[] { "DefinitelyStolen.dll" }), "Only the content entry is bundled; Forge is a declared standalone prerequisite");
        check(manifest.Element("Prerequisites")!.Elements("Mod").Any(x => x.Value == "nicokobo.forge"), "Standalone Forge prerequisite is declared");
        check(manifest.Element("Prerequisites")!.Elements("Mod").Any(x => x.Value == "nicokobo.forge"), "Standalone Forge prerequisite is declared");
        using var module = ModuleDefinition.ReadModule(binary);
        check(module.Types.Count(x => x.Interfaces.Any(i => i.InterfaceType.FullName == "IMod")) == 1, "The package has exactly one official IMod entry");
        check(module.AssemblyReferences.All(x => !x.Name.Contains("Il2Cpp") && !x.Name.Contains("Melon") && x.Name != "Nicokobo.Forge" && x.Name != "System.Text.Json"), "No Demo, Forge or incompatible JSON dependency remains");
        var calls = module.Types.SelectMany(AllTypes).SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand).OfType<MethodReference>().ToArray();
        check(!calls.Any(m => m.DeclaringType.FullName == "HarmonyLib.Harmony" && m.Name == "PatchAll"), "Entry does not repeat the loader's PatchAll");
        check(!calls.Any(m => m.Name == "SaveGame" && m.DeclaringType.Name == "PlayerStore"), "Theft gameplay never triggers an extra native save");
        using var game = ModuleDefinition.ReadModule(@"F:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed\Assembly-CSharp.dll");
        var patchTypes = module.Types.Where(x => x.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch")).ToArray();
        check(patchTypes.Length == 20, "Every required precise patch is a loader-discoverable patch class");
        foreach (var type in patchTypes)
        {
            var attr = type.CustomAttributes.Single(x => x.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
            string targetType = ((TypeReference)attr.ConstructorArguments[0].Value).FullName;
            string targetName = (string)attr.ConstructorArguments[1].Value;
            var candidates = game.Types.Single(t => t.FullName == targetType).Methods.Where(m => m.Name == targetName);
            if (attr.ConstructorArguments.Count == 3)
            {
                var parameters = ((CustomAttributeArgument[])attr.ConstructorArguments[2].Value).Select(a => ((TypeReference)a.Value).FullName).ToArray();
                candidates = candidates.Where(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters));
            }
            check(candidates.Count() == 1, "Exact native target resolves: " + targetType + "." + targetName);
        }
        var catalog = Catalog.Load();
        var portraits = catalog.Enabled.SelectMany(role => Enumerable.Range(0, 3).Select(variant => SceneLayout.PortraitName(role.Id, variant))).Concat(SpecialNpcs.All.Select(x => x.Portrait)).Distinct().ToArray();
        check(portraits.Length == 101 && portraits.All(x => File.Exists(Path.Combine(source, "Textures", "Paper", x + ".png"))), "All 101 occupation and named-character portraits ship in official resources");
        check(Enumerable.Range(0, 3).SelectMany(region => Enumerable.Range(0, 3).Select(variant => SceneLayout.SceneName(region, variant))).All(x => File.Exists(Path.Combine(source, "Textures", "Paper", x + ".jpg"))), "All nine authored regional backgrounds ship");
        check(File.Exists(Path.Combine(source, "Textures", "Items", "escape-smoke.png")), "The smoke grenade retains its dedicated inventory icon");
    }
    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition root)
    { yield return root; foreach (var nested in root.NestedTypes.SelectMany(AllTypes)) yield return nested; }
}
