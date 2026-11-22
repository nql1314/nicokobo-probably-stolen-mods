using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Nicokobo.NativeTest;

namespace Nicokobo.ContentNativeProbe;

public sealed partial class NativeProbeMod : IMod
{
    private GameObject? _object;
    public void Init(ModManifest manifest) { }
    public void OnEnable()
    {
        if (!Environment.GetCommandLineArgs().Contains("--nicokobo-native-test", StringComparer.Ordinal)) return;
        try
        {
            var options = ProbeOptions.Parse(Environment.GetCommandLineArgs());
            _object = new GameObject("NicoKobo explicit native probe " + options.RunId);
            UnityEngine.Object.DontDestroyOnLoad(_object);
            _object.AddComponent<ProbeRunner>().Begin(options);
        }
        catch (Exception error)
        {
            ProbeResult.WriteArgumentFailure(Environment.GetCommandLineArgs(), error);
            Debug.LogError("[ContentNativeProbe] Argument failure: " + error);
            Application.Quit(21);
        }
    }
    public void OnDisable()
    {
        if (_object != null) UnityEngine.Object.Destroy(_object);
        _object = null;
    }
}

internal sealed class ProbeOptions
{
    internal string RunId = "", Output = "", Mode = "", Persistent = "", Company = "", Product = "", Baseline = "";
    internal int Slot, ProcessId, TimeoutSeconds = 240;
    internal bool DuplicateSlot, UseCopy, DeveloperMode, AllowPrimaryDisplay;
    internal bool Reload => Mode.EndsWith("-reload", StringComparison.Ordinal);
    internal string Family => Mode.Split('-')[0];
    internal bool FullStory => Family is "storylegal" or "storysettled" or "storylost" or "storybeyond";
    internal bool LongRun => FullStory || Family is "achievements" or "achievementsgaps" or "reputation" or "breeding" or "augprotection" or "chemicalreactions" or "nightdevices" or "businessdevices" or "extendedproduction";
    internal bool AugPersistence => FullStory || Family is "aug" or "achievements" or "achievementsgaps";
    internal bool TheftRun => Family is "theft" or "theftui";
    internal static ProbeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string arg in args.Where(a => a.StartsWith("--native-test-", StringComparison.Ordinal)))
        {
            int split = arg.IndexOf('=');
            if (split < 0 || !values.TryAdd(arg.Substring(0, split), arg.Substring(split + 1)))
                throw new ArgumentException("Malformed or duplicate native test option: " + arg);
        }
        var allowed = new HashSet<string>(new[] { "run", "output", "mode", "persistent", "company", "product", "slot", "timeout", "baseline", "duplicate-slot", "use-copy", "developer-mode", "allow-primary-display" }.Select(k => "--native-test-" + k), StringComparer.Ordinal);
        if (values.Keys.Any(k => !allowed.Contains(k))) throw new ArgumentException("Unknown native test option");
        string Need(string key) => values.TryGetValue("--native-test-" + key, out string value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Missing --native-test-" + key);
        var o = new ProbeOptions { RunId = Need("run"), Output = Need("output"), Mode = Need("mode"),
            Persistent = Need("persistent"), Company = Need("company"), Product = Need("product") };
        if (!Guid.TryParseExact(o.RunId, "D", out _)) throw new ArgumentException("Run ID must be a canonical GUID");
        if (!new[] { "catalog", "production-save", "production-reload", "aug-save", "aug-reload", "theft-save", "theft-reload", "review-save", "review-reload", "manager-save", "manager-reload", "manageren-save", "manageren-reload", "night-save", "night-reload", "furnace-save", "furnace-reload", "grayline-save", "grayline-reload", "dossier-save", "dossier-reload", "dossieraug-save", "dossieraug-reload", "dossiertheft-save", "dossiertheft-reload", "storylegal-save", "storylegal-reload", "storysettled-save", "storysettled-reload", "storylost-save", "storylost-reload", "storybeyond-save", "storybeyond-reload", "theftui-save", "theftui-reload", "logisticsfull-save", "logisticsfull-reload", "achievements-save", "achievements-reload", "ratfixture-save", "ratfixture-reload", "achievementsgaps-save", "achievementsgaps-reload", "performance-save", "reputation-save", "reputation-reload", "breeding-save", "breeding-reload", "augprotection-save", "augprotection-reload", "logisticsboundaries-save", "logisticsboundaries-reload", "deviceflows-save", "deviceflows-reload", "containerproduction-save", "containerproduction-reload", "bottleproduction-save", "bottleproduction-reload", "extendedproduction-save", "extendedproduction-reload", "vanillachemistry-save", "vanillachemistry-reload", "chemicalreactions-save", "chemicalreactions-reload", "nightdevices-save", "nightdevices-reload", "businessdevices-save", "businessdevices-reload", "vanillachemistrycapacity-save", "nativedna-save", "nativedna-reload" }.Contains(o.Mode))
            throw new ArgumentException("Unknown mode " + o.Mode);
        if (!Regex.IsMatch(o.Product, "^NicoT_[0-9a-f]{9}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Unique test clone product must match NicoT_<9 lowercase hex>");
        if (!int.TryParse(Need("slot"), NumberStyles.None, CultureInfo.InvariantCulture, out o.Slot) || o.Slot < 0)
            throw new ArgumentException("Slot must be a nonnegative integer");
        foreach (string p in new[] { o.Output, o.Persistent }) if (!Path.IsPathRooted(p)) throw new ArgumentException("Absolute paths required");
        o.Output = Path.GetFullPath(o.Output); o.Persistent = NormalizePath(o.Persistent);
        if (!o.Output.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || File.Exists(o.Output) || !System.IO.Directory.Exists(Path.GetDirectoryName(o.Output)))
            throw new ArgumentException("Output must be a new .json in an existing run directory");
        if (!string.Equals(new DirectoryInfo(o.Persistent).Name, o.Product, StringComparison.Ordinal))
            throw new ArgumentException("Persistent leaf must be the unique clone product");
        int maximumTimeout = o.LongRun ? 10800 : 1800;
        if (values.TryGetValue("--native-test-timeout", out string timeout) && (!int.TryParse(timeout, out o.TimeoutSeconds) || o.TimeoutSeconds < 30 || o.TimeoutSeconds > maximumTimeout))
            throw new ArgumentException("Timeout must be 30.." + maximumTimeout + " seconds");
        o.ProcessId = NativeProbeLaunch.ReadProcessId(o.Output, o.RunId, o.Mode, o.Product);
        if (o.Reload)
        {
            o.Baseline = Path.GetFullPath(Need("baseline"));
            if (!File.Exists(o.Baseline) || o.Baseline.Equals(o.Output, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Missing distinct save baseline");
            var baseline = JObject.Parse(File.ReadAllText(o.Baseline));
            bool checkpoint = o.Family == "breeding" && ProbeContinuations.BreedingCheckpoint(baseline);
            bool sameFamily = (string?)baseline["mode"] == o.Family + "-save" || (o.Family == "breeding" && (string?)baseline["mode"] == "breeding-reload");
            if (((bool?)baseline["passed"] != true && !checkpoint) || (bool?)baseline["executed"] != true ||
                !sameFamily || (string?)baseline["runId"] == o.RunId ||
                (int?)baseline["processId"] == o.ProcessId ||
                !string.Equals((string?)baseline["persistence"]?["persistentDataPath"], o.Persistent, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Reload requires a passed same-family baseline, or a strictly bound incomplete breeding checkpoint, from another isolated process");
        }
        else if (values.ContainsKey("--native-test-baseline")) throw new ArgumentException("Baseline is only valid for reload modes");
        foreach (string key in new[] { "--native-test-duplicate-slot", "--native-test-use-copy", "--native-test-developer-mode", "--native-test-allow-primary-display" })
            if (values.TryGetValue(key, out string flag) && flag is not ("true" or "false")) throw new ArgumentException("Boolean option must be true or false: " + key);
        o.DuplicateSlot = values.TryGetValue("--native-test-duplicate-slot", out string duplicate) && duplicate == "true";
        o.UseCopy = values.TryGetValue("--native-test-use-copy", out string copy) && copy == "true";
        o.DeveloperMode = values.TryGetValue("--native-test-developer-mode", out string developer) && developer == "true";
        o.AllowPrimaryDisplay = values.TryGetValue("--native-test-allow-primary-display", out string primary) && primary == "true";
        if ((o.DuplicateSlot && (o.Reload || !o.AugPersistence || o.Family == "achievementsgaps")) || (o.UseCopy && (!o.Reload || !o.AugPersistence || o.Family == "achievementsgaps")))
            throw new ArgumentException("Native duplication requires an Aug, full-story or achievements save/reload family");
        if (o.Reload)
        {
            var source = JObject.Parse(File.ReadAllText(o.Baseline));
            if ((int?)(o.UseCopy ? source["persistence"]?["duplicateSlotId"] : source["persistence"]?["slotId"]) != o.Slot)
                throw new ArgumentException("Reload slot does not match the passed native save baseline");
            if ((string?)source["environment"]?["trustExpectation"] != (o.DeveloperMode ? "developer" : "ordinary"))
                throw new ArgumentException("Reload trust expectation must match the save baseline");
        }
        return o;
    }
    internal static string NormalizePath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

internal sealed class ProbeCheck
{
    public string name = "", status = "", evidence = "";
    public object? expected, observed;
    public bool passed;
    public bool required = true;
    public string? error;
}
internal sealed class ProbeResult
{
    public int schema = 1;
    public string runId = "", mode = "", processToken = Guid.NewGuid().ToString("D"), startedUtc = DateTime.UtcNow.ToString("O"), completedUtc = "", phase = "";
    public int processId;
    public bool passed, executed, executedChecksPassed, coverageComplete;
    public bool cleanupVerified = false;
    public string cleanupStatus = "external-supervisor-required";
    public string? error;
    public List<ProbeCheck> checks = new();
    public List<string> untested = new();
    public List<string> notTested => untested;
    public Dictionary<string, object?> environment = new(), candidates = new(), persistence = new();
    internal void Check(string name, object? expected, object? observed, bool passed, string evidence = "", string? error = null)
        => checks.Add(new() { name = name, expected = EvidenceJson.Snapshot(expected), observed = EvidenceJson.Snapshot(observed), passed = passed,
            status = passed ? "passed" : "failed", evidence = evidence, error = error });
    internal void Skip(string name, string reason, bool required = true)
    {
        untested.Add(name + ": " + reason);
        checks.Add(new() { name = name, expected = "Executed real production behavior", observed = null, passed = false, status = "not-tested", error = reason, required = required });
    }
    internal void Write(string path)
    {
        completedUtc = DateTime.UtcNow.ToString("O");
        executedChecksPassed = executed && checks.Count > 0 && checks.Any(c => c.status == "passed") && checks.All(c => c.status != "failed") && error == null;
        coverageComplete = untested.Count == 0 && checks.All(c => c.status != "not-tested");
        passed = executedChecksPassed && checks.Where(c => c.required).All(c => c.status == "passed");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        using var json = new JsonTextWriter(writer);
        EvidenceJson.CreateSerializer().Serialize(json, this);
    }
    internal static void WriteArgumentFailure(string[] args, Exception error)
    {
        try
        {
            string? path = args.SingleOrDefault(a => a.StartsWith("--native-test-output=", StringComparison.Ordinal))?.Substring(21);
            string? id = args.SingleOrDefault(a => a.StartsWith("--native-test-run=", StringComparison.Ordinal))?.Substring(18);
            if (path == null || !Path.IsPathRooted(path) || File.Exists(path) || !System.IO.Directory.Exists(Path.GetDirectoryName(path))) return;
            new ProbeResult { runId = id ?? "invalid", mode = "argument-failure", error = error.ToString(), phase = "arguments" }.Write(path);
        }
        catch (Exception writeError) { Debug.LogError("[ContentNativeProbe] Could not write argument failure: " + writeError); }
    }
}

internal static class Reflect
{
    internal const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    internal static Type Type(string fullName, string? assemblyName = null)
    {
        assemblyName ??= fullName.StartsWith("Nicokobo.ContentInterop.", StringComparison.Ordinal) ? "Nicokobo.Forge" :
            fullName.StartsWith("Nicokobo.FoodProcessing.", StringComparison.Ordinal) ? "Nicokobo.FoodProcessing" :
            fullName.StartsWith("MechcoreProtocol.SynthesisExpansion.", StringComparison.Ordinal) ? "MechcoreProtocol.SynthesisExpansion" :
            fullName.StartsWith("MechcoreProtocol.ModuleMatrix.", StringComparison.Ordinal) ? "MechcoreProtocol.ModuleMatrix" :
            fullName.StartsWith("Nicokobo.AutomatedLogistics.", StringComparison.Ordinal) ? "Nicokobo.AutomatedLogistics" :
            fullName.StartsWith("AugMechanicalAscension.", StringComparison.Ordinal) ? "AugMechanicalAscension" :
            fullName.StartsWith("DefinitelyStolen.", StringComparison.Ordinal) ? "DefinitelyStolen" : null;
        var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => assemblyName == null || a.GetName().Name == assemblyName)
            .Select(a => a.GetType(fullName, false)).Where(t => t != null).ToArray();
        if (matches.Length != 1) throw new TypeLoadException(fullName + " in " + (assemblyName ?? "unique loaded assembly") + ": expected one already-loaded type, observed " + matches.Length);
        return matches[0]!;
    }
    internal static object? Get(object target, string name)
    {
        Type type = target as Type ?? target.GetType(); object? instance = target is Type ? null : target;
        var property = type.GetProperty(name, All);
        if (property != null) return property.GetValue(instance);
        var field = type.GetField(name, All);
        return field != null ? field.GetValue(instance) : throw new MissingMemberException(type.FullName, name);
    }
    internal static object? Call(object target, string name, params object?[] arguments)
    {
        Type type = target as Type ?? target.GetType();
        var candidates = type.GetMethods(All).Where(m => m.Name == name && !m.IsGenericMethod && m.GetParameters().Length == arguments.Length)
            .Where(m => m.GetParameters().Select((p, i) => arguments[i] == null || (p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType).IsInstanceOfType(arguments[i])).All(x => x)).ToArray();
        if (candidates.Length != 1) throw new MissingMethodException(type.FullName, name + " (matching overloads=" + candidates.Length + ")");
        return Invoke(candidates[0], target is Type ? null : target, arguments);
    }
    internal static object? Invoke(MethodInfo method, object? target, object?[] arguments)
    {
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    internal static IEnumerable<object> Each(object? source) => source is IEnumerable values ? values.Cast<object>() : Enumerable.Empty<object>();
    internal static int Int(object target, string name) => Convert.ToInt32(Get(target, name), CultureInfo.InvariantCulture);
    internal static string Str(object target, string name) => Convert.ToString(Get(target, name), CultureInfo.InvariantCulture) ?? "";
    internal static JToken Json(object? value) => value == null ? JValue.CreateNull() : JToken.FromObject(value);
    internal static string HashFile(string path)
    {
        using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}

public sealed partial class ProbeRunner : MonoBehaviour
{
    internal static readonly string[] ContentMods = { "nicokobo.food_processing", "nicokobo.mechcore_protocol",
        "nicokobo.automated_logistics", "nicokobo.definitely_stolen" };
    internal static readonly string[] RequiredMods = ContentMods.Concat(new[] { "nicokobo.forge", "nicokobo.item-manager",
        "nicokobo.speed-up", "nicokobo.stock-reroll" }).ToArray();
    private ProbeOptions? _options;
    private ProbeResult? _result;
    private IEnumerator? _work;
    private float _started;
    private bool _finished;
    private float _nextProgress;
    private bool _progressCreated;
    private readonly HashSet<string> _assets = new(StringComparer.Ordinal);
    internal void Begin(ProbeOptions options)
    {
        _options = options; _result = new() { runId = options.RunId, mode = options.Mode, processId = options.ProcessId, phase = "isolation" };
        _result.environment["processIdentitySource"] = "External supervisor launch.json; unique native processToken is generated by this probe instance";
        _started = Time.realtimeSinceStartup;
        ModHook.OnModAssetsLoaded += AssetsLoaded;
        _work = Execute();
    }
    private void AssetsLoaded(LoadedMod mod) => _assets.Add(mod.Manifest.ID);
    private void OnDestroy() => ModHook.OnModAssetsLoaded -= AssetsLoaded;
    private void Update()
    {
        if (_finished || _work == null || _options == null || _result == null) return;
        try
        {
            if (Time.realtimeSinceStartup - _started > _options.TimeoutSeconds)
                throw new TimeoutException("Internal timeout; phase=" + _result.phase + "; scene=" + SceneManager.GetActiveScene().name + "; assets=" + string.Join(",", _assets));
            VerifyIsolation(false);
            WriteProgress();
            if (!_work.MoveNext()) Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    private void VerifyIsolation(bool record)
    {
        NativeProbeLaunch.AssertWindow();
        var o = _options!; var r = _result!;
        string actual = ProbeOptions.NormalizePath(Application.persistentDataPath);
        if (!actual.Equals(o.Persistent, StringComparison.OrdinalIgnoreCase) || Application.companyName != o.Company || Application.productName != o.Product)
            throw new InvalidOperationException("Isolation mismatch. actual=" + actual + "; company=" + Application.companyName + "; product=" + Application.productName);
        if (SteamManager.Initialized) throw new InvalidOperationException("SteamManager.Initialized must remain false in isolated clone");
        if (!record) return;
        r.environment["persistentDataPath"] = actual; r.environment["company"] = Application.companyName; r.environment["product"] = Application.productName;
        r.environment["applicationVersion"] = Application.version; r.environment["unityVersion"] = Application.unityVersion;
        r.environment["dataPath"] = Application.dataPath; r.environment["steamInitialized"] = SteamManager.Initialized;
        r.environment["assemblyCSharp"] = new { path = typeof(PlayerStore).Assembly.Location, sha256 = Reflect.HashFile(typeof(PlayerStore).Assembly.Location), version = typeof(PlayerStore).Assembly.GetName().Version?.ToString() };
        r.Check("isolation.actual-runtime-path-and-steam", new { persistent = o.Persistent, company = o.Company, product = o.Product, steamInitialized = false },
            new { persistent = actual, company = Application.companyName, product = Application.productName, steamInitialized = SteamManager.Initialized }, true);
    }
    private IEnumerator Execute()
    {
        VerifyIsolation(true);
        const string markerKey = "NicoKoboNativeProbeRunId";
        PlayerPrefs.SetString(markerKey, _options!.RunId);
        PlayerPrefs.Save();
        string marker = PlayerPrefs.GetString(markerKey, "");
        _result!.environment["playerPrefsMarker"] = new { key = markerKey, value = marker,
            expectedRegistryIdentity = "HKEY_CURRENT_USER\\Software\\" + Application.companyName + "\\" + Application.productName };
        _result.Check("isolation.playerprefs-roundtrip", _options.RunId, marker, marker == _options.RunId,
            "Only after actual persistent/company/product/Steam guard. Supervisor must independently read the unique registry key and compare formal key bytes.");
        Application.runInBackground = true;
        var o = _options!; var r = _result!;
        r.environment["trustExpectation"] = o.DeveloperMode ? "developer" : "ordinary";
        r.phase = o.DeveloperMode ? "developer-loader-assets" : "ordinary-loader-assets";
        while (NewGameData.Instance == null || ModLoader.Instance == null || RequiredMods.Any(id =>
            !ModLoader.Mods.Any(m => m.Manifest.ID == id && m.IsEnabled && m.Instance != null && string.IsNullOrEmpty(m.LoadError)))) yield return null;
        while (ContentMods.Any(id => !_assets.Contains(id) && ModLoader.Mods.First(m => m.Manifest.ID == id).Sprites.Count == 0)) yield return null;
        foreach (object wait in NativeProbeLaunch.PrepareWindow(evidence => r.environment["testWindow"] = evidence, o.AllowPrimaryDisplay)) yield return wait;
        r.environment["loadedModTrust"] = ModLoader.Mods.Select(mod => new { id = mod.Manifest.ID, version = mod.Manifest.ModVersion,
            mod.IsEnabled, trust = mod.TrustState.ToString(), developerMode = mod.TrustRecord?.developerMode,
            mod.LoadError, assembly = mod.Assembly?.FullName, mod.DllPath }).ToArray();
        foreach (var mod in ModLoader.Mods.Where(m => o.DeveloperMode || RequiredMods.Contains(m.Manifest.ID)))
        {
            if (o.DeveloperMode)
                r.Check("loader.developer-trust." + mod.Manifest.ID, "Developer mode enabled by user; unchanged approved files may remain Match, changed files use DeveloperMode", new { mod.IsEnabled, trust = mod.TrustState.ToString(), developerMode = mod.TrustRecord?.developerMode },
                    mod.IsEnabled && mod.TrustRecord?.developerMode == true && (mod.TrustState == ModTrustState.Match || mod.TrustState == ModTrustState.DeveloperMode));
            else
                r.Check("loader.ordinary-trust." + mod.Manifest.ID, "Enabled through ordinary approval, Developer mode off", new { mod.IsEnabled, trust = mod.TrustState.ToString(), developerMode = mod.TrustRecord?.developerMode },
                    mod.IsEnabled && mod.TrustState != ModTrustState.DeveloperMode && mod.TrustRecord?.developerMode != true);
        }
        foreach (var mod in ModLoader.Mods.Where(m => RequiredMods.Contains(m.Manifest.ID)))
        {
            r.candidates[mod.Manifest.ID] = new { version = mod.Manifest.ModVersion, assembly = mod.Assembly.FullName, dllPath = mod.DllPath,
                sha256 = Reflect.HashFile(mod.DllPath), enabled = mod.IsEnabled, source = mod.Source.ToString(), trust = mod.TrustState.ToString(),
                developerMode = mod.TrustRecord == null ? null : Reflect.Json(mod.TrustRecord), scan = Reflect.Json(mod.LastScan), spriteCount = mod.Sprites.Count, localizationCount = mod.Localization.Count };
        }
        foreach (object? wait in PrepareManagerLocale(o, r)) yield return wait;
        var context = new ProbeContext(o, r);
        var interopAssembly = Reflect.Type("Nicokobo.ContentInterop.MachineCatalog").Assembly;
        r.environment["loadedContentInterop"] = new { path = interopAssembly.Location, version = interopAssembly.GetName().Version?.ToString(), sha256 = Reflect.HashFile(interopAssembly.Location) };
        r.phase = "normal-scene-start";
        context.PrepareStart();
        SceneManager.LoadScene("InventoryScene", LoadSceneMode.Single);
        yield return null;
        while (!context.Ready(out string missing)) { r.environment["waitingFor"] = missing; yield return null; }
        r.Check("normal-game.native-initial-or-load", o.Reload ? "normal InventoryScene -> PlayerStore.LoadGame" : "normal InventoryScene -> StartNewGame -> InitialSave",
            new { PlayerStore.Instance.saveSlotId, PlayerStore.Instance.runID, PlayerStore.Instance.startScenarioId }, true);
        r.executed = true;
        foreach (object? wait in context.Catalog()) yield return wait;
        if (o.Reload) foreach (object? wait in context.Reload()) yield return wait;
        else
        {
            if (o.Family == "production") foreach (object? wait in context.Production()) yield return wait;
            if (o.Family == "logisticsfull") foreach (object? wait in context.FullLogistics()) yield return wait;
            if (o.Family == "achievements") foreach (object? wait in context.ForgeAchievements()) yield return wait;
            if (o.Family == "ratfixture") foreach (object? wait in context.RatFixtureAchievement()) yield return wait;
            if (o.Family == "achievementsgaps") foreach (object? wait in context.NativeAchievementGaps()) yield return wait;
            if (o.Family == "performance") foreach (object? wait in context.NativePerformance()) yield return wait;
            if (o.Family == "reputation") foreach (object? wait in context.NativeReputation()) yield return wait;
            if (o.Family == "breeding") foreach (object? wait in context.NativeBreeding()) yield return wait;
            if (o.Family == "augprotection") foreach (object? wait in context.AugProtection()) yield return wait;
            if (o.Family == "logisticsboundaries") foreach (object? wait in context.LogisticsBoundaries()) yield return wait;
            if (o.Family == "deviceflows") foreach (object? wait in context.NativeDeviceFlows()) yield return wait;
            if (o.Family == "containerproduction") foreach (object? wait in context.NativeContainerProduction()) yield return wait;
            if (o.Family == "bottleproduction") foreach (object? wait in context.NativeContainerProduction(false)) yield return wait;
            if (o.Family == "extendedproduction") foreach (object? wait in context.NativeExtendedProduction()) yield return wait;
            if (o.Family == "vanillachemistry") foreach (object? wait in context.VanillaChemistry()) yield return wait;
            if (o.Family == "vanillachemistrycapacity") foreach (object? wait in context.VanillaChemistryCapacity()) yield return wait;
            if (o.Family == "chemicalreactions") foreach (object? wait in context.NativeChemicalReactions()) yield return wait;
            if (o.Family == "nightdevices") foreach (object? wait in context.NativeNightDevices()) yield return wait;
            if (o.Family == "businessdevices") foreach (object? wait in context.NativeBusinessDevices()) yield return wait;
            if (o.Family == "nativedna") foreach (object? wait in context.NativeDna()) yield return wait;
            if (o.Family == "aug") foreach (object? wait in context.Aug()) yield return wait;
            if (o.Family == "theft") foreach (object? wait in context.Theft()) yield return wait;
            if (o.Family == "theftui") foreach (object? wait in context.ReviewTheftLootSelection()) yield return wait;
            if (o.Family is "manager" or "manageren") foreach (object? wait in context.ItemManager()) yield return wait;
            if (o.Family == "night") foreach (object? wait in context.Night()) yield return wait;
            if (o.Family == "furnace") foreach (object? wait in context.FurnaceNight()) yield return wait;
            if (o.Family == "grayline") foreach (object? wait in context.Grayline()) yield return wait;
            if (o.FullStory)
            {
                string ending = o.Family switch { "storylegal" => "legal_property", "storysettled" => "settled_account", "storylost" => "lost_and_found", _ => "beyond_the_world" };
                foreach (object? wait in context.FullStory(ending)) yield return wait;
            }
            if (o.Family is "dossier" or "dossieraug" or "dossiertheft") foreach (object? wait in context.Dossier()) yield return wait;
            if (o.Family == "review")
            {
                foreach (object? wait in context.ReviewProductionRegressions()) yield return wait;
                foreach (object? wait in context.ReviewLogisticsRegressions()) yield return wait;
            }
            if (o.Mode != "catalog" && o.Family != "breeding") context.Save();
        }
        foreach (object? wait in context.CaptureEvidenceFrame(o.Mode)) yield return wait;
        r.untested.AddRange(new[] { "Steam account achievements / Workshop / cloud: clone Steam intentionally disabled", "Physical mouse gestures and visual UI layout: programmatic native operations only", "External preservation and candidate installation cleanup: execution supervisor must verify separately" });
        r.untested.Add(o.Family switch
        {
            "furnace" => "One real furnace night plus direct-cycle furnace regression; physical mouse gestures and other production chains are outside this run",
            "night" => "This scenario covers three actual nights and the recorded cheese production chain; further idle-night variants, water-debit paths and natural multiday story routes require their separate evidence",
            "grayline" => "E01/E02 live fixtures, one interrupted purchase, saving with Mod busy flags, two actual nights and the next Tuesday supply are covered; later story events, endings and physical mouse gestures remain separate",
            "storylegal" or "storysettled" or "storylost" or "storybeyond" => "This independent slot covers one ending and its recorded branches; other ending slots, full upstream economic acquisition and physical mouse gestures remain separate",
            _ => "Full PlayerStore.EndNight and natural multiday story routes: not implied by callback checks"
        });
        r.phase = "completed";
    }
    private void WriteProgress(bool force = false)
    {
        if (_options == null || _result == null || (!force && Time.realtimeSinceStartup < _nextProgress)) return;
        _nextProgress = Time.realtimeSinceStartup + 5f;
        try
        {
            var data = new { runId = _options.RunId, mode = _options.Mode, processId = _result.processId,
                phase = _result.phase, _result.executed, checks = _result.checks.Count,
                failed = _result.checks.Count(c => c.status == "failed"),
                failedChecks = _result.checks.Where(c => c.status == "failed").Select(c => c.name).ToArray(),
                day = _result.executed && PlayerStore.HasInstance ? (int?)StoreStation.GetDayCounter() : null,
                utc = DateTime.UtcNow.ToString("O"), elapsedSeconds = Time.realtimeSinceStartup - _started,
                originalError = _result.error };
            using var stream = new FileStream(_options.Output + ".progress.json", _progressCreated ? FileMode.Truncate : FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(JsonConvert.SerializeObject(data, Formatting.Indented));
            _progressCreated = true;
        }
        catch (Exception progressError) { _result.environment["progressWriteError"] = progressError.ToString(); }
    }
    private void Finish(Exception? error)
    {
        if (_finished) return; _finished = true;
        var r = _result!; r.error = error?.ToString();
        if (error != null) r.Check("runner.top-level", "No exception", error.GetType().Name, false, r.phase, error.ToString());
        if (error != null) Debug.LogError("[ContentNativeProbe] phase=" + r.phase + "; original failure=" + error);
        WriteProgress(true);
        try { r.Write(_options!.Output); Debug.Log("[ContentNativeProbe] Result=" + _options.Output + "; passed=" + r.passed); }
        catch (Exception writeError)
        {
            Debug.LogError("[ContentNativeProbe] RESULT WRITE FAILED; original failure=" + r.error + "; serialization failure=" + writeError);
            try
            {
                using var stream = new FileStream(_options!.Output + ".write-failure.json", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(JsonConvert.SerializeObject(new { r.runId, r.mode, r.processId, r.phase,
                    passed = false, coverageComplete = false, resultValid = false, r.executed,
                    originalError = r.error, serializationError = writeError.ToString(),
                    checks = r.checks.Select(c => new { c.name, c.status, c.passed, c.required, c.error }) }, Formatting.Indented));
            }
            catch (Exception diagnosticError) { Debug.LogError("[ContentNativeProbe] Diagnostic write also failed: " + diagnosticError); }
            Application.Quit(22); return;
        }
        Application.Quit(r.passed ? 0 : 23);
    }
}
