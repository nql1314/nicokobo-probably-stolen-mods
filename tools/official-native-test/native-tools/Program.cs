using System.Security.Cryptography;
using System.Text.Json;
using DseCecil;
using DseCecil.Cil;

try
{
if (args.Length != 8 || args[0] != "isolate-steam")
    throw new ArgumentException("Usage: isolate-steam <original DLL> <fresh clone DLL> <isolated persistent> <product> <company> <new run root> <formal persistent>");
string original = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
string expectedPersistent = Path.GetFullPath(args[3]), expectedProduct = args[4];
string expectedCompany = args[5], workspace = Path.GetFullPath(args[6]);
string formalPersistent = Path.GetFullPath(args[7]);
if (!System.Text.RegularExpressions.Regex.IsMatch(expectedProduct, "^NicoT_[0-9a-f]{9}$"))
    throw new ArgumentException("Use a unique equal-length NicoT_ test product name");
if (expectedPersistent != Path.Combine(Path.GetDirectoryName(formalPersistent)!, expectedProduct))
    throw new IOException("Unexpected isolated persistent directory");
if (File.Exists(output)) throw new IOException("Isolation output must be new");
if (original.Equals(output, StringComparison.OrdinalIgnoreCase)) throw new IOException("Never replace the installed game");
// The caller stages into its task workspace. The input game is always read-only.
if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(workspace), "^native-[0-9a-f]{32}$") ||
    !File.Exists(Path.Combine(workspace, "ownership.json")) ||
    !output.Equals(Path.Combine(workspace, "game", "Probably Stolen_Data", "Managed", "Assembly-CSharp.dll"), StringComparison.OrdinalIgnoreCase) ||
    original.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(expectedCompany))
    throw new IOException("Isolation output must belong to this newly owned run; original must remain outside it");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(original)!);
using var module = ModuleDefinition.ReadModule(original, new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
var before = Methods(module).ToDictionary(method => method.FullName, Body, StringComparer.Ordinal);
var steam = module.Types.Single(type => type.FullName == "SteamManager");
var awake = steam.Methods.Single(method => method.Name == "Awake" && method.Parameters.Count == 0);
if (!awake.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.FullName.Contains("RestartAppIfNecessary")) ||
    !awake.Body.Instructions.Any(instruction => instruction.Operand is MethodReference method && method.FullName.Contains("SteamAPI::Init")))
    throw new InvalidOperationException("The verified Steam startup baseline changed");
awake.Body = new MethodBody(awake);
var il = awake.Body.GetILProcessor();
var unityScope = module.AssemblyReferences.Single(reference => reference.Name == "UnityEngine.CoreModule");
var appType = new TypeReference("UnityEngine", "Application", module, unityScope);
var debugType = new TypeReference("UnityEngine", "Debug", module, unityScope);
var getPath = new MethodReference("get_persistentDataPath", module.TypeSystem.String, appType);
var getProduct = new MethodReference("get_productName", module.TypeSystem.String, appType);
var getCompany = new MethodReference("get_companyName", module.TypeSystem.String, appType);
var log = new MethodReference("Log", module.TypeSystem.Void, debugType);
log.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
var concat = new MethodReference("Concat", module.TypeSystem.String, module.TypeSystem.String);
concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
var ordinal = new TypeReference("System", "StringComparison", module, module.TypeSystem.Object.Scope, true);
var equals = new MethodReference("Equals", module.TypeSystem.Boolean, module.TypeSystem.String);
equals.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
equals.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
equals.Parameters.Add(new ParameterDefinition(ordinal));
var environment = new TypeReference("System", "Environment", module, module.TypeSystem.Object.Scope);
var exit = new MethodReference("Exit", module.TypeSystem.Void, environment);
exit.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
foreach (var (label, getter) in new[] { ("persistentDataPath", getPath), ("productName", getProduct), ("companyName", getCompany) })
{
    il.Append(il.Create(OpCodes.Ldstr, "[NativeIsolation] " + label + "="));
    il.Append(il.Create(OpCodes.Call, getter));
    il.Append(il.Create(OpCodes.Call, concat));
    il.Append(il.Create(OpCodes.Call, log));
}
var valid = il.Create(OpCodes.Nop);
var rejected = il.Create(OpCodes.Ldstr, "[NativeIsolation] REFUSED: unexpected persistent path or PlayerPrefs identity");
// Unity reports Windows persistentDataPath with forward slashes; retain the
// canonical filesystem path in evidence, but emit Unity's separator form here.
foreach (var (getter, expected, comparison) in new[] { (getPath, expectedPersistent.Replace('\\', '/'), 5), (getProduct, expectedProduct, 4), (getCompany, expectedCompany, 4) })
{
    il.Append(il.Create(OpCodes.Call, getter));
    il.Append(il.Create(OpCodes.Ldstr, expected));
    il.Append(il.Create(OpCodes.Ldc_I4, comparison));
    il.Append(il.Create(OpCodes.Call, equals));
    il.Append(il.Create(OpCodes.Brfalse, rejected));
}
il.Append(il.Create(OpCodes.Br, valid));
il.Append(rejected);
il.Append(il.Create(OpCodes.Call, log));
il.Append(il.Create(OpCodes.Ldc_I4, 86));
il.Append(il.Create(OpCodes.Call, exit));
il.Append(valid);
il.Append(il.Create(OpCodes.Ldarg_0));
il.Append(il.Create(OpCodes.Stsfld, steam.Fields.Single(field => field.Name == "s_instance")));
il.Append(il.Create(OpCodes.Ldarg_0));
il.Append(il.Create(OpCodes.Ldc_I4_0));
il.Append(il.Create(OpCodes.Stfld, steam.Fields.Single(field => field.Name == "m_bInitialized")));
il.Append(il.Create(OpCodes.Ret));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
module.Write(output);
using var afterModule = ModuleDefinition.ReadModule(output, new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
var after = Methods(afterModule).ToDictionary(method => method.FullName, Body, StringComparer.Ordinal);
if (!before.Keys.ToHashSet().SetEquals(after.Keys)) throw new InvalidOperationException("Method inventory changed");
var changed = before.Keys.Where(key => before[key] != after[key]).ToArray();
if (changed.Length != 1 || changed[0] != awake.FullName)
    throw new InvalidOperationException("Isolation changed unexpected method bodies: " + string.Join(", ", changed));
var protectedPrefixes = new[] { "ModLoader", "ModScanner", "ModTrust", "ModUIManager", "ModFingerprint", "PlayerStore", "SaveFiles" };
var protectedMethods = Methods(module).Where(method => protectedPrefixes.Any(prefix => method.DeclaringType.FullName.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
if (protectedMethods.Any(method => before[method.FullName] != after[method.FullName])) throw new InvalidOperationException("Native loading/save rules changed");
var evidence = new
{
    createdAtUtc = DateTime.UtcNow.ToString("o"), original, output,
    expectedPersistent, expectedProduct, expectedCompany, runRoot = workspace,
    originalSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original))),
    isolatedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
    changedMethods = changed, protectedMethodsUnchanged = protectedMethods.Length,
    purpose = "Test-only Steam startup isolation. Mod loading, scanning, trust, activation and save method bodies are unchanged."
};
File.WriteAllText(output + ".isolation.json", JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(evidence));
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
return 0;

static IEnumerable<MethodDefinition> Methods(ModuleDefinition module)
{
    return Types(module.Types).SelectMany(type => type.Methods);
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }
}
static string Body(MethodDefinition method)
{
    if (!method.HasBody) return "<none>";
    var instructions = method.Body.Instructions;
    string Operand(object? operand) => operand switch
    {
        null => "", Instruction target => "@" + instructions.IndexOf(target),
        Instruction[] targets => string.Join(",", targets.Select(target => "@" + instructions.IndexOf(target))),
        VariableDefinition variable => "v" + variable.Index, ParameterDefinition parameter => "p" + parameter.Index,
        IMetadataTokenProvider metadata => metadata.ToString() ?? "", _ => Convert.ToString(operand, System.Globalization.CultureInfo.InvariantCulture) ?? ""
    };
    return string.Join(";", method.Body.Variables.Select(variable => variable.VariableType.FullName)) + "|" +
        string.Join(";", instructions.Select(instruction => instruction.OpCode.Code + ":" + Operand(instruction.Operand))) + "|" +
        string.Join(";", method.Body.ExceptionHandlers.Select(handler => handler.HandlerType + ":" +
            Operand(handler.TryStart) + ":" + Operand(handler.TryEnd) + ":" + Operand(handler.HandlerStart) + ":" +
            Operand(handler.HandlerEnd) + ":" + Operand(handler.FilterStart) + ":" + handler.CatchType?.FullName));
}
