using System.Reflection;
using DseCecil;
using MechcoreProtocol.SynthesisExpansion.Definitions;

internal static class PatchChecks
{
    internal static void Run(AssemblyDefinition candidate, AssemblyDefinition game, Action<bool, string> check)
    {
        IEnumerable<MethodDefinition> Named(TypeDefinition type, string name)
        {
            while (true)
            {
                foreach (var method in type.Methods.Where(method => method.Name == name)) yield return method;
                type = game.MainModule.Types.FirstOrDefault(parent => parent.FullName == type.BaseType?.FullName)!;
                if (type == null) yield break;
            }
        }
        string TypeName(Type type) => type.IsGenericType
            ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">"
            : type.FullName!;
        MethodBase[] ResolveTargets(string patchName)
        {
            Assembly? ResolveGameDependency(object? sender, ResolveEventArgs request)
            {
                string path = Path.Combine(Path.GetDirectoryName(game.MainModule.FileName)!, new AssemblyName(request.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            }
            AppDomain.CurrentDomain.AssemblyResolve += ResolveGameDependency;
            try
            {
                // Only execute the compiled reflection selectors. Do not initialize
                // the Mod or install its patches; resolve against the real game types.
                var type = Assembly.LoadFrom(candidate.MainModule.FileName).GetType(patchName, throwOnError: true)!;
                var enumerate = type.GetMethod("TargetMethods", BindingFlags.NonPublic | BindingFlags.Static)!;
                return ((IEnumerable<MethodBase>)enumerate.Invoke(null, null)!).ToArray();
            }
            finally { AppDomain.CurrentDomain.AssemblyResolve -= ResolveGameDependency; }
        }
        void Target(string patch, string declaring, string name, string[]? parameters)
        {
            var type = game.MainModule.Types.SingleOrDefault(native => native.FullName == declaring)
                ?? throw new InvalidOperationException("Native patch declaring type missing: " + declaring + " (" + patch + ")");
            var named = Named(type, name).ToArray();
            // AccessTools.Method resolves the full CLR signature; optional arguments
            // can be omitted at a C# call site, but not from its Type[] lookup.
            var matches = parameters == null ? named : named.Where(method =>
                method.Parameters.Select(param => param.ParameterType.FullName).SequenceEqual(parameters)).ToArray();
            // CLR lookup selects the nearest override; its abstract base is not
            // a second ambiguous target for a declared concrete graph method.
            if (matches.Length > 1)
                matches = matches.Where(method => method.DeclaringType == matches[0].DeclaringType).ToArray();
            check(matches.Length == 1, $"Missing or ambiguous native Harmony target: {patch} -> {declaring}.{name} ({string.Join(",", parameters ?? Array.Empty<string>())})");
            var target = matches.Single();
            var patchType = candidate.MainModule.Types.Single(type => type.Name == patch);
            foreach (var injection in patchType.Methods.Where(method => method.Name is "Prefix" or "Postfix"))
            foreach (var parameter in injection.Parameters)
            {
                if (!parameter.Name.StartsWith("__") || parameter.Name is "__instance" or "__state") continue;
                if (parameter.Name == "__result")
                    check(parameter.ParameterType.FullName.TrimEnd('&') == target.ReturnType.FullName,
                        $"Patch result type differs: {patch} -> {target.FullName}");
                else if (int.TryParse(parameter.Name.Substring(2), out int index))
                    check(index < target.Parameters.Count && parameter.ParameterType.FullName.TrimEnd('&') == target.Parameters[index].ParameterType.FullName.TrimEnd('&'),
                        $"Patch argument type differs: {patch}/{parameter.Name}");
            }
        }
        int targets = 0;
        foreach (var patch in candidate.MainModule.Types)
        foreach (var attribute in patch.CustomAttributes.Where(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
        {
            if (attribute.ConstructorArguments.Count == 0)
            {
                foreach (var method in ResolveTargets(patch.FullName))
                {
                    check(method != null, $"Native Harmony target resolved to null: {patch.Name}");
                    Target(patch.Name, method!.DeclaringType!.FullName!, method.Name, method.GetParameters().Select(param => TypeName(param.ParameterType)).ToArray());
                    targets++;
                }
                continue;
            }
            string declaring = "", name = ""; string[]? parameters = null;
            foreach (var arg in attribute.ConstructorArguments)
            {
                if (arg.Type.FullName == "System.Type") declaring = ((TypeReference)arg.Value).FullName;
                else if (arg.Type.FullName == "System.String") name = (string)arg.Value;
                else if (arg.Type.FullName == "System.Type[]")
                    parameters = ((CustomAttributeArgument[])arg.Value).Select(arg => ((TypeReference)arg.Value).FullName).ToArray();
                else if (arg.Type.FullName == "HarmonyLib.ArgumentType[]")
                {
                    var modes = (CustomAttributeArgument[])arg.Value;
                    for (int i = 0; i < modes.Length; i++) if (Convert.ToInt32(modes[i].Value) is 1 or 2) parameters![i] += "&";
                }
            }
            Target(patch.Name, declaring, name, parameters); targets++;
        }
        check(targets >= 30, "Patch metadata coverage unexpectedly dropped");

        // The wrapper and the player's actual search must share the patched out overload.
        var scav = game.MainModule.Types.Single(type => type.Name == "ScavHelper");
        var wrapper = scav.Methods.Single(method => method.Name == "GetRandomScavengedItem" && method.Parameters.Count == 0);
        check(wrapper.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call &&
            call.DeclaringType.Name == "ScavHelper" && call.Name == "GetRandomScavengedItem" &&
            call.Parameters.Count == 1 && call.Parameters[0].ParameterType.FullName == "GameItem&"), "Robot scavenging no longer reaches the native shared out overload");
        var search = scav.Methods.Single(method => method.Name == "ScavengeDumpingGrounds");
        check(search.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference call && call.DeclaringType.Name == "ScavHelper" && call.Name == "GetRandomScavengedItem" && call.Parameters.Count == 1),
            "Manual scavenging no longer reaches the patched out overload");

        foreach (string sprite in new[] { "scavenging_robot_broken", "mining_drill_upgraded" })
            check(candidate.MainModule.Types.Single(type => type.Name == "RobotAppearance").Methods.Any(method => method.HasBody &&
                method.Body.Instructions.Any(instruction => Equals(instruction.Operand, sprite))), "State sprite not used: " + sprite);
    }
}
