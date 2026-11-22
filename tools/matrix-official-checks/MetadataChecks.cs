using DseCecil;
using DseCecil.Cil;

internal static class MetadataChecks
{
    internal static void Run(AssemblyDefinition candidate, AssemblyDefinition game, Action<bool,string> check)
    {
        check(!candidate.MainModule.AssemblyReferences.Any(reference => reference.Name.Contains("Il2Cpp") ||
            reference.Name.Contains("Melon") || reference.Name.Contains("Forge")), "Old loader dependency remains");
        check(candidate.MainModule.Types.Count(type=>type.Interfaces.Any(entry=>entry.InterfaceType.FullName=="IMod"))==1,"Official entry point mismatch");
        // The candidate now also contains Synthesis and Aug patches. This suite
        // checks Matrix's own native targets; their suites cover the other modules.
        var matrixTypes = candidate.MainModule.Types.Where(type => type.Namespace.StartsWith("MechcoreProtocol.ModuleMatrix", StringComparison.Ordinal)).ToArray();
        foreach(var patch in matrixTypes)
        foreach(var attribute in patch.CustomAttributes.Where(attribute=>attribute.AttributeType.FullName=="HarmonyLib.HarmonyPatch"))
        {
            string declaring="", name=""; string[]? parameters=null;
            foreach(var arg in attribute.ConstructorArguments)
            {
                if(arg.Type.FullName=="System.Type") declaring=((TypeReference)arg.Value).FullName;
                else if(arg.Type.FullName=="System.String") name=(string)arg.Value;
                else if(arg.Type.FullName=="System.Type[]") parameters=((CustomAttributeArgument[])arg.Value).Select(value=>((TypeReference)value.Value).FullName).ToArray();
            }
            var type=game.MainModule.Types.Single(type=>type.FullName==declaring);
            var targets=type.Methods.Where(method=>method.Name==name && (parameters==null ||
                method.Parameters.Select(parameter=>parameter.ParameterType.FullName).SequenceEqual(parameters))).ToArray();
            check(targets.Length==1,"Missing/ambiguous Harmony target: "+patch.Name);
            var target=targets.Single();
            foreach(var injection in patch.Methods.Where(method=>method.Name is "Prefix" or "Postfix" or "Finalizer"))
            foreach(var arg in injection.Parameters)
            {
                if(arg.Name=="__result") check(arg.ParameterType.FullName.TrimEnd('&')==target.ReturnType.FullName,"Patch result mismatch: "+patch.Name);
                else if(arg.Name.StartsWith("__") && int.TryParse(arg.Name.Substring(2),out int index))
                    check(index<target.Parameters.Count && arg.ParameterType.FullName.TrimEnd('&')==target.Parameters[index].ParameterType.FullName.TrimEnd('&'),
                        "Patch argument mismatch: "+patch.Name+"/"+arg.Name);
            }
        }
        var effects=game.MainModule.Types.Single(type=>type.Name=="ModuleEffectHelper");
        check(!matrixTypes.SelectMany(type=>type.Methods).Any(method=>method.Name=="Transpiler"),
            "Matrix must not rewrite the native random generator's IL");
        var work=effects.Methods.Single(method=>method.Name=="ActivateOnUseAction");
        check(work.Body.Instructions.Any(instruction=>instruction.Operand is MethodReference call && call.Name=="ComputeAllModuleTempStat"),
            "Module action no longer refreshes temporary stats after continuous growth");
        var store=game.MainModule.Types.Single(type=>type.Name=="PlayerStore");
        var night=store.Methods.Single(method=>method.Name=="EndNight").Body.Instructions;
        int cycle=night.ToList().FindLastIndex(instruction=>instruction.OpCode.Code==Code.Callvirt && instruction.Operand is MethodReference call && call.Name=="ForEach");
        int late=night.ToList().FindIndex(instruction=>instruction.Operand is MethodReference call && call.Name=="FireOnGoingSleepLate");
        check(cycle>=0 && late>cycle,"Sleep-late decay hook moved before native machine cycles");
    }
}
